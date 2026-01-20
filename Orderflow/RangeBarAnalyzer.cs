using System;
using System.Collections.Generic;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using Utils.Common.Logging;

namespace MyNamespace.Strategies.Orderflow
{
    /// <summary>
    /// Universeller Range-Bar Analyzer für Pattern-Erkennung
    /// Kann von allen Pattern-Evaluatoren verwendet werden
    /// </summary>
    public static class RangeBarAnalyzer
    {
        public static RangeBarAnalysisResult AnalyzeRangeBarPattern(
            OfFeaturesHistory history, 
            int currentBar, 
            OvSnapshot currentSnapshot, 
            OrderflowThresholds thresholds,
            RangeBarPatternDefinition patternDefinition,
            ILoggerSource loggerSource = null)
        {
            LoggerHelper.LogInfo(loggerSource, $"[RangeBarAnalyzer] 🚪 ENTRY: history={history != null}, currentBar={currentBar}");

            if (history == null || currentBar < 3)
            {
                LoggerHelper.LogInfo(loggerSource, $"[RangeBarAnalyzer] ❌ EARLY RETURN: history={history == null}, currentBar={currentBar} < 3");
                return new RangeBarAnalysisResult { 
                    IsRangeMarket = false, 
                    HasValidReversalSetup = false,
                    Reason = "Insufficient history or bars" 
                };
            }

            if (patternDefinition == null)
            {
                return new RangeBarAnalysisResult
                {
                    IsRangeMarket = false,
                    HasValidReversalSetup = false,
                    Reason = "No pattern definition provided"
                };
            }

            var tickTrend = thresholds.RangeBarTrendSizeTicks;
            var tickReversal = thresholds.RangeBarReversalSizeTicks;
            var minConsecutiveTrend = thresholds.RangeBarMinConsecutiveTrendBars;
            var maxAlternatingLength = thresholds.RangeBarMaxAlternatingLength;

            decimal tickSize = thresholds.TickSizeDecimal ?? 0.25m;
            if (tickSize <= 0m)
                tickSize = 0.25m;

            var recent = new List<(int barsAgo, int absoluteBar, bool isBull, int sizeTicks, decimal sizePrice)>();
            var availableBars = history.Count + (currentSnapshot != null ? 1 : 0);
            var analysisBars = Math.Min(maxAlternatingLength + 2, availableBars);

            for (int barsAgo = 0; barsAgo < analysisBars; barsAgo++)
            {
                OfFeatures? features = null;
                decimal openVal, closeVal;
                int absoluteBar;

                if (barsAgo == 0)
                {
                    if (currentSnapshot == null)
                        continue;

                    openVal = currentSnapshot.Open;
                    closeVal = currentSnapshot.Close;
                    absoluteBar = currentBar;
                }
                else
                {
                    if (!history.TryGetOfFeatures(barsAgo, out var historyFeatures))
                        continue;

                    features = historyFeatures;
                    openVal = features!.Open;
                    closeVal = features.Close;
                    absoluteBar = features.Bar;
                }

                if (recent.Any(r => r.absoluteBar == absoluteBar))
                    continue;

                var barSizePrice = Math.Abs(closeVal - openVal);
                var sizeInTicks = (int)decimal.Round(barSizePrice / tickSize, MidpointRounding.AwayFromZero);
                var isBullish = closeVal > openVal;

                recent.Add((barsAgo, absoluteBar, isBullish, sizeInTicks, barSizePrice));
            }

            recent = recent.OrderBy(r => r.absoluteBar).ToList();

            if (recent.Count < 4)
            {
                return new RangeBarAnalysisResult
                {
                    IsRangeMarket = false,
                    HasValidReversalSetup = false,
                    Reason = $"Not enough bars for pattern analysis (have {recent.Count}, need at least 4)"
                };
            }

            var barTypes = new List<string>();
            var barIndices = new List<int>();
            int reversalCount = 0;

            foreach (var r in recent)
            {
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
                    LoggerHelper.LogWarn(loggerSource, $"[RangeBarAnalyzer] INVALID RANGEUS INPUT: {msg}");
                    return new RangeBarAnalysisResult
                    {
                        IsRangeMarket = true,
                        HasValidReversalSetup = false,
                        Reason = msg,
                        BarTypes = barTypes
                    };
                }
            }

            bool hasValidLongSetup = false;
            bool hasValidShortSetup = false;
            string lastReversalType = "";
            var lastTrendTypes = new List<string>();
            int? lastReversalAbsoluteBar = null;
            int trendBarsBeforeFirstReversal = 0;

            var isRangeMarket = reversalCount > 0;
            var reason = reversalCount == 0
                ? "No reversal bars detected"
                : $"Reversal bars detected ({reversalCount})";

