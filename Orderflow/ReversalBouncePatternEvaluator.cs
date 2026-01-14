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
    /// Implementierung des IPatternEvaluator f?r ReversalBounce-Muster (Long und Short).
    /// </summary>
    public class ReversalBouncePatternEvaluator : IPatternEvaluator
    {
        private readonly OrderDirections _direction;
        private readonly OrderflowPatternType _patternType;
        private readonly ILoggerSource _loggerSource;
        private OrderflowThresholds? _externalThresholds; // Neue Property f?r externe Thresholds
        private Func<OrderflowThresholds?>? _thresholdsProvider; // Callback f?r dynamische Thresholds

        // Statische Property f?r globale Thresholds - alle Instanzen verwenden diese
        public static OrderflowThresholds? GlobalThresholds { get; set; }

        public OrderflowPatternType Type => _patternType;
        public OrderDirections Direction => _direction; // Ob der Evaluator f?r Long oder Short ist
        public string? previousDirection = null; // "BULL" oder "BEAR"

        // Property zum Setzen externer Thresholds (statisch)
        public OrderflowThresholds? ExternalThresholds
        {
            get => _externalThresholds;
            set => _externalThresholds = value;
        }

        // Property zum Setzen eines Callback-Providers f?r dynamische Thresholds
        public Func<OrderflowThresholds?>? ThresholdsProvider
        {
            get => _thresholdsProvider;
            set => _thresholdsProvider = value;
        }

        // Parameterloser Konstruktor für PatternSignaturer Discovery
        public ReversalBouncePatternEvaluator() : this(OrderDirections.Buy, null)
        {
        }

        public ReversalBouncePatternEvaluator(OrderDirections direction, ILoggerSource loggerSource = null)
        {
            _direction = direction;
            _patternType = direction == OrderDirections.Buy
                ? OrderflowPatternType.PotentialLongReversalBounce
                : OrderflowPatternType.PotentialShortReversalBounce;

            _loggerSource = loggerSource;

            // Eindeutiges Log zur Identifikation dieser Instanz
            LoggerHelper.LogInfo(_loggerSource, "[ReversalBouncePatternEvaluator] [CTOR] ReversalBouncePatternEvaluator created - Direction: {_direction}, Hash: {GetHashCode()}");
        }

        // Helper: konsistente Formatierung f?r MetValue
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
            // Verwende globale Thresholds zuerst, dann dynamische, dann externe, dann ?bergebene
            var effectiveThresholds = GlobalThresholds ?? _thresholdsProvider?.Invoke() ?? _externalThresholds ?? thresholds;

            // Debug-Log zur ?berpr?fung, welche Thresholds verwendet werden
            if (GlobalThresholds != null)
            {
                LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] Using global thresholds: ReversalThCvdImpulseLong={FormatMetValue(GlobalThresholds?.ReversalThCvdImpulseLong)}");
            }
            else if (_thresholdsProvider != null)
            {
                var dynamicThresholds = _thresholdsProvider.Invoke();
                LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] Using dynamic thresholds: ReversalThCvdImpulseLong={FormatMetValue(dynamicThresholds?.ReversalThCvdImpulseLong)}");
            }
            else if (_externalThresholds != null)
            {
                LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] Using external thresholds: ReversalThCvdImpulseLong={FormatMetValue(_externalThresholds?.ReversalThCvdImpulseLong)}");
            }
            else
            {
                LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] Using passed thresholds: ReversalThCvdImpulseLong={FormatMetValue(thresholds?.ReversalThCvdImpulseLong)}");
            }


            LoggerHelper.LogDebug(_loggerSource, $"[ReversalBouncePatternEvaluator] Evaluate: start direction={_direction}, bar={features?.Bar}");

            // --- Log der wichtigsten aktuellen Werte und relevanten Schwellenwerte ---
            LoggerHelper.LogInfo(_loggerSource,
                $"[ReversalBouncePatternEvaluator] Snapshot values: CvdImpulse={FormatMetValue(currentSnapshot.CvdImpulse)}, AggPressure={FormatMetValue(currentSnapshot.AggPressure)}, VolBurstZ={FormatMetValue(currentSnapshot.VolBurstZ)}, Efficiency={FormatMetValue(currentSnapshot.Efficiency)}, " +
                $"Close={FormatMetValue(currentSnapshot.Close)}, Open={FormatMetValue(currentSnapshot.Open)}, MaxCounterShareBear={FormatMetValue(currentSnapshot.MaxCounterShareBear)}, MaxCounterShareBull={FormatMetValue(currentSnapshot.MaxCounterShareBull)}, " +
                $"StackedBuyImbCount={FormatMetValue(currentSnapshot.StackedBuyImbCount)}, StackedBuyImbTopCount={FormatMetValue(currentSnapshot.StackedBuyImbTopCount)}, StackedSellImbCount={FormatMetValue(currentSnapshot.StackedSellImbCount)}, StackedSellImbBottomCount={FormatMetValue(currentSnapshot.StackedSellImbBottomCount)}, " +
                $"SlopeEff={FormatMetValue(features?.SlopeEff)}, InflectionCvd={FormatMetValue(features?.InflectionCvd)}, InflectionPressure={FormatMetValue(features?.InflectionPressure)}"
            );

            LoggerHelper.LogInfo(_loggerSource,
                $"[ReversalBouncePatternEvaluator] Thresholds: ReversalThCvdImpulseMinForLongReversal={FormatMetValue(effectiveThresholds?.ReversalThCvdImpulseMinForLongReversal)}, " +
                $"ReversalThCvdImpulseMaxForShortReversal={FormatMetValue(effectiveThresholds?.ReversalThCvdImpulseMaxForShortReversal)}, " +
                $"ReversalThCvdImpulseLong={FormatMetValue(effectiveThresholds?.ReversalThCvdImpulseLong)}, ReversalThCvdImpulseShort={FormatMetValue(effectiveThresholds?.ReversalThCvdImpulseShort)}, " +
                $"ReversalThAggPressureBreakoutBull={FormatMetValue(effectiveThresholds?.ReversalThAggPressureBreakoutBull)}, ReversalThAggPressureBreakoutBear={FormatMetValue(effectiveThresholds?.ReversalThAggPressureBreakoutBear)}, " +
                $"ReversalThVolBurstZ={FormatMetValue(effectiveThresholds?.ReversalThVolBurstZ)}, ReversalThEfficiency={FormatMetValue(effectiveThresholds?.ReversalThEfficiency)}, " +
                $"ReversalMaxCounterDeltaShareBear={FormatMetValue(effectiveThresholds?.ReversalMaxCounterDeltaShareBear)}, ReversalMaxCounterDeltaShareBull={FormatMetValue(effectiveThresholds?.ReversalMaxCounterDeltaShareBull)}, " +
                $"ReversalThStackedImbAnyRangeMinDirectional={FormatMetValue(effectiveThresholds?.ReversalThStackedImbAnyRangeMinDirectional)}, ReversalThStackedImbAnchoredRangeMinDirectional={FormatMetValue(effectiveThresholds?.ReversalThStackedImbAnchoredRangeMinDirectional)}, " +
                $"MinSignalsRequired={FormatMetValue(effectiveThresholds?.MinSignalsRequired)}"
            );
            // -----------------------------------------------------------------------

            var reasons = new List<string>();
            int metCriteriaCount = 0;
            
            // KORREKT: Verwende Konstruktor-Direction statt detectedDirection
            bool isValidSetup = false;
            OrderDirections detectedDirection = _direction; // KORREKT: Verwende Konstruktor-Direction

            // Sammler f?r erzeugte Condition-Details
            var metHardConditions = new List<EvaluatedConditionDetail>();
            var metRelConditions = new List<EvaluatedConditionDetail>();
            var metDiagnosticConditions = new List<EvaluatedConditionDetail>();

            // KORREKTUR: Alter Long-Zweig wird nicht mehr verwendet - durch neue Logik ersetzt!
            // Die neue Logik verwendet actualDirection statt Direction und wird weiter unten ausgeführt
            if (false && Direction == OrderDirections.Buy)
            {
                // ####################################################################
                // #                        LONG EVALUATION                           #
                // ####################################################################


                LoggerHelper.LogDebug(_loggerSource, "[ReversalBouncePatternEvaluator] Evaluate: Long path (DEAKTIVIERT)");

                // **NEUES HARTES KRITERIUM: Mindest-CVD-Impuls f?r Long Reversal**
                // Wenn dieser Schwellenwert gesetzt ist und der aktuelle CvdImpulse zu negativ ist,
                // wird das Muster sofort als NICHT erkannt zur?ckgegeben.
                if (thresholds.ReversalThCvdImpulseMinForLongReversal.HasValue &&
                    currentSnapshot.CvdImpulse < thresholds.ReversalThCvdImpulseMinForLongReversal.Value)
                {
                    var reasonText = $"[ReversalBouncePatternEvaluator] BLOCKED (Hard Criterium): CvdImpulse ({currentSnapshot.CvdImpulse:F2}) ist zu negativ f?r ein Long Reversal (Min. erlaubt: {thresholds.ReversalThCvdImpulseMinForLongReversal.Value:F2}).";
                    reasons.Add(reasonText);
                    LoggerHelper.LogWarn(_loggerSource, $"[ReversalBouncePatternEvaluator] HARD BLOCK for Long Reversal Bounce: {reasonText}");

                    var diagDetail = new EvaluatedConditionDetail(
                        "CvdImpulseTooNegativeForLongReversal",
                        FormatMetValue(currentSnapshot.CvdImpulse, "F2"),
                        $"< {FormatMetValue(thresholds.ReversalThCvdImpulseMinForLongReversal.Value, "F2")}"
                    );
                    metDiagnosticConditions.Add(diagDetail);

                    // LogInfo speziell f?r dieses Block-Event (zeigt Vergleich)
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] Compare: current CvdImpulse={currentSnapshot.CvdImpulse:F2} < ReversalThCvdImpulseMinForLongReversal={thresholds.ReversalThCvdImpulseMinForLongReversal.Value:F2} => HARD BLOCK Long");

                    return PatternEvaluationResult.NotDetected(
                        Type,
                        string.Join(" | ", reasons),
                        null,
                        metHardConditions,
                        metRelConditions,
                        metDiagnosticConditions
                    );
                }

                // Strenger Block: wenn Bar b?risch oder CVD klar negativ, disqualifizieren
                bool isBearishBar = currentSnapshot.Close <= currentSnapshot.Open;
                bool cvdNegative = currentSnapshot.CvdImpulse < 0m;
                bool aggNotBullish = !thresholds.ReversalThAggPressureBreakoutBull.HasValue || currentSnapshot.AggPressure <= 0m;

                // Log die drei Werte, die hier gepr?ft werden
                LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] Bearish/CVD/Agg check: Close={currentSnapshot.Close:F2}, Open={currentSnapshot.Open:F2}, CvdImpulse={currentSnapshot.CvdImpulse:F2}, AggPressure={currentSnapshot.AggPressure:F2}, ThresholdAggBull={FormatMetValue(thresholds.ReversalThAggPressureBreakoutBull)}");

                // NEU: Range-Bar-Analyse
                var rangeAnalysis = RangeBarAnalyzer.AnalyzeRangeBarPattern(history, features.Bar, currentSnapshot, effectiveThresholds);
                LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] Range-Bar Analysis: IsRangeMarket={rangeAnalysis.IsRangeMarket}, HasValidReversalSetup={rangeAnalysis.HasValidReversalSetup}, ConsecutiveTrendBars={rangeAnalysis.ConsecutiveTrendBars}, BarsSinceLastReversal={rangeAnalysis.BarsSinceLastReversal}, Reason={rangeAnalysis.Reason}");

                // KORREKTUR: Pattern-Prüfung ZUERST - ohne gültiges Pattern kein Entry-Fenster
                if (rangeAnalysis.IsRangeMarket && !rangeAnalysis.HasValidReversalSetup)
                {
                    var reasonText = $"[ReversalBouncePatternEvaluator] BLOCKED (Range Market): Alternating pattern detected without valid reversal setup. Pattern: [{string.Join(", ", rangeAnalysis.BarTypes)}]";
                    reasons.Add(reasonText);
                    LoggerHelper.LogWarn(_loggerSource, $"[ReversalBouncePatternEvaluator] RANGE BLOCK for Long Reversal Bounce: {reasonText}");

                    var diagDetail = new EvaluatedConditionDetail(
                        "RangeMarketWithoutValidReversal",
                        $"{string.Join(", ", rangeAnalysis.BarTypes)}",
                        "Alternating pattern without reversal setup"
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

                // KORREKTUR: Spezifische Long-Setup-Prüfung
                if (!rangeAnalysis.HasValidLongReversalSetup)
                {
                    var reasonText = $"[ReversalBouncePatternEvaluator] BLOCKED (Pattern Direction): No valid LONG reversal setup found. Pattern: [{string.Join(", ", rangeAnalysis.BarTypes)}]. Expected: [TREND_BEAR, TREND_BEAR, REVERSAL_BULL] for Long pattern";
                    reasons.Add(reasonText);
                    LoggerHelper.LogWarn(_loggerSource, $"[ReversalBouncePatternEvaluator] PATTERN DIRECTION BLOCK for Long Reversal Bounce: {reasonText}");

                    var diagDetail = new EvaluatedConditionDetail(
                        "PatternDirectionMismatch",
                        $"{string.Join(", ", rangeAnalysis.BarTypes)}",
                        "Expected [TREND_BEAR, TREND_BEAR, REVERSAL_BULL] for Long"
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

                // KORREKTUR: Setze isValidSetup=true wenn gültiges Long-Pattern gefunden
                isValidSetup = true;

                // KORREKTUR: Jetzt Entry-Fenster-Prüfung - nur wenn gültiges Pattern vorhanden
                bool isInEntryWindow = rangeAnalysis.HasValidReversalSetup && rangeAnalysis.BarsSinceLastValidReversal <= effectiveThresholds.RangeBarMaxBarsAfterReversal;
                
                if (!isInEntryWindow)
                {
                    var reasonTextExpired = $"[ReversalBouncePatternEvaluator] BLOCKED (Entry Window): No valid reversal setup within entry window. Requires HasValidReversalSetup=true AND BarsSinceLastValidReversal <= MaxBarsAfterReversal.";
                    reasons.Add(reasonTextExpired);
                    LoggerHelper.LogWarn(_loggerSource, $"[ReversalBouncePatternEvaluator] ENTRY WINDOW BLOCK for Long Reversal Bounce: {reasonTextExpired}");

                    var diagDetailExpired = new EvaluatedConditionDetail(
                        "EntryWindowExpired",
                        $"{rangeAnalysis.BarsSinceLastReversal} > {effectiveThresholds.RangeBarMaxBarsAfterReversal}",
                        "Too many bars since last valid reversal"
                    );
                    metDiagnosticConditions.Add(diagDetailExpired);

                    return PatternEvaluationResult.NotDetected(
                        Type,
                        string.Join(" | ", reasons),
                        null,
                        metHardConditions,
                        metRelConditions,
                        metDiagnosticConditions
                    );
                }

                // KORREKTUR: Innerhalb Entry-Fenster - hier ist die Logik anders!
                LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 🔍 IM ENTRY-FENSTER (Long): Bars seit Reversal={rangeAnalysis.BarsSinceLastValidReversal}, Gültiges Long-Setup={rangeAnalysis.HasValidLongReversalSetup}");
                
                // Innerhalb Entry-Fenster: Orderflow-Prüfung ist ausreichend, keine neue Pattern-Prüfung nötig
                // Das ursprüngliche Pattern wurde bereits erkannt und das Entry-Fenster ist offen

                if (isBearishBar || cvdNegative)
                {
                    reasons.Add($"BLOCKED: Bearish candle (Close {currentSnapshot.Close:F2} <= Open {currentSnapshot.Open:F2}) OR CvdImpulse {currentSnapshot.CvdImpulse:F2} < 0.");
                    LoggerHelper.LogWarn(_loggerSource, $"[ReversalBouncePatternEvaluator] Blocking Long: bearish candle OR negative CVD. Bar={features.Bar}");
                    metDiagnosticConditions.Add(new EvaluatedConditionDetail(
                    "BarBearishOrCvdNegative",
                    $"{FormatMetValue(currentSnapshot.Close)}/{FormatMetValue(currentSnapshot.Open)} | Cvd={FormatMetValue(currentSnapshot.CvdImpulse)}",
                    "Bearish candle || CvdImpulse < 0 => disqualify Long"
                    ));

            }

                            // Momentum-Shift-Bestätigung (Inflection)
                bool inflectionDetected = false;
                if (detectedDirection == OrderDirections.Sell)
                {
                    // Short-Logik
                    inflectionDetected = features.InflectionCvd == InflectionType.Bearish || features.InflectionPressure == InflectionType.Bearish;
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] Inflection check (Bear): InflectionCvd={features.InflectionCvd}, InflectionPressure={features.InflectionPressure}, => inflectionBearish={inflectionDetected}");
                    if (inflectionDetected)
                    {
                        metCriteriaCount++;
                        reasons.Add("Bearish Inflection in CVD oder AggPressure erkannt.");

                        var detail = new EvaluatedConditionDetail(
                            "InflectionBearish",
                            "Bearish",
                            "InflectionCvd or InflectionPressure == Bearish"
                        );
                        metRelConditions.Add(detail);
                        LoggerHelper.LogDebug(_loggerSource, $"[ReversalBouncePatternEvaluator] Created MetRel detail: {detail.ToShortString()}");
                    }
                }
                else
                {
                    // Long-Logik
                    inflectionDetected = features.InflectionCvd == InflectionType.Bullish || features.InflectionPressure == InflectionType.Bullish;
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] Inflection check (Bull): InflectionCvd={features.InflectionCvd}, InflectionPressure={features.InflectionPressure}, => inflectionBullish={inflectionDetected}");
                    if (inflectionDetected)
                    {
                        metCriteriaCount++;
                        reasons.Add("Bullish Inflection in CVD oder AggPressure erkannt.");

                        var detail = new EvaluatedConditionDetail(
                            "InflectionBullish",
                            "Bullish",
                            "InflectionCvd or InflectionPressure == Bullish"
                        );
                        metRelConditions.Add(detail);
                        LoggerHelper.LogDebug(_loggerSource, $"[ReversalBouncePatternEvaluator] Created MetRel detail: {detail.ToShortString()}");
                    }
                }
                
                if (!inflectionDetected)
                {
                    if (detectedDirection == OrderDirections.Sell)
                    {
                        reasons.Add("FAIL: Keine Bearish Inflection in CVD oder AggPressure erkannt.");
                    }
                    else
                    {
                        reasons.Add("FAIL: Keine Bullish Inflection in CVD oder AggPressure erkannt.");
                    }
                }

                // Kriterien-Gruppe 2: Mangel an bullischem Widerstand
                if (thresholds.ReversalMaxCounterDeltaShareBull.HasValue)
                {
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] Compare MaxCounterShareBull: current={currentSnapshot.MaxCounterShareBull:F2} vs Threshold={thresholds.ReversalMaxCounterDeltaShareBull.Value:F2}");
                    if (currentSnapshot.MaxCounterShareBull < thresholds.ReversalMaxCounterDeltaShareBull.Value)
                    {
                        metCriteriaCount++;
                        reasons.Add($"MaxCounterShareBull ({currentSnapshot.MaxCounterShareBull:F2}) < ReversalThMaxCounterDeltaShareBull ({thresholds.ReversalMaxCounterDeltaShareBull.Value:F2})");

                        var detail = new EvaluatedConditionDetail(
                            "MaxCounterShareBullLow",
                            FormatMetValue(currentSnapshot.MaxCounterShareBull, "F2"),
                            $"< {FormatMetValue(thresholds.ReversalMaxCounterDeltaShareBull.Value, "F2")}"
                        );
                        metHardConditions.Add(detail);
                        LoggerHelper.LogDebug(_loggerSource, $"[ReversalBouncePatternEvaluator] Created MetHard detail: {detail.ToShortString()}");
                    }
                    else
                    {
                        reasons.Add("FAIL: MaxCounterShareBull ist zu hoch (starker bullischer Widerstand).");
                    }
                }
                else
                {
                    reasons.Add("HINWEIS: ReversalThMaxCounterDeltaShareBull nicht gesetzt, Kriterium nicht gepr?ft.");
                }

                // Kriterien-Gruppe 3: Volumen- und Effizienz-Best?tigung
                if (thresholds.ReversalThVolBurstZ.HasValue)
                {
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] Compare VolBurstZ: current={currentSnapshot.VolBurstZ:F2} vs Threshold={thresholds.ReversalThVolBurstZ.Value:F2}");
                    if (currentSnapshot.VolBurstZ > thresholds.ReversalThVolBurstZ.Value)
                    {
                        metCriteriaCount++;
                        reasons.Add($"VolBurstZ ({currentSnapshot.VolBurstZ:F2}) > ReversalThVolBurstZ ({thresholds.ReversalThVolBurstZ.Value:F2})");

                        var detail = new EvaluatedConditionDetail(
                            "VolBurstZAboveTh",
                            FormatMetValue(currentSnapshot.VolBurstZ, "F2"),
                            $"> {FormatMetValue(thresholds.ReversalThVolBurstZ.Value, "F2")}"
                        );
                        metHardConditions.Add(detail);
                        LoggerHelper.LogDebug(_loggerSource, $"[ReversalBouncePatternEvaluator] Created MetHard detail: {detail.ToShortString()}");
                    }
                    else
                    {
                        reasons.Add("VolBurstZ ist nicht signifikant.");
                    }
                }
                else
                {
                    reasons.Add("HINWEIS: ReversalThVolBurstZ nicht gesetzt, Kriterium nicht gepr?ft.");
                }

                if (thresholds.ReversalThEfficiency.HasValue)
                {
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] Compare Efficiency (Short): current={currentSnapshot.Efficiency:F2} vs -Threshold={-thresholds.ReversalThEfficiency.Value:F2}, SlopeEff={features.SlopeEff:F2}");
                    if (currentSnapshot.Efficiency < -thresholds.ReversalThEfficiency.Value && features.SlopeEff < 0)
                    {
                        metCriteriaCount++;
                        reasons.Add($"Efficiency ({currentSnapshot.Efficiency:F2}) < -ReversalThEfficiency ({-thresholds.ReversalThEfficiency.Value:F2}) und SlopeEff ({features.SlopeEff:F2}) < 0");

                        var detail = new EvaluatedConditionDetail(
                            "EfficiencyBelowNegTh",
                            FormatMetValue(currentSnapshot.Efficiency, "F2"),
                            $"< -{FormatMetValue(thresholds.ReversalThEfficiency.Value, "F2")}, SlopeEff<0"
                        );
                        metHardConditions.Add(detail);
                        LoggerHelper.LogDebug(_loggerSource, $"[ReversalBouncePatternEvaluator] Created MetHard detail: {detail.ToShortString()}");
                    }
                    else
                    {
                        reasons.Add("Ineffiziente Abw?rtsbewegung oder positive Efficiency-Steigung.");
                    }
                }
                else
                {
                    reasons.Add("HINWEIS: ReversalThEfficiency nicht gesetzt, Kriterium nicht gepr?ft.");
                }

                // Kriterien-Gruppe 4: Stacked Selling Imbalances
                if ((thresholds.ReversalThStackedImbAnyRangeMinDirectional.HasValue && thresholds.ReversalThStackedImbAnyRangeMinDirectional.Value > 0) ||
                    (thresholds.ReversalThStackedImbAnchoredRangeMinDirectional.HasValue && thresholds.ReversalThStackedImbAnchoredRangeMinDirectional.Value > 0))
                {
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] Stacked Sell Imb thresholds: anyTh={FormatMetValue(thresholds.ReversalThStackedImbAnyRangeMinDirectional)}, anchoredTh={FormatMetValue(thresholds.ReversalThStackedImbAnchoredRangeMinDirectional)}, current any={currentSnapshot.StackedSellImbCount}, bottom={currentSnapshot.StackedSellImbBottomCount}");
                    if ((thresholds.ReversalThStackedImbAnyRangeMinDirectional.HasValue && currentSnapshot.StackedSellImbCount >= thresholds.ReversalThStackedImbAnyRangeMinDirectional.Value) ||
                        (thresholds.ReversalThStackedImbAnchoredRangeMinDirectional.HasValue && currentSnapshot.StackedSellImbBottomCount >= thresholds.ReversalThStackedImbAnchoredRangeMinDirectional.Value))
                    {
                        metCriteriaCount++;
                        reasons.Add("Signifikante Stacked Sell Imbalances erkannt.");

                        var detail = new EvaluatedConditionDetail(
                            "StackedSellImb",
                            $"any={currentSnapshot.StackedSellImbCount}, bottom={currentSnapshot.StackedSellImbBottomCount}",
                            "StackedImbalance thresholds matched"
                        );
                        metRelConditions.Add(detail);
                        LoggerHelper.LogDebug(_loggerSource, $"[ReversalBouncePatternEvaluator] Created MetRel detail: {detail.ToShortString()}");
                    }
                    else
                    {
                        reasons.Add("Keine signifikanten Stacked Sell Imbalances erkannt.");
                    }
                }
                else
                {
                    reasons.Add("HINWEIS: Reversal Stacked Imbalance Thresholds nicht gesetzt, Kriterium nicht gepr?ft.");
                }
            }

            // ####################################################################
            // #                        GESAMTBEWERTUNG                           #
            // ####################################################################
            int minSignals = thresholds.MinSignalsRequired > 0 ? thresholds.MinSignalsRequired : 1;

            // KORREKTUR: Entry-Fenster-Logik - wenn im Entry-Fenster und Orderflow passt, als Detected melden!
            // Wir müssen die Variablen aus dem Long/Short-Zweig hier verfügbar machen
            // Verwende andere Namen um Konflikte zu vermeiden
            bool entryWindowActive = false;
            bool barIsBearish = false;
            bool cvdIsNegative = false;
            bool entryWindowActiveShort = false;
            bool barIsBullish = false;
            bool cvdIsPositive = false;
            RangeBarAnalysisResult? entryRangeAnalysis = null;
            
            if (Direction == OrderDirections.Buy)
            {
                // Long-Variablen aus dem Long-Zweig holen
                barIsBearish = currentSnapshot.Close <= currentSnapshot.Open;
                cvdIsNegative = currentSnapshot.CvdImpulse < 0;
                entryRangeAnalysis = RangeBarAnalyzer.AnalyzeRangeBarPattern(history, features.Bar, currentSnapshot, effectiveThresholds);
                entryWindowActive = entryRangeAnalysis.HasValidReversalSetup && entryRangeAnalysis.BarsSinceLastValidReversal > 0 && entryRangeAnalysis.BarsSinceLastValidReversal <= effectiveThresholds.RangeBarMaxBarsAfterReversal;
                
                if (entryWindowActive && !(barIsBearish || cvdIsNegative))
                {
                    // Wir sind im Long Entry-Fenster und Orderflow-Basic-Checks sind bestanden
                    // JETZT ERST die Orderflow-Prüfung durchführen und loggen!
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 🔍 ORDERFLOW-PRÜFUNG (Long) - Entry-Fenster aktiv, Basic-Checks bestanden");
                    
                    // Kriterien-Gruppe 1: Aggressive Kaufkraft (Orderflow-Prüfung im Entry-Fenster)
                    // ----------------------------------------------------
                    
                    // Aggressive CVD vorhanden?
                    bool aggressiveCvdPresent = false;
                    LoggerHelper.LogDebug(_loggerSource, $"[ReversalBouncePatternEvaluator] ReversalThCvdImpulseLong.HasValue={thresholds.ReversalThCvdImpulseLong.HasValue}");
                    if (thresholds.ReversalThCvdImpulseLong.HasValue)
                    {
                        bool cvdMet = currentSnapshot.CvdImpulse > thresholds.ReversalThCvdImpulseLong.Value;
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 📊 CVD-Prüfung: Current={currentSnapshot.CvdImpulse:F2} > Threshold={thresholds.ReversalThCvdImpulseLong.Value:F2} => {(cvdMet ? "✅ ERFÜLLT" : "❌ NICHT ERFÜLLT")}");
                    }
                    else
                    {
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 📊 CVD-Prüfung: Current={currentSnapshot.CvdImpulse:F2} (Threshold=n/a) => ⚠️ KEIN THRESHOLD");
                    }
                    if (thresholds.ReversalThCvdImpulseLong.HasValue && currentSnapshot.CvdImpulse > thresholds.ReversalThCvdImpulseLong.Value)
                    {
                        aggressiveCvdPresent = true;
                        metCriteriaCount++;
                        
                        var detail = new EvaluatedConditionDetail(
                            "AggressiveCvdPresent",
                            $"Cvd={FormatMetValue(currentSnapshot.CvdImpulse)} > Th={FormatMetValue(thresholds.ReversalThCvdImpulseLong.Value)}",
                            "Aggressive CVD present"
                        );
                        metHardConditions.Add(detail);
                        LoggerHelper.LogDebug(_loggerSource, $"[ReversalBouncePatternEvaluator] Erstellt MetHard Detail: {detail.ToShortString()}");

                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ✅ CVD-Kriterium ERFÜLLT: +1 Punkt (metCriteriaCount={metCriteriaCount})");
                    }

                    // Aggressive Pressure vorhanden?
                    bool aggressivePressurePresent = false;
                    if (thresholds.ReversalThAggPressureBreakoutBull.HasValue)
                    {
                        bool pressureMet = currentSnapshot.AggPressure > thresholds.ReversalThAggPressureBreakoutBull.Value;
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 📊 AggPressure-Prüfung: Current={currentSnapshot.AggPressure:F2} > Threshold={thresholds.ReversalThAggPressureBreakoutBull.Value:F2} => {(pressureMet ? "✅ ERFÜLLT" : "❌ NICHT ERFÜLLT")}");
                    }
                    else
                    {
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 📊 AggPressure-Prüfung: Current={currentSnapshot.AggPressure:F2} (Threshold=n/a) => ⚠️ KEIN THRESHOLD");
                    }
                    if (thresholds.ReversalThAggPressureBreakoutBull.HasValue && currentSnapshot.AggPressure > thresholds.ReversalThAggPressureBreakoutBull.Value)
                    {
                        aggressivePressurePresent = true;
                        metCriteriaCount++;
                        
                        var detail = new EvaluatedConditionDetail(
                            "AggressivePressurePresent",
                            $"AggPressure={FormatMetValue(currentSnapshot.AggPressure)} > Th={FormatMetValue(thresholds.ReversalThAggPressureBreakoutBull.Value)}",
                            "Aggressive pressure present"
                        );
                        metHardConditions.Add(detail);
                        LoggerHelper.LogDebug(_loggerSource, $"[ReversalBouncePatternEvaluator] Erstellt MetHard Detail: {detail.ToShortString()}");

                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ✅ AggPressure-Kriterium ERFÜLLT: +1 Punkt (metCriteriaCount={metCriteriaCount})");
                    }

                    // Zusammenfassung Kriterien-Gruppe 1
                    if (aggressiveCvdPresent || aggressivePressurePresent)
                    {
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 🎯 Kriterien-Gruppe 1 ERFÜLLT: Aggressive Kaufkraft vorhanden (CVD: {aggressiveCvdPresent}, Pressure: {aggressivePressurePresent})");
                    }
                    else
                    {
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ❌ Kriterien-Gruppe 1 NICHT ERFÜLLT: Keine aggressive Kaufkraft vorhanden");
                        reasons.Add("FAIL: Unzureichender CvdImpulse oder AggPressure.");
                    }
                    
                    // Jetzt die volle Orderflow-Bewertung durchführen
                    if (metCriteriaCount >= minSignals)
                    {
                        decimal confidence = (decimal)metCriteriaCount / (decimal)GetPossibleCriteriaCount(thresholds);
                        // KORREKTUR: Pattern-Typ basierend auf Direction bestimmen (alter Code)
                        var finalPatternType = OrderflowPatternType.PotentialLongReversalBounce;
                        
                        // Spezieller Reason für Entry-Fenster
                        var entryWindowReasons = new List<string>(reasons);
                        entryWindowReasons.Insert(0, $"Gültiges Reversal-Setup im Entry-Fenster (Bars seit Reversal={entryRangeAnalysis.BarsSinceLastValidReversal})");
                        
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 🎯 ENTRY-FENSTER PATTERN ERKANNT: {finalPatternType}, Konfidenz={confidence:F2}, Kriterien={metCriteriaCount}, Bars seit Reversal={entryRangeAnalysis.BarsSinceLastValidReversal}");
                        
                        return PatternEvaluationResult.Detected(finalPatternType, confidence, entryWindowReasons, new Dictionary<string, object>(), metHardConditions, metRelConditions);
                    }
                    else
                    {
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ❌ ENTRY-FENSTER: Nicht genügend Kriterien erfüllt (metCriteriaCount={metCriteriaCount} < minSignals={minSignals})");
                        return PatternEvaluationResult.NotDetected(Type, "Entry-Fenster aktiv aber nicht genügend Orderflow-Kriterien erfüllt", new Dictionary<string, object>(), metHardConditions, metRelConditions);
                    }
                }
            }
            else if (Direction == OrderDirections.Sell)
            {
                // Short-Variablen aus dem Short-Zweig holen
                barIsBullish = currentSnapshot.Close > currentSnapshot.Open;
                cvdIsPositive = currentSnapshot.CvdImpulse > 0;
                entryRangeAnalysis = RangeBarAnalyzer.AnalyzeRangeBarPattern(history, features.Bar, currentSnapshot, effectiveThresholds);
                entryWindowActiveShort = entryRangeAnalysis.HasValidReversalSetup && entryRangeAnalysis.BarsSinceLastValidReversal > 0 && entryRangeAnalysis.BarsSinceLastValidReversal <= effectiveThresholds.RangeBarMaxBarsAfterReversal;
                
                if (entryWindowActiveShort && !(barIsBullish || cvdIsPositive))
                {
                    // Wir sind im Short Entry-Fenster und Orderflow-Basic-Checks sind bestanden
                    // Jetzt die volle Orderflow-Bewertung durchführen
                    if (metCriteriaCount >= minSignals)
                    {
                        decimal confidence = (decimal)metCriteriaCount / (decimal)GetPossibleCriteriaCount(thresholds);
                        var finalPatternType = OrderflowPatternType.PotentialShortReversalBounce;
                        
                        // Spezieller Reason für Entry-Fenster
                        var entryWindowReasons = new List<string>(reasons);
                        entryWindowReasons.Insert(0, $"Gültiges Reversal-Setup im Entry-Fenster (Bars seit Reversal={entryRangeAnalysis.BarsSinceLastValidReversal})");
                        
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 🎯 ENTRY-FENSTER PATTERN ERKANNT: {finalPatternType}, Konfidenz={confidence:F2}, Kriterien={metCriteriaCount}, Bars seit Reversal={entryRangeAnalysis.BarsSinceLastValidReversal}");
                        
                        return PatternEvaluationResult.Detected(finalPatternType, confidence, entryWindowReasons, new Dictionary<string, object>(), metHardConditions, metRelConditions);
                    }
                }
            }

            // KORREKTUR: Normale Pattern-Erkennung nur außerhalb des Entry-Fensters!
            // Im Entry-Fenster wird die Logik weiter oben bereits behandelt
            bool isInEntryWindowNow = false;
            
            // ZUERST: Pattern-Analyse durchführen um die richtige Direction zu bestimmen
            var patternAnalysis = RangeBarAnalyzer.AnalyzeRangeBarPattern(history, features.Bar, currentSnapshot, effectiveThresholds);
            
            // Direction basierend auf dem tatsächlichen Pattern bestimmen, nicht auf der Konstruktor-Direction
            OrderDirections actualDirection = Direction; // Default
            
            // INFO: Logge die Pattern-Analyse Ergebnisse für Direction-Bestimmung
            LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 🔍 DIRECTION-ANALYSE: HasValidLong={patternAnalysis.HasValidLongReversalSetup}, HasValidShort={patternAnalysis.HasValidShortReversalSetup}, HasValidReversal={patternAnalysis.HasValidReversalSetup}, LastReversalType={patternAnalysis.LastReversalType}");
            
            if (patternAnalysis.HasValidLongReversalSetup)
            {
                actualDirection = OrderDirections.Buy;
                LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ✅ DIRECTION SET: Long (HasValidLongReversalSetup=True)");
            }
            else if (patternAnalysis.HasValidShortReversalSetup)
            {
                actualDirection = OrderDirections.Sell;
                LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ✅ DIRECTION SET: Short (HasValidShortReversalSetup=True)");
            }
            else if (patternAnalysis.HasValidReversalSetup)
            {
                // Wenn wir im Entry-Fenster sind, die Direction vom letzten gültigen Pattern ableiten
                // Verwende LastReversalType aus der Pattern-Analyse
                if (!string.IsNullOrEmpty(patternAnalysis.LastReversalType))
                {
                    if (patternAnalysis.LastReversalType == "REVERSAL_BULL")
                    {
                        actualDirection = OrderDirections.Buy;
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ✅ DIRECTION SET: Long (LastReversalType=REVERSAL_BULL)");
                    }
                    else if (patternAnalysis.LastReversalType == "REVERSAL_BEAR")
                    {
                        actualDirection = OrderDirections.Sell;
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ✅ DIRECTION SET: Short (LastReversalType=REVERSAL_BEAR)");
                    }
                    else
                    {
                        // Fallback auf Konstruktor-Direction
                        actualDirection = Direction;
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ⚠️ DIRECTION FALLBACK: Using Konstruktor-Direction={Direction} (Unknown LastReversalType={patternAnalysis.LastReversalType})");
                    }
                }
                else
                {
                    // Fallback auf Konstruktor-Direction
                    actualDirection = Direction;
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ⚠️ DIRECTION FALLBACK: Using Konstruktor-Direction={Direction} (LastReversalType is empty)");
                }
            }
            else
            {
                // Kein gültiges Pattern
                actualDirection = Direction;
                LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ❌ DIRECTION DEFAULT: Using Konstruktor-Direction={Direction} (No valid pattern found)");
            }
            
            LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 🎯 FINAL DIRECTION: {actualDirection}");
            
            if (actualDirection == OrderDirections.Buy)
            {
                isInEntryWindowNow = patternAnalysis.HasValidReversalSetup && 
                                   patternAnalysis.BarsSinceLastValidReversal > 0 && 
                                   patternAnalysis.BarsSinceLastValidReversal <= effectiveThresholds.RangeBarMaxBarsAfterReversal;
                
                // KLARES DEUTSCHES LOGGING: Entry-Fenster Status für Long
                if (isInEntryWindowNow)
                {
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 🟢 ENTRY-FENSTER AKTIV (Long): Bars seit Reversal={patternAnalysis.BarsSinceLastValidReversal}, Max-Bars={effectiveThresholds.RangeBarMaxBarsAfterReversal}");
                }
                else
                {
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 🔴 ENTRY-FENSTER INAKTIV (Long): Bars seit Reversal={patternAnalysis.BarsSinceLastValidReversal}, Gültiges Setup={patternAnalysis.HasValidReversalSetup}");
                }
            }
            else if (actualDirection == OrderDirections.Sell)
            {
                isInEntryWindowNow = patternAnalysis.HasValidReversalSetup && 
                                   patternAnalysis.BarsSinceLastValidReversal > 0 && 
                                   patternAnalysis.BarsSinceLastValidReversal <= effectiveThresholds.RangeBarMaxBarsAfterReversal;
                
                // KLARES DEUTSCHES LOGGING: Entry-Fenster Status für Short
                if (isInEntryWindowNow)
                {
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 🟢 ENTRY-FENSTER AKTIV (Short): Bars seit Reversal={patternAnalysis.BarsSinceLastValidReversal}, Max-Bars={effectiveThresholds.RangeBarMaxBarsAfterReversal}");
                }
                else
                {
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 🔴 ENTRY-FENSTER INAKTIV (Short): Bars seit Reversal={patternAnalysis.BarsSinceLastValidReversal}, Gültiges Setup={patternAnalysis.HasValidReversalSetup}");
                }
            }

            // Nur normale Pattern-Erkennung wenn NICHT im Entry-Fenster
            // HIER NUR BAR-ABFOLGE PRÜFEN - kein Orderflow, kein Score!
            if (!isInEntryWindowNow)
            {
                // Prüfe ob gültiges Reversal-Pattern gefunden wurde (Bar-Abfolge)
                bool hasValidPattern = false;
                string patternType = "";
                
                if (Direction == OrderDirections.Buy)
                {
                    var rangeAnalysis = RangeBarAnalyzer.AnalyzeRangeBarPattern(history, features.Bar, currentSnapshot, effectiveThresholds);
                    hasValidPattern = rangeAnalysis.HasValidLongReversalSetup;
                    patternType = "Long-Reversal";
                    
                    if (hasValidPattern)
                    {
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 🎯 GÜLTIGES PATTERN GEFUNDEN (Long): Bar-Abfolge stimmt - Entry-Fenster wird geöffnet");
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 📊 Pattern-Details: [{string.Join(", ", rangeAnalysis.BarTypes)}]");
                        // KORREKTUR: Pattern-Erkennung öffnet nur Entry-Fenster, aber gibt NICHT sofort Detected zurück
                        // Das eigentliche Trading-Ergebnis kommt erst aus dem Entry-Fenster mit Orderflow-Prüfung
                        return PatternEvaluationResult.NotDetected(Type, "Gültiges Long-Reversal Pattern gefunden - Entry-Fenster geöffnet", new Dictionary<string, object>(), metHardConditions, metRelConditions);
                    }
                }
                else if (Direction == OrderDirections.Sell)
                {
                    var rangeAnalysis = RangeBarAnalyzer.AnalyzeRangeBarPattern(history, features.Bar, currentSnapshot, effectiveThresholds);
                    hasValidPattern = rangeAnalysis.HasValidShortReversalSetup;
                    patternType = "Short-Reversal";
                    
                    if (hasValidPattern)
                    {
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 🎯 GÜLTIGES PATTERN GEFUNDEN (Short): Bar-Abfolge stimmt - Entry-Fenster wird geöffnet");
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 📊 Pattern-Details: [{string.Join(", ", rangeAnalysis.BarTypes)}]");
                        // KORREKTUR: Pattern-Erkennung öffnet nur Entry-Fenster, aber gibt NICHT sofort Detected zurück
                        // Das eigentliche Trading-Ergebnis kommt erst aus dem Entry-Fenster mit Orderflow-Prüfung
                        return PatternEvaluationResult.NotDetected(Type, "Gültiges Short-Reversal Pattern gefunden - Entry-Fenster geöffnet", new Dictionary<string, object>(), metHardConditions, metRelConditions);
                    }
                }
                
                if (!hasValidPattern)
                {
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ❌ KEIN GÜLTIGES PATTERN ({patternType}): Bar-Abfolge stimmt nicht");
                }
                
                // Wenn kein gültiges Pattern gefunden wurde, return NOT DETECTED
                return PatternEvaluationResult.NotDetected(Type, "Kein gültiges Reversal-Pattern gefunden", new Dictionary<string, object>(), metHardConditions, metRelConditions);
            }
            else
            {
                // KORREKT: Wenn wir im Entry-Fenster sind, zuerst prüfen ob aktueller Bar ein Gegen-Reversal-Bar ist
                // Range-Bar Logik: Reversal-Bars alternieren immer (BULL → BEAR → BULL)
                // Entry-Fenster schließt bei Gegen-Reversal-Bar gegen die Trade-Richtung
                bool currentBarIsOppositeReversal = false;
                
                // Prüfe ob der aktuelle Bar in der Pattern-Analyse als Reversal markiert ist
                if (patternAnalysis.BarTypes.Count > 0)
                {
                    string? currentBarType = patternAnalysis.BarTypes.LastOrDefault();
                    if (currentBarType != null)
                    {
                        if (actualDirection == OrderDirections.Buy)
                        {
                            // Long-Entry-Fenster: Gegen-Reversal ist REVERSAL_BEAR
                            currentBarIsOppositeReversal = currentBarType == "REVERSAL_BEAR";
                        }
                        else if (actualDirection == OrderDirections.Sell)
                        {
                            // Short-Entry-Fenster: Gegen-Reversal ist REVERSAL_BULL
                            currentBarIsOppositeReversal = currentBarType == "REVERSAL_BULL";
                        }
                    }
                }
                
                if (currentBarIsOppositeReversal && patternAnalysis.HasValidReversalSetup)
                {
                    // GEGEN-REVERSAL-BAR IM ENTRY-FENSTER → FENSTER SCHLIESSEN!
                    string? currentBarType = patternAnalysis.BarTypes.LastOrDefault();
                    if (currentBarType != null)
                    {
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 🚨 ENTRY-FENSTER GESCHLOSSEN: Gegen-Reversal-Bar ({currentBarType}) gegen {actualDirection}-Richtung gefunden - Entry-Fenster wird geschlossen");
                    }
                    return PatternEvaluationResult.NotDetected(Type, "Entry-Fenster geschlossen - Gegen-Reversal-Bar erschienen", new Dictionary<string, object>(), metHardConditions, metRelConditions);
                }
                
                // KORREKT: Wenn wir im Entry-Fenster sind, hier die Orderflow-Prüfung durchführen!
                LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 🎯 ENTRY-FENSTER AKTIV: Orderflow-Prüfung wird durchgeführt - keine neue Pattern-Erkennung nötig");
                
                // Orderflow-Prüfung im Entry-Fenster durchführen
                // Basic-Checks zuerst
                bool basicChecksPassed = false;
                
                if (actualDirection == OrderDirections.Buy)
                {
                    bool barIsBearishLong = currentSnapshot.Close <= currentSnapshot.Open;
                    bool cvdNegative = currentSnapshot.CvdImpulse < 0;
                    basicChecksPassed = !(barIsBearishLong || cvdNegative);
                    
                    if (!basicChecksPassed)
                    {
                        string failedReason = "";
                        if (barIsBearishLong) failedReason += $"Bar ist bärisch ({barIsBearishLong})";
                        if (cvdNegative) 
                        {
                            if (!string.IsNullOrEmpty(failedReason)) failedReason += " UND ";
                            failedReason += $"CVD negativ ({cvdNegative})";
                        }
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ❌ BASIC-CHECKS FEHLGESCHLAGEN (Long): {failedReason}");
                        return PatternEvaluationResult.NotDetected(Type, "Entry-Fenster aktiv aber Basic-Checks fehlgeschlagen", new Dictionary<string, object>(), metHardConditions, metRelConditions);
                    }
                    
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ✅ BASIC-CHECKS BESTANDEN (Long): Orderflow-Prüfung wird durchgeführt");
                    
                    // Orderflow-Prüfung für Long
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 🔍 ORDERFLOW-PRÜFUNG (Long) - Entry-Fenster aktiv, Basic-Checks bestanden");
                    
                    // Kriterien-Gruppe 1: Aggressive Kaufkraft
                    bool aggressiveCvdPresent = false;
                    if (thresholds.ReversalThCvdImpulseLong.HasValue)
                    {
                        bool cvdMet = currentSnapshot.CvdImpulse > thresholds.ReversalThCvdImpulseLong.Value;
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 📊 CVD-Prüfung: Current={currentSnapshot.CvdImpulse:F2} > Threshold={thresholds.ReversalThCvdImpulseLong.Value:F2} => {(cvdMet ? "✅ ERFÜLLT" : "❌ NICHT ERFÜLLT")}");
                        if (cvdMet)
                        {
                            aggressiveCvdPresent = true;
                            metCriteriaCount++;
                            LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ✅ CVD-Kriterium ERFÜLLT: +1 Punkt (metCriteriaCount={metCriteriaCount})");
                        }
                    }
                    
                    bool aggressivePressurePresent = false;
                    if (thresholds.ReversalThAggPressureBreakoutBull.HasValue)
                    {
                        bool pressureMet = currentSnapshot.AggPressure > thresholds.ReversalThAggPressureBreakoutBull.Value;
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 📊 AggPressure-Prüfung: Current={currentSnapshot.AggPressure:F2} > Threshold={thresholds.ReversalThAggPressureBreakoutBull.Value:F2} => {(pressureMet ? "✅ ERFÜLLT" : "❌ NICHT ERFÜLLT")}");
                        if (pressureMet)
                        {
                            aggressivePressurePresent = true;
                            metCriteriaCount++;
                            LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ✅ AggPressure-Kriterium ERFÜLLT: +1 Punkt (metCriteriaCount={metCriteriaCount})");
                        }
                    }
                    
                    if (aggressiveCvdPresent || aggressivePressurePresent)
                    {
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 🎯 Kriterien-Gruppe 1 ERFÜLLT: Aggressive Kaufkraft vorhanden (CVD: {aggressiveCvdPresent}, Pressure: {aggressivePressurePresent})");
                    }
                    else
                    {
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ❌ Kriterien-Gruppe 1 NICHT ERFÜLLT: Keine aggressive Kaufkraft vorhanden");
                    }
                }
                else if (actualDirection == OrderDirections.Sell)
                {
                    bool barIsBullishShort = currentSnapshot.Close > currentSnapshot.Open;
                    bool cvdPositive = currentSnapshot.CvdImpulse > 0;
                    basicChecksPassed = !(barIsBullishShort || cvdPositive);
                    
                    if (!basicChecksPassed)
                    {
                        string failedReason = "";
                        if (barIsBullishShort) failedReason += $"Bar ist bullisch ({barIsBullishShort})";
                        if (cvdPositive) 
                        {
                            if (!string.IsNullOrEmpty(failedReason)) failedReason += " UND ";
                            failedReason += $"CVD positiv ({cvdPositive})";
                        }
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ❌ BASIC-CHECKS FEHLGESCHLAGEN (Short): {failedReason}");
                        return PatternEvaluationResult.NotDetected(Type, "Entry-Fenster aktiv aber Basic-Checks fehlgeschlagen", new Dictionary<string, object>(), metHardConditions, metRelConditions);
                    }
                    
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ✅ BASIC-CHECKS BESTANDEN (Short): Orderflow-Prüfung wird durchgeführt");
                    
                    // Orderflow-Prüfung für Short
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 🔍 ORDERFLOW-PRÜFUNG (Short) - Entry-Fenster aktiv, Basic-Checks bestanden");
                    
                    // Kriterien-Gruppe 1: Aggressive Verkaufskraft
                    bool aggressiveCvdPresent = false;
                    if (thresholds.ReversalThCvdImpulseShort.HasValue)
                    {
                        bool cvdMet = currentSnapshot.CvdImpulse < thresholds.ReversalThCvdImpulseShort.Value;
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 📊 CVD-Prüfung: Current={currentSnapshot.CvdImpulse:F2} < Threshold={thresholds.ReversalThCvdImpulseShort.Value:F2} => {(cvdMet ? "✅ ERFÜLLT" : "❌ NICHT ERFÜLLT")}");
                        if (cvdMet)
                        {
                            aggressiveCvdPresent = true;
                            metCriteriaCount++;
                            LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ✅ CVD-Kriterium ERFÜLLT: +1 Punkt (metCriteriaCount={metCriteriaCount})");
                        }
                    }
                    
                    bool aggressivePressurePresent = false;
                    if (thresholds.ReversalThAggPressureBreakoutBear.HasValue)
                    {
                        bool pressureMet = currentSnapshot.AggPressure < thresholds.ReversalThAggPressureBreakoutBear.Value;
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 📊 AggPressure-Prüfung: Current={currentSnapshot.AggPressure:F2} < Threshold={thresholds.ReversalThAggPressureBreakoutBear.Value:F2} => {(pressureMet ? "✅ ERFÜLLT" : "❌ NICHT ERFÜLLT")}");
                        if (pressureMet)
                        {
                            aggressivePressurePresent = true;
                            metCriteriaCount++;
                            LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ✅ AggPressure-Kriterium ERFÜLLT: +1 Punkt (metCriteriaCount={metCriteriaCount})");
                        }
                    }
                    
                    if (aggressiveCvdPresent || aggressivePressurePresent)
                    {
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 🎯 Kriterien-Gruppe 1 ERFÜLLT: Aggressive Verkaufskraft vorhanden (CVD: {aggressiveCvdPresent}, Pressure: {aggressivePressurePresent})");
                    }
                    else
                    {
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ❌ Kriterien-Gruppe 1 NICHT ERFÜLLT: Keine aggressive Verkaufskraft vorhanden");
                    }
                }
                
                // Prüfen ob genügend Kriterien erfüllt sind
                if (metCriteriaCount >= minSignals)
                {
                    decimal confidence = (decimal)metCriteriaCount / (decimal)GetPossibleCriteriaCount(thresholds);
                    var finalPatternType = actualDirection == OrderDirections.Buy
                        ? OrderflowPatternType.PotentialLongReversalBounce
                        : OrderflowPatternType.PotentialShortReversalBounce;
                    
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 🎯 ENTRY-FENSTER PATTERN ERKANNT: {finalPatternType}, Konfidenz={confidence:F2}, Kriterien={metCriteriaCount}, Bars seit Reversal={patternAnalysis.BarsSinceLastValidReversal}");
                    
                    return PatternEvaluationResult.Detected(finalPatternType, confidence, new List<string> { $"Entry-Fenster Pattern erkannt (Kriterien: {metCriteriaCount}/{minSignals})" }, new Dictionary<string, object>(), metHardConditions, metRelConditions);
                }
                else
                {
                    // Detailliertes Logging der fehlgeschlagenen Kriterien für bessere Anpassung
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] ❌ ENTRY-FENSTER: Nicht genügend Kriterien erfüllt (metCriteriaCount={metCriteriaCount} < minSignals={minSignals})");
                    
                    // Zusätzliche Details: Welche Kriterien wurden geprüft und welche fehlgeschlagen
                    var missingCriteria = new List<string>();
                    var checkedCriteria = new List<string>();
                    
                    if (actualDirection == OrderDirections.Buy)
                    {
                        // Long-Kriterien zusammenfassen
                        if (thresholds.ReversalThCvdImpulseLong.HasValue)
                        {
                            bool cvdMet = currentSnapshot.CvdImpulse > thresholds.ReversalThCvdImpulseLong.Value;
                            checkedCriteria.Add($"CVD: {currentSnapshot.CvdImpulse:F2} > {thresholds.ReversalThCvdImpulseLong.Value:F2} = {(cvdMet ? "✅" : "❌")}");
                            if (!cvdMet) missingCriteria.Add($"CVD (Current: {currentSnapshot.CvdImpulse:F2}, Required: >{thresholds.ReversalThCvdImpulseLong.Value:F2})");
                        }
                        
                        if (thresholds.ReversalThAggPressureBreakoutBull.HasValue)
                        {
                            bool pressureMet = currentSnapshot.AggPressure > thresholds.ReversalThAggPressureBreakoutBull.Value;
                            checkedCriteria.Add($"AggPressure: {currentSnapshot.AggPressure:F2} > {thresholds.ReversalThAggPressureBreakoutBull.Value:F2} = {(pressureMet ? "✅" : "❌")}");
                            if (!pressureMet) missingCriteria.Add($"AggPressure (Current: {currentSnapshot.AggPressure:F2}, Required: >{thresholds.ReversalThAggPressureBreakoutBull.Value:F2})");
                        }
                        else
                        {
                            // Auch wenn kein Threshold gesetzt ist, loggen dass es geprüft wurde
                            checkedCriteria.Add($"AggPressure: {currentSnapshot.AggPressure:F2} > n/a = ⚠️ KEIN THRESHOLD");
                            missingCriteria.Add($"AggPressure (Current: {currentSnapshot.AggPressure:F2}, Required: >n/a - KEIN THRESHOLD SET)");
                        }
                    }
                    else if (actualDirection == OrderDirections.Sell)
                    {
                        // Short-Kriterien zusammenfassen
                        if (thresholds.ReversalThCvdImpulseShort.HasValue)
                        {
                            bool cvdMet = currentSnapshot.CvdImpulse < thresholds.ReversalThCvdImpulseShort.Value;
                            checkedCriteria.Add($"CVD: {currentSnapshot.CvdImpulse:F2} < {thresholds.ReversalThCvdImpulseShort.Value:F2} = {(cvdMet ? "✅" : "❌")}");
                            if (!cvdMet) missingCriteria.Add($"CVD (Current: {currentSnapshot.CvdImpulse:F2}, Required: <{thresholds.ReversalThCvdImpulseShort.Value:F2})");
                        }
                        
                        if (thresholds.ReversalThAggPressureBreakoutBear.HasValue)
                        {
                            bool pressureMet = currentSnapshot.AggPressure < thresholds.ReversalThAggPressureBreakoutBear.Value;
                            checkedCriteria.Add($"AggPressure: {currentSnapshot.AggPressure:F2} < {thresholds.ReversalThAggPressureBreakoutBear.Value:F2} = {(pressureMet ? "✅" : "❌")}");
                            if (!pressureMet) missingCriteria.Add($"AggPressure (Current: {currentSnapshot.AggPressure:F2}, Required: <{thresholds.ReversalThAggPressureBreakoutBear.Value:F2})");
                        }
                        else
                        {
                            // Auch wenn kein Threshold gesetzt ist, loggen dass es geprüft wurde
                            checkedCriteria.Add($"AggPressure: {currentSnapshot.AggPressure:F2} < n/a = ⚠️ KEIN THRESHOLD");
                            missingCriteria.Add($"AggPressure (Current: {currentSnapshot.AggPressure:F2}, Required: <n/a - KEIN THRESHOLD SET)");
                        }
                    }
                    
                    // Detaillierte Zusammenfassung loggen
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator] 📊 KRITERIEN-ZUSAMMENFASSUNG ({actualDirection}):");
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator]   Geprüft: [{string.Join(", ", checkedCriteria)}]");
                    if (missingCriteria.Count > 0)
                    {
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator]   Fehlend: [{string.Join(", ", missingCriteria)}]");
                    }
                    else
                    {
                        LoggerHelper.LogInfo(_loggerSource, $"[ReversalBouncePatternEvaluator]   Alle verfügbaren Kriterien wurden erfüllt, aber minSignals={minSignals} wurde nicht erreicht");
                    }
                    
                    return PatternEvaluationResult.NotDetected(Type, "Entry-Fenster aktiv aber nicht genügend Orderflow-Kriterien erfüllt", new Dictionary<string, object>(), metHardConditions, metRelConditions);
                }
            }
        }

        // Helper-Methode, um die maximale Anzahl der m?glichen Kriterien zu ermitteln,
        // basierend darauf, welche Schwellenwerte gesetzt sind.
        private int GetPossibleCriteriaCount(OrderflowThresholds thresholds)
        {
            int possible = 0;
            possible++; // Group 1b (either/or inflection) is always checked

            if (_direction == OrderDirections.Buy)
            {
                if (thresholds.ReversalThCvdImpulseLong.HasValue || thresholds.ReversalThAggPressureBreakoutBull.HasValue) possible++; // Group 1a
                if (thresholds.ReversalMaxCounterDeltaShareBear.HasValue) possible++; // Group 2
            }
            else // Short
            {
                if (thresholds.ReversalThCvdImpulseShort.HasValue || thresholds.ReversalThAggPressureBreakoutBear.HasValue) possible++; // Group 1a
                if (thresholds.ReversalMaxCounterDeltaShareBull.HasValue) possible++; // Group 2
            }

            if (thresholds.ReversalThVolBurstZ.HasValue) possible++; // Group 3a
            if (thresholds.ReversalThEfficiency.HasValue) possible++; // Group 3b

            if ((thresholds.ReversalThStackedImbAnyRangeMinDirectional.HasValue && thresholds.ReversalThStackedImbAnyRangeMinDirectional.Value > 0) ||
                (thresholds.ReversalThStackedImbAnchoredRangeMinDirectional.HasValue && thresholds.ReversalThStackedImbAnchoredRangeMinDirectional.Value > 0)) possible++; // Group 4

            return Math.Max(1, possible); // Mindestens 1 m?gliches Kriterium
        }
    }
}

