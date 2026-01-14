using System;
using System.Collections.Generic;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using Utils.Common.Logging;
using static MyNamespace.Strategies.Goldfluss3_3;

namespace MyNamespace.Strategies.Orderflow
{
    /// <summary>
    /// Implementiert die Logik zur Erkennung von Breakout-Mustern.
    /// Passt sich der bereitgestellten IPatternEvaluator-Schnittstelle an.
    /// </summary>
    public class BreakoutPatternEvaluator : IPatternEvaluator
    {
        private readonly OrderDirections _direction;
        private readonly OrderflowPatternType _patternType;
        private readonly ILoggerSource _loggerSource;

        public OrderflowPatternType Type => _patternType;
        public OrderDirections Direction => _direction;

        public BreakoutPatternEvaluator(OrderDirections direction, ILoggerSource loggerSource = null)
        {
            _direction = direction;
            // Der Typ ist spezifisch für Breakouts
            _patternType = direction == OrderDirections.Buy
                ? OrderflowPatternType.PotentialLongBreakout
                : OrderflowPatternType.PotentialShortBreakout;

            _loggerSource = loggerSource;
        }
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


        /// <summary>
        /// Evaluiert, ob ein Breakout-Muster vorliegt.
        /// (Die tatsächliche Logik wird in einem späteren Schritt implementiert, ähnlich wie bei ReversalBouncePatternEvaluator)
        /// </summary>
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
            LoggerHelper.LogDebug(_loggerSource, $"[BreakoutPatternEvaluator] BreakoutPatternEvaluator.Evaluate: start direction={_direction}, bar={features?.Bar}");

            var reasons = new List<string>();
            var metHardConditions = new List<EvaluatedConditionDetail>();
            var metRelConditions = new List<EvaluatedConditionDetail>();
            var metDiagnosticConditions = new List<EvaluatedConditionDetail>();
            int metCriteriaCount = 0;

            // Defensive checks
            if (currentSnapshot == null)
            {
                LoggerHelper.LogWarn(_loggerSource, "[BreakoutPatternEvaluator] Evaluate: currentSnapshot is null -> abort");
                reasons.Add("currentSnapshot is null");
                return PatternEvaluationResult.NotDetected(Type, string.Join(" | ", reasons));
            }

            if (thresholds == null)
            {
                LoggerHelper.LogWarn(_loggerSource, "[BreakoutPatternEvaluator] Evaluate: thresholds is null -> abort");
                reasons.Add("thresholds not provided");
                return PatternEvaluationResult.NotDetected(Type, string.Join(" | ", reasons));
            }

            LoggerHelper.LogDebug(_loggerSource, $"[BreakoutPatternEvaluator] Evaluate: CvdImpulse={FormatMetValue(currentSnapshot.CvdImpulse)}, AggPressure={FormatMetValue(currentSnapshot.AggPressure)}, VolBurstZ={FormatMetValue(currentSnapshot.VolBurstZ)}, Efficiency={FormatMetValue(currentSnapshot.Efficiency)}");

