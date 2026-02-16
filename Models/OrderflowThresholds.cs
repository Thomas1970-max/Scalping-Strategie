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

        [Category("Reversal")]
        [DisplayName("Reversal: Min Signals Required")]
        [Description("Mindestanzahl erfüllter Signale/Kriterien für Reversal-Patterns (z.B. ReversalBounce).")]
        [DefaultValue(2)]
        public int ReversalMinSignalsRequired_UI { get; set; } = 2;

        [Category("Breakout")]
        [DisplayName("Breakout: Min Signals Required")]
        [Description("Mindestanzahl erfüllter Signale/Kriterien für Breakout-Patterns.")]
        [DefaultValue(3)]
        public int BreakoutMinSignalsRequired_UI { get; set; } = 2;

        [Category("Continuation")]
        [DisplayName("Continuation: Min Signals Required")]
        [Description("Mindestanzahl erfüllter Signale/Kriterien für Continuation-Patterns.")]
        [DefaultValue(3)]
        public int ContinuationMinSignalsRequired_UI { get; set; } = 2;

        [Category("Continuation")]
        [DisplayName("Continuation: CVD Impulse Long")]
        [Description("Spezielle CVD-Impulse-Schwelle für Continuation-Long-Matching.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(20.0)]
        public double ContinuationThCvdImpulseLong_UI { get; set; } = 20.0;

        [Browsable(false)]
        public decimal? ContinuationThCvdImpulseLong
        {
            get => (decimal?)ContinuationThCvdImpulseLong_UI;
            set
            {
                if (value.HasValue)
                    ContinuationThCvdImpulseLong_UI = (double)value.Value;
                else
                    ContinuationThCvdImpulseLong_UI = 20.0;
            }
        }

        [Category("Continuation")]
        [DisplayName("Continuation: CVD Impulse Short")]
        [Description("Spezielle CVD-Impulse-Schwelle für Continuation-Short-Matching.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(-20.0)]
        public double ContinuationThCvdImpulseShort_UI { get; set; } = -20.0;

        [Browsable(false)]
        public decimal? ContinuationThCvdImpulseShort
        {
            get => (decimal?)ContinuationThCvdImpulseShort_UI;
            set
            {
                if (value.HasValue)
                    ContinuationThCvdImpulseShort_UI = (double)value.Value;
                else
                    ContinuationThCvdImpulseShort_UI = -20.0;
            }
        }

        [Category("Continuation")]
        [DisplayName("Continuation: AggPressure (Bull)")]
        [Description("Aggression-Pressure Schwelle (Bull) für Continuation-Checks.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(0.10)]
        public double ContinuationThAggPressureBull_UI { get; set; } = 0.10;

        [Browsable(false)]
        public decimal? ContinuationThAggPressureBull
        {
            get => (decimal?)ContinuationThAggPressureBull_UI;
            set
            {
                if (value.HasValue)
                    ContinuationThAggPressureBull_UI = (double)value.Value;
                else
                    ContinuationThAggPressureBull_UI = 0.10;
            }
        }

        [Category("Continuation")]
        [DisplayName("Continuation: AggPressure (Bear)")]
        [Description("Aggression-Pressure Schwelle (Bear) für Continuation-Checks.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(-0.10)]
        public double ContinuationThAggPressureBear_UI { get; set; } = -0.10;

        [Browsable(false)]
        public decimal? ContinuationThAggPressureBear
        {
            get => (decimal?)ContinuationThAggPressureBear_UI;
            set
            {
                if (value.HasValue)
                    ContinuationThAggPressureBear_UI = (double)value.Value;
                else
                    ContinuationThAggPressureBear_UI = -0.10;
            }
        }

        [Category("Continuation")]
        [DisplayName("Continuation: TradeRateZ")]
        [Description("TradeRateZ Schwelle für Continuation-Checks (wird als |TradeRateZ| verglichen).")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(1.50)]
        public double ContinuationThTradeRateZ_UI { get; set; } = 1.50;

        [Browsable(false)]
        public decimal? ContinuationThTradeRateZ
        {
            get => (decimal?)ContinuationThTradeRateZ_UI;
            set
            {
                if (value.HasValue)
                    ContinuationThTradeRateZ_UI = (double)value.Value;
                else
                    ContinuationThTradeRateZ_UI = 1.50;
            }
        }

        [Category("Continuation")]
        [DisplayName("Continuation: InterTradeTimeZ (Bull)")]
        [Description("InterTradeTimeZ Schwelle (Bull) für Continuation-Checks.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(0.30)]
        public double ContinuationThInterTradeTimeZBull_UI { get; set; } = 0.30;

        [Browsable(false)]
        public decimal? ContinuationThInterTradeTimeZBull
        {
            get => (decimal?)ContinuationThInterTradeTimeZBull_UI;
            set
            {
                if (value.HasValue)
                    ContinuationThInterTradeTimeZBull_UI = (double)value.Value;
                else
                    ContinuationThInterTradeTimeZBull_UI = 0.30;
            }
        }

        [Category("Continuation")]
        [DisplayName("Continuation: InterTradeTimeZ (Bear)")]
        [Description("InterTradeTimeZ Schwelle (Bear) für Continuation-Checks.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(-0.30)]
        public double ContinuationThInterTradeTimeZBear_UI { get; set; } = -0.30;

        [Browsable(false)]
        public decimal? ContinuationThInterTradeTimeZBear
        {
            get => (decimal?)ContinuationThInterTradeTimeZBear_UI;
            set
            {
                if (value.HasValue)
                    ContinuationThInterTradeTimeZBear_UI = (double)value.Value;
                else
                    ContinuationThInterTradeTimeZBear_UI = -0.30;
            }
        }

        [Category("Continuation")]
        [DisplayName("Continuation: Efficiency")]
        [Description("Effizienz-Schwelle, nur für Continuation-Matching.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(0.01)]
        public double ContinuationThEfficiency_UI { get; set; } = 0.01;

        [Browsable(false)]
        public decimal? ContinuationThEfficiency
        {
            get => (decimal?)ContinuationThEfficiency_UI;
            set
            {
                if (value.HasValue)
                    ContinuationThEfficiency_UI = (double)value.Value;
                else
                    ContinuationThEfficiency_UI = 0.01;
            }
        }

        [Category("Continuation")]
        [DisplayName("Continuation: Max Counter Share (Bull)")]
        [Description("Maximaler Counter-Share (Bull) für Continuation-Checks.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(0.40)]
        public double ContinuationMaxCounterShareBull_UI { get; set; } = 0.40;

        [Browsable(false)]
        public decimal? ContinuationMaxCounterShareBull
        {
            get => (decimal?)ContinuationMaxCounterShareBull_UI;
            set
            {
                if (value.HasValue)
                    ContinuationMaxCounterShareBull_UI = (double)value.Value;
                else
                    ContinuationMaxCounterShareBull_UI = 0.40;
            }
        }

        [Category("Continuation")]
        [DisplayName("Continuation: Max Counter Share (Bear)")]
        [Description("Maximaler Counter-Share (Bear) für Continuation-Checks.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(0.40)]
        public double ContinuationMaxCounterShareBear_UI { get; set; } = 0.40;

        [Browsable(false)]
        public decimal? ContinuationMaxCounterShareBear
        {
            get => (decimal?)ContinuationMaxCounterShareBear_UI;
            set
            {
                if (value.HasValue)
                    ContinuationMaxCounterShareBear_UI = (double)value.Value;
                else
                    ContinuationMaxCounterShareBear_UI = 0.40;
            }
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

        // CVD Impulse Min For Long Reversal (blocker)
        [Category("Reversal")]
        [DisplayName("Reversal: CVD Impulse Min For Long Reversal")]
        [Description("Harter Min-Blocker für Long-Reversal (falls gesetzt). Wenn gesetzt, blockiert Long-Reversals unter diesem Wert.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(double.NaN)]
        public double ReversalThCvdImpulseMinForLongReversal_UI { get; set; } = double.NaN;

        [Browsable(false)]
        public decimal? ReversalThCvdImpulseMinForLongReversal
        {
            get => double.IsNaN(ReversalThCvdImpulseMinForLongReversal_UI) ? (decimal?)null : (decimal)ReversalThCvdImpulseMinForLongReversal_UI;
            set
            {
                if (value.HasValue)
                    ReversalThCvdImpulseMinForLongReversal_UI = (double)value.Value;
                else
                    ReversalThCvdImpulseMinForLongReversal_UI = double.NaN;
            }
        }

        // CVD Impulse Max For Short Reversal (blocker)
        [Category("Reversal")]
        [DisplayName("Reversal: CVD Impulse Max For Short Reversal")]
        [Description("Harter Max-Blocker f�r Short-Reversal (falls gesetzt). Wenn gesetzt, blockiert Short-Reversals oberhalb dieses Wertes.")]
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

        // Imbalance Score Min (Long)
        [Category("Reversal")]
        [DisplayName("Reversal: Imbalance Score Min (Long)")]
        [Description("Mindestwert für Imbalance-Score in Long-Entry-Fenster. Leerlassen zum Deaktivieren.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(double.NaN)]
        public double ReversalThImbalanceScoreMinLong_UI { get; set; } = 0.55;

        [Browsable(false)]
        public decimal? ReversalThImbalanceScoreMinLong
        {
            get => double.IsNaN(ReversalThImbalanceScoreMinLong_UI) ? (decimal?)null : (decimal)ReversalThImbalanceScoreMinLong_UI;
            set
            {
                if (value.HasValue)
                    ReversalThImbalanceScoreMinLong_UI = (double)value.Value;
                else
                    ReversalThImbalanceScoreMinLong_UI = 0.55;
            }
        }

        // Imbalance Score Max (Short)
        [Category("Reversal")]
        [DisplayName("Reversal: Imbalance Score Max (Short)")]
        [Description("Maximalwert für Imbalance-Score in Short-Entry-Fenster. Leerlassen zum Deaktivieren.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(double.NaN)]
        public double ReversalThImbalanceScoreMaxShort_UI { get; set; } = -0.55;

        [Browsable(false)]
        public decimal? ReversalThImbalanceScoreMaxShort
        {
            get => double.IsNaN(ReversalThImbalanceScoreMaxShort_UI) ? (decimal?)null : (decimal)ReversalThImbalanceScoreMaxShort_UI;
            set
            {
                if (value.HasValue)
                    ReversalThImbalanceScoreMaxShort_UI = (double)value.Value;
                else
                    ReversalThImbalanceScoreMaxShort_UI = -0.55;
            }
        }

        // AggPressure Breakout (Bull)
        [Category("Reversal")]
        [DisplayName("Reversal: AggPressure Breakout (Bull)")]
        [Description("Aggression-Pressure Schwelle (Bull) für Reversal-Checks.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(double.NaN)]
        public double ReversalThAggPressureBreakoutBull_UI { get; set; } = -0.1;

        [Browsable(false)]
        public decimal? ReversalThAggPressureBreakoutBull
        {
            get => double.IsNaN(ReversalThAggPressureBreakoutBull_UI) ? (decimal?)null : (decimal)ReversalThAggPressureBreakoutBull_UI;
            set
            {
                if (value.HasValue)
                    ReversalThAggPressureBreakoutBull_UI = (double)value.Value;
                else
                    ReversalThAggPressureBreakoutBull_UI = -0.1;
            }
        }

        // AggPressure Breakout (Bear)
        [Category("Reversal")]
        [DisplayName("Reversal: AggPressure Breakout (Bear)")]
        [Description("Aggression-Pressure Schwelle (Bear) für Reversal-Checks.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(double.NaN)]
        public double ReversalThAggPressureBreakoutBear_UI { get; set; } = 0.6;

        [Browsable(false)]
        public decimal? ReversalThAggPressureBreakoutBear
        {
            get => double.IsNaN(ReversalThAggPressureBreakoutBear_UI) ? (decimal?)null : (decimal)ReversalThAggPressureBreakoutBear_UI;
            set
            {
                if (value.HasValue)
                    ReversalThAggPressureBreakoutBear_UI = (double)value.Value;
                else
                    ReversalThAggPressureBreakoutBear_UI = 0.6;
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

        // Max CounterDelta Share (Bull)
        [Category("Reversal")]
        [DisplayName("Reversal: Max CounterDelta Share (Bull)")]
        [Description("Maximaler Counter-Delta-Anteil f�r Reversal-Checks (Bull).")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(double.NaN)]
        public double ReversalMaxCounterDeltaShareBull_UI { get; set; } = 0.4;

        [Browsable(false)]
        public decimal? ReversalMaxCounterDeltaShareBull
        {
            get => double.IsNaN(ReversalMaxCounterDeltaShareBull_UI) ? (decimal?)null : (decimal)ReversalMaxCounterDeltaShareBull_UI;
            set
            {
                if (value.HasValue)
                    ReversalMaxCounterDeltaShareBull_UI = (double)value.Value;
                else
                    ReversalMaxCounterDeltaShareBull_UI = 0.4;
            }
        }

        // Max CounterDelta Share (Bear)
        [Category("Reversal")]
        [DisplayName("Reversal: Max CounterDelta Share (Bear)")]
        [Description("Maximaler Counter-Delta-Anteil f�r Reversal-Checks (Bear).")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(double.NaN)]
        public double ReversalMaxCounterDeltaShareBear_UI { get; set; } = 0.4;

        [Browsable(false)]
        public decimal? ReversalMaxCounterDeltaShareBear
        {
            get => double.IsNaN(ReversalMaxCounterDeltaShareBear_UI) ? (decimal?)null : (decimal)ReversalMaxCounterDeltaShareBear_UI;
            set
            {
                if (value.HasValue)
                    ReversalMaxCounterDeltaShareBear_UI = (double)value.Value;
                else
                    ReversalMaxCounterDeltaShareBear_UI = 0.4;
            }
        }

        // Vol Burst Z
        [Category("Reversal")]
        [DisplayName("Reversal: Vol Burst Z")]
        [Description("VolBurst Z Schwelle, nur f�r Reversal-Matching.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(double.NaN)]
        public double ReversalThVolBurstZ_UI { get; set; } = 0.4;

        [Browsable(false)]
        public decimal? ReversalThVolBurstZ
        {
            get => double.IsNaN(ReversalThVolBurstZ_UI) ? (decimal?)null : (decimal)ReversalThVolBurstZ_UI;
            set
            {
                if (value.HasValue)
                    ReversalThVolBurstZ_UI = (double)value.Value;
                else
                    ReversalThVolBurstZ_UI = 0.4;
            }
        }

        // Efficiency
        [Category("Reversal")]
        [DisplayName("Reversal: Efficiency")]
        [Description("Effizienz-Schwelle, nur f�r Reversal-Matching.")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(double.NaN)]
        public double ReversalThEfficiency_UI { get; set; } = 0.01;

        [Browsable(false)]
        public decimal? ReversalThEfficiency
        {
            get => double.IsNaN(ReversalThEfficiency_UI) ? (decimal?)null : (decimal)ReversalThEfficiency_UI;
            set
            {
                if (value.HasValue)
                    ReversalThEfficiency_UI = (double)value.Value;
                else
                    ReversalThEfficiency_UI = 0.01;
            }
        }

        // StackedImb Anchored Range Min Directional (use double UI, integer wrapper)
        [Category("Reversal")]
        [DisplayName("Reversal: StackedImb Anchored Range Min Directional")]
        [Description("Reversal-spezifischer StackedImbalance Anchored Range Minimum (directional).")]
        [TypeConverter(typeof(DoubleConverter))]
        [DefaultValue(double.NaN)]
        public double ReversalThStackedImbAnchoredRangeMinDirectional_UI { get; set; } = double.NaN;

        [Browsable(false)]
        public int? ReversalThStackedImbAnchoredRangeMinDirectional
        {
            get => double.IsNaN(ReversalThStackedImbAnchoredRangeMinDirectional_UI) ? (int?)null : Convert.ToInt32(ReversalThStackedImbAnchoredRangeMinDirectional_UI);
            set
            {
                if (value.HasValue)
                    ReversalThStackedImbAnchoredRangeMinDirectional_UI = value.Value;
                else
                    ReversalThStackedImbAnchoredRangeMinDirectional_UI = double.NaN;
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

        // OPTIONAL: Neue Hard-Kriterien für CVD (Stufe 1 des 2-stufigen Systems) - DEPRECATED
        /// <summary>
        /// Mindest-CVD-Impuls für Long-Reversal Bounce - DEPRECATED, nutze CVD-Shift
        /// </summary>
        [Obsolete("Use ReversalCvdShiftMinLong instead")]
        public decimal? ReversalCvdImpulseMinForLongReversal { get; set; } = -1.0m;

        /// <summary>
        /// Mindest-CVD-Impuls für Short-Reversal Bounce (Max-Wert, da negativ) - DEPRECATED, nutze CVD-Shift
        /// </summary>
        [Obsolete("Use ReversalCvdShiftMaxShort instead")]
        public decimal? ReversalCvdImpulseMinForShortReversal { get; set; } = 1.0m;

        public decimal? TickSizeDecimal { get; set; } = 0.25m;

        // -----------------------------------------
        // R�ckw�rtskompatible "Effective" Properties:
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

                ContinuationThCvdImpulseLong = this.ContinuationThCvdImpulseLong,
                ContinuationThCvdImpulseShort = this.ContinuationThCvdImpulseShort,
                ContinuationThAggPressureBull = this.ContinuationThAggPressureBull,
                ContinuationThAggPressureBear = this.ContinuationThAggPressureBear,
                ContinuationThTradeRateZ = this.ContinuationThTradeRateZ,
                ContinuationThInterTradeTimeZBull = this.ContinuationThInterTradeTimeZBull,
                ContinuationThInterTradeTimeZBear = this.ContinuationThInterTradeTimeZBear,
                ContinuationThEfficiency = this.ContinuationThEfficiency,
                ContinuationMaxCounterShareBull = this.ContinuationMaxCounterShareBull,
                ContinuationMaxCounterShareBear = this.ContinuationMaxCounterShareBear,

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



