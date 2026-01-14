using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ATAS.DataFeedsCore;
using static MyNamespace.Strategies.Goldfluss3_3;
using MyNamespace.Strategies.Orderflow;
using System.ComponentModel.DataAnnotations;
using OFTParameter = OFT.Attributes.ParameterAttribute;

namespace MyNamespace.Strategies.Models
{
    // Macht die Klasse im PropertyGrid aufklappbar — wenn die Hauptstrategie eine
    // Property vom Typ OrderflowThresholds expose't, erscheinen die konfigurierbaren
    // Reversal-Properties direkt in der Hauptstrategie (unabhängig von SetupConfiguration).
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class OrderflowThresholds : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // ---------------------------------------------------------
        // Nicht-sichtbare / interne Properties (werden im PropertyGrid
        // ausgeblendet). Nur die Reversal-Properties weiter unten sind sichtbar.
        // ---------------------------------------------------------

        // Volumen- / Tempo- / Orderflow-Metriken (intern)
        [Browsable(false)]
        public decimal? ThVolBurstZ { get; set; }
        [Browsable(false)]
        public decimal? ThVolPerSecond { get; set; }
        [Browsable(false)]
        public decimal? ThTradeRateZBreakout { get; set; }
        [Browsable(false)]
        public decimal? ThBarDeltaPerVolume { get; set; }

        // CVD / Delta / Coherence (intern)
        [Browsable(false)]
        public decimal? ThCvdCoherence { get; set; }
        [Browsable(false)]
        public decimal? ThCvdImpulseLong { get; set; }
        [Browsable(false)]
        public decimal? ThCvdImpulseShort { get; set; }
        [Browsable(false)]
        public bool IsCvdStable { get; set; } = true;

        // Aggressions-/Pressure-Metriken (intern)
        [Browsable(false)]
        public decimal? ThAggPressureBreakoutBull { get; set; }
        [Browsable(false)]
        public decimal? ThAggPressureBreakoutBear { get; set; }

        // Effizienz / Absorption (intern)
        [Browsable(false)]
        public decimal? ThEfficiency { get; set; }
        [Browsable(false)]
        public decimal? ThAbsorptionEfficiencyMax { get; set; }
        [Browsable(false)]
        public int? ThAbsorptionMaxTicksFromExtreme { get; set; }

        // Alias (intern)
        [Browsable(false)]
        public decimal? ThEff
        {
            get => ThEfficiency;
            set => ThEfficiency = value;
        }

        // Max Counter-Delta Share (intern)
        [Browsable(false)]
        public decimal? MaxCounterDeltaShareBull { get; set; }
        [Browsable(false)]
        public decimal? MaxCounterDeltaShareBear { get; set; }

        // InterTradeTime (intern)
        [Browsable(false)]
        public decimal? ThInterTradeTimeZBull { get; set; }
        [Browsable(false)]
        public decimal? ThInterTradeTimeZBear { get; set; }

        [Browsable(false)]
        public decimal? ThInterTradeZBull
        {
            get => ThInterTradeTimeZBull;
            set => ThInterTradeTimeZBull = value;
        }
        [Browsable(false)]
        public decimal? ThInterTradeZBear
        {
            get => ThInterTradeTimeZBear;
            set => ThInterTradeTimeZBear = value;
        }

        // Sweep- / Sweep-Logik (intern)
        [Browsable(false)]
        public bool? RequireSweepUp { get; set; }
        [Browsable(false)]
        public bool? RequireSweepDown { get; set; }

        [Browsable(false)]
        public int? SweepMinLevels { get; set; }
        [Browsable(false)]
        public int? SweepMaxSeconds { get; set; }
        [Browsable(false)]
        public decimal? SweepMinAggressorRatio { get; set; }

        // Min signals (intern)
        [Browsable(false)]
        public int MinSignalsRequired { get; set; } = 1;

        // Stacked imbalance (intern)
        [Browsable(false)]
        public int? ThStackedImbAnchoredRangeMinDirectional { get; set; }
        [Browsable(false)]
        public int? ThStackedImbAnyRangeMinDirectional { get; set; }
        [Browsable(false)]
        public int? ThOppositeAnchoredWeakMax { get; set; }

