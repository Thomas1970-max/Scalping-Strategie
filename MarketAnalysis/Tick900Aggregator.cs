using System;
using ATAS.DataFeedsCore;
using ATAS.Indicators;

namespace MyNamespace.Strategies.MarketAnalysis
{
    public sealed class Tick900Aggregator
    {
        private readonly int _tradesPerBar;

        private int _count;
        private decimal _open;
        private decimal _high;
        private decimal _low;
        private decimal _close;
        private decimal _volume;
        private decimal _netDelta;
        private DateTime _time;
        private DateTime _lastTime;

        public int CurrentCount => _count;
        public DateTime CurrentTime => _time;
        public DateTime CurrentLastTime => _lastTime;
        public decimal CurrentOpen => _open;
        public decimal CurrentHigh => _high;
        public decimal CurrentLow => _low;
        public decimal CurrentClose => _close;
        public decimal CurrentVolume => _volume;
        public decimal CurrentNetDelta => _netDelta;

        public TickCandle? GetCurrentFormingCandle()
        {
            if (_count <= 0)
                return null;

            return new TickCandle
            {
                Time = _time,
                LastTime = _lastTime,
                Open = _open,
                High = _high,
                Low = _low,
                Close = _close,
                Volume = _volume,
                NetDelta = _netDelta,
                TradeCount = _count
            };
        }

        public Tick900Aggregator(int tradesPerBar = 900)
        {
            _tradesPerBar = tradesPerBar > 0 ? tradesPerBar : 900;
            Reset();
        }

        public void Reset()
        {
            _count = 0;
            _open = 0m;
            _high = 0m;
            _low = 0m;
            _close = 0m;
            _volume = 0m;
            _netDelta = 0m;
            _time = default;
            _lastTime = default;
        }

        public bool AddTrade(MarketDataArg trade, out TickCandle? closedCandle)
        {
            closedCandle = null;
            if (trade == null)
                return false;

            var price = trade.Price;
            var vol = trade.Volume;

            if (_count == 0)
            {
                _open = price;
                _high = price;
                _low = price;
                _close = price;
                _volume = 0m;
                _netDelta = 0m;
                _time = trade.Time;
                _lastTime = trade.Time;
            }
            else
            {
                if (price > _high) _high = price;
                if (price < _low) _low = price;
                _close = price;
                _lastTime = trade.Time;
            }

            _volume += vol;
            var dir = trade.Direction.ToString();
            if (dir == "Buy")
                _netDelta += vol;
            else if (dir == "Sell")
                _netDelta -= vol;

            _count++;

            if (_count < _tradesPerBar)
                return false;

            closedCandle = new TickCandle
            {
                Time = _time,
                LastTime = _lastTime,
                Open = _open,
                High = _high,
                Low = _low,
                Close = _close,
                Volume = _volume,
                NetDelta = _netDelta,
                TradeCount = _count
            };

            Reset();
            return true;
        }

        public bool AddTrade(CumulativeTrade trade, out TickCandle? closedCandle)
        {
            closedCandle = null;
            if (trade == null)
                return false;

            var price = trade.FirstPrice;
            var vol = trade.Volume;

            if (_count == 0)
            {
                _open = price;
                _high = price;
                _low = price;
                _close = price;
                _volume = 0m;
                _netDelta = 0m;
                _time = trade.Time;
                _lastTime = trade.Time;
            }
            else
            {
                if (price > _high) _high = price;
                if (price < _low) _low = price;
                _close = price;
                _lastTime = trade.Time;
            }

            _volume += vol;
            try
            {
                var askVol = trade.NewAsk.Volume;
                var bidVol = trade.NewBid.Volume;
                _netDelta += askVol - bidVol;
            }
            catch
            {
            }

            _count++;

            if (_count < _tradesPerBar)
                return false;

            closedCandle = new TickCandle
            {
                Time = _time,
                LastTime = _lastTime,
                Open = _open,
                High = _high,
                Low = _low,
                Close = _close,
                Volume = _volume,
                NetDelta = _netDelta,
                TradeCount = _count
            };

            Reset();
            return true;
        }

        public bool AddTrade(DateTime time, decimal price, decimal volume, decimal netDelta, out TickCandle? closedCandle)
        {
            closedCandle = null;

            if (_count == 0)
            {
                _open = price;
                _high = price;
                _low = price;
                _close = price;
                _volume = 0m;
                _netDelta = 0m;
                _time = time;
                _lastTime = time;
            }
            else
            {
                if (price > _high) _high = price;
                if (price < _low) _low = price;
                _close = price;
                _lastTime = time;
            }

            _volume += volume;
            _netDelta += netDelta;
            _count++;

            if (_count < _tradesPerBar)
                return false;

            closedCandle = new TickCandle
            {
                Time = _time,
                LastTime = _lastTime,
                Open = _open,
                High = _high,
                Low = _low,
                Close = _close,
                Volume = _volume,
                NetDelta = _netDelta,
                TradeCount = _count
            };

            Reset();
            return true;
        }
    }
}
