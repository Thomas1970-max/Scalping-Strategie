using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using ATAS.Indicators;
using ATAS.Indicators.Technical;
using System.Drawing;
using Utils.Common.Logging;



namespace MyNamespace.Strategies
{
    //Basis-Datentyp
    public struct Candle
    {
        public decimal Open;
        public decimal High;
        public decimal Low;
        public decimal Close;
        public Candle(decimal o, decimal h, decimal l, decimal c)
        {
            Open = o; High = h; Low = l; Close = c;
        }
    }
    //Utilities
    public static class IndicatorUtils
    {
        public static decimal Sma(IReadOnlyList<decimal> values, int endIndex, int period)
        {
            if (period <= 0) throw new ArgumentException(nameof(period));
            if (endIndex < 0) return 0m;
            int available = Math.Min(period, endIndex + 1);
            int start = endIndex - available + 1;
            decimal sum = 0m;
            for (int i = start; i <= endIndex; i++)
                sum += values[i];
            return sum / available;
        }

        public static decimal StdDev(IReadOnlyList<decimal> values, int endIndex, int period)
        {
            if (period <= 0) throw new ArgumentException(nameof(period));
            if (endIndex < 0) return 0m;
            int available = Math.Min(period, endIndex + 1);
            if (available <= 1) return 0m;
            decimal mean = Sma(values, endIndex, available);
            decimal sumSq = 0m;
            int start = endIndex - available + 1;
            for (int i = start; i <= endIndex; i++)
            {
                var d = values[i] - mean;
                sumSq += d * d;
            }
            return (decimal)Math.Sqrt((double)(sumSq / available));
        }

        public static decimal Highest(IReadOnlyList<decimal> values, int endIndex, int period)
        {
            if (endIndex < 0) return 0m;
            int start = Math.Max(0, endIndex - period + 1);
            decimal max = values[start];
            for (int i = start + 1; i <= endIndex; i++) if (values[i] > max) max = values[i];
            return max;
        }

        public static decimal Lowest(IReadOnlyList<decimal> values, int endIndex, int period)
        {
            if (endIndex < 0) return 0m;
            int start = Math.Max(0, endIndex - period + 1);
            decimal min = values[start];
            for (int i = start + 1; i <= endIndex; i++) if (values[i] < min) min = values[i];
            return min;
        }

        // einfache Linear Regression: berechne den Wert am letzten Punkt (x = n-1)
        public static decimal LinearReg(IReadOnlyList<decimal> values, int endIndex, int period)
        {
            if (period <= 0) throw new ArgumentException(nameof(period));
            if (endIndex < 0) return 0m;
            int available = Math.Min(period, endIndex + 1);
            if (available <= 0) return 0m;
            int start = endIndex - available + 1;
            int n = available;

            decimal sumX = 0m, sumY = 0m, sumXY = 0m, sumX2 = 0m;
            for (int i = 0; i < n; i++)
            {
                decimal x = i;
                decimal y = values[start + i];
                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumX2 += x * x;
            }

            decimal denom = n * sumX2 - sumX * sumX;
            if (denom == 0m) return 0m;

            decimal a = (n * sumXY - sumX * sumY) / denom;
            decimal b = (sumY - a * sumX) / n;

            return a * (n - 1) + b;
        }
    }
    //Squeeze types
    public enum SqueezeStateEnum { Unknown, On, Off, NoSqueeze }

    public class SqueezeResult
    {
        public decimal MomentumVal { get; init; }
        public decimal MomentumSlope { get; init; }
        public SqueezeStateEnum State { get; init; }
        public int StateDurationBars { get; init; }
    }
    public class SqueezeMomentumCalculator
    {
        public int BBPeriod { get; set; } = 20;
        public decimal BBMultFactor { get; set; } = 2.0m;
        public int KCPeriod { get; set; } = 20;
        public decimal KCMultFactor { get; set; } = 1.5m;

        private readonly List<decimal> _closes = new();
        private readonly List<decimal> _highs = new();
        private readonly List<decimal> _lows = new();
        private readonly List<decimal> _ranges = new();
        private readonly List<decimal> _linRegInput = new();

        private decimal _lastMomentumVal = 0m;
        private SqueezeStateEnum _lastState = SqueezeStateEnum.Unknown;
        private int _stateDuration = 0;
        // public accessor for warmup/count check
        public int Count => _closes.Count;

