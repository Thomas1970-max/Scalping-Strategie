using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.MarketAnalysis;
using MyNamespace.Strategies.Models;
using Utils.Common.Logging;
using static MyNamespace.Strategies.Goldfluss3_3;

namespace MyNamespace.Strategies.Orderflow
{
    public sealed class ReversalBouncePatternEvaluatorV2 : IPatternEvaluator
    {
        private enum AllowPath
        {
            None,
            UaToFa,
            MultiFaDefense
        }

        private enum AbsorptionPattern
        {
            None,
            A,
            B,
            C
        }

        private enum SessionType
        {
            None,
            Immediate,
            Retest
        }

        private enum ReversalPhase
        {
            Touch,
            Defense,
            Confirm
        }

        private sealed class ScoreItem
        {
            public string Key { get; init; } = string.Empty;
            public decimal Points { get; init; }
            public string TextDe { get; init; } = string.Empty;
        }

        private sealed class DecisionResult
        {
            public bool Allowed { get; init; }
            public bool Entry { get; init; }
            public AllowPath Path { get; init; }
            public int BaseScore { get; init; }
            public decimal TotalScore { get; init; }
            public decimal Confidence { get; init; }
            public string BlockReasonDe { get; init; } = string.Empty;
            public List<ScoreItem> Items { get; init; } = new();
        }

        private sealed class ZoneTracker
        {
            public int ZoneId;
            public int FirstRealTouchBar;
            public int FirstTouchBar;
            public int LastTouchBar;
            public DateTime FirstTouchTime;
            public bool Confirmed;
            public int ConfirmedBar;
            public bool RetestAttempted;
            public int RetestBar;
            public DateTime? RetestAttemptTime;
            public bool ImmediateAttempted;
            public bool ImmediateDisabled;
            public int FirstOutsideBar;
            public int MaxOutsideExtensionTicks;
            public decimal MaxFavorableExcursionPrice;
            public bool MovedAwaySeen;
            public bool EntryTriggered;
            public int LastStoryLoggedBar;
            public bool StoryPending;
            public bool StoryEmitted;
            public int LastTouchedBar;
            public int StoryExitBar;

            // --- Multi-Bar Session ---
            public bool SessionActive;
            public SessionType SessionKind;
            public int SessionStartBar;
            public int SessionStartChartBar;
            public int SessionLastEvalBar;
            public decimal SessionTouchLow;
            public decimal SessionTouchHigh;
            public decimal SessionTouchPocPrice;
            public bool SessionPressureSeen;
            public ReversalPhase SessionPhase;
            public int SessionPhaseStartBar;
            public int SessionStrongDefenseCount;
            public bool SessionSawAbsorption;
            public bool SessionSawSweep;
            public bool SessionSawMultiFaDefense;
            public bool SessionSawFaAtZone;
            public bool SessionAbsorptionConfirmed;
            public int SessionAbsorptionBar;
            public decimal SessionAbsorptionPocPrice;
            public int SessionLastPatternCBar;
            public bool SessionSawUaToFa;
            public int SessionUaToFaBar;
            public bool SessionLatchUfToFa;
            public int SessionLatchUfToFaBar;
            public bool SessionLatchClose;
            public int SessionLatchCloseBar;
            public int SessionLatchCloseBrokenBar;
            public bool SessionLatchPoc;
            public int SessionLatchPocBar;
            public bool SessionLatchAbsorption;
            public int SessionLatchAbsorptionBar;
            public bool SessionSawExhaustion;
            public int ConsecutiveBadCloses;
            public int SessionMaxPenetrationTicks;
            public decimal SessionBestScore;
            public List<ScoreItem>? SessionBestScoreItems;
            public List<DecisionResult>? SessionDecisionHistory;
            public List<int>? SessionDecisionBars;
            public List<ReversalPhase>? SessionDecisionPhases;
            public string? SessionEndReason;
            public bool SessionTouchWasReversal;

            public decimal LastKnownZoneLow;
            public decimal LastKnownZoneHigh;
            public MarketStructureContext.ZoneType LastKnownZoneType;
            public bool LastKnownZoneConfirmed;
        }

        private readonly OrderDirections _direction;
        private readonly OrderflowPatternType _patternType;
        private readonly ILoggerSource? _loggerSource;
        private readonly int _compressionGapTicks;

        private readonly Dictionary<int, ZoneTracker> _trackersByZoneId = new();

        private int? _lastValidReversalBarIndex;
        private OrderDirections? _lastValidReversalDirection;

        public OrderflowPatternType Type => _patternType;
        public OrderDirections Direction => _direction;

        public ReversalBouncePatternEvaluatorV2(OrderDirections direction, ILoggerSource? loggerSource = null, int? compressionGapTicks = null)
        {
            _direction = direction;
            _patternType = direction == OrderDirections.Buy
                ? OrderflowPatternType.PotentialLongReversalBounce
                : OrderflowPatternType.PotentialShortReversalBounce;

            _loggerSource = loggerSource;
            _compressionGapTicks = compressionGapTicks.HasValue
                ? Math.Max(0, compressionGapTicks.Value)
                : 12;
        }

        private void LogExplainOnce(int bar, int zoneId, string stage, IEnumerable<string> lines)
        {
            try
            {
                if (_loggerSource == null)
                    return;

                var msg = $"[ReversalBounceV2:{stage}] " + string.Join(" | ", lines);
                SmartLogger.Instance.LogIfChanged(
                    category: "ReversalBounceV2",
                    sourceId: $"V2.{_direction}",
                    barIndex: bar,
                    message: msg,
                    signature: SmartLogger.ComposeSignature(
                        ("dir", _direction.ToString()),
                        ("zone", zoneId.ToString()),
                        ("stage", stage),
                        ("bar", bar.ToString())),
                    backendLogAction: s => _loggerSource.LogInfo(s)
                );
            }
            catch
            {
                // ignore logging failures
            }
        }

        private static int RoundTicks(decimal priceDelta, decimal tickSize)
        {
            if (tickSize <= 0m)
                return 0;
            return (int)Math.Round(priceDelta / tickSize, MidpointRounding.AwayFromZero);
        }

        private static decimal ComputeMedian(IReadOnlyList<decimal> values)
        {
            if (values == null || values.Count == 0)
                return 0m;
            var tmp = values.ToList();
            tmp.Sort();
            int n = tmp.Count;
            if ((n & 1) == 1)
                return tmp[n / 2];
            return (tmp[(n / 2) - 1] + tmp[n / 2]) * 0.5m;
        }

        private static decimal GetAdaptiveAbsNetDeltaMin(OfFeaturesHistory history, OvSnapshot curr, int lookbackBars, decimal multiplier)
        {
            if (history == null || curr == null)
                return 0m;

            int want = Math.Max(5, lookbackBars);
            var vals = new List<decimal>(want);
            for (int i = 0; i < Math.Min(history.Count, 600); i++)
            {
                var s = history.GetOfFeatures(i)?.Snapshot;
                if (s == null)
                    continue;
                if (s.Bar >= curr.Bar)
                    continue;
                vals.Add(Math.Abs(s.NetDeltaTotal));
                if (vals.Count >= want)
                    break;
            }

            if (vals.Count == 0)
                return 0m;
            decimal median = ComputeMedian(vals);
            if (median < 1m)
                median = 1m;
            return Math.Max(0m, median * multiplier);
        }

        private static decimal GetAdaptiveVolumeMin(OfFeaturesHistory history, OvSnapshot curr, int lookbackBars, decimal multiplier)
        {
            if (history == null || curr == null)
                return 0m;

            int maxBar = curr.Bar;
            int minBar = Math.Max(0, maxBar - (lookbackBars - 1));
            var vals = new List<decimal>(lookbackBars);
            for (int b = minBar; b <= maxBar; b++)
            {
                OvSnapshot? s = null;
                if (b == curr.Bar)
                    s = curr;
                else if (history.TryGetByBar(b, out var f) && f?.Snapshot != null)
                    s = f.Snapshot;

                if (s == null)
                    continue;

                if (s.Volume > 0m)
                    vals.Add(s.Volume);
            }

            if (vals.Count == 0)
                return 0m;

            decimal median = ComputeMedian(vals);
            if (median < 1m)
                median = 1m;
            return Math.Max(0m, median * multiplier);
        }

        private static decimal GetAdaptivePocVolumeMin(OfFeaturesHistory history, OvSnapshot curr, int lookbackBars, decimal multiplier)
        {
            if (history == null || curr == null)
                return 0m;

            int maxBar = curr.Bar;
            int minBar = Math.Max(0, maxBar - (lookbackBars - 1));
            var vals = new List<decimal>(lookbackBars);
            for (int b = minBar; b <= maxBar; b++)
            {
                OvSnapshot? s = null;
                if (b == curr.Bar)
                    s = curr;
                else if (history.TryGetByBar(b, out var f) && f?.Snapshot != null)
                    s = f.Snapshot;

                if (s == null)
                    continue;

                if (s.PocVolume > 0m)
                    vals.Add(s.PocVolume);
            }

            if (vals.Count == 0)
                return 0m;

            decimal median = ComputeMedian(vals);
            if (median < 1m)
                median = 1m;
            return Math.Max(0m, median * multiplier);
        }

        private static AllowPath DetermineAllowPath(
            OfFeaturesHistory history,
            OvSnapshot curr,
            OvSnapshot? prev,
            MarketStructureContext.Zone zone,
            OrderflowThresholds thresholds,
            decimal tickSize,
            OrderDirections dir,
            out int touchesW,
            out int faAtZoneW,
            out bool progressOk)
        {
            touchesW = 0;
            faAtZoneW = 0;
            progressOk = false;

            bool allowUaToFa = false;
            if (prev != null)
            {
                bool prevUa = IsUnfinishedAuction(prev, thresholds, dir);
                bool currFa = IsFinishedAuction(curr, thresholds, dir);
                bool faAtZone = currFa && IsFinishedAuctionAtZone(curr, zone, tickSize, dir);
                allowUaToFa = prevUa && faAtZone;
            }

            if (allowUaToFa)
                return AllowPath.UaToFa;

            const int W = 5;
            const int ProgressTicksMinMultiFa = 6;
            const int LookaheadBarsForProgress = 5;
            int maxBar = curr.Bar;
            int minBar = Math.Max(0, maxBar - (W - 1));
            int consecutiveTouches = 0;
            int maxConsecutiveTouches = 0;
            int strongFaAtZoneW = 0;

            for (int b = minBar; b <= maxBar; b++)
            {
                OvSnapshot? s = null;
                if (b == curr.Bar)
                    s = curr;
                else if (history.TryGetByBar(b, out var f) && f?.Snapshot != null)
                    s = f.Snapshot;

                if (s == null)
                    continue;

                if (TouchesZone(s, zone))
                {
                    touchesW++;
                    consecutiveTouches++;
                    if (consecutiveTouches > maxConsecutiveTouches)
                        maxConsecutiveTouches = consecutiveTouches;
                }
                else
                {
                    consecutiveTouches = 0;
                }

                bool isFa = IsFinishedAuction(s, thresholds, dir);
                if (isFa && IsFinishedAuctionAtZone(s, zone, tickSize, dir))
                {
                    faAtZoneW++;

                    // Count each FA@Zone as a defense. The quality / follow-through is handled via progressOk.
                    strongFaAtZoneW++;

                    decimal need;
                    if (dir == OrderDirections.Buy)
                        need = zone.High + (ProgressTicksMinMultiFa * tickSize);
                    else
                        need = zone.Low - (ProgressTicksMinMultiFa * tickSize);

                    // Follow-through detection: use excursion (High/Low) and allow a wider window.
                    decimal best = dir == OrderDirections.Buy ? s.High : s.Low;
                    for (int nb = b; nb <= Math.Min(maxBar, b + LookaheadBarsForProgress); nb++)
                    {
                        OvSnapshot? ns = null;
                        if (nb == curr.Bar)
                            ns = curr;
                        else if (history.TryGetByBar(nb, out var nf) && nf?.Snapshot != null)
                            ns = nf.Snapshot;
                        if (ns == null)
                            continue;
                        if (dir == OrderDirections.Buy)
                            best = Math.Max(best, ns.High);
                        else
                            best = Math.Min(best, ns.Low);
                    }

                    if (dir == OrderDirections.Buy)
                    {
                        if (best >= need)
                            progressOk = true;
                    }
                    else
                    {
                        if (best <= need)
                            progressOk = true;
                    }
                }
            }

            bool multiFaDefense = strongFaAtZoneW >= 2 && progressOk;
            if (!multiFaDefense)
                return AllowPath.None;

            if (touchesW >= 4)
                return AllowPath.None;

            if (maxConsecutiveTouches >= 3)
                return AllowPath.None;

            return AllowPath.MultiFaDefense;
        }

        private sealed class ProximityEval
        {
            public decimal Factor { get; init; }
            public string ReasonDe { get; init; } = string.Empty;
        }

        private static ProximityEval EvaluateAbsorptionProximity(OvSnapshot curr, MarketStructureContext.Zone zone, decimal tickSize, OrderDirections dir)
        {
            if (tickSize <= 0m)
                tickSize = 0.25m;

            // Extrem-Reversal: Kontaktbereich ist am Rand (Long: zone.High, Short: zone.Low)
            decimal edge = dir == OrderDirections.Buy ? zone.High : zone.Low;

            if (TouchesZone(curr, zone))
            {
                return new ProximityEval { Factor = 1.0m, ReasonDe = "Zonenkontakt: JA" };
            }

            // Wir werten die Nähe als Minimum aus Close-Nähe und Docht-Nähe.
            // Long: Docht ist Low (wie nah kam der Tiefpunkt an zone.High?)
            // Short: Docht ist High (wie nah kam der Hochpunkt an zone.Low?)
            decimal close = curr.Close;
            decimal wick = dir == OrderDirections.Buy ? curr.Low : curr.High;

            decimal distClose = dir == OrderDirections.Buy ? (close - edge) : (edge - close);
            decimal distWick = dir == OrderDirections.Buy ? (wick - edge) : (edge - wick);

            int distCloseTicks = RoundTicks(Math.Max(0m, distClose), tickSize);
            int distWickTicks = RoundTicks(Math.Max(0m, distWick), tickSize);

            int bestTicks = Math.Min(distCloseTicks, distWickTicks);
            string bestFrom = distWickTicks <= distCloseTicks ? "Docht" : "Close";

            if (bestTicks <= 2)
                return new ProximityEval { Factor = 1.0m, ReasonDe = $"Zonennähe: hoch (über {bestFrom}, Abstand {bestTicks} Ticks)" };
            if (bestTicks <= 6)
                return new ProximityEval { Factor = 0.5m, ReasonDe = $"Zonennähe: mittel (über {bestFrom}, Abstand {bestTicks} Ticks)" };
            return new ProximityEval { Factor = 0.0m, ReasonDe = $"Zonennähe: niedrig (Abstand {bestTicks} Ticks)" };
        }

        private sealed class SweepEval
        {
            public bool IsSweep { get; init; }
            public decimal Points { get; init; }
            public decimal PenetrationRatio { get; init; }
            public int PenetrationTicks { get; init; }
            public int ReclaimTicks { get; init; }
            public string TextDe { get; init; } = string.Empty;
        }

        private static SweepEval EvaluateSweepPenetration(
            OfFeaturesHistory history,
            OvSnapshot curr,
            MarketStructureContext.Zone zone,
            decimal tickSize,
            OrderDirections dir)
        {
            if (tickSize <= 0m)
                tickSize = 0.25m;

            const int AdaptiveVolLookback = 30;
            const decimal VolMedianMultiplier = 0.8m;
            decimal volMin = GetAdaptiveVolumeMin(history, curr, AdaptiveVolLookback, VolMedianMultiplier);
            bool volumeOk = curr.Volume >= volMin;

            int zoneHeightTicks = Math.Max(1, RoundTicks(Math.Abs(zone.High - zone.Low), tickSize));

            // Penetration: through the OUTER edge (support -> below zone.Low; resistance -> above zone.High)
            int penetrationTicks;
            if (dir == OrderDirections.Buy)
                penetrationTicks = Math.Max(0, RoundTicks(Math.Max(0m, zone.Low - curr.Low), tickSize));
            else
                penetrationTicks = Math.Max(0, RoundTicks(Math.Max(0m, curr.High - zone.High), tickSize));

            decimal ratio = penetrationTicks / (decimal)zoneHeightTicks;

            // Reclaim: close back beyond the outer edge by at least 1 tick; strength measured in ticks.
            int reclaimTicks;
            if (dir == OrderDirections.Buy)
                reclaimTicks = RoundTicks(Math.Max(0m, curr.Close - zone.Low), tickSize);
            else
                reclaimTicks = RoundTicks(Math.Max(0m, zone.High - curr.Close), tickSize);

            // Sweep corridor (soft): avoid micro-noise and avoid too-deep breaks.
            // Start values chosen for robustness; can be tuned later.
            const decimal MinRatio = 0.10m;
            const decimal MaxRatio = 0.40m;

            bool inCorridor = ratio >= MinRatio && ratio <= MaxRatio;
            bool reclaimed = reclaimTicks >= 1;
            bool isSweep = penetrationTicks > 0 && inCorridor && reclaimed && volumeOk;

            decimal points = 0m;
            if (isSweep)
            {
                // Soft score: stronger reclaim -> more points
                points = reclaimTicks >= 2 ? 2m : 1m;
            }

            string text;
            if (penetrationTicks <= 0)
            {
                text = "Stop-Run: kein Stop-Run (kein Durchstich außerhalb der Zone) -> +0";
            }
            else if (!inCorridor)
            {
                text = $"Stop-Run: kein Stop-Run (Durchstich {penetrationTicks} Ticks ist nicht passend zur typischen Stop-Run-Größe) -> +0";
            }
            else if (!reclaimed)
            {
                text = $"Stop-Run: kein Stop-Run (Durchstich ok, aber keine Rückholung; Rückholung={reclaimTicks} Ticks) -> +0";
            }
            else if (!volumeOk)
            {
                text = $"Stop-Run: kein Stop-Run (Volumen zu niedrig; Vol={curr.Volume:0}, Min~{volMin:0}) -> +0";
            }
            else
            {
                text = $"Stop-Run: Stop-Run erkannt (Durchstich {penetrationTicks} Ticks, Rückholung={reclaimTicks} Ticks) -> +{points:0.0}";
            }

            return new SweepEval
            {
                IsSweep = isSweep,
                Points = points,
                PenetrationRatio = ratio,
                PenetrationTicks = penetrationTicks,
                ReclaimTicks = reclaimTicks,
                TextDe = text
            };
        }

        private static int CountStrongDefenseSignals(ZoneTracker tracker)
        {
            int c = 0;
            if (tracker.SessionSawAbsorption) c++;
            if (tracker.SessionSawSweep) c++;
            if (tracker.SessionSawUaToFa) c++;
            if (tracker.SessionSawMultiFaDefense) c++;
            return c;
        }

