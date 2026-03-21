using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ATAS.DataFeedsCore;
using ATAS.Indicators;
using MyNamespace.Strategies.Models;
using MyNamespace.Strategies.Orderflow;
using Utils.Common.Logging;
using static MyNamespace.Strategies.Geldfluss3_3;

namespace MyNamespace.Strategies.MarketAnalysis
{
    public class VolumeProfileGenerator
    {
        private readonly Func<int, IndicatorCandle> _getCandle;
        private readonly Func<decimal, int> _toTickIndex;
        private readonly Func<int, decimal> _fromTickIndex;
        private readonly Func<decimal, decimal> _roundToTick;

        
        // Delegates als Delegate speichern, um sowohl Action als auch Action<string> zu akzeptieren
        private readonly Delegate _logInfo;
        private readonly Delegate _logWarn;
        private readonly Delegate _logDebug;

        private int _lastBarCalculatedForVolumeProfile = -1;
        private decimal _cachedPdPOC, _cachedPdVAH, _cachedPdVAL;

        public VolumeProfileGenerator(
            Func<int, IndicatorCandle> getCandle,
            Func<decimal, int> toTickIndex,
            Func<int, decimal> fromTickIndex,
            Func<decimal, decimal> roundToTick,
            Delegate logInfo = null,
            Delegate logWarn = null,
            Delegate logDebug = null)
        {
            _getCandle = getCandle ?? throw new ArgumentNullException(nameof(getCandle));
            _toTickIndex = toTickIndex ?? throw new ArgumentNullException(nameof(toTickIndex));
            _fromTickIndex = fromTickIndex ?? throw new ArgumentNullException(nameof(fromTickIndex));
            _roundToTick = roundToTick ?? throw new ArgumentNullException(nameof(roundToTick));

            _logInfo = logInfo;
            _logWarn = logWarn;
            _logDebug = logDebug;
        }

        // Hilfsfunktion: versucht, den Delegate mit einem String-Argument aufzurufen,
        // fallback: rufe parameterlose Delegate auf, fallback: nichts tun.
        private void SafeLog(Delegate dlg, string message)
        {
            if (dlg == null) return;

            try
            {
                var minParams = dlg.Method.GetParameters().Length;
                if (minParams == 0)
                    dlg.DynamicInvoke();
                else
                    dlg.DynamicInvoke(message);
            }
            catch (ArgumentException)
            {
                // Falls das Argument nicht passt (z. B. erwartet object statt string), nochmal mit object
                try { dlg.DynamicInvoke((object)message); } catch { /* swallow */ }
            }
            catch
            {
                // swallow any logging exception to avoid breaking main logic
            }
        }

        public (decimal poc, decimal vah, decimal val) GetPreviousDayVolumeProfile(int endBar, decimal valueAreaFraction = 0.70m)
        {
            if (endBar == _lastBarCalculatedForVolumeProfile)
            {
                SafeLog(_logDebug, $"VPG: returning cached for bar {endBar}");
                return (_cachedPdPOC, _cachedPdVAH, _cachedPdVAL);
            }

            if (endBar < 0)
            {
                _cachedPdPOC = _cachedPdVAH = _cachedPdVAL = 0m;
                return (_cachedPdPOC, _cachedPdVAH, _cachedPdVAL);
            }

            var range = FindPreviousDayBarRange(endBar);
            if (range.startBar < 0 || range.endBar < 0)
            {
                _cachedPdPOC = _cachedPdVAH = _cachedPdVAL = 0m;
                return (_cachedPdPOC, _cachedPdVAH, _cachedPdVAL);
            }

            var hist = BuildVolumeProfileFromClusters(range.startBar, range.endBar);
            CalculatePOC_VAH_VAL_FromTickIndexHist(hist, valueAreaFraction, out _cachedPdPOC, out _cachedPdVAH, out _cachedPdVAL);

            _lastBarCalculatedForVolumeProfile = endBar;
            return (_cachedPdPOC, _cachedPdVAH, _cachedPdVAL);
        }

