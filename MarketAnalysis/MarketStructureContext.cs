using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Orderflow;
using MyNamespace.Strategies.Models;
using static MyNamespace.Strategies.Goldfluss3_3;

namespace MyNamespace.Strategies.MarketAnalysis
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
        public bool ZoneArchiveIgnoreTypeForMultiTouch { get; set; } = true;

        public List<Zone> ActiveZones { get; } = new List<Zone>();
        public EntrySignal? LastSignal { get; private set; }

        public int WickZoneMinTicks { get; set; } = 3;
        public bool ZigZagIgnoreWicks { get; set; } = true;
        public int ZigZagMinMoveTicks { get; set; } = 10;
        public int ZigZagLeftBars { get; set; } = 2;
        public int ZigZagRightBars { get; set; } = 1;
        public int ZigZagSensitivity { get; set; } = 0;
        public decimal ZoneReadyMoveAwayMultiple { get; set; } = 2.5m;
        public int ZoneReadyDistanceTicks { get; set; } = 4;
        public int ZoneBreakDistanceTicks { get; set; } = 1;
        public int ZoneMergeDistanceTicks { get; set; } = 2;
        public int MomentumProtectionBars { get; set; } = 3;
        public int MomentumProtectionBodyTicksMin { get; set; } = 6;

        public void Reset()
        {
            ActiveZones.Clear();
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
            ReinsertConfirmedZoneThroughMerge(pending, tickSize);
            return true;
        }

        private void ReinsertConfirmedZoneThroughMerge(Zone zone, decimal tickSize)
        {
            if (zone == null)
                return;
            if (!zone.IsConfirmed)
                return;

            for (int i = ActiveZones.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(ActiveZones[i], zone))
                {
                    ActiveZones.RemoveAt(i);
                    break;
                }
            }

            AddOrMergeZone(zone, tickSize);
        }

        public void MarkZoneUsed(int zoneId)
        {
            var z = ActiveZones.FirstOrDefault(x => x.Id == zoneId);
            if (z == null)
                return;

            z.Status = ZoneStatus.Used;
        }

        private void EnqueueRecentCandle(int bar, IMarketCandle candle)
        {
            _recentCandles.Enqueue((bar, candle));
            while (_recentCandles.Count > RecentCandleBuffer)
                _recentCandles.Dequeue();
        }

        private void GetEffectiveZigZagParams(out int left, out int right, out int minMoveTicks)
        {
            int s = Math.Max(0, Math.Min(10, ZigZagSensitivity));
            left = Math.Max(1, ZigZagLeftBars + (s / 3));
            right = Math.Max(1, ZigZagRightBars + (s / 4));
            minMoveTicks = Math.Max(1, (int)Math.Round(ZigZagMinMoveTicks * (1m + (0.15m * s)), MidpointRounding.AwayFromZero));
        }

        private int GetEffectiveWickMinTicks()
        {
            int s = Math.Max(0, Math.Min(10, ZigZagSensitivity));
            return Math.Max(1, (int)Math.Round(WickZoneMinTicks * (1m + (0.10m * s)), MidpointRounding.AwayFromZero));
        }

        private int GetEffectiveZoneMergeDistanceTicks()
        {
            int s = Math.Max(0, Math.Min(10, ZigZagSensitivity));
            return Math.Max(0, ZoneMergeDistanceTicks + (s / 2));
        }

        private void TryUpdateZigZagAndCreateZones(int bar, decimal tickSize, decimal vwap)
        {
            if (_recentCandles.Count == 0)
                return;

            GetEffectiveZigZagParams(out int left, out int right, out int minMoveTicks);
            int rightPendingConfirm = Math.Max(right, 2);
            int n = _recentCandles.Count;
            var items = _recentCandles.ToArray();

            var lastC = items[n - 1].Candle;
            if (lastC != null)
                InvalidatePendingZonesOnClose(bar, lastC, tickSize);

            if (n >= left + 1)
                TryCreatePendingSwingZones(items, n, left, bar, tickSize, vwap);

            if (n >= left + rightPendingConfirm + 1)
            {
                int centerIndexP = n - 1 - rightPendingConfirm;
                if (centerIndexP >= left)
                {
                    var centerP = items[centerIndexP];
                    var centerCP = centerP.Candle;
                    if (centerCP != null)
                    {
                        decimal centerHighP = centerCP.High;
                        decimal centerLowP = centerCP.Low;

                        bool isSwingHighP = true;
                        bool isSwingLowP = true;
                        for (int i = centerIndexP - left; i <= centerIndexP + rightPendingConfirm; i++)
                        {
                            if (i == centerIndexP)
                                continue;

                            var cP = items[i].Candle;
                            if (cP == null)
                                continue;

                            var hiP = cP.High;
                            var loP = cP.Low;
                            if (hiP > centerHighP) isSwingHighP = false;
                            if (loP < centerLowP) isSwingLowP = false;
                            if (!isSwingHighP && !isSwingLowP)
                                break;
                        }

                        int swingBarP = centerP.Bar;
                        if (isSwingHighP && swingBarP != _lastConfirmedSwingHighBar)
                        {
                            if (TryConfirmPendingZoneFromSwing(swingBarP, ZoneType.Resistance, tickSize, vwap))
                            {
                                _lastConfirmedSwingHighBar = swingBarP;
                                _lastConfirmedSwingHigh = centerHighP;
                                _lastConfirmedSwingDir = ZigZagDir.Up;
                                return;
                            }
                        }

                        if (isSwingLowP && swingBarP != _lastConfirmedSwingLowBar)
                        {
                            if (TryConfirmPendingZoneFromSwing(swingBarP, ZoneType.Support, tickSize, vwap))
                            {
                                _lastConfirmedSwingLowBar = swingBarP;
                                _lastConfirmedSwingLow = centerLowP;
                                _lastConfirmedSwingDir = ZigZagDir.Down;
                                return;
                            }
                        }
                    }
                }
            }

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

            var minMove = Math.Max(1, minMoveTicks) * tickSize;
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

        private bool IsStillValidZigZagCandidate(int pivotBar, ZoneType type, decimal tickSize)
        {
            var items = _recentCandles.ToArray();
            if (items.Length == 0)
                return false;

            int pivotIndex = -1;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].Bar == pivotBar)
                {
                    pivotIndex = i;
                    break;
                }
            }

            if (pivotIndex < 0)
                return false;

            var pivotCandle = items[pivotIndex].Candle;
            if (pivotCandle == null)
                return false;

            GetEffectiveZigZagParams(out int left, out int right, out _);
            if (pivotIndex < left)
                return false;

            decimal pivotHigh = pivotCandle.High;
            decimal pivotLow = pivotCandle.Low;

            if (type == ZoneType.Resistance)
            {
                for (int i = pivotIndex - left; i <= Math.Min(items.Length - 1, pivotIndex + right); i++)
                {
                    if (i == pivotIndex)
                        continue;
                    var c = items[i].Candle;
                    if (c == null)
                        continue;
                    if (c.High > pivotHigh)
                        return false;
                }

                return true;
            }

            for (int i = pivotIndex - left; i <= Math.Min(items.Length - 1, pivotIndex + right); i++)
            {
                if (i == pivotIndex)
                    continue;
                var c = items[i].Candle;
                if (c == null)
                    continue;
                if (c.Low < pivotLow)
                    return false;
            }

            return true;
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

                if (!IsStillValidZigZagCandidate(z.PivotBar, z.Type, tickSize))
                {
                    ActiveZones.RemoveAt(i);
                    continue;
                }

                if (z.Type == ZoneType.Support)
                {
                    if (c.Close < z.Low - breakDist)
                    {
                        ActiveZones.RemoveAt(i);
                    }
                }
                else
                {
                    if (c.Close > z.High + breakDist)
                    {
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
                ReinsertConfirmedZoneThroughMerge(pending, tickSize);
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

            var wickMinTicks = GetEffectiveWickMinTicks();

            if (type == ZoneType.Support)
            {
                var minLow = c.Low;
                var bodyLow = Math.Min(c.Open, c.Close);
                var wickSize = bodyLow - minLow;
                var minWickSize = Math.Max(1, wickMinTicks) * tickSize;
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
            var minWickSizeR = Math.Max(1, wickMinTicks) * tickSize;
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

            var ts = tickSize > 0m ? tickSize : 0.25m;
            var mergeDist = Math.Max(0, GetEffectiveZoneMergeDistanceTicks()) * ts;
            var minZoneSize = 1m * ts;

            if (newZone.IsConfirmed)
                ApplyArchivePromotion(newZone, currentBar: newZone.CreatedBar, tickSize: ts);

            if (newZone.IsConfirmed)
            {
                for (int i = ActiveZones.Count - 1; i >= 0; i--)
                {
                    var z = ActiveZones[i];
                    if (z == null)
                        continue;
                    if (z.Type != newZone.Type)
                        continue;
                    if (z.Status == ZoneStatus.Used)
                        continue;

                    if (z.IsConfirmed)
                        continue;

                    if (DoZonesOverlap(z, newZone, mergeDist))
                        ActiveZones.RemoveAt(i);
                }
            }

            if (!newZone.IsConfirmed)
            {
                ActiveZones.Add(newZone);
                return;
            }

            var clusterIndices = FindOverlapCluster(newZone, mergeDist);
            if (clusterIndices.Count == 0)
            {
                ActiveZones.Add(newZone);
                return;
            }

            clusterIndices.Sort();
            var clusterZones = clusterIndices
                .Select(idx => (Index: idx, Zone: ActiveZones[idx]))
                .Where(x => x.Zone != null)
                .ToList();

            bool hasReadyOrTriggered = clusterZones.Any(z => z.Zone.Status == ZoneStatus.Ready || z.Zone.Status == ZoneStatus.Triggered);

            if (hasReadyOrTriggered)
            {
                var winnerInfo = SelectBestZoneInCluster(clusterZones, newZone, currentBar: newZone.CreatedBar);
                if (winnerInfo.Index == -1)
                {
                    for (int k = clusterIndices.Count - 1; k >= 0; k--)
                        ActiveZones.RemoveAt(clusterIndices[k]);

                    ActiveZones.Add(newZone);
                }
                else
                {
                    for (int k = clusterIndices.Count - 1; k >= 0; k--)
                    {
                        int idx = clusterIndices[k];
                        if (idx != winnerInfo.Index)
                            ActiveZones.RemoveAt(idx);
                    }
                }

                return;
            }

            if (TryIntersectNewClusterIntoBestZone(clusterZones, newZone, minZoneSize, out int winnerIdx, out decimal newLow, out decimal newHigh))
            {
                var winner = ActiveZones[winnerIdx];
                if (winner != null)
                {
                    winner.Low = newLow;
                    winner.High = newHigh;
                }

                for (int k = clusterIndices.Count - 1; k >= 0; k--)
                {
                    int idx = clusterIndices[k];
                    if (idx != winnerIdx)
                        ActiveZones.RemoveAt(idx);
                }

                return;
            }

            var bestInfo = SelectBestZoneInCluster(clusterZones, newZone, currentBar: newZone.CreatedBar);
            if (bestInfo.Index == -1)
            {
                for (int k = clusterIndices.Count - 1; k >= 0; k--)
                    ActiveZones.RemoveAt(clusterIndices[k]);

                ActiveZones.Add(newZone);
            }
            else
            {
                for (int k = clusterIndices.Count - 1; k >= 0; k--)
                {
                    int idx = clusterIndices[k];
                    if (idx != bestInfo.Index)
                        ActiveZones.RemoveAt(idx);
                }
            }
        }

        private static bool DoZonesOverlap(Zone z1, Zone z2, decimal mergeDist)
        {
            return (z1.Low <= z2.High + mergeDist) && (z1.High >= z2.Low - mergeDist);
        }

        private List<int> FindOverlapCluster(Zone newZone, decimal mergeDist)
        {
            var cluster = new List<int>();
            var visited = new HashSet<int>();
            var toProcess = new Queue<int>();

            for (int i = 0; i < ActiveZones.Count; i++)
            {
                var z = ActiveZones[i];
                if (z == null || z.Type != newZone.Type || z.Status == ZoneStatus.Used)
                    continue;
                if (!z.IsConfirmed)
                    continue;

                if (DoZonesOverlap(z, newZone, mergeDist))
                    toProcess.Enqueue(i);
            }

            while (toProcess.Count > 0)
            {
                int idx = toProcess.Dequeue();
                if (!visited.Add(idx))
                    continue;

                cluster.Add(idx);

                var zone = ActiveZones[idx];
                if (zone == null)
                    continue;

                for (int i = 0; i < ActiveZones.Count; i++)
                {
                    if (i == idx || visited.Contains(i))
                        continue;
                    var other = ActiveZones[i];
                    if (other == null || other.Type != zone.Type || other.Status == ZoneStatus.Used)
                        continue;
                    if (!other.IsConfirmed)
                        continue;

                    if (DoZonesOverlap(zone, other, mergeDist))
                        toProcess.Enqueue(i);
                }
            }

            return cluster;
        }

        private sealed class ZoneRankInfo
        {
            public int Index { get; init; }
            public Zone Zone { get; init; } = null!;
            public int Rank { get; init; }
        }

        private static ZoneRankInfo SelectBestZoneInCluster(List<(int Index, Zone Zone)> clusterZones, Zone newZone, int currentBar)
        {
            ZoneRankInfo? bestExisting = null;
            foreach (var (idx, zone) in clusterZones)
            {
                if (zone == null)
                    continue;

                int rank = CalculateZoneRank(zone, currentBar);
                var info = new ZoneRankInfo { Index = idx, Zone = zone, Rank = rank };
                if (bestExisting == null || info.Rank > bestExisting.Rank)
                    bestExisting = info;
            }

            int newRank = CalculateZoneRank(newZone, currentBar);
            if (bestExisting == null || newRank > bestExisting.Rank)
                return new ZoneRankInfo { Index = -1, Zone = newZone, Rank = newRank };

            return bestExisting;
        }

        private static int CalculateZoneRank(Zone zone, int currentBar)
        {
            int score = 0;

            if (zone.Status == ZoneStatus.Ready || zone.Status == ZoneStatus.Triggered)
                score += 10000;
            else if (zone.Status == ZoneStatus.New)
                score += 5000;

            score += Math.Max(0, zone.TouchCount) * 1000;
            score += (int)(Math.Max(0, zone.MultiTouchScore) * 100);

            int age = Math.Max(0, currentBar - zone.CreatedBar);
            score += Math.Min(age / 2, 500);

            var size = zone.High - zone.Low;
            if (size > 0m)
            {
                var precision = Math.Min(100m / size, 100m);
                score += (int)precision;
            }

            return score;
        }

        private static bool TryIntersectNewClusterIntoBestZone(
            List<(int Index, Zone Zone)> clusterZones,
            Zone newZone,
            decimal minZoneSize,
            out int winnerIndex,
            out decimal intersectionLow,
            out decimal intersectionHigh)
        {
            winnerIndex = -1;
            intersectionLow = 0m;
            intersectionHigh = 0m;

            var best = clusterZones
                .Where(z => z.Zone != null)
                .OrderByDescending(z => CalculateZoneRank(z.Zone, currentBar: newZone.CreatedBar))
                .FirstOrDefault();

            if (best.Zone == null)
                return false;

            winnerIndex = best.Index;

            decimal low = decimal.MinValue;
            decimal high = decimal.MaxValue;

            foreach (var (_, zone) in clusterZones)
            {
                if (zone == null)
                    continue;
                low = Math.Max(low, zone.Low);
                high = Math.Min(high, zone.High);
            }

            low = Math.Max(low, newZone.Low);
            high = Math.Min(high, newZone.High);

            if (low >= high)
                return false;

            if ((high - low) < minZoneSize)
                return false;

            intersectionLow = low;
            intersectionHigh = high;
            return true;
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
                if (!ZoneArchiveIgnoreTypeForMultiTouch && a.Type != zone.Type)
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
                    if (z.IsConfirmed)
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
                        if (z.IsConfirmed)
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
                        if (z.IsConfirmed)
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



