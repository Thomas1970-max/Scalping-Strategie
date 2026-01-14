using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using static MyNamespace.Strategies.Goldfluss3_3;

namespace MyNamespace.Strategies.Orderflow
{
    /// <summary>
    /// Konfigurationsklasse für die spezifischen Bedingungen und muster-spezifischen Parameter
    /// eines einzelnen Orderflow-Musters.
    /// </summary>
    public class SetupConditionConfig
    {
        // Allgemeine Muster-spezifische Entry-Kriterien, die zuvor in SetupConfiguration waren
        public decimal MinEfficiencyForEntry { get; set; } = 0.6m; // Mindesteffizienz für Entry (z.B. 60%)
        public decimal MaxEfficiencyForEntry { get; set; } = 0.8m; // Maximale Effizienz für Short-Entry (für andere Setups)
        public decimal MinCvdImpulseLong { get; set; } = 0.5m;    // Mindest-CVD-Impuls für Long
        public decimal MaxCvdImpulseShort { get; set; } = -0.5m;  // Maximaler (negativer) CVD-Impuls für Short
        public decimal MinSlopeCvdLong { get; set; } = 0.05m;     // Mindeststeigung des CVD (positiv) für Long
        public decimal MaxSlopeCvdShort { get; set; } = -0.05m;   // Maximal (negativ) Steigung des CVD für Short
        public decimal RequiredVolBurstZ { get; set; } = 2.0m;    // Könnte auch in MinConfidenceForEntry umgewandelt werden

        // --- Parameter für die Mustererkennung (Phase 1) - spezifisch pro Muster ---
        // Dies sind jetzt direkt Eigenschaften dieser SetupConditionConfig Instanz.

        // --- Reversal/Bounce Muster (Long/Short) ---
        public int ReversalAggPressureLookback { get; set; } = 5;
        public decimal ReversalAggPressureMinShare { get; set; } = 0.6m;
        public int ReversalStackedImbMinRange { get; set; } = 2;
        public decimal ReversalAbsorptionEfficiencyMax { get; set; } = 0.3m;
        public decimal ReversalAbsorptionMinTradeRateZ { get; set; } = -1.0m;
        public decimal ReversalAbsorptionMinCvdImpulseMagnitude { get; set; } = 0.2m;
        public decimal ReversalMinConfidenceForEntry { get; set; } = 0.7m; // Neu: MinConfidence für Reversal-Muster

        // --- Breakout Muster (Long/Short) ---
        public int BreakoutBurstWindowBars { get; set; } = 3;
        public int BreakoutConfirmBars { get; set; } = 2;
        public decimal BreakoutAggPressureStrengthEpsilon { get; set; } = 0.01m;
        public decimal BreakoutMinCvdImpulseLong { get; set; } = 0.8m;
        public decimal BreakoutMaxCvdImpulseShort { get; set; } = -0.8m;
        public decimal BreakoutMinEfficiency { get; set; } = 0.7m;
        public decimal BreakoutMinSlopeCvdLong { get; set; } = 0.07m;
        public decimal BreakoutMaxSlopeCvdShort { get; set; } = -0.07m;
        public decimal BreakoutMinTradeRateZ { get; set; } = 1.0m;
        public decimal BreakoutMinAggPressureMagnitude { get; set; } = 0.05m;
        public decimal BreakoutMinConfidenceForEntry { get; set; } = 0.75m; // Neu: MinConfidence für Breakout-Muster


        // --- Trend Continuation Muster (Long/Short) ---
        public int TrendContinuationPullbackBars { get; set; } = 3;
        public decimal TrendContinuationPullbackMaxRetracement { get; set; } = 0.3m;
        public decimal TrendContinuationCvdCoherenceMin { get; set; } = 0.6m;
        public decimal TrendContinuationMinCvdImpulseLong { get; set; } = 0.4m;
        public decimal TrendContinuationMaxCvdImpulseShort { get; set; } = -0.4m;
        public decimal TrendContinuationMinEfficiency { get; set; } = 0.5m;
        public decimal TrendContinuationMinSlopeCvdLong { get; set; } = 0.03m;
        public decimal TrendContinuationMaxSlopeCvdShort { get; set; } = -0.03m;
        public decimal TrendContinuationMinTradeRateZ { get; set; } = 0.5m;
        public decimal TrendContinuationMinAggPressureMagnitude { get; set; } = 0.02m;
        public decimal TrendContinuationMinConfidenceForEntry { get; set; } = 0.65m; // Neu: MinConfidence für Continuation-Muster

