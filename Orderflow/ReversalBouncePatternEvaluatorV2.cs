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
            const int ProgressTicksMin = 4;
            int maxBar = curr.Bar;
            int minBar = Math.Max(0, maxBar - (W - 1));

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
                    touchesW++;

                bool isFa = IsFinishedAuction(s, thresholds, dir);
                if (isFa && IsFinishedAuctionAtZone(s, zone, tickSize, dir))
                {
                    faAtZoneW++;

                    decimal need;
                    if (dir == OrderDirections.Buy)
                        need = zone.High + (ProgressTicksMin * tickSize);
                    else
                        need = zone.Low - (ProgressTicksMin * tickSize);

                    decimal best = dir == OrderDirections.Buy ? s.High : s.Low;
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

            bool multiFaDefense = faAtZoneW >= 2 && progressOk;
            if (!multiFaDefense)
                return AllowPath.None;

            if (touchesW >= 4)
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
            OvSnapshot curr,
            MarketStructureContext.Zone zone,
            decimal tickSize,
            OrderDirections dir)
        {
            if (tickSize <= 0m)
                tickSize = 0.25m;

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
            bool isSweep = penetrationTicks > 0 && inCorridor && reclaimed;

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
            bool diagnosticSoftWhenBlocked = false)
        {
            const decimal EntryThreshold = 6m;

            int touchesW;
            int faAtZoneW;
            bool progressOk;
            var path = DetermineAllowPath(history, curr, prev, zone, thresholds, tickSize, dir, out touchesW, out faAtZoneW, out progressOk);

            if (path == AllowPath.None)
            {
                var blockedItems = new List<ScoreItem>(16)
                {
                    new ScoreItem
                    {
                        Key = "Allow",
                        Points = 0m,
                        TextDe = $"Harte Freigabe: NEIN – zu wenig Bestätigung am Level (Berührungen letzte 5 Bars: {touchesW}; Finished-Auction an der Zone letzte 5 Bars: {faAtZoneW}; klare Wegbewegung erkennbar: {(progressOk ? "JA" : "NEIN")})."
                    }
                };

                // Diagnose-Modus (Backtest): Soft-Kriterien trotzdem berechnen, aber Entry bleibt gesperrt.
                // So sieht man, ob z.B. DeltaFlip/Absorption vorhanden gewesen wären.
                decimal softScore = 0m;
                if (diagnosticSoftWhenBlocked)
                {
                    bool diagDeltaFlip = HasDeltaFlipWithinWindow(history, curr, windowBars: 3, dir);
                    decimal diagDeltaPts = diagDeltaFlip ? 2m : 0m;
                    softScore += diagDeltaPts;
                    blockedItems.Add(new ScoreItem { Key = "DeltaFlip", Points = diagDeltaPts, TextDe = $"Delta-Change: {(diagDeltaFlip ? "JA" : "NEIN")} ({(diagDeltaFlip ? "+2" : "+0")}) [Diagnose]" });

                    bool diagAbsorption = false;
                    if (prev != null)
                        diagAbsorption = ImbalanceNoFollowThrough(prev, curr, zone, tickSize, dir);
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

                    decimal diagPocPts = 0m;
                    if (prev != null)
                    {
                        decimal pocShift = curr.CandlePocPrice - prev.CandlePocPrice;
                        bool pocOk = dir == OrderDirections.Buy ? pocShift >= 0m : pocShift <= 0m;
                        diagPocPts = pocOk ? 1m : 0m;
                        softScore += diagPocPts;
                        blockedItems.Add(new ScoreItem { Key = "PocShift", Points = diagPocPts, TextDe = $"POC-Shift: {(pocOk ? "OK" : "nicht OK")} ({(pocOk ? "+1" : "+0")}) [Diagnose]" });
                    }
                    else
                    {
                        blockedItems.Add(new ScoreItem { Key = "PocShift", Points = 0m, TextDe = "POC-Shift: n/v (kein vorheriger Bar) -> +0 [Diagnose]" });
                    }

                    var diagSweepEval = EvaluateSweepPenetration(curr, zone, tickSize, dir);
                    decimal diagReactionPts = diagSweepEval.Points;
                    softScore += diagReactionPts;
                    blockedItems.Add(new ScoreItem { Key = "Reaktion", Points = diagReactionPts, TextDe = diagSweepEval.TextDe + " [Diagnose]" });

                    decimal diagConfirmPts;
                    if (dir == OrderDirections.Buy)
                    {
                        int awayTicks = RoundTicks(Math.Max(0m, (curr.Close - zone.High)), tickSize);
                        diagConfirmPts = awayTicks >= 2 ? 2m : (awayTicks >= 1 ? 1m : 0m);
                        blockedItems.Add(new ScoreItem
                        {
                            Key = "Bestätigung",
                            Points = diagConfirmPts,
                            TextDe = $"Bestätigung (Long): Schlusskurs {curr.Close:F2} liegt {awayTicks} Ticks über der Zonen-Oberkante {zone.High:F2} -> +{diagConfirmPts:0.0} [Diagnose]"
                        });
                    }
                    else
                    {
                        int awayTicks = RoundTicks(Math.Max(0m, (zone.Low - curr.Close)), tickSize);
                        diagConfirmPts = awayTicks >= 2 ? 2m : (awayTicks >= 1 ? 1m : 0m);
                        blockedItems.Add(new ScoreItem
                        {
                            Key = "Bestätigung",
                            Points = diagConfirmPts,
                            TextDe = $"Bestätigung (Short): Schlusskurs {curr.Close:F2} liegt {awayTicks} Ticks unter der Zonen-Unterkante {zone.Low:F2} -> +{diagConfirmPts:0.0} [Diagnose]"
                        });
                    }
                    softScore += diagConfirmPts;

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
                    ? "Basis: +3 (Auktionswechsel UA→FA am Level)"
                    : $"Basis: +2 (Mehrfach-Verteidigung: FA@Zone≥2, Wegbewegung JA, Berührungen W=5={touchesW})"
            });

            decimal score = baseScore;

            bool deltaFlip = HasDeltaFlipWithinWindow(history, curr, windowBars: 3, dir);
            decimal deltaPts = deltaFlip ? 2m : 0m;
            score += deltaPts;
            items.Add(new ScoreItem { Key = "DeltaFlip", Points = deltaPts, TextDe = $"Delta-Change: {(deltaFlip ? "JA" : "NEIN")} ({(deltaFlip ? "+2" : "+0")})" });

            bool absorption = false;
            if (prev != null)
                absorption = ImbalanceNoFollowThrough(prev, curr, zone, tickSize, dir);

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

            decimal pocPts = 0m;
            if (prev != null)
            {
                decimal pocShift = curr.CandlePocPrice - prev.CandlePocPrice;
                bool pocOk = dir == OrderDirections.Buy ? pocShift >= 0m : pocShift <= 0m;
                pocPts = pocOk ? 1m : 0m;
                score += pocPts;
                items.Add(new ScoreItem { Key = "PocShift", Points = pocPts, TextDe = $"POC-Shift: {(pocOk ? "OK" : "nicht OK")} ({(pocOk ? "+1" : "+0")})" });
            }
            else
            {
                items.Add(new ScoreItem { Key = "PocShift", Points = 0m, TextDe = "POC-Shift: n/v (kein vorheriger Bar) -> +0" });
            }

            var sweepEval = EvaluateSweepPenetration(curr, zone, tickSize, dir);
            decimal reactionPts = sweepEval.Points;
            score += reactionPts;
            items.Add(new ScoreItem { Key = "Reaktion", Points = reactionPts, TextDe = sweepEval.TextDe });

            decimal confirmPts = 0m;
            if (dir == OrderDirections.Buy)
            {
                int awayTicks = RoundTicks(Math.Max(0m, (curr.Close - zone.High)), tickSize);
                confirmPts = awayTicks >= 2 ? 2m : (awayTicks >= 1 ? 1m : 0m);
                items.Add(new ScoreItem
                {
                    Key = "Bestätigung",
                    Points = confirmPts,
                    TextDe = $"Bestätigung (Long): Schlusskurs {curr.Close:F2} liegt {awayTicks} Ticks über der Zonen-Oberkante {zone.High:F2} -> +{confirmPts:0.0}"
                });
            }
            else
            {
                int awayTicks = RoundTicks(Math.Max(0m, (zone.Low - curr.Close)), tickSize);
                confirmPts = awayTicks >= 2 ? 2m : (awayTicks >= 1 ? 1m : 0m);
                items.Add(new ScoreItem
                {
                    Key = "Bestätigung",
                    Points = confirmPts,
                    TextDe = $"Bestätigung (Short): Schlusskurs {curr.Close:F2} liegt {awayTicks} Ticks unter der Zonen-Unterkante {zone.Low:F2} -> +{confirmPts:0.0}"
                });
            }
            score += confirmPts;

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
                        currentMarketStructureContext.MarkZoneUsed(trackerZoneId);
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
                        if (gapTicks <= compressionGapTicks)
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
                                    $"Blockiert: Gegen-Zone ist zu nah dran.",
                                    $"Abstand={gapTicks} Ticks (Limit={compressionGapTicks}).",
                                    $"Folge: Kein Entry-Check, um Seitwärts-Chaos zu vermeiden."
                                });

                            return PatternEvaluationResult.NotDetected(Type, $"CompressionGate: opposing CONFIRMED zone too close (gapTicks={gapTicks} <= {compressionGapTicks})");
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

                        currentMarketStructureContext.MarkZoneUsed(candidateZone.Id);
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
            if (tracker.FirstTouchTime != default && !tracker.RetestAttempted && !tracker.SessionActive)
            {
                var wait = currentSnapshot.Time - tracker.FirstTouchTime;
                if (wait.TotalMinutes > RetestWaitMinutesMax)
                {
                    currentMarketStructureContext.MarkZoneUsed(candidateZone.Id);
                    _trackersByZoneId.Remove(candidateZone.Id);
                    return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: expired waiting for retest (minutes={wait.TotalMinutes:F1})");
                }
            }

            if (tracker.EntryTriggered)
                return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: entry already triggered");

            if (currentSnapshot.Bar - tracker.FirstTouchBar > MaxBarsAfterFirstTouch)
            {
                currentMarketStructureContext.MarkZoneUsed(candidateZone.Id);
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

                    bool armedStopRun = responseOk && reclaimedInside && extensionOk;
                    bool armedTouch = TouchesZone(currentSnapshot, candidateZone) && reclaimedInside;

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

                            currentMarketStructureContext.MarkZoneUsed(candidateZone.Id);
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

            // Bestätigte Zone, aber kein Touch → warten + Swing-Invalidierung
            if (!TouchesZone(currentSnapshot, candidateZone))
            {
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
                        currentMarketStructureContext.MarkZoneUsed(candidateZone.Id);
                        _trackersByZoneId.Remove(candidateZone.Id);
                        return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: invalidated (new swing high exceeded {level:F2} before retest)");
                    }
                }
                else if (_direction == OrderDirections.Sell && swingSignal?.LastSwingLow.HasValue == true)
                {
                    var level = swingSignal.LastSwingLow.Value - SignificantBreakTicks * tickSize;
                    if (currentSnapshot.Low <= level && currentSnapshot.Bar > tracker.ConfirmedBar)
                    {
                        currentMarketStructureContext.MarkZoneUsed(candidateZone.Id);
                        _trackersByZoneId.Remove(candidateZone.Id);
                        return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: invalidated (new swing low exceeded {level:F2} before retest)");
                    }
                }

                return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: confirmed, waiting retest touch");
            }

            // Retest-Touch erkannt → Gating-Checks
            if (currentSnapshot.Bar - tracker.FirstTouchBar < MinBarsBetweenFirstTouchAndRetest)
                return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: retest too early");

            if (!TryCountAwayClosesSinceFirstTouch(history, tracker.FirstTouchBar, currentSnapshot.Bar, candidateZone, _direction, out var awayCloses)
                || awayCloses < RetestMinAwayCloses)
            {
                return PatternEvaluationResult.NotDetected(Type, $"Zone {candidateZone.Id}: retest ignored (awayClosesInRow={awayCloses} < {RetestMinAwayCloses})");
            }

            // Retest-Touch ist valide → Session starten (statt One-Shot)
            tracker.RetestAttempted = true;
            tracker.RetestBar = currentSnapshot.Bar;
            tracker.RetestAttemptTime = currentSnapshot.Time;
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

                currentMarketStructureContext.MarkZoneUsed(candidateZone.Id);
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

            int sessionBarNr = currentSnapshot.Bar - tracker.SessionStartBar;

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
            decision = EvaluateDecision(currentSnapshot, prev, history, zone, thresholds, tickSize, _direction, diagnosticSoftWhenBlocked: true);

            tracker.SessionDecisionHistory ??= new List<DecisionResult>();
            tracker.SessionDecisionHistory.Add(decision);

            tracker.SessionDecisionBars ??= new List<int>();
            tracker.SessionDecisionBars.Add(currentSnapshot.Bar);

            if (decision.TotalScore > tracker.SessionBestScore)
            {
                tracker.SessionBestScore = decision.TotalScore;
                tracker.SessionBestScoreItems = decision.Items != null ? new List<ScoreItem>(decision.Items) : null;
            }

            if (decision.Entry)
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
                var lines = new List<string>(64);
                string szenario = tracker.SessionKind == SessionType.Immediate ? "A" : "B";
                string dirDe = _direction == OrderDirections.Buy ? "Long" : "Short";
                int sessionBars = (currentSnapshot.Bar - tracker.SessionStartBar) + 1;
                string zoneStateDe = zone.IsConfirmed ? "BESTÄTIGT" : "PENDING";

                lines.Add("═══════════════════════════════════════════════════════════");
                lines.Add($"MULTI-BAR REVERSAL | {dirDe} Zone #{zone.Id} | {sessionBars} Bars | Status: {zoneStateDe} | Szenario {szenario}");
                lines.Add("═══════════════════════════════════════════════════════════");
                lines.Add(string.Empty);

                // --- Kerzen-Übersicht ---
                int startBar = tracker.SessionStartBar;
                int endBar = currentSnapshot.Bar;
                OvSnapshot? prevSnap = null;
                int barNumber = 0;

                int green = 0;
                int red = 0;
                int posDeltaBars = 0;
                int negDeltaBars = 0;
                bool hasAbsorption = false;
                bool hasDeltaFlip = false;
                OvSnapshot? firstBarSnap = null;
                OvSnapshot? lastBarSnap = null;

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

                    string barLabel = FormatBarRef(history, b);
                    string color = s.Close >= s.Open ? "↑" : "↓";
                    string pocDeltaSign = s.PocDelta >= 0m ? "+" : string.Empty;
                    bool touches = s.High >= zone.Low && s.Low <= zone.High;
                    bool closeInZone = s.Close >= zone.Low && s.Close <= zone.High;

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

                    string closeSimple;
                    if (closeInZone)
                    {
                        closeSimple = "IN Zone";
                    }
                    else
                    {
                        if (_direction == OrderDirections.Buy)
                            closeSimple = s.Close > zone.High ? "ÜBER Zone" : "UNTER Zone";
                        else
                            closeSimple = s.Close < zone.Low ? "UNTER Zone" : "ÜBER Zone";
                    }

                    var flags = new List<string>(4);
                    bool faHere = IsFinishedAuction(s, thresholds, _direction)
                        && IsFinishedAuctionAtZone(s, zone, tickSize, _direction);
                    if (faHere)
                        flags.Add("★FA");

                    // Absorption proxy
                    if (prevSnap != null)
                    {
                        bool absorbBuy = s.PocDelta < 0m && s.Close > s.Open;
                        bool absorbSell = s.PocDelta > 0m && s.Close < s.Open;
                        if (_direction == OrderDirections.Buy && absorbBuy)
                        {
                            flags.Add("◆ABS");
                            hasAbsorption = true;
                        }
                        if (_direction == OrderDirections.Sell && absorbSell)
                        {
                            flags.Add("◆ABS");
                            hasAbsorption = true;
                        }
                    }

                    if (prevSnap != null)
                    {
                        bool pocFlip = _direction == OrderDirections.Buy
                            ? (prevSnap.PocDelta < 0m && s.PocDelta > 0m)
                            : (prevSnap.PocDelta > 0m && s.PocDelta < 0m);
                        if (pocFlip)
                        {
                            flags.Add("↳ΔFLIP");
                            hasDeltaFlip = true;
                        }
                    }

                    string eventStr = flags.Count > 0 ? ("| " + string.Join(" ", flags)) : string.Empty;

                    int k = s.ChartBarNumber > 0 ? s.ChartBarNumber : barNumber;
                    lines.Add($"K{k}: {color} Δ{pocDeltaSign}{Math.Abs(s.PocDelta):0} | {closeSimple} | {barScore:0.0}/{EntryThreshold:0.0} {eventStr}");

                    prevSnap = s;
                }

                lines.Add(string.Empty);

                string outcomeEmoji;
                string outcomeText;
                if (finalDecision?.Entry == true)
                {
                    outcomeEmoji = "🟢";
                    outcomeText = "GO — Reversal bestätigt";
                }
                else if (outcome.Contains("VERFALL", StringComparison.OrdinalIgnoreCase))
                {
                    outcomeEmoji = "⏰";
                    outcomeText = "SESSION VERFALLEN";
                }
                else
                {
                    outcomeEmoji = "🔴";
                    outcomeText = "NOGO — Keine Bestätigung";
                }

                lines.Add($"SCORE: {tracker.SessionBestScore:0.0}/{EntryThreshold:0.0} → {outcomeEmoji} {outcomeText}");
                if (tracker.SessionEndReason != null)
                    lines.Add($"Grund: {tracker.SessionEndReason}");
                lines.Add(string.Empty);

                // --- Szenarien-Analyse (12 Szenarien) ---
                lines.Add("SZENARIEN-ANALYSE:");

                bool isLong = _direction == OrderDirections.Buy;

                // 1) Starker Counterattack
                bool strongCounterattack = false;
                if (firstBarSnap != null)
                {
                    // Heuristik: Start-Bar zeigt starken Gegenschlag der Gegenseite (negatives Delta bei Long / positives bei Short)
                    strongCounterattack = _direction == OrderDirections.Buy
                        ? (firstBarSnap.PocDelta < -50m)
                        : (firstBarSnap.PocDelta > 50m);
                }
                if (strongCounterattack)
                    lines.Add(isLong
                        ? "  • ✓ Starker Counterattack: Verkäufer griffen massiv an (Δ < -50)"
                        : "  • ✓ Starker Counterattack: Käufer griffen massiv an (Δ > +50)");

                // 2) Gegenwehr (Farbe + PocDelta kombiniert) – nur ausgeben, wenn zutreffend
                float totalBars = Math.Max(1, green + red);
                float greenRatio = green / totalBars;
                float redRatio = red / totalBars;

                bool strongResistance = isLong
                    ? (greenRatio > 0.66f && posDeltaBars > negDeltaBars)
                    : (redRatio > 0.66f && negDeltaBars > posDeltaBars);
                bool moderateResistance = isLong
                    ? (greenRatio > 0.50f && posDeltaBars >= negDeltaBars)
                    : (redRatio > 0.50f && negDeltaBars >= posDeltaBars);
                bool weakResistance = isLong
                    ? (greenRatio > 0.33f || hasAbsorption)
                    : (redRatio > 0.33f || hasAbsorption);

                if (strongResistance)
                    lines.Add(isLong
                        ? "  • ✓ Starke Gegenwehr: Käufer dominiert (> 66% Grün + Δ+ > Δ-)"
                        : "  • ✓ Starke Gegenwehr: Verkäufer dominiert (> 66% Rot + Δ- > Δ+)");
                else if (moderateResistance)
                    lines.Add(isLong
                        ? "  • ✓ Moderate Gegenwehr: Käufer präsent (> 50% Grün)"
                        : "  • ✓ Moderate Gegenwehr: Verkäufer präsent (> 50% Rot)");
                else if (weakResistance)
                    lines.Add(isLong
                        ? "  • ⊘ Schwache Gegenwehr: Käufer boten kaum Widerstand"
                        : "  • ⊘ Schwache Gegenwehr: Verkäufer boten kaum Widerstand");
                else
                    lines.Add(isLong
                        ? "  • ✗ Keine Gegenwehr: Dominiert durch Rot (< 33% Grün)"
                        : "  • ✗ Keine Gegenwehr: Dominiert durch Grün (< 33% Rot)");

                // 9) Absorption (zeitlich früh, als Teil der Gegenwehr)
                if (hasAbsorption)
                    lines.Add(isLong
                        ? "  • ✓ Absorption: Käufer absorbierten Verkaufsdruck (Gegenwehr erkannt)"
                        : "  • ✓ Absorption: Verkäufer absorbierten Kaufsdruck (Gegenwehr erkannt)");

                // 3) Delta-Flip
                if (hasDeltaFlip)
                    lines.Add(isLong
                        ? "  • ✓ Delta-Flip: Delta drehte konsistent nach oben (echte Käufer-Dominanz)"
                        : "  • ✓ Delta-Flip: Delta drehte konsistent nach unten (echte Verkäufer-Dominanz)");

                // 4) Delta-Pendel
                bool deltaPendulum = posDeltaBars > 0 && negDeltaBars > 0 && !hasDeltaFlip;
                if (deltaPendulum)
                    lines.Add("  • ⊘ Delta-Pendel: Delta pendelte (ambivalente Marktmeinung)");

                // 5-9) Close-Position Szenarien auf Basis der letzten Bar
                if (lastBarSnap != null)
                {
                    bool closeInZone = lastBarSnap.Close >= zone.Low && lastBarSnap.Close <= zone.High;
                    bool closeAboveZone = lastBarSnap.Close > zone.High;
                    bool closeBelowZone = lastBarSnap.Close < zone.Low;

                    if (isLong)
                    {
                        if (closeAboveZone && lastBarSnap.PocDelta > 0m)
                            lines.Add("  • ✓ Breakout über Zone: Close ÜBER Zone mit Kaufdruck → Bullisher Breakout!");
                        else if (closeAboveZone && lastBarSnap.PocDelta < 0m)
                            lines.Add("  • ⚠ Fake-Out: Close ÜBER Zone mit Verkaufsdruck → Verdächtig (Falle?)");
                        else if (closeBelowZone && lastBarSnap.PocDelta < 0m)
                            lines.Add("  • ✗ Breakdown unter Zone: Close UNTER Zone mit Verkaufsdruck → Breakdown! Reversal gescheitert");

                        if (closeInZone)
                        {
                            if (green > red)
                                lines.Add("  • ⊘ Range/Consolidation: Close IN Zone (mit grüner Dominanz) → Käufer consolidieren");
                            else
                                lines.Add("  • ⊘ Range/Consolidation: Close IN Zone aber ohne klare Dominanz");
                        }
                    }
                    else
                    {
                        if (closeBelowZone && lastBarSnap.PocDelta < 0m)
                            lines.Add("  • ✓ Breakout über Zone: Close UNTER Zone mit Verkaufsdruck → Bärischer Breakout!");
                        else if (closeBelowZone && lastBarSnap.PocDelta > 0m)
                            lines.Add("  • ⚠ Fake-Out: Close UNTER Zone mit Kaufdruck → Verdächtig (Falle?)");
                        else if (closeAboveZone && lastBarSnap.PocDelta > 0m)
                            lines.Add("  • ✗ Breakdown unter Zone: Close ÜBER Zone mit Kaufdruck → Breakup! Reversal gescheitert");

                        if (closeInZone)
                        {
                            if (red > green)
                                lines.Add("  • ⊘ Range/Consolidation: Close IN Zone (mit roter Dominanz) → Verkäufer consolidieren");
                            else
                                lines.Add("  • ⊘ Range/Consolidation: Close IN Zone aber ohne klare Dominanz");
                        }
                    }
                }

                // 10) Zu lange Session
                if (sessionBars > maxSessionBars)
                    lines.Add($"  • ⏰ Zu lange Session: Session zu lange ({sessionBars} > {maxSessionBars} Bars) → VERFALLEN");

                // 11) Schwaches Setup
                if (tracker.SessionBestScore < 2m)
                    lines.Add("  • ✗ Schwaches Setup: Kaum Bewegung. Das war kein Reversal-Versuch");
                // 12) Score zu niedrig (nur wenn NICHT schwaches Setup)
                else if (tracker.SessionBestScore < EntryThreshold)
                    lines.Add("  • ⊘ Score zu niedrig: Zu viel Ambivalenz für GO");

                lines.Add(string.Empty);

                lines.Add("GRÜNDE FÜR KEIN-GO:");
                if (finalDecision?.Entry == true)
                {
                    lines.Add("  (n/v – GO)");
                }
                else
                {
                    if (sessionBars > maxSessionBars)
                        lines.Add($"  ❌ Zeitlimit überschritten ({sessionBars} > {maxSessionBars} Bars)");

                    if (finalDecision?.Allowed == false)
                        lines.Add($"  ❌ Zulassung fehlt: {finalDecision.BlockReasonDe}");
                    else if (tracker.SessionBestScore < EntryThreshold)
                        lines.Add($"  ❌ Score zu niedrig ({tracker.SessionBestScore:0.0} < {EntryThreshold:0.0})");
                }

                lines.Add(string.Empty);

                // Fazit (kurz, laienverständlich)
                string fazit;
                if (finalDecision?.Entry == true)
                    fazit = "Alle Kriterien erfüllt → REVERSAL BESTÄTIGT!";
                else if (sessionBars > maxSessionBars)
                    fazit = $"Gegenseite präsent, aber Session zu lange ({sessionBars} > {maxSessionBars} Bars) → VERFALLEN.";
                else if (finalDecision?.Allowed == false)
                    fazit = "Harte Freigabe fehlt → Setup war (noch) nicht zulässig.";
                else
                    fazit = $"Score {tracker.SessionBestScore:0.0}/{EntryThreshold:0.0} → Zu ambivalent für GO.";

                lines.Add($"FAZIT: {fazit}");

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
            for (int i = 0; i < Math.Min(history.Count, 200); i++)
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
            OrderDirections dir)
        {
            decimal oneTick = tickSize;

            if (dir == OrderDirections.Buy)
            {
                bool hasSellImb = (curr.StackedSellImbBottomCount > 0 || curr.StackedSellImbCount > 0) && curr.NetDeltaTotal < 0m;
                if (!hasSellImb)
                    return false;

                // no further downside: low not meaningfully below previous low; and close rejects upwards
                bool noFurtherDown = curr.Low >= prev.Low - oneTick;
                bool rejection = curr.Close >= Math.Max(z.High, prev.Close);
                return noFurtherDown && rejection;
            }
            else
            {
                bool hasBuyImb = (curr.StackedBuyImbTopCount > 0 || curr.StackedBuyImbCount > 0) && curr.NetDeltaTotal > 0m;
                if (!hasBuyImb)
                    return false;

                bool noFurtherUp = curr.High <= prev.High + oneTick;
                bool rejection = curr.Close <= Math.Min(z.Low, prev.Close);
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