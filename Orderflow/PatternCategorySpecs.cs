using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MyNamespace.Strategies.Models;
// using ATAS.DataFeedsCore; // Vermutlich nicht direkt hier ben�tigt, aber falls ja, beibehalten
// using MyNamespace.Strategies.Models; // Behalten, falls du Modelle hier verwendest
// using static MyNamespace.Strategies.Goldfluss3_3; // Behalten, falls du statische Member der Strategie hier verwendest


namespace MyNamespace.Strategies.Orderflow
{

    // Definiert wie Ihre Gesamtstrategie derzeit agiert(z.B.risikoreicher, schneller), unabh�ngig vom spezifischen Orderflow-Muster.    
    public class ModeSpec
    {
        // Beispiel: Placeholder f�r Modus-spezifische Parameter Ihrer GESAMTEN STRATEGIE.
        // NICHT f�r Orderflow-Muster-Gating-Metriken.
        public bool IsFastModeActive { get; set; } = false;
        public decimal GlobalRiskMultiplier { get; set; } = 1.0m;
        public int GlobalMinSignalsRequired { get; set; } = 2; // Dies k�nnte die alte MinSignalsRequired ersetzen, aber als globaler Wert

        // F�gen Sie hier weitere Parameter hinzu, die den aktuellen Betriebsmodus der Strategie beschreiben
        // (z.B. Lookback-Perioden, Schwellenwerte, etc., die sich mit dem "Modus" Ihrer Strategie �ndern)
    }

    // --- ModeSpecsEntry (Vorlagen f�r harte/relevante Metriken nach Kategorie) ---
    // Die Eigenschaft 'Mode' wurde entfernt, da die Kategorie bereits durch den Schl�ssel im Map-Dictionary gegeben ist.
    public class ModeSpecsEntry
    {
        public GateMetric HardRequired { get; set; }
        public GateMetric Relevant { get; set; }
        public override string ToString() => $"Relevant: {Relevant}";
    }


    // Definiert was ein Reversal, Breakout, etc. ist, in Bezug auf Orderflow-Metriken.
    // Umbenannt von ModeSpecs zu PatternCategorySpecs
    public static class PatternCategorySpecs
    {
        // GateMetric-Enum (zur Referenz):
        // None, VolBurstZ, AggPressureDirectional, CvdImpulse, CvdCoherence,
        // TradeRateZ, InterTradeTimeZ, Efficiency, StackedImbalanceAnyDirectional,
        // StackedImbalanceAnchoredDirectional, StackedImbalanceOppositeAnchoredWeak,
        // MaxCounterDeltaShare, BarDeltaPerVolume, VolPerSecond, All

        public static readonly Dictionary<PatternCategory, ModeSpecsEntry> Map =
            new Dictionary<PatternCategory, ModeSpecsEntry>
        {
            // BREAKOUT:
            // - HardRequired: VolBurstZ + TradeRateZ (ohne Volumenimpuls / erh�hte Trade-Rate kein echter Breakout)
            // - Relevant: Volumenimpulse, TradeRate, Delta-Proxy, Imbalance
            {
                PatternCategory.Breakout,
                new ModeSpecsEntry
                {
                    HardRequired =
                        GateMetric.VolBurstZ
                        | GateMetric.TradeRateZ,

                    Relevant =
                        GateMetric.VolBurstZ
                        | GateMetric.CvdImpulse
                        | GateMetric.TradeRateZ
                        | GateMetric.AggPressureDirectional
                        | GateMetric.StackedImbalanceAnyDirectional
                }
            },

            // MEAN REVERSION:
            // - HardRequired: InterTradeTimeZ (Trade-Tempo / "Verlangsamung" ist Kernsignal)
            // - Relevant: ITT, Delta-Proxy, Effizienz/Volumen
            {
                PatternCategory.MeanReversion,
                new ModeSpecsEntry
                {
                    HardRequired =
                        GateMetric.InterTradeTimeZ,

                    Relevant =
                        GateMetric.InterTradeTimeZ
                        | GateMetric.BarDeltaPerVolume
                        | GateMetric.Efficiency
                        | GateMetric.VolPerSecond
                }
            },

            // REVERSAL:
            // - HardRequired: BarDeltaPerVolume (Orderflow-Drehung) + AggPressureDirectional (Wechsel der Aggression)
            // - Relevant: Delta-Proxy, Aggressionsrichtung, CVD-Coherence, VolBurst, ITT, Imbalance
            {
                PatternCategory.Reversal,
                new ModeSpecsEntry
                {
                    HardRequired =
                        GateMetric.BarDeltaPerVolume
                        | GateMetric.AggPressureDirectional
                        | GateMetric.CvdImpulse,

                    Relevant =
                        GateMetric.BarDeltaPerVolume
                        | GateMetric.AggPressureDirectional
                        | GateMetric.VolBurstZ
                        | GateMetric.CvdCoherence
                        | GateMetric.InterTradeTimeZ
                        | GateMetric.StackedImbalanceAnyDirectional
                        | GateMetric.Efficiency
                }
            },

            // CONTINUATION (Trendfortsetzung):
            // - HardRequired: VolBurstZ + TradeRateZ (Trend braucht Impuls + anhaltende Aktivit�t)
            // - Relevant: wie Breakout, aber ohne Imbalance-Pflicht, eher Flow-/Tempo-orientiert
            {
                PatternCategory.Continuation,
                new ModeSpecsEntry
                {
                    HardRequired =
                        GateMetric.VolBurstZ
                        | GateMetric.TradeRateZ,

                    Relevant =
                        GateMetric.VolBurstZ
                        | GateMetric.TradeRateZ
                        | GateMetric.InterTradeTimeZ
                        | GateMetric.BarDeltaPerVolume
                        | GateMetric.VolPerSecond
                }
            },

            // UNKNOWN:
            // - keine HardRequired (Default-Kategorie)
            // - kleines, robustes Basisset
            {
                PatternCategory.Unknown,
                new ModeSpecsEntry
                {
                    HardRequired = GateMetric.None,
                    Relevant =
                        GateMetric.BarDeltaPerVolume
                        | GateMetric.AggPressureDirectional
                        | GateMetric.VolBurstZ
                }
            }
        };
    }
}

