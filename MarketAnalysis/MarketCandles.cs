using System;
using ATAS.Indicators;

namespace MyNamespace.Strategies.MarketAnalysis
{
    public interface IMarketCandle
    {
        DateTime Time { get; }
        DateTime LastTime { get; }
        decimal Open { get; }
        decimal High { get; }
        decimal Low { get; }
        decimal Close { get; }
        decimal Volume { get; }
        decimal NetDelta { get; }
    }

    public sealed class IndicatorCandleAdapter : IMarketCandle
    {
        private readonly IndicatorCandle _c;
        private readonly decimal _netDelta;

        public IndicatorCandleAdapter(IndicatorCandle candle, decimal netDelta = 0m)
        {
            _c = candle ?? throw new ArgumentNullException(nameof(candle));
            _netDelta = netDelta;
        }

        public DateTime Time => _c.Time;
        public DateTime LastTime => _c.LastTime;
        public decimal Open => _c.Open;
        public decimal High => _c.High;
        public decimal Low => _c.Low;
        public decimal Close => _c.Close;
        public decimal Volume => _c.Volume;
        public decimal NetDelta => _netDelta;
    }

    public sealed class TickCandle : IMarketCandle
    {
        public DateTime Time { get; init; }
        public DateTime LastTime { get; init; }
        public decimal Open { get; init; }
        public decimal High { get; init; }
        public decimal Low { get; init; }
        public decimal Close { get; init; }
        public decimal Volume { get; init; }
        public decimal NetDelta { get; init; }
        public int TradeCount { get; init; }
    }
}