            for (int i = barTypes.Count - 1; i >= 0; i--)
            {
                var t = barTypes[i];
                if (!t.StartsWith("REVERSAL_"))
                    continue;

                foreach (var pattern in patternDefinition.LongPatterns)
                {
                    if (IsPatternMatch(barTypes, i, pattern))
                    {
                        hasValidLongSetup = true;
                        lastReversalType = t;
                        lastTrendTypes = pattern.Take(pattern.Length - 1).ToList();
                        lastReversalAbsoluteBar = barIndices[i];
                        trendBarsBeforeFirstReversal = pattern.Length - 1;
                        reason = $"Matched long pattern '{patternDefinition.Name}'";
                        break;
                    }
                }

                foreach (var pattern in patternDefinition.ShortPatterns)
                {
                    if (IsPatternMatch(barTypes, i, pattern))
                    {
                        hasValidShortSetup = true;
                        lastReversalType = t;
                        lastTrendTypes = pattern.Take(pattern.Length - 1).ToList();
                        lastReversalAbsoluteBar = barIndices[i];
                        trendBarsBeforeFirstReversal = pattern.Length - 1;
                        reason = $"Matched short pattern '{patternDefinition.Name}'";
                        break;
                    }
                }

                if (hasValidLongSetup || hasValidShortSetup)
                    break;
            }

            var hasValidReversalSetup = hasValidLongSetup || hasValidShortSetup;

            for (int i = 0; i < barTypes.Count; i++)
            {
                var t = barTypes[i];
                if (t.StartsWith("REVERSAL_"))
                {
                    lastReversalType = t;
                    var trendTypesBefore = new List<string>();
                    int trendCount = 0;

                    for (int j = i - 1; j >= 0 && trendCount < minConsecutiveTrend; j--)
                    {
                        if (barTypes[j].StartsWith("TREND_"))
                        {
                            trendTypesBefore.Insert(0, barTypes[j]);
                            trendCount++;
                        }
                    }

                    lastTrendTypes = trendTypesBefore;

                    if (t == "REVERSAL_BULL" &&
                        trendTypesBefore.Count >= minConsecutiveTrend &&
                        trendTypesBefore.All(x => x == "TREND_BEAR"))
                    {
                        hasValidLongSetup = true;
                    }

                    if (t == "REVERSAL_BEAR" &&
                        trendTypesBefore.Count >= minConsecutiveTrend &&
                        trendTypesBefore.All(x => x == "TREND_BULL"))
                    {
                        hasValidShortSetup = true;
                    }

                    break;
                }
            }

            return new RangeBarAnalysisResult
            {
                IsRangeMarket = isRangeMarket,
                HasValidReversalSetup = hasValidReversalSetup,
                ConsecutiveTrendBars = trendBarsBeforeFirstReversal,
                BarTypes = barTypes.Take(maxAlternatingLength).ToList(),
                CurrentBarType = barTypes.LastOrDefault() ?? string.Empty,
                Reason = reason,
                LastReversalBarIndex = lastReversalAbsoluteBar,
                HasValidLongReversalSetup = hasValidLongSetup,
                HasValidShortReversalSetup = hasValidShortSetup,
                LastReversalType = lastReversalType,
                LastTrendTypes = lastTrendTypes
            };
        }

        private static bool IsPatternMatch(IReadOnlyList<string> barTypes, int endIndex, string[] pattern)
        {
            if (pattern == null || pattern.Length == 0)
                return false;

            if (endIndex - (pattern.Length - 1) < 0)
                return false;

            for (int offset = 0; offset < pattern.Length; offset++)
            {
                if (!string.Equals(barTypes[endIndex - (pattern.Length - 1) + offset], pattern[offset], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private static string DetermineBarType(OvSnapshot snapshot, int tickTrend, int tickReversal, decimal tickSize)
        {
            if (snapshot == null)
                return "UNKNOWN";
            
            decimal priceMovement = Math.Abs(snapshot.Close - snapshot.Open);
            int ticks = (int)decimal.Round(priceMovement / tickSize, MidpointRounding.AwayFromZero);

            if (ticks == tickReversal)
                return snapshot.Close > snapshot.Open ? "REVERSAL_BULL" : "REVERSAL_BEAR";
            else if (ticks == tickTrend)
                return snapshot.Close > snapshot.Open ? "TREND_BULL" : "TREND_BEAR";
            
            return "UNKNOWN";
        }
    }

    /// <summary>
    /// Result-Klasse für Range-Bar Analyse
    /// Enthält alle Pattern-Informationen für Evaluatoren
    /// </summary>
    public class RangeBarAnalysisResult
    {
        public bool IsRangeMarket { get; set; }
        public bool HasValidReversalSetup { get; set; }
        public bool HasValidLongReversalSetup { get; set; }
        public bool HasValidShortReversalSetup { get; set; }
        public string CurrentBarType { get; set; } = string.Empty;
        public string LastReversalType { get; set; } = "";
        public List<string> LastTrendTypes { get; set; } = new List<string>();
        public List<string> BarTypes { get; set; } = new List<string>();
        public int ConsecutiveTrendBars { get; set; }
        public int? LastReversalBarIndex { get; set; }
        public string Reason { get; set; } = "";
    }

    public class RangeBarPatternDefinition
    {
        public string Name { get; set; } = "";
        public List<string[]> LongPatterns { get; set; } = new();
        public List<string[]> ShortPatterns { get; set; } = new();
    }
}
