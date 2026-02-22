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

        [Category("Breakout")]
        [DisplayName("Breakout: Min Signals Required")]
        [Description("Mindestanzahl erfüllter Signale/Kriterien für Breakout-Patterns.")]
        [DefaultValue(3)]
        public int BreakoutMinSignalsRequired_UI { get; set; } = 2;

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

        // CVD Impulse Long (visible as double)
        [Category("Reversal")]
        [DisplayName("Reversal: CVD Impulse Long")]
        [Description("Spezielle CVD-Impulse-Schwelle f�r Reversal-Long-Matching.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(59.0)]
        public double ReversalThCvdImpulseLong_UI { get; set; } = 60.0;

        // unsichtbarer Wrapper f�r Business-Logik (decimal?)
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
        [Description("Spezielle CVD-Impulse-Schwelle für Reversal-Short-Matching. überschreibt ggf. ThCvdImpulseShort für Reversal-Checks.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(double.NaN)]
        public double ReversalThCvdImpulseShort_UI { get; set; } = -100;

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


        [Category("Reversal")]
        [DisplayName("Range-Bar: Min Wick Abs Delta")]
        [Description("Minimale absolute Delta-Aktivität in der relevanten Wick-Zone, bevor die Dominanz des gegenüberliegenden Wicks einen Eintrag hart blockieren kann (0 = deaktiviert).")]
        public decimal RangeBarMinWickAbsDelta { get; set; } = 0m;

        [Category("Reversal")]
        [DisplayName("Range-Bar: Adaptive Wick Abs Delta")]
        [Description("Wenn aktiviert, wird RangeBarMinWickAbsDelta aus den letzten Momentaufnahmen berechnet (Median-Wick-Abs-Delta * Multiplikator).).")]
        public bool RangeBarUseAdaptiveWickAbsDelta { get; set; } = true;

        [Category("Reversal")]
        [DisplayName("Range-Bar: Wick Abs Delta Lookback")]
        [Description("Rückblickfenster für die Berechnung des adaptiven Wick-Abs-Delta-Schwellenwerts.")]
        public int RangeBarWickAbsDeltaLookback { get; set; } = 30;

        [Category("Reversal")]
        [DisplayName("Range-Bar: Wick Abs Delta Multiplier")]
        [Description("Multiplikator, der bei der Berechnung des adaptiven RangeBarMinWickAbsDelta auf den medianen Wick-Abs-Delta angewendet wird.Mehr blocken (strenger): Multiplier auf 1.2 bis 1.5).Weniger blocken (lockerer): Multiplier auf 0.8 bis 0.9")]
        public decimal RangeBarWickAbsDeltaMultiplier { get; set; } = 1.0m;

        [Category("Reversal")]
        [DisplayName("Reversal: Finished Auction Max Bid@Low")]
        [Description("Maximal erlaubtes aggressives Sell-Volumen (Bid) am Kerzen-Low, damit die Down-Auction als 'finished' gilt (0 = streng).")]
        public decimal FinishedAuctionMaxBidAtLow { get; set; } = 0m;

        [Category("Reversal")]
        [DisplayName("Reversal: Finished Auction Max Ask@High")]
        [Description("Maximal erlaubtes aggressives Buy-Volumen (Ask) am Kerzen-High, damit die Up-Auction als 'finished' gilt (0 = streng).")]
        public decimal FinishedAuctionMaxAskAtHigh { get; set; } = 0m;

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
        public decimal DeltaShiftMin { get; set; } = 0m;


        // =========================================================================
        // NEUES 2-STUFIGES SYSTEM: Anchored Imbalance Ratio (SOFT-Kriterium)
        // =========================================================================
        // Prozentwert: Wie viel % des Imbalance-Stacks muss an der High/Low verankert sein?
        // Beispiel: 50 = mindestens 50% des Stacks am Extrem (High für Long, Low für Short)
        // 
        // RELIEF-Rebounds haben typischerweise 0-20% (Stack in der Mitte der Bar)
        // ECHTE KRAFT hat 60-100% (Stack am Extrem)
        // =========================================================================

        /// <summary>
        /// Anchored Imbalance Ratio für LONG-Setups (Prozentwert)
        /// Schwellenwert: >= 50% des Buy-Imbalance-Stacks muss an der High verankert sein
        /// Wenn nicht erfüllt: Warnung ausgeben, aber nicht blocken (SOFT-Kriterium)
        /// </summary>
        public decimal? ReversalStackedImbAnchoredRatioMinLong { get; set; } = 50m;

        /// <summary>
        /// Anchored Imbalance Ratio für SHORT-Setups (Prozentwert)
        /// Schwellenwert: >= 50% des Sell-Imbalance-Stacks muss an der Low verankert sein
        /// Wenn nicht erfüllt: Warnung ausgeben, aber nicht blocken (SOFT-Kriterium)
        /// </summary>
        public decimal? ReversalStackedImbAnchoredRatioMinShort { get; set; } = 50m;

        // NEU: CVD-Shift Kriterien (HART - ersetzen absolute CVD-Impulse)
        /// <summary>
        /// CVD-Shift für LONG-Setups (Mindestwert)
        /// CVD_Shift = Delta_aktuell - Delta_vorherige (echte Momentum-Veränderung)
        /// Delta[n] = CVD[n] - CVD[n-1] (Delta pro Kerze)
        /// Shift[n] = Delta[n] - Delta[n-1] (Veränderung der Dynamik)
        /// 
        /// Wenn Shift < diesem Wert → Sofort blocken (keine Momentum-Umkehr)
        /// Beispiel: Delta[-300] → Delta[-100] → Shift = +200 ✓ (Verkaufsdruck nachlassend)
        /// </summary>
        public decimal? ReversalCvdShiftMinLong { get; set; } = 0m;  // Shift muss > 0 sein

        /// <summary>
        /// CVD-Shift für SHORT-Setups (Mindestwert - negativ!)
        /// CVD_Shift = Delta_aktuell - Delta_vorherige (echte Momentum-Veränderung)
        /// Delta[n] = CVD[n] - CVD[n-1] (Delta pro Kerze)
        /// Shift[n] = Delta[n] - Delta[n-1] (Veränderung der Dynamik)
        /// 
        /// Wenn Shift < diesem Wert → Sofort blocken (zu viel Verkaufsdruck)
        /// Beispiel: Delta[+100] → Delta[+300] → Shift = +200 > -500 ✓ OK
        /// Beispiel: Delta[+300] → Delta[+100] → Shift = -200 > -500 ✓ OK
        /// Beispiel: Delta[+100] → Delta[-100] → Shift = -200 > -500 ✓ OK
        /// Beispiel: Delta[+100] → Delta[-600] → Shift = -700 < -500 ✗ BLOCK
        /// </summary>
        public decimal? ReversalCvdShiftMinShort { get; set; } = -500m;  // Shift muss > -500 sein

        // =========================================
        // DYNAMIC IMBALANCE THRESHOLDS (Reversal)
        // =========================================
        
        /// <summary>
        /// Anchored-Ratio 100% - Dynamischer Threshold für ImbalanceScore
        /// Bessere Struktur = flexiblere Score-Anforderung
        /// </summary>
        [Category("Dynamic Imbalance Threshold (Reversal)")]
        [Description("Anchored-Ratio 100% - Dynamischer Threshold für ImbalanceScore")]
        [DefaultValue(0.15)]
        public double DynamicThresholdFullyAnchoredUI { get; set; } = 0.15;

        /// <summary>
        /// Anchored-Ratio 85% - Dynamischer Threshold für ImbalanceScore
        /// </summary>
        [Category("Dynamic Imbalance Threshold (Reversal)")]
        [Description("Anchored-Ratio 85% - Dynamischer Threshold für ImbalanceScore")]
        [DefaultValue(0.20)]
        public double DynamicThresholdHighlyAnchoredUI { get; set; } = 0.20;

        /// <summary>
        /// Anchored-Ratio 70% - Dynamischer Threshold für ImbalanceScore
        /// </summary>
        [Category("Dynamic Imbalance Threshold (Reversal)")]
        [Description("Anchored-Ratio 70% - Dynamischer Threshold für ImbalanceScore")]
        [DefaultValue(0.30)]
        public double DynamicThresholdWellAnchoredUI { get; set; } = 0.30;

        /// <summary>
        /// Anchored-Ratio 50% - Dynamischer Threshold für ImbalanceScore
        /// </summary>
        [Category("Dynamic Imbalance Threshold (Reversal)")]
        [Description("Anchored-Ratio 50% - Dynamischer Threshold für ImbalanceScore")]
        [DefaultValue(0.40)]
        public double DynamicThresholdMediumAnchoredUI { get; set; } = 0.40;

        /// <summary>
        /// Anchored-Ratio 30% - Dynamischer Threshold für ImbalanceScore
        /// </summary>
        [Category("Dynamic Imbalance Threshold (Reversal)")]
        [Description("Anchored-Ratio 30% - Dynamischer Threshold für ImbalanceScore")]
        [DefaultValue(0.48)]
        public double DynamicThresholdLowAnchoredUI { get; set; } = 0.48;

        /// <summary>
        /// Notfall-Schwelle für sehr schwache ImbalanceScores in Bar 1 nach Reversal
        /// </summary>
        [Category("Dynamic Imbalance Threshold (Reversal)")]
        [DisplayName("ImbalanceScore: Notfall-Schwelle für 1. Bar (sehr schwach)")]
        [Description("Definiert die absolute Mindestschwelle für den ImbalanceScore in der ersten Bar nach einem Reversal bei sehr schlechter Verankerung (<30%). Nur wenn der Score diese Schwelle erfüllt, wird ein Entry mit Warnung erlaubt. Empfohlener Wert: -0.50 für Short-Setups.")]
        [DefaultValue(-0.50)]
        public double DynamicThresholdReliefWarningUI { get; set; } = -0.50;

        /// <summary>
        /// Moderater Threshold für Bar 1 nach Reversal (zwischen Relief und Standard)
        /// </summary>
        [Category("Dynamic Imbalance Threshold (Reversal)")]
        [DisplayName("ImbalanceScore: Moderater Schwelle für 1. Bar nach Reversal")]
        [Description("Definiert die minimale Stärke des ImbalanceScore für die erste Bar nach einem Reversal. Dies ist eine mittlere Stufe zwischen der Standard-Schwelle und der Notfall-Schwelle. Empfohlener Wert: -0.30 für Short-Setups.")]
        [DefaultValue(-0.30)]
        public double DynamicThresholdBar1ModerateUI { get; set; } = -0.30;

        /// <summary>
        /// Erste Bar nach Reversal mit guter Verankerung (>=70%) - Dynamischer Threshold
        /// </summary>
        [Category("Dynamic Imbalance Threshold (Reversal)")]
        [Description("Erste Bar nach Reversal mit guter Verankerung (>=70%) - Dynamischer Threshold")]
        [DefaultValue(0.25)]
        public double DynamicThresholdFirstBarStrongUI { get; set; } = 0.25;

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

                // Reversal-sichtbare Werte (kopiere Wrapper-Werte)
                ReversalThCvdImpulseLong = this.ReversalThCvdImpulseLong,
                ReversalThCvdImpulseShort = this.ReversalThCvdImpulseShort,
            };
        }

    }

}



