using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using static MyNamespace.Strategies.Goldfluss3_3;

namespace MyNamespace.Strategies.Orderflow
{
    /// <summary>
    /// Das Ergebnis der Bewertung eines spezifischen Orderflow-Musters.
    /// Enthält Informationen darüber, ob das Muster erkannt wurde, wie stark es ist und warum.
    /// </summary>
    public class PatternEvaluationResult
    {
        public OrderflowPatternType PatternType { get; }
        public bool IsDetected { get; }
        public decimal ConfidenceScore { get; } // Wert zwischen 0 und 1, der angibt, wie gut das Muster passt
                                                // Gründe / erklärende Texte (kann mehrere Einträge enthalten)
        public List<string> Reasons { get; }

        // Einzelne, strukturierte Informationen über erfüllte Bedingungen
        public List<EvaluatedConditionDetail> MetHardConditions { get; }       // Liste der erfüllten Hard Conditions
        public List<EvaluatedConditionDetail> MetRelevantConditions { get; }   // Liste der erfüllten Relevant Conditions
        public List<EvaluatedConditionDetail> MetDiagnosticConditions { get; } // Diagnostische/zusätzliche Bedingungen

        // Detaillierte Werte der einzelnen Kriterien (optional)
        public Dictionary<string, object> MatchedCriteriaValues { get; }

        // Neu: Zähler und erklärender String
        public int MetCriteriaCount { get; }           // Anzahl erfüllter Kriterien (wenn berechnet)
        public int PossibleCriteriaCount { get; }      // Anzahl möglicher Kriterien (GetPossibleCriteriaCount)
        public string DetectReason { get; }            // Kurztext mit zusammengefasster Reason (optional)

        public PatternEvaluationResult(
            OrderflowPatternType patternType,
            bool isDetected,
            decimal confidenceScore,
            List<string> reasons = null,
            Dictionary<string, object> matchedCriteriaValues = null,
            List<EvaluatedConditionDetail> metHardConditions = null,
            List<EvaluatedConditionDetail> metRelevantConditions = null,
            List<EvaluatedConditionDetail> metDiagnosticConditions = null,
            int metCriteriaCount = -1,
            int possibleCriteriaCount = -1,
            string detectReason = "")
        {
            PatternType = patternType;
            IsDetected = isDetected;
            ConfidenceScore = confidenceScore;
            Reasons = reasons ?? new List<string>();
            MatchedCriteriaValues = matchedCriteriaValues ?? new Dictionary<string, object>();

            MetHardConditions = metHardConditions ?? new List<EvaluatedConditionDetail>();
            MetRelevantConditions = metRelevantConditions ?? new List<EvaluatedConditionDetail>();
            MetDiagnosticConditions = metDiagnosticConditions ?? new List<EvaluatedConditionDetail>();

            MetCriteriaCount = metCriteriaCount;
            PossibleCriteriaCount = possibleCriteriaCount;
            DetectReason = detectReason ?? string.Empty;
        }

        /// <summary>
        /// Erstellt ein Ergebnis für ein nicht erkanntes Muster (einfache Variante).
        /// Beibehaltung der alten Signatur für Rückwärtskompatibilität.
        /// </summary>
        public static PatternEvaluationResult NotDetected(OrderflowPatternType patternType, string reason = null)
        {
            var reasons = new List<string>();
            if (!string.IsNullOrEmpty(reason))
            {
                reasons.Add(reason);
            }
            return new PatternEvaluationResult(patternType, false, 0m, reasons, null, null, null, null, -1, -1, reason);
        }

        /// <summary>
        /// Erstellt ein Ergebnis für ein nicht erkanntes Muster (erweiterte Variante).
        /// Erlaubt das Mitgeben bereits erfüllter Met-Listen und optionaler MatchedValues.
        /// </summary>
        public static PatternEvaluationResult NotDetected(
            OrderflowPatternType patternType,
            string reason,
            Dictionary<string, object> matchedCriteriaValues = null,
            List<EvaluatedConditionDetail> metHardConditions = null,
            List<EvaluatedConditionDetail> metRelevantConditions = null,
            List<EvaluatedConditionDetail> metDiagnosticConditions = null,
            int metCriteriaCount = -1,
            int possibleCriteriaCount = -1,
            string detectReason = null)
        {
            var reasons = new List<string>();
            if (!string.IsNullOrEmpty(reason))
            {
                reasons.Add(reason);
            }

            return new PatternEvaluationResult(
                patternType,
                false,
                0m,
                reasons,
                matchedCriteriaValues,
                metHardConditions,
                metRelevantConditions,
                metDiagnosticConditions,
                metCriteriaCount,
                possibleCriteriaCount,
                detectReason ?? reason);
        }

        /// <summary>
        /// Erstellt ein Ergebnis für ein erkanntes Muster.
        /// </summary>
        public static PatternEvaluationResult Detected(
            OrderflowPatternType patternType,
            decimal confidenceScore = 1m,
            List<string> reasons = null,
            Dictionary<string, object> matchedCriteriaValues = null,
            List<EvaluatedConditionDetail> metHardConditions = null,
            List<EvaluatedConditionDetail> metRelevantConditions = null,
            List<EvaluatedConditionDetail> metDiagnosticConditions = null,
            int metCriteriaCount = -1,
            int possibleCriteriaCount = -1,
            string detectReason = null)
        {
            return new PatternEvaluationResult(
                patternType,
                true,
                confidenceScore,
                reasons,
                matchedCriteriaValues,
                metHardConditions,
                metRelevantConditions,
                metDiagnosticConditions,
                metCriteriaCount,
                possibleCriteriaCount,
                detectReason);
        }
    }
}