        private (int startBar, int endBar) FindPreviousDayBarRange(int endBarInclusive)
        {
            SafeLog(_logInfo, $"FindPrevDayRange: enter endBar={endBarInclusive}");
            if (endBarInclusive < 0) return (-1, -1);

            var last = _getCandle(endBarInclusive);
            if (last == null)
            {
                SafeLog(_logInfo, $"FindPrevDayRange: last candle null for endBar={endBarInclusive}");
                return (-1, -1);
            }

            var day = last.Time.Date;
            SafeLog(_logInfo, $"FindPrevDayRange: last.Time={last.Time:o} day={day:yyyy-MM-dd}");

            int i = endBarInclusive;
            while (i > 0)
            {
                var prev = _getCandle(i - 1);
                if (prev == null)
                {
                    SafeLog(_logInfo, $"FindPrevDayRange: prev=null at i={i} ? break");
                    break;
                }
                if (prev.Time.Date != day)
                {
                    SafeLog(_logInfo, $"FindPrevDayRange: boundary hit at i={i}, prev.Time={prev.Time:o}");
                    break;
                }
                i--;
            }

            var startCandle = _getCandle(i);
            SafeLog(_logInfo, $"FindPrevDayRange: startBar={i} time={(startCandle?.Time.ToString("o") ?? "N/A")}, endBar={endBarInclusive} time={last.Time:o}");

            return (i, endBarInclusive);
        }

        private IEnumerable<(decimal Price, decimal Volume)> EnumerateClusterRows(IndicatorCandle c)
        {
            if (c == null) yield break;

            var priceLevels = c.GetAllPriceLevels();
            if (priceLevels == null) yield break;

            foreach (var pv in priceLevels)
            {
                if (pv == null) continue;

                decimal price = (decimal)pv.Price;
                decimal vol = (decimal)pv.Volume;
                if (vol <= 0m) vol = (decimal)pv.Ask + (decimal)pv.Bid;

                if (vol > 0m)
                    yield return (_roundToTick(price), vol);
            }
        }

        private Dictionary<int, decimal> BuildVolumeProfileFromClusters(int startBar, int endBar)
        {
            var hist = new Dictionary<int, decimal>(4096);

            for (int i = startBar; i <= endBar; i++)
            {
                var c = _getCandle(i);
                if (c == null) continue;

                foreach (var (price, vol) in EnumerateClusterRows(c))
                {
                    if (vol <= 0m) continue;
                    int idx = _toTickIndex(price);
                    if (hist.TryGetValue(idx, out var v)) hist[idx] = v + vol;
                    else hist[idx] = vol;
                }
            }

            return hist;
        }

        private void CalculatePOC_VAH_VAL_FromTickIndexHist(
            Dictionary<int, decimal> hist,
            decimal valueAreaFraction,
            out decimal pocPrice,
            out decimal vahPrice,
            out decimal valPrice)
        {
            pocPrice = vahPrice = valPrice = 0m;
            if (hist == null || hist.Count == 0) return;

            decimal total = 0m;
            foreach (var v in hist.Values) total += v;
            if (total <= 0m) return;

            int pocIdx = 0; decimal pocVol = -1m;
            foreach (var kv in hist)
                if (kv.Value > pocVol) { pocVol = kv.Value; pocIdx = kv.Key; }

            decimal GetVol(int idx) => hist.TryGetValue(idx, out var v) ? v : 0m;

            int lo = pocIdx, hi = pocIdx;
            decimal cum = pocVol;
            decimal target = total * valueAreaFraction;

            while (cum < target)
            {
                int nextLo = lo - 1;
                int nextHi = hi + 1;

                bool hasLo = hist.ContainsKey(nextLo);
                bool hasHi = hist.ContainsKey(nextHi);

                if (!hasLo && !hasHi) break;

                if (hasLo && !hasHi)
                {
                    decimal volLo = GetVol(nextLo);
                    lo = nextLo; cum += volLo;
                    continue;
                }
                if (!hasLo && hasHi)
                {
                    decimal volHi = GetVol(nextHi);
                    hi = nextHi; cum += volHi;
                    continue;
                }

                decimal volLo2 = GetVol(nextLo);
                decimal volHi2 = GetVol(nextHi);

                decimal cumLo = cum + volLo2;
                decimal cumHi = cum + volHi2;

                decimal diffLo = Math.Abs(target - cumLo);
                decimal diffHi = Math.Abs(target - cumHi);

                bool takeLower;
                if (diffLo < diffHi) takeLower = true;
                else if (diffHi < diffLo) takeLower = false;
                else
                {
                    if (volLo2 > volHi2) takeLower = true;
                    else if (volHi2 > volLo2) takeLower = false;
                    else takeLower = false;
                }

                if (takeLower)
                {
                    lo = nextLo; cum = cumLo;
                }
                else
                {
                    hi = nextHi; cum = cumHi;
                }
            }

            pocPrice = _fromTickIndex(pocIdx);
            valPrice = _fromTickIndex(lo);
            vahPrice = _fromTickIndex(hi);
        }
    }
}

