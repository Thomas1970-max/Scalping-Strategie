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

        private enum SessionType
        {
            None,
            Immediate,
            Retest
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
			public int SessionLastEvalBar;
			public decimal SessionTouchLow;
			public decimal SessionTouchHigh;
			public bool SessionSawUaToFa;
			public int SessionUaToFaBar;
			public int ConsecutiveBadCloses;
			public int SessionMaxPenetrationTicks;
			public decimal SessionBestScore;
			public List<ScoreItem>? SessionBestScoreItems;
			public List<DecisionResult>? SessionDecisionHistory;
			public List<int>? SessionDecisionBars;
			public string? SessionEndReason;
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
            const int CloseAwayTicksMinFaQuality = 2;
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

                    int closeAwayTicks;
                    if (dir == OrderDirections.Buy)
                        closeAwayTicks = RoundTicks(Math.Max(0m, s.Close - zone.High), tickSize);
                    else
                        closeAwayTicks = RoundTicks(Math.Max(0m, zone.Low - s.Close), tickSize);

                    if (closeAwayTicks >= CloseAwayTicksMinFaQuality)
                        strongFaAtZoneW++;

                    decimal need;
                    if (dir == OrderDirections.Buy)
                        need = zone.High + (ProgressTicksMinMultiFa * tickSize);
                    else
                        need = zone.Low - (ProgressTicksMinMultiFa * tickSize);

                    decimal best = s.Close;
                    for (int nb = b + 1; nb <= Math.Min(maxBar, b + 2); nb++)
                    {
                        OvSnapshot? ns = null;
                        if (nb == curr.Bar)
                            ns = curr;
                        else if (history.TryGetByBar(nb, out var nf) && nf?.Snapshot != null)
                            ns = nf.Snapshot;
                        if (ns == null)
                            continue;
                        if (dir == OrderDirections.Buy)
                            best = Math.Max(best, ns.Close);
                        else
                            best = Math.Min(best, ns.Close);
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

        private DecisionResult EvaluateDecision(
            OvSnapshot curr,
            OvSnapshot? prev,
            OfFeaturesHistory history,
            MarketStructureContext.Zone zone,
            OrderflowThresholds thresholds,
            decimal tickSize,
            OrderDirections dir,
            ZoneTracker? tracker,
            bool diagnosticSoftWhenBlocked = false)
        {
            const decimal EntryThreshold = 6m;
            const int AdaptiveLookback = 30;
            const decimal AbsNetDeltaMedianMultiplier = 1.0m;
            const int PocShiftMinTicks = 1;
            decimal absNetDeltaMin = GetAdaptiveAbsNetDeltaMin(history, curr, AdaptiveLookback, AbsNetDeltaMedianMultiplier);

            int touchesW;
            int faAtZoneW;
            bool progressOk;
            var path = DetermineAllowPath(history, curr, prev, zone, thresholds, tickSize, dir, out touchesW, out faAtZoneW, out progressOk);
            if (tracker != null && tracker.SessionActive && tracker.SessionSawUaToFa)
                path = AllowPath.UaToFa;

            if (path == AllowPath.None)
            {
                var blockedItems = new List<ScoreItem>(16)
                {
                    new ScoreItem
                    {
                        Key = "Allow",
                        Points = 0m,
                        TextDe = $"Harte Freigabe: NEIN – zu wenig Bestätigung am Level (Berührungen letzte 5 Bars: {touchesW}; Finished-Auction an der Zone letzte 5 Bars: {faAtZoneW}; klare Wegbewegung erkennbar: {(progressOk ? "JA" : "NEIN")})." +
                                (tracker != null && tracker.SessionActive
                                    ? $" | UA→FA in Session gesehen: {(tracker.SessionSawUaToFa ? "JA" : "NEIN")}" + (tracker.SessionSawUaToFa ? $" (Bar {tracker.SessionUaToFaBar})" : string.Empty)
                                    : string.Empty)
                    }
                };

                // Diagnose-Modus (Backtest): Soft-Kriterien trotzdem berechnen, aber Entry bleibt gesperrt.
                // So sieht man, ob z.B. DeltaFlip/Absorption vorhanden gewesen wären.
                decimal softScore = 0m;
                if (diagnosticSoftWhenBlocked)
                {
                    bool diagAbsorption = false;
                    if (prev != null)
                        diagAbsorption = ImbalanceNoFollowThrough(prev, curr, zone, tickSize, dir, absNetDeltaMin);
                    var diagProxEval = diagAbsorption ? EvaluateAbsorptionProximity(curr, zone, tickSize, dir) : new ProximityEval { Factor = 0m, ReasonDe = "Zonennähe: n/v" };
                    decimal diagAbsorptionPts = diagAbsorption ? (2m * diagProxEval.Factor) : 0m;
                    softScore += diagAbsorptionPts;
                    blockedItems.Add(new ScoreItem
                    {
                        Key = "Absorption",
                        Points = diagAbsorptionPts,
                        TextDe = diagAbsorption
                            ? $"Absorption (Imbalance ohne Anschluss): JA, {diagProxEval.ReasonDe} (Faktor {diagProxEval.Factor:0.0}) -> +{diagAbsorptionPts:0.0} [Diagnose]"
                            : "Absorption (Imbalance ohne Anschluss): NEIN -> +0 [Diagnose]"
                    });

                    var diagSweepEval = EvaluateSweepPenetration(history, curr, zone, tickSize, dir);
                    decimal diagReactionPts = diagSweepEval.Points;
                    softScore += diagReactionPts;
                    blockedItems.Add(new ScoreItem { Key = "Reaktion", Points = diagReactionPts, TextDe = diagSweepEval.TextDe + " [Diagnose]" });

                    decimal diagConfirmPts;
                    if (dir == OrderDirections.Buy)
                    {
                        int awayTicks = RoundTicks(Math.Max(0m, (curr.Close - zone.High)), tickSize);
                        diagConfirmPts = awayTicks >= 2 ? 2m : (awayTicks >= 1 ? 1m : 0m);
                        bool intentOk = awayTicks <= 0 || curr.PocDelta >= 0m;
                        if (!intentOk)
                            diagConfirmPts = 0m;
                        blockedItems.Add(new ScoreItem
                        {
                            Key = "Bestätigung",
                            Points = diagConfirmPts,
                            TextDe = intentOk
                                ? $"Bestätigung (Long): Schlusskurs {curr.Close:F2} liegt {awayTicks} Ticks über der Zonen-Oberkante {zone.High:F2} -> +{diagConfirmPts:0.0} [Diagnose]"
                                : $"Bestätigung (Long): Close über Zone, aber Verkaufsdruck (POCΔ {curr.PocDelta:+0;-0;0}) -> +0 (Fake-Out Penalty) [Diagnose]"
                        });
                    }
                    else
                    {
                        int awayTicks = RoundTicks(Math.Max(0m, (zone.Low - curr.Close)), tickSize);
                        diagConfirmPts = awayTicks >= 2 ? 2m : (awayTicks >= 1 ? 1m : 0m);
                        bool intentOk = awayTicks <= 0 || curr.PocDelta <= 0m;
                        if (!intentOk)
                            diagConfirmPts = 0m;
                        blockedItems.Add(new ScoreItem
                        {
                            Key = "Bestätigung",
                            Points = diagConfirmPts,
                            TextDe = intentOk
                                ? $"Bestätigung (Short): Schlusskurs {curr.Close:F2} liegt {awayTicks} Ticks unter der Zonen-Unterkante {zone.Low:F2} -> +{diagConfirmPts:0.0} [Diagnose]"
                                : $"Bestätigung (Short): Close unter Zone, aber Kaufdruck (POCΔ {curr.PocDelta:+0;-0;0}) -> +0 (Fake-Out Penalty) [Diagnose]"
                        });
                    }
                    softScore += diagConfirmPts;

                    bool responseEvidenceDiag = diagAbsorption || diagReactionPts > 0m || diagConfirmPts > 0m;

                    if (!responseEvidenceDiag)
                    {
                        blockedItems.Add(new ScoreItem { Key = "DeltaFlip", Points = 0m, TextDe = "Delta-Flip: n/v (kein Turn-Beweis) -> +0 [Diagnose]" });
                    }
                    else
                    {
                        bool diagDeltaFlip = HasDeltaFlipWithinWindow(history, curr, windowBars: 3, dir);
                        decimal diagDeltaPts = diagDeltaFlip ? 2m : 0m;
                        softScore += diagDeltaPts;
                        blockedItems.Add(new ScoreItem { Key = "DeltaFlip", Points = diagDeltaPts, TextDe = $"Delta-Change: {(diagDeltaFlip ? "JA" : "NEIN")} ({(diagDeltaFlip ? "+2" : "+0")}) [Diagnose]" });
                    }

                    decimal diagPocPts = 0m;
                    if (!responseEvidenceDiag)
                    {
                        blockedItems.Add(new ScoreItem { Key = "PocShift", Points = 0m, TextDe = "POC-Shift: n/v (kein Turn-Beweis) -> +0 [Diagnose]" });
                    }
                    else if (prev != null)
                    {
                        decimal pocShift = curr.CandlePocPrice - prev.CandlePocPrice;
                        bool pocDirOk = dir == OrderDirections.Buy ? pocShift >= 0m : pocShift <= 0m;
                        int pocShiftTicks = RoundTicks(Math.Abs(pocShift), tickSize);

                        bool pocMagOk = pocShiftTicks >= PocShiftMinTicks;
                        const int AdaptivePocVolLookback = 30;
                        const decimal PocVolumeMedianMultiplier = 0.8m;
                        decimal pocVolMin = GetAdaptivePocVolumeMin(history, curr, AdaptivePocVolLookback, PocVolumeMedianMultiplier);
                        bool pocVolOk = curr.PocVolume >= pocVolMin;
                        var pocProxEval = EvaluateAbsorptionProximity(curr, zone, tickSize, dir);
                        bool proximityOk = pocProxEval.Factor >= 0.5m;
                        bool pocOk = pocDirOk && pocMagOk && pocVolOk && proximityOk;
                        diagPocPts = (pocOk ? 1m : 0m);
                        softScore += diagPocPts;
                        blockedItems.Add(new ScoreItem { Key = "PocShift", Points = diagPocPts, TextDe = $"POC-Shift: {((pocDirOk && pocMagOk && pocVolOk && proximityOk) ? "OK" : "nicht OK")} ({(diagPocPts > 0m ? "+1" : "+0")}) [Diagnose]" });
                    }
                    else
                    {
                        blockedItems.Add(new ScoreItem { Key = "PocShift", Points = 0m, TextDe = "POC-Shift: n/v (kein vorheriger Bar) -> +0 [Diagnose]" });
                    }

                    blockedItems.Add(new ScoreItem { Key = "Total", Points = softScore, TextDe = $"Gesamt-Score (Diagnose, ohne harte Freigabe): {softScore:0.0} (Schwelle {EntryThreshold:0.0})" });
                }

                return new DecisionResult
                {
                    Allowed = false,
                    Entry = false,
                    Path = AllowPath.None,
                    BaseScore = 0,
                    TotalScore = diagnosticSoftWhenBlocked ? softScore : 0m,
                    Confidence = 0m,
                    BlockReasonDe = "Zulassung fehlt: weder Auktionswechsel (UA→FA) am Level noch Mehrfach-Verteidigung (FA≥2 + Wegbewegung + kein Kleben).",
                    Items = blockedItems
                };
            }

            int baseScore = path == AllowPath.UaToFa ? 3 : 2;
            var items = new List<ScoreItem>(16);
            items.Add(new ScoreItem
            {
                Key = "Base",
                Points = baseScore,
                TextDe = path == AllowPath.UaToFa
                    ? ((tracker != null && tracker.SessionActive && tracker.SessionSawUaToFa && tracker.SessionUaToFaBar >= 0 && tracker.SessionUaToFaBar != curr.Bar)
                        ? $"Basis: +3 (UA→FA in Session gesehen – Latch bei Bar {tracker.SessionUaToFaBar})"
                        : "Basis: +3 (Auktionswechsel UA→FA am Level)")
                    : $"Basis: +2 (Mehrfach-Verteidigung: FA@Zone≥2, Wegbewegung JA, Berührungen W=5={touchesW})"
            });

            decimal score = baseScore;

            bool absorption = false;
            if (prev != null)
                absorption = ImbalanceNoFollowThrough(prev, curr, zone, tickSize, dir, absNetDeltaMin);

            var proxEval = absorption ? EvaluateAbsorptionProximity(curr, zone, tickSize, dir) : new ProximityEval { Factor = 0m, ReasonDe = "Zonennähe: n/v" };
            decimal prox = proxEval.Factor;
            decimal absorptionPts = absorption ? (2m * proxEval.Factor) : 0m;
            score += absorptionPts;
            items.Add(new ScoreItem
            {
                Key = "Absorption",
                Points = absorptionPts,
                TextDe = absorption
                    ? $"Absorption (Imbalance ohne Anschluss): JA, {proxEval.ReasonDe} (Faktor {prox:0.0}) -> +{absorptionPts:0.0}"
                    : "Absorption (Imbalance ohne Anschluss): NEIN -> +0"
            });

            var sweepEval = EvaluateSweepPenetration(history, curr, zone, tickSize, dir);
            decimal reactionPts = sweepEval.Points;
            score += reactionPts;
            items.Add(new ScoreItem { Key = "Reaktion", Points = reactionPts, TextDe = sweepEval.TextDe });

            decimal confirmPts = 0m;
            if (dir == OrderDirections.Buy)
            {
                int awayTicks = RoundTicks(Math.Max(0m, (curr.Close - zone.High)), tickSize);
                confirmPts = awayTicks >= 2 ? 2m : (awayTicks >= 1 ? 1m : 0m);
                bool intentOk = awayTicks <= 0 || curr.PocDelta >= 0m;
                if (!intentOk)
                    confirmPts = 0m;
                items.Add(new ScoreItem
                {
                    Key = "Bestätigung",
                    Points = confirmPts,
                    TextDe = intentOk
                        ? $"Bestätigung (Long): Schlusskurs {curr.Close:F2} liegt {awayTicks} Ticks über der Zonen-Oberkante {zone.High:F2} -> +{confirmPts:0.0}"
                        : $"Bestätigung (Long): Close über Zone, aber Verkaufsdruck (POCΔ {curr.PocDelta:+0;-0;0}) -> +0 (Fake-Out Penalty)"
                });
            }
            else
            {
                int awayTicks = RoundTicks(Math.Max(0m, (zone.Low - curr.Close)), tickSize);
                confirmPts = awayTicks >= 2 ? 2m : (awayTicks >= 1 ? 1m : 0m);
                bool intentOk = awayTicks <= 0 || curr.PocDelta <= 0m;
                if (!intentOk)
                    confirmPts = 0m;
                items.Add(new ScoreItem
                {
                    Key = "Bestätigung",
                    Points = confirmPts,
                    TextDe = intentOk
                        ? $"Bestätigung (Short): Schlusskurs {curr.Close:F2} liegt {awayTicks} Ticks unter der Zonen-Unterkante {zone.Low:F2} -> +{confirmPts:0.0}"
                        : $"Bestätigung (Short): Close unter Zone, aber Kaufdruck (POCΔ {curr.PocDelta:+0;-0;0}) -> +0 (Fake-Out Penalty)"
                });
            }
            score += confirmPts;

            bool responseEvidence = absorption || reactionPts > 0m || confirmPts > 0m;

            if (!responseEvidence)
            {
                items.Add(new ScoreItem { Key = "DeltaFlip", Points = 0m, TextDe = "Delta-Flip: n/v (kein Turn-Beweis) -> +0" });
                items.Add(new ScoreItem { Key = "PocShift", Points = 0m, TextDe = "POC-Shift: n/v (kein Turn-Beweis) -> +0" });
            }
            else
            {
                const decimal DeltaShiftMin = 20m;
                const decimal ProximityMinFactor = 0.5m;
                var deltaFlipProxEval = EvaluateAbsorptionProximity(curr, zone, tickSize, dir);
                decimal deltaPts = 0m;
                string deltaFlipText = "Delta-Flip: NEIN";
                if (deltaFlipProxEval.Factor >= ProximityMinFactor)
                {
                    bool deltaFlipRaw = HasDeltaFlipWithinWindow(history, curr, windowBars: 3, dir);
                    if (deltaFlipRaw && prev != null)
                    {
                        bool currSignOk = dir == OrderDirections.Buy ? curr.PocDelta > 0m : curr.PocDelta < 0m;
                        bool prevSignOk = dir == OrderDirections.Buy ? prev.PocDelta > 0m : prev.PocDelta < 0m;
                        bool persistenceOk = currSignOk && prevSignOk;

                        var prevPrev = GetPreviousClosedSnapshot(history, prev.Bar, maxLookback: 20);
                        OvSnapshot? flipFrom = prevPrev;
                        if (flipFrom != null)
                        {
                            bool flipFromOpp = dir == OrderDirections.Buy ? flipFrom.PocDelta < 0m : flipFrom.PocDelta > 0m;
                            if (!flipFromOpp)
                                flipFrom = null;
                        }

                        if (!persistenceOk)
                        {
                            deltaFlipText = "Delta-Flip: NEIN (Persistenz < 2 Bars)";
                        }
                        else if (flipFrom == null)
                        {
                            deltaFlipText = "Delta-Flip: NEIN (kein Flip-Ursprung für Magnitude/Persistenz)";
                        }
                        else
                        {
                            decimal shift = Math.Abs(curr.PocDelta - flipFrom.PocDelta);
                            if (shift >= DeltaShiftMin)
                            {
                                deltaPts = 2m * deltaFlipProxEval.Factor;
                                deltaFlipText = $"Delta-Flip: JA (Shift={shift:0}, Persistenz=2, Prox={deltaFlipProxEval.Factor:0.0}) -> +{deltaPts:0.0}";
                            }
                            else
                            {
                                deltaFlipText = $"Delta-Flip: NEIN (Magnitude {shift:0} < {DeltaShiftMin:0})";
                            }
                        }
                    }
                    else if (deltaFlipRaw)
                    {
                        deltaFlipText = "Delta-Flip: NEIN (kein prev für Persistenz/Magnitude)";
                    }
                    else
                    {
                        deltaFlipText = "Delta-Flip: NEIN (kein Flip im Fenster)";
                    }
                }
                else
                {
                    deltaFlipText = $"Delta-Flip: NEIN (Zonennähe {deltaFlipProxEval.Factor:0.0} < {ProximityMinFactor:0.0})";
                }

                score += deltaPts;
                items.Add(new ScoreItem { Key = "DeltaFlip", Points = deltaPts, TextDe = deltaFlipText });

                decimal pocPts = 0m;
                if (prev != null)
                {
                    decimal pocShift = curr.CandlePocPrice - prev.CandlePocPrice;
                    int pocShiftTicks = RoundTicks(Math.Abs(pocShift), tickSize);

                    bool dirOk = dir == OrderDirections.Buy ? pocShift >= 0m : pocShift <= 0m;
                    bool magnitudeOk = pocShiftTicks >= PocShiftMinTicks;
                    const int AdaptivePocVolLookback = 30;
                    const decimal PocVolumeMedianMultiplier = 0.8m;
                    decimal pocVolMin = GetAdaptivePocVolumeMin(history, curr, AdaptivePocVolLookback, PocVolumeMedianMultiplier);
                    bool pocVolOk = curr.PocVolume >= pocVolMin;
                    var pocProxEval = EvaluateAbsorptionProximity(curr, zone, tickSize, dir);
                    bool proximityOk = pocProxEval.Factor >= 0.5m;
                    bool pocOk = dirOk && magnitudeOk && pocVolOk && proximityOk;
                    pocPts = (pocOk ? 1m : 0m);
                    string reason = pocOk
                        ? $"POC-Shift: OK (Shift={pocShiftTicks} Ticks, Prox={pocProxEval.Factor:0.0}) -> +1"
                        : $"POC-Shift: nicht OK (Dir={dirOk}, Mag={magnitudeOk}, Vol={pocVolOk}, Prox={proximityOk}) -> +0";
                    score += pocPts;
                    items.Add(new ScoreItem { Key = "PocShift", Points = pocPts, TextDe = reason });
                }
                else
                {
                    items.Add(new ScoreItem { Key = "PocShift", Points = 0m, TextDe = "POC-Shift: n/v (kein vorheriger Bar) -> +0" });
                }
            }

            bool entry = score >= EntryThreshold;
            decimal confidence = 0.40m + Math.Min(0.60m, score * 0.08m);
            if (confidence > 1m) confidence = 1m;

            items.Add(new ScoreItem { Key = "Total", Points = score, TextDe = $"Gesamt-Score: {score:0.0} (Schwelle {EntryThreshold:0.0})" });

            return new DecisionResult
            {
                Allowed = true,
                Entry = entry,
                Path = path,
                BaseScore = baseScore,
                TotalScore = score,
                Confidence = confidence,
                BlockReasonDe = entry ? string.Empty : "Score zu niedrig.",
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
                        currentMarketStructureContext.ConsumeZoneOnExpiry(trackerZoneId);
                        (toRemove ??= new List<int>()).Add(trackerZoneId);
                    }
                }

                if (toRemove != null)
                {
                    for (int i = 0; i < toRemove.Count; i++)
                        _trackersByZoneId.Remove(toRemove[i]);
                }
            }

            // --- Candidate Selection ---
            // 1) Zonen, die aktuell berührt werden
            var prevClosed = GetPreviousClosedSnapshot(history, currentSnapshot.Bar, maxLookback: 12);
            var touchedZones = new List<MarketStructureContext.Zone>(4);
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

            MarketStructureContext.Zone? candidateZone = touchedZones.Count > 0 ? touchedZones[0] : null;
            bool candidateFromTouch = candidateZone != null;

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

            // 2) Fallback: Zone mit aktiver Session (auch ohne Touch)
            if (candidateZone == null)
            {
                foreach (var kv in _trackersByZoneId)
                {
                    var t = kv.Value;
                    if (!t.SessionActive)
                        continue;

                    MarketStructureContext.Zone? sessionZone = null;
                    foreach (var z in zones)
                    {
                        if (z != null && z.Id == kv.Key
                            && (z.Status == MarketStructureContext.ZoneStatus.New || z.Status == MarketStructureContext.ZoneStatus.Ready))
                        {
                            bool dirOk = (_direction == OrderDirections.Buy && z.Type == MarketStructureContext.ZoneType.Support)
                                         || (_direction == OrderDirections.Sell && z.Type == MarketStructureContext.ZoneType.Resistance);
                            if (dirOk)
                                sessionZone = z;
                        }
                    }

                    if (sessionZone != null)
                    {
                        candidateZone = sessionZone;
                        candidateFromTouch = false;
                        break;
                    }
                }
            }

            if (candidateZone == null)
                return PatternEvaluationResult.NotDetected(Type, $"ZoneGate: no touched/session zone for dir={_direction} (zones={zones.Count})");

            // --- Compression Gate ---
            try
            {
                if (candidateZone.IsConfirmed)
                {
                    MarketStructureContext.Zone? nearestOpp = null;
                    decimal bestGap = decimal.MaxValue;

                    foreach (var z in zones)
                    {
                        if (z == null || z.Id == candidateZone.Id || !z.IsConfirmed)
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
                            ? (currentSnapshot.Close >= candidateZone.High && currentSnapshot.Close <= nearestOpp.Low)
                            : (currentSnapshot.Close <= candidateZone.Low && currentSnapshot.Close >= nearestOpp.High);

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
            var tracker = GetOrCreateTracker(candidateZone.Id, currentSnapshot.Bar);

            // ============================================================
            // AKTIVE SESSION WEITERFÜHREN (A oder B)
            // ============================================================
            if (tracker.SessionActive)
            {
                try
                {
                    bool touchNow = TouchesZone(currentSnapshot, candidateZone);
                    if (touchNow && prevClosed != null)
                    {
                        decimal tol = tickSize;
                        bool approachOk = _direction == OrderDirections.Buy
                            ? prevClosed.Close >= (candidateZone.High - tol)
                            : prevClosed.Close <= (candidateZone.Low + tol);
                        if (!approachOk)
                        {
                            LogExplainOnce(
                                currentSnapshot.Bar,
                                candidateZone.Id,
                                stage: "Session.Touch.Blocked.WrongSide",
                                lines: new[]
                                {
                                    $"Dir={_direction}",
                                    $"Zone={candidateZone.Id}",
                                    $"Type={candidateZone.Type}",
                                    $"PrevClose={prevClosed.Close:F2}",
                                    $"Bounds=[{candidateZone.Low:F2}..{candidateZone.High:F2}]",
                                    $"Rule={(candidateZone.Type == MarketStructureContext.ZoneType.Support ? "Support nur von oben" : "Resistance nur von unten")}: Session-Bar nicht entry-relevant"
                                });
                            tracker.SessionLastEvalBar = currentSnapshot.Bar;
                            return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: session touch blocked (wrong side)");
                        }
                    }
                }
                catch { }

                var sessionOutcome = EvaluateActiveSession(
                    tracker, currentSnapshot, history, candidateZone, thresholds, tickSize,
                    MaxSessionBars, MaxConsecutiveBadCloses, MaxSessionPenetrationTicks,
                    out var sessionDecision);

                switch (sessionOutcome)
                {
                    case SessionEvalOutcome.EntryGo:
                    {
                        LogSessionProtocol(tracker, candidateZone, history, currentSnapshot, thresholds, tickSize,
                            MaxSessionBars, MaxConsecutiveBadCloses, MaxSessionPenetrationTicks,
                            "GO – Entry ausgelöst", sessionDecision);

                        var scenarioLabel = tracker.SessionKind == SessionType.Immediate ? "Immediate" : "Retest";
                        var reasons = new List<string>
                        {
                            $"Zone{scenarioLabel} id={candidateZone.Id}",
                            $"Path={(sessionDecision!.Path == AllowPath.UaToFa ? "UAtoFA" : "MultiFA")}",
                            $"Score={sessionDecision.TotalScore:0.0}",
                            $"Confidence={sessionDecision.Confidence:0.00}",
                            $"SessionBars={currentSnapshot.Bar - tracker.SessionStartBar}"
                        };

                        tracker.EntryTriggered = true;
                        _lastValidReversalBarIndex = currentSnapshot.Bar;
                        _lastValidReversalDirection = _direction;

                        currentMarketStructureContext.ConsumeZoneOnEntry(candidateZone.Id);
                        _trackersByZoneId.Remove(candidateZone.Id);

                        if (_loggerSource != null)
                            LoggerHelper.LogInfo(_loggerSource,
                                $"[ReversalBounceV2] DETECTED ({scenarioLabel}): zone={candidateZone.Id} dir={_direction} bar={currentSnapshot.Bar} score={sessionDecision.TotalScore:0.0} conf={sessionDecision.Confidence:0.00}");

                        return PatternEvaluationResult.Detected(Type, sessionDecision.Confidence, reasons,
                            new Dictionary<string, object>(), new List<EvaluatedConditionDetail>(), new List<EvaluatedConditionDetail>());
                    }

                    case SessionEvalOutcome.Invalidated:
                    {
                        // Letzte Decision für Logging (kann null sein wenn Invalidierung vor Scoring kam)
                        var lastDec = sessionDecision ?? (tracker.SessionDecisionHistory?.Count > 0
                            ? tracker.SessionDecisionHistory[tracker.SessionDecisionHistory.Count - 1]
                            : null);
                        LogSessionProtocol(tracker, candidateZone, history, currentSnapshot, thresholds, tickSize,
                            MaxSessionBars, MaxConsecutiveBadCloses, MaxSessionPenetrationTicks,
                            "ABBRUCH – Session invalidiert", lastDec);

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
                        LogSessionProtocol(tracker, candidateZone, history, currentSnapshot, thresholds, tickSize,
                            MaxSessionBars, MaxConsecutiveBadCloses, MaxSessionPenetrationTicks,
                            "VERFALL – Session abgelaufen ohne GO", lastDec);

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
                        bool ok = TryCountAwayClosesSinceFirstTouch(
                            history, firstTouchBar: readyBar, currentBar: currentSnapshot.Bar,
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
                            LogSessionProtocol(tracker, candidateZone, history, currentSnapshot, thresholds, tickSize,
                                MaxSessionBars, MaxConsecutiveBadCloses, MaxSessionPenetrationTicks,
                                "GO – Entry ausgelöst (Sofort auf Touch-Bar)", firstDecision);

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
                    return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: erste Berührung – warte auf Wegbewegung");

                tracker.MovedAwaySeen = true;

                if (!candidateZone.IsConfirmed)
                {
                    return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: pending; waiting zigzag confirmation before retest");
                }

                tracker.Confirmed = true;
                tracker.ConfirmedBar = currentSnapshot.Bar;
                if (_loggerSource != null)
                    LoggerHelper.LogInfo(_loggerSource, $"[ReversalBounceV2] Zone confirmed: id={candidateZone.Id} dir={_direction} firstTouch={tracker.FirstTouchBar} confirmedBar={tracker.ConfirmedBar}");

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
                LogSessionProtocol(tracker, candidateZone, history, currentSnapshot, thresholds, tickSize,
                    MaxSessionBars, MaxConsecutiveBadCloses, MaxSessionPenetrationTicks,
                    "GO – Entry ausgelöst (Sofort auf Retest-Touch-Bar)", retestDecision);

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
            tracker.SessionLastEvalBar = -1;
            tracker.SessionTouchLow = snap.Low;
            tracker.SessionTouchHigh = snap.High;
            tracker.SessionSawUaToFa = false;
            tracker.SessionUaToFaBar = -1;
            tracker.ConsecutiveBadCloses = 0;
            tracker.SessionMaxPenetrationTicks = 0;
            tracker.SessionBestScore = 0m;
            tracker.SessionBestScoreItems = null;
            tracker.SessionDecisionHistory = new List<DecisionResult>();
            tracker.SessionDecisionBars = new List<int>();
            tracker.SessionEndReason = null;
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

            int sessionBarNr = currentSnapshot.Bar - tracker.SessionStartBar + 1;

            // --- Invalidierung: Session-Dauer ---
            if (sessionBarNr > maxSessionBars)
            {
                tracker.SessionActive = false;
                tracker.SessionEndReason = $"Session abgelaufen nach {sessionBarNr} Bars ohne ausreichenden Score (max. {maxSessionBars}).";
                return SessionEvalOutcome.Expired;
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
                tracker.SessionEndReason = $"Session abgebrochen: {tracker.ConsecutiveBadCloses} Bars in Folge mit Close auf der falschen Seite der Zone (Range-Verdacht).";
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
                tracker.SessionEndReason = $"Session abgebrochen: Penetration {tracker.SessionMaxPenetrationTicks} Ticks übersteigt Limit ({maxSessionPenetrationTicks} Ticks). Kein Sweep mehr, sondern Durchbruch.";
                return SessionEvalOutcome.Invalidated;
            }

            // --- Scoring ---
            tracker.SessionLastEvalBar = currentSnapshot.Bar;
            var prev = GetPreviousClosedSnapshot(history, currentSnapshot.Bar, maxLookback: 20);
            if (prev != null)
            {
                bool prevUa = IsUnfinishedAuction(prev, thresholds, _direction);
                bool currFaAtZone = IsFinishedAuction(currentSnapshot, thresholds, _direction)
                    && IsFinishedAuctionAtZone(currentSnapshot, zone, tickSize, _direction);
                if (prevUa && currFaAtZone)
                {
                    tracker.SessionSawUaToFa = true;
                    tracker.SessionUaToFaBar = currentSnapshot.Bar;
                }
            }

            DecisionResult dec = EvaluateDecision(currentSnapshot, prev, history, zone, thresholds, tickSize, _direction, tracker, diagnosticSoftWhenBlocked: true);
            decision = dec;

            tracker.SessionDecisionHistory ??= new List<DecisionResult>();
            tracker.SessionDecisionHistory.Add(dec);

            tracker.SessionDecisionBars ??= new List<int>();
            tracker.SessionDecisionBars.Add(currentSnapshot.Bar);

            if (dec.TotalScore > tracker.SessionBestScore)
            {
                tracker.SessionBestScore = dec.TotalScore;
                tracker.SessionBestScoreItems = dec.Items != null ? new List<ScoreItem>(dec.Items) : null;
            }

            if (dec.Entry)
            {
                tracker.SessionActive = false;
                tracker.SessionEndReason = $"GO – Entry ausgelöst bei Session-Bar {sessionBarNr}.";
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

                const decimal EntryThreshold = 6m;
                var lines = new List<string>(96);
                string dirDe = _direction == OrderDirections.Buy ? "BULLISCH (Kauf)" : "BÄRISCH (Verkauf)";
                int sessionBars = (currentSnapshot.Bar - tracker.SessionStartBar) + 1;
                string zoneStatusDe = zone.IsConfirmed ? "BESTÄTIGT" : "PENDING";

                lines.Add("═══════════════════════════════════════════════════════════");
                lines.Add($"REVERSAL-REPORT | {dirDe} | Zone #{zone.Id} | {zoneStatusDe}");
                lines.Add($"Bereich: {zone.Low:F2} bis {zone.High:F2} | Dauer: {sessionBars} Bars");
                if (tracker.SessionActive && tracker.SessionSawUaToFa && tracker.SessionUaToFaBar >= 0)
                    lines.Add($"UA→FA: gesehen in Session (Latch) bei Bar {tracker.SessionUaToFaBar}");
                lines.Add($"Szenario: {tracker.SessionKind} | Status: {outcome.ToUpperInvariant()}");
                lines.Add("═══════════════════════════════════════════════════════════");
                lines.Add(string.Empty);

                lines.Add("DER PREISVERLAUF:");
                int startBar = tracker.SessionStartBar;
                int endBar = currentSnapshot.Bar;
                OvSnapshot? prevSnap = null;
                OvSnapshot? prevPrevSnap = null;
                int barNumber = 0;

                int green = 0;
                int red = 0;
                int posDeltaBars = 0;
                int negDeltaBars = 0;
                bool hasAbsorption = false;
                bool hasDeltaFlip = false;
                OvSnapshot? firstBarSnap = null;
                OvSnapshot? lastBarSnap = null;
                bool hasFA = false;

                var barsFA = new List<int>();
                var barsAbs = new List<int>();
                var barsFlip = new List<int>();

                for (int b = startBar; b <= endBar; b++)
                {
                    barNumber++;
                    OvSnapshot? s = null;
                    if (b == currentSnapshot.Bar)
                        s = currentSnapshot;
                    else if (history.TryGetByBar(b, out var f) && f?.Snapshot != null)
                    {
                        s = f.Snapshot;
                    }

                    if (s == null)
                        continue;

                    if (firstBarSnap == null)
                        firstBarSnap = s;
                    lastBarSnap = s;

                    string color = s.Close >= s.Open ? "↑" : "↓";
                    string barLabel = s.ChartBarNumber > 0 ? $"K{s.ChartBarNumber}" : $"B{b}";
                    bool closeInZone = s.Close >= zone.Low && s.Close <= zone.High;

                    int chartBar = s.ChartBarNumber;

                    if (s.Close >= s.Open) green++; else red++;
                    if (s.PocDelta > 0m) posDeltaBars++;
                    else if (s.PocDelta < 0m) negDeltaBars++;

                    decimal barScore = 0m;
                    if (tracker.SessionDecisionHistory != null && tracker.SessionDecisionBars != null)
                    {
                        for (int i = 0; i < Math.Min(tracker.SessionDecisionHistory.Count, tracker.SessionDecisionBars.Count); i++)
                        {
                            if (tracker.SessionDecisionBars[i] == b)
                            {
                                barScore = tracker.SessionDecisionHistory[i].TotalScore;
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

                    var events = new List<string>(6);
                    bool faHere = IsFinishedAuction(s, thresholds, _direction) && IsFinishedAuctionAtZone(s, zone, tickSize, _direction);
                    bool uaToFaHere = prevSnap != null
                        && IsUnfinishedAuction(prevSnap, thresholds, _direction)
                        && faHere;
                    if (uaToFaHere)
                        events.Add("UA→FA");
                    if (faHere)
                    {
                        events.Add("★STOPP(FA)");
                        hasFA = true;
                        if (chartBar > 0) barsFA.Add(chartBar);
                    }

                    // Absorption (Chunk B): anchored imbalance + adaptive magnitude + zone-edge rejection
                    if (prevSnap != null)
                    {
                        const int AdaptiveLookback = 30;
                        const decimal AbsNetDeltaMedianMultiplier = 1.0m;
                        decimal absNetDeltaMin = GetAdaptiveAbsNetDeltaMin(history, s, AdaptiveLookback, AbsNetDeltaMedianMultiplier);
                        if (ImbalanceNoFollowThrough(prevSnap, s, zone, tickSize, _direction, absNetDeltaMin))
                        {
                            events.Add("🛡️ABS");
                            hasAbsorption = true;
                            if (chartBar > 0) barsAbs.Add(chartBar);
                        }
                    }

                    // DeltaFlip (Chunk C): proximity gate + flip-in-window + magnitude
                    if (prevSnap != null)
                    {
                        const decimal ProximityMinFactor = 0.5m;
                        const decimal DeltaShiftMin = 20m;
                        var dfProx = EvaluateAbsorptionProximity(s, zone, tickSize, _direction);
                        if (dfProx.Factor >= ProximityMinFactor)
                        {
                            bool deltaFlipRaw = HasDeltaFlipWithinWindow(history, s, windowBars: 3, dir: _direction);
                            if (deltaFlipRaw)
                            {
                                bool currSignOk = _direction == OrderDirections.Buy ? s.PocDelta > 0m : s.PocDelta < 0m;
                                bool prevSignOk = _direction == OrderDirections.Buy ? prevSnap.PocDelta > 0m : prevSnap.PocDelta < 0m;
                                bool persistenceOk = currSignOk && prevSignOk;

                                OvSnapshot? flipFrom = prevPrevSnap;
                                if (flipFrom != null)
                                {
                                    bool flipFromOpp = _direction == OrderDirections.Buy ? flipFrom.PocDelta < 0m : flipFrom.PocDelta > 0m;
                                    if (!flipFromOpp)
                                        flipFrom = null;
                                }

                                decimal shift = flipFrom != null ? Math.Abs(s.PocDelta - flipFrom.PocDelta) : 0m;
                                if (persistenceOk && flipFrom != null && shift >= DeltaShiftMin)
                                {
                                    events.Add("🔄FLIP");
                                    hasDeltaFlip = true;
                                    if (chartBar > 0) barsFlip.Add(chartBar);
                                }
                            }
                        }
                    }

                    string eventStr = events.Count > 0 ? ("| " + string.Join(" ", events)) : string.Empty;
                    lines.Add($"{barLabel}: {color} Δ{s.PocDelta:+0;-0;0} | {pos.PadRight(10)} | Score: {barScore:0.0}/{EntryThreshold:0.0} {eventStr}");

                    prevPrevSnap = prevSnap;
                    prevSnap = s;
                }

                lines.Add(string.Empty);
                lines.Add("MARKT-ANALYSE:");
                bool isLong = _direction == OrderDirections.Buy;

                string FormatBars(List<int> bars)
                {
                    if (bars == null || bars.Count == 0)
                        return string.Empty;
                    var distinct = bars.Distinct().ToList();
                    distinct.Sort();
                    return $" (in K{string.Join(", K", distinct)})";
                }

                if (isLong && firstBarSnap != null && firstBarSnap.PocDelta < -50m)
                    lines.Add("  • ⚡ Aufprall: Verkäufer kamen mit Wucht rein, wurden aber gestoppt.");
                else if (!isLong && firstBarSnap != null && firstBarSnap.PocDelta > 50m)
                    lines.Add("  • ⚡ Aufprall: Käufer stürmten vor, prallten aber ab.");

                if (hasAbsorption)
                    lines.Add($"  • 🛡️ Absorption: Die Gegenseite wurde förmlich 'aufgesogen'. Kein Durchkommen{FormatBars(barsAbs)}.");
                if (hasDeltaFlip)
                    lines.Add($"  • 🔄 Stimmungsumschwung: Die Initiative hat mitten in der Zone gewechselt{FormatBars(barsFlip)}.");
                if (hasFA)
                    lines.Add($"  • ✅ Stop-Signal: Finished Auction (FA) zeigte einen sauberen Stopp an der Zone{FormatBars(barsFA)}.");

                if (lastBarSnap != null)
                {
                    bool breakout = isLong ? lastBarSnap.Close > zone.High : lastBarSnap.Close < zone.Low;
                    if (breakout && ((isLong && lastBarSnap.PocDelta > 0m) || (!isLong && lastBarSnap.PocDelta < 0m)))
                        lines.Add("  • ✅ Bestätigung: Aggressiver Ausbruch aus der Zone bestätigt das Reversal.");
                    else if (breakout)
                        lines.Add("  • ⚠ Warnung: Preis bricht aus, aber der Orderflow passt (noch) nicht sauber.");
                }

                // Bestätigung (Penalty gespiegelt wie im Scoring): nur zählen, wenn Close weg von Zone UND PocΔ-Intent passt
                if (lastBarSnap != null)
                {
                    if (isLong)
                    {
                        int awayTicks = RoundTicks(Math.Max(0m, (lastBarSnap.Close - zone.High)), tickSize);
                        bool intentOk = awayTicks <= 0 || lastBarSnap.PocDelta >= 0m;
                        if (awayTicks >= 1 && intentOk)
                            lines.Add("  • ✅ Bestätigung: Preis bewegt sich weg von der Zone mit Kaufdruck.");
                        else if (awayTicks >= 1 && !intentOk)
                            lines.Add($"  • ⚠ Bestätigung: Preis bewegt sich weg von der Zone, aber Verkaufsdruck (POCΔ {lastBarSnap.PocDelta:+0;-0;0})");
                    }
                    else
                    {
                        int awayTicks = RoundTicks(Math.Max(0m, (zone.Low - lastBarSnap.Close)), tickSize);
                        bool intentOk = awayTicks <= 0 || lastBarSnap.PocDelta <= 0m;
                        if (awayTicks >= 1 && intentOk)
                            lines.Add("  • ✅ Bestätigung: Preis bewegt sich weg von der Zone mit Verkaufsdruck.");
                        else if (awayTicks >= 1 && !intentOk)
                            lines.Add($"  • ⚠ Bestätigung: Preis bewegt sich weg von der Zone, aber Kaufdruck (POCΔ {lastBarSnap.PocDelta:+0;-0;0})");
                    }
                }

                lines.Add(string.Empty);

                string emoji = (finalDecision?.Entry == true) ? "🟢 GO" : "🔴 NO-GO";
                lines.Add($"FAZIT: {emoji} (Score {tracker.SessionBestScore:0.0}/{EntryThreshold:0.0})");

                if (finalDecision?.Entry != true)
                {
                    string grund = finalDecision?.BlockReasonDe ?? tracker.SessionEndReason ?? "Zu wenig Bestätigung";
                    lines.Add($"GRUND FÜR ABLEHNUNG: {grund}");

                    var allowItemText = finalDecision?.Items?.FirstOrDefault(x => x.Key == "Allow")?.TextDe;
                    if (!string.IsNullOrWhiteSpace(allowItemText))
                        lines.Add(allowItemText);
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

        private ZoneTracker GetOrCreateTracker(int zoneId, int currentBar)
        {
            if (_trackersByZoneId.TryGetValue(zoneId, out var tracker))
                return tracker;

            tracker = new ZoneTracker
            {
                ZoneId = zoneId,
                FirstRealTouchBar = currentBar,
                FirstTouchBar = currentBar,
                LastTouchBar = currentBar,
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
                LastTouchedBar = currentBar,
                StoryExitBar = -1,
                // Multi-Bar Session
                SessionActive = false,
                SessionKind = SessionType.None,
                SessionStartBar = -1,
                SessionLastEvalBar = -1,
                SessionTouchLow = 0m,
                SessionTouchHigh = 0m,
                ConsecutiveBadCloses = 0,
                SessionMaxPenetrationTicks = 0,
                SessionBestScore = 0m,
                SessionBestScoreItems = null,
                SessionDecisionHistory = null,
                SessionEndReason = null
            };

            _trackersByZoneId[zoneId] = tracker;
            return tracker;
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
            decimal oneTick = tickSize > 0m ? tickSize : 0.25m;
            if (dir == OrderDirections.Buy)
            {
                // Low must be inside zone, or max 1 tick below it.
                return s.Low <= z.High && s.Low >= (z.Low - oneTick);
            }

            // Short: High must be inside zone, or max 1 tick above it.
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