        private DecisionResult EvaluateDefenseDecision(
            OvSnapshot curr,
            OvSnapshot? prev,
            OfFeaturesHistory history,
            MarketStructureContext.Zone zone,
            OrderflowThresholds thresholds,
            decimal tickSize,
            OrderDirections dir,
            ZoneTracker tracker)
        {
            const decimal DefenseThreshold = 4m;
            const int AdaptiveLookback = 30;
            const decimal AbsNetDeltaMedianMultiplier = 1.0m;
            decimal absNetDeltaMin = GetAdaptiveAbsNetDeltaMin(history, curr, AdaptiveLookback, AbsNetDeltaMedianMultiplier);

            var items = new List<ScoreItem>(16);
            decimal score = 0m;

            bool faAtZone = IsFinishedAuction(curr, thresholds, dir) && IsFinishedAuctionAtZone(curr, zone, tickSize, dir);
            if (faAtZone)
                tracker.SessionSawFaAtZone = true;
            items.Add(new ScoreItem { Key = "FA", Points = 0m, TextDe = faAtZone ? "Finished Auction an Zone: JA" : "Finished Auction an Zone: NEIN" });

            bool absorption = false;
            AbsorptionPattern absorptionPattern = AbsorptionPattern.None;
            if (prev != null)
            {
                OvSnapshot? prevPrev = null;
                if (history.TryGetByBar(curr.Bar - 2, out var prevPrevF) && prevPrevF?.Snapshot != null)
                    prevPrev = prevPrevF.Snapshot;
                absorptionPattern = DetectAbsorptionPattern(prevPrev, prev, curr, zone, tickSize, dir, absNetDeltaMin);
                absorption = absorptionPattern != AbsorptionPattern.None;
            }
            var proxEval = absorption
                ? EvaluateAbsorptionProximity(curr, zone, tickSize, dir)
                : new ProximityEval { Factor = 0m, ReasonDe = "Zonennähe: n/v" };
            decimal absorptionPts = absorption ? (2m * proxEval.Factor) : 0m;
            score += absorptionPts;
            items.Add(new ScoreItem { Key = "Absorption", Points = absorptionPts, TextDe = absorption ? $"Absorption({absorptionPattern}): JA, {proxEval.ReasonDe} -> +{absorptionPts:0.0}" : "Absorption: NEIN -> +0" });
            if (absorption)
                tracker.SessionSawAbsorption = true;

            var sweepEval = EvaluateSweepPenetration(history, curr, zone, tickSize, dir);
            decimal sweepPts = sweepEval.Points;
            score += sweepPts;
            items.Add(new ScoreItem { Key = "Sweep", Points = sweepPts, TextDe = sweepEval.TextDe });
            if (sweepEval.IsSweep)
                tracker.SessionSawSweep = true;

            if (prev != null)
            {
                bool prevUa = IsUnfinishedAuction(prev, thresholds, dir);
                if (prevUa && faAtZone)
                {
                    tracker.SessionSawUaToFa = true;
                    if (tracker.SessionUaToFaBar < 0)
                        tracker.SessionUaToFaBar = curr.Bar;
                }
            }
            decimal uaToFaPts = tracker.SessionSawUaToFa ? 1m : 0m;
            score += uaToFaPts;
            items.Add(new ScoreItem { Key = "UA→FA", Points = uaToFaPts, TextDe = tracker.SessionSawUaToFa ? "UA→FA: JA -> +1" : "UA→FA: NEIN -> +0" });

            int touchesW;
            int faAtZoneW;
            bool progressOk;
            var pathNow = DetermineAllowPath(history, curr, prev, zone, thresholds, tickSize, dir, out touchesW, out faAtZoneW, out progressOk);
            bool multiFa = pathNow == AllowPath.MultiFaDefense;
            decimal multiFaPts = (multiFa && !tracker.SessionSawUaToFa) ? 1m : 0m;
            score += multiFaPts;
            items.Add(new ScoreItem { Key = "MultiFA", Points = multiFaPts, TextDe = multiFa ? "Multi-FA-Verteidigung: JA -> +1" : "Multi-FA-Verteidigung: NEIN -> +0" });
            if (multiFaPts > 0m)
                tracker.SessionSawMultiFaDefense = true;

            if (!tracker.SessionPressureSeen)
            {
                const int AdaptiveVolLookback = 30;
                decimal volMedian = GetAdaptiveVolumeMin(history, curr, AdaptiveVolLookback, multiplier: 1.0m);
                bool lowVol = volMedian > 0m && curr.Volume > 0m && curr.Volume < (volMedian * 0.85m);
                decimal lowVolPts = lowVol ? 1m : 0m;
                score += lowVolPts;
                items.Add(new ScoreItem { Key = "WenigGegenwehr", Points = lowVolPts, TextDe = lowVol ? "Wenig Gegenwehr: Volumen unter Session-Median (adaptiv) -> +1" : "Wenig Gegenwehr: NEIN -> +0" });

                bool exhaustionHere = dir == OrderDirections.Buy
                    ? (curr.NetDeltaTotal > -absNetDeltaMin)
                    : (curr.NetDeltaTotal < absNetDeltaMin);
                if (exhaustionHere)
                    tracker.SessionSawExhaustion = true;
                decimal exhPts = tracker.SessionSawExhaustion ? 1m : 0m;
                score += exhPts;
                items.Add(new ScoreItem { Key = "Exhaustion", Points = exhPts, TextDe = tracker.SessionSawExhaustion ? "Exhaustion (Latch): JA -> +1" : "Exhaustion (Latch): NEIN -> +0" });
            }
            else
            {
                items.Add(new ScoreItem { Key = "WenigGegenwehr", Points = 0m, TextDe = "Wenig Gegenwehr: n/v (Druck vorhanden)" });
                items.Add(new ScoreItem { Key = "Exhaustion", Points = 0m, TextDe = "Exhaustion: n/v (Druck vorhanden)" });
            }

            int strongCountNow = CountStrongDefenseSignals(tracker);
            tracker.SessionStrongDefenseCount = strongCountNow;
            items.Add(new ScoreItem { Key = "Defense", Points = score, TextDe = $"Verteidigung-Score: {score:0.0} (min {DefenseThreshold:0.0}); Strong={strongCountNow}/2" });

            bool allowed = score >= DefenseThreshold && strongCountNow >= 2;
            return new DecisionResult
            {
                Allowed = allowed,
                Entry = false,
                Path = tracker.SessionSawUaToFa ? AllowPath.UaToFa : (tracker.SessionSawMultiFaDefense ? AllowPath.MultiFaDefense : AllowPath.None),
                BaseScore = 0,
                TotalScore = score,
                Confidence = 0m,
                BlockReasonDe = allowed ? string.Empty : "Verteidigung noch nicht bestätigt.",
                Items = items
            };
        }

        private DecisionResult EvaluateConfirmDecision(
            OvSnapshot curr,
            OvSnapshot? prev,
            OfFeaturesHistory history,
            MarketStructureContext.Zone zone,
            OrderflowThresholds thresholds,
            decimal tickSize,
            OrderDirections dir,
            ZoneTracker tracker)
        {
            const decimal EntryThreshold = 6m;
            const int AdaptiveLookback = 30;
            const decimal AbsNetDeltaMedianMultiplier = 1.0m;
            decimal absNetDeltaMin = GetAdaptiveAbsNetDeltaMin(history, curr, AdaptiveLookback, AbsNetDeltaMedianMultiplier);
            int pocShiftMinTicks = Math.Max(0, thresholds.PocShiftMinTicks);

            var items = new List<ScoreItem>(16);
            decimal score = 0m;

            bool aggressiveEntry;
            if (dir == OrderDirections.Buy)
                aggressiveEntry = (curr.StackedBuyImbCount > 0 || curr.NetDeltaTotal > absNetDeltaMin) && curr.Close > curr.Open;
            else
                aggressiveEntry = (curr.StackedSellImbCount > 0 || curr.NetDeltaTotal < -absNetDeltaMin) && curr.Close < curr.Open;
            decimal aggrPts = aggressiveEntry ? 3m : 0m;
            score += aggrPts;
            items.Add(new ScoreItem { Key = "Aggression", Points = aggrPts, TextDe = aggressiveEntry ? "Aggression: JA -> +3" : "Aggression: NEIN -> +0" });

            decimal confirmPts = 0m;
            if (dir == OrderDirections.Buy)
            {
                int awayTicks = RoundTicks(Math.Max(0m, (curr.Close - zone.High)), tickSize);
                confirmPts = awayTicks >= 2 ? 2m : (awayTicks >= 1 ? 1m : 0m);
                bool intentOk = awayTicks <= 0 || curr.PocDelta >= 0m;
                if (!intentOk)
                    confirmPts = 0m;
                items.Add(new ScoreItem { Key = "Bestätigung", Points = confirmPts, TextDe = intentOk ? $"Bestätigung (Long): Close {awayTicks} Ticks über Zone -> +{confirmPts:0.0}" : $"Bestätigung (Long): Close über Zone, aber Verkaufsdruck (POCΔ {curr.PocDelta:+0;-0;0}) -> +0" });
            }
            else
            {
                int awayTicks = RoundTicks(Math.Max(0m, (zone.Low - curr.Close)), tickSize);
                confirmPts = awayTicks >= 2 ? 2m : (awayTicks >= 1 ? 1m : 0m);
                bool intentOk = awayTicks <= 0 || curr.PocDelta <= 0m;
                if (!intentOk)
                    confirmPts = 0m;
                items.Add(new ScoreItem { Key = "Bestätigung", Points = confirmPts, TextDe = intentOk ? $"Bestätigung (Short): Close {awayTicks} Ticks unter Zone -> +{confirmPts:0.0}" : $"Bestätigung (Short): Close unter Zone, aber Kaufdruck (POCΔ {curr.PocDelta:+0;-0;0}) -> +0" });
            }
            score += confirmPts;

            decimal pocPts = 0m;
            if (tracker.SessionAbsorptionConfirmed)
            {
                decimal shift = curr.CandlePocPrice - tracker.SessionAbsorptionPocPrice;
                bool dirOk = dir == OrderDirections.Buy ? shift > 0m : shift < 0m;
                int shiftTicks = RoundTicks(Math.Abs(shift), tickSize);
                bool magOk = shiftTicks >= pocShiftMinTicks;
                pocPts = (dirOk && magOk) ? 2m : 0m;
                score += pocPts;
                items.Add(new ScoreItem { Key = "PocShift", Points = pocPts, TextDe = (dirOk && magOk) ? $"POC-Shift vs AbsorptionPOC: OK ({shiftTicks} Ticks) -> +2" : $"POC-Shift vs AbsorptionPOC: nicht OK (Dir={dirOk}, Mag={magOk}) -> +0" });
            }
            else
            {
                items.Add(new ScoreItem { Key = "PocShift", Points = 0m, TextDe = "POC-Shift: n/v (keine AbsorptionPOC)" });
            }

            decimal deltaFlipPts = 0m;
            if (aggressiveEntry)
            {
                bool deltaFlipRaw = HasDeltaFlipWithinWindow(history, curr, windowBars: 3, dir);
                deltaFlipPts = deltaFlipRaw ? 1m : 0m;
                score += deltaFlipPts;
                items.Add(new ScoreItem { Key = "DeltaFlip", Points = deltaFlipPts, TextDe = deltaFlipRaw ? "Delta-Flip: JA -> +1" : "Delta-Flip: NEIN -> +0" });
            }
            else
            {
                items.Add(new ScoreItem { Key = "DeltaFlip", Points = 0m, TextDe = "Delta-Flip: n/v (keine Aggression)" });
            }

            bool entry = aggressiveEntry && score >= EntryThreshold;
            decimal confidence = 0.40m + Math.Min(0.60m, score * 0.08m);
            if (confidence > 1m) confidence = 1m;
            items.Add(new ScoreItem { Key = "Total", Points = score, TextDe = $"Confirm-Score: {score:0.0} (Schwelle {EntryThreshold:0.0})" });

            return new DecisionResult
            {
                Allowed = tracker.SessionAbsorptionConfirmed,
                Entry = entry,
                Path = tracker.SessionSawUaToFa ? AllowPath.UaToFa : AllowPath.MultiFaDefense,
                BaseScore = 0,
                TotalScore = score,
                Confidence = entry ? confidence : 0m,
                BlockReasonDe = entry ? string.Empty : (!aggressiveEntry ? "Warte auf Aggression in Reversal-Richtung." : "Score zu niedrig."),
                Items = items
            };
        }

        private void LogExplainMultilineOnce(int bar, int zoneId, string stage, IEnumerable<string> lines)
        {
            try
            {
                if (_loggerSource == null)
                    return;

                var msg = $"[ReversalBounceV2:{stage}] " + string.Join(Environment.NewLine, lines);
                SmartLogger.Instance.LogIfChanged(
                    category: "ReversalBounceV2",
                    sourceId: $"V2.{_direction}",
                    barIndex: bar,
                    message: msg,
                    signature: SmartLogger.ComposeSignature(
                        ("dir", _direction.ToString()),
                        ("zone", zoneId.ToString()),
                        ("stage", stage),
                        ("bar", bar.ToString())),
                    backendLogAction: s => _loggerSource.LogInfo(s)
                );
            }
            catch
            {
                // ignore logging failures
            }
        }

