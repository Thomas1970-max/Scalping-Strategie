using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.MarketAnalysis;
using MyNamespace.Strategies.Models;
using static MyNamespace.Strategies.Goldfluss3_3;

namespace MyNamespace.Strategies.Orderflow
{
    /// <summary>
    /// Platzhalter für den Marktstrukturkontext.
    /// Diese Klasse wird später mit detaillierten Informationen über
    /// signifikante Preislevel, Volumenprofile (HVNs, LVNs), VWAP, VAH/VAL/POC etc. gefüllt.
    /// Für die aktuelle Phase der Orderflow-Mustererkennung wird sie vorerst als leerer Kontext übergeben.
    /// </summary>
    public class MarketStructureContext
    {
        private enum ZigZagDir
        {
            Unknown,
            Up,
            Down
        }

        public enum ZoneType
        {
            Resistance,
            Support
        }

        public enum ZoneStatus
        {
            New,
            Ready,
            Triggered,
            Used
        }

        public sealed class Zone
        {
            public int Id { get; init; }
            public ZoneType Type { get; init; }
            public decimal Low { get; set; }
            public decimal High { get; set; }
            public ZoneStatus Status { get; set; }
            public int PivotBar { get; init; }
            public bool IsConfirmed { get; set; } = true;
            public int CreatedBar { get; init; }
            public int? ReadyBar { get; set; }
            public int TouchCount { get; set; }
            public int LastTouchedBar { get; set; }
            public int BreakCloseCount { get; set; }
            public decimal? CreatedVwap { get; set; }
            public bool IsMultiTouch { get; set; }
            public int MultiTouchScore { get; set; }

            public decimal Mid => (Low + High) / 2m;
            public decimal Height => Math.Abs(High - Low);
        }

        private sealed class ArchivedZone
        {
            public ZoneType Type { get; init; }
            public decimal Low { get; init; }
            public decimal High { get; init; }
            public decimal Mid { get; init; }
            public int RemovedBar { get; init; }
        }

        public sealed class EntrySignal
        {
            public bool HasSignal { get; init; }
            public OrderDirections Direction { get; init; }
            public int Bar { get; init; }
            public int ZoneId { get; init; }
            public ZoneType ZoneType { get; init; }
            public decimal ZoneLow { get; set; }
            public decimal ZoneHigh { get; set; }
            public string Reason { get; init; } = string.Empty;
        }

        private readonly Queue<decimal> _deltaWindow = new();
        private readonly Queue<(int Bar, IMarketCandle Candle)> _recentCandles = new();
        private const int RecentCandleBuffer = 30;

        private ZigZagDir _zzDir = ZigZagDir.Unknown;
        private decimal _zzCandidateHigh;
        private int _zzCandidateHighBar;
        private decimal _zzCandidateLow;
        private int _zzCandidateLowBar;
        private int _nextZoneId = 1;

        private int _lastConfirmedSwingHighBar = -1;
        private decimal? _lastConfirmedSwingHigh;
        private int _lastConfirmedSwingLowBar = -1;
        private decimal? _lastConfirmedSwingLow;
        private ZigZagDir _lastConfirmedSwingDir = ZigZagDir.Unknown;

        private readonly List<ArchivedZone> _archivedZones = new();
        public int ZoneArchiveLookbackBars { get; set; } = 400;
        public int ZoneArchiveMatchDistanceTicks { get; set; } = 8;
        public int ZoneArchiveMaxItems { get; set; } = 200;

        public List<Zone> ActiveZones { get; } = new List<Zone>();
        public EntrySignal? LastSignal { get; private set; }

