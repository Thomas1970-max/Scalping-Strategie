using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using ATAS.DataFeedsCore;
using ATAS.Indicators;
using OFT.Attributes;
using Utils.Common.Logging;

namespace MyNamespace.Strategies.MarketAnalysis
{
    [DisplayName("Public Active Volume")]
    [Category(IndicatorCategories.VolumeOrderFlow)]
    public class PublicActiveVolume : Indicator
    {
        private readonly object _locker = new();

        private ILoggerSource? _loggerSource;

        private Dictionary<decimal, decimal> _totalValues = new();
        private List<CumulativeTrade> _cumulativeTrades = new();

        private DateTime _dateTimeFrom = DateTime.MinValue;
        private int _filter = 0;

        private bool _reloadTrades;
        private bool _tickBasedCalculation;

        private CumulativeTrade _lastTrade = new();
        private DateTime _lastTickTime;

        private decimal _totalVolume;
        private long _signature;

        [Range(0, int.MaxValue)]
        [Display(Name = "Filter", GroupName = "Settings", Order = 10)]
        public int Filter
        {
            get => _filter;
            set
            {
                _filter = Math.Max(0, value);
                RecalculateValues();
            }
        }

        [Display(Name = "Session Begin", GroupName = "Settings", Order = 20)]
        public DateTime DateFrom
        {
            get => _dateTimeFrom;
            set
            {
                _dateTimeFrom = value;
                _reloadTrades = true;
                RecalculateValues();
            }
        }

        [Browsable(false)]
        public decimal TotalVolume
        {
            get
            {
                lock (_locker)
                    return _totalVolume;
            }
        }

        [Browsable(false)]
        public long Signature
        {
            get
            {
                lock (_locker)
                    return _signature;
            }
        }

        public PublicActiveVolume()
            : base(true)
        {
            DataSeries[0].IsHidden = true;
            ((ValueDataSeries)DataSeries[0]).VisualType = VisualMode.Hide;
            DenyToChangePanel = true;

            _loggerSource = this as ILoggerSource;
        }

        [Browsable(false)]
        public ILoggerSource? LoggerSource
        {
            get => _loggerSource;
        }

        public void SetLoggerSource(ILoggerSource? loggerSource)
        {
            _loggerSource = loggerSource;
        }

        private void LogInfo(string message)
        {
            // In OvSnapshotHistory wird LoggerHelper auch mit loggerSource=null verwendet.
            // Falls LoggerHelper in dieser Version dennoch einen non-null sender erwartet,
            // fallbacken wir auf mögliche ATAS-Textlogs via Reflection.
            var src = _loggerSource;
            if (src != null)
            {
                try
                {
                    LoggerHelper.LogInfo(src, message);
                    return;
                }
                catch
                {
                    // ignore and fallback
                }
            }

            TryWriteTextLog(message);
        }

        private void TryWriteTextLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                // Einige ATAS-Kontexte stellen AddTextLog / Print / WriteLine bereit.
                // Wir rufen diese Methoden per Reflection auf, damit der Code unabhängig von API-Varianten baut.
                var t = GetType();
                var mi = t.GetMethod("AddTextLog", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null)
                {
                    mi.Invoke(this, new object[] { message });
                    return;
                }

