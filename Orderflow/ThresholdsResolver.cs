using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ATAS.Indicators;
using ATAS.DataFeedsCore;
using DevExpress.Xpf.Bars;
using MyNamespace.Strategies.Models;
using Utils.Common.Logging;
using static MyNamespace.Strategies.Goldfluss3_3;

namespace MyNamespace.Strategies.Orderflow
{
    public class ThresholdsResolver : IThresholdsResolver
    {
        private const int HistoricalLookbackBars = 30;
        private readonly ILoggerSource _loggerSource;
        private readonly bool _detailedLoggingEnabled;
        private int? _lastLoggedThresholdsBar = null;
        // Thread‑safe LastResult speichern
        private readonly object _lastResultLock = new();
        private AdaptiveThresholdsResult? _lastAdaptiveThresholdsResult;
        public AdaptiveThresholdsResult? LastAdaptiveThresholdsResult
        {
            get { lock (_lastResultLock) return _lastAdaptiveThresholdsResult; }
            private set { lock (_lastResultLock) { _lastAdaptiveThresholdsResult = value; } }
        }
        public ThresholdsResolver(ILoggerSource loggerSource, bool detailedLoggingEnabled = false)
        {
            _loggerSource = loggerSource ?? throw new ArgumentNullException(nameof(loggerSource));
            _detailedLoggingEnabled = detailedLoggingEnabled;

            LoggerHelper.LogInfo(_loggerSource, "[ThresholdsResolver] Instance created.");
            // Initialisiere den ThresholdsPruner-Logger (einmalig; InitializeLogger ist idempotent)
            ThresholdsPruner.InitializeLogger(
                logInfoAction: msg => { /* Pruner nicht für Info-Ausgaben nutzen */ },
                logDebugAction: msg => LoggerHelper.LogDebug(_loggerSource, $"[ThresholdsResolver] {msg}"),
                logWarnAction: msg => LoggerHelper.LogWarn(_loggerSource, $"[ThresholdsResolver] {msg}"),
                logErrorAction: msg => LoggerHelper.LogError(_loggerSource, $"[ThresholdsResolver] {msg}")
            );
        }