        // --- Flags und Parameter für die CommonEntryConditions ---
        // Diese werden jetzt von den IPatternEvaluators intern genutzt, um Komponenten zu aktivieren/deaktivieren
        // oder ihre Logik zu steuern. Die Evaluatoren nutzen diese Werte direkt.

        // AggPressureConsistent
        public bool UseAggPressureConsistent { get; set; } = false;
        public int AggPressureConsistentLookback { get; set; } = 3;
        public decimal AggPressureConsistentMinSharePositive { get; set; } = 0.67m;
        public decimal AggPressureConsistentEpsilon { get; set; } = 0.01m;

        // GetAggPressureStrongInCurrentBar
        public bool UseAggPressureStrongInCurrentBar { get; set; } = false;
        public decimal AggPressureStrongInCurrentBarStrengthEpsilon { get; set; } = 0.20m;

        // VolBurstZHigherThanAvg
        public bool UseVolBurstZHigherThanAvg { get; set; } = false;
        public int VolBurstZAvgLookback { get; set; } = 10;
        public decimal VolBurstZAvgMultiplier { get; set; } = 1.5m;

        // MajorVolBurstAndCvdImpulseConsistency
        public bool UseMajorVolBurstAndCvdImpulseConsistency { get; set; } = false;
        public int MajorVolBurstAndCvdImpulseConsistencyBarsAgoBurst { get; set; } = 1;
        public int MajorVolBurstAndCvdImpulseConsistencyBarsAfterBurst { get; set; } = 0;

        // SweepPattern
        public bool UseSweepPattern { get; set; } = false;

        // InflectionPressureConsistency
        public bool UseInflectionPressureConsistency { get; set; } = false;

        // CvdImpulseRisingSequence
        public bool UseCvdImpulseRisingSequence { get; set; } = false;
        public int CvdImpulseRisingLookback { get; set; } = 3;
        public int CvdImpulseRisingMinPairs { get; set; } = 2;

        // RecentMajorBurstThenCvdConsistency
        public bool UseRecentMajorBurstThenCvdConsistency { get; set; } = false;
        public int RecentMajorBurstWindowBars { get; set; } = 5;
        public int RecentMajorBurstConfirmBars { get; set; } = 2;

        // Stacked Imbalance patterns
        public bool UseStackedImbalanceAnchoredDirectional { get; set; } = false;
        public int StackedImbalanceAnchoredMinRange { get; set; } = 0; // Kein Override, sondern direkter Wert

        public bool UseStackedImbalanceAnyDirectional { get; set; } = false;
        public int StackedImbalanceAnyMinRange { get; set; } = 0;

        public bool UseStackedImbalanceOppositeAnchoredWeak { get; set; } = false;
        public int OppositeAnchoredWeakMax { get; set; } = 0;

        // AbsorptionAtLevel
        public bool UseAbsorptionAtLevel { get; set; } = false;
        public int AbsorptionAnchoredMinRange { get; set; } = 0;
        public decimal AbsorptionEfficiencyMax { get; set; } = 0m;
        public decimal AbsorptionMinTradeRateZ { get; set; } = 0m;
        public decimal AbsorptionMinCvdImpulseMag { get; set; } = 0m;

        public int? MinSignalsRequired { get; set; } = null; // optionaler Override pro Pattern