        // Reversal blockers (intern copies — visible Reversal-* variants below
        // werden bevorzugt für Konfiguration verwendet)
        [Browsable(false)]
        public decimal? ThCvdImpulseMinForLongReversal { get; set; }
        [Browsable(false)]
        public decimal? ThCvdImpulseMaxForShortReversal { get; set; }

        [Browsable(false)]
        public int? ThPersistBull { get; set; }
        [Browsable(false)]
        public int? ThPersistBear { get; set; }

        // ---------------------------------------------------------
        // Sichtbare Reversal-Properties (für Strategy-Konfiguration).
        // Diese sind double + DefaultValue(double.NaN) — editierbar im PropertyGrid.
        // Die alten nullable Properties bleiben als Browsable(false)-Wrapper erhalten.
        // ---------------------------------------------------------

        // CVD Impulse Long (visible as double)
        [Category("Reversal")]
        [DisplayName("Reversal: CVD Impulse Long")]
        [Description("Spezielle CVD-Impulse-Schwelle für Reversal-Long-Matching.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(59.0)]
        public double ReversalThCvdImpulseLong_UI { get; set; } = 59.0;

        // unsichtbarer Wrapper für Business-Logik (decimal?)
        [Browsable(false)]
        public decimal? ReversalThCvdImpulseLong
        {
            get => (decimal?)ReversalThCvdImpulseLong_UI;
            set
            {
                if (value.HasValue)
                    ReversalThCvdImpulseLong_UI = (double)value.Value;
                else
                    ReversalThCvdImpulseLong_UI = 59.0;
            }
        }

        // CVD Impulse Short
        [Category("Reversal")]
        [DisplayName("Reversal: CVD Impulse Short")]
        [Description("Spezielle CVD-Impulse-Schwelle für Reversal-Short-Matching. Überschreibt ggf. ThCvdImpulseShort für Reversal-Checks.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(double.NaN)]
        public double ReversalThCvdImpulseShort_UI { get; set; } = -136;

        [Browsable(false)]
        public decimal? ReversalThCvdImpulseShort
        {
            get => double.IsNaN(ReversalThCvdImpulseShort_UI) ? (decimal?)null : (decimal)ReversalThCvdImpulseShort_UI;
            set
            {
                if (value.HasValue)
                    ReversalThCvdImpulseShort_UI = (double)value.Value;
                else
                    ReversalThCvdImpulseShort_UI = -136;
            }
        }

        // CVD Impulse Min For Long Reversal (blocker)
        [Category("Reversal")]
        [DisplayName("Reversal: CVD Impulse Min For Long Reversal")]
        [Description("Harter Min-Blocker für Long-Reversal (falls gesetzt). Wenn gesetzt, blockiert Long-Reversals unter diesem Wert.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(double.NaN)]
        public double ReversalThCvdImpulseMinForLongReversal_UI { get; set; } = -50;

        [Browsable(false)]
        public decimal? ReversalThCvdImpulseMinForLongReversal
        {
            get => double.IsNaN(ReversalThCvdImpulseMinForLongReversal_UI) ? (decimal?)null : (decimal)ReversalThCvdImpulseMinForLongReversal_UI;
            set
            {
                if (value.HasValue)
                    ReversalThCvdImpulseMinForLongReversal_UI = (double)value.Value;
                else
                    ReversalThCvdImpulseMinForLongReversal_UI = -50;
            }
        }

        // CVD Impulse Max For Short Reversal (blocker)
        [Category("Reversal")]
        [DisplayName("Reversal: CVD Impulse Max For Short Reversal")]
        [Description("Harter Max-Blocker für Short-Reversal (falls gesetzt). Wenn gesetzt, blockiert Short-Reversals oberhalb dieses Wertes.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(double.NaN)]
        public double ReversalThCvdImpulseMaxForShortReversal_UI { get; set; } = double.NaN;

        [Browsable(false)]
        public decimal? ReversalThCvdImpulseMaxForShortReversal
        {
            get => double.IsNaN(ReversalThCvdImpulseMaxForShortReversal_UI) ? (decimal?)null : (decimal)ReversalThCvdImpulseMaxForShortReversal_UI;
            set
            {
                if (value.HasValue)
                    ReversalThCvdImpulseMaxForShortReversal_UI = (double)value.Value;
                else
                    ReversalThCvdImpulseMaxForShortReversal_UI = double.NaN;
            }
        }

        // AggPressure Breakout (Bull)
        [Category("Reversal")]
        [DisplayName("Reversal: AggPressure Breakout (Bull)")]
        [Description("Aggression-Pressure Schwelle (Bull) für Reversal-Checks.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(double.NaN)]
        public double ReversalThAggPressureBreakoutBull_UI { get; set; } = -0.79;

        [Browsable(false)]
        public decimal? ReversalThAggPressureBreakoutBull
        {
            get => double.IsNaN(ReversalThAggPressureBreakoutBull_UI) ? (decimal?)null : (decimal)ReversalThAggPressureBreakoutBull_UI;
            set
            {
                if (value.HasValue)
                    ReversalThAggPressureBreakoutBull_UI = (double)value.Value;
                else
                    ReversalThAggPressureBreakoutBull_UI = -0.79;
            }
        }

        // AggPressure Breakout (Bear)
        [Category("Reversal")]
        [DisplayName("Reversal: AggPressure Breakout (Bear)")]
        [Description("Aggression-Pressure Schwelle (Bear) für Reversal-Checks.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(double.NaN)]
        public double ReversalThAggPressureBreakoutBear_UI { get; set; } = double.NaN;

        [Browsable(false)]
        public decimal? ReversalThAggPressureBreakoutBear
        {
            get => double.IsNaN(ReversalThAggPressureBreakoutBear_UI) ? (decimal?)null : (decimal)ReversalThAggPressureBreakoutBear_UI;
            set
            {
                if (value.HasValue)
                    ReversalThAggPressureBreakoutBear_UI = (double)value.Value;
                else
                    ReversalThAggPressureBreakoutBear_UI = double.NaN;
            }
        }

        // NEU: Range-Bar-Filterung
        [Category("Reversal")]
        [DisplayName("Range-Bar: Trend-Bar Size (Ticks)")]
        [Description("Größe eines Trend-Bars in Ticks für Range-Markt-Erkennung.")]
        [Range(1, 100)]
        [DefaultValue(6)]
        public int RangeBarTrendSizeTicks_UI { get; set; } = 5;

        [Browsable(false)]
        public int RangeBarTrendSizeTicks
        {
            get => RangeBarTrendSizeTicks_UI;
            set => RangeBarTrendSizeTicks_UI = value;
        }

        [Category("Reversal")]
        [DisplayName("Range-Bar: Reversal-Bar Size (Ticks)")]
        [Description("Größe eines Reversal-Bars in Ticks für Range-Markt-Erkennung.")]
        [Range(1, 100)]
        [DefaultValue(9)]
        public int RangeBarReversalSizeTicks_UI { get; set; } = 8;

        [Browsable(false)]
        public int RangeBarReversalSizeTicks
        {
            get => RangeBarReversalSizeTicks_UI;
            set => RangeBarReversalSizeTicks_UI = value;
        }

        [Category("Reversal")]
        [DisplayName("Range-Bar: Min Consecutive Trend Bars")]
        [Description("Mindestanzahl aufeinanderfolgender Trend-Bars vor einem Reversal.")]
        [Range(1, 10)]
        [DefaultValue(2)]
        public int RangeBarMinConsecutiveTrendBars_UI { get; set; } = 2;

        [Browsable(false)]
        public int RangeBarMinConsecutiveTrendBars
        {
            get => RangeBarMinConsecutiveTrendBars_UI;
            set => RangeBarMinConsecutiveTrendBars_UI = value;
        }

        [Category("Reversal")]
        [DisplayName("Range-Bar: Max Alternating Pattern Length")]
        [Description("Maximale Länge von alternierenden Bull/Bear-Bars, die als Range gelten.")]
        [Range(2, 20)]
        [DefaultValue(8)]
        public int RangeBarMaxAlternatingLength_UI { get; set; } = 8;

        [Browsable(false)]
        public int RangeBarMaxAlternatingLength
        {
            get => RangeBarMaxAlternatingLength_UI;
            set => RangeBarMaxAlternatingLength_UI = value;
        }

        // NEU: Entry-Fenster nach Reversal
        [Category("Reversal")]
        [DisplayName("Range-Bar: Max Bars After Reversal")]
        [Description("Maximale Anzahl von Bars nach einem Reversal, in denen ein Entry erlaubt ist.")]
        [Range(1, 10)]
        [DefaultValue(3)]
        public int RangeBarMaxBarsAfterReversal_UI { get; set; } = 3;

        [Browsable(false)]
        public int RangeBarMaxBarsAfterReversal
        {
            get => RangeBarMaxBarsAfterReversal_UI;
            set => RangeBarMaxBarsAfterReversal_UI = value;
        }

        
        // Max CounterDelta Share (Bull)
        [Category("Reversal")]
        [DisplayName("Reversal: Max CounterDelta Share (Bull)")]
        [Description("Maximaler Counter-Delta-Anteil für Reversal-Checks (Bull).")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(double.NaN)]
        public double ReversalMaxCounterDeltaShareBull_UI { get; set; } = double.NaN;

        [Browsable(false)]
        public decimal? ReversalMaxCounterDeltaShareBull
        {
            get => double.IsNaN(ReversalMaxCounterDeltaShareBull_UI) ? (decimal?)null : (decimal)ReversalMaxCounterDeltaShareBull_UI;
            set
            {
                if (value.HasValue)
                    ReversalMaxCounterDeltaShareBull_UI = (double)value.Value;
                else
                    ReversalMaxCounterDeltaShareBull_UI = double.NaN;
            }
        }

        // Max CounterDelta Share (Bear)
        [Category("Reversal")]
        [DisplayName("Reversal: Max CounterDelta Share (Bear)")]
        [Description("Maximaler Counter-Delta-Anteil für Reversal-Checks (Bear).")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(double.NaN)]
        public double ReversalMaxCounterDeltaShareBear_UI { get; set; } = double.NaN;

        [Browsable(false)]
        public decimal? ReversalMaxCounterDeltaShareBear
        {
            get => double.IsNaN(ReversalMaxCounterDeltaShareBear_UI) ? (decimal?)null : (decimal)ReversalMaxCounterDeltaShareBear_UI;
            set
            {
                if (value.HasValue)
                    ReversalMaxCounterDeltaShareBear_UI = (double)value.Value;
                else
                    ReversalMaxCounterDeltaShareBear_UI = double.NaN;
            }
        }

        // Vol Burst Z
        [Category("Reversal")]
        [DisplayName("Reversal: Vol Burst Z")]
        [Description("VolBurst Z Schwelle, nur für Reversal-Matching.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(double.NaN)]
        public double ReversalThVolBurstZ_UI { get; set; } = 0.36;

        [Browsable(false)]
        public decimal? ReversalThVolBurstZ
        {
            get => double.IsNaN(ReversalThVolBurstZ_UI) ? (decimal?)null : (decimal)ReversalThVolBurstZ_UI;
            set
            {
                if (value.HasValue)
                    ReversalThVolBurstZ_UI = (double)value.Value;
                else
                    ReversalThVolBurstZ_UI = 0.36;
            }
        }

        // Efficiency
        [Category("Reversal")]
        [DisplayName("Reversal: Efficiency")]
        [Description("Effizienz-Schwelle, nur für Reversal-Matching.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(double.NaN)]
        public double ReversalThEfficiency_UI { get; set; } = 0.06;

        [Browsable(false)]
        public decimal? ReversalThEfficiency
        {
            get => double.IsNaN(ReversalThEfficiency_UI) ? (decimal?)null : (decimal)ReversalThEfficiency_UI;
            set
            {
                if (value.HasValue)
                    ReversalThEfficiency_UI = (double)value.Value;
                else
                    ReversalThEfficiency_UI = 0.06;
            }
        }

        // StackedImb Anchored Range Min Directional (use double UI, integer wrapper)
        [Category("Reversal")]
        [DisplayName("Reversal: StackedImb Anchored Range Min Directional")]
        [Description("Reversal-spezifischer StackedImbalance Anchored Range Minimum (directional).")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(double.NaN)]
        public double ReversalThStackedImbAnchoredRangeMinDirectional_UI { get; set; } = 5;

        [Browsable(false)]
        public int? ReversalThStackedImbAnchoredRangeMinDirectional
        {
            get => double.IsNaN(ReversalThStackedImbAnchoredRangeMinDirectional_UI) ? (int?)null : Convert.ToInt32(ReversalThStackedImbAnchoredRangeMinDirectional_UI);
            set
            {
                if (value.HasValue)
                    ReversalThStackedImbAnchoredRangeMinDirectional_UI = value.Value;
                else
                    ReversalThStackedImbAnchoredRangeMinDirectional_UI = 5;
            }
        }

        // StackedImb Any Range Min Directional (double UI, int wrapper)
        [Category("Reversal")]
        [DisplayName("Reversal: StackedImb Any Range Min Directional")]
        [Description("Reversal-spezifischer StackedImbalance Any Range Minimum (directional).")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(double.NaN)]
        public double ReversalThStackedImbAnyRangeMinDirectional_UI { get; set; } = 1;

        [Browsable(false)]
        public int? ReversalThStackedImbAnyRangeMinDirectional
        {
            get => double.IsNaN(ReversalThStackedImbAnyRangeMinDirectional_UI) ? (int?)null : Convert.ToInt32(ReversalThStackedImbAnyRangeMinDirectional_UI);
            set
            {
                if (value.HasValue)
                    ReversalThStackedImbAnyRangeMinDirectional_UI = value.Value;
                else
                    ReversalThStackedImbAnyRangeMinDirectional_UI = 1;
            }
        }

        public decimal? TickSizeDecimal { get; set; } = 0.25m;

        // -----------------------------------------
        // Rückwärtskompatible "Effective" Properties:
        // Diese lesen Reversal-Value wenn gesetzt, sonst die internen
        // (nicht-sichtbaren) klassischen Properties.
        // -----------------------------------------

        [Browsable(false)]
        public decimal? EffectiveThCvdImpulseLong
        {
            get => ReversalThCvdImpulseLong ?? ThCvdImpulseLong;
            set
            {
                ReversalThCvdImpulseLong = value;
                ThCvdImpulseLong = value;
            }
        }

        [Browsable(false)]
        public decimal? EffectiveThCvdImpulseShort
        {
            get => ReversalThCvdImpulseShort ?? ThCvdImpulseShort;
            set
            {
                ReversalThCvdImpulseShort = value;
                ThCvdImpulseShort = value;
            }
        }

        [Browsable(false)]
        public decimal? EffectiveThAggPressureBreakoutBull
        {
            get => ReversalThAggPressureBreakoutBull ?? ThAggPressureBreakoutBull;
            set
            {
                ReversalThAggPressureBreakoutBull = value;
                ThAggPressureBreakoutBull = value;
            }
        }

        [Browsable(false)]
        public decimal? EffectiveThAggPressureBreakoutBear
        {
            get => ReversalThAggPressureBreakoutBear ?? ThAggPressureBreakoutBear;
            set
            {
                ReversalThAggPressureBreakoutBear = value;
                ThAggPressureBreakoutBear = value;
            }
        }

        [Browsable(false)]
        public decimal? EffectiveThVolBurstZ
        {
            get => ReversalThVolBurstZ ?? ThVolBurstZ;
            set
            {
                ReversalThVolBurstZ = value;
                ThVolBurstZ = value;
            }
        }

        [Browsable(false)]
        public decimal? EffectiveThEfficiency
        {
            get => ReversalThEfficiency ?? ThEfficiency;
            set
            {
                ReversalThEfficiency = value;
                ThEfficiency = value;
            }
        }

        [Browsable(false)]
        public int? EffectiveThStackedImbAnchoredRangeMinDirectional
        {
            get => ReversalThStackedImbAnchoredRangeMinDirectional ?? ThStackedImbAnchoredRangeMinDirectional;
            set
            {
                ReversalThStackedImbAnchoredRangeMinDirectional = value;
                ThStackedImbAnchoredRangeMinDirectional = value;
            }
        }

        [Browsable(false)]
        public int? EffectiveThStackedImbAnyRangeMinDirectional
        {
            get => ReversalThStackedImbAnyRangeMinDirectional ?? ThStackedImbAnyRangeMinDirectional;
            set
            {
                ReversalThStackedImbAnyRangeMinDirectional = value;
                ThStackedImbAnyRangeMinDirectional = value;
            }
        }

        // ---------------------------------------------------------
        // Clone / Kopieren
        // ---------------------------------------------------------
        public OrderflowThresholds Clone()
        {
            return new OrderflowThresholds
            {
                // interne Werte kopieren
                ThVolBurstZ = this.ThVolBurstZ,
                ThVolPerSecond = this.ThVolPerSecond,
                ThTradeRateZBreakout = this.ThTradeRateZBreakout,
                ThBarDeltaPerVolume = this.ThBarDeltaPerVolume,

                ThCvdCoherence = this.ThCvdCoherence,
                ThCvdImpulseLong = this.ThCvdImpulseLong,
                ThCvdImpulseShort = this.ThCvdImpulseShort,
                IsCvdStable = this.IsCvdStable,

                ThAggPressureBreakoutBull = this.ThAggPressureBreakoutBull,
                ThAggPressureBreakoutBear = this.ThAggPressureBreakoutBear,

                ThEfficiency = this.ThEfficiency,
                ThAbsorptionEfficiencyMax = this.ThAbsorptionEfficiencyMax,
                ThAbsorptionMaxTicksFromExtreme = this.ThAbsorptionMaxTicksFromExtreme,

                MaxCounterDeltaShareBull = this.MaxCounterDeltaShareBull,
                MaxCounterDeltaShareBear = this.MaxCounterDeltaShareBear,

                ThInterTradeTimeZBull = this.ThInterTradeTimeZBull,
                ThInterTradeTimeZBear = this.ThInterTradeTimeZBear,

                RequireSweepUp = this.RequireSweepUp,
                RequireSweepDown = this.RequireSweepDown,
                SweepMinLevels = this.SweepMinLevels,
                SweepMaxSeconds = this.SweepMaxSeconds,
                SweepMinAggressorRatio = this.SweepMinAggressorRatio,

                MinSignalsRequired = this.MinSignalsRequired,

                ThStackedImbAnchoredRangeMinDirectional = this.ThStackedImbAnchoredRangeMinDirectional,
                ThStackedImbAnyRangeMinDirectional = this.ThStackedImbAnyRangeMinDirectional,
                ThOppositeAnchoredWeakMax = this.ThOppositeAnchoredWeakMax,

                ThCvdImpulseMinForLongReversal = this.ThCvdImpulseMinForLongReversal,
                ThCvdImpulseMaxForShortReversal = this.ThCvdImpulseMaxForShortReversal,

                ThPersistBull = this.ThPersistBull,
                ThPersistBear = this.ThPersistBear,

                // Reversal-sichtbare Werte (kopiere Wrapper-Werte)
                ReversalThCvdImpulseLong = this.ReversalThCvdImpulseLong,
                ReversalThCvdImpulseShort = this.ReversalThCvdImpulseShort,

                ReversalThCvdImpulseMinForLongReversal = this.ReversalThCvdImpulseMinForLongReversal,
                ReversalThCvdImpulseMaxForShortReversal = this.ReversalThCvdImpulseMaxForShortReversal,

                ReversalThAggPressureBreakoutBull = this.ReversalThAggPressureBreakoutBull,
                ReversalThAggPressureBreakoutBear = this.ReversalThAggPressureBreakoutBear,

                ReversalMaxCounterDeltaShareBull = this.ReversalMaxCounterDeltaShareBull,
                ReversalMaxCounterDeltaShareBear = this.ReversalMaxCounterDeltaShareBear,

                ReversalThVolBurstZ = this.ReversalThVolBurstZ,
                ReversalThEfficiency = this.ReversalThEfficiency,

                ReversalThStackedImbAnchoredRangeMinDirectional = this.ReversalThStackedImbAnchoredRangeMinDirectional,
                ReversalThStackedImbAnyRangeMinDirectional = this.ReversalThStackedImbAnyRangeMinDirectional,
            };
        }

    }

}