            // --- CRITERIA: directional CVD / AggPressure (hard) ---
            bool directionalPressureMet = false;
            if (_direction == OrderDirections.Buy)
            {
                if (thresholds.ThCvdImpulseLong.HasValue && currentSnapshot.CvdImpulse > thresholds.ThCvdImpulseLong.Value)
                {
                    directionalPressureMet = true;
                    metCriteriaCount++;
                    reasons.Add($"CvdImpulse {currentSnapshot.CvdImpulse:F2} > ThCvdImpulseLong {thresholds.ThCvdImpulseLong.Value:F2}");
                    metHardConditions.Add(new EvaluatedConditionDetail(
                        "CvdImpulseAboveThLong",
                        FormatMetValue(currentSnapshot.CvdImpulse),
                        $"> {FormatMetValue(thresholds.ThCvdImpulseLong.Value)}"));
                    LoggerHelper.LogDebug(_loggerSource, "[BreakoutPatternEvaluator] Long directional CvdImpulse matched");
                }

                if (thresholds.ThAggPressureBreakoutBull.HasValue && currentSnapshot.AggPressure > thresholds.ThAggPressureBreakoutBull.Value)
                {
                    directionalPressureMet = true;
                    metCriteriaCount++;
                    reasons.Add($"AggPressure {currentSnapshot.AggPressure:F2} > ThAggPressureBreakoutBull {thresholds.ThAggPressureBreakoutBull.Value:F2}");
                    metHardConditions.Add(new EvaluatedConditionDetail(
                        "AggPressureAboveThBull",
                        FormatMetValue(currentSnapshot.AggPressure),
                        $"> {FormatMetValue(thresholds.ThAggPressureBreakoutBull.Value)}"));
                    LoggerHelper.LogDebug(_loggerSource, "[BreakoutPatternEvaluator] Long AggPressure matched");
                }
            }
            else // Short
            {
                if (thresholds.ThCvdImpulseShort.HasValue && currentSnapshot.CvdImpulse < thresholds.ThCvdImpulseShort.Value)
                {
                    directionalPressureMet = true;
                    metCriteriaCount++;
                    reasons.Add($"CvdImpulse {currentSnapshot.CvdImpulse:F2} < ThCvdImpulseShort {thresholds.ThCvdImpulseShort.Value:F2}");
                    metHardConditions.Add(new EvaluatedConditionDetail(
                        "CvdImpulseBelowThShort",
                        FormatMetValue(currentSnapshot.CvdImpulse),
                        $"< {FormatMetValue(thresholds.ThCvdImpulseShort.Value)}"));
                    LoggerHelper.LogDebug(_loggerSource, "[BreakoutPatternEvaluator] Short directional CvdImpulse matched");
                }

                if (thresholds.ThAggPressureBreakoutBear.HasValue && currentSnapshot.AggPressure < thresholds.ThAggPressureBreakoutBear.Value)
                {
                    directionalPressureMet = true;
                    metCriteriaCount++;
                    reasons.Add($"AggPressure {currentSnapshot.AggPressure:F2} < ThAggPressureBreakoutBear {thresholds.ThAggPressureBreakoutBear.Value:F2}");
                    metHardConditions.Add(new EvaluatedConditionDetail(
                        "AggPressureBelowThBear",
                        FormatMetValue(currentSnapshot.AggPressure),
                        $"< {FormatMetValue(thresholds.ThAggPressureBreakoutBear.Value)}"));
                    LoggerHelper.LogDebug(_loggerSource, "[BreakoutPatternEvaluator] Short AggPressure matched");
                }
            }

            if (!directionalPressureMet)
            {
                reasons.Add("FAIL: Kein ausreichender directional CVD/AggPressure für Breakout.");
            }

            // --- CRITERIA: Inflection / momentum shift (relative confirmation) ---
            bool inflectionMatch = false;
            if (_direction == OrderDirections.Buy)
            {
                if (features != null && (features.InflectionCvd == InflectionType.Bullish || features.InflectionPressure == InflectionType.Bullish))
                {
                    inflectionMatch = true;
                    metCriteriaCount++;
                    reasons.Add("Bullish Inflection bestätigt Momentum-Shift.");
                    metRelConditions.Add(new EvaluatedConditionDetail("InflectionBullish", "Bullish", "InflectionCvd or InflectionPressure == Bullish"));
                    LoggerHelper.LogDebug(_loggerSource, "[BreakoutPatternEvaluator] Long inflection matched");
                }
                else
                {
                    reasons.Add("Keine Bullish Inflection.");
                }
            }
            else
            {
                if (features != null && (features.InflectionCvd == InflectionType.Bearish || features.InflectionPressure == InflectionType.Bearish))
                {
                    inflectionMatch = true;
                    metCriteriaCount++;
                    reasons.Add("Bearish Inflection bestätigt Momentum-Shift.");
                    metRelConditions.Add(new EvaluatedConditionDetail("InflectionBearish", "Bearish", "InflectionCvd or InflectionPressure == Bearish"));
                    LoggerHelper.LogDebug(_loggerSource, "[BreakoutPatternEvaluator] Short inflection matched");
                }
                else
                {
                    reasons.Add("Keine Bearish Inflection.");
                }
            }