        public int WickZoneMinTicks { get; set; } = 3;
        public bool ZigZagIgnoreWicks { get; set; } = true;
        public int ZigZagMinMoveTicks { get; set; } = 10;
        public int ZigZagLeftBars { get; set; } = 2;
        public int ZigZagRightBars { get; set; } = 1;
        public decimal ZoneReadyMoveAwayMultiple { get; set; } = 2.5m;
        public int ZoneReadyDistanceTicks { get; set; } = 4;
        public int ZoneBreakDistanceTicks { get; set; } = 1;
        public int ZoneMergeDistanceTicks { get; set; } = 2;
        public int DeltaLookbackBars { get; set; } = 20;
        public decimal DeltaSpikeMultiplier { get; set; } = 3m;
        public decimal DeltaNeutralizationMaxFactor { get; set; } = 0.35m;
        public int MomentumProtectionBars { get; set; } = 3;
        public int MomentumProtectionBodyTicksMin { get; set; } = 6;

        public void Reset()
        {
            ActiveZones.Clear();
            _deltaWindow.Clear();
            _recentCandles.Clear();
            _zzDir = ZigZagDir.Unknown;
            _zzCandidateHigh = 0m;
            _zzCandidateHighBar = 0;
            _zzCandidateLow = 0m;
            _zzCandidateLowBar = 0;
            _nextZoneId = 1;
            _lastConfirmedSwingHighBar = -1;
            _lastConfirmedSwingHigh = null;
            _lastConfirmedSwingLowBar = -1;
            _lastConfirmedSwingLow = null;
            _lastConfirmedSwingDir = ZigZagDir.Unknown;
            _archivedZones.Clear();
            LastSignal = null;
        }

        public void Update(
            int bar,
            IMarketCandle candle,
            OvSnapshot? snapshot,
            decimal tickSize,
            decimal vwap,
            IReadOnlyList<OfFeatures>? recentOf = null,
            bool allowZoneCreation = true,
            bool allowZoneLifecycle = true)
        {
            if (candle == null)
                return;

            if (tickSize <= 0m)
                tickSize = 0.25m;
            if (allowZoneCreation)
            {
                EnqueueRecentCandle(bar, candle);
                TryUpdateZigZagAndCreateZones(bar, tickSize, vwap);
            }

            if (allowZoneLifecycle)
            {
                var readyDist = Math.Max(0, ZoneReadyDistanceTicks) * tickSize;
                var breakDist = Math.Max(0, ZoneBreakDistanceTicks) * tickSize;
                UpdateZoneLifecycle(bar, candle, readyDist, breakDist);
            }
        }

        private bool TryConfirmPendingZoneFromSwing(int swingBar, ZoneType type, decimal tickSize, decimal vwap)
        {
            var pending = ActiveZones.FirstOrDefault(z => z != null && z.PivotBar == swingBar && !z.IsConfirmed && z.Type == type);
            if (pending == null)
                return false;

            var bounds = ComputeZoneBoundsFromSwing(swingBar, type, tickSize);
            if (!bounds.HasValue)
                return false;

            pending.Low = bounds.Value.low;
            pending.High = bounds.Value.high;
            pending.IsConfirmed = true;
            pending.BreakCloseCount = 0;
            pending.CreatedVwap = vwap;
            return true;
        }

        public void MarkZoneUsed(int zoneId)
        {
            var z = ActiveZones.FirstOrDefault(x => x.Id == zoneId);
            if (z == null)
                return;

            z.Status = ZoneStatus.Used;
        }

        private void UpdateDeltaWindow(decimal netDelta)
        {
            int n = Math.Max(1, DeltaLookbackBars);
            _deltaWindow.Enqueue(Math.Abs(netDelta));
            while (_deltaWindow.Count > n)
                _deltaWindow.Dequeue();
        }

        private decimal GetDeltaAverageAbs()
        {
            if (_deltaWindow.Count == 0)
                return 0m;
            return _deltaWindow.Average();
        }

        private bool IsDeltaSpike(decimal netDelta)
        {
            var avg = GetDeltaAverageAbs();
            if (avg <= 0m)
                return false;

            return Math.Abs(netDelta) > avg * Math.Max(1m, DeltaSpikeMultiplier);
        }