        /// <summary>
        /// Stellt eine Standard-SetupConditionConfig basierend auf OrderflowPatternType bereit.
        /// Dadurch wird das Konzept des „Template Matching“ für allgemeine Bedingungen zentralisiert.
        /// </summary>
        public static SetupConditionConfig GetDefault(OrderflowPatternType currentPatternType)
        {
            var config = new SetupConditionConfig();

            // Hier werden die Standardeinstellungen für die jeweiligen Mustertypen konfiguriert.
            // Die Werte hier dienen als Ausgangspunkt und können bei Bedarf angepasst werden.
            if (currentPatternType == OrderflowPatternType.PotentialLongReversalBounce ||
            currentPatternType == OrderflowPatternType.PotentialShortReversalBounce)
            {
                // Beispielhaft die Werte, wie du sie hattest, ergänzt um die neuen Confidence-Werte
                config.UseAggPressureStrongInCurrentBar = true;
                config.AggPressureStrongInCurrentBarStrengthEpsilon = 0.15m;
                config.UseInflectionPressureConsistency = true;
                config.UseCvdImpulseRisingSequence = false;
                config.CvdImpulseRisingLookback = 4;
                config.CvdImpulseRisingMinPairs = 3;
                config.UseStackedImbalanceAnchoredDirectional = true;
                config.StackedImbalanceAnchoredMinRange = 2;
                config.UseStackedImbalanceOppositeAnchoredWeak = true;
                config.OppositeAnchoredWeakMax = 1;
                config.UseAbsorptionAtLevel = true; // Beispiel
                config.AbsorptionAnchoredMinRange = 1; // Beispiel
                config.AbsorptionEfficiencyMax = 0.4m; // Beispiel
                config.AbsorptionMinTradeRateZ = -0.5m; // Beispiel
                config.AbsorptionMinCvdImpulseMag = 0.1m; // Beispiel


                config.ReversalAggPressureLookback = 5;
                config.ReversalAggPressureMinShare = 0.6m;
                config.ReversalStackedImbMinRange = 2;
                config.ReversalAbsorptionEfficiencyMax = 0.3m;
                config.ReversalAbsorptionMinTradeRateZ = -1.0m;
                config.ReversalAbsorptionMinCvdImpulseMagnitude = 0.2m;

                config.MinEfficiencyForEntry = 0.6m;
                config.RequiredVolBurstZ = 2.0m;
                config.ReversalMinConfidenceForEntry = 0.7m; // Setzen des spezifischen Confidence-Werts
            }
            else if (currentPatternType == OrderflowPatternType.PotentialLongBreakout ||
                     currentPatternType == OrderflowPatternType.PotentialShortBreakout)
            {
                config.UseVolBurstZHigherThanAvg = true;
                config.VolBurstZAvgLookback = 15;
                config.VolBurstZAvgMultiplier = 2.0m;
                config.UseRecentMajorBurstThenCvdConsistency = true;
                config.RecentMajorBurstWindowBars = 3;
                config.RecentMajorBurstConfirmBars = 1;

                config.BreakoutBurstWindowBars = 3;
                config.BreakoutConfirmBars = 2;
                config.BreakoutAggPressureStrengthEpsilon = 0.01m;
                config.BreakoutMinCvdImpulseLong = 0.8m;
                config.BreakoutMaxCvdImpulseShort = -0.8m;
                config.BreakoutMinEfficiency = 0.7m;
                config.BreakoutMinSlopeCvdLong = 0.07m;
                config.BreakoutMaxSlopeCvdShort = -0.07m;
                config.BreakoutMinTradeRateZ = 1.0m;
                config.BreakoutMinAggPressureMagnitude = 0.05m;
                config.BreakoutMinConfidenceForEntry = 0.75m; // Setzen des spezifischen Confidence-Werts
            }
            else if (currentPatternType == OrderflowPatternType.PotentialLongTrendContinuation ||
                     currentPatternType == OrderflowPatternType.PotentialShortTrendContinuation)
            {
                config.UseCvdImpulseRisingSequence = true;
                config.CvdImpulseRisingLookback = 4;
                config.CvdImpulseRisingMinPairs = 3;
                config.UseAggPressureConsistent = true;
                config.AggPressureConsistentLookback = 5;
                config.AggPressureConsistentMinSharePositive = 0.80m;

                config.TrendContinuationPullbackBars = 3;
                config.TrendContinuationPullbackMaxRetracement = 0.3m;
                config.TrendContinuationCvdCoherenceMin = 0.6m;
                config.TrendContinuationMinCvdImpulseLong = 0.4m;
                config.TrendContinuationMaxCvdImpulseShort = -0.4m;
                config.TrendContinuationMinEfficiency = 0.5m;
                config.TrendContinuationMinSlopeCvdLong = 0.03m;
                config.TrendContinuationMaxSlopeCvdShort = -0.03m;
                config.TrendContinuationMinTradeRateZ = 0.5m;
                config.TrendContinuationMinAggPressureMagnitude = 0.02m;
                config.TrendContinuationMinConfidenceForEntry = 0.65m; // Setzen des spezifischen Confidence-Werts
            }

            return config;
        }

