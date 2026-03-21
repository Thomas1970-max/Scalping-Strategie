using System; // Für [Flags] Attribut, falls GateMetric ein Flags-Enum ist
using ATAS.DataFeedsCore;
using System.Collections.Generic;
using static MyNamespace.Strategies.Geldfluss3_3;
using MyNamespace.Strategies.Orderflow;
using static MyNamespace.Strategies.Models.DetectedOrderflowPattern;
using MyNamespace.Strategies.Models;
using System.Windows.Documents;

namespace MyNamespace.Strategies.Models
{

    public static class TradeProfileExtensions
    {
        public static ProfileParameterSet GetProfileParameters(this TradeProfile profile)
        {
            switch (profile)
            {
                case TradeProfile.Conservative:
                    return new ProfileParameterSet
                    {
                        VolBurstZMultiplier = 1.25,
                        TradeRateZMultiplier = 1.25,
                        CvdImpulseClampMultiplier = 1.25,
                        MinSignalsOffset = 1,
                        AggressionFactor = 0.8
                    };
                case TradeProfile.Aggressive:
                    return new ProfileParameterSet
                    {
                        VolBurstZMultiplier = 0.75,
                        TradeRateZMultiplier = 0.75,
                        CvdImpulseClampMultiplier = 0.75,
                        MinSignalsOffset = -1,
                        AggressionFactor = 1.25
                    };
                case TradeProfile.Neutral:
                default:
                    return new ProfileParameterSet
                    {
                        VolBurstZMultiplier = 1.0,
                        TradeRateZMultiplier = 1.0,
                        CvdImpulseClampMultiplier = 1.0,
                        MinSignalsOffset = 0,
                        AggressionFactor = 1.0
                    };
            }
        }
    }

    //Diese Klasse wird detailliertere, möglicherweise kurzfristigere und dynamischere Zustände des Marktes erfassen, die über reine Volatilität und übergeordnete Richtung hinausgehen.
    public class MarketState
    {
        public MarketRegime Regime { get; set; } = MarketRegime.None;
        public MarketDirectionalBias DirectionalBias { get; set; } = MarketDirectionalBias.Undefined;
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public decimal? Confidence { get; set; } = null; // 0..1

        public bool IsOverbought { get; set; } = false;
        public bool IsOversold { get; set; } = false;
        public bool IsNearResistance { get; set; } = false;
        public bool IsNearSupport { get; set; } = false;
        public bool IsConsolidating { get; set; } = false; // Z.B. enge Range, geringe Preisbewegung
        public bool IsBreakingOut { get; set; } = false;   // Könnte ein temporärer Zustand sein, der auf ein Breakout-Muster hinweist
        public decimal RsiValue { get; set; } // Beispiel für einen Oszillator-Wert
        public decimal DistanceToNearestSupport { get; set; } // Absolute oder relative Distanz
        public decimal DistanceToNearestResistance { get; set; } // Absolute oder relative Distanz

        public override string ToString()
        {
            return $"Time={TimestampUtc:O}, Bias={DirectionalBias}, Regime={Regime}, Conf={(Confidence.HasValue ? Confidence.Value.ToString("F2") : "n/a")}, Consolidating={IsConsolidating}, Breakout={IsBreakingOut}";
        }

        // Weitere Zustände, die für deine Strategie relevant sein könnten
    }

    public class ConditionEvaluationResult
    {
        public bool IsMet { get; }
        public string Reason { get; }

        private ConditionEvaluationResult(bool isMet, string reason)
        {
            IsMet = isMet;
            Reason = reason;
        }

        public static ConditionEvaluationResult Met(string reason = null) => new ConditionEvaluationResult(true, reason ?? "Condition met.");
        public static ConditionEvaluationResult NotMet(string reason) => new ConditionEvaluationResult(false, reason);
    }

    public class TradingSession
    {
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }

        public TradingSession(TimeSpan start, TimeSpan end)
        {
            StartTime = start;
            EndTime = end;
        }
    }

    public class SessionLevel
    {
        public DateTime Date { get; set; }
        public decimal Value { get; set; }
        public string Label { get; set; } = string.Empty;
    }

    public class DetectedOrderflowPattern
    {
        // Grundlegende Pattern-Metadaten
        public OrderflowPatternType Type { get; set; } = OrderflowPatternType.None;
        public OrderDirections Direction { get; set; } = OrderDirections.Buy;
        public PatternCategory Category { get; set; } = PatternCategory.Unknown;

        // Level-Kandidaten (nullable, da nicht immer vorhanden)
        public decimal? DerivedLevel { get; private set; }
        public decimal? OfDerivedLevelCandidate { get; set; } = null;

        // Details aus der Setup/Evaluierung (wird von PatternSignaturer übergeben)
        public SetupEvaluationDetails SetupEvaluationDetails { get; set; }
        public SetupEvaluationDetails SetupEvalDetails
        {
            get => SetupEvaluationDetails;
            set => SetupEvaluationDetails = value;
        }

        public decimal? Level
        {
            get => OfDerivedLevelCandidate;
            set => OfDerivedLevelCandidate = value;
        }

        // Optional: falls der Evaluator bereits PatternEvaluationResult liefert, ist Evaluation gesetzt.
        public PatternEvaluationResult Evaluation { get; set; } = null;

        // Typisierte Collections (korrekt generisch)
        public List<string> Reasons { get; set; } = new List<string>();
        public Dictionary<string, object> MatchedCriteriaValues { get; set; } = new Dictionary<string, object>();
        public List<EvaluatedConditionDetail> MetHardConditions { get; set; } = new List<EvaluatedConditionDetail>();
        public List<EvaluatedConditionDetail> MetRelevantConditions { get; set; } = new List<EvaluatedConditionDetail>();
        public List<EvaluatedConditionDetail> MetDiagnosticConditions { get; set; } = new List<EvaluatedConditionDetail>();
        public int MetCriteriaCount { get; set; } = -1;
        public int PossibleCriteriaCount { get; set; } = -1;
        public string DetectReason { get; set; } = string.Empty;

        // Confidence / Stärke des erkannten Musters (vom Evaluator geliefert) — Bereich [0,1]
        public decimal ConfidenceScore { get; set; } = 0m;

        // Numerischer Score
        public double Score { get; set; } = 0d;

        // Kombinierte Kennzahl
        public decimal CombinedConfidence { get; set; } = 0m;

        // Convenience-Eigenschaft: IsDetected
        public bool IsDetected => ConfidenceScore > 0m || Score > 0d || CombinedConfidence > 0m || (Evaluation?.IsDetected ?? false);

        // Standard-Konstruktor (parameterlos)
        public DetectedOrderflowPattern() { }

        // Minimaler Konstruktor
        public DetectedOrderflowPattern(OrderflowPatternType type, OrderDirections direction, PatternCategory category)
        {
            Type = type;
            Direction = direction;
            Category = category;
        }

        // Rückwärtskompatibler Konstruktor (alte Signatur: nutzt nur ConfidenceScore)
        public DetectedOrderflowPattern(
            OrderflowPatternType type,
            OrderDirections direction,
            PatternCategory category,
            decimal? ofDerivedLevelCandidate,
            SetupEvaluationDetails setupEvaluationDetails,
            decimal confidenceScore = 0m)
            : this(type, direction, category)
        {
            OfDerivedLevelCandidate = ofDerivedLevelCandidate;
            SetupEvaluationDetails = setupEvaluationDetails;
            ConfidenceScore = confidenceScore;
            CombinedConfidence = confidenceScore;
            Score = 0d;
        }

        // Vollständiger Konstruktor mit Score und CombinedConfidence
        public DetectedOrderflowPattern(
            OrderflowPatternType type,
            OrderDirections direction,
            PatternCategory category,
            decimal? ofDerivedLevelCandidate,
            SetupEvaluationDetails setupEvaluationDetails,
            decimal confidenceScore,
            double score,
            decimal combinedConfidence)
            : this(type, direction, category)
        {
            OfDerivedLevelCandidate = ofDerivedLevelCandidate;
            SetupEvaluationDetails = setupEvaluationDetails;
            ConfidenceScore = confidenceScore;
            Score = score;
            CombinedConfidence = combinedConfidence;
        }

        public override string ToString()
        {
            if (Type == OrderflowPatternType.None)
                return "No Dominant Pattern Detected";

            return $"{Type} ({Direction}) Category={Category} Conf={ConfidenceScore:F2} Score={Score:F3} CombinedConf={CombinedConfidence:F2}";
        }

        // Adapter: konstruiert ein PatternEvaluationResult aus diesem DetectedOrderflowPattern
        public PatternEvaluationResult ToPatternEvaluationResult()
        {
            // Falls Evaluation bereits gesetzt und vollständig ist, verwende sie direkt (vermeidet Datenverlust)
            if (Evaluation != null)
            {
                // Normalisiere: falls detectReason leer, versuche aus Reasons zu bauen
                var detectReasonFromEval = string.IsNullOrWhiteSpace(Evaluation.DetectReason)
                    ? (Evaluation.Reasons != null && Evaluation.Reasons.Any()
                        ? string.Join(" | ", Evaluation.Reasons)
                        : string.Empty)
                    : Evaluation.DetectReason;

                // Wenn Evaluation bereits alle Felder hat, gib sie zurück; ansonsten erstelle eine neue Instanz basierend auf Evaluation + lokale Felder
                if (Evaluation.MetCriteriaCount != -1 || Evaluation.PossibleCriteriaCount != -1 || !string.IsNullOrEmpty(Evaluation.DetectReason))
                {
                    return Evaluation;
                }

                return new PatternEvaluationResult(
                    patternType: Evaluation.PatternType,
                    isDetected: Evaluation.IsDetected,
                    confidenceScore: Evaluation.ConfidenceScore,
                    reasons: Evaluation.Reasons ?? new List<string>(),
                    matchedCriteriaValues: Evaluation.MatchedCriteriaValues ?? new Dictionary<string, object>(),
                    metHardConditions: Evaluation.MetHardConditions ?? new List<EvaluatedConditionDetail>(),
                    metRelevantConditions: Evaluation.MetRelevantConditions ?? new List<EvaluatedConditionDetail>(),
                    metDiagnosticConditions: Evaluation.MetDiagnosticConditions ?? new List<EvaluatedConditionDetail>(),
                    metCriteriaCount: this.MetCriteriaCount >= 0 ? this.MetCriteriaCount : Evaluation.MetCriteriaCount,
                    possibleCriteriaCount: this.PossibleCriteriaCount >= 0 ? this.PossibleCriteriaCount : Evaluation.PossibleCriteriaCount,
                    detectReason: !string.IsNullOrWhiteSpace(this.DetectReason) ? this.DetectReason : detectReasonFromEval
                );
            }

            // Sonst baue neues PatternEvaluationResult aus den Feldern dieser Klasse
            var reasons = this.Reasons ?? new List<string>();
            var matched = this.MatchedCriteriaValues ?? new Dictionary<string, object>();
            var metHard = this.MetHardConditions ?? new List<EvaluatedConditionDetail>();
            var metRelevant = this.MetRelevantConditions ?? new List<EvaluatedConditionDetail>();
            var metDiagnostic = this.MetDiagnosticConditions ?? new List<EvaluatedConditionDetail>();

            // Heuristischer metCriteriaCount fallback
            int metCount = this.MetCriteriaCount;
            if (metCount < 0)
            {
                try { metCount = (metHard?.Count ?? 0) + (metRelevant?.Count ?? 0); } catch { metCount = -1; }
            }

            int possibleCount = this.PossibleCriteriaCount >= 0 ? this.PossibleCriteriaCount : -1;

            var detectReason = !string.IsNullOrWhiteSpace(this.DetectReason)
                ? this.DetectReason
                : (reasons.Any() ? string.Join(" | ", reasons) : string.Empty);

            return new PatternEvaluationResult(
                patternType: this.Type,
                isDetected: this.IsDetected,
                confidenceScore: this.ConfidenceScore,
                reasons: reasons,
                matchedCriteriaValues: matched,
                metHardConditions: metHard,
                metRelevantConditions: metRelevant,
                metDiagnosticConditions: metDiagnostic,
                metCriteriaCount: metCount,
                possibleCriteriaCount: possibleCount,
                detectReason: detectReason
            );
        }

        public class ProfileParameterSet
        {
            public double VolBurstZMultiplier { get; set; } = 1.0;
            public double TradeRateZMultiplier { get; set; } = 1.0;
            public double CvdImpulseClampMultiplier { get; set; } = 1.0;
            public int MinSignalsOffset { get; set; } = 0;
            public double AggressionFactor { get; set; } = 1.0;
        }
    }
}