        private bool IsDeltaNeutralized(decimal netDelta)
        {
            var avg = GetDeltaAverageAbs();
            if (avg <= 0m)
                return true;
            var maxAbs = avg * Math.Max(0.01m, DeltaSpikeMultiplier);
            var cap = maxAbs * Math.Max(0m, DeltaNeutralizationMaxFactor);
            return Math.Abs(netDelta) <= cap;
        }

        private void EnqueueRecentCandle(int bar, IMarketCandle candle)
        {
            _recentCandles.Enqueue((bar, candle));
            while (_recentCandles.Count > RecentCandleBuffer)
                _recentCandles.Dequeue();
        }

        private void TryUpdateZigZagAndCreateZones(int bar, decimal tickSize, decimal vwap)
        {
            if (_recentCandles.Count == 0)
                return;

            int left = Math.Max(1, ZigZagLeftBars);
            int right = Math.Max(1, ZigZagRightBars);
            int n = _recentCandles.Count;
            var items = _recentCandles.ToArray();

            var lastC = items[n - 1].Candle;
            if (lastC != null)
                InvalidatePendingZonesOnClose(bar, lastC, tickSize);

            if (n >= left + 1)
                TryCreatePendingSwingZones(items, n, left, bar, tickSize, vwap);

            if (n < left + right + 1)
                return;

            int centerIndex = n - 1 - right;
            if (centerIndex < left)
                return;

            var center = items[centerIndex];
            var centerC = center.Candle;
            if (centerC == null)
                return;

            decimal centerHigh = centerC.High;
            decimal centerLow = centerC.Low;

            bool isSwingHigh = true;
            bool isSwingLow = true;
            for (int i = centerIndex - left; i <= centerIndex + right; i++)
            {
                if (i == centerIndex)
                    continue;

                var c = items[i].Candle;
                if (c == null)
                    continue;

                var hi = c.High;
                var lo = c.Low;
                if (hi > centerHigh) isSwingHigh = false;
                if (lo < centerLow) isSwingLow = false;
                if (!isSwingHigh && !isSwingLow)
                    break;
            }

            var minMove = Math.Max(1, ZigZagMinMoveTicks) * tickSize;
            int swingBar = center.Bar;

            if (isSwingHigh && swingBar != _lastConfirmedSwingHighBar)
            {
                if (TryConfirmPendingZoneFromSwing(swingBar, ZoneType.Resistance, tickSize, vwap))
                {
                    _lastConfirmedSwingHighBar = swingBar;
                    _lastConfirmedSwingHigh = centerHigh;
                    _lastConfirmedSwingDir = ZigZagDir.Up;
                    return;
                }

                bool moveOk = !_lastConfirmedSwingLow.HasValue || (centerHigh - _lastConfirmedSwingLow.Value) >= minMove;
                bool alternationOk = _lastConfirmedSwingDir != ZigZagDir.Up;
                if (moveOk && alternationOk)
                {
                    ConfirmOrBuildZoneFromSwing(swingBar, ZoneType.Resistance, tickSize, vwap);
                    _lastConfirmedSwingHighBar = swingBar;
                    _lastConfirmedSwingHigh = centerHigh;
                    _lastConfirmedSwingDir = ZigZagDir.Up;
                }
                else
                {
                    _lastConfirmedSwingHighBar = swingBar;
                    _lastConfirmedSwingHigh = centerHigh;
                }
            }

            if (isSwingLow && swingBar != _lastConfirmedSwingLowBar)
            {
                if (TryConfirmPendingZoneFromSwing(swingBar, ZoneType.Support, tickSize, vwap))
                {
                    _lastConfirmedSwingLowBar = swingBar;
                    _lastConfirmedSwingLow = centerLow;
                    _lastConfirmedSwingDir = ZigZagDir.Down;
                    return;
                }

                bool moveOk = !_lastConfirmedSwingHigh.HasValue || (_lastConfirmedSwingHigh.Value - centerLow) >= minMove;
                bool alternationOk = _lastConfirmedSwingDir != ZigZagDir.Down;
                if (moveOk && alternationOk)
                {
                    ConfirmOrBuildZoneFromSwing(swingBar, ZoneType.Support, tickSize, vwap);
                    _lastConfirmedSwingLowBar = swingBar;
                    _lastConfirmedSwingLow = centerLow;
                    _lastConfirmedSwingDir = ZigZagDir.Down;
                }
                else
                {
                    _lastConfirmedSwingLowBar = swingBar;
                    _lastConfirmedSwingLow = centerLow;
                }
            }
        }