            // --- CRITERIA: Low counter-force near breakout (MaxCounterShare) ---
            if (_direction == OrderDirections.Buy)
            {
                if (thresholds.MaxCounterDeltaShareBear.HasValue)
                {
                    if (currentSnapshot.MaxCounterShareBear < thresholds.MaxCounterDeltaShareBear.Value)
                    {
                        metCriteriaCount++;
                        reasons.Add($"MaxCounterShareBear {currentSnapshot.MaxCounterShareBear:F2} < ThMaxCounterDeltaShareBear {thresholds.MaxCounterDeltaShareBear.Value:F2}");
                        metHardConditions.Add(new EvaluatedConditionDetail("MaxCounterShareBearLow", FormatMetValue(currentSnapshot.MaxCounterShareBear), $"< {FormatMetValue(thresholds.MaxCounterDeltaShareBear.Value)}"));
                        LoggerHelper.LogDebug(_loggerSource, "[BreakoutPatternEvaluator] Low bear counter-share matched for Long breakout");
                    }
                    else
                    {
                        reasons.Add("Starker bärischer Gegenwiderstand vorhanden.");
                    }
                }
                else
                {
                    metDiagnosticConditions.Add(new EvaluatedConditionDetail(
                           "ThMaxCounterDeltaShareBearMissing",
                           "n/a",
                           "Threshold not configured"));
                    LoggerHelper.LogDebug(_loggerSource, "[BreakoutPatternEvaluator] ThMaxCounterDeltaShareBear not set; Überspringen der Gegenaktienprüfung für Long");
                }
            }
            else
            {
                if (thresholds.MaxCounterDeltaShareBull.HasValue)
                {
                    if (currentSnapshot.MaxCounterShareBull < thresholds.MaxCounterDeltaShareBull.Value)
                    {
                        metCriteriaCount++;
                        reasons.Add($"MaxCounterShareBull {currentSnapshot.MaxCounterShareBull:F2} < ThMaxCounterDeltaShareBull {thresholds.MaxCounterDeltaShareBull.Value:F2}");
                        metHardConditions.Add(new EvaluatedConditionDetail("MaxCounterShareBullLow", FormatMetValue(currentSnapshot.MaxCounterShareBull), $"< {FormatMetValue(thresholds.MaxCounterDeltaShareBull.Value)}"));
                        LoggerHelper.LogDebug(_loggerSource, "[BreakoutPatternEvaluator] Low bull counter-share matched for Short breakout");
                    }
                    else
                    {
                        reasons.Add("Starker bullischer Gegenwiderstand vorhanden.");
                    }
                }
                else
                {
                    LoggerHelper.LogDebug(_loggerSource, "[BreakoutPatternEvaluator] ThMaxCounterDeltaShareBull not set; skipping counter-share check for Short");
                }
            }

            // --- CRITERIA: Volume burst and efficiency (supporting hard conditions) ---
            if (thresholds.ThVolBurstZ.HasValue)
            {
                if (currentSnapshot.VolBurstZ > thresholds.ThVolBurstZ.Value)
                {
                    metCriteriaCount++;
                    reasons.Add($"VolBurstZ {currentSnapshot.VolBurstZ:F2} > ThVolBurstZ {thresholds.ThVolBurstZ.Value:F2}");
                    metHardConditions.Add(new EvaluatedConditionDetail("VolBurstZAboveTh", FormatMetValue(currentSnapshot.VolBurstZ), $"> {FormatMetValue(thresholds.ThVolBurstZ.Value)}"));
                    LoggerHelper.LogDebug(_loggerSource, "[BreakoutPatternEvaluator] VolBurstZ matched");
                }
                else
                {
                    reasons.Add("VolBurstZ nicht signifikant.");
                }
            }
            else
            {
                LoggerHelper.LogDebug(_loggerSource, "[BreakoutPatternEvaluator] ThVolBurstZ not set; skipping VolBurstZ check");
            }

            if (thresholds.ThEfficiency.HasValue)
            {
                if (_direction == OrderDirections.Buy)
                {
                    if (currentSnapshot.Efficiency > thresholds.ThEfficiency.Value && features != null && features.SlopeEff > 0)
                    {
                        metCriteriaCount++;
                        reasons.Add($"Efficiency {currentSnapshot.Efficiency:F2} > ThEfficiency {thresholds.ThEfficiency.Value:F2} (slope>0)");
                        metHardConditions.Add(new EvaluatedConditionDetail("EfficiencyAboveTh", FormatMetValue(currentSnapshot.Efficiency), $"> {FormatMetValue(thresholds.ThEfficiency.Value)}, SlopeEff>0"));
                        LoggerHelper.LogDebug(_loggerSource, "[BreakoutPatternEvaluator] Efficiency positive matched for Long breakout");
                    }
                    else
                    {
                        reasons.Add("Efficiency nicht ausreichend für Long breakout.");
                    }
                }
                else
                {
                    if (currentSnapshot.Efficiency < -thresholds.ThEfficiency.Value && features != null && features.SlopeEff < 0)
                    {
                        metCriteriaCount++;
                        reasons.Add($"Efficiency {currentSnapshot.Efficiency:F2} < -ThEfficiency {-thresholds.ThEfficiency.Value:F2} (slope<0)");
                        metHardConditions.Add(new EvaluatedConditionDetail("EfficiencyBelowNegTh", FormatMetValue(currentSnapshot.Efficiency), $"< -{FormatMetValue(thresholds.ThEfficiency.Value)}, SlopeEff<0"));
                        LoggerHelper.LogDebug(_loggerSource, "[BreakoutPatternEvaluator] Efficiency negative matched for Short breakout");
                    }
                    else
                    {
                        reasons.Add("Efficiency nicht ausreichend für Short breakout.");
                    }
                }
            }
            else
            {
                LoggerHelper.LogDebug(_loggerSource, "[BreakoutPatternEvaluator] ThEfficiency not set; skipping Efficiency check");
            }

