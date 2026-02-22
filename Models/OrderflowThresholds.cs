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
    // Macht die Klasse im PropertyGrid aufklappbar � wenn die Hauptstrategie eine
    // Property vom Typ OrderflowThresholds expose't, erscheinen die konfigurierbaren
    // Reversal-Properties direkt in der Hauptstrategie (unabh�ngig von SetupConfiguration).
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
        public int MinSignalsRequired { get; set; } = 2;

        // Stacked imbalance (intern)
        [Browsable(false)]
        public int? ThStackedImbAnchoredRangeMinDirectional { get; set; }
        [Browsable(false)]
        public int? ThStackedImbAnyRangeMinDirectional { get; set; }
        [Browsable(false)]
        public int? ThOppositeAnchoredWeakMax { get; set; }

        // Imbalance score (intern)
        [Browsable(false)]
        public decimal? ThImbalanceScoreMinLong { get; set; }
        [Browsable(false)]
        public decimal? ThImbalanceScoreMaxShort { get; set; }

        // Reversal blockers (intern copies � visible Reversal-* variants below
        // werden bevorzugt f�r Konfiguration verwendet)
        [Browsable(false)]
        public decimal? ThCvdImpulseMinForLongReversal { get; set; }
        [Browsable(false)]
        public decimal? ThCvdImpulseMaxForShortReversal { get; set; }

        [Browsable(false)]
        public int? ThPersistBull { get; set; }
        [Browsable(false)]
        public int? ThPersistBear { get; set; }

        // ---------------------------------------------------------
        // Sichtbare Reversal-Properties (f�r Strategy-Konfiguration).
        // Diese sind double + DefaultValue(double.NaN) � editierbar im PropertyGrid.
        // Die alten nullable Properties bleiben als Browsable(false)-Wrapper erhalten.
        // ---------------------------------------------------------

        [Category("Continuation")]
        [DisplayName("Continuation: Phase Lookback Bars")]
        [Description("Wie viele Bars rückwirkend Healthy_Pullback oder Momentum_Refuel gewesen sein darf, damit ContinuationPullback-Einstiege weiterhin erlaubt sind.")]
        [DefaultValue(6)]
        public int ContinuationPhaseLookbackBars_UI { get; set; } = 6;

        [Category("Continuation")]
        [DisplayName("Continuation: Compression Gap (Ticks)")]
        [Description("Blockiert Entry, wenn bestätigte SUP/RES-Zonen zu nah beieinander liegen (Gap in Ticks). Gilt für ContinuationPullback.")]
        [DefaultValue(12)]
        public int ContinuationCompressionGapTicks_UI { get; set; } = 12;

        [Browsable(false)]
        public int? ContinuationPhaseLookbackBars
        {
            get => ContinuationPhaseLookbackBars_UI;
            set => ContinuationPhaseLookbackBars_UI = Math.Max(0, value ?? 6);
        }

        [Browsable(false)]
        public int? ContinuationCompressionGapTicks
        {
            get => ContinuationCompressionGapTicks_UI;
            set => ContinuationCompressionGapTicks_UI = Math.Max(0, value ?? 12);
        }


        [Category("Reversal")]
        [DisplayName("Reversal: Finished Auction Max Ask@Low")]
        [Description("Maximal erlaubtes aggressives Buy-Volumen (Ask) am Kerzen-Low, damit die Down-Auction als 'finished' gilt (0 = streng).")]
        public decimal FinishedAuctionMaxAskAtLow { get; set; } = 0m;

        [Category("Reversal")]
        [DisplayName("Reversal: Finished Auction Max Bid@High")]
        [Description("Maximal erlaubtes aggressives Sell-Volumen (Bid) am Kerzen-High, damit die Up-Auction als 'finished' gilt (0 = streng).")]
        public decimal FinishedAuctionMaxBidAtHigh { get; set; } = 0m;

        [Category("Reversal")]
        [DisplayName("Reversal: POC Shift Min (Ticks)")]
        [Description("Mindestverschiebung des Candle-POC in Ticks für die Confirmation-Kerze.")]
        [Range(0, 100)]
        [DefaultValue(1)]
        public int PocShiftMinTicks { get; set; } = 1;

        [Category("Reversal")]
        [DisplayName("Reversal: Delta Shift Min")]
        [Description("Mindestveränderung des NetDeltaTotal zwischen Reversal-Kerze und Confirmation-Kerze. 0 = nur Richtung prüfen.")]
        public decimal DeltaShiftMin { get; set; } = 20m;

        public decimal? TickSizeDecimal { get; set; } = 0.25m;
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

            };
        }

    }

}