        public AdaptiveThresholdsResult CalculateAdaptiveThresholds(
            OrderflowPatternType? assumedOrderflowPatternType,
            PatternCategory? patternCategory,
            MarketRegime regime,
            OfFeaturesHistory? history,
            IReadOnlyDictionary<int, OfFeatures>? featuresByBar,
            ModeSpecsEntry categorySpecs,
            SetupConditionConfig patternConditionConfig,
            SetupConfiguration globalStratConfig,
            OrderflowThresholds? initialThresholds = null,
            int? barIndex = null)
        {
            var swTotal = Stopwatch.StartNew();
            try
            {
                // Prepare working values: if hint not provided, use None for internal logic
                var workingPatternType = assumedOrderflowPatternType ?? OrderflowPatternType.None;
                var workingCategory = patternCategory ?? PatternCategory.Unknown;

                // Collector für kompakte Zusammenfassung
                var summaryParts = new List<string>();
                void AddSummary(string s) => summaryParts.Add(s);

                // Null-sichere Format-Helfer
                string Fmt(decimal? d, string fmt = "F2") => d.HasValue ? d.Value.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture) : "NULL";
                string FmtInt(int? i) => i.HasValue ? i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "NULL";

                // Lokale Hilfsfunktion: einheitliches Final-Logging + SmartLogger-Aufruf
                void LogFinalSummaryAndSmartLog(
                     OrderflowThresholds fullSnapshot,
                     OrderflowThresholds prunedSnapshot,
                     int? curBar,
                     int historyVer,
                     int recentSnapshotCount)
                {
                    swTotal.Stop();
                    
                    // Signatur/compact erzeugen (unabhängig vom Logging-Level)
                    int sigBar = curBar ?? (barIndex ?? -1);
                    string compact = SerializeThresholdsCompact(fullSnapshot);
                    string hash = SmartLogger.ComputeHashHex(compact);
                    var sig = SmartLogger.ComposeSignature(
                        ("barIndex", sigBar.ToString()),
                        ("regime", regime.ToString()),
                        ("historyVersion", historyVer.ToString()),
                        ("thresholdSetHash", hash)
                    );

                    // SmartLogger: weiterhin senden, weil er intern "only if changed" haben sollte
                    SmartLogger.Instance.LogIfChanged(
                        category: "Thresholds",
                        sourceId: "ThresholdsResolver",
                        barIndex: sigBar,
                        message: $"[ThresholdsResolver:Result] pruned={SerializeThresholdsCompact(prunedSnapshot)} | full={compact}",
                        signature: sig,
                        backendLogAction: s => LoggerHelper.LogInfo(_loggerSource, $"[ThresholdsResolver] {s}")
                    );
                    // Entscheide, ob ausführliche INFO-Summary geschrieben werden soll:
                    bool isNewBar = !_lastLoggedThresholdsBar.HasValue || _lastLoggedThresholdsBar.Value != sigBar;

                    // compose compact summary line
                    var finalSummary = $"[ThresholdsResolver:Summary] PatternHint={(assumedOrderflowPatternType.HasValue ? assumedOrderflowPatternType.Value.ToString() : "null")} " +
                                       $"ResolvedPattern={workingPatternType} CategoryHint={(patternCategory.HasValue ? patternCategory.Value.ToString() : "null")} ResolvedCategory={workingCategory} Regime={regime} | " +
                                       string.Join(" | ", summaryParts) +
                                       $" | time={swTotal.ElapsedMilliseconds}ms";

                    // Wenn neue Bar: INFO (eine Zeile pro Bar), ansonsten DEBUG (kompakter)
                    if (isNewBar)
                    {
                        LoggerHelper.LogInfo(_loggerSource, $"[ThresholdsResolver] {finalSummary}");
                        _lastLoggedThresholdsBar = sigBar;
                    }
                    else
                    {
                        // nur als Debug, damit bei mehreren Pattern gleichen Bar nicht die UI überschwemmt wird
                        LoggerHelper.LogDebug(_loggerSource, $"[ThresholdsResolver:Summary-DUP][Bar={sigBar}] {string.Join(" | ", summaryParts)} | time={swTotal.ElapsedMilliseconds}ms");
                    }

                    // zusätzliches Timing-Log immer Debug
                    LoggerHelper.LogDebug(_loggerSource, $"[ThresholdsResolver] [Timing] CalculateAdaptiveThresholds completed in {swTotal.ElapsedMilliseconds} ms (historyCount={recentSnapshotCount})");
                }

                // Initial Start-Zeile in Debug, Summary-Basisdaten hinzufügen
                LoggerHelper.LogDebug(_loggerSource,
                    $"[ThresholdsResolver] [CalculateAdaptiveThresholds] Start PatternHint={(assumedOrderflowPatternType.HasValue ? assumedOrderflowPatternType.Value.ToString() : "null")} " +
                    $"ResolvedPattern={workingPatternType} CategoryHint={(patternCategory.HasValue ? patternCategory.Value.ToString() : "null")} " +
                    $"ResolvedCategory={workingCategory} Regime={regime} initialThresholds? {(initialThresholds != null)}");

                var thresholds = initialThresholds != null ? Clone(initialThresholds) : new OrderflowThresholds();

                if (initialThresholds == null)
                {
                    // Defaults
                    thresholds.MinSignalsRequired = GetDefaultMinSignalsRequired(workingPatternType, workingCategory, regime);
                    thresholds.ThVolBurstZ = GetDefaultVolBurstZ(workingCategory);
                    thresholds.ThAggPressureBreakoutBull = 0.10m;
                    thresholds.ThAggPressureBreakoutBear = 0.10m;
                    thresholds.ThCvdImpulseLong = 20m;
                    thresholds.ThCvdImpulseShort = -20m;
                    thresholds.ThCvdCoherence = GetDefaultCvdCoherence(workingCategory);
                    thresholds.ThTradeRateZBreakout = GetDefaultTradeRateZ(workingCategory);
                    thresholds.ThInterTradeTimeZBull = 0.3m;
                    thresholds.ThInterTradeTimeZBear = -0.3m;
                    thresholds.ThEfficiency = GetDefaultEfficiency(workingCategory);
                    thresholds.MaxCounterDeltaShareBull = 0.4m;
                    thresholds.MaxCounterDeltaShareBear = 0.4m;
                    thresholds.ThStackedImbAnchoredRangeMinDirectional = 1;
                    thresholds.ThStackedImbAnyRangeMinDirectional = 1;
                    thresholds.ThOppositeAnchoredWeakMax = 0;

                    AddSummary($"Defaults: MinSignals={FmtInt(thresholds.MinSignalsRequired)} ,VolBurstZ={Fmt(thresholds.ThVolBurstZ)},CvdCoherence={Fmt(thresholds.ThCvdCoherence)},TradeRateZBreakout={Fmt(thresholds.ThTradeRateZBreakout)}");
                    LoggerHelper.LogDebug(_loggerSource, $"[ThresholdsResolver] [Defaults] applied defaults for category={workingCategory} pattern={workingPatternType}");
                }

                var recentSnapshots = history?
                    .GetLast(HistoricalLookbackBars)
                    ?.Select(of => of?.Snapshot)
                    ?.Where(s => s != null)
                    ?.ToList()
                    ?? new List<OvSnapshot>();

                AddSummary($"History: recentSnapshots.Count={recentSnapshots.Count}(lookback={HistoricalLookbackBars})");

                int historyVersion = history?.Count ?? 0;

                if (!recentSnapshots.Any())
                {
                    LoggerHelper.LogWarn(_loggerSource, "[ThresholdsResolver] [CalculateAdaptiveThresholds] Keine Historie verfügbar; Standardwerte zurücksetzen und zurückkehren.");
                    // Wir möchten trotzdem beide Repräsentationen liefern: full (vor Prune) und pruned (nach Prune).
                    var fullSnapshot = Clone(thresholds);
                    var nullified = ThresholdsPruner.PruneIrrelevant(thresholds, workingCategory, patternConditionConfig);
                    LoggerHelper.LogDebug(_loggerSource, $"[ThresholdsResolver] [Pruner] Ungültige Felder (kein Verlauf): {string.Join(',', (nullified as IEnumerable<string>) ?? Enumerable.Empty<string>())}");
                    AddSummary($"Result: pruned={SerializeThresholdsCompact(thresholds)} | full={SerializeThresholdsCompact(fullSnapshot)}");

                    LogFinalSummaryAndSmartLog(
                        fullSnapshot: fullSnapshot,
                        prunedSnapshot: Clone(thresholds),
                        curBar: barIndex ?? (int?)null,
                        historyVer: historyVersion,
                        recentSnapshotCount: recentSnapshots.Count);

                    return new AdaptiveThresholdsResult
                    {
                        Full = fullSnapshot,
                        Pruned = Clone(thresholds),
                        HistoryVersion = historyVersion,
                        ResolvedCategory = workingCategory,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                }

                // --- VolBurstZ ---
                var volBurstZValues = recentSnapshots.Select(s => Math.Abs(s.VolBurstZ)).Where(v => v > 0).ToList();
                if (volBurstZValues.Any())
                {
                    var avg = volBurstZValues.DefaultIfEmpty(0m).Average();
                    var std = CalculateStandardDeviation(volBurstZValues, avg);
                    LoggerHelper.LogDebug(_loggerSource, $"[ThresholdsResolver] [Stats][VolBurstZ] n={volBurstZValues.Count} avg={avg:F3} std={std:F3} min={volBurstZValues.Min():F3} max={volBurstZValues.Max():F3}");

                    if (workingCategory == PatternCategory.Reversal || regime == MarketRegime.Slow)
                        thresholds.ThVolBurstZ = Math.Min(2.5m, Math.Max(0.5m, avg + std * 0.5m)); // Angepasst von 1.2m auf 2.5m

                    else if (workingCategory == PatternCategory.Breakout || regime == MarketRegime.Fast)
                        thresholds.ThVolBurstZ = Math.Min(3.0m, Math.Max(1.5m, avg + std * 1.5m));

                    thresholds.ThVolBurstZ = Math.Min(3.0m, Math.Max(0.7m, thresholds.ThVolBurstZ ?? avg));
                    AddSummary($"VolBurstZ={Fmt(thresholds.ThVolBurstZ, "F3")}(n={volBurstZValues.Count})");
                }
                else
                {
                    AddSummary("VolBurstZ=NO_VALUES");
                    LoggerHelper.LogDebug(_loggerSource, "[ThresholdsResolver] [VolBurstZ] Keine Werte ungleich Null in den letzten Momentaufnahmen.");
                }

                // --- CvdImpulse ---
                if (thresholds == null) throw new ArgumentNullException(nameof(thresholds));
                const int minSampleSize = 5; // anpassbar
                var cvdLongValues = recentSnapshots.Select(s => s.CvdImpulse).Where(v => v > 10m).ToList();
                var cvdShortValues = recentSnapshots.Select(s => s.CvdImpulse).Where(v => v < -10m).ToList();

                int countLong = cvdLongValues.Count;
                int countShort = cvdShortValues.Count;

                // Default: keine thresholds verwenden
                thresholds.ThCvdImpulseLong = null;
                thresholds.ThCvdImpulseShort = null;

                // Long: nur berechnen, wenn ausreichend positive Samples vorhanden
                if (countLong == 0)
                {
                    AddSummary("CvdLong: n=0");
                    LoggerHelper.LogDebug(_loggerSource, "[ThresholdsResolver] [Stats][CvdImpulse] positive count=0 (no positive directional data)");
                }
                else
                {
                    decimal avgLong = cvdLongValues.Average();
                    decimal stdLong = CalculateStandardDeviation(cvdLongValues, avgLong);

                    string stabilityFlagLong = countLong < minSampleSize ? $" unstable(count={countLong}<{minSampleSize})" : string.Empty;
                    if (countLong >= minSampleSize)
                    {
                        var multiplier = (regime == MarketRegime.Normal || workingCategory == PatternCategory.MeanReversion) ? 0.3m : 0.8m;
                        decimal rawLong = avgLong + stdLong * multiplier;
                        decimal clampedLong = Math.Min(100m, Math.Max(10m, rawLong));
                        thresholds.ThCvdImpulseLong = clampedLong;
                        AddSummary($"CvdLong: n={countLong}{stabilityFlagLong} min={cvdLongValues.Min():F2} max={cvdLongValues.Max():F2} avg={avgLong:F2} std={stdLong:F2} => ThCvdImpulseLong={Fmt(thresholds.ThCvdImpulseLong, "F2")}{(clampedLong != rawLong ? "(clamped)" : "")}");
                        LoggerHelper.LogDebug(_loggerSource, $"[ThresholdsResolver] [CvdImpulse:RAW][Long] multiplier={multiplier:F2} => rawLong={rawLong:F2}");
                    }
                    else
                    {
                        AddSummary($"CvdLong: n={countLong}{stabilityFlagLong} => ThCvdImpulseLong=NULL");
                        LoggerHelper.LogDebug(_loggerSource, $"[ThresholdsResolver] [Stats][CvdImpulse][Long] n={countLong} min={cvdLongValues.Min():F2} max={cvdLongValues.Max():F2} avg={avgLong:F2} std={stdLong:F2}");
                    }
                }

                // Short: nur berechnen, wenn ausreichend negative Samples vorhanden
                if (countShort == 0)
                {
                    AddSummary("CvdShort: n=0");
                    LoggerHelper.LogDebug(_loggerSource, "[ThresholdsResolver] [Stats][CvdImpulse] negative count=0 (no negative directional data)");
                }
                else
                {
                    decimal avgShort = cvdShortValues.Average();
                    decimal stdShort = CalculateStandardDeviation(cvdShortValues, avgShort);

                    string stabilityFlagShort = countShort < minSampleSize ? $" unstable(count={countShort}<{minSampleSize})" : string.Empty;
                    if (countShort >= minSampleSize)
                    {
                        var multiplier = (regime == MarketRegime.Normal || workingCategory == PatternCategory.MeanReversion) ? 0.3m : 0.8m;
                        decimal rawShort = avgShort - stdShort * multiplier;
                        decimal clampedShort = Math.Max(-100m, Math.Min(-10m, rawShort));
                        thresholds.ThCvdImpulseShort = clampedShort;
                        AddSummary($"CvdShort: n={countShort}{stabilityFlagShort} min={cvdShortValues.Min():F2} max={cvdShortValues.Max():F2} avg={avgShort:F2} std={stdShort:F2} => ThCvdImpulseShort={Fmt(thresholds.ThCvdImpulseShort, "F2")}{(clampedShort != rawShort ? "(clamped)" : "")}");
                        LoggerHelper.LogDebug(_loggerSource, $"[ThresholdsResolver] [CvdImpulse:RAW][Short] multiplier={multiplier:F2} => rawShort={rawShort:F2}");
                    }
                    else
                    {
                        AddSummary($"CvdShort: n={countShort}{stabilityFlagShort} => ThCvdImpulseShort=NULL");
                        LoggerHelper.LogDebug(_loggerSource, $"[ThresholdsResolver] [Stats][CvdImpulse][Short] n={countShort} min={cvdShortValues.Min():F2} max={cvdShortValues.Max():F2} avg={avgShort:F2} std={stdShort:F2}");
                    }
                }

                thresholds.IsCvdStable = (countLong >= minSampleSize) || (countShort >= minSampleSize);
                if (!thresholds.IsCvdStable)
                {
                    AddSummary($"CvdStability=UNSTABLE(pos={countLong},neg={countShort})");
                    LoggerHelper.LogDebug(_loggerSource, $"[ThresholdsResolver] [CvdImpulse] insgesamt instabil (positiveSamples={countLong}, negativeSamples={countShort}); thresholds disabled for insufficient directional data.");
                }
                else
                {
                    AddSummary($"CvdStability=OK(pos={countLong},neg={countShort})");
                }

                // --- CvdCoherence adjustments for InterTradeTimeZ ---
                if (thresholds.ThCvdCoherence.HasValue && thresholds.ThCvdCoherence > 0.8m)
                {
                    var beforeBull = thresholds.ThInterTradeTimeZBull;
                    var beforeBear = thresholds.ThInterTradeTimeZBear;
                    thresholds.ThInterTradeTimeZBull = Math.Max(0.1m, (thresholds.ThInterTradeTimeZBull ?? 0m) * 0.8m);
                    thresholds.ThInterTradeTimeZBear = Math.Min(-0.1m, (thresholds.ThInterTradeTimeZBear ?? 0m) * 0.8m);
                    AddSummary($"CvdCoherence>0.8: InterTradeTimeZBull {Fmt(beforeBull)}->{Fmt(thresholds.ThInterTradeTimeZBull)} , InterTradeTimeZBear {Fmt(beforeBear)}->{Fmt(thresholds.ThInterTradeTimeZBear)}");
                    LoggerHelper.LogDebug(_loggerSource, $"[ThresholdsResolver] [CvdCoherence] scaled InterTradeTimeZ (beforeBull={beforeBull}, afterBull={thresholds.ThInterTradeTimeZBull}, beforeBear={beforeBear}, afterBear={thresholds.ThInterTradeTimeZBear})");
                }

                // --- Stacked Imbalance ---
                var stackedImbCountsEnumerable = recentSnapshots.Select(s => (s.StackedBuyImbCount + s.StackedSellImbCount)).Where(c => c > 0);
                var stackedImbCounts = stackedImbCountsEnumerable as List<int> ?? stackedImbCountsEnumerable.ToList();
                if (stackedImbCounts.Any())
                {
                    stackedImbCounts.Sort();
                    int median = stackedImbCounts[stackedImbCounts.Count / 2];
                    var beforeAnchored = thresholds.ThStackedImbAnchoredRangeMinDirectional;
                    var beforeAny = thresholds.ThStackedImbAnyRangeMinDirectional;
                    thresholds.ThStackedImbAnchoredRangeMinDirectional = Math.Max(1, median / 2);
                    thresholds.ThStackedImbAnyRangeMinDirectional = Math.Max(1, median);
                    thresholds.ThOppositeAnchoredWeakMax = 0;
                    AddSummary($"StackedImb: n={stackedImbCounts.Count} median={median} Anchored:{FmtInt(beforeAnchored)}->{FmtInt(thresholds.ThStackedImbAnchoredRangeMinDirectional)} Any:{FmtInt(beforeAny)}->{FmtInt(thresholds.ThStackedImbAnyRangeMinDirectional)}");
                    if (_detailedLoggingEnabled)
                        LoggerHelper.LogDebug(_loggerSource, $"[ThresholdsResolver] [StackedImbalance] values sample: {string.Join(',', stackedImbCounts.Take(20))}");
                }
                else
                {
                    AddSummary("StackedImb: NONE -> defaults(Anchored=1,Any=1)");
                    thresholds.ThStackedImbAnchoredRangeMinDirectional = 1;
                    thresholds.ThStackedImbAnyRangeMinDirectional = 1;
                    thresholds.ThOppositeAnchoredWeakMax = 0;
                    LoggerHelper.LogDebug(_loggerSource, "[ThresholdsResolver] [StackedImbalance] Keine gestapelten Ungleichgewichte gefunden; Verwendung der Standardeinstellungen.");
                }

                // --- BEGIN PROFILE ADJUSTMENT (Phase 6) ---
                try
                {
                    var profile = globalStratConfig?.ActiveTradeProfile ?? TradeProfile.Neutral;
                    var profileParams = profile.GetProfileParameters();

                    // VolBurstZ: multiplicative, lower-bounded
                    if (thresholds.ThVolBurstZ.HasValue)
                        thresholds.ThVolBurstZ = Math.Max(0.1m, thresholds.ThVolBurstZ.Value * (decimal)profileParams.VolBurstZMultiplier);

                    // TradeRateZ (Breakout)
                    if (thresholds.ThTradeRateZBreakout.HasValue)
                        thresholds.ThTradeRateZBreakout = thresholds.ThTradeRateZBreakout.Value * (decimal)profileParams.TradeRateZMultiplier;

                    // MinSignalsRequired: additive offset, clamp >= 1
                    if (thresholds.MinSignalsRequired is int currentMin)
                    {
                        int adjusted = Math.Max(1, currentMin + profileParams.MinSignalsOffset);
                        thresholds.MinSignalsRequired = adjusted;
                    }

                    // CvdImpulseLong/Short: scale magnitude, preserve sign, enforce minimum magnitude 1
                    if (thresholds.ThCvdImpulseLong.HasValue)
                    {
                        var mag = Math.Max(1m, Math.Abs(thresholds.ThCvdImpulseLong.Value) * (decimal)profileParams.CvdImpulseClampMultiplier);
                        thresholds.ThCvdImpulseLong = Math.Sign(thresholds.ThCvdImpulseLong.Value) * mag;
                    }

                    if (thresholds.ThCvdImpulseShort.HasValue)
                    {
                        var mag = Math.Max(1m, Math.Abs(thresholds.ThCvdImpulseShort.Value) * (decimal)profileParams.CvdImpulseClampMultiplier);
                        thresholds.ThCvdImpulseShort = Math.Sign(thresholds.ThCvdImpulseShort.Value) * mag;
                    }

                    // Add to summary (compact) + debug the multipliers applied (null-safe formats)
                    AddSummary($"ProfileAdjust: profile={profile} VolBurstZ={Fmt(thresholds.ThVolBurstZ)} TradeRateZ={Fmt(thresholds.ThTradeRateZBreakout)} MinSignals={FmtInt(thresholds.MinSignalsRequired)} CvdLong={(thresholds.ThCvdImpulseLong.HasValue ? thresholds.ThCvdImpulseLong.Value.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) : "NULL")} CvdShort={(thresholds.ThCvdImpulseShort.HasValue ? thresholds.ThCvdImpulseShort.Value.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) : "NULL")}");
                    LoggerHelper.LogDebug(_loggerSource,
                        $"[ProfileAdjust] Applied profile={profile} VolBurstZMul={profileParams.VolBurstZMultiplier:F2} TradeRateZMul={profileParams.TradeRateZMultiplier:F2} MinSignalsOffset={profileParams.MinSignalsOffset} CvdClampMul={profileParams.CvdImpulseClampMultiplier:F2}");
                }
                catch (Exception exProfile)
                {
                    LoggerHelper.LogDebug(_loggerSource, $"[ThresholdsResolver] [ProfileAdjust] Fehler beim Anwenden des TradeProfile: {exProfile.Message}");
                    AddSummary("ProfileAdjust: ERROR");
                }
                // --- END PROFILE ADJUSTMENT ---

                // --- Prune irrelevant thresholds (mit Bar-Context & Logging) ---
                // bestimme currentBar (falls Du ihn schon als Parameter übergibst, nutze diesen):
                int? currentBar = null;
                if (barIndex.HasValue)
                {
                    currentBar = barIndex;
                }
                else
                {
                    try
                    {
                        if (history != null)
                        {
                            var lastOf = history.GetLast(1)?.FirstOrDefault();
                            if (lastOf != null)
                            {
                                var prop = lastOf.GetType().GetProperty("Bar") ?? lastOf.GetType().GetProperty("BarIndex") ?? lastOf.GetType().GetProperty("Index");
                                if (prop != null)
                                {
                                    var val = prop.GetValue(lastOf);
                                    if (val != null) currentBar = Convert.ToInt32(val);
                                }
                            }
                        }

                        if (!currentBar.HasValue && featuresByBar != null && featuresByBar.Count > 0)
                        {
                            currentBar = featuresByBar.Keys.Max();
                        }
                    }
                    catch
                    {
                        currentBar = null;
                    }
                }
                string currentPatternName = workingPatternType.ToString();

                // Debug: Zustand vor dem Prunen (immer Debug, nicht Info)
                LoggerHelper.LogDebug(_loggerSource, $"[ThresholdsResolver] [Pruner] Before pruning: {SerializeThresholdsCompact(thresholds)} Context: Bar={currentBar?.ToString() ?? "n/a"} Pattern={currentPatternName}");

                // CLONE full snapshot BEFORE pruning (für MarketStateEngine)
                var fullSnapshotBeforePrune = Clone(thresholds);

                // Aufruf mit zusätzlichem Kontext (Pruner gibt nur Debug für einzelne Nullify Aktionen aus)
                GateMetric keep = GateMetric.None;
                if (workingPatternType == OrderflowPatternType.None || workingCategory == PatternCategory.Unknown)
                {
                    // Schutz für Kern-Gates in Erkennungsmodus
                    keep |= GateMetric.CvdImpulse | GateMetric.TradeRateZ | GateMetric.VolBurstZ;
                }

                var nullifiedAfterPrune = ThresholdsPruner.PruneIrrelevant(
                    thresholds,
                    workingCategory,
                    patternConditionConfig,
                    callerCategorySpecs: categorySpecs,
                    keepMask: keep,
                    barIndex: currentBar,
                    patternName: currentPatternName
                );

                // Normalize nullified list to deterministic, sorted, comma-separated string (no spaces)
                var nullifiedEnumerable = (nullifiedAfterPrune as IEnumerable<string>) ??
                                          ((nullifiedAfterPrune is System.Collections.IEnumerable nonGen)
                                              ? nonGen.Cast<object>().Where(x => x is string).Cast<string>()
                                              : Enumerable.Empty<string>());

                var nullifiedListSorted = nullifiedEnumerable.Where(s => !string.IsNullOrEmpty(s)).Select(s => s.Trim()).Distinct().OrderBy(s => s).ToList();
                string nullifiedJoined = string.Join(",", nullifiedListSorted);

                // Wenn kein Bar-Kontext gegeben ist, nur Debug-Logging (keine Info!, um "Bar=n/a" INFO-Duplizate zu vermeiden)
                if (!currentBar.HasValue)
                {
                    if (nullifiedListSorted.Any())
                    {
                        LoggerHelper.LogDebug(_loggerSource,
                            $"[ThresholdsResolver] [Pruner] Ungültige Felder: {nullifiedJoined} Context: Bar=n/a Pattern={currentPatternName} | before={SerializeThresholdsCompact(fullSnapshotBeforePrune)} | after={SerializeThresholdsCompact(thresholds)}");
                        AddSummary($"Pruner: nullified={nullifiedJoined}(Bar=n/a)");
                    }
                    else
                    {
                        LoggerHelper.LogDebug(_loggerSource, $"[ThresholdsResolver] [Pruner] Keine Felder ungültig. Context: Bar=n/a Pattern={currentPatternName}");
                        AddSummary("Pruner: nullified=NONE(Bar=n/a)");
                    }
                }
                else
                {
                    // Bar vorhanden => in summaryParts aufnehmen (einzige Info-Zeile pro Bar)
                    if (nullifiedListSorted.Any())
                    {
                        AddSummary($"Pruner:nullified={nullifiedJoined}(Bar={currentBar})");
                        LoggerHelper.LogInfo(_loggerSource,
                            $"[ThresholdsResolver] [Pruner] Ungültige Felder: {nullifiedJoined} Context: Bar={currentBar} Pattern={currentPatternName} | before={SerializeThresholdsCompact(fullSnapshotBeforePrune)} | after={SerializeThresholdsCompact(thresholds)}");
                    }
                    else
                    {
                        AddSummary($"Pruner:nullified=NONE(Bar={currentBar})");
                        LoggerHelper.LogDebug(_loggerSource, $"[ThresholdsResolver] [Pruner] Keine Felder ungültig. Context: Bar={currentBar} Pattern={currentPatternName}");
                    }
                }

                // Debug: Zustand nach dem Prunen
                LoggerHelper.LogDebug(_loggerSource, $"[ThresholdsResolver] [Pruner] After pruning: {SerializeThresholdsCompact(thresholds)} Context: Bar={currentBar?.ToString() ?? "n/a"} Pattern={currentPatternName}");

                // --- Ende Pruner-Block ---
                // Füge Resultat-Summary hinzu (wird später in einer Zeile geloggt)
                AddSummary($"Result pruned={SerializeThresholdsCompact(thresholds)} | full={SerializeThresholdsCompact(fullSnapshotBeforePrune)}");

                // Final logging & SmartLogger via helper
                LogFinalSummaryAndSmartLog(
                    fullSnapshot: fullSnapshotBeforePrune,
                    prunedSnapshot: Clone(thresholds),
                    curBar: currentBar,
                    historyVer: historyVersion,
                    recentSnapshotCount: recentSnapshots.Count);

                // Erweitertes Result mit Pruner-Info
                var result = new AdaptiveThresholdsResult
                {
                    Full = fullSnapshotBeforePrune,
                    Pruned = Clone(thresholds),
                    HistoryVersion = historyVersion,
                    ResolvedCategory = workingCategory,
                    CreatedAtUtc = DateTime.UtcNow,

                    // Neu: normalisierte, deterministische Nullified-Infos
                    NullifiedJoined = nullifiedJoined ?? string.Empty,
                    NullifiedList = nullifiedListSorted.AsReadOnly()
                };
                // setze LastAdaptiveThresholdsResult (thread-safe)
                this.LastAdaptiveThresholdsResult = result;

                return result;
            }
            catch (Exception ex)
            {
                swTotal.Stop();
                LoggerHelper.LogError(_loggerSource, $"[CalculateAdaptiveThresholds] Exception: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        public OrderflowThresholds? Clone(OrderflowThresholds original)
        {
            if (original == null) return null;
            return new OrderflowThresholds
            {
                MinSignalsRequired = original.MinSignalsRequired,
                ThVolBurstZ = original.ThVolBurstZ,
                ThAggPressureBreakoutBull = original.ThAggPressureBreakoutBull,
                ThAggPressureBreakoutBear = original.ThAggPressureBreakoutBear,
                ThCvdImpulseLong = original.ThCvdImpulseLong,
                ThCvdImpulseShort = original.ThCvdImpulseShort,
                ThCvdCoherence = original.ThCvdCoherence,
                ThTradeRateZBreakout = original.ThTradeRateZBreakout,
                ThInterTradeTimeZBull = original.ThInterTradeTimeZBull,
                ThInterTradeTimeZBear = original.ThInterTradeTimeZBear,
                ThEfficiency = original.ThEfficiency,
                MaxCounterDeltaShareBull = original.MaxCounterDeltaShareBull,
                MaxCounterDeltaShareBear = original.MaxCounterDeltaShareBear,
                ThStackedImbAnchoredRangeMinDirectional = original.ThStackedImbAnchoredRangeMinDirectional,
                ThStackedImbAnyRangeMinDirectional = original.ThStackedImbAnyRangeMinDirectional,
                ThOppositeAnchoredWeakMax = original.ThOppositeAnchoredWeakMax
            };
        }

        #region Defaults & Helpers

        private static int GetDefaultMinSignalsRequired(OrderflowPatternType orderflowPatternType, PatternCategory patternCategory, MarketRegime regime)
        {
            if (patternCategory == PatternCategory.Reversal) return 2;
            if (patternCategory == PatternCategory.Breakout || patternCategory == PatternCategory.Continuation) return 3;
            return 1;
        }

        private static decimal? GetDefaultVolBurstZ(PatternCategory patternCategory)
        {
            if (patternCategory == PatternCategory.Reversal || patternCategory == PatternCategory.MeanReversion) return 0.9m;
            if (patternCategory == PatternCategory.Breakout || patternCategory == PatternCategory.Continuation) return 1.5m;
            return 1.0m;
        }

        private static decimal? GetDefaultCvdCoherence(PatternCategory patternCategory)
        {
            if (patternCategory == PatternCategory.Reversal || patternCategory == PatternCategory.MeanReversion) return 0.65m;
            return 0.75m;
        }

        private static decimal? GetDefaultTradeRateZ(PatternCategory patternCategory)
        {
            if (patternCategory == PatternCategory.Reversal || patternCategory == PatternCategory.MeanReversion) return 0.4m;
            return 0.8m;
        }

        private static decimal? GetDefaultEfficiency(PatternCategory patternCategory)
        {
            if (patternCategory == PatternCategory.MeanReversion || patternCategory == PatternCategory.Reversal) return 0.02m;
            return 0.03m;
        }

        private static decimal CalculateStandardDeviation(List<decimal> values, decimal average)
        {
            if (values == null || values.Count <= 1) return 0m;
            decimal sum = values.Sum(v => (v - average) * (v - average));
            return (decimal)Math.Sqrt((double)(sum / (values.Count - 1)));
        }

        private string SerializeThresholdsCompact(OrderflowThresholds t)
        {
            if (t == null) return "<null>";
            // Verwende InvariantCulture-formatiert und konsistente NULL-Token (NULL)
            string fmt(decimal? d, string format = "F2") => d.HasValue ? d.Value.ToString(format, System.Globalization.CultureInfo.InvariantCulture) : "NULL";
            string fmtInt(int? i) => i.HasValue ? i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "NULL";
            // Für ThCvdImpulseLong als integer-like (F0) falls vorhanden
            string cvdLong = t.ThCvdImpulseLong.HasValue ? t.ThCvdImpulseLong.Value.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) : "NULL";
            string vol = fmt(t.ThVolBurstZ, "F2");
            string tradeZ = fmt(t.ThTradeRateZBreakout, "F2");
            string interBull = fmt(t.ThInterTradeTimeZBull, "F2");
            string interBear = fmt(t.ThInterTradeTimeZBear, "F2");
            string anchored = fmtInt(t.ThStackedImbAnchoredRangeMinDirectional);
            string any = fmtInt(t.ThStackedImbAnyRangeMinDirectional);
            string opp = fmtInt(t.ThOppositeAnchoredWeakMax);
            string minSignals = fmtInt(t.MinSignalsRequired);

            return $"MinSignals={minSignals}, VolBurstZ={vol}, CvdImpLong={cvdLong}, TradeRateZ={tradeZ}, InterTradeZ={interBull}/{interBear}, StackedAnchoredMin={anchored}, OppositeAnchoredWeakMax={opp}";
        }

        #endregion
    }
}