            // --- CRITERIA: Stacked Imbalances in breakout direction (relative confirmation) ---
            bool stackedImbMatch = false;
            if ((_direction == OrderDirections.Buy &&
                 ((thresholds.ThStackedImbAnyRangeMinDirectional.HasValue && thresholds.ThStackedImbAnyRangeMinDirectional.Value > 0) ||
                  (thresholds.ThStackedImbAnchoredRangeMinDirectional.HasValue && thresholds.ThStackedImbAnchoredRangeMinDirectional.Value > 0)))
                ||
                (_direction == OrderDirections.Sell &&
                 ((thresholds.ThStackedImbAnyRangeMinDirectional.HasValue && thresholds.ThStackedImbAnyRangeMinDirectional.Value > 0) ||
                  (thresholds.ThStackedImbAnchoredRangeMinDirectional.HasValue && thresholds.ThStackedImbAnchoredRangeMinDirectional.Value > 0))))
            {
                if (_direction == OrderDirections.Buy)
                {
                    if ((thresholds.ThStackedImbAnyRangeMinDirectional.HasValue && currentSnapshot.StackedBuyImbCount >= thresholds.ThStackedImbAnyRangeMinDirectional.Value) ||
                        (thresholds.ThStackedImbAnchoredRangeMinDirectional.HasValue && currentSnapshot.StackedBuyImbTopCount >= thresholds.ThStackedImbAnchoredRangeMinDirectional.Value))
                    {
                        stackedImbMatch = true;
                        metCriteriaCount++;
                        reasons.Add($"Stacked Buy Imbalances detected (any={currentSnapshot.StackedBuyImbCount}, top={currentSnapshot.StackedBuyImbTopCount})");
                        metRelConditions.Add(new EvaluatedConditionDetail("StackedBuyImb", $"any={currentSnapshot.StackedBuyImbCount}, top={currentSnapshot.StackedBuyImbTopCount}", "StackedImbalance thresholds matched"));
                        LoggerHelper.LogDebug(_loggerSource, "[BreakoutPatternEvaluator] StackedBuyImb matched");
                    }
                    else
                    {
                        reasons.Add("Keine signifikanten Stacked Buy Imbalances.");
                    }
                }
                else
                {
                    if ((thresholds.ThStackedImbAnyRangeMinDirectional.HasValue && currentSnapshot.StackedSellImbCount >= thresholds.ThStackedImbAnyRangeMinDirectional.Value) ||
                        (thresholds.ThStackedImbAnchoredRangeMinDirectional.HasValue && currentSnapshot.StackedSellImbBottomCount >= thresholds.ThStackedImbAnchoredRangeMinDirectional.Value))
                    {
                        stackedImbMatch = true;
                        metCriteriaCount++;
                        reasons.Add($"Stacked Sell Imbalances detected (any={currentSnapshot.StackedSellImbCount}, bottom={currentSnapshot.StackedSellImbBottomCount})");
                        metRelConditions.Add(new EvaluatedConditionDetail("StackedSellImb", $"any={currentSnapshot.StackedSellImbCount}, bottom={currentSnapshot.StackedSellImbBottomCount}", "StackedImbalance thresholds matched"));
                        LoggerHelper.LogDebug(_loggerSource, "[BreakoutPatternEvaluator] StackedSellImb matched");
                    }
                    else
                    {
                        reasons.Add("Keine signifikanten Stacked Sell Imbalances.");
                    }
                }
            }
            else
            {
                LoggerHelper.LogDebug(_loggerSource, "[BreakoutPatternEvaluator] Stacked imbalance thresholds not configured; skipping stacked imbalance checks");
            }

