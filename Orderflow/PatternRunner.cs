using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.MarketAnalysis;
using MyNamespace.Strategies.Models;
using Utils.Common.Logging;
using static MyNamespace.Strategies.Goldfluss3_3;

namespace MyNamespace.Strategies.Orderflow
{
    public sealed class PatternRunner
    {
        private readonly ILoggerSource? _loggerSource;
        private readonly decimal _tickSize;
        private readonly OrderflowThresholds _uiThresholds;
        private readonly IPatternEvaluator? _reversalLong;
        private readonly IPatternEvaluator? _reversalShort;
        private readonly IPatternEvaluator? _continuationLong;
        private readonly IPatternEvaluator? _continuationShort;

        private int? _lastPullbackLikePhaseBar;

        public IReadOnlyList<IPatternEvaluator> Evaluators { get; }

        public PatternRunner(
            IPatternEvaluator? reversalLong,
            IPatternEvaluator? reversalShort,
            IPatternEvaluator? continuationLong,
            IPatternEvaluator? continuationShort,
            OrderflowThresholds uiThresholds,
            ILoggerSource? loggerSource,
            decimal tickSize)
        {
            _reversalLong = reversalLong;
            _reversalShort = reversalShort;
            _continuationLong = continuationLong;
            _continuationShort = continuationShort;
            _uiThresholds = uiThresholds ?? throw new ArgumentNullException(nameof(uiThresholds));
            _loggerSource = loggerSource;
            _tickSize = tickSize > 0m ? tickSize : 0.25m;

            Evaluators = new[]
            {
                _reversalLong,
                _reversalShort,
                _continuationLong,
                _continuationShort
            }
            .Where(x => x != null)
            .Select(x => x!)
            .ToList();
        }

        public DetectedOrderflowPattern DetectDominantOrderflowPattern(
            int bar,
            OfFeaturesHistory history,
            Dictionary<int, OfFeatures> ofFeaturesByBar,
            MarketRegime currentRegime,
            MarketBiasV2 currentDirectionalBias,
            MarketStateV2 currentMarketState,
            MarketStructureContext currentMarketStructureContext,
            int? currentBar = null)
        {
            if (ofFeaturesByBar == null)
                throw new ArgumentNullException(nameof(ofFeaturesByBar));
            if (history == null)
                throw new ArgumentNullException(nameof(history));

            if (!ofFeaturesByBar.TryGetValue(bar, out var currentFeatures) || currentFeatures?.Snapshot == null)
            {
                return new DetectedOrderflowPattern(
                    OrderflowPatternType.None,
                    OrderDirections.Buy,
                    PatternCategory.Unknown,
                    null,
                    new SetupEvaluationDetails(),
                    0m);
            }

            var thresholds = _uiThresholds;
            thresholds.TickSizeDecimal = _tickSize;

            var state = currentMarketState ?? new MarketStateV2();

            if (state.Phase == MarketPhaseV2.Healthy_Pullback || state.Phase == MarketPhaseV2.Momentum_Refuel)
            {
                _lastPullbackLikePhaseBar = bar;
            }

            var evals = new List<(IPatternEvaluator ev, PatternEvaluationResult res)>(4);

            PatternEvaluationResult? TryEval(IPatternEvaluator? ev)
            {
                try
                {
                    if (ev == null)
                        return null;

                    return ev.Evaluate(
                        currentFeatures.Snapshot,
                        currentFeatures,
                        history,
                        thresholds,
                        currentRegime,
                        currentDirectionalBias,
                        state,
                        currentMarketStructureContext);
                }
                catch (Exception ex)
                {
                    if (_loggerSource != null)
                    {
                        LoggerHelper.LogWarn(_loggerSource,
                            $"[PatternRunner] Evaluator {ev?.GetType().Name ?? "(null)"} threw: {ex.GetType().Name}: {ex.Message}");
                    }
                    return null;
                }
            }

            var r1 = TryEval(_reversalLong);
            if (r1 != null && _reversalLong != null) evals.Add((_reversalLong, r1));
            var r2 = TryEval(_reversalShort);
            if (r2 != null && _reversalShort != null) evals.Add((_reversalShort, r2));

            var c1 = TryEval(_continuationLong);
            if (c1 != null && _continuationLong != null) evals.Add((_continuationLong, c1));
            var c2 = TryEval(_continuationShort);
            if (c2 != null && _continuationShort != null) evals.Add((_continuationShort, c2));

            var detected = evals
                .Where(x => x.res != null && x.res.IsDetected)
                .ToList();

            if (detected.Count > 0)
            {
                int phaseLookbackBars = thresholds.ContinuationPhaseLookbackBars ?? 6;
                phaseLookbackBars = Math.Max(0, phaseLookbackBars);
                bool preferContinuation = (state.Phase == MarketPhaseV2.Healthy_Pullback || state.Phase == MarketPhaseV2.Momentum_Refuel)
                                          || (_lastPullbackLikePhaseBar.HasValue && (bar - _lastPullbackLikePhaseBar.Value) <= phaseLookbackBars);

                if (preferContinuation)
                {
                    var contDetected = detected
                        .Where(x => x.res.PatternType.GetCategory() == PatternCategory.Continuation)
                        .OrderByDescending(x => x.res.ConfidenceScore)
                        .ToList();

                    if (contDetected.Count > 0)
                        detected = contDetected;
                }

                detected = detected
                    .OrderByDescending(x => x.res.ConfidenceScore)
                    .ToList();
            }

            if (detected.Count == 0)
            {
                return new DetectedOrderflowPattern(
                    OrderflowPatternType.None,
                    OrderDirections.Buy,
                    PatternCategory.Unknown,
                    null,
                    new SetupEvaluationDetails(),
                    0m);
            }

            var winner = detected[0];
            var winnerCategory = winner.ev.Type.GetCategory();
            var confidence = winner.res.ConfidenceScore;

            var p = new DetectedOrderflowPattern(
                type: winner.res.PatternType,
                direction: winner.ev.Direction,
                category: winnerCategory,
                ofDerivedLevelCandidate: null,
                setupEvaluationDetails: new SetupEvaluationDetails(),
                confidenceScore: confidence,
                score: 0d,
                combinedConfidence: confidence);

            p.Evaluation = winner.res;
            p.Reasons = winner.res.Reasons?.ToList() ?? new List<string>();
            p.MatchedCriteriaValues = winner.res.MatchedCriteriaValues ?? new Dictionary<string, object>();
            p.MetHardConditions = winner.res.MetHardConditions ?? new List<EvaluatedConditionDetail>();
            p.MetRelevantConditions = winner.res.MetRelevantConditions ?? new List<EvaluatedConditionDetail>();
            p.MetDiagnosticConditions = winner.res.MetDiagnosticConditions ?? new List<EvaluatedConditionDetail>();
            p.MetCriteriaCount = winner.res.MetCriteriaCount;
            p.PossibleCriteriaCount = winner.res.PossibleCriteriaCount;
            p.DetectReason = winner.res.DetectReason ?? string.Empty;

            return p;
        }
    }
}
