using System;
using System.Collections.Generic;
using System.Linq;
using MyNamespace.Strategies.Models; // für DetectedOrderflowPattern, PatternCategory, OrderflowPatternType
using ATAS.DataFeedsCore; // für OrderDirections

namespace MyNamespace.Strategies.Orderflow
{
    public class ConflictResolver
    {
        public bool UsePerformanceFactorInScore { get; set; } = false;
        public double MinScoreLead { get; set; } = 0.20;
        public double RejectIfBelow { get; set; } = 0.10;
        public decimal MinConfidence { get; set; } = 0.30m;
        public double OppositeDirectionCancelThreshold { get; set; } = 0.05;

        
        public DetectedOrderflowPattern ResolveConflict(
            IEnumerable<DetectedOrderflowPattern> candidates,
            IReadOnlyDictionary<string, double> performanceFactors = null,
            string higherTimeframeDirection = null)
        {
            if (candidates == null) return null;

            // Filtere sinnvolle Kandidaten
            var filtered = candidates
                .Where(c => c != null && c.ConfidenceScore >= MinConfidence && (c.Score > 0.0 || c.MetCriteriaCount >= 2))
                .ToList();

            if (filtered.Count == 0) return null;

            // Ordnere nach Score desc, dann CombinedConfidence desc
            var ordered = filtered
                .OrderByDescending(c => c.Score)
                .ThenByDescending(c => (double)c.CombinedConfidence)
                .ToList();

            if (ordered.Count == 1)
                return ordered[0];

            // Top zwei Kandidaten
            var top = ordered[0];
            var runnerUp = ordered[1];

            // Wenn entgegengesetzte Richtungen und relative Differenz klein -> ablehnen
            bool oppositeDir = top.Direction != runnerUp.Direction;
            double relDiff = Math.Abs(top.Score - runnerUp.Score) / Math.Max(Math.Abs(top.Score), Math.Abs(runnerUp.Score));
            if (oppositeDir && relDiff < OppositeDirectionCancelThreshold)
            {
                // Ambiguous -> keine Entscheidung
                return null;
            }

            // Wenn Top deutlich voraus ist (MinScoreLead) -> akzeptieren
            if (top.Score - runnerUp.Score >= MinScoreLead)
                return top;

            // Falls beide ähnlich, dann CombinedConfidence entscheidet
            if (top.CombinedConfidence > runnerUp.CombinedConfidence)
                return top;
            if (runnerUp.CombinedConfidence > top.CombinedConfidence)
                return runnerUp;

            // Fallback: wenn höhere Priorität (nutze PatternSignaturer.GetEvaluationPriority wäre intern, hier schauen wir nach Category)
            // Wenn gleich, dann keine Entscheidung
            if (top.Category != runnerUp.Category)
            {
                // Priorisiere nach Category (z.B. Reversal > Breakout > Continuation). Du kannst hier spezifische Regeln ergänzen.
                int pTop = MapCategoryPriority(top.Category);
                int pUp = MapCategoryPriority(runnerUp.Category);
                if (pTop < pUp) return top;
                if (pUp < pTop) return runnerUp;
            }

            return null;
        }

        private int MapCategoryPriority(PatternCategory c)
        {
            // Kleinere Werte = höhere Priorität
            switch (c)
            {
                case PatternCategory.Reversal: return 1;
                case PatternCategory.Breakout: return 2;
                case PatternCategory.Continuation: return 3;
                default: return 99;
            }
        }
    }
}

