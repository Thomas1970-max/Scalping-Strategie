using System.Collections.Generic;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using static MyNamespace.Strategies.Goldfluss3_3;

namespace MyNamespace.Strategies.Orderflow
{
    public interface IThresholdsResolver
    {
        AdaptiveThresholdsResult CalculateAdaptiveThresholds(
            OrderflowPatternType? assumedOrderflowPatternType, // Hint: optional
            PatternCategory? patternCategory, // optional (kann zur schnelleren Pruning-Entscheidung genutzt werden)
            MarketRegime regime,
            OfFeaturesHistory? history,
            IReadOnlyDictionary<int, OfFeatures>? featuresByBar,
            ModeSpecsEntry categorySpecs,
            SetupConditionConfig patternConditionConfig,
            SetupConfiguration globalStratConfig,
            OrderflowThresholds? initialThresholds = null,
            int? barIndex = null
        );

        OrderflowThresholds? Clone(OrderflowThresholds original);

        // Neu: zuletzt berechnetes Ergebnis (kann null sein)
        AdaptiveThresholdsResult? LastAdaptiveThresholdsResult { get; }
    }
}



