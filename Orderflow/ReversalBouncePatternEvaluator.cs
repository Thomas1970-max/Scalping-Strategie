using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Windows.Documents;
using System.Windows.Media.Media3D;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using MyNamespace.Strategies.Orderflow;
using Utils.Common.Logging;
using static MyNamespace.Strategies.Goldfluss3_3;

namespace MyNamespace.Strategies.Orderflow
{
    /// <summary>
    /// Implementierung des IPatternEvaluator für ReversalBounce-Muster (Long und Short).
    /// </summary>
    public class ReversalBouncePatternEvaluator : IPatternEvaluator
    {
        private readonly OrderDirections _direction;
        private readonly OrderflowPatternType _patternType;
        private readonly ILoggerSource _loggerSource;
        private OrderflowThresholds? _externalThresholds;
        private Func<OrderflowThresholds?>? _thresholdsProvider;

        public OrderflowPatternType Type => _patternType;
        public OrderDirections Direction => _direction; // Ob der Evaluator für Long oder Short ist
        public static OrderflowThresholds? GlobalThresholds { get; set; }

        public OrderflowThresholds? ExternalThresholds
        {
            get => _externalThresholds;
            set => _externalThresholds = value;
        }

        public Func<OrderflowThresholds?>? ThresholdsProvider
        {
            get => _thresholdsProvider;
            set => _thresholdsProvider = value;
        }

        public ReversalBouncePatternEvaluator(OrderDirections direction, ILoggerSource loggerSource = null)
        {
            _direction = direction;
            _patternType = direction == OrderDirections.Buy
                ? OrderflowPatternType.PotentialLongReversalBounce
                : OrderflowPatternType.PotentialShortReversalBounce;

            _loggerSource = loggerSource;
        }

        // Helper: konsistente Formatierung für MetValue
        private static string FormatMetValue(object value, string numericFormat = "F2")
        {
            if (value == null) return "n/a";
            if (value is decimal d) return d.ToString(numericFormat, System.Globalization.CultureInfo.InvariantCulture);
            if (value is double db) return Convert.ToDecimal(db).ToString(numericFormat, System.Globalization.CultureInfo.InvariantCulture);
            if (value is float f) return Convert.ToDecimal(f).ToString(numericFormat, System.Globalization.CultureInfo.InvariantCulture);
            if (value is long || value is int || value is short || value is byte)
                return Convert.ToInt64(value).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return value.ToString();
        }

