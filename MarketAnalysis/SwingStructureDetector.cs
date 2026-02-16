using System;
using System.Collections.Generic;
using System.Linq;

namespace MyNamespace.Strategies.Orderflow
{
    public enum SwingStructureBias
    {
        None = 0,
        Bullish = 1,
        Bearish = -1
    }

    public sealed class SwingStructureSignal
    {
        public SwingStructureBias Bias { get; set; } = SwingStructureBias.None;
        public decimal Confidence { get; set; } = 0m;
        public decimal? LastSwingHigh { get; set; }
        public decimal? LastSwingLow { get; set; }
        public decimal? BufferedHigh { get; set; }
        public decimal? BufferedLow { get; set; }
        public decimal? InvalidationLevel { get; set; }
        public int BarsBuffered { get; set; } = 0;
    }

    public sealed class SwingStructureDetector
    {
        private readonly int _left;
        private readonly int _right;
        private readonly int _maxBars;
        private readonly List<Bar> _bars;

        private decimal? _prevSwingHigh;
        private decimal? _lastSwingHigh;
        private decimal? _prevSwingLow;
        private decimal? _lastSwingLow;

        private struct Bar
        {
            public decimal High;
            public decimal Low;
            public decimal Close;
        }

        public SwingStructureDetector(int leftBars, int rightBars, int maxBars = 200)
        {
            _left = Math.Max(1, leftBars);
            _right = Math.Max(1, rightBars);
            _maxBars = Math.Max(_left + _right + 5, maxBars);
            _bars = new List<Bar>(_maxBars);
        }

        public void Clear()
        {
            _bars.Clear();
            _prevSwingHigh = null;
            _lastSwingHigh = null;
            _prevSwingLow = null;
            _lastSwingLow = null;
        }

        public void AddBar(decimal high, decimal low, decimal close)
        {
            _bars.Add(new Bar { High = high, Low = low, Close = close });
            if (_bars.Count > _maxBars)
                _bars.RemoveAt(0);

            TryConfirmSwingAt(_bars.Count - 1 - _right);
        }

        public SwingStructureSignal GetSignal()
        {
            var signal = new SwingStructureSignal
            {
                BarsBuffered = _bars.Count,
                LastSwingHigh = _lastSwingHigh,
                LastSwingLow = _lastSwingLow
            };

            if (_bars.Count == 0)
                return signal;

            decimal bufHigh = _bars[0].High;
            decimal bufLow = _bars[0].Low;
            for (int i = 1; i < _bars.Count; i++)
            {
                if (_bars[i].High > bufHigh) bufHigh = _bars[i].High;
                if (_bars[i].Low < bufLow) bufLow = _bars[i].Low;
            }
            signal.BufferedHigh = bufHigh;
            signal.BufferedLow = bufLow;

            var lastClose = _bars[_bars.Count - 1].Close;

            if (_prevSwingLow.HasValue && _lastSwingLow.HasValue && _lastSwingHigh.HasValue)
            {
                var l1 = _prevSwingLow.Value;
                var l2 = _lastSwingLow.Value;
                var h1 = _lastSwingHigh.Value;

                if (l2 > l1 && lastClose > h1)
                {
                    signal.Bias = SwingStructureBias.Bullish;
                    signal.InvalidationLevel = l2;
                    signal.Confidence = EstimateConfidence(
                        direction: SwingStructureBias.Bullish,
                        lastClose: lastClose,
                        breakLevel: h1,
                        invalidation: l2);

                    return signal;
                }
            }

            if (_prevSwingHigh.HasValue && _lastSwingHigh.HasValue && _lastSwingLow.HasValue)
            {
                var h1 = _prevSwingHigh.Value;
                var h2 = _lastSwingHigh.Value;
                var l1 = _lastSwingLow.Value;

                if (h2 < h1 && lastClose < l1)
                {
                    signal.Bias = SwingStructureBias.Bearish;
                    signal.InvalidationLevel = h2;
                    signal.Confidence = EstimateConfidence(
                        direction: SwingStructureBias.Bearish,
                        lastClose: lastClose,
                        breakLevel: l1,
                        invalidation: h2);

                    return signal;
                }
            }

            return signal;
        }

        private decimal EstimateConfidence(SwingStructureBias direction, decimal lastClose, decimal breakLevel, decimal invalidation)
        {
            decimal distFromBreak = Math.Abs(lastClose - breakLevel);
            decimal risk = Math.Abs(breakLevel - invalidation);
            if (risk <= 0m) return 0m;

            decimal rr = distFromBreak / risk;
            decimal c = rr >= 0.5m ? 1m : rr / 0.5m;
            if (direction == SwingStructureBias.Bullish && invalidation >= breakLevel)
                c = 0m;
            if (direction == SwingStructureBias.Bearish && invalidation <= breakLevel)
                c = 0m;

            return Clamp01(c);
        }

        private void TryConfirmSwingAt(int centerIndex)
        {
            if (centerIndex < _left) return;
            if (centerIndex + _right >= _bars.Count) return;

            var center = _bars[centerIndex];

            bool isSwingHigh = true;
            for (int i = centerIndex - _left; i <= centerIndex + _right; i++)
            {
                if (i == centerIndex) continue;
                if (_bars[i].High >= center.High)
                {
                    isSwingHigh = false;
                    break;
                }
            }

            bool isSwingLow = true;
            for (int i = centerIndex - _left; i <= centerIndex + _right; i++)
            {
                if (i == centerIndex) continue;
                if (_bars[i].Low <= center.Low)
                {
                    isSwingLow = false;
                    break;
                }
            }

            if (isSwingHigh)
                OnSwingHigh(center.High);
            if (isSwingLow)
                OnSwingLow(center.Low);
        }

        private void OnSwingHigh(decimal high)
        {
            _prevSwingHigh = _lastSwingHigh;
            _lastSwingHigh = high;
        }

        private void OnSwingLow(decimal low)
        {
            _prevSwingLow = _lastSwingLow;
            _lastSwingLow = low;
        }

        private static decimal Clamp01(decimal v)
        {
            if (v < 0m) return 0m;
            if (v > 1m) return 1m;
            return v;
        }
    }
}