        private void TryCreatePendingSwingZones(
            (int Bar, IMarketCandle Candle)[] items,
            int n,
            int left,
            int bar,
            decimal tickSize,
            decimal vwap)
        {
            int centerIndex = n - 1;
            if (centerIndex < left)
                return;

            var center = items[centerIndex];
            var centerC = center.Candle;
            if (centerC == null)
                return;

            int swingBar = center.Bar;
            if (HasPendingOrConfirmedZoneForPivot(swingBar))
                return;

            decimal centerHigh = centerC.High;
            decimal centerLow = centerC.Low;

            bool isSwingHigh = true;
            bool isSwingLow = true;
            for (int i = centerIndex - left; i <= centerIndex - 1; i++)
            {
                var c = items[i].Candle;
                if (c == null)
                    continue;

                var hi = c.High;
                var lo = c.Low;
                if (hi > centerHigh) isSwingHigh = false;
                if (lo < centerLow) isSwingLow = false;
                if (!isSwingHigh && !isSwingLow)
                    break;
            }

            if (isSwingHigh)
                BuildPendingZoneFromSwing(swingBar, ZoneType.Resistance, tickSize, vwap);
            if (isSwingLow)
                BuildPendingZoneFromSwing(swingBar, ZoneType.Support, tickSize, vwap);
        }

        private bool HasPendingOrConfirmedZoneForPivot(int pivotBar)
        {
            foreach (var z in ActiveZones)
            {
                if (z == null)
                    continue;
                if (z.PivotBar == pivotBar)
                    return true;
            }
            return false;
        }

        private void InvalidatePendingZonesOnClose(int currentBar, IMarketCandle c, decimal tickSize)
        {
            var breakDist = Math.Max(0, ZoneBreakDistanceTicks) * (tickSize > 0m ? tickSize : 0.25m);
            for (int i = ActiveZones.Count - 1; i >= 0; i--)
            {
                var z = ActiveZones[i];
                if (z == null)
                    continue;
                if (z.IsConfirmed)
                    continue;
                if (z.Status == ZoneStatus.Used)
                    continue;

                if (z.Type == ZoneType.Support)
                {
                    if (c.Close < z.Low - breakDist)
                    {
                        ArchiveZone(z, removedBar: currentBar);
                        ActiveZones.RemoveAt(i);
                    }
                }
                else
                {
                    if (c.Close > z.High + breakDist)
                    {
                        ArchiveZone(z, removedBar: currentBar);
                        ActiveZones.RemoveAt(i);
                    }
                }
            }
        }

        private void ConfirmOrBuildZoneFromSwing(int swingBar, ZoneType type, decimal tickSize, decimal vwap)
        {
            var pending = ActiveZones.FirstOrDefault(z => z != null && z.PivotBar == swingBar && !z.IsConfirmed && z.Type == type);
            if (pending != null)
            {
                var bounds = ComputeZoneBoundsFromSwing(swingBar, type, tickSize);
                if (!bounds.HasValue)
                    return;

                pending.Low = bounds.Value.low;
                pending.High = bounds.Value.high;
                pending.IsConfirmed = true;
                pending.BreakCloseCount = 0;
                pending.CreatedVwap = vwap;
                return;
            }

            BuildZoneFromSwing(swingBar, type, tickSize, vwap);
        }

