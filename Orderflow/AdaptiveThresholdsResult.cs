using System;
using System.Collections.Generic;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using Utils.Common.Logging;
using static MyNamespace.Strategies.Goldfluss3_3;


namespace MyNamespace.Strategies.Orderflow
{
    public sealed class AdaptiveThresholdsResult
    {
        // adaptive, ungeprüfte "full" thresholds (können null sein, falls Berechnung fehlgeschlagen)
        public OrderflowThresholds? Full { get; init; }
               
        // ggf. geprunete, nur relevante Metriken je Bias/Category
        public OrderflowThresholds? Pruned { get; init; }

        // Hilfsinfo für Cache/Debugging - History-Version, die für die Berechnung verwendet wurde (falls bekannt)
        public int? HistoryVersion { get; init; }

        // Optional: die Kategorie, die intern als Basis verwendet oder aufgelöst wurde
        public PatternCategory? ResolvedCategory { get; init; }

        // Optional: Timestamp der Erstellung
        public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

        // Neu: Normalized, deterministische, kommagetrennte Liste oder leerer String
        public string NullifiedJoined { get; set; } = string.Empty;

        // Optional: Liste getrennt (falls benötigt)
        public IReadOnlyList<string> NullifiedList { get; set; } = Array.Empty<string>();
    }
}