        /// <summary>
        /// Wendet Überschreibungen aus einer vom Benutzer bereitgestellten Konfiguration auf diese Instanz an.
        /// </summary>
        /// <summary>
        /// Wendet Überschreibungen aus einer vom Benutzer bereitgestellten Konfiguration auf diese Instanz an.
        /// Dies wird verwendet, um globale OFTParameter aus der Strategie auf alle Musterkonfigurationen anzuwenden.
        /// </summary>
        public void ApplyOverridesFrom(SetupConditionConfig overrides)
        {
            if (overrides == null) return;

            // Hier werden alle Eigenschaften explizit verglichen und überschrieben,
            // die als globale OFTParameter in Goldfluss3_3 definiert sind.
            // Die 'Use'-Flags sind entscheidend, da sie die Evaluatoren anweisen,
            // bestimmte Komponenten zu aktivieren/deaktivieren.

            // Allgemeine Entry-Kriterien (falls global überschreibbar)
            if (overrides.MinEfficiencyForEntry != new SetupConditionConfig().MinEfficiencyForEntry) MinEfficiencyForEntry = overrides.MinEfficiencyForEntry;
            if (overrides.MaxEfficiencyForEntry != new SetupConditionConfig().MaxEfficiencyForEntry) MaxEfficiencyForEntry = overrides.MaxEfficiencyForEntry;
            if (overrides.MinCvdImpulseLong != new SetupConditionConfig().MinCvdImpulseLong) MinCvdImpulseLong = overrides.MinCvdImpulseLong;
            if (overrides.MaxCvdImpulseShort != new SetupConditionConfig().MaxCvdImpulseShort) MaxCvdImpulseShort = overrides.MaxCvdImpulseShort;
            if (overrides.MinSlopeCvdLong != new SetupConditionConfig().MinSlopeCvdLong) MinSlopeCvdLong = overrides.MinSlopeCvdLong;
            if (overrides.MaxSlopeCvdShort != new SetupConditionConfig().MaxSlopeCvdShort) MaxSlopeCvdShort = overrides.MaxSlopeCvdShort;
            if (overrides.RequiredVolBurstZ != new SetupConditionConfig().RequiredVolBurstZ) RequiredVolBurstZ = overrides.RequiredVolBurstZ;

            // Common Entry Condition Flags und Parameter
            // WICHTIG: Die "Use"-Flags müssen unbedingt überschrieben werden, wenn sie im Override gesetzt sind!
            if (overrides.UseAggPressureConsistent) UseAggPressureConsistent = true;
            if (overrides.AggPressureConsistentLookback != new SetupConditionConfig().AggPressureConsistentLookback) AggPressureConsistentLookback = overrides.AggPressureConsistentLookback;
            if (overrides.AggPressureConsistentMinSharePositive != new SetupConditionConfig().AggPressureConsistentMinSharePositive) AggPressureConsistentMinSharePositive = overrides.AggPressureConsistentMinSharePositive;
            if (overrides.AggPressureConsistentEpsilon != new SetupConditionConfig().AggPressureConsistentEpsilon) AggPressureConsistentEpsilon = overrides.AggPressureConsistentEpsilon;

            if (overrides.UseAggPressureStrongInCurrentBar) UseAggPressureStrongInCurrentBar = true;
            if (overrides.AggPressureStrongInCurrentBarStrengthEpsilon != new SetupConditionConfig().AggPressureStrongInCurrentBarStrengthEpsilon) AggPressureStrongInCurrentBarStrengthEpsilon = overrides.AggPressureStrongInCurrentBarStrengthEpsilon;

            if (overrides.UseVolBurstZHigherThanAvg) UseVolBurstZHigherThanAvg = true;
            if (overrides.VolBurstZAvgLookback != new SetupConditionConfig().VolBurstZAvgLookback) VolBurstZAvgLookback = overrides.VolBurstZAvgLookback;
            if (overrides.VolBurstZAvgMultiplier != new SetupConditionConfig().VolBurstZAvgMultiplier) VolBurstZAvgMultiplier = overrides.VolBurstZAvgMultiplier;

            if (overrides.UseMajorVolBurstAndCvdImpulseConsistency) UseMajorVolBurstAndCvdImpulseConsistency = true;
            if (overrides.MajorVolBurstAndCvdImpulseConsistencyBarsAgoBurst != new SetupConditionConfig().MajorVolBurstAndCvdImpulseConsistencyBarsAgoBurst) MajorVolBurstAndCvdImpulseConsistencyBarsAgoBurst = overrides.MajorVolBurstAndCvdImpulseConsistencyBarsAgoBurst;
            if (overrides.MajorVolBurstAndCvdImpulseConsistencyBarsAfterBurst != new SetupConditionConfig().MajorVolBurstAndCvdImpulseConsistencyBarsAfterBurst) MajorVolBurstAndCvdImpulseConsistencyBarsAfterBurst = overrides.MajorVolBurstAndCvdImpulseConsistencyBarsAfterBurst;

            if (overrides.UseSweepPattern) UseSweepPattern = true;
            if (overrides.UseInflectionPressureConsistency) UseInflectionPressureConsistency = true;
            if (overrides.UseCvdImpulseRisingSequence) UseCvdImpulseRisingSequence = true;
            if (overrides.CvdImpulseRisingLookback != new SetupConditionConfig().CvdImpulseRisingLookback) CvdImpulseRisingLookback = overrides.CvdImpulseRisingLookback;
            if (overrides.CvdImpulseRisingMinPairs != new SetupConditionConfig().CvdImpulseRisingMinPairs) CvdImpulseRisingMinPairs = overrides.CvdImpulseRisingMinPairs;

            if (overrides.UseRecentMajorBurstThenCvdConsistency) UseRecentMajorBurstThenCvdConsistency = true;
            if (overrides.RecentMajorBurstWindowBars != new SetupConditionConfig().RecentMajorBurstWindowBars) RecentMajorBurstWindowBars = overrides.RecentMajorBurstWindowBars;
            if (overrides.RecentMajorBurstConfirmBars != new SetupConditionConfig().RecentMajorBurstConfirmBars) RecentMajorBurstConfirmBars = overrides.RecentMajorBurstConfirmBars;

            if (overrides.UseStackedImbalanceAnchoredDirectional) UseStackedImbalanceAnchoredDirectional = true;
            if (overrides.StackedImbalanceAnchoredMinRange != new SetupConditionConfig().StackedImbalanceAnchoredMinRange) StackedImbalanceAnchoredMinRange = overrides.StackedImbalanceAnchoredMinRange;
            if (overrides.UseStackedImbalanceAnyDirectional) UseStackedImbalanceAnyDirectional = true;
            if (overrides.StackedImbalanceAnyMinRange != new SetupConditionConfig().StackedImbalanceAnyMinRange) StackedImbalanceAnyMinRange = overrides.StackedImbalanceAnyMinRange;
            if (overrides.UseStackedImbalanceOppositeAnchoredWeak) UseStackedImbalanceOppositeAnchoredWeak = true;
            if (overrides.OppositeAnchoredWeakMax != new SetupConditionConfig().OppositeAnchoredWeakMax) OppositeAnchoredWeakMax = overrides.OppositeAnchoredWeakMax;

            if (overrides.UseAbsorptionAtLevel) UseAbsorptionAtLevel = true;
            if (overrides.AbsorptionAnchoredMinRange != new SetupConditionConfig().AbsorptionAnchoredMinRange) AbsorptionAnchoredMinRange = overrides.AbsorptionAnchoredMinRange;
            if (overrides.AbsorptionEfficiencyMax != new SetupConditionConfig().AbsorptionEfficiencyMax) AbsorptionEfficiencyMax = overrides.AbsorptionEfficiencyMax;
            if (overrides.AbsorptionMinTradeRateZ != new SetupConditionConfig().AbsorptionMinTradeRateZ) AbsorptionMinTradeRateZ = overrides.AbsorptionMinTradeRateZ;
            if (overrides.AbsorptionMinCvdImpulseMag != new SetupConditionConfig().AbsorptionMinCvdImpulseMag) AbsorptionMinCvdImpulseMag = overrides.AbsorptionMinCvdImpulseMag;

            // Muster-spezifische Parameter (falls du auch hier globale Overrides erlauben möchtest,
            // z.B. einen globalen MinConfidenceForEntry, der *alle* Muster betrifft)
            // Sonst bleiben diese nur in den GetDefault-Methoden definiert und nur durch individuelle Pattern-OFTParameter änderbar.
            if (overrides.ReversalAggPressureLookback != new SetupConditionConfig().ReversalAggPressureLookback) ReversalAggPressureLookback = overrides.ReversalAggPressureLookback;
            // ... und so weiter für ALLE Eigenschaften, die global überschrieben werden können.
        }
    }
}


