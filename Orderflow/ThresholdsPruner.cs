using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using Utils.Common.Logging;
using static MyNamespace.Strategies.Goldfluss3_3;
using System.Windows.Documents;

namespace MyNamespace.Strategies.Orderflow
{
    public static class ThresholdsPruner
    {
        private static Action<string>? _externalLogInfo;
        private static Action<string>? _externalLogDebug;
        private static Action<string>? _externalLogWarn;
        private static Action<string>? _externalLogError;
        private static bool _isLoggerInitialized = false;

        public static void InitializeLogger(Action<string> logInfoAction, Action<string> logDebugAction, Action<string>? logWarnAction = null, Action<string>? logErrorAction = null)
        {
            if (!_isLoggerInitialized)
            {
                _externalLogInfo = logInfoAction;
                _externalLogDebug = logDebugAction;
                _externalLogWarn = logWarnAction;
                _externalLogError = logErrorAction;
                _isLoggerInitialized = true;
                // lokale Debug-Info behalten
                _externalLogDebug?.Invoke("[ThresholdsPruner] Logger initialized.");
                Debug.WriteLine("[ThresholdsPruner] Logger initialized.");
            }
        }

        /// <summary>
        /// PruneIrrelevant: Prunes thresholds that are not relevant according to categorySpecs.
        /// Returns the list of fields that were nullified (for logging/analysis).
        /// Optional context: barIndex and patternName — werden nur in Debug-Logs verwendet.
        /// </summary>
        public static System.Collections.Generic.List<string> PruneIrrelevant(
            OrderflowThresholds t,
            PatternCategory currentPatternCategory,
            SetupConditionConfig patternConditionConfig,
            ModeSpecsEntry? callerCategorySpecs = null, // neu
            GateMetric keepMask = GateMetric.None,
            int? barIndex = null,
            string? patternName = null)
        {
            var sw = Stopwatch.StartNew();
            var nullified = new System.Collections.Generic.List<string>();

            if (t == null)
            {
                var msgNull = $"[PruneIrrelevant] thresholds argument is null; nothing to prune. Context: Bar={barIndex?.ToString() ?? "n/a"} Pattern={patternName ?? "n/a"}";
                // Debug locally
                Debug.WriteLine(msgNull);
                // Use SmartLogger to emit once-per-bar info (if desired) — but here it's a no-op; instead call external debug if available
                _externalLogDebug?.Invoke(msgNull);
                return nullified;
            }

            // Entscheiden, ob callerCategorySpecs wirklich genutzt werden soll:
            // Wenn callerCategorySpecs == null => Map benutzen.
            // Wenn callerCategorySpecs vorhanden ist, aber beide Flags None sind => betrachten wir das als "keine Angabe" und benutzen Map (vermeidet unnötige Mismatch-Warnungen).
            ModeSpecsEntry categorySpecs;
            bool useCallerSpecs =
                callerCategorySpecs != null
                && (callerCategorySpecs.Relevant != GateMetric.None || callerCategorySpecs.HardRequired != GateMetric.None);

            if (useCallerSpecs)
            {
                categorySpecs = callerCategorySpecs!;
                if (PatternCategorySpecs.Map.TryGetValue(currentPatternCategory, out var mapSpecs))
                {
                    if (mapSpecs.Relevant != categorySpecs.Relevant || mapSpecs.HardRequired != categorySpecs.HardRequired)
                    {
                        var warnMsg = $"[PruneIrrelevant] categorySpecs mismatch between caller and PatternCategorySpecs.Map for {currentPatternCategory}. Using caller's specs. Caller.Relevant={categorySpecs.Relevant} Map.Relevant={mapSpecs.Relevant}";
                        _externalLogWarn?.Invoke(warnMsg);
                        Debug.WriteLine(warnMsg);
                    }
                }
            }
            else
            {
                if (!PatternCategorySpecs.Map.TryGetValue(currentPatternCategory, out categorySpecs))
                {
                    var warnMsg = $"[PruneIrrelevant] No PatternCategorySpecs entry for {currentPatternCategory}; falling back to PatternCategory.Unknown or safe default. Context: Bar={barIndex?.ToString() ?? "n/a"} Pattern={patternName ?? "n/a"}";
                    _externalLogWarn?.Invoke(warnMsg);
                    Debug.WriteLine(warnMsg);

                    if (!PatternCategorySpecs.Map.TryGetValue(PatternCategory.Unknown, out categorySpecs))
                    {
                        categorySpecs = new ModeSpecsEntry
                        {
                            HardRequired = GateMetric.VolBurstZ,
                            Relevant = GateMetric.VolBurstZ | GateMetric.BarDeltaPerVolume | GateMetric.AggPressureDirectional
                        };
                    }
                }
            }

            // Lokaler Helfer zum Nullstellen und Aufzeichnen
            void NullifyIf<T>(ref T? field, string fieldName) where T : struct
            {
                if (field != null)
                {
                    field = null;
                    nullified.Add(fieldName);
                    // nur lokale Debug-Ausgabe, kein externes Log pro Nullify
                    Debug.WriteLine($"[PruneIrrelevant] Nullified {fieldName}. Context: Bar={barIndex?.ToString() ?? "n/a"} Pattern={patternName ?? "n/a"}");
                }
            }

            void NullifyRefIf<T>(ref T field, string fieldName) where T : class
            {
                if (field != null)
                {
                    field = null!;
                    nullified.Add(fieldName);
                    Debug.WriteLine($"[PruneIrrelevant] Nullified {fieldName}. Context: Bar={barIndex?.ToString() ?? "n/a"} Pattern={patternName ?? "n/a"}");
                }
            }

            // Jetzt das eigentliche Prunen. Für alle Felder verwende NullifyIf / NullifyRefIf
            // VolBurstZ
            if (!categorySpecs.Relevant.HasFlag(GateMetric.VolBurstZ)
                && !categorySpecs.HardRequired.HasFlag(GateMetric.VolBurstZ)
                && !keepMask.HasFlag(GateMetric.VolBurstZ))
            {
                if (t.ThVolBurstZ != null)
                {
                    t.ThVolBurstZ = null;
                    nullified.Add(nameof(t.ThVolBurstZ));
                    Debug.WriteLine($"[PruneIrrelevant] Nullified {nameof(t.ThVolBurstZ)}. Context: Bar={barIndex?.ToString() ?? "n/a"} Pattern={patternName ?? "n/a"}");
                }
            }

            // AggPressureDirectional (zwei Felder: bull/bear)
            if (!categorySpecs.Relevant.HasFlag(GateMetric.AggPressureDirectional)
                && !categorySpecs.HardRequired.HasFlag(GateMetric.AggPressureDirectional)
                && !keepMask.HasFlag(GateMetric.AggPressureDirectional))
            {
                if (t.ThAggPressureBreakoutBull != null)
                {
                    t.ThAggPressureBreakoutBull = null;
                    nullified.Add(nameof(t.ThAggPressureBreakoutBull));
                    Debug.WriteLine($"[PruneIrrelevant] Nullified {nameof(t.ThAggPressureBreakoutBull)}. Context: Bar={barIndex?.ToString() ?? "n/a"} Pattern={patternName ?? "n/a"}");
                }
                if (t.ThAggPressureBreakoutBear != null)
                {
                    t.ThAggPressureBreakoutBear = null;
                    nullified.Add(nameof(t.ThAggPressureBreakoutBear));
                    Debug.WriteLine($"[PruneIrrelevant] Nullified {nameof(t.ThAggPressureBreakoutBear)}. Context: Bar={barIndex?.ToString() ?? "n/a"} Pattern={patternName ?? "n/a"}");
                }
            }

            // CvdImpulse (long/short)
            // WICHTIG: Bei Umkehrmustern dürfen wir die CvdImpulse-Schwellenwerte NICHT beschneiden, da
            // ein fehlender ThCvdImpulseLong/Short einen harten Disqualifikator deaktiviert und zu
            // Fehlalarmen führen kann (z. B. Meldung von Longs während eindeutiger bärischer Balken).
            bool isReversalCategory = currentPatternCategory == PatternCategory.Reversal
                                      || (patternName != null && patternName.IndexOf("Reversal", StringComparison.OrdinalIgnoreCase) >= 0);

            if (!isReversalCategory) // Nur für nicht umkehrbare Kategorien beschneiden
            {
                if (!categorySpecs.Relevant.HasFlag(GateMetric.CvdImpulse)
                    && !categorySpecs.HardRequired.HasFlag(GateMetric.CvdImpulse)
                    && !keepMask.HasFlag(GateMetric.CvdImpulse))
                {
                    if (t.ThCvdImpulseLong != null)
                    {
                        t.ThCvdImpulseLong = null;
                        nullified.Add(nameof(t.ThCvdImpulseLong));
                        Debug.WriteLine($"[PruneIrrelevant] Nullified {nameof(t.ThCvdImpulseLong)}. Context: Bar={barIndex?.ToString() ?? "n/a"} Pattern={patternName ?? "n/a"}");
                    }
                    if (t.ThCvdImpulseShort != null)
                    {
                        t.ThCvdImpulseShort = null;
                        nullified.Add(nameof(t.ThCvdImpulseShort));
                        Debug.WriteLine($"[PruneIrrelevant] Nullified {nameof(t.ThCvdImpulseShort)}. Context: Bar={barIndex?.ToString() ?? "n/a"} Pattern={patternName ?? "n/a"}");
                    }
                }
            }
            else
            {
                // Debug/Hinweis: wir behalten CvdImpulse thresholds für Reversal-Kategorie
                Debug.WriteLine($"[PruneIrrelevant] Beibehaltung der CvdImpulse-Schwellenwerte für Reversal Kategorie. Context: Bar={barIndex?.ToString() ?? "n/a"} Pattern={patternName ?? "n/a"}");
            }

            // CvdCoherence
            if (!categorySpecs.Relevant.HasFlag(GateMetric.CvdCoherence)
                && !categorySpecs.HardRequired.HasFlag(GateMetric.CvdCoherence)
                && !keepMask.HasFlag(GateMetric.CvdCoherence))
            {
                if (t.ThCvdCoherence != null)
                {
                    t.ThCvdCoherence = null;
                    nullified.Add(nameof(t.ThCvdCoherence));
                    Debug.WriteLine($"[PruneIrrelevant] Nullified {nameof(t.ThCvdCoherence)}. Context: Bar={barIndex?.ToString() ?? "n/a"} Pattern={patternName ?? "n/a"}");
                }
            }

            // TradeRateZ
            if (!categorySpecs.Relevant.HasFlag(GateMetric.TradeRateZ)
                && !categorySpecs.HardRequired.HasFlag(GateMetric.TradeRateZ)
                && !keepMask.HasFlag(GateMetric.TradeRateZ))
            {
                if (t.ThTradeRateZBreakout != null)
                {
                    t.ThTradeRateZBreakout = null;
                    nullified.Add(nameof(t.ThTradeRateZBreakout));
                    Debug.WriteLine($"[PruneIrrelevant] Nullified {nameof(t.ThTradeRateZBreakout)}. Context: Bar={barIndex?.ToString() ?? "n/a"} Pattern={patternName ?? "n/a"}");
                }
            }

            // InterTradeTimeZ
            if (!categorySpecs.Relevant.HasFlag(GateMetric.InterTradeTimeZ)
                && !categorySpecs.HardRequired.HasFlag(GateMetric.InterTradeTimeZ)
                && !keepMask.HasFlag(GateMetric.InterTradeTimeZ))
            {
                if (t.ThInterTradeTimeZBull != null)
                {
                    t.ThInterTradeTimeZBull = null;
                    nullified.Add(nameof(t.ThInterTradeTimeZBull));
                    Debug.WriteLine($"[PruneIrrelevant] Nullified {nameof(t.ThInterTradeTimeZBull)}. Context: Bar={barIndex?.ToString() ?? "n/a"} Pattern={patternName ?? "n/a"}");
                }
                if (t.ThInterTradeTimeZBear != null)
                {
                    t.ThInterTradeTimeZBear = null;
                    nullified.Add(nameof(t.ThInterTradeTimeZBear));
                    Debug.WriteLine($"[PruneIrrelevant] Nullified {nameof(t.ThInterTradeTimeZBear)}. Context: Bar={barIndex?.ToString() ?? "n/a"} Pattern={patternName ?? "n/a"}");
                }
            }

            // Efficiency
            if (!categorySpecs.Relevant.HasFlag(GateMetric.Efficiency)
                && !categorySpecs.HardRequired.HasFlag(GateMetric.Efficiency)
                && !keepMask.HasFlag(GateMetric.Efficiency))
            {
                if (t.ThEfficiency != null)
                {
                    t.ThEfficiency = null;
                    nullified.Add(nameof(t.ThEfficiency));
                    Debug.WriteLine($"[PruneIrrelevant] Nullified {nameof(t.ThEfficiency)}. Context: Bar={barIndex?.ToString() ?? "n/a"} Pattern={patternName ?? "n/a"}");
                }
            }

            // BarDeltaPerVolume
            if (!categorySpecs.Relevant.HasFlag(GateMetric.BarDeltaPerVolume)
                && !categorySpecs.HardRequired.HasFlag(GateMetric.BarDeltaPerVolume)
                && !keepMask.HasFlag(GateMetric.BarDeltaPerVolume))
            {
                if (t.ThBarDeltaPerVolume != null)
                {
                    t.ThBarDeltaPerVolume = null;
                    nullified.Add(nameof(t.ThBarDeltaPerVolume));
                    Debug.WriteLine($"[PruneIrrelevant] Nullified {nameof(t.ThBarDeltaPerVolume)}. Context: Bar={barIndex?.ToString() ?? "n/a"} Pattern={patternName ?? "n/a"}");
                }
            }

            // VolPerSecond
            if (!categorySpecs.Relevant.HasFlag(GateMetric.VolPerSecond)
                && !categorySpecs.HardRequired.HasFlag(GateMetric.VolPerSecond)
                && !keepMask.HasFlag(GateMetric.VolPerSecond))
            {
                if (t.ThVolPerSecond != null)
                {
                    t.ThVolPerSecond = null;
                    nullified.Add(nameof(t.ThVolPerSecond));
                    Debug.WriteLine($"[PruneIrrelevant] Nullified {nameof(t.ThVolPerSecond)}. Context: Bar={barIndex?.ToString() ?? "n/a"} Pattern={patternName ?? "n/a"}");
                }
            }

            // MaxCounterDeltaShare
            if (!categorySpecs.Relevant.HasFlag(GateMetric.MaxCounterDeltaShare)
                && !categorySpecs.HardRequired.HasFlag(GateMetric.MaxCounterDeltaShare)
                && !keepMask.HasFlag(GateMetric.MaxCounterDeltaShare))
            {
                if (t.MaxCounterDeltaShareBull != null)
                {
                    t.MaxCounterDeltaShareBull = null;
                    nullified.Add(nameof(t.MaxCounterDeltaShareBull));
                    Debug.WriteLine($"[PruneIrrelevant] Nullified {nameof(t.MaxCounterDeltaShareBull)}. Context: Bar={barIndex?.ToString() ?? "n/a"} Pattern={patternName ?? "n/a"}");
                }
                if (t.MaxCounterDeltaShareBear != null)
                {
                    t.MaxCounterDeltaShareBear = null;
                    nullified.Add(nameof(t.MaxCounterDeltaShareBear));
                    Debug.WriteLine($"[PruneIrrelevant] Nullified {nameof(t.MaxCounterDeltaShareBear)}. Context: Bar={barIndex?.ToString() ?? "n/a"} Pattern={patternName ?? "n/a"}");
                }
            }

            // Stacked Imbalance group
            bool stackedAnchoredRelevantOrRequired =
                categorySpecs.Relevant.HasFlag(GateMetric.StackedImbalanceAnchoredDirectional)
                || categorySpecs.HardRequired.HasFlag(GateMetric.StackedImbalanceAnchoredDirectional)
                || keepMask.HasFlag(GateMetric.StackedImbalanceAnchoredDirectional);

            bool stackedAnyRelevantOrRequired =
                categorySpecs.Relevant.HasFlag(GateMetric.StackedImbalanceAnyDirectional)
                || categorySpecs.HardRequired.HasFlag(GateMetric.StackedImbalanceAnyDirectional)
                || keepMask.HasFlag(GateMetric.StackedImbalanceAnyDirectional);

            bool stackedOppositeRelevantOrRequired =
                categorySpecs.Relevant.HasFlag(GateMetric.StackedImbalanceOppositeAnchoredWeak)
                || categorySpecs.HardRequired.HasFlag(GateMetric.StackedImbalanceOppositeAnchoredWeak)
                || keepMask.HasFlag(GateMetric.StackedImbalanceOppositeAnchoredWeak);

            if (!stackedAnchoredRelevantOrRequired
                && !stackedAnyRelevantOrRequired
                && !stackedOppositeRelevantOrRequired)
            {
                if (t.ThStackedImbAnchoredRangeMinDirectional != null)
                {
                    t.ThStackedImbAnchoredRangeMinDirectional = null;
                    nullified.Add(nameof(t.ThStackedImbAnchoredRangeMinDirectional));
                    Debug.WriteLine($"[PruneIrrelevant] Nullified {nameof(t.ThStackedImbAnchoredRangeMinDirectional)}. Context: Bar={barIndex?.ToString() ?? "n/a"} Pattern={patternName ?? "n/a"}");
                }
                if (t.ThStackedImbAnyRangeMinDirectional != null)
                {
                    t.ThStackedImbAnyRangeMinDirectional = null;
                    nullified.Add(nameof(t.ThStackedImbAnyRangeMinDirectional));
                    Debug.WriteLine($"[PruneIrrelevant] Nullified {nameof(t.ThStackedImbAnyRangeMinDirectional)}. Context: Bar={barIndex?.ToString() ?? "n/a"} Pattern={patternName ?? "n/a"}");
                }
                if (t.ThOppositeAnchoredWeakMax != null)
                {
                    t.ThOppositeAnchoredWeakMax = null;
                    nullified.Add(nameof(t.ThOppositeAnchoredWeakMax));
                    Debug.WriteLine($"[PruneIrrelevant] Nullified {nameof(t.ThOppositeAnchoredWeakMax)}. Context: Bar={barIndex?.ToString() ?? "n/a"} Pattern={patternName ?? "n/a"}");
                }
            }

            sw.Stop();

            // --- SMART LOGGING: einmalige Zusammenfassung per Bar (nur wenn sich Signatur geändert hat) ---
            try
            {
                int logBar = barIndex ?? -1;
                string pat = patternName ?? "n/a";

                // compose compact signature: include relevant flags and the hash of nullified fields
                var sigRaw = SmartLogger.ComposeSignature(
                    ("bar", logBar.ToString()),
                    ("pattern", pat),
                    ("relevant", categorySpecs.Relevant.ToString()),
                    ("hard", categorySpecs.HardRequired.ToString()),
                    ("keepMask", keepMask.ToString()),
                    ("nullified", string.Join(",", nullified))
                );

                // For performance/compactness use SHA1 hex over raw signature
                var sig = SmartLogger.ComputeHashHex(sigRaw);

                // Build a concise message for Ops/INFO
                var msg = $"[PruneIrrelevant] Category={currentPatternCategory} Bar={logBar} Pattern={pat} NullifiedCount={nullified.Count} Nullified=[{string.Join(",", nullified)}]";

                // backend action: prefer external Info action, fallback to Debug.WriteLine
                Action<string> backend = s =>
                {
                    if (_externalLogInfo != null) _externalLogInfo(s);
                    else Debug.WriteLine($"INFO (ThresholdsPruner): {s}");
                };

                // Use SmartLogger to emit once-per-bar-per-source logs (category "Pruner", source "ThresholdsPruner")
                SmartLogger.Instance.LogIfChanged(
                    category: "Pruner",
                    sourceId: "ThresholdsPruner",
                    barIndex: logBar,
                    message: msg,
                    signature: sig,
                    backendLogAction: backend
                );

                // Optionally also emit a debug-level summary (external debug channel) — not deduped by SmartLogger
                var debugMsg = $"[PruneIrrelevant DEBUG] Took {sw.ElapsedMilliseconds}ms. {msg}";
                if (_externalLogDebug != null)
                    _externalLogDebug(debugMsg);
                else
                    Debug.WriteLine(debugMsg);
            }
            catch (Exception ex)
            {
                // Fehler beim Loggen sollten nicht das Prunen beeinflussen
                var err = $"[PruneIrrelevant] Logging failed: {ex.Message}";
                _externalLogError?.Invoke(err);
                Debug.WriteLine(err);
            }

            // Keine Info-Zusammenfassung hier; SmartLogger übernimmt jetzt die zusammengefasste Ausgabe.
            return nullified;
        }

        #region Legacy logging wrappers (kept for backward compatibility)
        // Intern verwenden wir jetzt SmartLogger für die Info-Zusammenfassung.
        // Diese Wrapper lassen wir für direkte Warn/Error Aufrufe stehen.
        private static void LogInfo(string message)
        {
            _externalLogInfo?.Invoke(message);
            Debug.WriteLine($"INFO (ThresholdsPruner): {message}");
        }

        private static void LogDebug(string message)
        {
            // interne Debug-Wrapper: nur lokale Debug-Ausgabe; externe Debug wird in InitializeLogger/PruneIrrelevant direkt aufgerufen
            _externalLogDebug?.Invoke(message);
            Debug.WriteLine($"DEBUG (ThresholdsPruner): {message}");
        }

        private static void LogWarn(string message)
        {
            _externalLogWarn?.Invoke(message ?? string.Empty);
            Debug.WriteLine($"WARN (ThresholdsPruner): {message}");
        }

        private static void LogError(string message)
        {
            _externalLogError?.Invoke(message ?? string.Empty);
            Debug.WriteLine($"ERROR (ThresholdsPruner): {message}");
        }
        #endregion
    }
}


