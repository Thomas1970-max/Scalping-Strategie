using System;

namespace MyNamespace.Strategies.Models
{
    public enum HtfZoneType
    {
        None,
        Resistance,
        Support
    }

    public enum MarketPhaseV2
    {
        Trend_Impulse,
        Healthy_Pullback,
        Momentum_Refuel,
        Range_Balanced,
        Exhaustion,
        Volatile_Breakout,
        Maturing_Trend
    }

    public enum MarketBiasV2
    {
        Neutral,
        Long,
        Short
    }

    public sealed class MarketStateV2
    {
        public MarketPhaseV2 Phase { get; set; } = MarketPhaseV2.Range_Balanced;
        public MarketBiasV2 Bias { get; set; } = MarketBiasV2.Neutral;
        public MarketRegime Dynamic { get; set; } = MarketRegime.None;
        public decimal VolatilityMultiplier { get; set; } = 1.0m;
        public bool IsTradeable { get; set; } = true;
        public bool IsOverextended { get; set; } = false;
        public decimal ZSlope { get; set; } = 0m;
        public int StaircaseIndex { get; set; } = 0;

        public HtfZoneType HtfZoneType { get; set; } = HtfZoneType.None;

        // Kontextfelder: sollen Evaluatoren eine bessere Entscheidungsgrundlage geben,
        // ohne Entry-Logik in die Engine zu verlagern.
        public bool AnchorTrendConfirmed { get; set; } = false;
        public decimal Sd1 { get; set; } = 0m;
        public decimal SigmaFromVwap { get; set; } = 0m; // (Close - VWAP) / SD1
        public bool InPullbackZone { get; set; } = false; // zwischen VWAP und 1σ in Trendrichtung

        public bool IsTrendContinuing { get; set; } = false;

        public MarketAnalysis.SwingStructureBias SwingBias { get; set; } = MarketAnalysis.SwingStructureBias.None;
        public decimal SwingConfidence { get; set; } = 0m;
        public int SwingBarsSinceBreak { get; set; } = int.MaxValue;
        public MarketAnalysis.SwingStructureBias SwingLastBreakDirection { get; set; } = MarketAnalysis.SwingStructureBias.None;
    }

    public readonly struct MarketStateInputV2
    {
        public MarketStateInputV2(
            int bar,
            decimal high,
            decimal low,
            decimal close,
            decimal vwap,
            decimal upperBand1,
            decimal lowerBand1,
            decimal upperBand2,
            decimal lowerBand2,
            decimal upperBand3,
            decimal lowerBand3,
            decimal currentVah,
            decimal currentVal,
            decimal candlePocPrice,
            MarketRegime regime)
            : this(
                bar: bar,
                high: high,
                low: low,
                close: close,
                vwap: vwap,
                upperBand1: upperBand1,
                lowerBand1: lowerBand1,
                upperBand2: upperBand2,
                lowerBand2: lowerBand2,
                upperBand3: upperBand3,
                lowerBand3: lowerBand3,
                currentVah: currentVah,
                currentVal: currentVal,
                candlePocPrice: candlePocPrice,
                regime: regime,
                htfSwingHigh: null,
                htfSwingLow: null,
                isInHtfZone: false,
                htfZoneType: HtfZoneType.None)
        {
        }

        public MarketStateInputV2(
            int bar,
            decimal high,
            decimal low,
            decimal close,
            decimal vwap,
            decimal upperBand1,
            decimal lowerBand1,
            decimal upperBand2,
            decimal lowerBand2,
            decimal upperBand3,
            decimal lowerBand3,
            decimal currentVah,
            decimal currentVal,
            decimal candlePocPrice,
            MarketRegime regime,
            decimal? htfSwingHigh,
            decimal? htfSwingLow,
            bool isInHtfZone,
            HtfZoneType htfZoneType)
        {
            Bar = bar;
            High = high;
            Low = low;
            Close = close;
            Vwap = vwap;
            UpperBand1 = upperBand1;
            LowerBand1 = lowerBand1;
            UpperBand2 = upperBand2;
            LowerBand2 = lowerBand2;
            UpperBand3 = upperBand3;
            LowerBand3 = lowerBand3;
            CurrentVAH = currentVah;
            CurrentVAL = currentVal;
            CandlePocPrice = candlePocPrice;
            Regime = regime;
            HtfSwingHigh = htfSwingHigh;
            HtfSwingLow = htfSwingLow;
            IsInHtfZone = isInHtfZone;
            HtfZoneType = htfZoneType;
        }

        public int Bar { get; }
        public decimal High { get; }
        public decimal Low { get; }
        public decimal Close { get; }
        public decimal Vwap { get; }
        public decimal UpperBand1 { get; }
        public decimal LowerBand1 { get; }
        public decimal UpperBand2 { get; }
        public decimal LowerBand2 { get; }
        public decimal UpperBand3 { get; }
        public decimal LowerBand3 { get; }
        public decimal CurrentVAH { get; }
        public decimal CurrentVAL { get; }
        public decimal CandlePocPrice { get; }
        public MarketRegime Regime { get; }

        public decimal? HtfSwingHigh { get; }
        public decimal? HtfSwingLow { get; }
        public bool IsInHtfZone { get; }
        public HtfZoneType HtfZoneType { get; }
    }
}