        public PatternEvaluationResult Evaluate(
            OvSnapshot currentSnapshot,
            OfFeatures features,
            OfFeaturesHistory history,
            OrderflowThresholds thresholds,
            SetupConditionConfig config,
            MarketRegime currentVolatilityRegime,
            MarketDirectionalBias currentDirectionalBias,
            MarketState currentMarketState,
            MarketStructureContext currentMarketStructureContext)
            {
            LoggerHelper.LogDebug(_loggerSource, $"ReversalBouncePatternEvaluator.Evaluate: start direction={_direction}, bar={features?.Bar}");

            // NEU: 1. Effektive Thresholds bestimmen
            var effectiveThresholds = GlobalThresholds ?? _thresholdsProvider?.Invoke() ?? _externalThresholds ?? thresholds;

            if (GlobalThresholds != null)
            {
                LoggerHelper.LogInfo(_loggerSource, $"Using global thresholds: ReversalThCvdImpulseLong={FormatMetValue(GlobalThresholds?.ReversalThCvdImpulseLong)}");
            }
            else if (_thresholdsProvider != null)
            {
                var dynamicThresholds = _thresholdsProvider.Invoke();
                LoggerHelper.LogInfo(_loggerSource, $"Using dynamic thresholds: ReversalThCvdImpulseLong={FormatMetValue(dynamicThresholds?.ReversalThCvdImpulseLong)}");
            }
            else if (_externalThresholds != null)
            {
                LoggerHelper.LogInfo(_loggerSource, $"Using external thresholds: ReversalThCvdImpulseLong={FormatMetValue(_externalThresholds?.ReversalThCvdImpulseLong)}");
            }

            // NEU: 2. Range-Bar Pattern-Erkennung (universell)
            var rangeAnalysis = RangeBarAnalyzer.AnalyzeRangeBarPattern(
                history, features.Bar, currentSnapshot, effectiveThresholds, _loggerSource);
            
            LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 📊 RANGE-ANALYSE: HasValidReversalSetup={rangeAnalysis.HasValidReversalSetup}, HasValidLong={rangeAnalysis.HasValidLongReversalSetup}, HasValidShort={rangeAnalysis.HasValidShortReversalSetup}");
            LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 📊 PATTERN-DETAILS: BarTypes=[{string.Join(", ", rangeAnalysis.BarTypes)}], LastReversalType={rangeAnalysis.LastReversalType}, BarsSinceReversal={rangeAnalysis.BarsSinceLastReversal}");

            // NEU: 2. Pattern-Validierung
            if (!rangeAnalysis.HasValidReversalSetup)
            {
                var patternReasons = new List<string> { 
                    $"BLOCKED (Pattern): Kein gültiges Reversal-Setup gefunden. Pattern: [{string.Join(", ", rangeAnalysis.BarTypes)}]" 
                };
                LoggerHelper.LogWarn(_loggerSource, $"[ReversalBouncePatternEvaluator] 🚫 PATTERN BLOCK: {patternReasons[0]}");
                
                return PatternEvaluationResult.NotDetected(
                    Type,
                    string.Join(" | ", patternReasons),
                    null,
                    new List<EvaluatedConditionDetail>(),
                    new List<EvaluatedConditionDetail>(),
                    new List<EvaluatedConditionDetail>()
                );
            }

            // NEU: 3. Entry-Fenster Validierung
            int maxBarsAfterReversal = thresholds.RangeBarMaxBarsAfterReversal;
            if (rangeAnalysis.BarsSinceLastValidReversal > maxBarsAfterReversal)
            {
                var entryReasons = new List<string> { 
                    $"BLOCKED (Entry Window): Entry-Fenster abgelaufen. Bars seit Reversal={rangeAnalysis.BarsSinceLastValidReversal} > Max={maxBarsAfterReversal}" 
                };
                LoggerHelper.LogWarn(_loggerSource, $"[ReversalBouncePatternEvaluator] 🚫 ENTRY-FENSTER BLOCK: {entryReasons[0]}");
                
                return PatternEvaluationResult.NotDetected(
                    Type,
                    string.Join(" | ", entryReasons),
                    null,
                    new List<EvaluatedConditionDetail>(),
                    new List<EvaluatedConditionDetail>(),
                    new List<EvaluatedConditionDetail>()
                );
            }

            // NEU: 4. Direction aus Pattern bestimmen
            OrderDirections detectedDirection = OrderDirections.Buy; // Default
            
            if (rangeAnalysis.HasValidLongReversalSetup)
            {
                detectedDirection = OrderDirections.Buy;
                LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ✅ PATTERN-ERKENNUNG: LONG setup detected - {rangeAnalysis.LastReversalType}");
            }
            else if (rangeAnalysis.HasValidShortReversalSetup)
            {
                detectedDirection = OrderDirections.Sell;
                LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ✅ PATTERN-ERKENNUNG: SHORT setup detected - {rangeAnalysis.LastReversalType}");
            }

            // NEU: 5. Prüfen ob Pattern zur Evaluator-Richtung passt
            if (detectedDirection != _direction)
            {
                var directionReasons = new List<string> { 
                    $"BLOCKED (Direction Mismatch): Pattern-Direction={detectedDirection}, Evaluator-Direction={_direction}" 
                };
                LoggerHelper.LogWarn(_loggerSource, $"[ReversalBouncePatternEvaluator] 🚫 DIRECTION MISMATCH: {directionReasons[0]}");
                
                return PatternEvaluationResult.NotDetected(
                    Type,
                    string.Join(" | ", directionReasons),
                    null,
                    new List<EvaluatedConditionDetail>(),
                    new List<EvaluatedConditionDetail>(),
                    new List<EvaluatedConditionDetail>()
                );
            }

            // BESTEHEND: 6. Orderflow-Kriterien (nur im Entry-Fenster und bei passender Direction)
            LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ✅ ENTRY-FENSTER AKTIV: Orderflow-Prüfung wird durchgeführt für {detectedDirection}");

            var reasons = new List<string>();
            int metCriteriaCount = 0;

            // Sammler für erzeugte Condition‑Details
            var metHardConditions = new List<EvaluatedConditionDetail>();
            var metRelConditions = new List<EvaluatedConditionDetail>();
            var metDiagnosticConditions = new List<EvaluatedConditionDetail>();

            if (Direction == OrderDirections.Buy)
            {
                // ####################################################################
                // #                        LONG EVALUATION                           #
                // ####################################################################


                LoggerHelper.LogDebug(_loggerSource, "Evaluate: Long path");

                // **NEUES HARTES KRITERIUM: Mindest-CVD-Impuls für Long Reversal**
                // Wenn dieser Schwellenwert gesetzt ist und der aktuelle CvdImpulse zu negativ ist,
                // wird das Muster sofort als NICHT erkannt zurückgegeben.
                if (thresholds.ThCvdImpulseMinForLongReversal.HasValue &&
                    currentSnapshot.CvdImpulse < thresholds.ThCvdImpulseMinForLongReversal.Value)
                {
                    var reasonText = $"BLOCKED (Hard Criterium): CvdImpulse ({currentSnapshot.CvdImpulse:F2}) ist zu negativ für ein Long Reversal (Min. erlaubt: {thresholds.ThCvdImpulseMinForLongReversal.Value:F2}).";
                    reasons.Add(reasonText);
                    LoggerHelper.LogWarn(_loggerSource, $"[PatternSignaturer] HARD BLOCK for Long Reversal Bounce: {reasonText}");

                    var diagDetail = new EvaluatedConditionDetail(
                        "CvdImpulseTooNegativeForLongReversal",
                        FormatMetValue(currentSnapshot.CvdImpulse, "F2"),
                        $"< {FormatMetValue(thresholds.ThCvdImpulseMinForLongReversal.Value, "F2")}"
                    );
                    metDiagnosticConditions.Add(diagDetail);

                    return PatternEvaluationResult.NotDetected(
                        Type,
                        string.Join(" | ", reasons),
                        null,
                        metHardConditions,
                        metRelConditions,
                        metDiagnosticConditions
                    );
                }

                // Strenger Block: wenn Bar bärisch oder CVD klar negativ, disqualifizieren
                bool isBearishBar = currentSnapshot.Close <= currentSnapshot.Open;
                bool cvdNegative = currentSnapshot.CvdImpulse < 0m;
                bool aggNotBullish = !thresholds.ReversalThAggPressureBreakoutBull.HasValue || currentSnapshot.AggPressure <= 0m;

                if (isBearishBar || cvdNegative)
                {
                    reasons.Add($"BLOCKED: Bearish candle (Close {currentSnapshot.Close:F2} <= Open {currentSnapshot.Open:F2}) OR CvdImpulse {currentSnapshot.CvdImpulse:F2} < 0.");
                    LoggerHelper.LogWarn(_loggerSource, $"Blocking Long: bearish candle OR negative CVD. Bar={features.Bar}");
                    metDiagnosticConditions.Add(new EvaluatedConditionDetail(
                    "BarBearishOrCvdNegative",
                    $"{FormatMetValue(currentSnapshot.Close)}/{FormatMetValue(currentSnapshot.Open)} | Cvd={FormatMetValue(currentSnapshot.CvdImpulse)}",
                    "Bearish candle || CvdImpulse < 0 => disqualify Long"
                    ));
                    return PatternEvaluationResult.NotDetected(Type, string.Join(" | ", reasons), null, metHardConditions, metRelConditions, metDiagnosticConditions);
                }

                // Kriterien-Gruppe 1: Aggressive Kaufkraft
                // ----------------------------------------------------               

                // Aggressive CVD vorhanden?
                bool aggressiveCvdPresent = false;
                if (thresholds.ReversalThCvdImpulseLong.HasValue && currentSnapshot.CvdImpulse > thresholds.ReversalThCvdImpulseLong.Value)
                {
                    aggressiveCvdPresent = true;
                    reasons.Add($"CvdImpulse ({currentSnapshot.CvdImpulse:F2}) > ReversalThCvdImpulseLong ({thresholds.ReversalThCvdImpulseLong.Value:F2})");

                    var detail = new EvaluatedConditionDetail(
                        "CvdImpulseAboveReversalThLong",
                        FormatMetValue(currentSnapshot.CvdImpulse, "F2"),
                        $"> {FormatMetValue(thresholds.ReversalThCvdImpulseLong.Value, "F2")}"
                    );
                    metHardConditions.Add(detail);
                    LoggerHelper.LogDebug(_loggerSource, $"Erstellt MetHard Detail: {detail.ToShortString()}");
                }

                // Aggressive Pressure vorhanden?
                bool aggressivePressurePresent = false;
                if (thresholds.ReversalThAggPressureBreakoutBull.HasValue && currentSnapshot.AggPressure > thresholds.ReversalThAggPressureBreakoutBull.Value)
                {
                    aggressivePressurePresent = true;
                    reasons.Add($"AggPressure ({currentSnapshot.AggPressure:F2}) > ReversalThAggPressureBreakoutBull ({thresholds.ReversalThAggPressureBreakoutBull.Value:F2})");

                    var detail = new EvaluatedConditionDetail(
                        "AggPressureAboveReversalThBull",
                        FormatMetValue(currentSnapshot.AggPressure, "F2"),
                        $"> {FormatMetValue(thresholds.ReversalThAggPressureBreakoutBull.Value, "F2")}"
                    );
                    metHardConditions.Add(detail);
                    LoggerHelper.LogDebug(_loggerSource, $"Erstellt MetHard Detail: {detail.ToShortString()}");
                }

                // Defensive rule: wenn keine Long-CVD-Schwelle gesetzt ist (NULL) und aktueller CvdImpulse negativ,
                // dann behandeln wir die Aggressivitäts-Gruppe für Long als NICHT erfüllt (block Long).
                bool blockLongDueToCvdDirectionality = false;
                if (!thresholds.ReversalThCvdImpulseLong.HasValue && currentSnapshot.CvdImpulse < 0)
                {
                    blockLongDueToCvdDirectionality = true;

                    // Diagnostic: füge eine negative/erklärende EvaluatedConditionDetail hinzu, damit Audit-Log klar ist.
                    var diagDetail = new EvaluatedConditionDetail(
                        "CvdImpulseDirectionality",
                        FormatMetValue(currentSnapshot.CvdImpulse, "F2"),
                        "ReversalThCvdImpulseLong=NULL & CvdImpulse<0 => als NICHT erfüllt behandeln"
                    );
                    // Wir fügen diese als "hard" hinzu, damit sie in der Audit-Ausgabe erscheint.
                    metDiagnosticConditions.Add(diagDetail);

                    LoggerHelper.LogInfo(_loggerSource,
                        $"[PatternSignaturer] Blocking Long: ReversalThCvdImpulseLong is NULL and current CvdImpulse={currentSnapshot.CvdImpulse:F2} < 0");
                }

                // Verwende eine effektive Variable, damit die ursprüngliche Berechnung nicht verändert wird,
                // aber wir die Gruppe bei Bedarf blockieren können.
                bool effectiveAggressive = (aggressiveCvdPresent || aggressivePressurePresent) && !blockLongDueToCvdDirectionality;

                if (effectiveAggressive)
                {
                    metCriteriaCount++;
                }
                else
                {
                    // Wenn wir blocken, geben wir spezielleren Reason-Text aus.
                    if (blockLongDueToCvdDirectionality)
                        reasons.Add($"BLOCKED: Keine validierte Long-CVD-Schwelle (ReversalThCvdImpulseLong=NULL) und CvdImpulse negativ ({currentSnapshot.CvdImpulse:F2}). Aggressivitäts-Gruppe gilt als nicht erfüllt.");
                    else
                        reasons.Add("FAIL: Unzureichender CvdImpulse oder AggPressure.");
                }


                // Momentum-Shift-Bestätigung (Inflection)
                bool inflectionBullish = features.InflectionCvd == InflectionType.Bullish || features.InflectionPressure == InflectionType.Bullish;
                if (inflectionBullish)
                {
                    metCriteriaCount++;
                    reasons.Add("Bullish Inflection in CVD oder AggPressure erkannt.");

                    var detail = new EvaluatedConditionDetail(
                        "InflectionBullish",
                        "Bullish",
                        "InflectionCvd or InflectionPressure == Bullish"
                    );
                    metRelConditions.Add(detail);
                    LoggerHelper.LogDebug(_loggerSource, $"Created MetRel detail: {detail.ToShortString()}");
                }
                else
                {
                    reasons.Add("FAIL: Keine Bullish Inflection in CVD oder AggPressure erkannt.");
                }

                // Kriterien-Gruppe 2: Mangel an bärischem Widerstand
                if (thresholds.MaxCounterDeltaShareBear.HasValue)
                {
                    if (currentSnapshot.MaxCounterShareBear < thresholds.MaxCounterDeltaShareBear.Value)
                    {
                        metCriteriaCount++;
                        reasons.Add($"MaxCounterShareBear ({currentSnapshot.MaxCounterShareBear:F2}) < ThMaxCounterDeltaShareBear ({thresholds.MaxCounterDeltaShareBear.Value:F2})");

                        var detail = new EvaluatedConditionDetail(
                            "MaxCounterShareBearLow",
                            FormatMetValue(currentSnapshot.MaxCounterShareBear, "F2"),
                            $"< {FormatMetValue(thresholds.MaxCounterDeltaShareBear.Value, "F2")}"
                        );
                        metHardConditions.Add(detail);
                        LoggerHelper.LogDebug(_loggerSource, $"Created MetHard detail: {detail.ToShortString()}");
                    }
                    else
                    {
                        reasons.Add("FAIL: MaxCounterShareBear ist zu hoch (starker bärischer Widerstand).");
                    }
                }
                else
                {
                    reasons.Add("HINWEIS: ThMaxCounterDeltaShareBear nicht gesetzt, Kriterium nicht geprüft.");
                }

                // Kriterien-Gruppe 3: Volumen- und Effizienz-Bestätigung
                if (thresholds.ThVolBurstZ.HasValue)
                {
                    if (currentSnapshot.VolBurstZ > thresholds.ThVolBurstZ.Value)
                    {
                        metCriteriaCount++;
                        reasons.Add($"VolBurstZ ({currentSnapshot.VolBurstZ:F2}) > ThVolBurstZ ({thresholds.ThVolBurstZ.Value:F2})");

                        var detail = new EvaluatedConditionDetail(
                            "VolBurstZAboveTh",
                            FormatMetValue(currentSnapshot.VolBurstZ, "F2"),
                            $"> {FormatMetValue(thresholds.ThVolBurstZ.Value, "F2")}"
                        );
                        metHardConditions.Add(detail);
                        LoggerHelper.LogDebug(_loggerSource, $"Created MetHard detail: {detail.ToShortString()}");
                    }
                    else
                    {
                        reasons.Add("VolBurstZ ist nicht signifikant.");
                    }
                }
                else
                {
                    reasons.Add("HINWEIS: ThVolBurstZ nicht gesetzt, Kriterium nicht geprüft.");
                }

                if (thresholds.ThEfficiency.HasValue)
                {
                    if (currentSnapshot.Efficiency > thresholds.ThEfficiency.Value && features.SlopeEff > 0)
                    {
                        metCriteriaCount++;
                        reasons.Add($"Efficiency ({currentSnapshot.Efficiency:F2}) > ThEfficiency ({thresholds.ThEfficiency.Value:F2}) und SlopeEff ({features.SlopeEff:F2}) > 0");

                        var detail = new EvaluatedConditionDetail(
                            "EfficiencyAboveTh",
                            FormatMetValue(currentSnapshot.Efficiency, "F2"),
                            $"> {FormatMetValue(thresholds.ThEfficiency.Value, "F2")}, SlopeEff>0"
                        );
                        metHardConditions.Add(detail);
                        LoggerHelper.LogDebug(_loggerSource, $"Created MetHard detail: {detail.ToShortString()}");
                    }
                    else
                    {
                        reasons.Add("Ineffiziente Aufwärtsbewegung oder negative Efficiency-Steigung.");
                    }
                }
                else
                {
                    reasons.Add("HINWEIS: ThEfficiency nicht gesetzt, Kriterium nicht geprüft.");
                }

                // Kriterien-Gruppe 4: Stacked Buying Imbalances
                if ((thresholds.ThStackedImbAnyRangeMinDirectional.HasValue && thresholds.ThStackedImbAnyRangeMinDirectional.Value > 0) ||
                    (thresholds.ThStackedImbAnchoredRangeMinDirectional.HasValue && thresholds.ThStackedImbAnchoredRangeMinDirectional.Value > 0))
                {
                    if ((thresholds.ThStackedImbAnyRangeMinDirectional.HasValue && currentSnapshot.StackedBuyImbCount >= thresholds.ThStackedImbAnyRangeMinDirectional.Value) ||
                        (thresholds.ThStackedImbAnchoredRangeMinDirectional.HasValue && currentSnapshot.StackedBuyImbTopCount >= thresholds.ThStackedImbAnchoredRangeMinDirectional.Value))
                    {
                        metCriteriaCount++;
                        reasons.Add("Signifikante Stacked Buy Imbalances erkannt.");

                        var detail = new EvaluatedConditionDetail(
                            "StackedBuyImb",
                            $"any={currentSnapshot.StackedBuyImbCount}, top={currentSnapshot.StackedBuyImbTopCount}",
                            "StackedImbalance thresholds matched"
                        );
                        metRelConditions.Add(detail);
                        LoggerHelper.LogDebug(_loggerSource, $"Created MetRel detail: {detail.ToShortString()}");
                    }
                    else
                    {
                        reasons.Add("Keine signifikanten Stacked Buy Imbalances erkannt.");
                    }
                }
                else
                {
                    reasons.Add("HINWEIS: Stacked Imbalance Thresholds nicht gesetzt, Kriterium nicht geprüft.");
                }
            }
            else // Direction == OrderDirections.Sell
            {
                // ####################################################################
                // #                       SHORT EVALUATION                           #
                // ####################################################################

                LoggerHelper.LogDebug(_loggerSource, "Evaluate: Short path");

                // **NEUES HARTES KRITERIUM: Maximaler-CVD-Impuls für Short Reversal**
                // Wenn dieser Schwellenwert gesetzt ist und der aktuelle CvdImpulse zu positiv ist,
                // wird das Muster sofort als NICHT erkannt zurückgegeben.
                if (thresholds.ThCvdImpulseMaxForShortReversal.HasValue &&
                    currentSnapshot.CvdImpulse > thresholds.ThCvdImpulseMaxForShortReversal.Value)
                {
                    var reasonText = $"BLOCKED (Hard Criterium): CvdImpulse ({currentSnapshot.CvdImpulse:F2}) ist zu positiv für ein Short Reversal (Max. erlaubt: {thresholds.ThCvdImpulseMaxForShortReversal.Value:F2}).";
                    reasons.Add(reasonText);
                    LoggerHelper.LogWarn(_loggerSource, $"[PatternSignaturer] HARD BLOCK for Short Reversal Bounce: {reasonText}");

                    var diagDetail = new EvaluatedConditionDetail(
                        "CvdImpulseTooPositiveForShortReversal",
                        FormatMetValue(currentSnapshot.CvdImpulse, "F2"),
                        $"> {FormatMetValue(thresholds.ThCvdImpulseMaxForShortReversal.Value, "F2")}"
                    );
                    metDiagnosticConditions.Add(diagDetail);

                    return PatternEvaluationResult.NotDetected(
                        Type,
                        string.Join(" | ", reasons),
                        null,
                        metHardConditions,
                        metRelConditions,
                        metDiagnosticConditions
                    );
                }


                // Kriterien-Gruppe 1: Aggressive Verkaufsdruck
                bool aggressiveCvdPresent = false;
                if (thresholds.ReversalThCvdImpulseShort.HasValue && currentSnapshot.CvdImpulse < thresholds.ReversalThCvdImpulseShort.Value)
                {
                    aggressiveCvdPresent = true;
                    reasons.Add($"CvdImpulse ({currentSnapshot.CvdImpulse:F2}) < ReversalThCvdImpulseShort ({thresholds.ReversalThCvdImpulseShort.Value:F2})");

                    var detail = new EvaluatedConditionDetail(
                        "CvdImpulseBelowReversalThShort",
                        FormatMetValue(currentSnapshot.CvdImpulse, "F2"),
                        $"< {FormatMetValue(thresholds.ReversalThCvdImpulseShort.Value, "F2")}"
                    );
                    metHardConditions.Add(detail);
                    LoggerHelper.LogDebug(_loggerSource, $"Created MetHard detail: {detail.ToShortString()}");
                }

                bool aggressivePressurePresent = false;
                if (thresholds.ReversalThAggPressureBreakoutBear.HasValue && currentSnapshot.AggPressure < thresholds.ReversalThAggPressureBreakoutBear.Value)
                {
                    aggressivePressurePresent = true;
                    reasons.Add($"AggPressure ({currentSnapshot.AggPressure:F2}) < ReversalThAggPressureBreakoutBear ({thresholds.ReversalThAggPressureBreakoutBear.Value:F2})");

                    var detail = new EvaluatedConditionDetail(
                        "AggPressureBelowReversalThBear",
                        FormatMetValue(currentSnapshot.AggPressure, "F2"),
                        $"< {FormatMetValue(thresholds.ReversalThAggPressureBreakoutBear.Value, "F2")}"
                    );
                    metHardConditions.Add(detail);
                    LoggerHelper.LogDebug(_loggerSource, $"Created MetHard detail: {detail.ToShortString()}");
                }

                if (aggressiveCvdPresent || aggressivePressurePresent)
                {
                    metCriteriaCount++;
                }
                else
                {
                    reasons.Add("FAIL: Unzureichender CvdImpulse oder AggPressure.");
                }

                // Momentum-Shift-Bestätigung (Inflection)
                bool inflectionBearish = features.InflectionCvd == InflectionType.Bearish || features.InflectionPressure == InflectionType.Bearish;
                if (inflectionBearish)
                {
                    metCriteriaCount++;
                    reasons.Add("Bearish Inflection in CVD oder AggPressure erkannt.");

                    var detail = new EvaluatedConditionDetail(
                        "InflectionBearish",
                        "Bearish",
                        "InflectionCvd or InflectionPressure == Bearish"
                    );
                    metRelConditions.Add(detail);
                    LoggerHelper.LogDebug(_loggerSource, $"Created MetRel detail: {detail.ToShortString()}");
                }
                else
                {
                    reasons.Add("FAIL: Keine Bearish Inflection in CVD oder AggPressure erkannt.");
                }

                // Kriterien-Gruppe 2: Mangel an bullischem Widerstand
                if (thresholds.MaxCounterDeltaShareBull.HasValue)
                {
                    if (currentSnapshot.MaxCounterShareBull < thresholds.MaxCounterDeltaShareBull.Value)
                    {
                        metCriteriaCount++;
                        reasons.Add($"MaxCounterShareBull ({currentSnapshot.MaxCounterShareBull:F2}) < ThMaxCounterDeltaShareBull ({thresholds.MaxCounterDeltaShareBull.Value:F2})");

                        var detail = new EvaluatedConditionDetail(
                            "MaxCounterShareBullLow",
                            FormatMetValue(currentSnapshot.MaxCounterShareBull, "F2"),
                            $"< {FormatMetValue(thresholds.MaxCounterDeltaShareBull.Value, "F2")}"
                        );
                        metHardConditions.Add(detail);
                        LoggerHelper.LogDebug(_loggerSource, $"Created MetHard detail: {detail.ToShortString()}");
                    }
                    else
                    {
                        reasons.Add("FAIL: MaxCounterShareBull ist zu hoch (starker bullischer Widerstand).");
                    }
                }
                else
                {
                    reasons.Add("HINWEIS: ThMaxCounterDeltaShareBull nicht gesetzt, Kriterium nicht geprüft.");
                }

                // Kriterien-Gruppe 3: Volumen- und Effizienz-Bestätigung
                if (thresholds.ThVolBurstZ.HasValue)
                {
                    if (currentSnapshot.VolBurstZ > thresholds.ThVolBurstZ.Value)
                    {
                        metCriteriaCount++;
                        reasons.Add($"VolBurstZ ({currentSnapshot.VolBurstZ:F2}) > ThVolBurstZ ({thresholds.ThVolBurstZ.Value:F2})");

                        var detail = new EvaluatedConditionDetail(
                            "VolBurstZAboveTh",
                            FormatMetValue(currentSnapshot.VolBurstZ, "F2"),
                            $"> {FormatMetValue(thresholds.ThVolBurstZ.Value, "F2")}"
                        );
                        metHardConditions.Add(detail);
                        LoggerHelper.LogDebug(_loggerSource, $"Created MetHard detail: {detail.ToShortString()}");
                    }
                    else
                    {
                        reasons.Add("VolBurstZ ist nicht signifikant.");
                    }
                }
                else
                {
                    reasons.Add("HINWEIS: ThVolBurstZ nicht gesetzt, Kriterium nicht geprüft.");
                }

                if (thresholds.ThEfficiency.HasValue)
                {
                    if (currentSnapshot.Efficiency < -thresholds.ThEfficiency.Value && features.SlopeEff < 0)
                    {
                        metCriteriaCount++;
                        reasons.Add($"Efficiency ({currentSnapshot.Efficiency:F2}) < -ThEfficiency ({-thresholds.ThEfficiency.Value:F2}) und SlopeEff ({features.SlopeEff:F2}) < 0");

                        var detail = new EvaluatedConditionDetail(
                            "EfficiencyBelowNegTh",
                            FormatMetValue(currentSnapshot.Efficiency, "F2"),
                            $"< -{FormatMetValue(thresholds.ThEfficiency.Value, "F2")}, SlopeEff<0"
                        );
                        metHardConditions.Add(detail);
                        LoggerHelper.LogDebug(_loggerSource, $"Created MetHard detail: {detail.ToShortString()}");
                    }
                    else
                    {
                        reasons.Add("Ineffiziente Abwärtsbewegung oder positive Efficiency-Steigung.");
                    }
                }
                else
                {
                    reasons.Add("HINWEIS: ThEfficiency nicht gesetzt, Kriterium nicht geprüft.");
                }

                // Kriterien-Gruppe 4: Stacked Selling Imbalances
                if ((thresholds.ThStackedImbAnyRangeMinDirectional.HasValue && thresholds.ThStackedImbAnyRangeMinDirectional.Value > 0) ||
                    (thresholds.ThStackedImbAnchoredRangeMinDirectional.HasValue && thresholds.ThStackedImbAnchoredRangeMinDirectional.Value > 0))
                {
                    if ((thresholds.ThStackedImbAnyRangeMinDirectional.HasValue && currentSnapshot.StackedSellImbCount >= thresholds.ThStackedImbAnyRangeMinDirectional.Value) ||
                        (thresholds.ThStackedImbAnchoredRangeMinDirectional.HasValue && currentSnapshot.StackedSellImbBottomCount >= thresholds.ThStackedImbAnchoredRangeMinDirectional.Value))
                    {
                        metCriteriaCount++;
                        reasons.Add("Signifikante Stacked Sell Imbalances erkannt.");

                        var detail = new EvaluatedConditionDetail(
                            "StackedSellImb",
                            $"any={currentSnapshot.StackedSellImbCount}, bottom={currentSnapshot.StackedSellImbBottomCount}",
                            "StackedImbalance thresholds matched"
                        );
                        metRelConditions.Add(detail);
                        LoggerHelper.LogDebug(_loggerSource, $"Created MetRel detail: {detail.ToShortString()}");
                    }
                    else
                    {
                        reasons.Add("Keine signifikanten Stacked Sell Imbalances erkannt.");
                    }
                }
                else
                {
                    reasons.Add("HINWEIS: Stacked Imbalance Thresholds nicht gesetzt, Kriterium nicht geprüft.");
                }
            }

            // ####################################################################
            // #                        GESAMTBEWERTUNG                           #
            // ####################################################################
            int minSignals = thresholds.MinSignalsRequired > 0 ? thresholds.MinSignalsRequired : 1;

            if (metCriteriaCount >= minSignals)
            {
                decimal confidence = (decimal)metCriteriaCount / (decimal)GetPossibleCriteriaCount(thresholds);
                LoggerHelper.LogInfo(_loggerSource, $"ReversalBounceMuster erkannt: {Type}, confidence={confidence:F2}, met={metCriteriaCount}");

              
                // Audit-Log: vollständige metHard / metRel Ausgabe (leicht parsebar)
                var metHardCount = metHardConditions.Count;
                var metHardSummary = metHardConditions.Any()
                    ? string.Join(" | ", metHardConditions.Select(h => h.ToShortString()))
                    : "(none)";

                var relCount = metRelConditions.Count;
                var relSummary = metRelConditions.Any()
                    ? string.Join(" | ", metRelConditions.Select(r => r.ToShortString()))
                    : "(none)";

                var diagCount = metDiagnosticConditions.Count;
                var diagSummary = metDiagnosticConditions.Any()
                    ? string.Join(" | ", metDiagnosticConditions.Select(d => d.ToShortString()))
                    : null;

                var finalMetHardPart = $"metHard={metHardCount}: {metHardSummary}";
                if (diagCount > 0)
                    finalMetHardPart += $" | diagnostics={diagCount}: {diagSummary}";

                LoggerHelper.LogInfo(_loggerSource,
                    $"[PatternSignaturer] Detected {Type} ({Direction}) {finalMetHardPart}, metRel={relCount}: {relSummary}");

                return PatternEvaluationResult.Detected(Type, confidence, reasons, null, metHardConditions, metRelConditions);
            }
            else
            {
                reasons.Add($"ReversakBounceMuster NICHT erkannt: {metCriteriaCount} von {minSignals} erforderlichen Signalen wurden erfüllt.");
                LoggerHelper.LogDebug(_loggerSource, $"Pattern NOT detected: {Type}. Reasons: {string.Join(" | ", reasons)}");
                LoggerHelper.LogDebug(_loggerSource, $"ReversalBouncePatternEvaluator returning NOT DETECTED. metHard={metHardConditions.Count}, metRel={metRelConditions.Count}. Details: " +
                (metHardConditions.Any() ? string.Join(" | ", metHardConditions.Select(h => h.ToShortString())) : "(no hard)") + " / " +
                (metRelConditions.Any() ? string.Join(" | ", metRelConditions.Select(r => r.ToShortString())) : "(no rel)"));

                
                // Optional: auch hier Diagnostics in Debug ausgeben, damit nicht-metende Diagnostics sichtbar bleiben
                if (metDiagnosticConditions.Any())
                {
                    LoggerHelper.LogDebug(_loggerSource,
                        $"ReversalBouncePatternEvaluator diagnostics ({metDiagnosticConditions.Count}): " +
                        string.Join(" | ", metDiagnosticConditions.Select(d => d.ToShortString())));
                }

                return PatternEvaluationResult.NotDetected(Type, string.Join(" | ", reasons), null, metHardConditions, metRelConditions);
            }
        }

