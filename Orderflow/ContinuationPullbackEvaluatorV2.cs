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
    public sealed class ContinuationPullbackEvaluatorV2 : IPatternEvaluator
    {
        private enum Stage
        {
            None,
            Touch,
            Retest,
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
            public decimal TotalScore { get; init; }
            public decimal Confidence { get; init; }
            public string BlockReasonDe { get; init; } = string.Empty;
            public List<ScoreItem> Items { get; init; } = new();
        }

        private sealed class ZoneTracker
        {
            public int ZoneId;
            public bool SessionActive;
            public int SessionStartBar;
            public int SessionLastEvalBar;
            public Stage SessionStage;

            public int ConsecutiveInvalidCloses;

            public int TouchBar;
            public decimal TouchPocPrice;

            public int RetestDeadlineBar;
            public int? RetestBar;

            public int? ConfirmBar;

            public decimal SessionBestScore;
            public List<ScoreItem>? SessionBestScoreItems;
            public List<DecisionResult>? SessionDecisionHistory;
            public List<int>? SessionDecisionBars;
            public string? SessionEndReason;
        }

        private readonly OrderDirections _direction;
        private readonly OrderflowPatternType _patternType;
        private readonly ILoggerSource? _loggerSource;

        private int? _lastPullbackLikePhaseBar;

        private readonly Dictionary<int, ZoneTracker> _trackersByZoneId = new();

        public OrderflowPatternType Type => _patternType;
        public OrderDirections Direction => _direction;

        public ContinuationPullbackEvaluatorV2(OrderDirections direction, ILoggerSource? loggerSource = null)
        {
            _direction = direction;
            _patternType = direction == OrderDirections.Buy
                ? OrderflowPatternType.PotentialLongTrendContinuation
                : OrderflowPatternType.PotentialShortTrendContinuation;
            _loggerSource = loggerSource;
        }

        private void LogExplainOnce(int bar, int zoneId, string stage, IEnumerable<string> lines)
        {
            try
            {
                if (_loggerSource == null)
                    return;

                var msg = $"[ContinuationPullbackV2:{stage}] " + string.Join(" | ", lines);
                SmartLogger.Instance.LogIfChanged(
                    category: "ContinuationPullbackV2",
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
            }
        }

        private void LogExplainMultilineOnce(int bar, int zoneId, string stage, IEnumerable<string> lines)
        {
            try
            {
                if (_loggerSource == null)
                    return;

                var msg = string.Join(Environment.NewLine, lines);
                SmartLogger.Instance.LogIfChanged(
                    category: "ContinuationPullbackV2",
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

        private static bool TouchesZone(OvSnapshot s, MarketStructureContext.Zone z)
        {
            return s.High >= z.Low && s.Low <= z.High;
        }

        private static bool IsFinishedAuction(OvSnapshot s, OrderflowThresholds th, OrderDirections dir)
        {
            if (dir == OrderDirections.Buy)
                return s.AskAtLow <= th.FinishedAuctionMaxAskAtLow;
            return s.BidAtHigh <= th.FinishedAuctionMaxBidAtHigh;
        }

        private static bool IsFinishedAuctionAtZone(OvSnapshot s, MarketStructureContext.Zone z, decimal tickSize, OrderDirections dir)
        {
            decimal oneTick = tickSize > 0m ? tickSize : 0.25m;
            if (dir == OrderDirections.Buy)
                return s.Low <= z.High && s.Low >= (z.Low - oneTick);
            return s.High >= z.Low && s.High <= (z.High + oneTick);
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
            if (absNetDeltaMin < 0m)
                absNetDeltaMin = 0m;

            const decimal MinDeltaPerVol = 0.15m;
            const decimal MinExtremeDomRatio = 0.65m;
            const decimal MinExtremeShare = 0.05m;

            decimal vol = curr.Volume;
            if (vol <= 0m)
                vol = 1m;
            decimal deltaPerVol = Math.Abs(curr.NetDeltaTotal) / vol;
            if (deltaPerVol < MinDeltaPerVol)
                return false;

            if (dir == OrderDirections.Buy)
            {
                bool hasSellImb = curr.StackedSellImbBottomCount > 0 && curr.NetDeltaTotal < 0m;
                if (!hasSellImb)
                    return false;
                if (Math.Abs(curr.NetDeltaTotal) < absNetDeltaMin)
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

                bool noFurtherDown = curr.Low >= prev.Low - oneTick;
                bool rejection = curr.Close >= z.High;
                return noFurtherDown && rejection;
            }
            else
            {
                bool hasBuyImb = curr.StackedBuyImbTopCount > 0 && curr.NetDeltaTotal > 0m;
                if (!hasBuyImb)
                    return false;
                if (Math.Abs(curr.NetDeltaTotal) < absNetDeltaMin)
                    return false;

                decimal exAsk = curr.AskAtHigh;
                decimal exBid = curr.BidAtHigh;
                decimal exTot = exBid + exAsk;
                if (exTot <= 0m)
                    return false;
                decimal exShare = exTot / vol;
                if (exShare < MinExtremeShare)
                    return false;
                decimal dom = exAsk / exTot;
                if (dom < MinExtremeDomRatio)
                    return false;

                bool noFurtherUp = curr.High <= prev.High + oneTick;
                bool rejection = curr.Close <= z.Low;
                return noFurtherUp && rejection;
            }
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

        private static decimal GetAverageVolumeByBars(OfFeaturesHistory history, int currentBar, int lookback)
        {
            if (history == null)
                return 0m;
            if (lookback <= 0)
                return 0m;

            int got = 0;
            decimal sum = 0m;
            for (int b = currentBar - 1; b >= 0 && got < lookback; b--)
            {
                if (!history.TryGetByBar(b, out var f) || f?.Snapshot == null)
                    continue;
                var s = f.Snapshot;
                if (s.Volume <= 0m)
                    continue;
                sum += s.Volume;
                got++;
            }

            if (got == 0)
                return 0m;
            return sum / got;
        }

        private static bool IsZoneInvalidated(
            OvSnapshot curr,
            MarketStructureContext.Zone zone,
            decimal tickSize,
            OrderDirections dir,
            int invalidateWickTicks,
            int invalidateCloseTicks,
            ref int consecutiveInvalidCloses,
            int invalidateBars,
            out string reasonDe)
        {
            reasonDe = string.Empty;
            if (tickSize <= 0m)
                tickSize = 0.25m;

            invalidateWickTicks = Math.Max(0, invalidateWickTicks);
            invalidateCloseTicks = Math.Max(0, invalidateCloseTicks);
            invalidateBars = Math.Max(1, invalidateBars);

            bool wickInvalid;
            bool closeInvalid;

            if (dir == OrderDirections.Buy)
            {
                wickInvalid = curr.Low < (zone.Low - (invalidateWickTicks * tickSize));
                closeInvalid = curr.Close < (zone.Low - (invalidateCloseTicks * tickSize));
            }
            else
            {
                wickInvalid = curr.High > (zone.High + (invalidateWickTicks * tickSize));
                closeInvalid = curr.Close > (zone.High + (invalidateCloseTicks * tickSize));
            }

            if (closeInvalid)
                consecutiveInvalidCloses++;
            else
                consecutiveInvalidCloses = 0;

            if (wickInvalid)
            {
                reasonDe = dir == OrderDirections.Buy
                    ? $"Invalidation: Docht unter Zone (Low {curr.Low:F2} < ZoneLow {zone.Low:F2} - {invalidateWickTicks}t)"
                    : $"Invalidation: Docht über Zone (High {curr.High:F2} > ZoneHigh {zone.High:F2} + {invalidateWickTicks}t)";
                return true;
            }

            if (consecutiveInvalidCloses >= invalidateBars)
            {
                reasonDe = dir == OrderDirections.Buy
                    ? $"Invalidation: {consecutiveInvalidCloses}x Close unter Zone (Close {curr.Close:F2} < ZoneLow {zone.Low:F2} - {invalidateCloseTicks}t)"
                    : $"Invalidation: {consecutiveInvalidCloses}x Close über Zone (Close {curr.Close:F2} > ZoneHigh {zone.High:F2} + {invalidateCloseTicks}t)";
                return true;
            }

            return false;
        }

        private static bool TouchesPrice(OvSnapshot s, decimal price, decimal tickSize)
        {
            decimal tol = tickSize > 0m ? tickSize : 0.25m;
            return s.High >= (price - tol) && s.Low <= (price + tol);
        }

        private static bool IsBiasOk(OrderDirections dir, MarketBiasV2 bias)
        {
            if (dir == OrderDirections.Buy)
                return bias == MarketBiasV2.Long;
            return bias == MarketBiasV2.Short;
        }

        private static bool IsPhaseOk(MarketPhaseV2 phase)
        {
            return phase == MarketPhaseV2.Healthy_Pullback || phase == MarketPhaseV2.Momentum_Refuel;
        }

        private DecisionResult EvaluateDecision(
            OvSnapshot curr,
            OvSnapshot? prev,
            MarketStructureContext.Zone zone,
            OrderflowThresholds thresholds,
            OfFeaturesHistory history,
            decimal tickSize,
            ZoneTracker tracker)
        {
            const decimal EntryThreshold = 5m;
            const int AdaptiveLookback = 30;
            const decimal AbsNetDeltaMedianMultiplier = 1.0m;

            const int VolLookback = 20;

            decimal absNetDeltaMin = GetAdaptiveAbsNetDeltaMin(history, curr, AdaptiveLookback, AbsNetDeltaMedianMultiplier);

            decimal avgVol = GetAverageVolumeByBars(history, curr.Bar, VolLookback);
            bool hasAvgVol = avgVol > 0m;
            bool isLowVolume = hasAvgVol && curr.Volume > 0m && curr.Volume < (avgVol * 0.85m);

            bool isExhaustion = _direction == OrderDirections.Buy
                ? curr.NetDeltaTotal > -absNetDeltaMin
                : curr.NetDeltaTotal < absNetDeltaMin;

            var items = new List<ScoreItem>(16);
            decimal score = 0m;

            bool faAtZone = IsFinishedAuction(curr, thresholds, _direction) && IsFinishedAuctionAtZone(curr, zone, tickSize, _direction);
            bool absorption = prev != null && ImbalanceNoFollowThrough(prev, curr, zone, tickSize, _direction, absNetDeltaMin);

            if (tracker.SessionStage == Stage.Touch)
            {
                items.Add(new ScoreItem { Key = "Touch", Points = 1m, TextDe = "Touch: Session aktiv" });
                score += 1m;

                decimal lowVolPts = isLowVolume ? 2m : 0m;
                items.Add(new ScoreItem { Key = "LowVol", Points = lowVolPts, TextDe = isLowVolume ? $"Drying Up: Volumen {curr.Volume:0} < Avg {avgVol:0} -> +2" : (hasAvgVol ? $"Drying Up: Volumen {curr.Volume:0} >= Avg {avgVol:0} -> +0" : "Drying Up: n/v -> +0") });
                score += lowVolPts;

                decimal exhPts = isExhaustion ? 1m : 0m;
                items.Add(new ScoreItem { Key = "Exhaustion", Points = exhPts, TextDe = isExhaustion ? $"Exhaustion: Gegner-Delta nicht extrem (|Δ|~<{absNetDeltaMin:0}) -> +1" : $"Exhaustion: Gegner-Delta noch stark (|Δ|>~{absNetDeltaMin:0}) -> +0" });
                score += exhPts;

                decimal faPts = faAtZone ? 2m : 0m;
                items.Add(new ScoreItem { Key = "FA", Points = faPts, TextDe = faAtZone ? "Finished Auction an Zone: JA -> +2" : "Finished Auction an Zone: NEIN -> +0" });
                score += faPts;

                decimal absPts = absorption ? 1m : 0m;
                items.Add(new ScoreItem { Key = "Abs", Points = absPts, TextDe = absorption ? "Absorption: JA (Bonus) -> +1" : "Absorption: NEIN -> +0" });
                score += absPts;

                bool okTouch = isLowVolume || faAtZone || isExhaustion || absorption;
                if (!okTouch)
                {
                    return new DecisionResult
                    {
                        Allowed = false,
                        Entry = false,
                        TotalScore = score,
                        Confidence = 0m,
                        BlockReasonDe = "Touch-Kerze: kein Drying-Up/FA/Exhaustion/Absorption erkennbar.",
                        Items = items
                    };
                }

                bool fastConfirm = isLowVolume || faAtZone;
                items.Add(new ScoreItem { Key = "Stage", Points = 0m, TextDe = fastConfirm ? "Stage: Touch OK -> Fast-Path: warte Confirm" : "Stage: Touch OK -> Optional: warte Retest" });
                return new DecisionResult
                {
                    Allowed = true,
                    Entry = false,
                    TotalScore = score,
                    Confidence = Math.Min(1m, score / EntryThreshold),
                    BlockReasonDe = string.Empty,
                    Items = items
                };
            }

            if (tracker.SessionStage == Stage.Retest)
            {
                items.Add(new ScoreItem { Key = "Retest", Points = 1m, TextDe = "Retest: Session aktiv" });
                score += 1m;

                bool retestTouchesPoc = TouchesPrice(curr, tracker.TouchPocPrice, tickSize);
                decimal rtPts = retestTouchesPoc ? 1m : 0m;
                items.Add(new ScoreItem { Key = "RetestPoc", Points = rtPts, TextDe = retestTouchesPoc ? "Retest Touch-POC: JA -> +1" : "Retest Touch-POC: NEIN -> +0" });
                score += rtPts;

                decimal faPts = (faAtZone && retestTouchesPoc) ? 2m : 0m;
                items.Add(new ScoreItem { Key = "FA", Points = faPts, TextDe = (faAtZone && retestTouchesPoc) ? "Retest: Finished Auction an Zone: JA -> +2" : "Retest: Finished Auction an Zone: NEIN -> +0" });
                score += faPts;

                decimal absPts = (absorption && retestTouchesPoc) ? 1m : 0m;
                items.Add(new ScoreItem { Key = "Abs", Points = absPts, TextDe = (absorption && retestTouchesPoc) ? "Retest: Absorption: JA (Bonus) -> +1" : "Retest: Absorption: NEIN -> +0" });
                score += absPts;

                bool okRetest = retestTouchesPoc && (faAtZone || absorption);
                if (!okRetest)
                    items.Add(new ScoreItem { Key = "RetestOk", Points = 0m, TextDe = "Retest: optional – noch keine saubere Retest-Qualität" });

                return new DecisionResult
                {
                    Allowed = okRetest,
                    Entry = false,
                    TotalScore = score,
                    Confidence = Math.Min(1m, score / EntryThreshold),
                    BlockReasonDe = okRetest ? string.Empty : "Warte auf optionalen Retest oder Confirm.",
                    Items = items
                };
            }

            if (tracker.SessionStage == Stage.Confirm)
            {
                items.Add(new ScoreItem { Key = "Confirm", Points = 1m, TextDe = "Confirm: Session aktiv" });
                score += 1m;

                bool aggressiveEntry;
                if (_direction == OrderDirections.Buy)
                    aggressiveEntry = (curr.StackedBuyImbCount > 0 || curr.NetDeltaTotal > absNetDeltaMin) && curr.Close > curr.Open;
                else
                    aggressiveEntry = (curr.StackedSellImbCount > 0 || curr.NetDeltaTotal < -absNetDeltaMin) && curr.Close < curr.Open;

                decimal aggrPts = aggressiveEntry ? 3m : 0m;
                items.Add(new ScoreItem { Key = "Aggression", Points = aggrPts, TextDe = aggressiveEntry ? "Trend-Aggression: Initiative kehrt zurück -> +3" : "Trend-Aggression: noch fehlt Initiative -> +0" });
                score += aggrPts;

                bool pocShift = _direction == OrderDirections.Buy
                    ? curr.CandlePocPrice > tracker.TouchPocPrice
                    : curr.CandlePocPrice < tracker.TouchPocPrice;

                decimal pocPts = pocShift ? 2m : 0m;
                items.Add(new ScoreItem { Key = "PocShift", Points = pocPts, TextDe = pocShift ? "POC-Shift: Momentum bestätigt Richtung -> +2" : "POC-Shift: keine klare Richtung -> +0" });
                score += pocPts;

                bool entry = aggressiveEntry && score >= EntryThreshold;
                if (!entry)
                {
                    return new DecisionResult
                    {
                        Allowed = true,
                        Entry = false,
                        TotalScore = score,
                        Confidence = Math.Min(1m, score / EntryThreshold),
                        BlockReasonDe = aggressiveEntry ? "Warte auf stärkere Bestätigung (Score/POC-Shift)." : "Warte auf Aggression in Trendrichtung.",
                        Items = items
                    };
                }

                return new DecisionResult
                {
                    Allowed = true,
                    Entry = true,
                    TotalScore = score,
                    Confidence = Math.Min(1m, Math.Max(0.65m, score / EntryThreshold)),
                    BlockReasonDe = string.Empty,
                    Items = items
                };
            }

            return new DecisionResult
            {
                Allowed = false,
                Entry = false,
                TotalScore = 0m,
                Confidence = 0m,
                BlockReasonDe = "Unbekannte SessionStage.",
                Items = items
            };
        }

        private void StartSession(ZoneTracker tracker, OvSnapshot touchSnapshot)
        {
            tracker.SessionActive = true;
            tracker.SessionStartBar = touchSnapshot.Bar;
            tracker.SessionLastEvalBar = -1;
            tracker.SessionStage = Stage.Touch;
            tracker.ConsecutiveInvalidCloses = 0;
            tracker.TouchBar = touchSnapshot.Bar;
            tracker.TouchPocPrice = touchSnapshot.CandlePocPrice;
            tracker.RetestDeadlineBar = touchSnapshot.Bar + 2;
            tracker.RetestBar = null;
            tracker.ConfirmBar = null;
            tracker.SessionBestScore = 0m;
            tracker.SessionBestScoreItems = null;
            tracker.SessionDecisionHistory = null;
            tracker.SessionDecisionBars = null;
            tracker.SessionEndReason = null;
        }

        private void EndSession(ZoneTracker tracker, string reason)
        {
            tracker.SessionActive = false;
            tracker.SessionEndReason = reason;
        }

        private void LogSessionProtocol(
            ZoneTracker tracker,
            MarketStructureContext.Zone zone,
            OfFeaturesHistory history,
            OvSnapshot currentSnapshot,
            OrderflowThresholds thresholds,
            decimal tickSize,
            string outcome,
            DecisionResult? finalDecision)
        {
            try
            {
                if (_loggerSource == null) return;

                const decimal EntryThreshold = 5m;
                var lines = new List<string>(96);
                string dirDe = _direction == OrderDirections.Buy ? "LONG-Trend" : "SHORT-Trend";
                int sessionBars = (currentSnapshot.Bar - tracker.SessionStartBar) + 1;

                lines.Add("═══════════════════════════════════════════════════════════");
                lines.Add($"PULLBACK-CHECK | {dirDe} | Zone #{zone.Id} | {sessionBars} Bars");
                lines.Add($"Bereich: {zone.Low:F2} bis {zone.High:F2} | Ergebnis: {outcome.ToUpperInvariant()}");
                lines.Add("═══════════════════════════════════════════════════════════");
                lines.Add(string.Empty);

                lines.Add("ABLAUF DER KORREKTUR (KERZEN-LOG):");

                var barsLowVol = new List<int>();
                var barsExh = new List<int>();
                var barsAggr = new List<int>();
                var barsPocShift = new List<int>();
                var barsFA = new List<int>();
                var barsAbs = new List<int>();

                int startBar = tracker.SessionStartBar;
                int endBar = currentSnapshot.Bar;

                for (int b = startBar; b <= endBar; b++)
                {
                    OvSnapshot? s = (b == currentSnapshot.Bar)
                        ? currentSnapshot
                        : (history.TryGetByBar(b, out var f) ? f?.Snapshot : null);
                    if (s == null) continue;

                    string color = s.Close >= s.Open ? "↑" : "↓";
                    string barLabel = s.ChartBarNumber > 0 ? $"K{s.ChartBarNumber}" : $"B{b}";

                    int chartBar = s.ChartBarNumber;

                    decimal barScore = 0m;
                    DecisionResult? barDecision = null;
                    if (tracker.SessionDecisionHistory != null && tracker.SessionDecisionBars != null)
                    {
                        for (int i = 0; i < Math.Min(tracker.SessionDecisionHistory.Count, tracker.SessionDecisionBars.Count); i++)
                        {
                            if (tracker.SessionDecisionBars[i] != b)
                                continue;

                            barDecision = tracker.SessionDecisionHistory[i];
                            barScore = barDecision?.TotalScore ?? 0m;
                            break;
                        }
                    }

                    var events = new List<string>(8);
                    if (barDecision?.Items != null)
                    {
                        if (barDecision.Items.Any(x => x.Key == "LowVol" && x.Points > 0m))
                        {
                            events.Add("Vol↓");
                            if (chartBar > 0) barsLowVol.Add(chartBar);
                        }
                        if (barDecision.Items.Any(x => x.Key == "Exhaustion" && x.Points > 0m))
                        {
                            events.Add("Exh");
                            if (chartBar > 0) barsExh.Add(chartBar);
                        }
                        if (barDecision.Items.Any(x => x.Key == "FA" && x.Points > 0m))
                        {
                            events.Add("★FA");
                            if (chartBar > 0) barsFA.Add(chartBar);
                        }
                        if (barDecision.Items.Any(x => x.Key == "Abs" && x.Points > 0m))
                        {
                            events.Add("Abs");
                            if (chartBar > 0) barsAbs.Add(chartBar);
                        }
                        if (barDecision.Items.Any(x => x.Key == "Aggression" && x.Points > 0m))
                        {
                            events.Add("AGGR");
                            if (chartBar > 0) barsAggr.Add(chartBar);
                        }
                        if (barDecision.Items.Any(x => x.Key == "PocShift" && x.Points > 0m))
                        {
                            events.Add("POC-Shift");
                            if (chartBar > 0) barsPocShift.Add(chartBar);
                        }
                    }

                    string closePos = (s.Close >= zone.Low && s.Close <= zone.High)
                        ? "IN Zone"
                        : (_direction == OrderDirections.Buy
                            ? (s.Close > zone.High ? "ÜBER Zone" : "UNTER Zone")
                            : (s.Close < zone.Low ? "UNTER Zone" : "ÜBER Zone"));

                    string eventStr = events.Count > 0 ? ("| " + string.Join(" ", events)) : string.Empty;
                    lines.Add($"{barLabel}: {color} Δ{s.PocDelta:+0;-0;0} | {closePos} | Pkt:{barScore:0.0}/{EntryThreshold:0.0} {eventStr}");
                }

                lines.Add(string.Empty);
                lines.Add("SZENARIEN-ANALYSE (WARUM GO ODER NOGO?):");

                string FormatBars(List<int> bars)
                {
                    if (bars == null || bars.Count == 0)
                        return string.Empty;
                    var distinct = bars.Distinct().ToList();
                    distinct.Sort();
                    return $" (in K{string.Join(", K", distinct)})";
                }

                if (barsLowVol.Count > 0)
                    lines.Add($"  • ✓ Trockenlauf: Das Volumen nahm ab. Wenig Gegendruck{FormatBars(barsLowVol)}.");
                else
                    lines.Add("  • ⚠ Hoher Druck: Kein signifikanter Rückgang des Volumens im Pullback.");

                if (barsExh.Count > 0)
                    lines.Add($"  • ✓ Erschöpfung: Die Gegenseite ist 'verpufft'{FormatBars(barsExh)}.");

                if (barsFA.Count > 0 || barsAbs.Count > 0)
                {
                    var combined = barsFA.Concat(barsAbs).ToList();
                    lines.Add($"  • ✓ Zonen-Halt: Der Preis wurde an der Zone gestoppt/gehalten{FormatBars(combined)}.");
                }

                if (barsAggr.Count > 0)
                    lines.Add($"  • ✓ ATTACKE: Die Trend-Aggression ist zurück{FormatBars(barsAggr)}!");
                else
                    lines.Add("  • ✗ Keine Initiative: Bisher kein aggressiver Schlag in Trendrichtung.");

                if (barsPocShift.Count > 0)
                    lines.Add($"  • ✓ Wert-Verschiebung: Der faire Preis (POC) verschiebt sich wieder{FormatBars(barsPocShift)}.");

                if (tracker.ConsecutiveInvalidCloses > 0)
                    lines.Add($"  • ⚠ Zone schwächelt: Der Preis schloss bereits {tracker.ConsecutiveInvalidCloses}x außerhalb der Zone.");

                lines.Add(string.Empty);

                string emoji = (finalDecision?.Entry == true) ? "🟢" : "🔴";
                lines.Add($"ERGEBNIS: {emoji} {outcome.ToUpperInvariant()}");

                if (finalDecision?.Entry == true)
                {
                    lines.Add("FAZIT: Pullback erfolgreich bestätigt. Trendaufnahme erkannt.");
                }
                else
                {
                    string grund = finalDecision?.BlockReasonDe ?? tracker.SessionEndReason ?? "Kriterien nicht erreicht";
                    lines.Add($"GRUND: {grund}");
                }

                lines.Add("═══════════════════════════════════════════════════════════");

                LogExplainMultilineOnce(currentSnapshot.Bar, zone.Id, stage: "SessionProtocol", lines: lines);
            }
            catch
            {
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

            if (IsPhaseOk(currentMarketState.Phase))
                _lastPullbackLikePhaseBar = currentSnapshot.Bar;

            if (!IsBiasOk(_direction, currentMarketState.Bias) && !IsBiasOk(_direction, currentDirectionalBias))
                return PatternEvaluationResult.NotDetected(Type, "BiasGate: wrong direction");

            int phaseLookbackBars = thresholds.ContinuationPhaseLookbackBars ?? 6;
            phaseLookbackBars = Math.Max(0, phaseLookbackBars);
            bool phaseOkNow = IsPhaseOk(currentMarketState.Phase);
            bool phaseOkRecent = _lastPullbackLikePhaseBar.HasValue
                && (currentSnapshot.Bar - _lastPullbackLikePhaseBar.Value) <= phaseLookbackBars;

            var zones = currentMarketStructureContext.ActiveZones;
            if (zones == null || zones.Count == 0)
                return PatternEvaluationResult.NotDetected(Type, "ZoneGate: no zones");

            MarketStructureContext.Zone? candidateZone = null;
            foreach (var z in zones)
            {
                if (z == null)
                    continue;
                if (z.Status != MarketStructureContext.ZoneStatus.New && z.Status != MarketStructureContext.ZoneStatus.Ready)
                    continue;

                bool dirOk = (_direction == OrderDirections.Buy && z.Type == MarketStructureContext.ZoneType.Support)
                             || (_direction == OrderDirections.Sell && z.Type == MarketStructureContext.ZoneType.Resistance);
                if (!dirOk)
                    continue;

                if (!TouchesZone(currentSnapshot, z))
                    continue;

                candidateZone = z;
                break;
            }

            ZoneTracker? tracker = null;
            if (candidateZone != null)
            {
                if (!_trackersByZoneId.TryGetValue(candidateZone.Id, out tracker))
                {
                    tracker = new ZoneTracker { ZoneId = candidateZone.Id };
                    _trackersByZoneId[candidateZone.Id] = tracker;
                }
            }
            else
            {
                foreach (var kv in _trackersByZoneId)
                {
                    if (!kv.Value.SessionActive)
                        continue;

                    var z = zones.FirstOrDefault(x => x != null && x.Id == kv.Key);
                    if (z == null)
                        continue;

                    bool dirOk = (_direction == OrderDirections.Buy && z.Type == MarketStructureContext.ZoneType.Support)
                                 || (_direction == OrderDirections.Sell && z.Type == MarketStructureContext.ZoneType.Resistance);
                    if (!dirOk)
                        continue;

                    candidateZone = z;
                    tracker = kv.Value;
                    break;
                }
            }

            if (candidateZone == null || tracker == null)
                return PatternEvaluationResult.NotDetected(Type, "ZoneGate: no touched/session zone");

            if (!tracker.SessionActive && !(phaseOkNow || phaseOkRecent))
                return PatternEvaluationResult.NotDetected(Type, $"PhaseGate: {currentMarketState.Phase}");

            if (tracker.SessionActive && tracker.SessionLastEvalBar == currentSnapshot.Bar)
                return PatternEvaluationResult.NotDetected(Type, "Session: already evaluated");

            if (!tracker.SessionActive)
            {
                StartSession(tracker, currentSnapshot);
                LogExplainOnce(currentSnapshot.Bar, candidateZone.Id, stage: "SessionStart", lines: new[]
                {
                    $"Touch detected -> session start",
                    $"State={(candidateZone.IsConfirmed ? "CONFIRMED" : "PENDING")}",
                    $"TouchPOC={tracker.TouchPocPrice:F2}",
                    $"RetestDeadlineBar={tracker.RetestDeadlineBar}"
                });
            }

            int zoneHeightTicks = Math.Max(1, RoundTicks(Math.Abs(candidateZone.High - candidateZone.Low), tickSize));
            int invalidateWickTicks = Math.Max(3, (int)Math.Ceiling(zoneHeightTicks * 0.25m));
            int invalidateCloseTicks = Math.Max(1, (int)Math.Ceiling(zoneHeightTicks * 0.10m));
            const int InvalidateBars = 2;

            if (tracker.SessionActive)
            {
                if (IsZoneInvalidated(
                        currentSnapshot,
                        candidateZone,
                        tickSize,
                        _direction,
                        invalidateWickTicks,
                        invalidateCloseTicks,
                        ref tracker.ConsecutiveInvalidCloses,
                        InvalidateBars,
                        out var invalidReason))
                {
                    EndSession(tracker, invalidReason);
                    LogSessionProtocol(tracker, candidateZone, history, currentSnapshot, thresholds, tickSize, "NOGO – Zone invalidiert", null);
                    currentMarketStructureContext.MarkZoneUsed(candidateZone.Id);
                    _trackersByZoneId.Remove(candidateZone.Id);
                    return PatternEvaluationResult.NotDetected(Type, invalidReason);
                }
            }

            if (tracker.SessionStage == Stage.Touch)
            {
                var prev = GetPreviousClosedSnapshot(history, currentSnapshot.Bar, maxLookback: 20);
                tracker.SessionLastEvalBar = currentSnapshot.Bar;
                var dec = EvaluateDecision(currentSnapshot, prev, candidateZone, thresholds, history, tickSize, tracker);

                tracker.SessionDecisionHistory ??= new List<DecisionResult>();
                tracker.SessionDecisionHistory.Add(dec);
                tracker.SessionDecisionBars ??= new List<int>();
                tracker.SessionDecisionBars.Add(currentSnapshot.Bar);

                if (dec.TotalScore > tracker.SessionBestScore)
                {
                    tracker.SessionBestScore = dec.TotalScore;
                    tracker.SessionBestScoreItems = dec.Items != null ? new List<ScoreItem>(dec.Items) : null;
                }

                if (!dec.Allowed)
                {
                    EndSession(tracker, dec.BlockReasonDe);
                    LogSessionProtocol(tracker, candidateZone, history, currentSnapshot, thresholds, tickSize, "NOGO – Touch ungültig", dec);
                    currentMarketStructureContext.MarkZoneUsed(candidateZone.Id);
                    _trackersByZoneId.Remove(candidateZone.Id);
                    return PatternEvaluationResult.NotDetected(Type, dec.BlockReasonDe);
                }

                bool touchFastConfirm = dec.Items != null && dec.Items.Any(x => x.Key == "LowVol" && x.Points > 0m)
                                       || dec.Items != null && dec.Items.Any(x => x.Key == "FA" && x.Points > 0m);

                if (touchFastConfirm)
                {
                    // Variant A: Sofort-Confirm/Entry Check auf derselben Kerze (wichtig für Range-Bars)
                    tracker.SessionStage = Stage.Confirm;
                    tracker.ConfirmBar = currentSnapshot.Bar;

                    var decConfirmNow = EvaluateDecision(currentSnapshot, prev, candidateZone, thresholds, history, tickSize, tracker);

                    tracker.SessionDecisionHistory ??= new List<DecisionResult>();
                    tracker.SessionDecisionHistory.Add(decConfirmNow);
                    tracker.SessionDecisionBars ??= new List<int>();
                    tracker.SessionDecisionBars.Add(currentSnapshot.Bar);

                    if (decConfirmNow.TotalScore > tracker.SessionBestScore)
                    {
                        tracker.SessionBestScore = decConfirmNow.TotalScore;
                        tracker.SessionBestScoreItems = decConfirmNow.Items != null ? new List<ScoreItem>(decConfirmNow.Items) : null;
                    }

                    const decimal MinSessionBestScoreForEntry = 5m;
                    if (decConfirmNow.Entry && tracker.SessionBestScore >= MinSessionBestScoreForEntry)
                    {
                        EndSession(tracker, "GO (Sofort)");
                        LogSessionProtocol(tracker, candidateZone, history, currentSnapshot, thresholds, tickSize, "GO – Sofort", decConfirmNow);

                        var reasons = new List<string>
                        {
                            $"ZonePullback id={candidateZone.Id}",
                            $"TouchPOC={tracker.TouchPocPrice:F2}",
                            $"Score={decConfirmNow.TotalScore:0.0}",
                            $"Confidence={decConfirmNow.Confidence:0.00}",
                            "Sofort=1"
                        };

                        currentMarketStructureContext.MarkZoneUsed(candidateZone.Id);
                        _trackersByZoneId.Remove(candidateZone.Id);

                        if (_loggerSource != null)
                            LoggerHelper.LogInfo(_loggerSource,
                                $"[ContinuationPullbackV2] DETECTED(SOFORT): zone={candidateZone.Id} dir={_direction} bar={currentSnapshot.Bar} score={decConfirmNow.TotalScore:0.0} conf={decConfirmNow.Confidence:0.00}");

                        return PatternEvaluationResult.Detected(Type, decConfirmNow.Confidence, reasons,
                            new Dictionary<string, object>(), new List<EvaluatedConditionDetail>(), new List<EvaluatedConditionDetail>());
                    }

                    // Sofort-Confirm nicht ausgelöst -> normal weiter (Confirm auf nächster Kerze)
                    tracker.ConfirmBar = currentSnapshot.Bar + 1;
                    return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: Touch ok, fast confirm bar {tracker.ConfirmBar}");
                }

                tracker.SessionStage = Stage.Retest;
                return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: Touch ok, optional retest (<= {tracker.RetestDeadlineBar})");
            }

            if (tracker.SessionStage == Stage.Retest)
            {
                if (currentSnapshot.Bar > tracker.RetestDeadlineBar)
                {
                    tracker.SessionStage = Stage.Confirm;
                    tracker.ConfirmBar = currentSnapshot.Bar + 1;
                    return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: retest missed, go confirm bar {tracker.ConfirmBar}");
                }

                var prev = GetPreviousClosedSnapshot(history, currentSnapshot.Bar, maxLookback: 20);
                tracker.SessionLastEvalBar = currentSnapshot.Bar;
                var dec = EvaluateDecision(currentSnapshot, prev, candidateZone, thresholds, history, tickSize, tracker);

                tracker.SessionDecisionHistory ??= new List<DecisionResult>();
                tracker.SessionDecisionHistory.Add(dec);
                tracker.SessionDecisionBars ??= new List<int>();
                tracker.SessionDecisionBars.Add(currentSnapshot.Bar);

                if (dec.TotalScore > tracker.SessionBestScore)
                {
                    tracker.SessionBestScore = dec.TotalScore;
                    tracker.SessionBestScoreItems = dec.Items != null ? new List<ScoreItem>(dec.Items) : null;
                }

                if (!dec.Allowed)
                {
                    return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: waiting optional retest/confirm");
                }

                tracker.RetestBar = currentSnapshot.Bar;
                tracker.SessionStage = Stage.Confirm;
                tracker.ConfirmBar = currentSnapshot.Bar + 1;

                return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: retest ok, wait confirm bar {tracker.ConfirmBar}");
            }

            if (tracker.SessionStage == Stage.Confirm)
            {
                if (tracker.ConfirmBar.HasValue && currentSnapshot.Bar < tracker.ConfirmBar.Value)
                    return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: waiting confirm");

                if (tracker.ConfirmBar.HasValue && currentSnapshot.Bar > tracker.ConfirmBar.Value)
                {
                    EndSession(tracker, "Confirm-Bar verpasst." );
                    LogSessionProtocol(tracker, candidateZone, history, currentSnapshot, thresholds, tickSize, "NOGO – Confirm verpasst", null);
                    currentMarketStructureContext.MarkZoneUsed(candidateZone.Id);
                    _trackersByZoneId.Remove(candidateZone.Id);
                    return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: confirm missed");
                }

                var prev = GetPreviousClosedSnapshot(history, currentSnapshot.Bar, maxLookback: 20);
                tracker.SessionLastEvalBar = currentSnapshot.Bar;
                var dec = EvaluateDecision(currentSnapshot, prev, candidateZone, thresholds, history, tickSize, tracker);

                tracker.SessionDecisionHistory ??= new List<DecisionResult>();
                tracker.SessionDecisionHistory.Add(dec);
                tracker.SessionDecisionBars ??= new List<int>();
                tracker.SessionDecisionBars.Add(currentSnapshot.Bar);

                if (dec.TotalScore > tracker.SessionBestScore)
                {
                    tracker.SessionBestScore = dec.TotalScore;
                    tracker.SessionBestScoreItems = dec.Items != null ? new List<ScoreItem>(dec.Items) : null;
                }

                const decimal MinSessionBestScoreForEntry = 5m;

                if (!dec.Entry)
                {
                    EndSession(tracker, dec.BlockReasonDe);
                    LogSessionProtocol(tracker, candidateZone, history, currentSnapshot, thresholds, tickSize, "NOGO – keine Bestätigung", dec);
                    currentMarketStructureContext.MarkZoneUsed(candidateZone.Id);
                    _trackersByZoneId.Remove(candidateZone.Id);
                    return PatternEvaluationResult.NotDetected(Type, dec.BlockReasonDe);
                }

                if (tracker.SessionBestScore < MinSessionBestScoreForEntry)
                {
                    EndSession(tracker, $"Score zu niedrig: SessionBestScore {tracker.SessionBestScore:0.0} < {MinSessionBestScoreForEntry:0.0}");
                    LogSessionProtocol(tracker, candidateZone, history, currentSnapshot, thresholds, tickSize, "NOGO – Score zu niedrig", dec);
                    currentMarketStructureContext.MarkZoneUsed(candidateZone.Id);
                    _trackersByZoneId.Remove(candidateZone.Id);
                    return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: entry blocked by score");
                }

                EndSession(tracker, "GO" );
                LogSessionProtocol(tracker, candidateZone, history, currentSnapshot, thresholds, tickSize, "GO", dec);

                var reasons = new List<string>
                {
                    $"ZonePullback id={candidateZone.Id}",
                    $"TouchPOC={tracker.TouchPocPrice:F2}",
                    $"Score={dec.TotalScore:0.0}",
                    $"Confidence={dec.Confidence:0.00}"
                };

                currentMarketStructureContext.MarkZoneUsed(candidateZone.Id);
                _trackersByZoneId.Remove(candidateZone.Id);

                if (_loggerSource != null)
                    LoggerHelper.LogInfo(_loggerSource,
                        $"[ContinuationPullbackV2] DETECTED: zone={candidateZone.Id} dir={_direction} bar={currentSnapshot.Bar} score={dec.TotalScore:0.0} conf={dec.Confidence:0.00}");

                return PatternEvaluationResult.Detected(Type, dec.Confidence, reasons,
                    new Dictionary<string, object>(), new List<EvaluatedConditionDetail>(), new List<EvaluatedConditionDetail>());
            }

            return PatternEvaluationResult.NotDetected(Type, "No stage matched");
        }
    }
}
