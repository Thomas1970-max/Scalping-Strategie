using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using static MyNamespace.Strategies.Goldfluss3_3;

namespace MyNamespace.Strategies.Orderflow
{
    /// <summary>
    /// Definiert einen Vertrag für alle spezifischen Orderflow-Muster-Evaluatoren.
    /// Jeder Evaluator ist für die Erkennung eines bestimmten Musters verantwortlich.
    /// </summary>
    public interface IPatternEvaluator
    {
        /// <summary>
        /// Der Typ des Orderflow-Muster, das dieser Evaluator bewertet.
        /// </summary>
        OrderflowPatternType Type { get; }
        OrderDirections Direction { get; } // Ob der Evaluator für Long oder Short ist
        /// <summary>
        /// Bewertet, ob das spezifische Orderflow-Muster basierend auf den bereitgestellten Daten erkannt wird.
        /// </summary>
        /// <param name="currentSnapshot">Der aktuelle Orderflow-Snapshot des Balkens.</param>
        /// <param name="features">Die angereicherten Orderflow-Features des aktuellen Balkens.</param>
        /// <param name="history">Zugriff auf die historische Orderflow-Daten.</param>
        /// <param name="thresholds">Die adaptiven Schwellenwerte für die Orderflow-Analyse.</param>
        /// <param name="config">Die spezifische Konfiguration für diesen Muster-Evaluator.</param>
        /// <param name="currentMarketRegime">Das aktuelle globale Marktregime (z.B. Fast, Normal, Slow).</param>
        /// <param name="currentMarketState">Der aktuelle Zustand des Marktes, abgeleitet vom MarketPatternStateEngine (kommt in Phase 2).</param>
        /// <returns>Ein PatternEvaluationResult, das detailliert die Bewertung des Musters enthält.</returns>
        PatternEvaluationResult Evaluate(
            OvSnapshot currentSnapshot,
            OfFeatures features,
            OfFeaturesHistory history,
            OrderflowThresholds thresholds,
            SetupConditionConfig config,
            MarketRegime currentVolatilityRegime, // Umbenannt zur Klarheit
            MarketDirectionalBias currentDirectionalBias, // Neu
            MarketState currentMarketState,       // Aktualisierte Klasse
            MarketStructureContext currentMarketStructureContext // Neu
        );
    }
}


