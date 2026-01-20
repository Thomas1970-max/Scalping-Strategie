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
        private int? _lastValidReversalBarIndex;
        private OrderDirections? _lastValidReversalDirection;
        private string _lastValidReversalType = "";
        private int? _lastClosedEntryWindowReversalBarIndex;

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

        public bool TryGetLastValidReversalInfo(out int reversalBarIndex, out OrderDirections? direction)
        {
            if (_lastValidReversalBarIndex.HasValue)
            {
                reversalBarIndex = _lastValidReversalBarIndex.Value;
                direction = _lastValidReversalDirection;
                return true;
            }

            reversalBarIndex = -1;
            direction = null;
            return false;
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

        private static string BuildOrderflowThresholdLog(
            OrderDirections direction,
            OvSnapshot snapshot,
            OrderflowThresholds thresholds)
        {
            if (snapshot == null || thresholds == null)
                return "[ReversalBounceEvaluator] Orderflow-Schwellen: n/a (Snapshot oder Thresholds fehlen)";

            if (direction == OrderDirections.Buy)
            {
                var cvdTh = thresholds.ReversalThCvdImpulseLong;
                var aggTh = thresholds.ReversalThAggPressureBreakoutBull;
                var minCvd = thresholds.ThCvdImpulseMinForLongReversal;
                var imbMin = thresholds.ReversalThImbalanceScoreMinLong;
                return "[ReversalBounceEvaluator] ✅ ORDERFLOW-SCHWELLEN (Long): " +
                       $"CVD {FormatMetValue(snapshot.CvdImpulse)} {(cvdTh.HasValue ? $"> {FormatMetValue(cvdTh.Value)}" : "(keine Schwelle)")}, " +
                       $"AggPressure {FormatMetValue(snapshot.AggPressure)} {(aggTh.HasValue ? $"> {FormatMetValue(aggTh.Value)}" : "(keine Schwelle)")}, " +
                       $"MinCVD {FormatMetValue(snapshot.CvdImpulse)} {(minCvd.HasValue ? $">= {FormatMetValue(minCvd.Value)}" : "(keine Schwelle)")}, " +
                       $"VolBurstZ {FormatMetValue(snapshot.VolBurstZ)} {(thresholds.ThVolBurstZ.HasValue ? $"> {FormatMetValue(thresholds.ThVolBurstZ.Value)}" : "(keine Schwelle)")}, " +
                       $"Eff {FormatMetValue(snapshot.Efficiency)} {(thresholds.ThEfficiency.HasValue ? $"> {FormatMetValue(thresholds.ThEfficiency.Value)}" : "(keine Schwelle)")}, " +
                       $"ImbScore {FormatMetValue(snapshot.ImbalanceScore, "F3")} {(imbMin.HasValue ? $">= {FormatMetValue(imbMin.Value, "F3")}" : "(keine Schwelle)")}";
            }

            var cvdThShort = thresholds.ReversalThCvdImpulseShort;
            var aggThShort = thresholds.ReversalThAggPressureBreakoutBear;
            var imbMax = thresholds.ReversalThImbalanceScoreMaxShort;
            return "[ReversalBounceEvaluator] ✅ ORDERFLOW-SCHWELLEN (Short): " +
                   $"CVD {FormatMetValue(snapshot.CvdImpulse)} {(cvdThShort.HasValue ? $"< {FormatMetValue(cvdThShort.Value)}" : "(keine Schwelle)")}, " +
                   $"AggPressure {FormatMetValue(snapshot.AggPressure)} {(aggThShort.HasValue ? $"< {FormatMetValue(aggThShort.Value)}" : "(keine Schwelle)")}, " +
                   $"VolBurstZ {FormatMetValue(snapshot.VolBurstZ)} {(thresholds.ThVolBurstZ.HasValue ? $"> {FormatMetValue(thresholds.ThVolBurstZ.Value)}" : "(keine Schwelle)")}, " +
                   $"Eff {FormatMetValue(snapshot.Efficiency)} {(thresholds.ThEfficiency.HasValue ? $"> {FormatMetValue(thresholds.ThEfficiency.Value)}" : "(keine Schwelle)")}, " +
                   $"ImbScore {FormatMetValue(snapshot.ImbalanceScore, "F3")} {(imbMax.HasValue ? $"<= {FormatMetValue(imbMax.Value, "F3")}" : "(keine Schwelle)")}";
        }

        private static string BuildCriteriaStatusLog(
            List<string> reasons,
            List<EvaluatedConditionDetail> metHard,
            List<EvaluatedConditionDetail> metRel,
            List<EvaluatedConditionDetail> diagnostics)
        {
            var hardSummary = metHard.Any()
                ? string.Join(" | ", metHard.Select(h => $"✅ {h.ToShortString()}"))
                : "🚫 metHard: keine";

            var relSummary = metRel.Any()
                ? string.Join(" | ", metRel.Select(r => $"✅ {r.ToShortString()}"))
                : "🚫 metRel: keine";

            var failedReasons = reasons
                .Where(r => r.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)
                            || r.StartsWith("BLOCKED", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var failedSummary = failedReasons.Any()
                ? string.Join(" | ", failedReasons.Select(r => $"🚫 {r}"))
                : "🚫 keine";

            var diagSummary = diagnostics.Any()
                ? string.Join(" | ", diagnostics.Select(d => $"✅ {d.ToShortString()}"))
                : null;

            return "[ReversalBounceEvaluator] Kriterien: " +
                   $"{hardSummary} | {relSummary} | Fehlend: {failedSummary}" +
                   (diagSummary != null ? $" | Diagnostics: {diagSummary}" : string.Empty);
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
            LoggerHelper.LogDebug(_loggerSource, $"[ReversalBounceEvaluator] Start: Richtung={_direction}, Bar={features?.Bar}");

            // NEU: 1. Effektive Thresholds bestimmen (übergebene Thresholds haben Vorrang)
            var effectiveThresholds = thresholds ?? _externalThresholds ?? _thresholdsProvider?.Invoke() ?? GlobalThresholds;
            if (effectiveThresholds == null)
                effectiveThresholds = new OrderflowThresholds();

            thresholds = effectiveThresholds;

            if (thresholds != null)
            {
                LoggerHelper.LogInfo(
                    _loggerSource,
                    $"[ReversalBounceEvaluator] Schwellenquelle: Übergabe (Dir={_direction}, " +
                    $"ReversalThCvdImpulseLong={FormatMetValue(thresholds.ReversalThCvdImpulseLong)}, " +
                    $"ReversalThCvdImpulseShort={FormatMetValue(thresholds.ReversalThCvdImpulseShort)})");
            }
            else if (_externalThresholds != null)
            {
                LoggerHelper.LogInfo(
                    _loggerSource,
                    $"[ReversalBounceEvaluator] Schwellenquelle: Extern (Dir={_direction}, " +
                    $"ReversalThCvdImpulseLong={FormatMetValue(_externalThresholds?.ReversalThCvdImpulseLong)}, " +
                    $"ReversalThCvdImpulseShort={FormatMetValue(_externalThresholds?.ReversalThCvdImpulseShort)})");
            }
            else if (_thresholdsProvider != null)
            {
                var dynamicThresholds = _thresholdsProvider.Invoke();
                LoggerHelper.LogInfo(
                    _loggerSource,
                    $"[ReversalBounceEvaluator] Schwellenquelle: Provider (Dir={_direction}, " +
                    $"ReversalThCvdImpulseLong={FormatMetValue(dynamicThresholds?.ReversalThCvdImpulseLong)}, " +
                    $"ReversalThCvdImpulseShort={FormatMetValue(dynamicThresholds?.ReversalThCvdImpulseShort)})");
            }
            else if (GlobalThresholds != null)
            {
                LoggerHelper.LogInfo(
                    _loggerSource,
                    $"[ReversalBounceEvaluator] Schwellenquelle: Global (Dir={_direction}, " +
                    $"ReversalThCvdImpulseLong={FormatMetValue(GlobalThresholds?.ReversalThCvdImpulseLong)}, " +
                    $"ReversalThCvdImpulseShort={FormatMetValue(GlobalThresholds?.ReversalThCvdImpulseShort)})");
            }

            // NEU: 2. Range-Bar Pattern-Erkennung (universell)
            var rangeAnalysis = RangeBarAnalyzer.AnalyzeRangeBarPattern(
                history, features.Bar, currentSnapshot, effectiveThresholds, RangeBarPatternDefinitions.ReversalBounce, _loggerSource);
            
            LoggerHelper.LogInfo(_loggerSource, $"[ReversalBounceEvaluator] Range-Bar Analyse: gültiges Setup={rangeAnalysis.HasValidReversalSetup}, Long={rangeAnalysis.HasValidLongReversalSetup}, Short={rangeAnalysis.HasValidShortReversalSetup}");
            var barsSinceReversalLog = rangeAnalysis.LastReversalBarIndex.HasValue
                ? (features.Bar - rangeAnalysis.LastReversalBarIndex.Value)
                : (int?)null;
            LoggerHelper.LogInfo(_loggerSource, $"[ReversalBounceEvaluator] Pattern-Details: Bars=[{string.Join(", ", rangeAnalysis.BarTypes)}], Reversal={rangeAnalysis.LastReversalType}, Bars seit Reversal={barsSinceReversalLog}");

            // NEU: 2. Pattern-Validierung und Entry-Fenster im Evaluator verwalten
            if (rangeAnalysis.HasValidReversalSetup && rangeAnalysis.LastReversalBarIndex.HasValue &&
                rangeAnalysis.LastReversalBarIndex.Value == features.Bar &&
                (!_lastClosedEntryWindowReversalBarIndex.HasValue || rangeAnalysis.LastReversalBarIndex.Value > _lastClosedEntryWindowReversalBarIndex.Value))
            {
                _lastValidReversalBarIndex = rangeAnalysis.LastReversalBarIndex;
                _lastValidReversalDirection = rangeAnalysis.HasValidLongReversalSetup
                    ? OrderDirections.Buy
                    : OrderDirections.Sell;
                _lastValidReversalType = rangeAnalysis.LastReversalType;
            }

            if (_lastValidReversalBarIndex.HasValue && _lastValidReversalBarIndex.Value == features.Bar)
            {
                var entryDelayReasons = new List<string>
                {
                    $"BLOCKED (Entry Window): Entry-Fenster startet erst nach dem Reversal-Bar. Aktuell Bar={features.Bar}, Reversal-Bar={_lastValidReversalBarIndex.Value}."
                };
                LoggerHelper.LogInfo(_loggerSource, $"[ReversalBounceEvaluator] 🕒 PATTERN OK – ENTRY-FENSTER WARTET: {entryDelayReasons[0]}");

                return PatternEvaluationResult.NotDetected(
                    Type,
                    string.Join(" | ", entryDelayReasons),
                    new Dictionary<string, object>(),
                    new List<EvaluatedConditionDetail>(),
                    new List<EvaluatedConditionDetail>(),
                    new List<EvaluatedConditionDetail>()
                );
            }

            if (_lastValidReversalBarIndex.HasValue &&
                !string.IsNullOrWhiteSpace(rangeAnalysis.CurrentBarType) &&
                rangeAnalysis.CurrentBarType.StartsWith("REVERSAL_", StringComparison.Ordinal))
            {
                var reversalWindowReasons = new List<string>
                {
                    $"BLOCKED (Entry Window): Reversal-Bar erkannt ({rangeAnalysis.CurrentBarType}). Entry-Fenster schließt sofort. Bar={features.Bar}."
                };
                LoggerHelper.LogInfo(_loggerSource, $"[ReversalBounceEvaluator] Entry-Fenster geschlossen: {reversalWindowReasons[0]}");

                _lastClosedEntryWindowReversalBarIndex = features.Bar;
                _lastValidReversalBarIndex = null;
                _lastValidReversalDirection = null;
                _lastValidReversalType = "";

                return PatternEvaluationResult.NotDetected(
                    Type,
                    string.Join(" | ", reversalWindowReasons),
                    new Dictionary<string, object>(),
                    new List<EvaluatedConditionDetail>(),
                    new List<EvaluatedConditionDetail>(),
                    new List<EvaluatedConditionDetail>()
                );
            }

            if (!_lastValidReversalBarIndex.HasValue)
            {
                var patternReasons = new List<string> {
                    $"BLOCKED (Pattern): Kein gültiges Reversal-Setup gefunden. Pattern: [{string.Join(", ", rangeAnalysis.BarTypes)}]"
                };
                LoggerHelper.LogWarn(_loggerSource, $"[ReversalBounceEvaluator] Pattern blockiert: {patternReasons[0]}");

                return PatternEvaluationResult.NotDetected(
                    Type,
                    string.Join(" | ", patternReasons),
                    new Dictionary<string, object>(),
                    new List<EvaluatedConditionDetail>(),
                    new List<EvaluatedConditionDetail>(),
                    new List<EvaluatedConditionDetail>()
                );
            }

            int barsSinceLastValidReversal = features.Bar - _lastValidReversalBarIndex.Value;
            if (barsSinceLastValidReversal == 0)
                barsSinceLastValidReversal = 999;

            int maxBarsAfterReversal = thresholds.RangeBarMaxBarsAfterReversal;
            if (barsSinceLastValidReversal > maxBarsAfterReversal)
            {
                var entryReasons = new List<string> {
                    $"BLOCKED (Entry Window): Entry-Fenster abgelaufen. Bars seit Reversal={barsSinceLastValidReversal} > Max={maxBarsAfterReversal}"
                };
                LoggerHelper.LogWarn(_loggerSource, $"[ReversalBounceEvaluator] Entry-Fenster abgelaufen: {entryReasons[0]}");

                _lastValidReversalBarIndex = null;
                _lastValidReversalDirection = null;
                _lastValidReversalType = "";

                return PatternEvaluationResult.NotDetected(
                    Type,
                    string.Join(" | ", entryReasons),
                    new Dictionary<string, object>(),
                    new List<EvaluatedConditionDetail>(),
                    new List<EvaluatedConditionDetail>(),
                    new List<EvaluatedConditionDetail>()
                );
            }

            // NEU: 3. Direction aus Pattern bestimmen
            var detectedDirection = _lastValidReversalDirection ?? OrderDirections.Buy;
            LoggerHelper.LogInfo(_loggerSource, $"[ReversalBounceEvaluator] ✅ ENTRY-FENSTER AKTIV: Pattern={_lastValidReversalType}, Richtung={detectedDirection}, Bars seit Reversal={barsSinceLastValidReversal}");

            if (detectedDirection != _direction)
            {
                var directionReasons = new List<string> {
                    $"BLOCKED (Direction Mismatch): Pattern-Direction={detectedDirection}, Evaluator-Direction={_direction}"
                };
                LoggerHelper.LogDebug(_loggerSource, $"[ReversalBounceEvaluator][Eval={_direction}] Richtung passt nicht: {directionReasons[0]}");

                return PatternEvaluationResult.NotDetected(
                    Type,
                    string.Join(" | ", directionReasons),
                    new Dictionary<string, object>(),
                    new List<EvaluatedConditionDetail>(),
                    new List<EvaluatedConditionDetail>(),
                    new List<EvaluatedConditionDetail>()
                );
            }

            // BESTEHEND: 4. Orderflow-Kriterien (nur im Entry-Fenster und bei passender Direction)
            LoggerHelper.LogInfo(_loggerSource, $"[ReversalBounceEvaluator] Orderflow-Prüfung startet für Richtung {detectedDirection}");
            LoggerHelper.LogInfo(_loggerSource, BuildOrderflowThresholdLog(detectedDirection, currentSnapshot, effectiveThresholds));

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


                LoggerHelper.LogDebug(_loggerSource, "[ReversalBounceEvaluator] Pfad: Long");

                // **NEUES HARTES KRITERIUM: Mindest-CVD-Impuls für Long Reversal**
                // Wenn dieser Schwellenwert gesetzt ist und der aktuelle CvdImpulse zu negativ ist,
                // wird das Muster sofort als NICHT erkannt zurückgegeben.
                if (thresholds.ThCvdImpulseMinForLongReversal.HasValue &&
                    currentSnapshot.CvdImpulse < thresholds.ThCvdImpulseMinForLongReversal.Value)
                {
                    var reasonText = $"BLOCKED (Hard Criterium): CvdImpulse ({currentSnapshot.CvdImpulse:F2}) ist zu negativ für ein Long Reversal (Min. erlaubt: {thresholds.ThCvdImpulseMinForLongReversal.Value:F2}).";
                    reasons.Add(reasonText);
                    LoggerHelper.LogWarn(_loggerSource, $"[ReversalBounceEvaluator] Harter Block Long: {reasonText}");

                    var diagDetail = new EvaluatedConditionDetail(
                        "CvdImpulseTooNegativeForLongReversal",
                        FormatMetValue(currentSnapshot.CvdImpulse, "F2"),
                        $"< {FormatMetValue(thresholds.ThCvdImpulseMinForLongReversal.Value, "F2")}"
                    );
                    metDiagnosticConditions.Add(diagDetail);

                    return PatternEvaluationResult.NotDetected(
                        Type,
                        string.Join(" | ", reasons),
                        new Dictionary<string, object>(),
                        metHardConditions,
                        metRelConditions,
                        metDiagnosticConditions
                    );
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
                    LoggerHelper.LogDebug(_loggerSource, $"[ReversalBounceEvaluator] MetHard erzeugt: {detail.ToShortString()}");
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
                        $"> {FormatMetValue(thresholds.ReversalThAggPressureBreakoutBull.Value, "F2") }"
                    );
                    metHardConditions.Add(detail);
                    LoggerHelper.LogDebug(_loggerSource, $"[ReversalBounceEvaluator] MetHard erzeugt: {detail.ToShortString()}");
                }

                if (aggressiveCvdPresent || aggressivePressurePresent)
                {
                    metCriteriaCount++;
                }
                else
                {
                    reasons.Add("FAIL: Unzureichender CvdImpulse oder AggPressure.");
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
                        LoggerHelper.LogDebug(_loggerSource, $"[ReversalBounceEvaluator] MetHard erzeugt: {detail.ToShortString()}");
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
                        LoggerHelper.LogDebug(_loggerSource, $"[ReversalBounceEvaluator] MetHard erzeugt: {detail.ToShortString()}");
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
                        LoggerHelper.LogDebug(_loggerSource, $"[ReversalBounceEvaluator] MetHard erzeugt: {detail.ToShortString()}");
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
                        LoggerHelper.LogDebug(_loggerSource, $"[ReversalBounceEvaluator] MetRel erzeugt: {detail.ToShortString()}");
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

                // Kriterien-Gruppe 5: Imbalance Score (Trend-Bar Bestätigung)
                if (thresholds.ReversalThImbalanceScoreMinLong.HasValue)
                {
                    if (currentSnapshot.ImbalanceScore >= thresholds.ReversalThImbalanceScoreMinLong.Value)
                    {
                        metCriteriaCount++;
                        reasons.Add($"ImbalanceScore ({currentSnapshot.ImbalanceScore:F3}) >= ThImbScoreMinLong ({thresholds.ReversalThImbalanceScoreMinLong.Value:F3})");

                        var detail = new EvaluatedConditionDetail(
                            "ImbalanceScoreMinLong",
                            FormatMetValue(currentSnapshot.ImbalanceScore, "F3"),
                            $">= {FormatMetValue(thresholds.ReversalThImbalanceScoreMinLong.Value, "F3") }"
                        );
                        metRelConditions.Add(detail);
                        LoggerHelper.LogDebug(_loggerSource, $"[ReversalBounceEvaluator] MetRel erzeugt: {detail.ToShortString()}");
                    }
                    else
                    {
                        reasons.Add("ImbalanceScore zu schwach für Long (Trend-Bar Bestätigung fehlt).");
                    }
                }
                else
                {
                    reasons.Add("HINWEIS: ImbalanceScore-Threshold (Long) nicht gesetzt, Kriterium nicht geprüft.");
                }
            }
            else // Direction == OrderDirections.Sell
            {
                // ####################################################################
                // #                       SHORT EVALUATION                           #
                // ####################################################################

                LoggerHelper.LogDebug(_loggerSource, "[ReversalBounceEvaluator] Pfad: Short");

                // Kriterien-Gruppe 1: Aggressive Verkaufskraft
                // ----------------------------------------------------
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
                        LoggerHelper.LogDebug(_loggerSource, $"[ReversalBounceEvaluator] MetRel erzeugt: {detail.ToShortString()}");
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
                LoggerHelper.LogInfo(_loggerSource, $"[ReversalBounceEvaluator] ReversalBounceMuster erkannt: {Type}, confidence={confidence:F2}, met={metCriteriaCount}");
                LoggerHelper.LogInfo(_loggerSource, BuildCriteriaStatusLog(reasons, metHardConditions, metRelConditions, metDiagnosticConditions));

              
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
                    $"[ReversalBounceEvaluator] Erkannt: {Type} ({Direction}) {finalMetHardPart}, metRel={relCount}: {relSummary}");

                return PatternEvaluationResult.Detected(Type, confidence, reasons, new Dictionary<string, object>(), metHardConditions, metRelConditions);
            }
            else
            {
                reasons.Add($"ReversakBounceMuster NICHT erkannt: {metCriteriaCount} von {minSignals} erforderlichen Signalen wurden erfüllt.");
                LoggerHelper.LogInfo(
                    _loggerSource,
                    $"[ReversalBounceEvaluator] NICHT erkannt (Dir={Direction}) met={metCriteriaCount}/{minSignals}, " +
                    $"hard={metHardConditions.Count}, rel={metRelConditions.Count}. Gründe: {string.Join(" | ", reasons)}"
                );
                LoggerHelper.LogInfo(_loggerSource, BuildCriteriaStatusLog(reasons, metHardConditions, metRelConditions, metDiagnosticConditions));
                LoggerHelper.LogDebug(_loggerSource, $"[ReversalBounceEvaluator] Muster NICHT erkannt: {Type}. Gründe: {string.Join(" | ", reasons)}");
                LoggerHelper.LogDebug(_loggerSource, $"[ReversalBounceEvaluator] Ergebnis NICHT erkannt. metHard={metHardConditions.Count}, metRel={metRelConditions.Count}. Details: " +
                (metHardConditions.Any() ? string.Join(" | ", metHardConditions.Select(h => h.ToShortString())) : "(no hard)") + " / " +
                (metRelConditions.Any() ? string.Join(" | ", metRelConditions.Select(r => r.ToShortString())) : "(no rel)"));

                
                // Optional: auch hier Diagnostics in Debug ausgeben, damit nicht-metende Diagnostics sichtbar bleiben
                if (metDiagnosticConditions.Any())
                {
                    LoggerHelper.LogDebug(_loggerSource,
                        $"[ReversalBounceEvaluator] Diagnostics ({metDiagnosticConditions.Count}): " +
                        string.Join(" | ", metDiagnosticConditions.Select(d => d.ToShortString())));
                }

                return PatternEvaluationResult.NotDetected(Type, string.Join(" | ", reasons), new Dictionary<string, object>(), metHardConditions, metRelConditions);
            }
        }

        // Helper-Methode, um die maximale Anzahl der möglichen Kriterien zu ermitteln,
        // basierend darauf, welche Schwellenwerte gesetzt sind.
        private int GetPossibleCriteriaCount(OrderflowThresholds thresholds)
        {
            int possible = 0;
            // possible++; // Inflection entfernt - kein Zählen mehr nötig

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

            if ((_direction == OrderDirections.Buy && thresholds.ReversalThImbalanceScoreMinLong.HasValue) ||
                (_direction == OrderDirections.Sell && thresholds.ReversalThImbalanceScoreMaxShort.HasValue)) possible++; // Group 5

            return Math.Max(1, possible); // Mindestens 1 mögliches Kriterium
        }
    }
}
