using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using MyNamespace.Strategies.Orderflow;
using Utils.Common.Logging;
using static MyNamespace.Strategies.Goldfluss3_3;


namespace MyNamespace.Strategies.Orderflow
{
    public static class OrderflowPatternTypeExtensions
    {
        public static PatternCategory GetCategory(this OrderflowPatternType patternType)
        {
            switch (patternType)
            {
                case OrderflowPatternType.None:
                    return PatternCategory.Unknown;
                case OrderflowPatternType.GeneralTrendDetection:
                    // Allgemeine Trend-Detektion ? Fortsetzung / Continuation
                    return PatternCategory.Continuation;

                case OrderflowPatternType.GeneralRangeDetection:
                    // Range-Erkennung ? MeanReversion (Range- / Mean-Reversion-Muster)
                    return PatternCategory.MeanReversion;

                case OrderflowPatternType.PotentialLongReversalBounce:
                case OrderflowPatternType.PotentialShortReversalBounce:
                case OrderflowPatternType.PotentialLongPullback:
                case OrderflowPatternType.PotentialShortPullback:
                    return PatternCategory.Reversal;

                case OrderflowPatternType.PotentialLongTrendContinuation:
                case OrderflowPatternType.PotentialShortTrendContinuation:
                    return PatternCategory.Continuation;

                case OrderflowPatternType.PotentialLongBreakout:
                case OrderflowPatternType.PotentialShortBreakout:
                    return PatternCategory.Breakout;

                // Falls später neue Muster hinzukommen, hier erweitern.
                default:
                    // Defensive Fallback: loggen und Unknown zurückgeben (PatternSignaturer überspringt Unknown).
                    try
                    {
                        LoggerHelper.LogWarn(typeof(OrderflowPatternTypeExtensions),
                            $"[OrderflowPatternTypeExtensions] GetCategory: unbekannter OrderflowPatternType '{patternType}' — verwende PatternCategory.Unknown als Fallback.");
                    }
                    catch
                    {
                        // Logging darf niemals Exceptions durchreichen; swallow falls Logger Probleme macht.
                    }
                    return PatternCategory.Unknown;
            }
        }
    }
}