        public PatternEvaluationResult Evaluate(
            OvSnapshot currentSnapshot,
            OfFeatures features,
            OfFeaturesHistory history,
            OrderflowThresholds thresholds,
            MarketRegime currentVolatilityRegime,
            MarketBiasV2 currentDirectionalBias,
            MarketStateV2 currentMarketState,
            MarketStructureContext currentMarketStructureContext)
        {
            if (currentMarketStructureContext == null)
                return PatternEvaluationResult.NotDetected(Type, "MarketStructureContext is null");

            if (currentSnapshot == null)
                return PatternEvaluationResult.NotDetected(Type, "currentSnapshot is null");

            if (history == null)
                return PatternEvaluationResult.NotDetected(Type, "history is null");

            if (thresholds == null)
                return PatternEvaluationResult.NotDetected(Type, "thresholds is null");

            decimal tickSize = thresholds.TickSizeDecimal ?? 0.25m;
            if (tickSize <= 0m) tickSize = 0.25m;

            const int MinBarsBetweenFirstTouchAndRetest = 2;
            const int MaxBarsAfterFirstTouch = 5000;
            const int ZoneTouchCooldownBars = 1;
            const int ConsumeAwayDistanceTicks = 16;
            const int ImmediateResponseTimeBarsMax = 2;
            const int ImmediateMaxOutsideExtensionTicks = 4;
            const int RetestMinAwayCloses = 2;
            const int RetestWaitMinutesMax = 60;
            const int MaxSessionBars = 5;
            const int MaxConsecutiveBadCloses = 2;
            const int MaxSessionPenetrationTicks = 8;
            int compressionGapTicks = _compressionGapTicks;

            var zones = currentMarketStructureContext.ActiveZones;
            if (zones == null || zones.Count == 0)
                return PatternEvaluationResult.NotDetected(Type, "ZoneGate: no zones");

            if (_trackersByZoneId.Count > 0)
            {
                List<int>? toRemove = null;
                foreach (var kv in _trackersByZoneId)
                {
                    var trackerZoneId = kv.Key;
                    var trackedTracker = kv.Value;
                    MarketStructureContext.Zone? trackedZone = null;
                    foreach (var z in zones)
                    {
                        if (z != null && z.Id == trackerZoneId)
                        {
                            trackedZone = z;
                            break;
                        }
                    }

                    if (trackedZone == null)
                    {
                        (toRemove ??= new List<int>()).Add(trackerZoneId);
                        continue;
                    }

                    if (!trackedTracker.Confirmed && trackedZone.Status != MarketStructureContext.ZoneStatus.Used)
                    {
                        bool movedAwayNow = HasMovedAwayFromZone(currentSnapshot, trackedZone, tickSize);
                        if (movedAwayNow)
                            trackedTracker.MovedAwaySeen = true;
                    }

                    if (!trackedTracker.RetestAttempted)
                        continue;

                    var away = ConsumeAwayDistanceTicks * tickSize;
                    if (currentSnapshot.Close >= trackedZone.High + away || currentSnapshot.Close <= trackedZone.Low - away)
                    {
                        // Do not delete zones while a session is still active; it would suppress the session's chance to reach GO.
                        if (!trackedTracker.SessionActive || trackedTracker.EntryTriggered)
                        {
                            LogExplainOnce(
                                currentSnapshot.Bar,
                                trackerZoneId,
                                stage: "Consume.Expiry.AwayCleanup",
                                lines: new[]
                                {
                                    $"Dir={_direction}",
                                    $"Zone={trackerZoneId}",
                                    $"Bounds=[{trackedZone.Low:F2}..{trackedZone.High:F2}]({trackedZone.Type})",
                                    $"Close={currentSnapshot.Close:F2}",
                                    $"AwayTicks={ConsumeAwayDistanceTicks}",
                                    $"Reason: RetestAttempted und Preis ist weit genug weg → ConsumeZoneOnExpiry."
                                });

                            // Always print a full protocol when a valid session has started and we end/consume the zone.
                            try
                            {
                                var lastDec = trackedTracker.SessionDecisionHistory != null && trackedTracker.SessionDecisionHistory.Count > 0
                                    ? trackedTracker.SessionDecisionHistory[trackedTracker.SessionDecisionHistory.Count - 1]
                                    : null;

                                if (trackedTracker.SessionStartBar >= 0 && trackedTracker.SessionDecisionHistory != null && trackedTracker.SessionDecisionHistory.Count > 0)
                                {
                                    LogSessionProtocol(
                                        trackedTracker,
                                        trackedZone,
                                        history,
                                        currentSnapshot,
                                        thresholds,
                                        tickSize,
                                        MaxSessionBars,
                                        MaxConsecutiveBadCloses,
                                        MaxSessionPenetrationTicks,
                                        outcome: "VERFALL – Zone verbraucht (AwayCleanup)",
                                        finalDecision: lastDec);
                                }
                            }
                            catch { }

                            currentMarketStructureContext.ConsumeZoneOnExpiry(trackerZoneId);
                            (toRemove ??= new List<int>()).Add(trackerZoneId);
                        }
                        else
                        {
                            LogExplainOnce(
                                currentSnapshot.Bar,
                                trackerZoneId,
                                stage: "Consume.Skip.AwayCleanup.SessionActive",
                                lines: new[]
                                {
                                    $"Dir={_direction}",
                                    $"Zone={trackerZoneId}",
                                    $"Bounds=[{trackedZone.Low:F2}..{trackedZone.High:F2}]({trackedZone.Type})",
                                    $"Close={currentSnapshot.Close:F2}",
                                    $"AwayTicks={ConsumeAwayDistanceTicks}",
                                    $"Reason: SessionActive=true → AwayCleanup wird übersprungen, damit die Session weiter geprüft werden kann."
                                });
                        }
                    }
                }

                if (toRemove != null)
                {
                    for (int i = 0; i < toRemove.Count; i++)
                        _trackersByZoneId.Remove(toRemove[i]);
                }
            }

            var prevClosed = GetPreviousClosedSnapshot(history, currentSnapshot.Bar, maxLookback: 12);

            // ============================================================
            // RETRO-SEED (PENDING ZONE): Zone entsteht in dieser Bar, Touch-Bar war ggf. schon in Bar-1/Bar-2.
            // ============================================================
            try
            {
                // Nur wenn keine aktive Session läuft (harte Regel: max 1 Session).
                bool anyActiveSession = false;
                foreach (var kv in _trackersByZoneId)
                {
                    if (kv.Value != null && kv.Value.SessionActive)
                    {
                        anyActiveSession = true;
                        break;
                    }
                }

                if (!anyActiveSession)
                {
                    int ctxBar = currentMarketStructureContext.CurrentBar;
                    int ctxZoneCreationBar = currentMarketStructureContext.CurrentZoneCreationBar;
                    OvSnapshot? snapBarMinus1 = GetPreviousClosedSnapshotByOffset(history, currentSnapshot.Bar, offset: 1, maxLookback: 30);
                    OvSnapshot? snapBarMinus2 = GetPreviousClosedSnapshotByOffset(history, currentSnapshot.Bar, offset: 2, maxLookback: 60);

                    MarketStructureContext.Zone? retroZone = null;
                    OvSnapshot? retroTouchSnap = null;
                    OvSnapshot? retroReversalSnap = null;

                    foreach (var z in zones)
                    {
                        if (z == null)
                            continue;
                        if (z.IsConfirmed)
                            continue;
                        if (z.Status != MarketStructureContext.ZoneStatus.New && z.Status != MarketStructureContext.ZoneStatus.Ready)
                            continue;
                        bool dirOk = (_direction == OrderDirections.Buy && z.Type == MarketStructureContext.ZoneType.Support)
                                     || (_direction == OrderDirections.Sell && z.Type == MarketStructureContext.ZoneType.Resistance);
                        if (!dirOk)
                            continue;

                        if (z.CreatedBar != ctxZoneCreationBar)
                        {
                            LogExplainOnce(
                                currentSnapshot.Bar,
                                z.Id,
                                stage: "Retest.RetroSeed.Skip.CreatedBarMismatch",
                                lines: new[]
                                {
                                    $"Dir={_direction}",
                                    $"Zone={z.Id}",
                                    $"ZoneCreatedBar(Tick900)={z.CreatedBar}",
                                    $"CtxCurrentBar(RangeOrOther)={ctxBar}",
                                    $"CtxZoneCreationBar(Tick900)={ctxZoneCreationBar}",
                                    $"CurrentSnapshotBarIdx(Range)={currentSnapshot.Bar} ChartBar={currentSnapshot.ChartBarNumber}",
                                    $"Rule: Retro-Seed nur wenn ZoneCreatedBar == CtxZoneCreationBar (Tick900-Kontext)"
                                });
                            continue;
                        }

                        // Touch-Bar rückwirkend finden (bar-1 bevorzugt).
                        OvSnapshot? touchCandidate = null;
                        if (snapBarMinus1 != null && TouchesZone(snapBarMinus1, z))
                            touchCandidate = snapBarMinus1;
                        else if (snapBarMinus2 != null && TouchesZone(snapBarMinus2, z))
                            touchCandidate = snapBarMinus2;

                        if (touchCandidate == null)
                            continue;

                        retroZone = z;
                        retroTouchSnap = touchCandidate;
                        retroReversalSnap = touchCandidate == snapBarMinus2 ? snapBarMinus1 : null;
                        break;
                    }

                    if (retroZone != null && retroTouchSnap != null)
                    {
                        var retroTracker = GetOrCreateTracker(retroZone.Id, currentSnapshot.Bar);
                        if (!retroTracker.EntryTriggered && !retroTracker.SessionActive)
                        {
                            if (retroTracker.FirstTouchTime == DateTime.MinValue)
                                retroTracker.FirstTouchTime = retroTouchSnap.Time;
                            if (retroTracker.FirstTouchBar > retroTouchSnap.Bar)
                                retroTracker.FirstTouchBar = retroTouchSnap.Bar;

                            retroTracker.MovedAwaySeen = true;
                            retroTracker.ImmediateDisabled = true;
                            retroTracker.ImmediateAttempted = true;

                            retroTracker.RetestAttempted = true;
                            retroTracker.RetestBar = retroTouchSnap.Bar;
                            retroTracker.RetestAttemptTime = retroTouchSnap.Time;

                            LogExplainOnce(
                                currentSnapshot.Bar,
                                retroZone.Id,
                                stage: "Retest.RetroSeed.Start",
                                lines: new[]
                                {
                                    $"Dir={_direction}",
                                    $"Zone={retroZone.Id}",
                                    $"State=PENDING (CreatedBar={retroZone.CreatedBar})",
                                    $"Bounds=[{retroZone.Low:F2}..{retroZone.High:F2}]",
                                    $"CurrBarIdx={currentSnapshot.Bar} ChartBar={currentSnapshot.ChartBarNumber} Time={currentSnapshot.Time:O}",
                                    $"RetroTouchBarIdx={retroTouchSnap.Bar} ChartBar={retroTouchSnap.ChartBarNumber} Time={retroTouchSnap.Time:O}"
                                });

                            StartSession(retroTracker, SessionType.Retest, retroTouchSnap);

                            // Falls Umkehrbar bereits geschlossen vorhanden (Touch war bar-2): historisch prüfen.
                            if (retroReversalSnap != null)
                            {
                                var out1 = EvaluateActiveSession(
                                    retroTracker, retroReversalSnap, history, retroZone, thresholds, tickSize,
                                    MaxSessionBars, MaxConsecutiveBadCloses, MaxSessionPenetrationTicks,
                                    out var dec1);

                                if (out1 == SessionEvalOutcome.EntryGo && dec1 != null)
                                {
                                    bool stillNear = TouchesZone(currentSnapshot, retroZone) || IsRejectWickAtZone(currentSnapshot, retroZone, tickSize, _direction);
                                    if (!stillNear)
                                    {
                                        retroTracker.SessionActive = false;
                                        retroTracker.SessionEndReason = "Retro-GO in bar-1, aber aktueller Bar nicht mehr zonennah → kein Entry.";
                                        return PatternEvaluationResult.NotDetected(Type, $"Zone {retroZone.Id}: retro signal missed (price moved away)");
                                    }

                                    LogSessionProtocol(
                                        retroTracker,
                                        retroZone,
                                        history,
                                        currentSnapshot,
                                        thresholds,
                                        tickSize,
                                        MaxSessionBars,
                                        MaxConsecutiveBadCloses,
                                        MaxSessionPenetrationTicks,
                                        "GO – Entry ausgelöst (Retro: Signalbar war bar-1)",
                                        dec1);

                                    var reasons = new List<string>
                                    {
                                        $"ZoneRetestRetro id={retroZone.Id}",
                                        $"Path={(dec1.Path == AllowPath.UaToFa ? "UAtoFA" : "MultiFA")}",
                                        $"Score={dec1.TotalScore:0.0}",
                                        $"Confidence={dec1.Confidence:0.00}"
                                    };

                                    retroTracker.EntryTriggered = true;
                                    _lastValidReversalBarIndex = currentSnapshot.Bar;
                                    _lastValidReversalDirection = _direction;

                                    currentMarketStructureContext.ConsumeZoneOnEntry(retroZone.Id);
                                    _trackersByZoneId.Remove(retroZone.Id);

                                    if (_loggerSource != null)
                                        LoggerHelper.LogInfo(_loggerSource,
                                            $"[ReversalBounceV2] DETECTED (RetestRetro): zone={retroZone.Id} dir={_direction} bar={currentSnapshot.Bar} score={dec1.TotalScore:0.0} conf={dec1.Confidence:0.00}");

                                    return PatternEvaluationResult.Detected(Type, dec1.Confidence, reasons,
                                        new Dictionary<string, object>(), new List<EvaluatedConditionDetail>(), new List<EvaluatedConditionDetail>());
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // --- Candidate Selection ---
            // Harte Regel: maximal eine aktive Session insgesamt.
            // Solange irgendeine Session aktiv ist, wird ausschließlich diese Session weiter evaluiert.
            // Touches anderer Zonen starten keine neue Session und dürfen die aktive Session nicht verdrängen.
            MarketStructureContext.Zone? candidateZone = null;
            bool candidateFromTouch = false;

            ZoneTracker? activeSessionTracker = null;
            foreach (var kv in _trackersByZoneId)
            {
                var t = kv.Value;
                if (!t.SessionActive)
                    continue;

                if (activeSessionTracker == null)
                {
                    activeSessionTracker = t;
                    continue;
                }

                if (t.SessionStartBar >= 0 && (activeSessionTracker.SessionStartBar < 0 || t.SessionStartBar < activeSessionTracker.SessionStartBar))
                    activeSessionTracker = t;
            }

            if (activeSessionTracker != null)
            {
                int activeZoneId = activeSessionTracker.ZoneId;
                MarketStructureContext.Zone? foundZoneAnyStatus = null;
                MarketStructureContext.Zone? foundLiveEligible = null;

                foreach (var z in zones)
                {
                    if (z == null || z.Id != activeZoneId)
                        continue;

                    foundZoneAnyStatus = z;

                    // For continuing an already active session we must not require New/Ready.
                    // A zone can legitimately be Triggered while the session is still evaluating.
                    if (z.Status != MarketStructureContext.ZoneStatus.Used)
                    {
                        bool dirOk = (_direction == OrderDirections.Buy && z.Type == MarketStructureContext.ZoneType.Support)
                                     || (_direction == OrderDirections.Sell && z.Type == MarketStructureContext.ZoneType.Resistance);
                        if (dirOk)
                            foundLiveEligible = z;
                    }
                }

                if (foundLiveEligible != null)
                {
                    candidateZone = foundLiveEligible;
                    candidateFromTouch = false;
                }
                else
                {
                    if (!activeSessionTracker.LastKnownZoneConfirmed)
                    {
                        try
                        {
                            LogExplainOnce(
                                currentSnapshot.Bar,
                                activeZoneId,
                                stage: "Session.Abort.PendingZoneGone",
                                lines: new[]
                                {
                                    $"Dir={_direction}",
                                    $"Zone={activeZoneId}",
                                    $"Reason: Pending-Session und Zone ist nicht mehr live/eligible (missing oder Used) → Session wird sofort abgebrochen.",
                                    $"PinnedBounds=[{activeSessionTracker.LastKnownZoneLow:F2}..{activeSessionTracker.LastKnownZoneHigh:F2}] Type={activeSessionTracker.LastKnownZoneType} Confirmed={activeSessionTracker.LastKnownZoneConfirmed}",
                                    $"zones.Count={zones.Count}"
                                });
                        }
                        catch { }

                        activeSessionTracker.SessionActive = false;
                        activeSessionTracker.SessionEndReason = "Pending zone discarded/used/missing -> abort";
                        _trackersByZoneId.Remove(activeZoneId);
                        return PatternEvaluationResult.NotDetected(Type, $"Zone {activeZoneId}: pending session aborted (zone gone/used)");
                    }

                    if (foundZoneAnyStatus != null && foundZoneAnyStatus.Status == MarketStructureContext.ZoneStatus.Used)
                    {
                        try
                        {
                            LogExplainOnce(
                                currentSnapshot.Bar,
                                activeZoneId,
                                stage: "Session.ZoneUsed.FallbackPinnedZone",
                                lines: new[]
                                {
                                    $"Dir={_direction}",
                                    $"Zone={activeZoneId}",
                                    $"Reason: SessionActive=true aber Zone.Status=Used → Session wird NICHT beendet. Fallback auf gepinnte Zone-Daten.",
                                    $"PinnedBounds=[{activeSessionTracker.LastKnownZoneLow:F2}..{activeSessionTracker.LastKnownZoneHigh:F2}] Type={activeSessionTracker.LastKnownZoneType} Confirmed={activeSessionTracker.LastKnownZoneConfirmed}"
                                });
                        }
                        catch { }
                    }
                    else
                    {
                        try
                        {
                            LogExplainOnce(
                                currentSnapshot.Bar,
                                activeZoneId,
                                stage: "Session.ZoneMissing.FallbackPinnedZone",
                                lines: new[]
                                {
                                    $"Dir={_direction}",
                                    $"Zone={activeZoneId}",
                                    $"Reason: SessionActive=true aber Zone nicht gefunden/eligible in ActiveZones → Fallback auf gepinnte Zone-Daten.",
                                    $"PinnedBounds=[{activeSessionTracker.LastKnownZoneLow:F2}..{activeSessionTracker.LastKnownZoneHigh:F2}] Type={activeSessionTracker.LastKnownZoneType} Confirmed={activeSessionTracker.LastKnownZoneConfirmed}",
                                    $"zones.Count={zones.Count}"
                                });
                        }
                        catch { }
                    }

                    candidateZone = new MarketStructureContext.Zone
                    {
                        Id = activeZoneId,
                        Type = activeSessionTracker.LastKnownZoneType,
                        Low = activeSessionTracker.LastKnownZoneLow,
                        High = activeSessionTracker.LastKnownZoneHigh,
                        Status = MarketStructureContext.ZoneStatus.Ready,
                        PivotBar = -1,
                        IsConfirmed = activeSessionTracker.LastKnownZoneConfirmed,
                        CreatedBar = -1,
                    };
                    candidateFromTouch = false;
                }
            }
            else
            {
                // Keine aktive Session → Touch-Kandidaten prüfen
                var touchedZones = new List<MarketStructureContext.Zone>(4);

                int zoneCountAll = 0;
                int zoneCountEligibleStatus = 0;
                int zoneCountDirOk = 0;
                int zoneCountTouchesAny = 0;
                int zoneCountTouchesEligible = 0;
                foreach (var z in zones)
                {
                    if (z == null)
                        continue;

                    zoneCountAll++;
                    bool statusEligible = (z.Status == MarketStructureContext.ZoneStatus.New || z.Status == MarketStructureContext.ZoneStatus.Ready);
                    if (statusEligible)
                        zoneCountEligibleStatus++;

                    bool touchesAny = TouchesZone(currentSnapshot, z);
                    if (touchesAny)
                        zoneCountTouchesAny++;

                    if (z.Status != MarketStructureContext.ZoneStatus.New && z.Status != MarketStructureContext.ZoneStatus.Ready)
                        continue;

                    bool dirOk = (_direction == OrderDirections.Buy && z.Type == MarketStructureContext.ZoneType.Support)
                                 || (_direction == OrderDirections.Sell && z.Type == MarketStructureContext.ZoneType.Resistance);
                    if (!dirOk)
                        continue;

                    zoneCountDirOk++;

                    if (!TouchesZone(currentSnapshot, z))
                        continue;

                    zoneCountTouchesEligible++;

                    if (prevClosed != null)
                    {
                        decimal tol = tickSize;
                        bool approachOk = _direction == OrderDirections.Buy
                            ? prevClosed.Close >= (z.High - tol)
                            : prevClosed.Close <= (z.Low + tol);
                        if (!approachOk)
                        {
                            LogExplainOnce(
                                currentSnapshot.Bar,
                                z.Id,
                                stage: "Touch.Blocked.WrongSide",
                                lines: new[]
                                {
                                    $"Dir={_direction}",
                                    $"Zone={z.Id}",
                                    $"Type={z.Type}",
                                    $"PrevClose={prevClosed.Close:F2}",
                                    $"Bounds=[{z.Low:F2}..{z.High:F2}]",
                                    $"Rule={(z.Type == MarketStructureContext.ZoneType.Support ? "Support nur von oben" : "Resistance nur von unten")}: Touch ignoriert"
                                });
                            continue;
                        }
                    }

                    touchedZones.Add(z);
                }

                if (touchedZones.Count == 0)
                {
                    try
                    {
                        SmartLogger.Instance.LogIfChanged(
                            category: "ReversalBounceV2",
                            sourceId: $"V2.{_direction}",
                            barIndex: currentSnapshot.Bar,
                            message: $"[ReversalBounceV2:ZoneGate.NoCandidate] Dir={_direction} | zones={zoneCountAll} | eligibleStatus(New/Ready)={zoneCountEligibleStatus} | dirOk={zoneCountDirOk} | touchesAny={zoneCountTouchesAny} | touchesEligible={zoneCountTouchesEligible} | prevClosed={(prevClosed != null ? prevClosed.Close.ToString("F2") : "null")}",
                            signature: SmartLogger.ComposeSignature(
                                ("dir", _direction.ToString()),
                                ("stage", "ZoneGate.NoCandidate"),
                                ("bar", currentSnapshot.Bar.ToString()),
                                ("zAll", zoneCountAll.ToString()),
                                ("zElig", zoneCountEligibleStatus.ToString()),
                                ("zDir", zoneCountDirOk.ToString()),
                                ("zTouchAny", zoneCountTouchesAny.ToString()),
                                ("zTouchElig", zoneCountTouchesEligible.ToString())),
                            backendLogAction: s => _loggerSource?.LogInfo(s)
                        );
                    }
                    catch { }
                }

                candidateZone = touchedZones.Count > 0 ? touchedZones[0] : null;
                candidateFromTouch = candidateZone != null;

                if (touchedZones.Count > 1)
                {
                    LogExplainOnce(
                        currentSnapshot.Bar,
                        candidateZone?.Id ?? 0,
                        stage: "ZoneTouched.Multi",
                        lines: new[]
                        {
                            $"Dir={_direction}",
                            $"Mehrere Zonen wurden gleichzeitig berührt.",
                            $"Berührte Zonen: {string.Join(", ", touchedZones.Select(t => $"#{t.Id} [{t.Low:F2}..{t.High:F2}] ({(t.IsConfirmed ? "bestätigt" : "noch nicht bestätigt")})"))}",
                            $"Ich prüfe jetzt Zone #{candidateZone?.Id} (die erste in der Liste)."
                        });
                }
            }

            if (candidateZone == null)
            {
                // Wenn bereits eine Session läuft, MUSS diese weiter evaluiert werden – auch ohne frischen Zonenkontakt.
                ZoneTracker? active = null;
                try
                {
                    foreach (var kv in _trackersByZoneId)
                    {
                        if (kv.Value != null && kv.Value.SessionActive)
                        {
                            active = kv.Value;
                            break;
                        }
                    }
                }
                catch { }

                if (active != null)
                {
                    foreach (var z in zones)
                    {
                        if (z != null && z.Id == active.ZoneId)
                        {
                            candidateZone = z;
                            candidateFromTouch = false;
                            break;
                        }
                    }
                }
            }

            if (candidateZone == null)
                return PatternEvaluationResult.NotDetected(Type, $"ZoneGate: no touched/session zone for dir={_direction} (zones={zones.Count})");

            var tracker = GetOrCreateTracker(candidateZone.Id, currentSnapshot.Bar);

            // --- Compression Gate ---
            // Wichtig: darf KEINE aktive Session unterbrechen, sonst fehlen Decisions pro Bar (Score: n/v im Report).
            if (!tracker.SessionActive)
            {
                try
                {
                    if (candidateZone.IsConfirmed)
                    {
                        MarketStructureContext.Zone? nearestOpp = null;
                        decimal bestGap = decimal.MaxValue;

                        foreach (var z in zones)
                        {
                            if (z == null || z.Id == candidateZone.Id)
                                continue;

                            bool strongOpp = z.IsMultiTouch || z.MultiTouchScore >= 2;
                            if (!strongOpp)
                                continue;

                            bool oppOk = (_direction == OrderDirections.Buy && z.Type == MarketStructureContext.ZoneType.Resistance)
                                         || (_direction == OrderDirections.Sell && z.Type == MarketStructureContext.ZoneType.Support);
                            if (!oppOk)
                                continue;

                            decimal gap = _direction == OrderDirections.Buy
                                ? z.Low - candidateZone.High
                                : candidateZone.Low - z.High;
                            if (gap < 0m) gap = 0m;
                            if (gap < bestGap)
                            {
                                bestGap = gap;
                                nearestOpp = z;
                            }
                        }

                        if (nearestOpp != null)
                        {
                            int gapTicks = (int)Math.Round(bestGap / tickSize, MidpointRounding.AwayFromZero);
                            bool inBox = _direction == OrderDirections.Buy
                                ? (currentSnapshot.Close >= candidateZone.High && currentSnapshot.Close <= nearestOpp.High)
                                : (currentSnapshot.Close <= candidateZone.Low && currentSnapshot.Close >= nearestOpp.Low);
                            if (gapTicks <= compressionGapTicks && inBox)
                            {
                                LogExplainOnce(
                                    currentSnapshot.Bar,
                                    candidateZone.Id,
                                    stage: "Compression.Block",
                                    lines: new[]
                                    {
                                        $"Dir={_direction}",
                                        $"CandidateZone={candidateZone.Id}[{candidateZone.Low:F2}..{candidateZone.High:F2}]({candidateZone.Type})",
                                        $"NearestOppZone={nearestOpp.Id}[{nearestOpp.Low:F2}..{nearestOpp.High:F2}]({nearestOpp.Type})",
                                        $"Blockiert: Markt ist zwischen Zone und starker Gegen-Zone eingeklemmt (Box/Sandwich).",
                                        $"Abstand={gapTicks} Ticks (Limit={compressionGapTicks}).",
                                        $"Folge: Kein Entry-Check, um Seitwärts-Chaos zu vermeiden."
                                    });
                                return PatternEvaluationResult.NotDetected(Type, $"CompressionGate: boxed between zones (gapTicks={gapTicks} <= {compressionGapTicks})");
                            }
                        }
                    }
                }
                catch { }
            }

            tracker.LastKnownZoneLow = candidateZone.Low;
            tracker.LastKnownZoneHigh = candidateZone.High;
            tracker.LastKnownZoneType = candidateZone.Type;
            tracker.LastKnownZoneConfirmed = candidateZone.IsConfirmed;

            // ============================================================
            // AKTIVE SESSION WEITERFÜHREN (A oder B)
            // ============================================================
            if (tracker.SessionActive)
            {
                var sessionOutcome = EvaluateActiveSession(
                    tracker, currentSnapshot, history, candidateZone, thresholds, tickSize,
                    MaxSessionBars, MaxConsecutiveBadCloses, MaxSessionPenetrationTicks,
                    out var sessionDecision);

                switch (sessionOutcome)
                {
                    case SessionEvalOutcome.EntryGo:
                    {
                        // Safety-net: block entry if a strong opposite zone overlaps this price area
                        try
                        {
                            MarketStructureContext.Zone? oppStrong = null;
                            foreach (var oz in zones)
                            {
                                if (oz == null || oz.Id == candidateZone.Id)
                                    continue;
                                if (!oz.IsConfirmed || oz.Status == MarketStructureContext.ZoneStatus.Used)
                                    continue;
                                bool isOpp = (_direction == OrderDirections.Buy && oz.Type == MarketStructureContext.ZoneType.Resistance)
                                    || (_direction == OrderDirections.Sell && oz.Type == MarketStructureContext.ZoneType.Support);
                                if (!isOpp)
                                    continue;
                                bool strong = oz.IsMultiTouch || oz.MultiTouchScore >= 2;
                                if (!strong)
                                    continue;
                                decimal overlap = Math.Min(candidateZone.High, oz.High) - Math.Max(candidateZone.Low, oz.Low);
                                if (overlap <= 0m)
                                    continue;
                                int overlapTicks = (int)Math.Round(overlap / tickSize, MidpointRounding.AwayFromZero);
                                if (overlapTicks < 1)
                                    continue;
                                oppStrong = oz;
                                break;
                            }

                            if (oppStrong != null)
                            {
                                LogExplainOnce(currentSnapshot.Bar, candidateZone.Id, stage: "Entry.Blocked.OppositeOverlap", lines: new[]
                                {
                                    $"Dir={_direction}",
                                    $"CandidateZone={candidateZone.Id}({candidateZone.Type}) [{candidateZone.Low:F2}..{candidateZone.High:F2}]",
                                    $"OppStrongZone={oppStrong.Id}({oppStrong.Type}) [{oppStrong.Low:F2}..{oppStrong.High:F2}]",
                                    $"Rule: Kein Entry, wenn starke Gegen-Zone im gleichen Preisband überlappt"
                                });
                                tracker.SessionLastEvalBar = currentSnapshot.Bar;
                                return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: entry blocked by strong opposite overlap (zone {oppStrong.Id})");
                            }
                        }
                        catch { }

                        LogSessionProtocol(
                            tracker,
                            candidateZone,
                            history,
                            currentSnapshot,
                            thresholds,
                            tickSize,
                            MaxSessionBars,
                            MaxConsecutiveBadCloses,
                            MaxSessionPenetrationTicks,
                            "GO – Entry ausgelöst",
                            sessionDecision);

                        var reasons = new List<string>
                        {
                            $"Zone id={candidateZone.Id}",
                            $"Path={(sessionDecision!.Path == AllowPath.UaToFa ? "UAtoFA" : "MultiFA")}",
                            $"Score={sessionDecision.TotalScore:0.0}",
                            $"Confidence={sessionDecision.Confidence:0.00}"
                        };

                        tracker.EntryTriggered = true;
                        _lastValidReversalBarIndex = currentSnapshot.Bar;
                        _lastValidReversalDirection = _direction;

                        currentMarketStructureContext.ConsumeZoneOnEntry(candidateZone.Id);
                        _trackersByZoneId.Remove(candidateZone.Id);

                        if (_loggerSource != null)
                            LoggerHelper.LogInfo(_loggerSource,
                                $"[ReversalBounceV2] DETECTED: zone={candidateZone.Id} dir={_direction} bar={currentSnapshot.Bar} score={sessionDecision.TotalScore:0.0} conf={sessionDecision.Confidence:0.00}");

                        return PatternEvaluationResult.Detected(Type, sessionDecision.Confidence, reasons,
                            new Dictionary<string, object>(), new List<EvaluatedConditionDetail>(), new List<EvaluatedConditionDetail>());
                    }

                    case SessionEvalOutcome.Invalidated:
                    {
                        // Letzte Decision für Logging (kann null sein wenn Invalidierung vor Scoring kam)
                        var lastDec = sessionDecision ?? (tracker.SessionDecisionHistory?.Count > 0
                            ? tracker.SessionDecisionHistory[tracker.SessionDecisionHistory.Count - 1]
                            : null);
                        LogSessionProtocol(
                            tracker,
                            candidateZone,
                            history,
                            currentSnapshot,
                            thresholds,
                            tickSize,
                            MaxSessionBars,
                            MaxConsecutiveBadCloses,
                            MaxSessionPenetrationTicks,
                            "ABBRUCH – Session invalidiert",
                            lastDec);

                        if (tracker.SessionKind == SessionType.Immediate)
                        {
                            tracker.ImmediateDisabled = true;
                            // Zone bleibt für Szenario B
                        }
                        // Zone wird NICHT verbraucht – kann erneut getestet werden
                        return PatternEvaluationResult.NotDetected(Type,
                            $"Zone {candidateZone.Id}: session invalidated ({tracker.SessionEndReason})");
                    }

                    case SessionEvalOutcome.Expired:
                    {
                        var lastDec = sessionDecision ?? (tracker.SessionDecisionHistory?.Count > 0
                            ? tracker.SessionDecisionHistory[tracker.SessionDecisionHistory.Count - 1]
                            : null);
                        LogSessionProtocol(
                            tracker,
                            candidateZone,
                            history,
                            currentSnapshot,
                            thresholds,
                            tickSize,
                            MaxSessionBars,
                            MaxConsecutiveBadCloses,
                            MaxSessionPenetrationTicks,
                            "VERFALL – Session abgelaufen ohne GO",
                            lastDec);

                        if (tracker.SessionKind == SessionType.Immediate)
                        {
                            tracker.ImmediateDisabled = true;
                            // Zone bleibt für Szenario B
                        }
                        // Zone wird NICHT verbraucht
                        return PatternEvaluationResult.NotDetected(Type,
                            $"Zone {candidateZone.Id}: session expired ({tracker.SessionEndReason})");
                    }

                    default: // Continue
                        return PatternEvaluationResult.NotDetected(Type,
                            $"Zone {candidateZone.Id}: session active, bar {currentSnapshot.Bar - tracker.SessionStartBar}/{MaxSessionBars}");
                }
            }

            // ============================================================
            // KEIN TOUCH → nichts zu tun (kein Session-Start ohne Touch)
            // ============================================================
            if (!candidateFromTouch)
                return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: no touch, no active session");

            // ============================================================
            // AB HIER: Neuer Touch erkannt, noch keine aktive Session
            // ============================================================
            LogExplainOnce(
                currentSnapshot.Bar,
                candidateZone.Id,
                stage: "ZoneTouched",
                lines: new[]
                {
                    $"Dir={_direction}",
                    $"Zone={candidateZone.Id}",
                    $"Type={candidateZone.Type}",
                    $"State={(candidateZone.IsConfirmed ? "CONFIRMED" : "PENDING")}",
                    $"Bounds=[{candidateZone.Low:F2}..{candidateZone.High:F2}]",
                    $"Snap BarIdx={currentSnapshot.Bar} ChartBar={currentSnapshot.ChartBarNumber} Time={currentSnapshot.Time:O}",
                    $"Snap OHLC=({currentSnapshot.Open:F2},{currentSnapshot.High:F2},{currentSnapshot.Low:F2},{currentSnapshot.Close:F2})"
                });

            // Spezialfall: Zone erst ab ReadyBar aktiv, Markt war schon weg → direkt Retest-Modus
            try
            {
                if (!tracker.EntryTriggered && !tracker.RetestAttempted && !tracker.ImmediateAttempted)
                {
                    int readyBar = candidateZone.ReadyBar ?? -1;
                    if (readyBar >= 0 && readyBar < currentSnapshot.Bar)
                    {
                        int awayClosesFromReady;
                        bool ok = TryCountAwayClosesSinceFirstTouch(history, firstTouchBar: readyBar, currentBar: currentSnapshot.Bar,
                            zone: candidateZone, dir: _direction, out awayClosesFromReady);

                        if (ok && awayClosesFromReady >= RetestMinAwayCloses)
                        {
                            if (tracker.FirstTouchBar > readyBar)
                                tracker.FirstTouchBar = readyBar;

                            tracker.MovedAwaySeen = true;
                            tracker.ImmediateDisabled = true;
                            tracker.ImmediateAttempted = true;

                            if (candidateZone.IsConfirmed)
                            {
                                tracker.Confirmed = true;
                                if (tracker.ConfirmedBar < 0)
                                    tracker.ConfirmedBar = readyBar;
                            }

                            LogExplainOnce(
                                currentSnapshot.Bar,
                                candidateZone.Id,
                                stage: "RetestMode.FromReadyBar",
                                lines: new[]
                                {
                                    $"Zone ist neu aktiv ab ReadyBar={readyBar}, aber der Markt war davor schon weg.",
                                    $"Seit ReadyBar gab es {awayClosesFromReady} Close(s) außerhalb der Zone in Folge (min={RetestMinAwayCloses}).",
                                    $"Folge: Sofort-Einstieg (Szenario A) wird übersprungen → Retest (Szenario B)."
                                });
                        }
                    }
                }
            }
            catch { }

            if (tracker.FirstTouchTime == DateTime.MinValue)
                tracker.FirstTouchTime = currentSnapshot.Time;

            // Timeout: Zu lange auf Retest gewartet
            if (tracker.FirstTouchTime != default && !tracker.RetestAttempted && !tracker.SessionActive && !candidateZone.IsConfirmed)
            {
                var wait = currentSnapshot.Time - tracker.FirstTouchTime;
                if (wait.TotalMinutes > RetestWaitMinutesMax)
                {
                    LogExplainOnce(
                        currentSnapshot.Bar,
                        candidateZone.Id,
                        stage: "Consume.Expiry.RetestTimeout",
                        lines: new[]
                        {
                            $"Dir={_direction}",
                            $"Zone={candidateZone.Id}",
                            $"FirstTouchTime={tracker.FirstTouchTime:O}",
                            $"Now={currentSnapshot.Time:O}",
                            $"WaitMinutes={wait.TotalMinutes:F1} Limit={RetestWaitMinutesMax}",
                            $"Reason: Zu lange auf Retest gewartet → ConsumeZoneOnExpiry."
                        });
                    currentMarketStructureContext.ConsumeZoneOnExpiry(candidateZone.Id);
                    _trackersByZoneId.Remove(candidateZone.Id);

                    return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: expired waiting for retest (minutes={wait.TotalMinutes:F1})");
                }
            }

            if (tracker.EntryTriggered)
                return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: entry already triggered");

            if (currentSnapshot.Bar - tracker.FirstTouchBar > MaxBarsAfterFirstTouch)
            {
                LogExplainOnce(
                    currentSnapshot.Bar,
                    candidateZone.Id,
                    stage: "Consume.Expiry.MaxBarsAfterFirstTouch",
                    lines: new[]
                    {
                        $"Dir={_direction}",
                        $"Zone={candidateZone.Id}",
                        $"FirstTouchBar={tracker.FirstTouchBar}",
                        $"CurrentBar={currentSnapshot.Bar}",
                        $"BarsSinceFirstTouch={currentSnapshot.Bar - tracker.FirstTouchBar} Limit={MaxBarsAfterFirstTouch}",
                        $"Reason: Zu viele Bars seit FirstTouch → ConsumeZoneOnExpiry."
                    });
                currentMarketStructureContext.ConsumeZoneOnExpiry(candidateZone.Id);
                _trackersByZoneId.Remove(candidateZone.Id);

                return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: expired (barsSinceFirstTouch={currentSnapshot.Bar - tracker.FirstTouchBar})");
            }

            if (currentSnapshot.Bar <= tracker.LastTouchBar + ZoneTouchCooldownBars)
            {
                tracker.LastTouchBar = currentSnapshot.Bar;
            }

            // ============================================================
            // SZENARIO A: Sofort-Entry (Zone noch nicht bestätigt)
            // ============================================================
            if (!tracker.Confirmed)
            {
                if (!tracker.ImmediateDisabled)
                {
                    bool isOutside = _direction == OrderDirections.Buy
                        ? currentSnapshot.Low <= candidateZone.Low - tickSize
                        : currentSnapshot.High >= candidateZone.High + tickSize;

                    if (isOutside)
                    {
                        if (tracker.FirstOutsideBar < 0)
                            tracker.FirstOutsideBar = currentSnapshot.Bar;

                        int extTicks = _direction == OrderDirections.Buy
                            ? (int)Math.Round(Math.Max(0m, (candidateZone.Low - currentSnapshot.Low) / tickSize), MidpointRounding.AwayFromZero)
                            : (int)Math.Round(Math.Max(0m, (currentSnapshot.High - candidateZone.High) / tickSize), MidpointRounding.AwayFromZero);

                        if (extTicks > tracker.MaxOutsideExtensionTicks)
                            tracker.MaxOutsideExtensionTicks = extTicks;
                    }

                    bool reclaimedInside = currentSnapshot.Close >= candidateZone.Low && currentSnapshot.Close <= candidateZone.High;
                    bool responseOk = tracker.FirstOutsideBar >= 0 && (currentSnapshot.Bar - tracker.FirstOutsideBar) <= ImmediateResponseTimeBarsMax;
                    bool extensionOk = tracker.MaxOutsideExtensionTicks <= ImmediateMaxOutsideExtensionTicks;
                    bool approachOk = prevClosed == null || (_direction == OrderDirections.Buy
                        ? prevClosed.Close >= (candidateZone.High - tickSize)
                        : prevClosed.Close <= (candidateZone.Low + tickSize));

                    bool armedStopRun = responseOk && reclaimedInside && extensionOk;
                    bool armedTouch = TouchesZone(currentSnapshot, candidateZone) && reclaimedInside && approachOk;

                    // Armed-Moment → Session starten (statt One-Shot)
                    if (!tracker.ImmediateAttempted && (armedStopRun || armedTouch))
                    {
                        tracker.ImmediateAttempted = true;
                        StartSession(tracker, SessionType.Immediate, currentSnapshot);

                        // Erste Evaluation sofort auf der Touch-Bar
                        var firstOutcome = EvaluateActiveSession(
                            tracker, currentSnapshot, history, candidateZone, thresholds, tickSize,
                            MaxSessionBars, MaxConsecutiveBadCloses, MaxSessionPenetrationTicks,
                            out var firstDecision);

                        if (firstOutcome == SessionEvalOutcome.EntryGo)
                        {
                            // Safety-net: block entry if a strong opposite zone overlaps this price area
                            try
                            {
                                MarketStructureContext.Zone? oppStrong = null;
                                foreach (var oz in zones)
                                {
                                    if (oz == null || oz.Id == candidateZone.Id)
                                        continue;
                                    if (!oz.IsConfirmed || oz.Status == MarketStructureContext.ZoneStatus.Used)
                                        continue;
                                    bool isOpp = (_direction == OrderDirections.Buy && oz.Type == MarketStructureContext.ZoneType.Resistance)
                                        || (_direction == OrderDirections.Sell && oz.Type == MarketStructureContext.ZoneType.Support);
                                    if (!isOpp)
                                        continue;
                                    bool strong = oz.IsMultiTouch || oz.MultiTouchScore >= 2;
                                    if (!strong)
                                        continue;
                                    decimal overlap = Math.Min(candidateZone.High, oz.High) - Math.Max(candidateZone.Low, oz.Low);
                                    if (overlap <= 0m)
                                        continue;
                                    int overlapTicks = (int)Math.Round(overlap / tickSize, MidpointRounding.AwayFromZero);
                                    if (overlapTicks < 1)
                                        continue;
                                    oppStrong = oz;
                                    break;
                                }

                                if (oppStrong != null)
                                {
                                    LogExplainOnce(currentSnapshot.Bar, candidateZone.Id, stage: "Entry.Blocked.OppositeOverlap", lines: new[]
                                    {
                                        $"Dir={_direction}",
                                        $"CandidateZone={candidateZone.Id}({candidateZone.Type}) [{candidateZone.Low:F2}..{candidateZone.High:F2}]",
                                        $"OppStrongZone={oppStrong.Id}({oppStrong.Type}) [{oppStrong.Low:F2}..{oppStrong.High:F2}]",
                                        $"Rule: Kein Entry, wenn starke Gegen-Zone im gleichen Preisband überlappt"
                                    });
                                    tracker.SessionLastEvalBar = currentSnapshot.Bar;
                                    return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: entry blocked by strong opposite overlap (zone {oppStrong.Id})");
                                }
                            }
                            catch { }

                            LogSessionProtocol(
                                tracker,
                                candidateZone,
                                history,
                                currentSnapshot,
                                thresholds,
                                tickSize,
                                MaxSessionBars,
                                MaxConsecutiveBadCloses,
                                MaxSessionPenetrationTicks,
                                "GO – Entry ausgelöst",
                                firstDecision);

                            var reasonsA = new List<string>
                            {
                                $"ZoneImmediate id={candidateZone.Id}",
                                $"Path={(firstDecision!.Path == AllowPath.UaToFa ? "UAtoFA" : "MultiFA")}",
                                $"Score={firstDecision.TotalScore:0.0}",
                                $"Confidence={firstDecision.Confidence:0.00}"
                            };

                            tracker.EntryTriggered = true;
                            _lastValidReversalBarIndex = currentSnapshot.Bar;
                            _lastValidReversalDirection = _direction;

                            currentMarketStructureContext.ConsumeZoneOnEntry(candidateZone.Id);
                            _trackersByZoneId.Remove(candidateZone.Id);

                            if (_loggerSource != null)
                                LoggerHelper.LogInfo(_loggerSource,
                                    $"[ReversalBounceV2] DETECTED (Immediate): zone={candidateZone.Id} dir={_direction} bar={currentSnapshot.Bar} score={firstDecision.TotalScore:0.0} conf={firstDecision.Confidence:0.00}");

                            return PatternEvaluationResult.Detected(Type, firstDecision.Confidence, reasonsA,
                                new Dictionary<string, object>(), new List<EvaluatedConditionDetail>(), new List<EvaluatedConditionDetail>());
                        }

                        // Session läuft weiter → nächste Bars werden über den Session-Block oben evaluiert
                        return PatternEvaluationResult.NotDetected(Type,
                            $"Zone {candidateZone.Id}: Szenario A Session gestartet, warte auf Bestätigung");
                    }
                }

                bool movedAway = tracker.MovedAwaySeen || HasMovedAwayFromZone(currentSnapshot, candidateZone, tickSize);
                if (!movedAway)
                {
                    LogExplainOnce(
                        currentSnapshot.Bar,
                        candidateZone.Id,
                        stage: "Touch.WaitMoveAway",
                        lines: new[]
                        {
                            $"Dir={_direction}",
                            $"Zone={candidateZone.Id}",
                            $"Type={candidateZone.Type}",
                            $"State={(candidateZone.IsConfirmed ? "CONFIRMED" : "PENDING")}",
                            $"Bounds=[{candidateZone.Low:F2}..{candidateZone.High:F2}]",
                            $"Rule: Nach FirstTouch wird erst weiter geprüft, wenn der Markt erkennbar von der Zone weg gelaufen ist (MovedAway).",
                            $"Folge: Kein Session/Entry-Check auf dieser Bar."
                        });

                    return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: erste Berührung – warte auf Wegbewegung");
                }

                tracker.MovedAwaySeen = true;

                if (!candidateZone.IsConfirmed)
                {
                    LogExplainOnce(
                        currentSnapshot.Bar,
                        candidateZone.Id,
                        stage: "Touch.WaitZigZagConfirm",
                        lines: new[]
                        {
                            $"Dir={_direction}",
                            $"Zone={candidateZone.Id}",
                            $"Type={candidateZone.Type}",
                            $"State=PENDING",
                            $"Bounds=[{candidateZone.Low:F2}..{candidateZone.High:F2}]",
                            $"Rule: Retest (Szenario B) wird erst geprüft, wenn die Zone durch ZigZag bestätigt ist.",
                            $"Folge: Kein Entry-Check auf dieser Bar."
                        });

                    return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: pending; waiting zigzag confirmation before retest");
                }

                tracker.Confirmed = true;
                tracker.ConfirmedBar = currentSnapshot.Bar;
                if (_loggerSource != null)
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBounceV2] Zone confirmed: id={candidateZone.Id} dir={_direction} firstTouch={tracker.FirstTouchBar} confirmedBar={tracker.ConfirmedBar}");

                // If the zone becomes confirmed ON a touch bar, we only allow immediate Retest processing
                // in the special case where Scenario A was already explicitly skipped via RetestMode.FromReadyBar.
                // This keeps the normal A->B flow intact (first touch is NOT treated as retest).
                if (tracker.ImmediateDisabled && tracker.ImmediateAttempted)
                {
                    try
                    {
                        bool touchNowOnConfirm = TouchesZone(currentSnapshot, candidateZone);
                        bool approachNowOkOnConfirm = prevClosed == null || (_direction == OrderDirections.Buy
                            ? prevClosed.Close >= (candidateZone.High - tickSize)
                            : prevClosed.Close <= (candidateZone.Low + tickSize));

                        if (touchNowOnConfirm && approachNowOkOnConfirm)
                        {
                            if (currentSnapshot.Bar - tracker.FirstTouchBar >= MinBarsBetweenFirstTouchAndRetest)
                            {
                                if (TryCountAwayClosesSinceFirstTouch(history, tracker.FirstTouchBar, currentSnapshot.Bar, candidateZone, _direction, out var awayClosesNow)
                                    && awayClosesNow >= RetestMinAwayCloses)
                                {
                                    tracker.RetestAttempted = true;
                                    tracker.RetestBar = currentSnapshot.Bar;
                                    tracker.RetestAttemptTime = currentSnapshot.Time;
                                    LogExplainOnce(
                                        currentSnapshot.Bar,
                                        candidateZone.Id,
                                        stage: "Retest.SessionStart.OnConfirmTouch",
                                        lines: new[]
                                        {
                                            $"Zone wurde in dieser Touch-Bar bestätigt und Szenario A wurde vorher übersprungen (FromReadyBar) → Retest-Session wird sofort gestartet.",
                                            $"BarIdx={currentSnapshot.Bar} ChartBar={currentSnapshot.ChartBarNumber} Time={currentSnapshot.Time:O}",
                                            $"awayClosesInRow={awayClosesNow} (min={RetestMinAwayCloses})"
                                        });

                                    StartSession(tracker, SessionType.Retest, currentSnapshot);

                                    var retestOutcomeNow = EvaluateActiveSession(
                                        tracker, currentSnapshot, history, candidateZone, thresholds, tickSize,
                                        MaxSessionBars, MaxConsecutiveBadCloses, MaxSessionPenetrationTicks,
                                        out var retestDecisionNow);

                                    if (retestOutcomeNow == SessionEvalOutcome.EntryGo)
                                    {
                                        // Safety-net: block entry if a strong opposite zone overlaps this price area
                                        try
                                        {
                                            MarketStructureContext.Zone? oppStrong = null;
                                            foreach (var oz in zones)
                                            {
                                                if (oz == null || oz.Id == candidateZone.Id)
                                                    continue;
                                                if (!oz.IsConfirmed || oz.Status == MarketStructureContext.ZoneStatus.Used)
                                                    continue;
                                                bool isOpp = (_direction == OrderDirections.Buy && oz.Type == MarketStructureContext.ZoneType.Resistance)
                                                    || (_direction == OrderDirections.Sell && oz.Type == MarketStructureContext.ZoneType.Support);
                                                if (!isOpp)
                                                    continue;
                                                bool strong = oz.IsMultiTouch || oz.MultiTouchScore >= 2;
                                                if (!strong)
                                                    continue;
                                                decimal overlap = Math.Min(candidateZone.High, oz.High) - Math.Max(candidateZone.Low, oz.Low);
                                                if (overlap <= 0m)
                                                    continue;
                                                int overlapTicks = (int)Math.Round(overlap / tickSize, MidpointRounding.AwayFromZero);
                                                if (overlapTicks < 1)
                                                    continue;
                                                oppStrong = oz;
                                                break;
                                            }

                                            if (oppStrong != null)
                                            {
                                                LogExplainOnce(currentSnapshot.Bar, candidateZone.Id, stage: "Entry.Blocked.OppositeOverlap", lines: new[]
                                                {
                                                    $"Dir={_direction}",
                                                    $"CandidateZone={candidateZone.Id}({candidateZone.Type}) [{candidateZone.Low:F2}..{candidateZone.High:F2}]",
                                                    $"OppStrongZone={oppStrong.Id}({oppStrong.Type}) [{oppStrong.Low:F2}..{oppStrong.High:F2}]",
                                                    $"Rule: Kein Entry, wenn starke Gegen-Zone im gleichen Preisband überlappt"
                                                });
                                                tracker.SessionLastEvalBar = currentSnapshot.Bar;
                                                return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: entry blocked by strong opposite overlap (zone {oppStrong.Id})");
                                            }
                                        }
                                        catch { }

                                        LogSessionProtocol(
                                            tracker,
                                            candidateZone,
                                            history,
                                            currentSnapshot,
                                            thresholds,
                                            tickSize,
                                            MaxSessionBars,
                                            MaxConsecutiveBadCloses,
                                            MaxSessionPenetrationTicks,
                                            "GO – Entry ausgelöst (Sofort auf Retest-Touch-Bar)",
                                            retestDecisionNow);

                                        var reasonsNow = new List<string>
                                        {
                                            $"ZoneRetest id={candidateZone.Id}",
                                            $"Path={(retestDecisionNow!.Path == AllowPath.UaToFa ? "UAtoFA" : "MultiFA")}",
                                            $"Score={retestDecisionNow.TotalScore:0.0}",
                                            $"Confidence={retestDecisionNow.Confidence:0.00}"
                                        };

                                        tracker.EntryTriggered = true;
                                        _lastValidReversalBarIndex = currentSnapshot.Bar;
                                        _lastValidReversalDirection = _direction;

                                        currentMarketStructureContext.ConsumeZoneOnEntry(candidateZone.Id);
                                        _trackersByZoneId.Remove(candidateZone.Id);

                                        if (_loggerSource != null)
                                            LoggerHelper.LogInfo(_loggerSource,
                                                $"[ReversalBounceV2] DETECTED (RetestOnConfirmTouch): zone={candidateZone.Id} dir={_direction} bar={currentSnapshot.Bar} score={retestDecisionNow.TotalScore:0.0} conf={retestDecisionNow.Confidence:0.00}");

                                        return PatternEvaluationResult.Detected(Type, retestDecisionNow.Confidence, reasonsNow,
                                            new Dictionary<string, object>(), new List<EvaluatedConditionDetail>(), new List<EvaluatedConditionDetail>());
                                    }

                                    return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: retest session started on confirm-touch bar");
                                }
                            }
                        }
                    }
                    catch { }
                }

                return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: confirmed, waiting for retest");
            }

            // ============================================================
            // SZENARIO B: Retest (Zone ist bestätigt)
            // ============================================================
            if (!candidateZone.IsConfirmed)
            {
                return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: pending; retest blocked until zigzag confirmation");
            }

            // Bestätigte Zone, aber kein Touch (oder Touch von falscher Seite) → warten + Swing-Invalidierung
            bool touchNowSession = TouchesZone(currentSnapshot, candidateZone);
            bool approachNowOk = prevClosed == null || (_direction == OrderDirections.Buy
                ? prevClosed.Close >= (candidateZone.High - tickSize)
                : prevClosed.Close <= (candidateZone.Low + tickSize));
            if (!touchNowSession || !approachNowOk)
            {
                if (touchNowSession && !approachNowOk && prevClosed != null)
                {
                    LogExplainOnce(
                        currentSnapshot.Bar,
                        candidateZone.Id,
                        stage: "Touch.Blocked.WrongSide",
                        lines: new[]
                        {
                            $"Dir={_direction}",
                            $"Zone={candidateZone.Id}",
                            $"Type={candidateZone.Type}",
                            $"PrevClose={prevClosed.Close:F2}",
                            $"Bounds=[{candidateZone.Low:F2}..{candidateZone.High:F2}]",
                            $"Rule={(candidateZone.Type == MarketStructureContext.ZoneType.Support ? "Support nur von oben" : "Resistance nur von unten")}: Touch ignoriert"
                        });
                }

                LogExplainOnce(
                    currentSnapshot.Bar,
                    candidateZone.Id,
                    stage: "Retest.WaitingNoTouch",
                    lines: new[]
                    {
                        $"Zone={candidateZone.Id} CONFIRMED, aber kein Touch in dieser Bar.",
                        $"Snap BarIdx={currentSnapshot.Bar} ChartBar={currentSnapshot.ChartBarNumber} Time={currentSnapshot.Time:O}",
                        $"Snap High/Low=({currentSnapshot.High:F2}/{currentSnapshot.Low:F2}) Zone=[{candidateZone.Low:F2}..{candidateZone.High:F2}]"
                    });

                if (tracker.MaxFavorableExcursionPrice == 0m)
                    tracker.MaxFavorableExcursionPrice = _direction == OrderDirections.Buy ? currentSnapshot.High : currentSnapshot.Low;
                else if (_direction == OrderDirections.Buy)
                    tracker.MaxFavorableExcursionPrice = Math.Max(tracker.MaxFavorableExcursionPrice, currentSnapshot.High);
                else
                    tracker.MaxFavorableExcursionPrice = Math.Min(tracker.MaxFavorableExcursionPrice, currentSnapshot.Low);

                const int SignificantBreakTicks = 2;
                var swingSignal = ComputeSwingSignalFromHistory(history, currentBar: currentSnapshot.Bar, maxBars: 220);
                if (_direction == OrderDirections.Buy && swingSignal?.LastSwingHigh.HasValue == true)
                {
                    var level = swingSignal.LastSwingHigh.Value + SignificantBreakTicks * tickSize;
                    if (currentSnapshot.High >= level && currentSnapshot.Bar > tracker.ConfirmedBar)
                    {
                        currentMarketStructureContext.ConsumeZoneOnInvalidation(candidateZone.Id);
                        _trackersByZoneId.Remove(candidateZone.Id);
                        return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: invalidated (new swing high exceeded {level:F2} before retest)");
                    }
                }
                else if (_direction == OrderDirections.Sell && swingSignal?.LastSwingLow.HasValue == true)
                {
                    var level = swingSignal.LastSwingLow.Value - SignificantBreakTicks * tickSize;
                    if (currentSnapshot.Low <= level && currentSnapshot.Bar > tracker.ConfirmedBar)
                    {
                        currentMarketStructureContext.ConsumeZoneOnInvalidation(candidateZone.Id);
                        _trackersByZoneId.Remove(candidateZone.Id);
                        return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: invalidated (new swing low exceeded {level:F2} before retest)");
                    }
                }

                return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: confirmed, waiting retest touch");
            }

            // Retest-Touch erkannt → Gating-Checks
            if (currentSnapshot.Bar - tracker.FirstTouchBar < MinBarsBetweenFirstTouchAndRetest)
            {
                LogExplainOnce(
                    currentSnapshot.Bar,
                    candidateZone.Id,
                    stage: "Retest.BlockedTooEarly",
                    lines: new[]
                    {
                        $"Retest-Touch erkannt, aber zu früh nach FirstTouch.",
                        $"BarIdx={currentSnapshot.Bar} ChartBar={currentSnapshot.ChartBarNumber}",
                        $"firstTouchBar={tracker.FirstTouchBar} -> barsSinceFirstTouch={currentSnapshot.Bar - tracker.FirstTouchBar} (min={MinBarsBetweenFirstTouchAndRetest})"
                    });
                return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: retest too early");
            }

            if (!TryCountAwayClosesSinceFirstTouch(history, tracker.FirstTouchBar, currentSnapshot.Bar, candidateZone, _direction, out var awayCloses)
                || awayCloses < RetestMinAwayCloses)
            {
                LogExplainOnce(
                    currentSnapshot.Bar,
                    candidateZone.Id,
                    stage: "Retest.BlockedAwayCloses",
                    lines: new[]
                    {
                        $"Retest-Touch erkannt, aber Wegbewegung (AwayCloses) zu schwach.",
                        $"BarIdx={currentSnapshot.Bar} ChartBar={currentSnapshot.ChartBarNumber}",
                        $"awayClosesInRow={awayCloses} (min={RetestMinAwayCloses})"
                    });
                return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: retest ignored (awayClosesInRow={awayCloses} < {RetestMinAwayCloses})");
            }

            // Retest-Touch ist valide → Session starten (statt One-Shot)
            tracker.RetestAttempted = true;
            tracker.RetestBar = currentSnapshot.Bar;
            tracker.RetestAttemptTime = currentSnapshot.Time;
            LogExplainOnce(
                currentSnapshot.Bar,
                candidateZone.Id,
                stage: "Retest.SessionStart",
                lines: new[]
                {
                    $"Retest valide → Retest-Session wird gestartet.",
                    $"BarIdx={currentSnapshot.Bar} ChartBar={currentSnapshot.ChartBarNumber} Time={currentSnapshot.Time:O}"
                });
            StartSession(tracker, SessionType.Retest, currentSnapshot);

            // Erste Evaluation sofort auf der Touch-Bar
            var retestOutcome = EvaluateActiveSession(
                tracker, currentSnapshot, history, candidateZone, thresholds, tickSize,
                MaxSessionBars, MaxConsecutiveBadCloses, MaxSessionPenetrationTicks,
                out var retestDecision);

            if (retestOutcome == SessionEvalOutcome.EntryGo)
            {
                // Safety-net: block entry if a strong opposite zone overlaps this price area
                try
                {
                    MarketStructureContext.Zone? oppStrong = null;
                    foreach (var oz in zones)
                    {
                        if (oz == null || oz.Id == candidateZone.Id)
                            continue;
                        if (!oz.IsConfirmed || oz.Status == MarketStructureContext.ZoneStatus.Used)
                            continue;
                        bool isOpp = (_direction == OrderDirections.Buy && oz.Type == MarketStructureContext.ZoneType.Resistance)
                            || (_direction == OrderDirections.Sell && oz.Type == MarketStructureContext.ZoneType.Support);
                        if (!isOpp)
                            continue;
                        bool strong = oz.IsMultiTouch || oz.MultiTouchScore >= 2;
                        if (!strong)
                            continue;
                        decimal overlap = Math.Min(candidateZone.High, oz.High) - Math.Max(candidateZone.Low, oz.Low);
                        if (overlap <= 0m)
                            continue;
                        int overlapTicks = (int)Math.Round(overlap / tickSize, MidpointRounding.AwayFromZero);
                        if (overlapTicks < 1)
                            continue;
                        oppStrong = oz;
                        break;
                    }

                    if (oppStrong != null)
                    {
                        LogExplainOnce(currentSnapshot.Bar, candidateZone.Id, stage: "Entry.Blocked.OppositeOverlap", lines: new[]
                        {
                            $"Dir={_direction}",
                            $"CandidateZone={candidateZone.Id}({candidateZone.Type}) [{candidateZone.Low:F2}..{candidateZone.High:F2}]",
                            $"OppStrongZone={oppStrong.Id}({oppStrong.Type}) [{oppStrong.Low:F2}..{oppStrong.High:F2}]",
                            $"Rule: Kein Entry, wenn starke Gegen-Zone im gleichen Preisband überlappt"
                        });
                        tracker.SessionLastEvalBar = currentSnapshot.Bar;
                        return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: entry blocked by strong opposite overlap (zone {oppStrong.Id})");
                    }
                }
                catch { }

                LogSessionProtocol(
                    tracker,
                    candidateZone,
                    history,
                    currentSnapshot,
                    thresholds,
                    tickSize,
                    MaxSessionBars,
                    MaxConsecutiveBadCloses,
                    MaxSessionPenetrationTicks,
                    "GO – Entry ausgelöst (Sofort auf Retest-Touch-Bar)",
                    retestDecision);

                var reasons = new List<string>
                {
                    $"ZoneRetest id={candidateZone.Id}",
                    $"Path={(retestDecision!.Path == AllowPath.UaToFa ? "UAtoFA" : "MultiFA")}",
                    $"Score={retestDecision.TotalScore:0.0}",
                    $"Confidence={retestDecision.Confidence:0.00}"
                };

                tracker.EntryTriggered = true;
                _lastValidReversalBarIndex = currentSnapshot.Bar;
                _lastValidReversalDirection = _direction;

                currentMarketStructureContext.ConsumeZoneOnEntry(candidateZone.Id);
                _trackersByZoneId.Remove(candidateZone.Id);

                if (_loggerSource != null)
                    LoggerHelper.LogInfo(_loggerSource,
                        $"[ReversalBounceV2] DETECTED (Retest): zone={candidateZone.Id} dir={_direction} bar={currentSnapshot.Bar} score={retestDecision.TotalScore:0.0} conf={retestDecision.Confidence:0.00}");

                return PatternEvaluationResult.Detected(Type, retestDecision.Confidence, reasons,
                    new Dictionary<string, object>(), new List<EvaluatedConditionDetail>(), new List<EvaluatedConditionDetail>());
            }

            // Session läuft weiter → nächste Bars werden über den Session-Block oben evaluiert
            return PatternEvaluationResult.NotDetected(Type,
                $"Zone {candidateZone.Id}: Szenario B Session gestartet, warte auf Bestätigung");
        }

        private void StartSession(ZoneTracker tracker, SessionType kind, OvSnapshot snap)
        {
            tracker.SessionActive = true;
            tracker.SessionKind = kind;
            tracker.SessionStartBar = snap.Bar;
            tracker.SessionStartChartBar = snap.ChartBarNumber > 0 ? snap.ChartBarNumber : snap.Bar;
            tracker.SessionLastEvalBar = -1;

            tracker.SessionTouchLow = snap.Low;
            tracker.SessionTouchHigh = snap.High;
            tracker.SessionTouchPocPrice = snap.CandlePocPrice;
            tracker.SessionPressureSeen = false;
            tracker.SessionPhase = ReversalPhase.Touch;
            tracker.SessionPhaseStartBar = snap.Bar;
            tracker.SessionStrongDefenseCount = 0;
            tracker.SessionSawAbsorption = false;
            tracker.SessionSawSweep = false;
            tracker.SessionSawMultiFaDefense = false;
            tracker.SessionSawFaAtZone = false;
            tracker.SessionAbsorptionConfirmed = false;
            tracker.SessionAbsorptionBar = -1;
            tracker.SessionAbsorptionPocPrice = 0m;
            tracker.SessionSawUaToFa = false;
            tracker.SessionUaToFaBar = -1;
            tracker.SessionLatchUfToFa = false;
            tracker.SessionLatchUfToFaBar = -1;
            tracker.SessionLatchClose = false;
            tracker.SessionLatchCloseBar = -1;
            tracker.SessionLatchCloseBrokenBar = -1;
            tracker.SessionLatchPoc = false;
            tracker.SessionLatchPocBar = -1;
            tracker.SessionLatchAbsorption = false;
            tracker.SessionLatchAbsorptionBar = -1;
            tracker.SessionSawExhaustion = false;
            tracker.ConsecutiveBadCloses = 0;
            tracker.SessionMaxPenetrationTicks = 0;
            tracker.SessionBestScore = 0m;
            tracker.SessionBestScoreItems = null;

            tracker.SessionDecisionHistory = new List<DecisionResult>();
            tracker.SessionDecisionBars = new List<int>();
            tracker.SessionDecisionPhases = new List<ReversalPhase>();
            tracker.SessionEndReason = null;
            tracker.SessionTouchWasReversal = false;
        }

        private enum SessionEvalOutcome
        {
            Continue,
            EntryGo,
            Invalidated,
            Expired
        }

        private SessionEvalOutcome EvaluateActiveSession(
            ZoneTracker tracker,
            OvSnapshot currentSnapshot,
            OfFeaturesHistory history,
            MarketStructureContext.Zone zone,
            OrderflowThresholds thresholds,
            decimal tickSize,
            int maxSessionBars,
            int maxConsecutiveBadCloses,
            int maxSessionPenetrationTicks,
            out DecisionResult? decision)
        {
            decision = null;

            if (!tracker.SessionActive)
                return SessionEvalOutcome.Continue;

            if (tracker.SessionLastEvalBar == currentSnapshot.Bar)
                return SessionEvalOutcome.Continue;

            // Session erlaubt Touch-Bar + 2 Folge-Bars = max 3 Bars
            const int SignalSearchBars = 3;
            int currChartBar = currentSnapshot.ChartBarNumber > 0 ? currentSnapshot.ChartBarNumber : currentSnapshot.Bar;
            int startChartBar = tracker.SessionStartChartBar >= 0 ? tracker.SessionStartChartBar : tracker.SessionStartBar;
            int sessionBarNr = currChartBar - startChartBar + 1;

            bool closeInDirectionNow = _direction == OrderDirections.Buy
                ? currentSnapshot.Close > currentSnapshot.Open
                : currentSnapshot.Close < currentSnapshot.Open;

            static DecisionResult CreateExitDecision(string reason)
            {
                return new DecisionResult
                {
                    Allowed = false,
                    Entry = false,
                    Path = AllowPath.None,
                    BaseScore = 0,
                    TotalScore = 0m,
                    Confidence = 0m,
                    BlockReasonDe = reason,
                    Items = new List<ScoreItem>()
                };
            }

            // --- Invalidierung: Session-Dauer (Touch-Bar + 2 Folge-Bars) ---
            if (sessionBarNr > SignalSearchBars)
            {
                tracker.SessionActive = false;
                tracker.SessionEndReason = $"Session abgelaufen nach {sessionBarNr} Bars ohne Signalbar (max. {SignalSearchBars}).";

                var exitDec = CreateExitDecision(tracker.SessionEndReason);
                decision = exitDec;
                tracker.SessionDecisionHistory ??= new List<DecisionResult>();
                tracker.SessionDecisionBars ??= new List<int>();
                tracker.SessionDecisionPhases ??= new List<ReversalPhase>();
                tracker.SessionDecisionHistory.Add(exitDec);
                tracker.SessionDecisionBars.Add(currentSnapshot.Bar);
                tracker.SessionDecisionPhases.Add(ReversalPhase.Defense);
                return SessionEvalOutcome.Expired;
            }

            // --- Regel: Wenn Touch-Bar nicht Umkehrbar war, muss der nächste Chart-Bar Umkehrbar sein ---
            if (currChartBar == startChartBar)
            {
                tracker.SessionTouchWasReversal = closeInDirectionNow;
            }
            else if (currChartBar == startChartBar + 1 && !tracker.SessionTouchWasReversal && !closeInDirectionNow)
            {
                tracker.SessionActive = false;
                tracker.SessionEndReason = _direction == OrderDirections.Buy
                    ? $"Session abgebrochen: Touch-Bar war nicht Umkehrbar, und der Folge-Bar schließt nicht bullisch (O={currentSnapshot.Open:F2} C={currentSnapshot.Close:F2})."
                    : $"Session abgebrochen: Touch-Bar war nicht Umkehrbar, und der Folge-Bar schließt nicht bärisch (O={currentSnapshot.Open:F2} C={currentSnapshot.Close:F2}).";

                var exitDec = CreateExitDecision(tracker.SessionEndReason);
                decision = exitDec;
                tracker.SessionDecisionHistory ??= new List<DecisionResult>();
                tracker.SessionDecisionBars ??= new List<int>();
                tracker.SessionDecisionPhases ??= new List<ReversalPhase>();
                tracker.SessionDecisionHistory.Add(exitDec);
                tracker.SessionDecisionBars.Add(currentSnapshot.Bar);
                tracker.SessionDecisionPhases.Add(ReversalPhase.Defense);
                return SessionEvalOutcome.Invalidated;
            }

            // --- Invalidierung: Konsekutive Bad-Closes ---
            bool badClose = _direction == OrderDirections.Buy
                ? currentSnapshot.Close < zone.Low
                : currentSnapshot.Close > zone.High;

            if (badClose)
                tracker.ConsecutiveBadCloses++;
            else
                tracker.ConsecutiveBadCloses = 0;

            if (tracker.ConsecutiveBadCloses >= maxConsecutiveBadCloses)
            {
                tracker.SessionActive = false;
                tracker.SessionEndReason = $"Session abgebrochen: {tracker.ConsecutiveBadCloses} Bars in Folge mit Close auf der falschen Seite der Zone.";

                var exitDec = CreateExitDecision(tracker.SessionEndReason);
                decision = exitDec;
                tracker.SessionDecisionHistory ??= new List<DecisionResult>();
                tracker.SessionDecisionBars ??= new List<int>();
                tracker.SessionDecisionPhases ??= new List<ReversalPhase>();
                tracker.SessionDecisionHistory.Add(exitDec);
                tracker.SessionDecisionBars.Add(currentSnapshot.Bar);
                tracker.SessionDecisionPhases.Add(ReversalPhase.Defense);
                return SessionEvalOutcome.Invalidated;
            }

            // --- Invalidierung: Maximale Penetration ---
            int penetrationTicks;
            if (_direction == OrderDirections.Buy)
                penetrationTicks = Math.Max(0, RoundTicks(Math.Max(0m, zone.Low - currentSnapshot.Low), tickSize));
            else
                penetrationTicks = Math.Max(0, RoundTicks(Math.Max(0m, currentSnapshot.High - zone.High), tickSize));

            if (penetrationTicks > tracker.SessionMaxPenetrationTicks)
                tracker.SessionMaxPenetrationTicks = penetrationTicks;

            if (tracker.SessionMaxPenetrationTicks > maxSessionPenetrationTicks)
            {
                tracker.SessionActive = false;
                tracker.SessionEndReason = $"Session abgebrochen: Penetration {tracker.SessionMaxPenetrationTicks} Ticks übersteigt Limit ({maxSessionPenetrationTicks} Ticks).";

                var exitDec = CreateExitDecision(tracker.SessionEndReason);
                decision = exitDec;
                tracker.SessionDecisionHistory ??= new List<DecisionResult>();
                tracker.SessionDecisionBars ??= new List<int>();
                tracker.SessionDecisionPhases ??= new List<ReversalPhase>();
                tracker.SessionDecisionHistory.Add(exitDec);
                tracker.SessionDecisionBars.Add(currentSnapshot.Bar);
                tracker.SessionDecisionPhases.Add(ReversalPhase.Defense);
                return SessionEvalOutcome.Invalidated;
            }

            // --- Touch-Phase: Einmalig beim ersten Bar der Session ---
            tracker.SessionLastEvalBar = currentSnapshot.Bar;

            if (tracker.SessionPhase == ReversalPhase.Touch)
            {
                tracker.SessionTouchLow = currentSnapshot.Low;
                tracker.SessionTouchHigh = currentSnapshot.High;
                tracker.SessionTouchPocPrice = currentSnapshot.CandlePocPrice;
                try
                {
                    const int AdaptiveVolLookback = 30;
                    decimal volMedian = GetAdaptiveVolumeMin(history, currentSnapshot, AdaptiveVolLookback, multiplier: 1.0m);
                    int penetrationTicksLocal = _direction == OrderDirections.Buy
                        ? Math.Max(0, RoundTicks(Math.Max(0m, zone.Low - currentSnapshot.Low), tickSize))
                        : Math.Max(0, RoundTicks(Math.Max(0m, currentSnapshot.High - zone.High), tickSize));
                    bool volHigh = volMedian > 0m && currentSnapshot.Volume > (volMedian * 1.10m);
                    bool penetrated = penetrationTicksLocal >= 2;
                    tracker.SessionPressureSeen = volHigh && penetrated;
                }
                catch
                {
                }

                tracker.SessionPhase = ReversalPhase.Defense;
                tracker.SessionPhaseStartBar = currentSnapshot.Bar;
            }

            // ================================================================
            // NEUE ENTRY-LOGIK: Signalbar-Prüfung (UF→FA + Close + POC + Absorption)
            // ================================================================
            OvSnapshot? prev = null;
            if (history.TryGetByBar(currentSnapshot.Bar - 1, out var prevF) && prevF?.Snapshot != null)
                prev = prevF.Snapshot;
            else
                prev = GetPreviousClosedSnapshot(history, currentSnapshot.Bar, maxLookback: 20);

            OvSnapshot? prevPrev = null;
            if (history.TryGetByBar(currentSnapshot.Bar - 2, out var prevPrevF) && prevPrevF?.Snapshot != null)
                prevPrev = prevPrevF.Snapshot;
            else
                prevPrev = GetPreviousClosedSnapshotByOffset(history, currentSnapshot.Bar, offset: 2, maxLookback: 80);

            OvSnapshot? prev3 = null;
            if (history.TryGetByBar(currentSnapshot.Bar - 3, out var prev3F) && prev3F?.Snapshot != null)
                prev3 = prev3F.Snapshot;
            else
                prev3 = GetPreviousClosedSnapshotByOffset(history, currentSnapshot.Bar, offset: 3, maxLookback: 100);

            OvSnapshot? prev4 = null;
            if (history.TryGetByBar(currentSnapshot.Bar - 4, out var prev4F) && prev4F?.Snapshot != null)
                prev4 = prev4F.Snapshot;
            else
                prev4 = GetPreviousClosedSnapshotByOffset(history, currentSnapshot.Bar, offset: 4, maxLookback: 120);

            // --- Bisherige Kriterien weiterhin berechnen und loggen (KEIN Einfluss auf Entry) ---
            const int AdaptiveLookbackSession = 30;
            const decimal AbsNetDeltaMedianMultiplierSession = 1.0m;
            decimal absNetDeltaMinSession = GetAdaptiveAbsNetDeltaMin(history, currentSnapshot, AdaptiveLookbackSession, AbsNetDeltaMedianMultiplierSession);

            var logItems = new List<ScoreItem>(16);

            bool faAtZone = IsFinishedAuction(currentSnapshot, thresholds, _direction) && IsFinishedAuctionAtZone(currentSnapshot, zone, tickSize, _direction);
            if (faAtZone)
                tracker.SessionSawFaAtZone = true;
            logItems.Add(new ScoreItem { Key = "FA", Points = 0m, TextDe = faAtZone ? "Finished Auction an Zone: JA" : "Finished Auction an Zone: NEIN" });

            bool absorptionLog = false;
            AbsorptionPattern absorptionPatternLog = AbsorptionPattern.None;
            if (prev != null)
            {
                absorptionPatternLog = DetectAbsorptionPattern(prevPrev, prev, currentSnapshot, zone, tickSize, _direction, absNetDeltaMinSession, allowPatternC: true);

                // Historische Absorption (prev/prevPrev) zählt nur Pattern A/B.
                AbsorptionPattern absorptionPatternLogPrev = (prevPrev != null && prev3 != null)
                    ? DetectAbsorptionPattern(prev3, prevPrev, prev, zone, tickSize, _direction, absNetDeltaMinSession, allowPatternC: false)
                    : AbsorptionPattern.None;
                AbsorptionPattern absorptionPatternLogPrevPrev = (prevPrev != null && prev3 != null && prev4 != null)
                    ? DetectAbsorptionPattern(prev4, prev3, prevPrev, zone, tickSize, _direction, absNetDeltaMinSession, allowPatternC: false)
                    : AbsorptionPattern.None;

                bool hasAbsorptionInCurr = absorptionPatternLog != AbsorptionPattern.None;

                // Bestand (1 Bar): Wenn Pattern C auf der Umkehrkerze true war,
                // dann darf die restliche Signal-Logik im nächsten Bar nachziehen.
                bool hasCarryOverFromPatternC = tracker.SessionLastPatternCBar >= 0
                    && currentSnapshot.Bar == tracker.SessionLastPatternCBar + 1;

                absorptionPatternLog = hasAbsorptionInCurr
                    ? absorptionPatternLog
                    : (hasCarryOverFromPatternC ? AbsorptionPattern.C : (absorptionPatternLogPrev != AbsorptionPattern.None ? absorptionPatternLogPrev : absorptionPatternLogPrevPrev));

                absorptionLog = hasAbsorptionInCurr || hasCarryOverFromPatternC;
            }

            var proxEval = absorptionLog
                ? EvaluateAbsorptionProximity(currentSnapshot, zone, tickSize, _direction)
                : new ProximityEval { Factor = 0m, ReasonDe = "Zonennähe: n/v" };
            decimal absorptionPts = absorptionLog ? (2m * proxEval.Factor) : 0m;
            logItems.Add(new ScoreItem { Key = "Absorption", Points = absorptionPts, TextDe = absorptionLog ? $"Absorption({absorptionPatternLog}): JA, {proxEval.ReasonDe} -> +{absorptionPts:0.0}" : "Absorption: NEIN -> +0" });
            if (absorptionLog)
                tracker.SessionSawAbsorption = true;

            var sweepEval = EvaluateSweepPenetration(history, currentSnapshot, zone, tickSize, _direction);
            logItems.Add(new ScoreItem { Key = "Sweep", Points = sweepEval.Points, TextDe = sweepEval.TextDe });
            if (sweepEval.IsSweep)
                tracker.SessionSawSweep = true;

            if (prev != null)
            {
                bool prevUa = IsUnfinishedAuction(prev, thresholds, _direction);
                bool priceProgressOk = _direction == OrderDirections.Buy
                    ? prev.Low > currentSnapshot.Low
                    : prev.High < currentSnapshot.High;

                if (prevUa && faAtZone && priceProgressOk)
                {
                    tracker.SessionSawUaToFa = true;
                    if (tracker.SessionUaToFaBar < 0)
                        tracker.SessionUaToFaBar = currentSnapshot.Bar;
                }
            }
            logItems.Add(new ScoreItem { Key = "UA→FA", Points = tracker.SessionSawUaToFa ? 1m : 0m, TextDe = tracker.SessionSawUaToFa ? $"UA→FA: JA (Bar {tracker.SessionUaToFaBar}) -> +1" : "UA→FA: NEIN -> +0" });

            int touchesW;
            int faAtZoneW;
            bool progressOk;
            var pathNow = DetermineAllowPath(history, currentSnapshot, prev, zone, thresholds, tickSize, _direction, out touchesW, out faAtZoneW, out progressOk);
            bool multiFa = pathNow == AllowPath.MultiFaDefense;
            logItems.Add(new ScoreItem { Key = "MultiFA", Points = multiFa ? 1m : 0m, TextDe = multiFa ? "Multi-FA-Verteidigung: JA -> +1" : "Multi-FA-Verteidigung: NEIN -> +0" });
            if (multiFa)
                tracker.SessionSawMultiFaDefense = true;

            // ================================================================
            // HARTE ENTRY-KRITERIEN (neu):
            // 1) UF→FA: Vorgänger-Bar hatte UF, aktuelle Bar hat FA
            // 2) Close in Traderichtung (Long: Close > Open, Short: Close < Open)
            // 3) Kerzen-POC Position
            // 4) Absorption im Signalbar
            // ================================================================
            bool isSignalBar = false;
            string signalBlockReason = string.Empty;

            // Kriterium 1: UF→FA (Vorgänger hatte UF, dieses Bar hat FA)
            bool prevHadUf = prev != null && IsUnfinishedAuction(prev, thresholds, _direction);
            bool currHasFa = IsFinishedAuction(currentSnapshot, thresholds, _direction);
            bool priceProgressOkUfToFa = prev != null && (_direction == OrderDirections.Buy
                ? prev.Low > currentSnapshot.Low
                : prev.High < currentSnapshot.High);
            bool ufToFaHere = prevHadUf && currHasFa && priceProgressOkUfToFa;

            if (ufToFaHere && !tracker.SessionLatchUfToFa)
            {
                tracker.SessionLatchUfToFa = true;
                tracker.SessionLatchUfToFaBar = currentSnapshot.Bar;
            }

            bool ufToFaLatchOk = tracker.SessionLatchUfToFa;
            logItems.Add(new ScoreItem
            {
                Key = "Signal_UF→FA",
                Points = ufToFaLatchOk ? 1m : 0m,
                TextDe = ufToFaLatchOk
                    ? $"Signal UF→FA (Latch): JA (Bar {tracker.SessionLatchUfToFaBar})"
                    : $"Signal UF→FA (Latch): NEIN (Vorgänger UF={prevHadUf}, aktuell FA={currHasFa}, PriceProgress={priceProgressOkUfToFa})"
            });

            // Kriterium 2: Close in Traderichtung
            bool closeInDirection = _direction == OrderDirections.Buy
                ? currentSnapshot.Close > currentSnapshot.Open
                : currentSnapshot.Close < currentSnapshot.Open;
            logItems.Add(new ScoreItem
            {
                Key = "Signal_Close",
                Points = closeInDirection ? 1m : 0m,
                TextDe = closeInDirection
                    ? "Signal Close in Richtung: JA"
                    : $"Signal Close in Richtung: NEIN (O={currentSnapshot.Open:F2} C={currentSnapshot.Close:F2})"
            });

            AbsorptionPattern absCurr = AbsorptionPattern.None;
            AbsorptionPattern absPrev = AbsorptionPattern.None;
            AbsorptionPattern absPrevPrev = AbsorptionPattern.None;
            AbsorptionPattern absorptionPatternSignal = AbsorptionPattern.None;
            bool absorptionInSignal = false;
            bool rejectionWickAtZone = false;
            if (prev != null)
            {
                // Pattern C wird nur auf der Umkehrkerze (curr) akzeptiert.
                absCurr = DetectAbsorptionPattern(prevPrev, prev, currentSnapshot, zone, tickSize, _direction, absNetDeltaMinSession, allowPatternC: true);

                // Historische Absorption (prev/prevPrev) zählt nur Pattern A/B.
                absPrev = (prevPrev != null && prev3 != null)
                    ? DetectAbsorptionPattern(prev3, prevPrev, prev, zone, tickSize, _direction, absNetDeltaMinSession, allowPatternC: false)
                    : AbsorptionPattern.None;
                absPrevPrev = (prevPrev != null && prev3 != null && prev4 != null)
                    ? DetectAbsorptionPattern(prev4, prev3, prevPrev, zone, tickSize, _direction, absNetDeltaMinSession, allowPatternC: false)
                    : AbsorptionPattern.None;

                bool hasAbsorptionInCurr = absCurr != AbsorptionPattern.None;

                // Bestand (1 Bar): Wenn Pattern C auf der Umkehrkerze true war,
                // dann darf die restliche Signal-Logik im nächsten Bar nachziehen.
                bool hasCarryOverFromPatternC = tracker.SessionLastPatternCBar >= 0
                    && currentSnapshot.Bar == tracker.SessionLastPatternCBar + 1;

                absorptionPatternSignal = hasAbsorptionInCurr
                    ? absCurr
                    : (hasCarryOverFromPatternC ? AbsorptionPattern.C : (absPrev != AbsorptionPattern.None ? absPrev : absPrevPrev));

                absorptionInSignal = hasAbsorptionInCurr || hasCarryOverFromPatternC;
            }

            rejectionWickAtZone = IsRejectWickAtZone(currentSnapshot, zone, tickSize, _direction);
            absorptionInSignal = absorptionInSignal || rejectionWickAtZone;

            if (absorptionInSignal && !tracker.SessionLatchAbsorption)
            {
                tracker.SessionLatchAbsorption = true;
                tracker.SessionLatchAbsorptionBar = currentSnapshot.Bar;
            }

            bool absorptionLatchOk = tracker.SessionLatchAbsorption;

            // Kriterium 3: Kerzen-POC Position
            OvSnapshot pocBar = currentSnapshot;
            string pocBarText = "curr";
            bool requireCurrPocDelta = ufToFaHere && (absCurr == AbsorptionPattern.None) && ((absPrev != AbsorptionPattern.None) || (absPrevPrev != AbsorptionPattern.None));
            if (requireCurrPocDelta)
            {
                if (absPrev != AbsorptionPattern.None)
                {
                    pocBar = prev!;
                    pocBarText = "prev";
                }
                else if (absPrevPrev != AbsorptionPattern.None && prevPrev != null)
                {
                    pocBar = prevPrev;
                    pocBarText = "prevPrev";
                }
            }

            decimal barMid = (pocBar.High + pocBar.Low) / 2m;
            bool pocPositionOk;
            bool currPocDeltaOk = _direction == OrderDirections.Buy ? currentSnapshot.PocDelta > 0m : currentSnapshot.PocDelta < 0m;
            bool pocOkFinal;
            string pocPosText;
            decimal pocRange = pocBar.High - pocBar.Low;
            decimal pocBody = Math.Abs(pocBar.Close - pocBar.Open);
            decimal pocUpperWick = pocBar.High - Math.Max(pocBar.Open, pocBar.Close);
            decimal pocLowerWick = Math.Min(pocBar.Open, pocBar.Close) - pocBar.Low;
            const decimal RejBodyFrac = 0.30m;
            const decimal RejCloseFrac = 0.35m;
            const decimal RejWickFrac = 0.50m;
            const decimal RejPocMinFrac = 0.35m;
            if (_direction == OrderDirections.Buy)
            {
                // Long: POC muss in unterer Hälfte liegen (POC <= Mitte)
                pocPositionOk = pocBar.CandlePocPrice <= barMid;
                bool rejectionLong = false;
                bool pocOverrideOk = false;
                if (pocRange > 0m)
                {
                    bool isBull = pocBar.Close > pocBar.Open;
                    bool bodyOk = pocBody <= (RejBodyFrac * pocRange);
                    bool closeOk = (pocBar.High - pocBar.Close) <= (RejCloseFrac * pocRange);
                    bool wickOk = pocLowerWick >= (RejWickFrac * pocRange);
                    rejectionLong = isBull && bodyOk && closeOk && wickOk;
                    pocOverrideOk = rejectionLong && (pocBar.CandlePocPrice <= (pocBar.High - (RejPocMinFrac * pocRange)));
                }

                bool pocOk = pocPositionOk || pocOverrideOk;
                pocOkFinal = pocOk && (!requireCurrPocDelta || currPocDeltaOk);
                pocPosText = $"POC[{pocBarText}]={pocBar.CandlePocPrice:F2}, Mitte={barMid:F2}, Soll: untere Hälfte → {(pocPositionOk ? "JA" : "NEIN")}";
                if (!pocPositionOk && pocOverrideOk)
                    pocPosText += $" | Override=RejectionLong (Body={pocBody:F2}, Range={pocRange:F2}, WickL={pocLowerWick:F2})";
                if (requireCurrPocDelta)
                    pocPosText += $" | PocDelta[curr]={currentSnapshot.PocDelta:+0;-0;0} → {(currPocDeltaOk ? "JA" : "NEIN")}";
            }
            else
            {
                // Short: POC muss in oberer Hälfte liegen (POC >= Mitte)
                pocPositionOk = pocBar.CandlePocPrice >= barMid;
                bool rejectionShort = false;
                bool pocOverrideOk = false;
                if (pocRange > 0m)
                {
                    bool isBear = pocBar.Close < pocBar.Open;
                    bool bodyOk = pocBody <= (RejBodyFrac * pocRange);
                    bool closeOk = (pocBar.Close - pocBar.Low) <= (RejCloseFrac * pocRange);
                    bool wickOk = pocUpperWick >= (RejWickFrac * pocRange);
                    rejectionShort = isBear && bodyOk && closeOk && wickOk;
                    pocOverrideOk = rejectionShort && (pocBar.CandlePocPrice >= (pocBar.Low + (RejPocMinFrac * pocRange)));
                }

                bool pocOk = pocPositionOk || pocOverrideOk;
                pocOkFinal = pocOk && (!requireCurrPocDelta || currPocDeltaOk);
                pocPosText = $"POC[{pocBarText}]={pocBar.CandlePocPrice:F2}, Mitte={barMid:F2}, Soll: obere Hälfte → {(pocPositionOk ? "JA" : "NEIN")}";
                if (!pocPositionOk && pocOverrideOk)
                    pocPosText += $" | Override=RejectionShort (Body={pocBody:F2}, Range={pocRange:F2}, WickU={pocUpperWick:F2})";
                if (requireCurrPocDelta)
                    pocPosText += $" | PocDelta[curr]={currentSnapshot.PocDelta:+0;-0;0} → {(currPocDeltaOk ? "JA" : "NEIN")}";
            }
            logItems.Add(new ScoreItem { Key = "Signal_POC", Points = pocOkFinal ? 1m : 0m, TextDe = $"Signal POC-Position: {pocPosText}" });

            if (pocOkFinal && !tracker.SessionLatchPoc)
            {
                tracker.SessionLatchPoc = true;
                tracker.SessionLatchPocBar = currentSnapshot.Bar;
            }

            bool pocLatchOk = tracker.SessionLatchPoc;
            if (pocLatchOk)
            {
                logItems.Add(new ScoreItem
                {
                    Key = "Signal_POC_Latch",
                    Points = 1m,
                    TextDe = $"Signal POC (Latch): JA (Bar {tracker.SessionLatchPocBar})"
                });
            }

            // Kriterium 4: Absorption im Signalbar
            string absDbg = string.Empty;
            if (!absorptionInSignal && prev != null)
                absDbg = GetAbsorptionDebugText(prevPrev, prev, currentSnapshot, zone, tickSize, _direction, absNetDeltaMinSession);
            logItems.Add(new ScoreItem
            {
                Key = "Signal_Absorption",
                Points = absorptionLatchOk ? 1m : 0m,
                TextDe = absorptionLatchOk
                    ? $"Signal Absorption (Latch): JA (Bar {tracker.SessionLatchAbsorptionBar})"
                    : (string.IsNullOrEmpty(absDbg) ? "Signal Absorption: NEIN" : $"Signal Absorption: NEIN | {absDbg}")
            });

            // Alle 4 Kriterien müssen erfüllt sein
            isSignalBar = ufToFaLatchOk && pocLatchOk && absorptionLatchOk && closeInDirection;

            if (!isSignalBar)
            {
                var missing = new List<string>(4);
                if (!ufToFaLatchOk) missing.Add("UF→FA");
                if (!closeInDirection) missing.Add("Close in Richtung");
                if (!pocLatchOk) missing.Add("POC-Position");
                if (!absorptionLatchOk) missing.Add("Absorption");
                signalBlockReason = $"Kein Signalbar: fehlend [{string.Join(", ", missing)}]";
            }

            // Score für Logging (Summe der Info-Punkte, NICHT entry-relevant)
            decimal logScore = logItems.Sum(x => x.Points);
            logItems.Add(new ScoreItem { Key = "Signal_Check", Points = isSignalBar ? 1m : 0m, TextDe = isSignalBar ? "★ SIGNALBAR ERKANNT → Entry GO" : signalBlockReason });

            decimal confidence = isSignalBar ? 0.75m : 0m;
            var dec = new DecisionResult
            {
                Allowed = true,
                Entry = isSignalBar,
                Path = tracker.SessionSawUaToFa ? AllowPath.UaToFa : AllowPath.None,
                BaseScore = 0,
                TotalScore = logScore,
                Confidence = isSignalBar ? confidence : 0m,
                BlockReasonDe = isSignalBar ? string.Empty : signalBlockReason,
                Items = logItems
            };
            decision = dec;

            tracker.SessionDecisionHistory ??= new List<DecisionResult>();
            tracker.SessionDecisionHistory.Add(dec);
            tracker.SessionDecisionBars ??= new List<int>();
            tracker.SessionDecisionBars.Add(currentSnapshot.Bar);
            tracker.SessionDecisionPhases ??= new List<ReversalPhase>();
            tracker.SessionDecisionPhases.Add(ReversalPhase.Defense);

            if (dec.TotalScore > tracker.SessionBestScore)
            {
                tracker.SessionBestScore = dec.TotalScore;
                tracker.SessionBestScoreItems = dec.Items != null ? new List<ScoreItem>(dec.Items) : null;
            }

            if (isSignalBar)
            {
                tracker.SessionAbsorptionConfirmed = true;
                tracker.SessionAbsorptionBar = currentSnapshot.Bar;
                tracker.SessionAbsorptionPocPrice = currentSnapshot.CandlePocPrice;
                tracker.SessionActive = false;
                tracker.SessionEndReason = $"GO – Signalbar erkannt bei Session-Bar {sessionBarNr}.";
                return SessionEvalOutcome.EntryGo;
            }

            return SessionEvalOutcome.Continue;
        }

        private void LogSessionProtocol(
            ZoneTracker tracker,
            MarketStructureContext.Zone zone,
            OfFeaturesHistory history,
            OvSnapshot currentSnapshot,
            OrderflowThresholds thresholds,
            decimal tickSize,
            int maxSessionBars,
            int maxConsecutiveBadCloses,
            int maxSessionPenetrationTicks,
            string outcome,
            DecisionResult? finalDecision)
        {
            try
            {
                if (_loggerSource == null)
                    return;

                var lines = new List<string>(96);
                string dirDe = _direction == OrderDirections.Buy ? "BULLISCH (Kauf)" : "BÄRISCH (Verkauf)";
                int endBarForReport = currentSnapshot.Bar;
                if (tracker.SessionLastEvalBar >= tracker.SessionStartBar && tracker.SessionLastEvalBar <= currentSnapshot.Bar)
                    endBarForReport = tracker.SessionLastEvalBar;
                int sessionBars = (endBarForReport - tracker.SessionStartBar) + 1;
                string zoneStatusDe = zone.IsConfirmed ? "BESTÄTIGT" : "PENDING";
                string phaseDe = tracker.SessionPhase == ReversalPhase.Defense
                    ? "Verteidigung"
                    : (tracker.SessionPhase == ReversalPhase.Touch ? "Touch" : "Bestätigung");

                lines.Add("═══════════════════════════════════════════════════════════");
                lines.Add($"REVERSAL-REPORT | {dirDe} | Zone #{zone.Id} | {zoneStatusDe}");
                lines.Add($"Bereich: {zone.Low:F2} bis {zone.High:F2} | Dauer: {sessionBars} Bars");
                if (tracker.SessionActive && tracker.SessionSawUaToFa && tracker.SessionUaToFaBar >= 0)
                    lines.Add($"UA→FA: gesehen in Session (Latch) bei {FormatBarLabel(tracker.SessionUaToFaBar, history)}");
                lines.Add($"PHASE: {phaseDe} | Druck vorhanden={(tracker.SessionPressureSeen ? "JA" : "NEIN")} | strongDefense={tracker.SessionStrongDefenseCount}/2");

                if (tracker.SessionAbsorptionConfirmed)
                    lines.Add($"ABSORPTION: bestätigt bei {FormatBarLabel(tracker.SessionAbsorptionBar, history)} | AbsorptionPOC={tracker.SessionAbsorptionPocPrice:F2}");
                lines.Add($"Szenario: {tracker.SessionKind} | Status: {outcome.ToUpperInvariant()}");
                lines.Add("═══════════════════════════════════════════════════════════");
                lines.Add(string.Empty);

                lines.Add("STORYLINE:");
                int startBar = tracker.SessionStartBar;
                int endBar = endBarForReport;
                OvSnapshot? prevSnap = null;
                OvSnapshot? prevPrevSnap = null;
                int barNumber = 0;

                int confirmStartBar = tracker.SessionAbsorptionConfirmed
                    ? (tracker.SessionAbsorptionBar + 1)
                    : int.MaxValue;
                bool hasDefenseChapter = endBar >= (startBar + 1);
                bool hasConfirmChapter = tracker.SessionAbsorptionConfirmed && confirmStartBar <= endBar;

                for (int b = startBar; b <= endBar; b++)
                {
                    barNumber++;

                    if (b == startBar + 1 && hasDefenseChapter)
                    {
                        lines.Add(string.Empty);
                        lines.Add("VERTEIDIGUNG:");
                    }
                    if (b == confirmStartBar && hasConfirmChapter)
                    {
                        lines.Add(string.Empty);
                        lines.Add("BESTÄTIGUNG:");
                    }

                    OvSnapshot? s = null;
                    if (b == currentSnapshot.Bar)
                        s = currentSnapshot;
                    else if (history.TryGetByBar(b, out var f) && f?.Snapshot != null)
                    {
                        s = f.Snapshot;
                    }

                    if (s == null)
                        continue;

                    string color = s.Close >= s.Open ? "↑" : "↓";
                    string barLabel = s.ChartBarNumber > 0 ? $"K{s.ChartBarNumber}" : $"B{b}";
                    bool closeInZone = s.Close >= zone.Low && s.Close <= zone.High;

                    int chartBar = s.ChartBarNumber;

                    bool hasDecision = false;
                    decimal barScore = 0m;
                    ReversalPhase barPhase = ReversalPhase.Touch;
                    DecisionResult? barDecision = null;
                    if (tracker.SessionDecisionHistory != null && tracker.SessionDecisionBars != null)
                    {
                        int minCount = Math.Min(tracker.SessionDecisionHistory.Count, tracker.SessionDecisionBars.Count);
                        if (tracker.SessionDecisionPhases != null)
                            minCount = Math.Min(minCount, tracker.SessionDecisionPhases.Count);

                        for (int i = 0; i < minCount; i++)
                        {
                            if (tracker.SessionDecisionBars[i] == b)
                            {
                                barScore = tracker.SessionDecisionHistory[i].TotalScore;
                                barDecision = tracker.SessionDecisionHistory[i];
                                if (tracker.SessionDecisionPhases != null && i < tracker.SessionDecisionPhases.Count)
                                    barPhase = tracker.SessionDecisionPhases[i];
                                hasDecision = true;
                                break;
                            }
                        }
                    }

                    string pos;
                    if (closeInZone)
                    {
                        pos = "IN Zone";
                    }
                    else
                    {
                        if (_direction == OrderDirections.Buy)
                            pos = s.Close > zone.High ? "AUSBRUCH" : "UNTER Zone";
                        else
                            pos = s.Close < zone.Low ? "AUSBRUCH" : "ÜBER Zone";
                    }

                    string phaseLabel = hasDecision
                        ? (barPhase == ReversalPhase.Confirm
                            ? "[Bestätigung]"
                            : (b == tracker.SessionStartBar ? "[TouchVerteidigung]" : "[Verteidigung]"))
                        : (barNumber == 1 ? "[Touch]" : "");

                    var checkedParts = new List<string>(8);
                    if (hasDecision && barDecision?.Items != null)
                    {
                        foreach (var it in barDecision.Items)
                        {
                            if (it == null) continue;
                            if (string.IsNullOrEmpty(it.Key)) continue;
                            if (!it.Key.StartsWith("Signal_", StringComparison.Ordinal)) continue;

                            if (it.Key == "Signal_Check")
                            {
                                if (barDecision.Entry)
                                    checkedParts.Add("SIGNAL");
                                continue;
                            }

                            string shortKey = it.Key.Replace("Signal_", string.Empty, StringComparison.Ordinal);
                            checkedParts.Add($"{shortKey}({(it.Points > 0m ? "+" : "0")})");
                        }
                    }

                    string checks = checkedParts.Count > 0 ? " | " + string.Join(" ", checkedParts) : string.Empty;
                    lines.Add($"{barLabel}: {color} Δ{s.PocDelta:+0;-0;0} | {pos.PadRight(10)} | {phaseLabel}{checks}".TrimEnd());

                    prevPrevSnap = prevSnap;
                    prevSnap = s;
                }

                lines.Add(string.Empty);
                string emoji = (finalDecision?.Entry == true) ? "🟢 GO" : "🔴 NO-GO";
                lines.Add($"FAZIT: {emoji}");

                if (finalDecision?.Entry != true)
                {
                    string grund = finalDecision?.BlockReasonDe ?? tracker.SessionEndReason ?? "Zu wenig Bestätigung";
                    if (!string.IsNullOrEmpty(grund) && grund.StartsWith("Session abgelaufen", StringComparison.OrdinalIgnoreCase))
                    {
                        var lastMissing = tracker.SessionDecisionHistory?
                            .LastOrDefault(x => x != null && !string.IsNullOrEmpty(x.BlockReasonDe) && x.BlockReasonDe.StartsWith("Kein Signalbar", StringComparison.Ordinal));
                        if (!string.IsNullOrEmpty(lastMissing?.BlockReasonDe))
                            grund = lastMissing!.BlockReasonDe;
                    }
                    lines.Add($"GRUND FÜR ABLEHNUNG: {grund}");
                }
                else
                {
                    lines.Add("ENTSCHEIDUNG: Der Trend hat offiziell gedreht. Einstieg legitimiert.");
                }

                lines.Add("═══════════════════════════════════════════════════════════");

                LogExplainMultilineOnce(
                    currentSnapshot.Bar,
                    zone.Id,
                    stage: "SessionProtocol",
                    lines: lines);
            }
            catch
            {
                // ignore logging failures
            }
        }

        private ZoneTracker GetOrCreateTracker(int zoneId, int bar)
        {
            if (_trackersByZoneId.TryGetValue(zoneId, out var t))
                return t;

            t = new ZoneTracker
            {
                ZoneId = zoneId,
                FirstRealTouchBar = bar,
                FirstTouchBar = bar,
                LastTouchBar = -999,
                FirstTouchTime = DateTime.MinValue,
                Confirmed = false,
                ConfirmedBar = -1,
                ImmediateAttempted = false,
                ImmediateDisabled = false,
                FirstOutsideBar = -1,
                MaxOutsideExtensionTicks = 0,
                RetestAttempted = false,
                RetestBar = -1,
                MaxFavorableExcursionPrice = 0m,
                EntryTriggered = false,
                LastStoryLoggedBar = -1,
                StoryPending = false,
                StoryEmitted = false,
                LastTouchedBar = bar,
                StoryExitBar = -1,
                // Multi-Bar Session
                SessionActive = false,
                SessionKind = SessionType.None,
                SessionStartBar = -1,
                SessionStartChartBar = -1,
                SessionLastEvalBar = -1,
                SessionTouchLow = 0m,
                SessionTouchHigh = 0m,
                ConsecutiveBadCloses = 0,
                SessionMaxPenetrationTicks = 0,
                SessionBestScore = 0m,
                SessionBestScoreItems = null,
                SessionDecisionHistory = null,
                SessionEndReason = null,
                LastKnownZoneLow = 0m,
                LastKnownZoneHigh = 0m,
                LastKnownZoneType = MarketStructureContext.ZoneType.Support,
                LastKnownZoneConfirmed = false,
                SessionSawFaAtZone = false,
                SessionAbsorptionConfirmed = false,
                SessionAbsorptionBar = -1,
                SessionAbsorptionPocPrice = 0m,
                SessionLastPatternCBar = -1,
                SessionSawUaToFa = false,
                SessionUaToFaBar = -1,
                SessionLatchUfToFa = false,
                SessionLatchUfToFaBar = -1,
                SessionLatchClose = false,
                SessionLatchCloseBar = -1,
                SessionLatchCloseBrokenBar = -1,
                SessionLatchPoc = false,
                SessionLatchPocBar = -1,
                SessionLatchAbsorption = false,
                SessionLatchAbsorptionBar = -1,
                SessionSawExhaustion = false,
            };

            _trackersByZoneId[zoneId] = t;
            return t;
        }

        private static string FormatBarRef(OfFeaturesHistory history, int barIndex)
        {
            if (barIndex < 0)
                return "n/v";

            int chart = barIndex + 1;
            int sess = 0;
            try
            {
                if (history != null && history.TryGetByBar(barIndex, out var f) && f?.Snapshot != null)
                {
                    var s = f.Snapshot;
                    if (s.ChartBarNumber > 0)
                        chart = s.ChartBarNumber;
                    sess = s.SessionBarNumber;
                }
            }
            catch { }

            return sess > 0
                ? $"Chart {chart} (Sess {sess}), BarIdx {barIndex}"
                : $"Chart {chart}, BarIdx {barIndex}";
        }

        private static string FormatBarLabel(int barIndex, OfFeaturesHistory history)
        {
            if (barIndex < 0)
                return "n/v";

            int chart = barIndex + 1;
            try
            {
                if (history != null && history.TryGetByBar(barIndex, out var f) && f?.Snapshot != null)
                {
                    var s = f.Snapshot;
                    if (s.ChartBarNumber > 0)
                        chart = s.ChartBarNumber;
                }
            }
            catch { }

            return $"K{chart}";
        }

        private static bool TouchesZone(OvSnapshot s, MarketStructureContext.Zone z)
        {
            return s.High >= z.Low && s.Low <= z.High;
        }

        private static bool HasMovedAwayFromZone(OvSnapshot s, MarketStructureContext.Zone z, decimal tickSize)
        {
            int zoneHeightTicks = (int)Math.Round(Math.Abs(z.High - z.Low) / tickSize, MidpointRounding.AwayFromZero);
            int xTicks = Math.Max(zoneHeightTicks, 4);
            decimal away = xTicks * tickSize;

            if (z.Type == MarketStructureContext.ZoneType.Support)
            {
                // For support: move away = price pushed above zone top by X ticks
                return s.High >= z.High + away;
            }

            // Resistance: move away = price pushed below zone low by X ticks
            return s.Low <= z.Low - away;
        }

        private static bool TryCountAwayClosesSinceFirstTouch(
            OfFeaturesHistory history,
            int firstTouchBar,
            int currentBar,
            MarketStructureContext.Zone zone,
            OrderDirections dir,
            out int awayCloses)
        {
            awayCloses = 0;
            if (history == null)
                return false;

            // We only count bars strictly AFTER the first touch and strictly BEFORE the current bar.
            // currentBar is the potential retest touch bar.
            int start = firstTouchBar + 1;
            int end = currentBar - 1;
            if (end < start)
                return true;

            // We count only CONSECUTIVE closes outside the zone immediately BEFORE the potential retest bar.
            // This matches the intuitive idea: "the market moved away and stayed away for N closes".
            for (int b = end; b >= start; b--)
            {
                if (!history.TryGetByBar(b, out var f) || f?.Snapshot == null)
                    continue;

                var s = f.Snapshot;

                bool isAwayClose = dir == OrderDirections.Buy
                    ? (s.Close > zone.High)
                    : (s.Close < zone.Low);

                if (!isAwayClose)
                    break;

                awayCloses++;
            }

            return true;
        }

        private static bool IsFinishedAuction(OvSnapshot s, OrderflowThresholds th, OrderDirections dir)
        {
            if (dir == OrderDirections.Buy)
                return s.AskAtLow <= th.FinishedAuctionMaxAskAtLow;

            return s.BidAtHigh <= th.FinishedAuctionMaxBidAtHigh;
        }

        private static bool IsUnfinishedAuction(OvSnapshot s, OrderflowThresholds th, OrderDirections dir)
        {
            if (dir == OrderDirections.Buy)
                return s.AskAtLow > th.FinishedAuctionMaxAskAtLow;

            return s.BidAtHigh > th.FinishedAuctionMaxBidAtHigh;
        }

        private static bool HasDeltaFlipWithinWindow(OfFeaturesHistory history, OvSnapshot curr, int windowBars, OrderDirections dir)
        {
            if (history == null || curr == null)
                return false;

            int wantSign = dir == OrderDirections.Buy ? 1 : -1;
            decimal currV = curr.PocDelta;
            if (wantSign == 1 && currV <= 0m) return false;
            if (wantSign == -1 && currV >= 0m) return false;

            int needPrev = Math.Max(1, windowBars - 1);
            var prevs = new List<OvSnapshot>(needPrev);
            for (int i = 0; i < Math.Min(history.Count, 600); i++)
            {
                var s = history.GetOfFeatures(i)?.Snapshot;
                if (s == null) continue;
                if (s.Bar >= curr.Bar) continue;
                prevs.Add(s);
                if (prevs.Count >= needPrev) break;
            }

            if (prevs.Count == 0)
                return false;

            // prevs currently newest->older; we want chronological older->newer for adjacent flip checks.
            prevs.Reverse();

            // Check flip between last prev and curr.
            decimal lastPrev = prevs[^1].PocDelta;
            bool flippedToCurr = dir == OrderDirections.Buy
                ? (lastPrev < 0m && currV > 0m)
                : (lastPrev > 0m && currV < 0m);
            if (flippedToCurr)
                return true;

            // Allow earlier flip inside window: check any adjacent prev->prev flip.
            decimal last = prevs[0].PocDelta;
            for (int j = 1; j < prevs.Count; j++)
            {
                decimal v = prevs[j].PocDelta;
                bool stepFlip = dir == OrderDirections.Buy ? (last < 0m && v > 0m) : (last > 0m && v < 0m);
                if (stepFlip)
                    return true;
                last = v;
            }

            return false;
        }

        private static bool IsFinishedAuctionAtZone(OvSnapshot s, MarketStructureContext.Zone z, decimal tickSize, OrderDirections dir)
        {
            if (dir == OrderDirections.Buy)
                return s.Close >= z.Low;

            return s.Close <= z.High;
        }

        private static bool IsRejectWickAtZone(OvSnapshot s, MarketStructureContext.Zone z, decimal tickSize, OrderDirections dir)
        {
            decimal oneTick = tickSize > 0m ? tickSize : 0.25m;

            decimal range = s.High - s.Low;
            if (range <= 0m)
                return false;

            decimal body = Math.Abs(s.Close - s.Open);
            decimal upperWick = s.High - Math.Max(s.Open, s.Close);
            decimal lowerWick = Math.Min(s.Open, s.Close) - s.Low;

            const decimal MinWickFrac = 0.50m;
            const decimal MaxBodyFrac = 0.35m;

            const int MaxPenetrationTicks = 6;
            const int MinReclaimTicks = 2;

            bool bodyOk = (body / range) <= MaxBodyFrac;
            if (!bodyOk)
                return false;

            if (dir == OrderDirections.Buy)
            {
                bool touches = s.Low <= z.High && s.Low >= (z.Low - (oneTick * MaxPenetrationTicks));
                if (!touches)
                    return false;

                bool wickOk = (lowerWick / range) >= MinWickFrac;
                if (!wickOk)
                    return false;

                bool reclaimed = s.Close >= (z.Low + (oneTick * MinReclaimTicks));
                if (!reclaimed)
                    return false;

                return true;
            }

            bool touchesS = s.High >= z.Low && s.High <= (z.High + (oneTick * MaxPenetrationTicks));
            if (!touchesS)
                return false;

            bool wickOkS = (upperWick / range) >= MinWickFrac;
            if (!wickOkS)
                return false;

            bool reclaimedS = s.Close <= (z.High - (oneTick * MinReclaimTicks));
            if (!reclaimedS)
                return false;

            return true;
        }

        private static bool ImbalanceNoFollowThrough(
            OvSnapshot prev,
            OvSnapshot curr,
            MarketStructureContext.Zone z,
            decimal tickSize,
            OrderDirections dir,
            decimal absNetDeltaMin)
        {
            decimal oneTick = tickSize;

            const decimal MinDeltaPerVol = 0.15m;
            const decimal MinExtremeDomRatio = 0.65m;
            const decimal MinExtremeShare = 0.05m;
            const decimal AbsDeltaMultiplier = 1.50m;
            const decimal MinRecoveryRatio = 0.40m;
            const int MaxSweepTicks = 3;

            decimal vol = curr.Volume;
            if (vol <= 0m)
                vol = 1m;

            decimal deltaPerVol = Math.Abs(curr.NetDeltaTotal) / vol;
            decimal absDelta = Math.Abs(curr.NetDeltaTotal);
            bool hasStrongDeltaRatio = deltaPerVol >= MinDeltaPerVol;
            bool hasStrongAbsDelta = absDelta >= (absNetDeltaMin * AbsDeltaMultiplier);
            if (!hasStrongDeltaRatio && !hasStrongAbsDelta)
                return false;

            decimal candleHeight = curr.High - curr.Low;
            if (candleHeight <= 0m)
                candleHeight = oneTick;

            if (dir == OrderDirections.Buy)
            {
                bool hasSellImb = curr.StackedSellImbBottomCount > 0 && curr.NetDeltaTotal < 0m;
                if (!hasSellImb)
                    return false;

                decimal exBid = curr.BidAtLow;
                decimal exAsk = curr.AskAtLow;
                decimal exTot = exBid + exAsk;
                if (exTot <= 0m)
                    return false;
                decimal exShare = exTot / vol;
                if (exShare < MinExtremeShare)
                    return false;
                decimal dom = exBid / exTot;
                if (dom < MinExtremeDomRatio)
                    return false;

                bool noFurtherDownByPrev = curr.Low >= prev.Low - (oneTick * MaxSweepTicks);
                bool noFurtherDownByZone = curr.Low >= z.Low - (oneTick * MaxSweepTicks);
                bool noFurtherDown = noFurtherDownByPrev || noFurtherDownByZone;

                bool goodRecovery = ((curr.Close - curr.Low) / candleHeight) >= MinRecoveryRatio;

                decimal zoneHeight = z.High - z.Low;
                bool rejectionCandle = curr.Close >= (curr.Low + candleHeight * 0.50m);
                bool rejectionZone = zoneHeight > 0m && curr.Close >= (z.Low + zoneHeight * 0.33m);
                bool rejection = rejectionCandle || rejectionZone;

                return noFurtherDown && goodRecovery && rejection;
            }

            bool hasBuyImb = curr.StackedBuyImbTopCount > 0 && curr.NetDeltaTotal > 0m;
            if (!hasBuyImb)
                return false;

            decimal exAskS = curr.AskAtHigh;
            decimal exBidS = curr.BidAtHigh;
            decimal exTotS = exBidS + exAskS;
            if (exTotS <= 0m)
                return false;
            decimal exShareS = exTotS / vol;
            if (exShareS < MinExtremeShare)
                return false;
            decimal domS = exAskS / exTotS;
            if (domS < MinExtremeDomRatio)
                return false;

            bool noFurtherUpByPrev = curr.High <= prev.High + (oneTick * MaxSweepTicks);
            bool noFurtherUpByZone = curr.High <= z.High + (oneTick * MaxSweepTicks);
            bool noFurtherUp = noFurtherUpByPrev || noFurtherUpByZone;

            bool goodRecoveryS = ((curr.High - curr.Close) / candleHeight) >= MinRecoveryRatio;

            decimal zoneHeightS = z.High - z.Low;
            bool rejectionCandleS = curr.Close <= (curr.High - candleHeight * 0.50m);
            bool rejectionZoneS = zoneHeightS > 0m && curr.Close <= (z.High - zoneHeightS * 0.33m);
            bool rejectionS = rejectionCandleS || rejectionZoneS;

            return noFurtherUp && goodRecoveryS && rejectionS;
        }

        private static AbsorptionPattern DetectAbsorptionPattern(
            OvSnapshot prev,
            OvSnapshot curr,
            MarketStructureContext.Zone z,
            decimal tickSize,
            OrderDirections dir,
            decimal absNetDeltaMin)
        {
            return DetectAbsorptionPattern(prevPrev: null, prev: prev, curr: curr, z: z, tickSize: tickSize, dir: dir, absNetDeltaMin: absNetDeltaMin);
        }

        private static AbsorptionPattern DetectAbsorptionPattern(
            OvSnapshot? prevPrev,
            OvSnapshot prev,
            OvSnapshot curr,
            MarketStructureContext.Zone z,
            decimal tickSize,
            OrderDirections dir,
            decimal absNetDeltaMin)
        {
            return DetectAbsorptionPattern(prevPrev, prev, curr, z, tickSize, dir, absNetDeltaMin, allowPatternC: true);
        }

        private static AbsorptionPattern DetectAbsorptionPattern(
            OvSnapshot? prevPrev,
            OvSnapshot prev,
            OvSnapshot curr,
            MarketStructureContext.Zone z,
            decimal tickSize,
            OrderDirections dir,
            decimal absNetDeltaMin,
            bool allowPatternC)
        {
            if (ImbalanceNoFollowThrough(prev, curr, z, tickSize, dir, absNetDeltaMin))
                return AbsorptionPattern.A;
            if (PushFlipAbsorption(prevPrev, prev, curr, z, tickSize, dir, absNetDeltaMin))
                return AbsorptionPattern.B;
            if (allowPatternC && BidAbsorptionWithoutDeltaFlip(prev, curr, z, tickSize, dir))
                return AbsorptionPattern.C;
            return AbsorptionPattern.None;
        }

        private static bool BidAbsorptionWithoutDeltaFlip(
            OvSnapshot prev,
            OvSnapshot curr,
            MarketStructureContext.Zone z,
            decimal tickSize,
            OrderDirections dir)
        {
            decimal oneTick = tickSize;

            const decimal MinBidDomAtExtreme = 0.58m;
            const decimal MinRecoveryRatio = 0.35m;
            const decimal MinExtremeShare = 0.03m;
            const int MaxSweepTicks = 2;
            const int MinPrevRangeTicks = 3;

            decimal vol = curr.Volume;
            if (vol <= 0m)
                vol = 1m;

            decimal candleHeight = curr.High - curr.Low;
            if (candleHeight <= 0m)
                candleHeight = oneTick;

            int prevRangeTicks = RoundTicks(prev.High - prev.Low, oneTick);

            if (dir == OrderDirections.Buy)
            {
                bool prevBearishPush = prev.Close < prev.Open && prevRangeTicks >= MinPrevRangeTicks;
                if (!prevBearishPush)
                    return false;

                decimal exBid = curr.BidAtLow;
                decimal exAsk = curr.AskAtLow;
                decimal exTot = exBid + exAsk;
                if (exTot <= 0m)
                    return false;

                decimal dom = exBid / exTot;
                if (dom < MinBidDomAtExtreme)
                    return false;

                if ((exTot / vol) < MinExtremeShare)
                    return false;

                bool noFurtherDownByPrev = curr.Low >= prev.Low - (oneTick * MaxSweepTicks);
                bool noFurtherDownByZone = curr.Low >= z.Low - (oneTick * MaxSweepTicks);
                if (!(noFurtherDownByPrev || noFurtherDownByZone))
                    return false;

                decimal recoveryRatio = (curr.Close - curr.Low) / candleHeight;
                if (recoveryRatio < MinRecoveryRatio)
                    return false;

                return true;
            }

            bool prevBullishPush = prev.Close > prev.Open && prevRangeTicks >= MinPrevRangeTicks;
            if (!prevBullishPush)
                return false;

            decimal exAskS = curr.AskAtHigh;
            decimal exBidS = curr.BidAtHigh;
            decimal exTotS = exBidS + exAskS;
            if (exTotS <= 0m)
                return false;

            decimal domS = exAskS / exTotS;
            if (domS < MinBidDomAtExtreme)
                return false;

            if ((exTotS / vol) < MinExtremeShare)
                return false;

            bool noFurtherUpByPrev = curr.High <= prev.High + (oneTick * MaxSweepTicks);
            bool noFurtherUpByZone = curr.High <= z.High + (oneTick * MaxSweepTicks);
            if (!(noFurtherUpByPrev || noFurtherUpByZone))
                return false;

            decimal recoveryRatioS = (curr.High - curr.Close) / candleHeight;
            if (recoveryRatioS < MinRecoveryRatio)
                return false;

            return true;
        }

        private static bool PushFlipAbsorption(
            OvSnapshot prev,
            OvSnapshot curr,
            MarketStructureContext.Zone z,
            decimal tickSize,
            OrderDirections dir,
            decimal absNetDeltaMin)
        {
            return PushFlipAbsorption(prevPrev: null, prev: prev, curr: curr, z: z, tickSize: tickSize, dir: dir, absNetDeltaMin: absNetDeltaMin);
        }

        private static bool PushFlipAbsorption(
            OvSnapshot? prevPrev,
            OvSnapshot prev,
            OvSnapshot curr,
            MarketStructureContext.Zone z,
            decimal tickSize,
            OrderDirections dir,
            decimal absNetDeltaMin)
        {
            decimal oneTick = tickSize;
            if (absNetDeltaMin < 0m)
                absNetDeltaMin = 0m;

            const decimal AbsDeltaMultiplier = 1.00m;
            const decimal MinRecoveryRatio = 0.35m;
            const decimal MinRecoveryRatioPenetration = 0.55m;
            const decimal MinExtremeDomRatio = 0.55m;
            const decimal MinExtremeShare = 0.02m;
            const int PushWindowSweepTicks = 2;

            decimal vol = curr.Volume;
            if (vol <= 0m)
                vol = 1m;

            decimal candleHeight = curr.High - curr.Low;
            if (candleHeight <= 0m)
                candleHeight = oneTick;

            if (dir == OrderDirections.Buy)
            {
                decimal needPushDelta = absNetDeltaMin * 1.00m;
                bool IsBearishPush(OvSnapshot s) => s.Close < s.Open && s.NetDeltaTotal <= -needPushDelta;

                OvSnapshot pushBar = prev;
                bool usingPrevPrev = false;
                if (!IsBearishPush(prev))
                {
                    if (prevPrev == null || !IsBearishPush(prevPrev))
                        return false;
                    pushBar = prevPrev;
                    usingPrevPrev = true;
                }

                decimal needDelta = absNetDeltaMin * AbsDeltaMultiplier;
                if (curr.NetDeltaTotal < needDelta)
                    return false;

                decimal exBid = curr.BidAtLow;
                decimal exAsk = curr.AskAtLow;
                decimal exTot = exBid + exAsk;
                if (exTot <= 0m)
                    return false;
                if ((exTot / vol) < MinExtremeShare)
                    return false;

                decimal dom = exBid / exTot;
                if (dom < MinExtremeDomRatio)
                    return false;

                if (usingPrevPrev)
                {
                    bool midOk = prev.Low >= pushBar.Low - (oneTick * PushWindowSweepTicks);
                    if (!midOk)
                        return false;
                }

                bool currBullish = curr.Close > curr.Open;
                if (!currBullish)
                    return false;

                decimal refLow = Math.Max(pushBar.Low, z.Low);
                bool penetrated = curr.Low < refLow;
                if (penetrated && curr.Close < refLow)
                    return false;

                decimal recoveryRatio = (curr.Close - curr.Low) / candleHeight;
                decimal minRecovery = penetrated ? MinRecoveryRatioPenetration : MinRecoveryRatio;
                if (recoveryRatio < minRecovery)
                    return false;

                return true;
            }

            decimal needPushDeltaShort = absNetDeltaMin * 1.00m;
            bool IsBullishPush(OvSnapshot s) => s.Close > s.Open && s.NetDeltaTotal >= needPushDeltaShort;

            OvSnapshot pushBarS = prev;
            bool usingPrevPrevS = false;
            if (!IsBullishPush(prev))
            {
                if (prevPrev == null || !IsBullishPush(prevPrev))
                    return false;
                pushBarS = prevPrev;
                usingPrevPrevS = true;
            }

            decimal needDeltaShort = absNetDeltaMin * AbsDeltaMultiplier;
            if (curr.NetDeltaTotal > -needDeltaShort)
                return false;

            decimal exAskS = curr.AskAtHigh;
            decimal exBidS = curr.BidAtHigh;
            decimal exTotS = exBidS + exAskS;
            if (exTotS <= 0m)
                return false;
            if ((exTotS / vol) < MinExtremeShare)
                return false;

            decimal domS = exAskS / exTotS;
            if (domS < MinExtremeDomRatio)
                return false;

            if (usingPrevPrevS)
            {
                bool midOk = prev.High <= pushBarS.High + (oneTick * PushWindowSweepTicks);
                if (!midOk)
                    return false;
            }

            bool currBearish = curr.Close < curr.Open;
            if (!currBearish)
                return false;

            decimal refHigh = Math.Min(pushBarS.High, z.High);
            bool penetratedS = curr.High > refHigh;
            if (penetratedS && curr.Close > refHigh)
                return false;

            decimal recoveryRatioS = (curr.High - curr.Close) / candleHeight;
            decimal minRecoveryS = penetratedS ? MinRecoveryRatioPenetration : MinRecoveryRatio;
            if (recoveryRatioS < minRecoveryS)
                return false;

            return true;
        }

        private static string GetAbsorptionDebugText(
            OvSnapshot? prevPrev,
            OvSnapshot prev,
            OvSnapshot curr,
            MarketStructureContext.Zone z,
            decimal tickSize,
            OrderDirections dir,
            decimal absNetDeltaMin)
        {
            try
            {
                string a = GetAbsorptionDebugTextA(prev, curr, z, tickSize, dir, absNetDeltaMin);
                string b = GetAbsorptionDebugTextB(prevPrev, prev, curr, z, tickSize, dir, absNetDeltaMin);
                string c = GetAbsorptionDebugTextC(prev, curr, z, tickSize, dir);
                if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b) && string.IsNullOrEmpty(c))
                    return string.Empty;
                return $"ABSDBG(A:{a}|B:{b}|C:{c})";
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetAbsorptionDebugTextC(
            OvSnapshot prev,
            OvSnapshot curr,
            MarketStructureContext.Zone z,
            decimal tickSize,
            OrderDirections dir)
        {
            decimal oneTick = tickSize;

            const decimal MinBidDomAtExtreme = 0.58m;
            const decimal MinRecoveryRatio = 0.35m;
            const decimal MinExtremeShare = 0.03m;
            const int MaxSweepTicks = 2;
            const int MinPrevRangeTicks = 3;

            decimal vol = curr.Volume;
            if (vol <= 0m)
                vol = 1m;

            decimal candleHeight = curr.High - curr.Low;
            if (candleHeight <= 0m)
                candleHeight = oneTick;

            int prevRangeTicks = RoundTicks(prev.High - prev.Low, oneTick);

            if (dir == OrderDirections.Buy)
            {
                bool prevBearishPush = prev.Close < prev.Open && prevRangeTicks >= MinPrevRangeTicks;
                if (!prevBearishPush)
                    return "Prev kein ausreichender Push (bearish/range zu klein)";

                decimal exBid = curr.BidAtLow;
                decimal exAsk = curr.AskAtLow;
                decimal exTot = exBid + exAsk;
                if (exTot <= 0m)
                    return "Extrem (Low): Bid/Ask fehlt";

                decimal dom = exBid / exTot;
                if (dom < MinBidDomAtExtreme)
                    return "Extrem (Low): Bid-Dominanz zu klein";

                if ((exTot / vol) < MinExtremeShare)
                    return "Extrem (Low): Extrem-Share zu klein";

                bool noFurtherDownByPrev = curr.Low >= prev.Low - (oneTick * MaxSweepTicks);
                bool noFurtherDownByZone = curr.Low >= z.Low - (oneTick * MaxSweepTicks);
                if (!(noFurtherDownByPrev || noFurtherDownByZone))
                    return "Low: zu viel Sweep (weiter runter)";

                decimal recoveryRatio = (curr.Close - curr.Low) / candleHeight;
                if (recoveryRatio < MinRecoveryRatio)
                    return "Close-Recovery zu klein";

                return string.Empty;
            }

            bool prevBullishPush = prev.Close > prev.Open && prevRangeTicks >= MinPrevRangeTicks;
            if (!prevBullishPush)
                return "Prev kein ausreichender Push (bullish/range zu klein)";

            decimal exAskS = curr.AskAtHigh;
            decimal exBidS = curr.BidAtHigh;
            decimal exTotS = exBidS + exAskS;
            if (exTotS <= 0m)
                return "Extrem (High): Bid/Ask fehlt";

            decimal domS = exAskS / exTotS;
            if (domS < MinBidDomAtExtreme)
                return "Extrem (High): Ask-Dominanz zu klein";

            if ((exTotS / vol) < MinExtremeShare)
                return "Extrem (High): Extrem-Share zu klein";

            bool noFurtherUpByPrev = curr.High <= prev.High + (oneTick * MaxSweepTicks);
            bool noFurtherUpByZone = curr.High <= z.High + (oneTick * MaxSweepTicks);
            if (!(noFurtherUpByPrev || noFurtherUpByZone))
                return "High: zu viel Sweep (weiter hoch)";

            decimal recoveryRatioS = (curr.High - curr.Close) / candleHeight;
            if (recoveryRatioS < MinRecoveryRatio)
                return "Close-Recovery zu klein";

            return string.Empty;
        }

        private static string GetAbsorptionDebugTextA(
            OvSnapshot prev,
            OvSnapshot curr,
            MarketStructureContext.Zone z,
            decimal tickSize,
            OrderDirections dir,
            decimal absNetDeltaMin)
        {
            decimal oneTick = tickSize;
            if (absNetDeltaMin < 0m)
                absNetDeltaMin = 0m;

            const decimal MinDeltaPerVol = 0.15m;
            const decimal MinExtremeDomRatio = 0.65m;
            const decimal MinExtremeShare = 0.05m;
            const decimal AbsDeltaMultiplier = 1.50m;
            const decimal MinRecoveryRatio = 0.40m;
            const int MaxSweepTicks = 3;

            decimal vol = curr.Volume;
            if (vol <= 0m)
                vol = 1m;

            decimal deltaPerVol = Math.Abs(curr.NetDeltaTotal) / vol;
            decimal absDelta = Math.Abs(curr.NetDeltaTotal);
            bool hasStrongDeltaRatio = deltaPerVol >= MinDeltaPerVol;
            bool hasStrongAbsDelta = absDelta >= (absNetDeltaMin * AbsDeltaMultiplier);
            if (!hasStrongDeltaRatio && !hasStrongAbsDelta)
                return "Orderflow-Druck zu schwach (Delta/Volumen zu klein)";

            decimal candleHeight = curr.High - curr.Low;
            if (candleHeight <= 0m)
                candleHeight = oneTick;

            if (dir == OrderDirections.Buy)
            {
                bool hasSellImb = curr.StackedSellImbBottomCount > 0 && curr.NetDeltaTotal < 0m;
                if (!hasSellImb)
                    return "Kein klares Verkäufer-Ungleichgewicht am Tief (Sell-Imbalance fehlt)";

                decimal exBid = curr.BidAtLow;
                decimal exAsk = curr.AskAtLow;
                decimal exTot = exBid + exAsk;
                if (exTot <= 0m)
                    return "Keine aussagekräftigen Extrem-Orders am Tief (Bid/Ask fehlt)";
                decimal exShare = exTot / vol;
                if (exShare < MinExtremeShare)
                    return "Extrem-Volumen am Tief ist zu klein (relativ zum Gesamtvolumen)";
                decimal dom = exBid / exTot;
                if (dom < MinExtremeDomRatio)
                    return "Am Tief dominieren die Käufer nicht genug (Bid-Anteil zu klein)";

                bool noFurtherDownByPrev = curr.Low >= prev.Low - (oneTick * MaxSweepTicks);
                bool noFurtherDownByZone = curr.Low >= z.Low - (oneTick * MaxSweepTicks);
                bool noFurtherDown = noFurtherDownByPrev || noFurtherDownByZone;
                if (!noFurtherDown)
                    return "Preis macht neue Tiefs (kein klarer Stopp erkennbar)";

                bool goodRecovery = ((curr.Close - curr.Low) / candleHeight) >= MinRecoveryRatio;
                if (!goodRecovery)
                    return "Zu wenig Erholung vom Tief (Rücklauf zu klein)";

                decimal zoneHeight = z.High - z.Low;
                bool rejectionCandle = curr.Close >= (curr.Low + candleHeight * 0.50m);
                bool rejectionZone = zoneHeight > 0m && curr.Close >= (z.Low + zoneHeight * 0.33m);
                bool rejection = rejectionCandle || rejectionZone;
                if (!rejection)
                    return "Kein klares Zurückweisen (Close zu schwach)";

                return string.Empty;
            }

            bool hasBuyImb = curr.StackedBuyImbTopCount > 0 && curr.NetDeltaTotal > 0m;
            if (!hasBuyImb)
                return "Kein klares Käufer-Ungleichgewicht am Hoch (Buy-Imbalance fehlt)";

            decimal exAskS = curr.AskAtHigh;
            decimal exBidS = curr.BidAtHigh;
            decimal exTotS = exBidS + exAskS;
            if (exTotS <= 0m)
                return "Keine aussagekräftigen Extrem-Orders am Hoch (Bid/Ask fehlt)";
            decimal exShareS = exTotS / vol;
            if (exShareS < MinExtremeShare)
                return "Extrem-Volumen am Hoch ist zu klein (relativ zum Gesamtvolumen)";
            decimal domS = exAskS / exTotS;
            if (domS < MinExtremeDomRatio)
                return "Am Hoch dominieren die Verkäufer nicht genug (Ask-Anteil zu klein)";

            bool noFurtherUpByPrev = curr.High <= prev.High + (oneTick * MaxSweepTicks);
            bool noFurtherUpByZone = curr.High <= z.High + (oneTick * MaxSweepTicks);
            bool noFurtherUp = noFurtherUpByPrev || noFurtherUpByZone;
            if (!noFurtherUp)
                return "Preis macht neue Hochs (kein klarer Stopp erkennbar)";

            bool goodRecoveryS = ((curr.High - curr.Close) / candleHeight) >= MinRecoveryRatio;
            if (!goodRecoveryS)
                return "Zu wenig Rücklauf vom Hoch (Erholung zu klein)";

            decimal zoneHeightS = z.High - z.Low;
            bool rejectionCandleS = curr.Close <= (curr.High - candleHeight * 0.50m);
            bool rejectionZoneS = zoneHeightS > 0m && curr.Close <= (z.High - zoneHeightS * 0.33m);
            bool rejectionS = rejectionCandleS || rejectionZoneS;
            if (!rejectionS)
                return "Kein klares Zurückweisen (Close zu schwach)";

            return string.Empty;
        }

        private static string GetAbsorptionDebugTextB(
            OvSnapshot? prevPrev,
            OvSnapshot prev,
            OvSnapshot curr,
            MarketStructureContext.Zone z,
            decimal tickSize,
            OrderDirections dir,
            decimal absNetDeltaMin)
        {
            decimal oneTick = tickSize;
            if (absNetDeltaMin < 0m)
                absNetDeltaMin = 0m;

            const decimal AbsDeltaMultiplier = 1.00m;
            const decimal MinRecoveryRatio = 0.35m;
            const decimal MinRecoveryRatioPenetration = 0.55m;
            const decimal MinExtremeDomRatio = 0.55m;
            const decimal MinExtremeShare = 0.02m;
            const int PushWindowSweepTicks = 2;

            decimal vol = curr.Volume;
            if (vol <= 0m)
                vol = 1m;

            decimal candleHeight = curr.High - curr.Low;
            if (candleHeight <= 0m)
                candleHeight = oneTick;

            if (dir == OrderDirections.Buy)
            {
                decimal needPushDelta = absNetDeltaMin * 1.00m;
                bool IsBearishPush(OvSnapshot s) => s.Close < s.Open && s.NetDeltaTotal <= -needPushDelta;

                OvSnapshot pushBar = prev;
                bool usingPrevPrev = false;
                if (!IsBearishPush(prev))
                {
                    if (prevPrev == null || !IsBearishPush(prevPrev))
                        return "Kein klarer Abverkaufs-Impuls vor dem Reversal (Push-Bar fehlt)";
                    pushBar = prevPrev;
                    usingPrevPrev = true;
                }

                decimal needDelta = absNetDeltaMin * AbsDeltaMultiplier;
                if (curr.NetDeltaTotal < needDelta)
                    return "Reversal-Bar: Kaufdruck zu schwach (Delta zu klein)";

                decimal exBid = curr.BidAtLow;
                decimal exAsk = curr.AskAtLow;
                decimal exTot = exBid + exAsk;
                if (exTot <= 0m)
                    return "Keine aussagekräftigen Extrem-Orders am Tief (Bid/Ask fehlt)";
                if ((exTot / vol) < MinExtremeShare)
                    return "Extrem-Volumen am Tief ist zu klein (relativ zum Gesamtvolumen)";
                decimal dom = exBid / exTot;
                if (dom < MinExtremeDomRatio)
                    return "Am Tief dominieren die Käufer nicht genug (Bid-Anteil zu klein)";

                if (usingPrevPrev)
                {
                    bool midOk = prev.Low >= pushBar.Low - (oneTick * PushWindowSweepTicks);
                    if (!midOk)
                        return "Zwischenbar macht ein neues Tief (zu viel Durchstich)";
                }

                if (!(curr.Close > curr.Open))
                    return "Reversal-Bar ist nicht bullisch (Close nicht über Open)";

                decimal refLow = Math.Max(pushBar.Low, z.Low);
                bool penetrated = curr.Low < refLow;
                if (penetrated && curr.Close < refLow)
                    return "Stich wurde nicht zurückgeholt (Close nicht über Zone/Referenz)";

                decimal recoveryRatio = (curr.Close - curr.Low) / candleHeight;
                decimal minRecovery = penetrated ? MinRecoveryRatioPenetration : MinRecoveryRatio;
                if (recoveryRatio < minRecovery)
                    return "Zu wenig Erholung vom Tief (Rücklauf zu klein)";

                return string.Empty;
            }

            decimal needPushDeltaShort = absNetDeltaMin * 1.00m;
            bool IsBullishPush(OvSnapshot s) => s.Close > s.Open && s.NetDeltaTotal >= needPushDeltaShort;

            OvSnapshot pushBarS = prev;
            bool usingPrevPrevS = false;
            if (!IsBullishPush(prev))
            {
                if (prevPrev == null || !IsBullishPush(prevPrev))
                    return "Kein klarer Kauf-Impuls vor dem Reversal (Push-Bar fehlt)";
                pushBarS = prevPrev;
                usingPrevPrevS = true;
            }

            decimal needDeltaShort = absNetDeltaMin * AbsDeltaMultiplier;
            if (curr.NetDeltaTotal > -needDeltaShort)
                return "Reversal-Bar: Verkaufsdruck zu schwach (Delta zu klein)";

            decimal exAskS = curr.AskAtHigh;
            decimal exBidS = curr.BidAtHigh;
            decimal exTotS = exBidS + exAskS;
            if (exTotS <= 0m)
                return "Keine aussagekräftigen Extrem-Orders am Hoch (Bid/Ask fehlt)";
            if ((exTotS / vol) < MinExtremeShare)
                return "Extrem-Volumen am Hoch ist zu klein (relativ zum Gesamtvolumen)";
            decimal domS = exAskS / exTotS;
            if (domS < MinExtremeDomRatio)
                return "Am Hoch dominieren die Verkäufer nicht genug (Ask-Anteil zu klein)";

            if (usingPrevPrevS)
            {
                bool midOk = prev.High <= pushBarS.High + (oneTick * PushWindowSweepTicks);
                if (!midOk)
                    return "Zwischenbar macht ein neues Hoch (zu viel Durchstich)";
            }

            if (!(curr.Close < curr.Open))
                return "Reversal-Bar ist nicht bärisch (Close nicht unter Open)";

            decimal refHigh = Math.Min(pushBarS.High, z.High);
            bool penetratedS = curr.High > refHigh;
            if (penetratedS && curr.Close > refHigh)
                return "Stich wurde nicht zurückgeholt (Close nicht unter Zone/Referenz)";

            decimal recoveryRatioS = (curr.High - curr.Close) / candleHeight;
            decimal minRecoveryS = penetratedS ? MinRecoveryRatioPenetration : MinRecoveryRatio;
            if (recoveryRatioS < minRecoveryS)
                return "Zu wenig Rücklauf vom Hoch (Erholung zu klein)";

            return string.Empty;
        }

        private static OvSnapshot? GetPreviousClosedSnapshot(OfFeaturesHistory history, int currentBar, int maxLookback)
        {
            OvSnapshot? best = null;
            for (int i = 0; i < Math.Min(maxLookback, history.Count); i++)
            {
                var f = history.GetOfFeatures(i);
                var s = f?.Snapshot;
                if (s == null)
                    continue;
                if (s.Bar >= currentBar)
                    continue;
                if (best == null || s.Bar > best.Bar)
                    best = s;
            }

            return best;
        }

        private static OvSnapshot? GetPreviousClosedSnapshotByOffset(OfFeaturesHistory history, int currentBar, int offset, int maxLookback)
        {
            if (history == null || offset <= 0)
                return null;

            var prevs = new List<OvSnapshot>(offset);
            for (int i = 0; i < Math.Min(maxLookback, history.Count); i++)
            {
                var s = history.GetOfFeatures(i)?.Snapshot;
                if (s == null)
                    continue;
                if (s.Bar >= currentBar)
                    continue;
                prevs.Add(s);
                if (prevs.Count >= offset)
                    break;
            }

            return prevs.Count >= offset ? prevs[offset - 1] : null;
        }

        private static SwingStructureSignal? ComputeSwingSignalFromHistory(OfFeaturesHistory history, int currentBar, int maxBars)
        {
            if (history == null || history.Count <= 0)
                return null;

            int cap = Math.Max(20, Math.Min(maxBars, history.Count));
            var swing = new SwingStructureDetector(leftBars: 6, rightBars: 6, maxBars: Math.Max(200, cap));

            int startIndex = Math.Max(0, history.Count - cap);
            for (int i = startIndex; i < history.Count; i++)
            {
                var f = history.GetOfFeatures(i);
                var s = f?.Snapshot;
                if (s == null)
                    continue;
                if (s.Bar >= currentBar)
                    continue;
                swing.AddBar(high: s.High, low: s.Low, close: s.Close);
            }

            return swing.GetSignal();
        }
    }
}