        // Helper-Methode, um die maximale Anzahl der möglichen Kriterien zu ermitteln,
        // basierend darauf, welche Schwellenwerte gesetzt sind.
        private int GetPossibleCriteriaCount(OrderflowThresholds thresholds)
        {
            int possible = 0;
            possible++; // Group 1b (either/or inflection) is always checked

            if (_direction == OrderDirections.Buy)
            {
                if (thresholds.ReversalThCvdImpulseLong.HasValue || thresholds.ReversalThAggPressureBreakoutBull.HasValue) possible++; // Group 1a
                if (thresholds.MaxCounterDeltaShareBear.HasValue) possible++; // Group 2
            }
            else // Short
            {
                if (thresholds.ReversalThCvdImpulseShort.HasValue || thresholds.ReversalThAggPressureBreakoutBear.HasValue) possible++; // Group 1a
                if (thresholds.MaxCounterDeltaShareBull.HasValue) possible++; // Group 2
            }

            if (thresholds.ThVolBurstZ.HasValue) possible++; // Group 3a
            if (thresholds.ThEfficiency.HasValue) possible++; // Group 3b

            if ((thresholds.ThStackedImbAnyRangeMinDirectional.HasValue && thresholds.ThStackedImbAnyRangeMinDirectional.Value > 0) ||
                (thresholds.ThStackedImbAnchoredRangeMinDirectional.HasValue && thresholds.ThStackedImbAnchoredRangeMinDirectional.Value > 0)) possible++; // Group 4

            return Math.Max(1, possible); // Mindestens 1 mögliches Kriterium
        }
    }
}
