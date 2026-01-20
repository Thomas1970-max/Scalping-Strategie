using System;
using MyNamespace.Strategies.Models;

namespace MyNamespace.Strategies.Orderflow
{
    public sealed class ReversalContextThresholdsResult
    {
        public OrderflowThresholds? Bar1 { get; init; }
        public OrderflowThresholds? Bar2 { get; init; }
        public OrderflowThresholds? Bar3 { get; init; }
        public int LookbackBars { get; init; }
        public int HistoryVersion { get; init; }
        public PatternCategory ResolvedCategory { get; init; }
        public DateTime CreatedAtUtc { get; init; }
    }
}
