using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using static MyNamespace.Strategies.Goldfluss3_3;

namespace MyNamespace.Strategies.Orderflow
{
    // Repräsentiert eine einzelne, evaluierbare Bedingung
    public class ConditionTemplate
    {
        private readonly List<Func<OvSnapshot, OfFeaturesHistory, Dictionary<int, OfFeatures>, (bool passed, string reason)>> _conditions =
            new List<Func<OvSnapshot, OfFeaturesHistory, Dictionary<int, OfFeatures>, (bool passed, string reason)>>();

        public void AddCondition(Func<OvSnapshot, OfFeaturesHistory, Dictionary<int, OfFeatures>, bool> condition, string name)
        {
            _conditions.Add((snap, hist, feats) => (condition(snap, hist, feats), name));
        }

        public void AddCondition(Func<OvSnapshot, OfFeaturesHistory, Dictionary<int, OfFeatures>, (bool passed, string reason)> conditionWithReason)
        {
            _conditions.Add(conditionWithReason);
        }

        public int Count => _conditions.Count;

        public (bool allPassed, List<string> failedReasons) EvaluateWithReasons(
            OvSnapshot s, OfFeaturesHistory h, Dictionary<int, OfFeatures> f)
        {
            List<string> failedReasons = new List<string>();
            bool allPassed = true;
            foreach (var condFunc in _conditions)
            {
                var (passed, reason) = condFunc(s, h, f);
                if (!passed)
                {
                    allPassed = false;
                    // Hier rufen wir die neue interne Methode auf
                    string enrichedReason = EnrichSingleFailedReason(reason, s);
                    failedReasons.Add(enrichedReason);
                }
            }
            return (allPassed, failedReasons);
        }

        public (int passedCount, List<string> failedReasons) EvaluateWithRichReasons(
            OvSnapshot s, OfFeaturesHistory h, Dictionary<int, OfFeatures> f)
        {
            List<string> failedReasons = new List<string>();
            int passedCount = 0;
            foreach (var condFunc in _conditions)
            {
                var (passed, reason) = condFunc(s, h, f);
                if (passed)
                {
                    passedCount++;
                }
                else
                {
                    // Hier rufen wir die neue interne Methode auf
                    string enrichedReason = EnrichSingleFailedReason(reason, s);
                    failedReasons.Add(enrichedReason);

                }
            }
            return (passedCount, failedReasons);
        }
        /// <summary>
        /// Interne Hilfsmethode, um einen einzelnen Fehlergrund-String mit den aktuellen Werten
        /// aus einem OvSnapshot anzureichern.
        /// </summary>
        /// <param name="reason">Der ursprüngliche Fehlergrund-String, wie er von einer Bedingung geliefert wird.</param>
        /// <param name="currentSnapshot">Der aktuelle OvSnapshot mit den tatsächlichen Marktwerten.</param>
        /// <returns>Der angereicherte Fehlergrund-String.</returns>
        private string EnrichSingleFailedReason(string reason, OvSnapshot currentSnapshot)
        {

            // Die folgenden Vergleiche basieren auf den Strings, die Ihre Bedingungen erzeugen.
            // Es ist wichtig, dass die Suchmuster (z.B. "CvdImpulse Long") genau mit den generierten Strings übereinstimmen.

            if (!string.IsNullOrEmpty(reason) && reason.Contains("CvdImpulse Long (Mode=Bounce, Th=", StringComparison.OrdinalIgnoreCase))
            {
                return $"{reason} (Aktuell: {currentSnapshot.CvdImpulse:F4})";
            }
            if (reason.Contains("CounterDeltaShare Long (Max Bearish CounterFlow ="))
            {
                return $"{reason} (Aktuell Max Bearish CounterFlow: {currentSnapshot.MaxCounterShareBear:F2})";
            }
            if (reason.Contains("VolBurstZ calm (|Z| <="))
            {
                return $"{reason} (Aktuell |Z|: {Math.Abs(currentSnapshot.VolBurstZ):F2})";
            }
            if (reason.Contains("AggPressure Bull >="))
            {
                return $"{reason} (Aktuell AggPressure: {currentSnapshot.AggPressure:F2})";
            }
            // --- Bedingungen für Short-Trades ---
            if (reason.Contains("CvdImpulse Short (Mode=Bounce, Th="))
            {
                return $"{reason} (Aktuell: {currentSnapshot.CvdImpulse:F4})";
            }
            if (reason.Contains("CounterDeltaShare Short (Max Bullish CounterFlow ="))
            {
                return $"{reason} (Aktuell Max Bullish CounterFlow: {currentSnapshot.MaxCounterShareBull:F2})";
            }
            if (reason.Contains("AggPressure Bear <="))
            {
                return $"{reason} (Aktuell AggPressure: {currentSnapshot.AggPressure:F2})";
            }

            // Fügen Sie hier weitere Bedingungen hinzu, falls andere Metriken ebenfalls oft fehlschlagen
            // Beispiel:
            // if (reason.Contains("Efficiency >="))
            // {
            //     return $"{reason} (Aktuell: {currentSnapshot.Efficiency:F2})";
            // }

            return reason; // Wenn die Bedingung nicht erkannt wird, gib den Original-String zurück
        }
    }
    /// <summary>
    /// Repräsentiert die Menge der harten und relevanten Bedingungen, die für ein Setup erfüllt sein müssen.
    /// </summary>
    public class EntryConditionSet
    {
        public ConditionTemplate HardConditions { get; }
        public ConditionTemplate RelevantConditions { get; }

        public EntryConditionSet(ConditionTemplate hardConditions, ConditionTemplate relevantConditions)
        {
            HardConditions = hardConditions ?? new ConditionTemplate();
            RelevantConditions = relevantConditions ?? new ConditionTemplate();
        }
    }
}