        private void BuildPendingZoneFromSwing(int swingBar, ZoneType type, decimal tickSize, decimal vwap)
        {
            var bounds = ComputeZoneBoundsFromSwing(swingBar, type, tickSize);
            if (!bounds.HasValue)
                return;

            AddOrMergeZone(new Zone
            {
                Id = _nextZoneId++,
                Type = type,
                Low = bounds.Value.low,
                High = bounds.Value.high,
                Status = ZoneStatus.Ready,
                PivotBar = swingBar,
                IsConfirmed = false,
                CreatedBar = swingBar,
                ReadyBar = swingBar,
                LastTouchedBar = swingBar,
                TouchCount = 0,
                BreakCloseCount = 0,
                CreatedVwap = vwap
            }, tickSize);
        }

        private (decimal low, decimal high)? ComputeZoneBoundsFromSwing(int swingBar, ZoneType type, decimal tickSize)
        {
            var swing = _recentCandles.FirstOrDefault(x => x.Bar == swingBar);
            if (swing.Candle == null)
                return null;

            var c = swing.Candle;

            if (type == ZoneType.Support)
            {
                var minLow = c.Low;
                var bodyLow = Math.Min(c.Open, c.Close);
                var wickSize = bodyLow - minLow;
                var minWickSize = Math.Max(1, WickZoneMinTicks) * tickSize;
                if (wickSize < minWickSize)
                    return null;

                var top = bodyLow;
                if (top <= minLow)
                    return null;

                return (minLow, top);
            }

            var maxHigh = c.High;
            var bodyHigh = Math.Max(c.Open, c.Close);
            var wickSizeR = maxHigh - bodyHigh;
            var minWickSizeR = Math.Max(1, WickZoneMinTicks) * tickSize;
            if (wickSizeR < minWickSizeR)
                return null;

            var bottom = bodyHigh;
            if (bottom >= maxHigh)
                return null;

            return (bottom, maxHigh);
        }

        private void BuildZoneFromSwing(int swingBar, ZoneType type, decimal tickSize, decimal vwap)
        {
            var swing = _recentCandles.FirstOrDefault(x => x.Bar == swingBar);
            if (swing.Candle == null)
                return;

            var c = swing.Candle;

            if (type == ZoneType.Support)
            {
                var minLow = c.Low;
                var bodyLow = Math.Min(c.Open, c.Close);
                var wickSize = bodyLow - minLow;
                var minWickSize = Math.Max(1, WickZoneMinTicks) * tickSize;
                if (wickSize < minWickSize)
                    return;

                var top = bodyLow;
                if (top <= minLow)
                    return;

                AddOrMergeZone(new Zone
                {
                    Id = _nextZoneId++,
                    Type = ZoneType.Support,
                    Low = minLow,
                    High = top,
                    Status = ZoneStatus.New,
                    PivotBar = swingBar,
                    IsConfirmed = true,
                    CreatedBar = swingBar,
                    LastTouchedBar = swingBar,
                    TouchCount = 0,
                    BreakCloseCount = 0,
                    CreatedVwap = vwap
                }, tickSize);
            }
            else
            {
                var maxHigh = c.High;
                var bodyHigh = Math.Max(c.Open, c.Close);
                var wickSize = maxHigh - bodyHigh;
                var minWickSize = Math.Max(1, WickZoneMinTicks) * tickSize;
                if (wickSize < minWickSize)
                    return;

                var bottom = bodyHigh;
                if (bottom >= maxHigh)
                    return;

                AddOrMergeZone(new Zone
                {
                    Id = _nextZoneId++,
                    Type = ZoneType.Resistance,
                    Low = bottom,
                    High = maxHigh,
                    Status = ZoneStatus.New,
                    PivotBar = swingBar,
                    IsConfirmed = true,
                    CreatedBar = swingBar,
                    LastTouchedBar = swingBar,
                    TouchCount = 0,
                    BreakCloseCount = 0,
                    CreatedVwap = vwap
                }, tickSize);
            }
        }