            // --- POSSIBLE CRITERIA COUNT (dynamisch ermitteln, nur die geprüften Kriterien zählen) ---
            int possibleCriteria = 0;
            // directional CVD / AggPressure (count each configured threshold as separate possible criterion)
            if (_direction == OrderDirections.Buy)
            {
                if (thresholds.ThCvdImpulseLong.HasValue) possibleCriteria++;
                if (thresholds.ThAggPressureBreakoutBull.HasValue) possibleCriteria++;
                if (thresholds.MaxCounterDeltaShareBear.HasValue) possibleCriteria++;
                if (thresholds.ThVolBurstZ.HasValue) possibleCriteria++;
                if (thresholds.ThEfficiency.HasValue) possibleCriteria++;
                if ((thresholds.ThStackedImbAnyRangeMinDirectional.HasValue && thresholds.ThStackedImbAnyRangeMinDirectional.Value > 0) ||
                    (thresholds.ThStackedImbAnchoredRangeMinDirectional.HasValue && thresholds.ThStackedImbAnchoredRangeMinDirectional.Value > 0)) possibleCriteria++;
                // Inflection considered a relative criterion (always possible to check if features present)
                possibleCriteria++; // Inflection
            }
            else // Short
            {
                if (thresholds.ThCvdImpulseShort.HasValue) possibleCriteria++;
                if (thresholds.ThAggPressureBreakoutBear.HasValue) possibleCriteria++;
                if (thresholds.MaxCounterDeltaShareBull.HasValue) possibleCriteria++;
                if (thresholds.ThVolBurstZ.HasValue) possibleCriteria++;
                if (thresholds.ThEfficiency.HasValue) possibleCriteria++;
                if ((thresholds.ThStackedImbAnyRangeMinDirectional.HasValue && thresholds.ThStackedImbAnyRangeMinDirectional.Value > 0) ||
                    (thresholds.ThStackedImbAnchoredRangeMinDirectional.HasValue && thresholds.ThStackedImbAnchoredRangeMinDirectional.Value > 0)) possibleCriteria++;
                possibleCriteria++; // Inflection
            }
            possibleCriteria = Math.Max(1, possibleCriteria);

            int minSignals = thresholds.MinSignalsRequired > 0 ? thresholds.MinSignalsRequired : 1;

            // --- FINAL DECISION ---
            if (metCriteriaCount >= minSignals)
            {
                decimal confidence = (decimal)metCriteriaCount / (decimal)possibleCriteria;
                confidence = Math.Min(1m, confidence);
                LoggerHelper.LogInfo(_loggerSource, $"[BreakoutPatternEvaluator] Breakout erkannt: {Type}, confidence={confidence:F2}, met={metCriteriaCount}/{possibleCriteria}");

                // Audit-Log (metHard + metRel) mit separat aufgeführten Diagnostics
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
                    $"[BreakoutPatternEvaluator] Detected {Type} ({Direction}) {finalMetHardPart}, metRel={relCount}: {relSummary}");

                return PatternEvaluationResult.Detected(Type, confidence, reasons, null, metHardConditions, metRelConditions);
            }
            else
            {
                reasons.Add($"Breakout Muster NICHT erkannt: {metCriteriaCount} von {minSignals} erforderlichen Signalen erfüllt.");
                LoggerHelper.LogDebug(_loggerSource, $"[BreakoutPatternEvaluator] [FINAL] Breakout NOT detected: {Type}. met={metCriteriaCount}/{possibleCriteria}. Reasons: {string.Join(" | ", reasons)}");

                // Debug: metHard / metRel Details (wie vorher)
                LoggerHelper.LogDebug(_loggerSource, $"[BreakoutPatternEvaluator] [FINAL] RBreakout NOT detected returning NOT DETECTED. metHard={metHardConditions.Count}, metRel={metRelConditions.Count}. Details: " +
                    (metHardConditions.Any() ? string.Join(" | ", metHardConditions.Select(h => h.ToShortString())) : "(no hard)") + " / " +
                    (metRelConditions.Any() ? string.Join(" | ", metRelConditions.Select(r => r.ToShortString())) : "(no rel)"));

                // Optional: Diagnostics auch im Debug-Log sichtbar machen
                if (metDiagnosticConditions.Any())
                {
                    LoggerHelper.LogDebug(_loggerSource,
                        $"[BreakoutPatternEvaluator] Breakout diagnostics ({metDiagnosticConditions.Count}): " +
                        string.Join(" | ", metDiagnosticConditions.Select(d => d.ToShortString())));
                }

                return PatternEvaluationResult.NotDetected(Type, string.Join(" | ", reasons), null, metHardConditions, metRelConditions);
            }
        }
    }
}