                mi = t.GetMethod("Print", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null)
                {
                    mi.Invoke(this, new object[] { message });
                    return;
                }
            }
            catch
            {
                // ignore
            }
        }

        protected override void OnInitialize()
        {
            _reloadTrades = true;

            //LogInfo("[PublicActiveVolume] OnInitialize");
        }

        protected override void OnRecalculate()
        {
            lock (_locker)
            {
                _tickBasedCalculation = false;
                _totalValues.Clear();
                _cumulativeTrades.Clear();
                _totalVolume = 0m;
                _signature = 0;
                _lastTrade = new CumulativeTrade();
                _lastTickTime = default;
                _reloadTrades = true;
            }
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            if (bar == 0)
                _lastTickTime = GetCandle(0).Time;

            if (_tickBasedCalculation)
                return;

            if (bar < 0)
                return;

            if (IsNewSession(bar))
                ResetSession();
        }

        protected override void OnFinishRecalculate()
        {
            lock (_locker)
                _tickBasedCalculation = true;

            if (InstrumentInfo is null || !_reloadTrades)
                return;

            if (CurrentBar <= 0)
                return;

            var firstTime = GetCandle(0).Time;
            var lastTime = GetCandle(CurrentBar - 1).LastTime;

            var sessionStart = GetSessionStartTime(lastTime);
            var requestedFrom = _dateTimeFrom != DateTime.MinValue ? _dateTimeFrom : sessionStart;

            // WICHTIG: nicht gegen firstTime clampen. firstTime hängt vom geladenen Chart-Bereich ab.
            // Wenn der Chart z.B. erst ab 00:00 geladen ist, muss trotzdem ab SessionStart (z.B. 17:00 Vortag)
            // geladen werden, sonst fehlt Overnight-Volumen.
            var startTime = requestedFrom;
            // WICHTIG: Lookback relativ zur letzten verfügbaren Bar-Zeit clampen, NICHT relativ zu "jetzt".
            // Sonst wird bei historischen Charts (z.B. Nov 2025) startTime fälschlich auf "heute-5d" gezogen
            // und danach auf lastTime geklemmt -> Resultat: nur 1 Trade.
            var lookbackLimit = lastTime.AddDays(-5);
            if (startTime < lookbackLimit)
                startTime = lookbackLimit;

            if (startTime > lastTime)
                startTime = lastTime;

            //LogInfo($"[PublicActiveVolume] Recalc request: firstBarTime={firstTime:O} lastBarTime={lastTime:O} sessionStart={sessionStart:O} requestedFrom={requestedFrom:O} requestStartTime={startTime:O} filter={_filter}");

            var request = new CumulativeTradesRequest(startTime, lastTime, 0, 0);
            RequestForCumulativeTrades(request);
        }

        private DateTime GetSessionStartTime(DateTime referenceTime)
        {
            if (TryGetSessionTimes(out var start, out _))
            {
                var sessionStart = referenceTime.Date.Add(start);

                // Session kann über Mitternacht laufen (z.B. 17:00 -> 16:00). Wenn wir vor dem Start sind,
                // gehört referenceTime noch zur Session vom Vortag.
                if (referenceTime.TimeOfDay < start)
                    sessionStart = sessionStart.AddDays(-1);

                return sessionStart;
            }

            // Fallback: Bestimme Session-Start über Bars/IsNewSession (ATAS-intern). Das ist robust, auch wenn
            // WorkingTime/TradingOptions nicht erreichbar sind.
            var startBar = FindCurrentSessionStartBar();
            var candle = GetCandle(startBar);
            return candle != null ? candle.Time : referenceTime;
        }

        private int FindCurrentSessionStartBar()
        {
            var last = Math.Max(0, CurrentBar - 1);
            for (int i = last; i >= 0; i--)
            {
                if (IsNewSession(i))
                    return i;
            }

            return 0;
        }

        private bool TryGetSessionTimes(out TimeSpan start, out TimeSpan end)
        {
            // Default
            start = default;
            end = default;

            // 1) Security.WorkingTime (StartTime/EndTime)
            if (TryGetWorkingTime(out start, out end))
                return true;

            // 2) TradingOptions (SessionBeginTime/SessionEndTime)
            if (TryGetTradingOptionsTimes(out start, out end))
                return true;

            return false;
        }

        private bool TryGetWorkingTime(out TimeSpan start, out TimeSpan end)
        {
            start = default;
            end = default;

            var security = GetPropertyValue(this, "Security");
            if (security is null)
                return false;

            var workingTime = GetPropertyValue(security, "WorkingTime");
            if (workingTime is null)
                return false;

            if (!TryReadTimeSpan(workingTime, "StartTime", out start))
                return false;
            if (!TryReadTimeSpan(workingTime, "EndTime", out end))
                end = default;

            return true;
        }

        private bool TryGetTradingOptionsTimes(out TimeSpan start, out TimeSpan end)
        {
            start = default;
            end = default;

            // Je nach ATAS-Version kann TradingOptions an unterschiedlichen Stellen hängen.
            // Wir probieren zuerst this.TradingOptions, dann ChartInfo.TradingOptions.
            var tradingOptions = GetPropertyValue(this, "TradingOptions")
                ?? GetPropertyValue(GetPropertyValue(this, "ChartInfo"), "TradingOptions");

            if (tradingOptions is null)
                return false;

            if (!TryReadTimeSpan(tradingOptions, "SessionBeginTime", out start))
                return false;
            if (!TryReadTimeSpan(tradingOptions, "SessionEndTime", out end))
                end = default;

            return true;
        }

        private static object? GetPropertyValue(object? obj, string propertyName)
        {
            if (obj is null)
                return null;

            var t = obj.GetType();
            var pi = t.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return pi?.GetValue(obj);
        }

        private static bool TryReadTimeSpan(object obj, string propertyName, out TimeSpan value)
        {
            value = default;
            if (obj is null)
                return false;

            var raw = GetPropertyValue(obj, propertyName);
            if (raw is null)
                return false;

            if (raw is TimeSpan ts)
            {
                value = ts;
                return true;
            }

            // Manche ATAS-Properties können als DateTime/Nullable<DateTime> o.ä. geliefert werden.
            if (raw is DateTime dt)
            {
                value = dt.TimeOfDay;
                return true;
            }

            try
            {
                value = (TimeSpan)Convert.ChangeType(raw, typeof(TimeSpan));
                return true;
            }
            catch
            {
                return false;
            }
        }

        protected override void OnCumulativeTradesResponse(CumulativeTradesRequest request, IEnumerable<CumulativeTrade> cumulativeTrades)
        {
            _reloadTrades = false;

            int count = 0;
            DateTime minTime = DateTime.MaxValue;
            DateTime maxTime = DateTime.MinValue;
            if (cumulativeTrades != null)
            {
                foreach (var ct in cumulativeTrades)
                {
                    count++;
                    if (ct.Time < minTime) minTime = ct.Time;
                    if (ct.Time > maxTime) maxTime = ct.Time;
                }
            }

            //LogInfo($"[PublicActiveVolume] CumulativeTradesResponse: requestRange={FormatRequestRange(request)} trades={count} span={(count > 0 ? $"{minTime:O}..{maxTime:O}" : "<empty>")}");

            lock (_locker)
            {
                _cumulativeTrades = cumulativeTrades?.ToList() ?? new List<CumulativeTrade>();
                RebuildFromTrades_NoLock();
            }
        }

        private static string FormatRequestRange(CumulativeTradesRequest request)
        {
            if (request is null)
                return "<null>";

            // ATAS-Versionen unterscheiden sich teils bei Property-Namen. Wir lesen per Reflection.
            DateTime? from = TryReadDateTime(request, "From")
                ?? TryReadDateTime(request, "FromTime")
                ?? TryReadDateTime(request, "Start")
                ?? TryReadDateTime(request, "StartTime")
                ?? TryReadDateTime(request, "Begin")
                ?? TryReadDateTime(request, "BeginTime");

            DateTime? to = TryReadDateTime(request, "To")
                ?? TryReadDateTime(request, "ToTime")
                ?? TryReadDateTime(request, "End")
                ?? TryReadDateTime(request, "EndTime");

            if (from.HasValue && to.HasValue)
                return $"{from.Value:O}..{to.Value:O}";
            if (from.HasValue)
                return $"{from.Value:O}..";
            if (to.HasValue)
                return $"..{to.Value:O}";

            return request.ToString();
        }

        private static DateTime? TryReadDateTime(object obj, string propertyName)
        {
            var raw = GetPropertyValue(obj, propertyName);
            if (raw is null)
                return null;

            if (raw is DateTime dt)
                return dt;

            if (raw is DateTimeOffset dto)
                return dto.DateTime;

            if (raw is long ticks)
            {
                try { return new DateTime(ticks); } catch { return null; }
            }

            return null;
        }

        protected override void OnCumulativeTrade(CumulativeTrade trade)
        {
            if (ChartInfo is null)
                return;

            if (CurrentBar <= 0)
                return;

            lock (_locker)
            {
                if (DataProvider != null && _lastTickTime != default && DataProvider.IsNewSession(_lastTickTime, trade.Time))
                    ResetSession_NoLock();

                _lastTickTime = trade.Time;

                _cumulativeTrades.Add(trade);

                if (trade.NewBid.Volume < _filter && trade.NewAsk.Volume < _filter)
                    return;

                AddTrade_NoLock(trade.FirstPrice, trade.Volume);
                _lastTrade = trade;
            }
        }

        protected override void OnUpdateCumulativeTrade(CumulativeTrade trade)
        {
            if (ChartInfo is null)
                return;

            lock (_locker)
            {
                if (CurrentBar <= 0 || _cumulativeTrades.Count == 0)
                    return;

                if (DataProvider != null && _lastTickTime != default && DataProvider.IsNewSession(_lastTickTime, trade.Time))
                    ResetSession_NoLock();

                _lastTickTime = trade.Time;

                _cumulativeTrades.RemoveAt(_cumulativeTrades.Count - 1);
                _cumulativeTrades.Add(trade);

                if (trade.NewBid.Volume < _filter && trade.NewAsk.Volume < _filter)
                    return;

                var containsTrade = _lastTrade.IsEqual(trade);
                var incValue = containsTrade ? trade.Volume - _lastTrade.Volume : trade.Volume;

                AddTrade_NoLock(trade.FirstPrice, incValue);
                _lastTrade = trade;
            }
        }

        public Dictionary<decimal, decimal> GetTotalSnapshot()
        {
            lock (_locker)
                return new Dictionary<decimal, decimal>(_totalValues);
        }

        private void ResetSession()
        {
            lock (_locker)
                ResetSession_NoLock();
        }

        private void ResetSession_NoLock()
        {
            _totalValues.Clear();
            _cumulativeTrades.Clear();
            _totalVolume = 0m;
            _signature = 0;
            _lastTrade = new CumulativeTrade();
        }

        private void AddTrade_NoLock(decimal price, decimal volume)
        {
            if (volume == 0m)
                return;

            _totalValues.TryGetValue(price, out var v);
            var nv = v + volume;

            if (nv == 0m)
                _totalValues.Remove(price);
            else
                _totalValues[price] = nv;

            _totalVolume += volume;

            unchecked
            {
                long p = (long)(price * 10000m);
                long dv = (long)volume;
                _signature = (_signature * 397) ^ p;
                _signature = (_signature * 397) ^ dv;
            }
        }

        private void RebuildFromTrades_NoLock()
        {
            _totalValues.Clear();
            _totalVolume = 0m;
            _signature = 0;

            foreach (var ct in _cumulativeTrades)
            {
                if (ct.NewBid.Volume < _filter && ct.NewAsk.Volume < _filter)
                    continue;

                AddTrade_NoLock(ct.FirstPrice, ct.Volume);
            }
        }
    }
}