        private void AddOrMergeZone(Zone newZone, decimal tickSize)
        {
            if (newZone == null)
                return;

            ApplyArchivePromotion(newZone, currentBar: newZone.CreatedBar, tickSize: tickSize);

            var mergeDist = Math.Max(0, ZoneMergeDistanceTicks) * (tickSize > 0m ? tickSize : 0.25m);
            for (int i = ActiveZones.Count - 1; i >= 0; i--)
            {
                var z = ActiveZones[i];
                if (z == null)
                    continue;
                if (z.Type != newZone.Type)
                    continue;
                if (z.Status == ZoneStatus.Used)
                    continue;

                bool overlaps = newZone.Low <= z.High && newZone.High >= z.Low;
                if (!overlaps)
                    continue;

                // If a zone is already tradable (READY/TRIGGERED), a new swing zone that overlaps it
                // should not be stacked. However, if the new swing is more extreme, replace the
                // existing zone bounds while keeping the existing zone id/status.
                if (z.Status == ZoneStatus.Ready || z.Status == ZoneStatus.Triggered)
                {
                    bool replace = z.Type == ZoneType.Support
                        ? newZone.Low < z.Low
                        : newZone.High > z.High;

                    if (replace)
                    {
                        z.Low = newZone.Low;
                        z.High = newZone.High;
                        z.BreakCloseCount = 0;
                        ApplyArchivePromotion(z, currentBar: newZone.CreatedBar, tickSize: tickSize);
                    }
                    return;
                }

                // Merge should not widen zones. Keep the wick-width of the most extreme swing.
                if (z.Type == ZoneType.Support)
                {
                    if (newZone.Low < z.Low)
                    {
                        z.Low = newZone.Low;
                        z.High = newZone.High;
                    }
                }
                else
                {
                    if (newZone.High > z.High)
                    {
                        z.Low = newZone.Low;
                        z.High = newZone.High;
                    }
                }
                return;
            }

            ActiveZones.Add(newZone);
        }

        private void ApplyArchivePromotion(Zone zone, int currentBar, decimal tickSize)
        {
            if (zone == null)
                return;

            PurgeArchivedZones(currentBar);

            var distTicks = Math.Max(0, ZoneArchiveMatchDistanceTicks);
            var dist = distTicks * (tickSize > 0m ? tickSize : 0.25m);
            if (dist <= 0m)
                return;

            int score = 0;
            for (int i = _archivedZones.Count - 1; i >= 0; i--)
            {
                var a = _archivedZones[i];
                if (a == null)
                    continue;
                if (a.Type != zone.Type)
                    continue;

                bool near = (a.Low - dist) <= zone.High && (a.High + dist) >= zone.Low;
                if (!near)
                    continue;

                score++;
            }

            if (score > 0)
            {
                zone.IsMultiTouch = true;
                zone.MultiTouchScore = Math.Max(zone.MultiTouchScore, score);
                zone.TouchCount = Math.Max(zone.TouchCount, score);
            }
        }

        private void ArchiveZone(Zone z, int removedBar)
        {
            if (z == null)
                return;

            _archivedZones.Add(new ArchivedZone
            {
                Type = z.Type,
                Low = z.Low,
                High = z.High,
                Mid = (z.Low + z.High) / 2m,
                RemovedBar = removedBar
            });

            if (ZoneArchiveMaxItems > 0 && _archivedZones.Count > ZoneArchiveMaxItems)
            {
                int overflow = _archivedZones.Count - ZoneArchiveMaxItems;
                _archivedZones.RemoveRange(0, overflow);
            }
        }

        private void PurgeArchivedZones(int currentBar)
        {
            int lookback = Math.Max(0, ZoneArchiveLookbackBars);
            if (lookback <= 0)
                return;

            int minBar = currentBar - lookback;
            for (int i = _archivedZones.Count - 1; i >= 0; i--)
            {
                var a = _archivedZones[i];
                if (a == null)
                {
                    _archivedZones.RemoveAt(i);
                    continue;
                }

                if (a.RemovedBar < minBar)
                    _archivedZones.RemoveAt(i);
            }
        }