// NEU: Klasse für Range-Bar-Analyse
public static class RangeBarAnalyzer
{
    // KORREKTUR: Statische Variable um den letzten gültigen Reversal-Bar zwischen Aufrufen zu behalten
    private static int? _persistentLastValidReversalAbsoluteBar = null;
    private static int _persistentLastValidReversalCurrentBar = -1;
    private static string _persistentLastValidReversalType = "";  // KORREKTUR: Speichere auch den letzten ReversalType
    
    public static RangeBarAnalysisResult AnalyzeRangeBarPattern(MyNamespace.Strategies.Orderflow.OfFeaturesHistory history, int currentBar, OvSnapshot currentSnapshot, OrderflowThresholds thresholds)
    {
        LoggerHelper.LogInfo("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] 🚪 ENTRY: history={history != null}, currentBar={currentBar}");

        if (history == null || currentBar < 3)
        {
            LoggerHelper.LogInfo("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] ❌ EARLY RETURN: history={history == null}, currentBar={currentBar} < 3");
            return new RangeBarAnalysisResult { IsRangeMarket = false, Reason = "Insufficient history" };
        }

        // Konfigwerte aus thresholds
        var tickTrend = thresholds.RangeBarTrendSizeTicks;
        var tickReversal = thresholds.RangeBarReversalSizeTicks;
        var minConsecutiveTrend = thresholds.RangeBarMinConsecutiveTrendBars;
        var maxAlternatingLength = thresholds.RangeBarMaxAlternatingLength;

        // TickSize aus thresholds, fallback auf 0.25
        decimal tickSize = thresholds.TickSizeDecimal ?? 0.25m;
        if (tickSize <= 0m)
        {
            LoggerHelper.LogDebug("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] DEBUG WARNING: invalid tickSize ({tickSize}). Using fallback 0.25");
            tickSize = 0.25m;
        }

        var recent = new List<(int barsAgo, int absoluteBar, bool isBull, int sizeTicks, decimal sizePrice)>();

        // Wenn wir currentSnapshot als barsAgo==0 nutzen, zählen wir effektiv eine Bar mehr.
        // Daher kann analysisBars bis history.Count (ältere Bars) + 1 (Snapshot) gehen, aber begrenzt auf maxAlternatingLength+2
        var availableBars = history.Count + (currentSnapshot != null ? 1 : 0);
        var analysisBars = Math.Min(maxAlternatingLength + 2, availableBars);
            LoggerHelper.LogDebug("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] DEBUG: history.Count={history.Count}, availableBars={availableBars}, analysisBars={analysisBars}, currentBar={currentBar}");

        for (int barsAgo = 0; barsAgo < analysisBars; barsAgo++)
        {
            OfFeatures features = default;
            decimal openVal, closeVal;
            int absoluteBar;

            if (barsAgo == 0)
            {
                // Für die gerade geschlossene Bar ausschließlich Snapshot verwenden
                if (currentSnapshot == null)
                {
                    LoggerHelper.LogDebug("ReversalBouncePatternEvaluator", "[ReversalBouncePatternEvaluator] DEBUG: currentSnapshot is null for barsAgo=0, skipping");
                    continue;
                }

                openVal = currentSnapshot.Open;
                closeVal = currentSnapshot.Close;
                absoluteBar = currentBar; // currentBar repräsentiert die Snapshot-Bar
                    LoggerHelper.LogDebug("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] DEBUG barsAgo=0 (snapshot): abs={absoluteBar}, Open={openVal}, Close={closeVal}");
            }
            else
            {
                // Für ältere Bars History abfragen, aber nur wenn wirklich nötig
                // KORREKTUR: Überspringe die ersten paar History-Einträge, da sie oft Duplikate des Snapshots sind
                // und verwende nur jeden zweiten Eintrag, um Duplikate zu vermeiden
                int skipEntries = 2; // Erste 2 Einträge überspringen (oft Duplikate)
                int historyIndex = skipEntries + (barsAgo - 1) * 2;
                
                if (!history.TryGetOfFeatures(historyIndex, out features))
                {
                    LoggerHelper.LogDebug("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] DEBUG: history.TryGetOfFeatures({historyIndex}) returned false, skipping");
                    continue;
                }

                openVal = features.Open;
                closeVal = features.Close;
                absoluteBar = features.Bar;
                LoggerHelper.LogDebug("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] DEBUG barsAgo={barsAgo} (historyIndex={historyIndex}): abs={absoluteBar}, Open={openVal}, Close={closeVal}");
            }

            // Duplicate check sollte nicht mehr nötig sein, aber zur Sicherheit behalten
            if (recent.Any(r => r.absoluteBar == absoluteBar))
            {
                LoggerHelper.LogDebug("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] DEBUG: skipping duplicate absoluteBar={absoluteBar} (barsAgo={barsAgo})");
                continue;
            }

            var barSizePrice = Math.Abs(closeVal - openVal);

            // Defensive tickSize check (sollte >0 sein)
            if (tickSize <= 0m)
            {
                LoggerHelper.LogDebug("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] DEBUG ERROR: tickSize invalid ({tickSize}). Using fallback 0.25");
                tickSize = 0.25m;
            }
            var sizeInTicks = (int)decimal.Round(barSizePrice / tickSize, MidpointRounding.AwayFromZero);
            var isBullish = closeVal > openVal;

            recent.Add((barsAgo, absoluteBar, isBullish, sizeInTicks, barSizePrice));
            LoggerHelper.LogDebug("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] DEBUG: added bar abs={absoluteBar}, barsAgo={barsAgo}, sizePrice={barSizePrice}, sizeTicks={sizeInTicks}, bull={isBullish}, recentCount={recent.Count}");
        }

        // Sortiere �lteste -> neueste falls n�tig (wir wollen Reihenfolge von Vergangenheit zur Gegenwart)
        recent = recent.OrderBy(r => r.absoluteBar).ToList();

        if (recent.Count < 3)  // Mindestens 3 Bars für gültiges 3-Bar-Pattern
            return new RangeBarAnalysisResult { 
                IsRangeMarket = false, 
                Reason = $"Not enough bars for pattern analysis (have {recent.Count}, need at least 3)" 
            };

        // Klassifikation - nur die letzten 3 Bars für Pattern-Analyse
        var barTypes = new List<string>();
        var barIndices = new List<int>();
        int reversalCount = 0;
        int barsForPattern = Math.Min(recent.Count, 3); // Immer genau 3 Bars für Pattern

        for (int i = recent.Count - barsForPattern; i < recent.Count; i++)
        {
            var r = recent[i];
            barIndices.Add(r.absoluteBar);

            if (r.sizeTicks == tickTrend)
            {
                barTypes.Add(r.isBull ? "TREND_BULL" : "TREND_BEAR");
            }
            else if (r.sizeTicks == tickReversal)
            {
                barTypes.Add(r.isBull ? "REVERSAL_BULL" : "REVERSAL_BEAR");
                reversalCount++;
            }
            else
            {
                var msg = $"Invalid RangeUS bar size at bar {r.absoluteBar}: sizeTicks={r.sizeTicks} (expected {tickTrend} or {tickReversal})";
                LoggerHelper.LogWarn("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] INVALID RANGEUS INPUT: {msg}");
                return new RangeBarAnalysisResult
                {
                    IsRangeMarket = true,
                    HasValidReversalSetup = false,
                    Reason = msg,
                    BarTypes = barTypes
                };
            }
        }

        LoggerHelper.LogDebug("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] DEBUG BAR TYPES: [{string.Join(", ", barTypes)}]");
        LoggerHelper.LogDebug("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] DEBUG SIZES: [{string.Join(", ", recent.Select(r => r.sizeTicks))}]");
        LoggerHelper.LogDebug("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] DEBUG THRESHOLDS: trend={tickTrend}, reversal={tickReversal}, tickSize={tickSize}");

        // Erkennung gültiger Reversal-Setups
        bool hasValidReversalSetup = false;
        int trendBarsBeforeFirstReversal = 0;
        int consecutiveTrend = 0;
        int? lastReversalAbsoluteBar = null;
        int? lastValidReversalAbsoluteBar = null;

        for (int i = 0; i < barTypes.Count; i++)
        {
            var t = barTypes[i];
            if (t.StartsWith("TREND_"))
            {
                consecutiveTrend++;
            }
            else // REVERSAL_
            {
                var absBar = barIndices[i];

                // Prüfe ob dieses Reversal gültige Trend-Bars davor hat
                if (consecutiveTrend >= minConsecutiveTrend)
                {
                    // Gültiges Reversal gefunden
                    hasValidReversalSetup = true;
                    lastReversalAbsoluteBar = absBar;
                    
                    // DEAKTIVIERT: Alte Logik wird nicht mehr verwendet
                    // Die neue Logik in der Pattern-Erkennung setzt lastValidReversalAbsoluteBar korrekt
                    LoggerHelper.LogInfo("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] INFO VALID REVERSAL: found at i={i}, lastValidReversalAbsoluteBar wird von neuer Logik gesetzt");
                    
                    trendBarsBeforeFirstReversal = consecutiveTrend;
                }
                else
                {
                    // Ungültiges Reversal - ignoriere für gültige Setups aber merke es als letztes Reversal
                    lastReversalAbsoluteBar = absBar;
                    // hasValidReversalSetup und lastValidReversalAbsoluteBar nicht ändern
                }

                consecutiveTrend = 0;
            }
        }

        // Entry-Fenster Logik wird nach der Pattern-Erkennung ausgeführt
        int barsSinceLastValidReversal = 999;

        var isRangeMarket = !hasValidReversalSetup && reversalCount > 0;

        var reason = hasValidReversalSetup
            ? $"Valid reversal setup found (trendBarsBeforeFirstReversal={trendBarsBeforeFirstReversal})"
            : (reversalCount == 0
                ? "No reversal bars detected"
                : (reversalCount == 1
                    ? "Insufficient trend bars before the single reversal"
                    : $"Multiple reversals detected ({reversalCount}) - range market"));

        // NEU: Einfache Richtungskombinationen prüfen
        bool hasValidLongSetup = false;
        bool hasValidShortSetup = false;
        string lastReversalType = "";
        var lastTrendTypes = new List<string>();
        
        // KORREKTUR: Speichere den alten lastValidReversalAbsoluteBar, um ihn im Entry-Fenster zu behalten
        int? previousLastValidReversalAbsoluteBar = _persistentLastValidReversalAbsoluteBar;

        // Prüfe auf LONG-SETUP: TREND_BEAR, TREND_BEAR, REVERSAL_BULL
        LoggerHelper.LogInfo("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] INFO LONG SETUP CHECK: barTypes.Count={barTypes.Count}, barTypes=[{string.Join(", ", barTypes)}]");
        for (int i = 0; i <= barTypes.Count - 3; i++)
        {
            LoggerHelper.LogInfo("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] INFO LONG SETUP CHECK i={i}: checking [{barTypes[i]}, {barTypes[i+1]}, {barTypes[i+2]}]");
            if (barTypes[i] == "TREND_BEAR" && 
                barTypes[i + 1] == "TREND_BEAR" && 
                barTypes[i + 2] == "REVERSAL_BULL")
            {
                LoggerHelper.LogInfo("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] INFO LONG SETUP FOUND at i={i}: hasValidLongSetup=true");
                hasValidLongSetup = true;
                lastReversalType = "REVERSAL_BULL";
                lastTrendTypes = new List<string> { "TREND_BEAR", "TREND_BEAR" };
                
                // KORREKT: Setze lastValidReversalAbsoluteBar für Entry-Fenster NUR bei neuem gültigen Pattern
                lastValidReversalAbsoluteBar = barIndices[i + 2];  // REVERSAL_BULL
                _persistentLastValidReversalAbsoluteBar = lastValidReversalAbsoluteBar;
                _persistentLastValidReversalCurrentBar = currentBar;
                _persistentLastValidReversalType = "REVERSAL_BULL";  // KORREKTUR: Speichere auch den ReversalType
                LoggerHelper.LogInfo("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] INFO LONG SETUP: lastValidReversalAbsoluteBar={lastValidReversalAbsoluteBar}, lastReversalType=REVERSAL_BULL");
                break;
            }
        }
        LoggerHelper.LogInfo("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] INFO LONG SETUP RESULT: hasValidLongSetup={hasValidLongSetup}");

        // Prüfe auf SHORT-SETUP: TREND_BULL, TREND_BULL, REVERSAL_BEAR
        LoggerHelper.LogInfo("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] INFO SHORT SETUP CHECK: barTypes.Count={barTypes.Count}, barTypes=[{string.Join(", ", barTypes)}]");
        for (int i = 0; i <= barTypes.Count - 3; i++)
        {
            LoggerHelper.LogInfo("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] INFO SHORT SETUP CHECK i={i}: checking [{barTypes[i]}, {barTypes[i+1]}, {barTypes[i+2]}]");
            if (barTypes[i] == "TREND_BULL" && 
                barTypes[i + 1] == "TREND_BULL" && 
                barTypes[i + 2] == "REVERSAL_BEAR")
            {
                LoggerHelper.LogInfo("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] INFO SHORT SETUP FOUND at i={i}: hasValidShortSetup=true");
                hasValidShortSetup = true;
                lastReversalType = "REVERSAL_BEAR";
                lastTrendTypes = new List<string> { "TREND_BULL", "TREND_BULL" };
                
                // KORREKT: Setze lastValidReversalAbsoluteBar für Entry-Fenster NUR bei neuem gültigen Pattern
                lastValidReversalAbsoluteBar = barIndices[i + 2];  // REVERSAL_BEAR
                _persistentLastValidReversalAbsoluteBar = lastValidReversalAbsoluteBar;
                _persistentLastValidReversalCurrentBar = currentBar;
                _persistentLastValidReversalType = "REVERSAL_BEAR";  // KORREKTUR: Speichere auch den ReversalType
                LoggerHelper.LogInfo("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] INFO SHORT SETUP: lastValidReversalAbsoluteBar={lastValidReversalAbsoluteBar}, lastReversalType=REVERSAL_BEAR");
                break;
            }
        }
        LoggerHelper.LogInfo("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] INFO SHORT SETUP RESULT: hasValidShortSetup={hasValidShortSetup}");

        // Entry-Fenster Logik wird nach der Pattern-Erkennung ausgeführt
        if (hasValidLongSetup || hasValidShortSetup)
        {
            hasValidReversalSetup = true;
        }
        
        // KORREKTUR: Wenn kein neues Pattern gefunden, aber wir waren im Entry-Fenster, behalte den alten lastValidReversalAbsoluteBar UND lastReversalType
        if (!hasValidLongSetup && !hasValidShortSetup && previousLastValidReversalAbsoluteBar.HasValue)
        {
            lastValidReversalAbsoluteBar = previousLastValidReversalAbsoluteBar;
            hasValidReversalSetup = true; // KORREKTUR: Behalte auch hasValidReversalSetup=true im Entry-Fenster
            
            // KORREKTUR: Behalte auch den letzten ReversalType im Entry-Fenster
            if (_persistentLastValidReversalType != null)
            {
                lastReversalType = _persistentLastValidReversalType;
                LoggerHelper.LogInfo("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] ℹ️ ENTRY-FENSTER: Kein neues Pattern gefunden, behalte vorheriges Reversal-Bar={lastValidReversalAbsoluteBar}, setze hasValidReversalSetup=true, lastReversalType={lastReversalType}");
            }
            else
            {
                LoggerHelper.LogInfo("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] ℹ️ ENTRY-FENSTER: Kein neues Pattern gefunden, behalte vorheriges Reversal-Bar={lastValidReversalAbsoluteBar}, setze hasValidReversalSetup=true, lastReversalType=EMPTY");
            }
        }
        
        // KORREKTUR: Setze die statische Variable zurück, wenn wir zu weit vom letzten gültigen Reversal entfernt sind
        if (lastValidReversalAbsoluteBar.HasValue)
        {
            int barsSinceReversal = currentBar - lastValidReversalAbsoluteBar.Value;
            int maxBarsAfterReversalThreshold = thresholds.RangeBarMaxBarsAfterReversal;
            if (barsSinceReversal > maxBarsAfterReversalThreshold)
            {
                _persistentLastValidReversalAbsoluteBar = null;
                _persistentLastValidReversalCurrentBar = -1;
                _persistentLastValidReversalType = "";  // KORREKTUR: Setze auch den ReversalType zurück
            LoggerHelper.LogInfo("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] 🔄 ENTRY-FENSTER: Persistenten Status zurückgesetzt - Bars seit Reversal={barsSinceReversal} > Max-Bars={maxBarsAfterReversalThreshold}");
            }
        }
        if (hasValidReversalSetup && lastValidReversalAbsoluteBar.HasValue)
        {
            // NEU: Entry erst nach dem gültigen Reversal-Bar erlauben (mindestens 1 Bar danach)
            barsSinceLastValidReversal = currentBar - lastValidReversalAbsoluteBar.Value;
            LoggerHelper.LogInfo("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] ℹ️ ENTRY-FENSTER: Aktueller Bar={currentBar}, Reversal-Bar={lastValidReversalAbsoluteBar}, Bars seit Reversal={barsSinceLastValidReversal}");
            
            // KORREKT: Wenn wir auf dem Entry-Bar sind (barsSinceLastValidReversal = 0), ist Entry erlaubt
            // Die Logik wurde korrigiert: lastValidReversalAbsoluteBar zeigt auf den Entry-Bar
            if (barsSinceLastValidReversal < 0)
            {
                // Vor dem Entry-Bar - Entry nicht erlaubt
                LoggerHelper.LogInfo("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] ℹ️ ENTRY-FENSTER: Bars seit Reversal={barsSinceLastValidReversal} < 0, setze auf 999 (vor Entry-Fenster)");
                barsSinceLastValidReversal = 999; // Außerhalb des Fensters
            }
            else
            {
                LoggerHelper.LogInfo("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] ✅ ENTRY-FENSTER: Bars seit Reversal={barsSinceLastValidReversal} >= 0, Entry erlaubt");
            }
        }
        else
        {
            LoggerHelper.LogInfo("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] ℹ️ ENTRY-FENSTER: Gültiges Setup={hasValidReversalSetup}, Reversal-Bar={lastValidReversalAbsoluteBar}");
        }

        // KORREKTUR: Behalte gültige Setups für Entry-Fenster, auch wenn sich das aktuelle Pattern ändert
        // Wenn wir ein gültiges Setup hatten und noch im Entry-Fenster sind, behalte HasValidReversalSetup=true
        var maxBarsAfterReversal = thresholds.RangeBarMaxBarsAfterReversal;
        if (barsSinceLastValidReversal >= 0 && barsSinceLastValidReversal <= maxBarsAfterReversal)
        {
            // Wir hatten ein gültiges Setup und sind noch im Entry-Fenster
            if (!hasValidReversalSetup && lastValidReversalAbsoluteBar.HasValue)
            {
                // Pattern hat sich geändert, aber wir sind noch im Entry-Fenster
                hasValidReversalSetup = true;
                LoggerHelper.LogInfo("ReversalBouncePatternEvaluator", $"[ReversalBouncePatternEvaluator] 🔄 ENTRY-FENSTER: Pattern geändert aber immer noch im Entry-Fenster - setze hasValidReversalSetup=true");
            }
        }

        return new RangeBarAnalysisResult
        {
            IsRangeMarket = isRangeMarket,
            HasValidReversalSetup = hasValidReversalSetup,
            ConsecutiveTrendBars = trendBarsBeforeFirstReversal,
            BarTypes = barTypes.Take(maxAlternatingLength).ToList(),
            Reason = reason,
            LastReversalBarIndex = lastReversalAbsoluteBar,
            BarsSinceLastReversal = barsSinceLastValidReversal,
            BarsSinceLastValidReversal = barsSinceLastValidReversal,
            
            // NEU: Spezifische Richtungsinformationen
            HasValidLongReversalSetup = hasValidLongSetup,
            HasValidShortReversalSetup = hasValidShortSetup,
            LastReversalType = lastReversalType,
            LastTrendTypes = lastTrendTypes
        };
    }

}

public class RangeBarAnalysisResult
{
    public bool IsRangeMarket { get; set; }
    public bool HasValidReversalSetup { get; set; }
    public int ConsecutiveTrendBars { get; set; }
    public List<string> BarTypes { get; set; } = new();
    public string Reason { get; set; } = "";

    // NEU: Position des letzten Reversals (f?r Entry-Fenster)
    public int? LastReversalBarIndex { get; set; }
    public int BarsSinceLastReversal { get; set; } = 999; // Alte Variable f?r Kompatibilit?t
    public int BarsSinceLastValidReversal { get; set; } = 999; // NEU: F?r Entry-Fenster
    
    // NEU: Spezifische Richtungsinformationen
    public bool HasValidLongReversalSetup { get; set; }
    public bool HasValidShortReversalSetup { get; set; }
    public string LastReversalType { get; set; } = ""; // "REVERSAL_BULL" oder "REVERSAL_BEAR"
    public List<string> LastTrendTypes { get; set; } = new(); // Die Trend-Typen vor dem Reversal
}