        // optional: Reset-Funktion
        public void Reset()
        {
            _closes.Clear();
            _highs.Clear();
            _lows.Clear();
            _ranges.Clear();
            _linRegInput.Clear();
            _lastMomentumVal = 0m;
            _lastState = SqueezeStateEnum.Unknown;
            _stateDuration = 0;
        }
        public SqueezeResult Update(Candle c)
        {
            int bar = _closes.Count;
            //this.LogInfo($"[SQUEEZE-INDIKATOR] Update start: bar={bar}, _closes.Count={_closes.Count}, _linRegInput.Count={_linRegInput.Count}");

            _closes.Add(c.Close);
            _highs.Add(c.High);
            _lows.Add(c.Low);

            decimal range = c.High - c.Low; // Range-Bars: TrueRange nicht nötig
            _ranges.Add(range);
            //this.LogInfo($"[SQUEEZE-INDIKATOR] candle: Close={c.Close}, High={c.High}, Low={c.Low}, range={range}");

            try
            {
                // Bollinger
                var basis = IndicatorUtils.Sma(_closes, bar, BBPeriod);
                var dev = BBMultFactor * IndicatorUtils.StdDev(_closes, bar, BBPeriod);
                var upperBB = basis + dev;
                var lowerBB = basis - dev;

                // Keltner
                var ma = IndicatorUtils.Sma(_closes, bar, KCPeriod);
                var rangeSma = IndicatorUtils.Sma(_ranges, bar, KCPeriod);
                var upperKC = ma + rangeSma * KCMultFactor;
                var lowerKC = ma - rangeSma * KCMultFactor;

                //this.LogInfo($"[SQUEEZE-INDIKATOR] BB: basis={basis:F2}, dev={dev}, upperBB={upperBB:F2}, lowerBB={lowerBB:F2}; KC: ma={ma:F2}, rangeSma={rangeSma:F2}, upperKC={upperKC:F2}, lowerKC={lowerKC:F2}");

                // Squeeze Flags
                bool sqzOn = lowerBB > lowerKC && upperBB < upperKC;
                bool sqzOff = lowerBB < lowerKC && upperBB > upperKC;
                SqueezeStateEnum state = sqzOn ? SqueezeStateEnum.On : sqzOff ? SqueezeStateEnum.Off : SqueezeStateEnum.NoSqueeze;

                // Highest / Lowest for linreg input
                var high = IndicatorUtils.Highest(_highs, bar, KCPeriod);
                var low = IndicatorUtils.Lowest(_lows, bar, KCPeriod);

                decimal center = ((high + low) / 2.0m + ma) / 2.0m;
                decimal input = c.Close - center;
                _linRegInput.Add(input);

                //this.LogInfo($"[SQUEEZE-INDIKATOR] high(last{KCPeriod})={high:F2}, low(last{KCPeriod})={low:F2}, center={center:F2}, input={input:F2}");

                // Linear regression over last KCPeriod inputs
                var lastInputs = _linRegInput.Count <= 10
                    ? string.Join(",", _linRegInput)
                    : string.Join(",", _linRegInput.Skip(Math.Max(0, _linRegInput.Count - 10)));
                //this.LogInfo($"[SQUEEZE-INDIKATOR] linReg input last10=[{lastInputs}]");

                var val = IndicatorUtils.LinearReg(_linRegInput, _linRegInput.Count - 1, KCPeriod);

                decimal slope = val - _lastMomentumVal;
                //this.LogInfo($"[SQUEEZE-INDIKATOR] linReg val={val:F2}, slope={slope:F2}, prevMomentum={_lastMomentumVal:F2}");
                _lastMomentumVal = val;

                if (state == _lastState)
                    _stateDuration++;
                else
                {
                    _lastState = state;
                    _stateDuration = 1;
                }

                return new SqueezeResult
                {
                    MomentumVal = val,
                    MomentumSlope = slope,
                    State = state,
                    StateDurationBars = _stateDuration
                };
            }
            catch (Exception ex)
            {
                // Exception-Logging im gewünschten Stil
                this.LogInfo($"[SQUEEZE-INDIKATOR] Update exception (bar {bar}): {ex.GetType().Name}: {ex.Message}");
                this.LogInfo($"[SQUEEZE-INDIKATOR] Update exception (bar {bar}) stack: {ex.StackTrace}");

                return new SqueezeResult
                {
                    MomentumVal = 0m,
                    MomentumSlope = 0m,
                    State = SqueezeStateEnum.Unknown,
                    StateDurationBars = 0
                };
            }
        }
    }
}