        private void UpdateZoneLifecycle(int bar, IMarketCandle c, decimal readyDistance, decimal breakDistance)
        {
            for (int i = ActiveZones.Count - 1; i >= 0; i--)
            {
                var z = ActiveZones[i];
                if (z.Status == ZoneStatus.Used)
                {
                    ArchiveZone(z, removedBar: bar);
                    ActiveZones.RemoveAt(i);
                    continue;
                }

                if (z.Type == ZoneType.Support)
                {
                    bool touched = c.High >= z.Low && c.Low <= z.High;
                    if (touched && z.LastTouchedBar != bar)
                    {
                        z.TouchCount++;
                        z.LastTouchedBar = bar;
                    }

                    if (c.Close < z.Low - breakDistance)
                        z.BreakCloseCount++;
                    else if (c.Close >= z.Low)
                        z.BreakCloseCount = 0;

                    if (z.BreakCloseCount >= 2)
                    {
                        ArchiveZone(z, removedBar: bar);
                        ActiveZones.RemoveAt(i);
                        continue;
                    }

                    if (z.Status == ZoneStatus.New)
                    {
                        var moveAway = readyDistance;
                        if (c.Close >= z.High + moveAway)
                        {
                            z.Status = ZoneStatus.Ready;
                            z.ReadyBar = bar;
                        }
                    }
                }
                else
                {
                    bool touched = c.High >= z.Low && c.Low <= z.High;
                    if (touched && z.LastTouchedBar != bar)
                    {
                        z.TouchCount++;
                        z.LastTouchedBar = bar;
                    }

                    if (c.Close > z.High + breakDistance)
                        z.BreakCloseCount++;
                    else if (c.Close <= z.High)
                        z.BreakCloseCount = 0;

                    if (z.BreakCloseCount >= 2)
                    {
                        ArchiveZone(z, removedBar: bar);
                        ActiveZones.RemoveAt(i);
                        continue;
                    }

                    if (z.Status == ZoneStatus.New)
                    {
                        var moveAway = readyDistance;
                        if (c.Close <= z.Low - moveAway)
                        {
                            z.Status = ZoneStatus.Ready;
                            z.ReadyBar = bar;
                        }
                    }
                }
            }
        }

        private Zone? FindTouchedReadyZone(IMarketCandle c)
        {
            foreach (var z in ActiveZones)
            {
                if (z.Status != ZoneStatus.Ready)
                    continue;

                bool touched = c.High >= z.Low && c.Low <= z.High;
                if (!touched)
                    continue;

                return z;
            }

            return null;
        }

        private static bool IsVwapPullbackOk(ZoneType zoneType, decimal price, decimal vwap)
        {
            if (vwap <= 0m)
                return true;

            if (price > vwap)
                return zoneType == ZoneType.Support;

            if (price < vwap)
                return zoneType == ZoneType.Resistance;

            return true;
        }

        private bool IsMomentumBlocked(IReadOnlyList<OfFeatures>? recentOf, decimal tickSize)
        {
            if (recentOf == null)
                return false;

            int n = Math.Max(0, MomentumProtectionBars);
            if (n <= 0)
                return false;

            if (recentOf.Count < n)
                return false;

            int strongCount = 0;
            for (int i = recentOf.Count - n; i < recentOf.Count; i++)
            {
                var f = recentOf[i];
                if (f == null)
                    continue;

                var body = Math.Abs(f.Close - f.Open);
                var bodyTicks = tickSize > 0m ? body / tickSize : 0m;
                if (bodyTicks >= MomentumProtectionBodyTicksMin)
                    strongCount++;
            }

            return strongCount >= n;
        }
    }

    // Beispiel für eine zukünftige Hilfsklasse (muss nicht jetzt definiert werden)
    /*
    public class PriceLevel
    {
        public decimal Price { get; set; }
        public LevelType Type { get; set; } // Enum { Support, Resistance, HVN, LVN, POC }
        public int Touches { get; set; }
        public DateTime LastTouched { get; set; }
    }
    */
}



