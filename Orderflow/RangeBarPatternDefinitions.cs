using System.Collections.Generic;

namespace MyNamespace.Strategies.Orderflow
{
    public static class RangeBarPatternDefinitions
    {
        public static readonly RangeBarPatternDefinition ReversalBounce = new RangeBarPatternDefinition
        {
            Name = "ReversalBounce",
            LongPatterns = new List<string[]>
            {
                new[] { "TREND_BEAR", "TREND_BEAR", "REVERSAL_BULL" }
            },
            ShortPatterns = new List<string[]>
            {
                new[] { "TREND_BULL", "TREND_BULL", "REVERSAL_BEAR" }
            }
        };
    }
}
