using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MyNamespace.Strategies.Models;
using static MyNamespace.Strategies.Goldfluss3_3; // falls Pattern-Enums dort liegen
using Utils.Common.Logging;
using static MyNamespace.Strategies.Models.DetectedOrderflowPattern;

namespace MyNamespace.Strategies.Orderflow
{

    // DTO / Snapshot für alle vier Bias-Thresholds
    public readonly struct FullThresholdSnapshot
    {
        public OrderflowThresholds Bullish { get; init; }
        public OrderflowThresholds Bearish { get; init; }
        public OrderflowThresholds Sideways { get; init; }
        public OrderflowThresholds Choppy { get; init; }        
        public DateTime CreatedAtUtc { get; init; }
        public int HistoryVersion { get; init; }
        public MarketRegime Regime { get; init; }
    }

    public sealed class OrderflowThresholdManager : IDisposable
    {
        private readonly ILoggerSource _loggerSource;
        private readonly SetupConfiguration _globalConfig;
        private readonly IThresholdsResolver _thresholdsResolver;
        // These are set via Initialize(...)
        private OfFeaturesHistory? _ofFeaturesHistory;
        private OrderflowFeatureCalculator? _featureCalculator;
        private SetupConditionConfig? _defaultConditionConfig;
        private bool _initialized;

        // simpler cache: key = $"{regime}_{bias}_{historyVersion}"
        private readonly ConcurrentDictionary<string, OrderflowThresholds> _cache = new();
        // neuer Snapshot-Cache (vier Biases)
        private readonly ConcurrentDictionary<string, FullThresholdSnapshot> _fullSnapshotCache = new();
        private readonly Dictionary<string, DateTime> _lastLogTimeByContext = new Dictionary<string, DateTime>();
        private readonly TimeSpan _logCooldown = TimeSpan.FromSeconds(10);
        private bool _disposed;

        // Standardwerte als Konstanten für einfache Anpassung
        private const decimal DefaultAggBreakBull = 0.48m;
        private const decimal DefaultAggBreakBear = 0.48m;
        private const decimal DefaultVolBurstZ = 0.33m;
        private const decimal DefaultCvdImpLong = 5.0m;
        private const decimal DefaultCvdImpShort = -5.6m;
        private const decimal DefaultCvdCoherence = 0.7m;
        private const decimal DefaultTradeRateZBreakout = 0.5m;
        private const decimal DefaultIttZBull = 0.3m;
        private const decimal DefaultIttZBear = -0.3m;
        private const decimal DefaultEfficiency = 0.40m;
        private const decimal DefaultMaxCounterDeltaShare = 0.4m;

        // Cache-Größensteuerung (FIFO-Eviction)
        private int _maxCacheEntries = 1000; // Standardwert; kann über optionalen Konstruktor überschrieben werden
        private readonly ConcurrentQueue<string> _cacheKeysQueue = new();

        // Snapshot-Cache Eviction Steuerung
        private readonly ConcurrentQueue<string> _fullSnapshotKeysQueue = new();
        private int _maxFullSnapshotEntries = 200;

        // SmartLogger defaults
        private const string SmartCategory = "ThresholdsManager";
        private const string SmartSourceId = "OrderflowThresholdManager";
        
        /// <summary>
        /// Konstruktor. Der letzte Parameter ist optional und legt die maximale Anzahl Cache-Einträge fest.
        /// </summary>
        public OrderflowThresholdManager(IThresholdsResolver thresholdsResolver, ILoggerSource loggerSource, SetupConfiguration globalStrategyConfig, int maxCacheEntries = 1000)
        {
            _thresholdsResolver = thresholdsResolver ?? throw new ArgumentNullException(nameof(thresholdsResolver));
            _loggerSource = loggerSource ?? throw new ArgumentNullException(nameof(loggerSource));
            _globalConfig = globalStrategyConfig ?? throw new ArgumentNullException(nameof(globalStrategyConfig));

            if (maxCacheEntries > 0) _maxCacheEntries = maxCacheEntries;
            SmartLogInfo($"OrderflowThresholdManager constructed (lightweight). MaxCacheEntries={_maxCacheEntries}.", extraParts: ("phase", "ctor"));
        }

        /// <summary>
        /// Explizite Initialisierung mit ressourcenabhängigen Objekten.
        /// Idempotent: Mehrfachaufrufe sind sicher.
        /// </summary>
        public void Initialize(
            OfFeaturesHistory? ofHistory,
            OrderflowFeatureCalculator? featureCalculator,
            SetupConditionConfig? defaultConditionConfig)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(OrderflowThresholdManager));

            if (_initialized)
            {
                SmartLogDebug("OrderflowThresholdManager.Initialize called but manager already initialized.", extraParts: ("phase", "initialize"));
                return;
            }

            _ofFeaturesHistory = ofHistory ?? new OfFeaturesHistory();
            _featureCalculator = featureCalculator;
            _defaultConditionConfig = defaultConditionConfig ?? CreateDefaultConditionConfig();

            if (ofHistory == null)
                SmartLogWarn("OrderflowThresholdManager: ofHistory was null - using empty fallback.", extraParts: ("phase", "initialize"));

            if (featureCalculator == null)
                SmartLogDebug("OrderflowThresholdManager: featurecalculator is null (optional).", extraParts: ("phase", "initialize"));

            // Optional: perform one-time expensive precomputations here, protected by try/catch
            try
            {
                // Beispiel: nur ausführen wenn ausreichend History vorhanden
                if (_ofFeaturesHistory != null && _ofFeaturesHistory.Count >= 5)
                {
                    // ggf. Vorberechnungen - aktuell keine konkreten Schritte
                }

                _initialized = true;
                SmartLogInfo("OrderflowThresholdManager initialized.", extraParts: ("phase", "initialize"));
            }
            catch (Exception ex)
            {
                SmartLogError($"OrderflowThresholdManager.Initialize failed: {ex.Message}", ex, extraParts: ("phase", "initialize"));
                throw;
            }
            if (ofHistory != null)
            {
                _ofFeaturesHistory = ofHistory;
                ClearCache(); // neu geladene History -> alte Thresholds invalid
            }
        }

        private void EnsureInitialized()
        {
            if (!_initialized)
                throw new InvalidOperationException("OrderflowThresholdManager nicht initialisiert. Initialisieren Sie Initialize.(...) before use.");
        }


        /// <summary>
        /// Wird von deiner MarketStateEngine erwartet: liefert Thresholds für einen gegebenen MarketRegime und Bias. Neue Methode: Liefert ein FullThresholdSnapshot mit allen vier Bias-Thresholds für ein Regime.
        /// preserveAllMetrics = true: versuche, ungeprunte adaptive Werte zu bekommen (ModeSpecsEntry.Relevant = GateMetric.All).
        /// </summary>
        public FullThresholdSnapshot GetFullThresholdSnapshotForRegime(MarketRegime regime, OrderflowPatternType? detectedPattern = null, bool preserveAllMetrics = false)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(OrderflowThresholdManager));
            EnsureInitialized();

            var historyVersion = _ofFeaturesHistory?.Count ?? 0;
            string cacheKey = $"FULL_{regime}_{historyVersion}_{(preserveAllMetrics ? "ALL" : "PRUNED")}_{(detectedPattern?.ToString() ?? "GENERIC")}";

            if (_fullSnapshotCache.TryGetValue(cacheKey, out var cached))
            {
                SmartLogDebug($"OrderflowThresholdManager: Rückgabe des zwischengespeicherten FullThresholdSnapshot für {cacheKey}", extraParts: ("cacheKey", cacheKey));
                return cached;
            }

            try
            {
                ModeSpecsEntry modeSpecs = preserveAllMetrics ? CreateAllRelevantModeSpecsEntry() : CreateDefaultModeSpecsEntry();

                // Lokale Kopie des übergebenen detectedPattern für die Closure (ResolveForBias)
                var patternHintForResolver = detectedPattern; // kann null sein

                OrderflowThresholds ResolveForBias(MarketDirectionalBias bias)
                {
                    // benutze die lokale Variable statt einer nicht existierenden detectedPattern im Scope
                    var assumedPatternType = patternHintForResolver;
                    var category = MapBiasToPatternCategory(bias);

                    // wenn assumedPatternType == OrderflowPatternType.None -> transformiere zu null wie vorher
                    OrderflowThresholds t;
                    if (_ofFeaturesHistory != null && _ofFeaturesHistory.Count >= 5)
                    {
                        var result = _thresholdsResolver.CalculateAdaptiveThresholds(
                            assumedOrderflowPatternType: (assumedPatternType == OrderflowPatternType.None ? null : assumedPatternType),
                            patternCategory: category,
                            regime: regime,
                            history: _ofFeaturesHistory,
                            featuresByBar: null,
                            categorySpecs: modeSpecs,
                            patternConditionConfig: _defaultConditionConfig!,
                            globalStratConfig: _globalConfig,
                            initialThresholds: null,
                            barIndex: null
                        );

                        // preserveAllMetrics -> full (ungeprunt). sonst pruned (wie bisher).
                        t = preserveAllMetrics ? (result.Full ?? CreateSafeDefaultThresholds(category)) : (result.Pruned ?? CreateSafeDefaultThresholds(category));
                    }
                    else
                    {
                        SmartLogDebug($"OrderflowThresholdManager: Unzureichende Historie – Verwendung sicherer Standardeinstellungen für {bias}.", extraParts: ("bias", bias.ToString()));
                        t = CreateSafeDefaultThresholds(category);
                    }

                    // VALIDATE / NORMALIZE: nur dann, wenn wir nicht bewusst ungeprunte (preserveAllMetrics) Werte wollen
                    if (!preserveAllMetrics)
                    {
                        ValidateAndNormalizeThresholds(t, $"fullsnap_{regime}_{bias}");
                    }
                    else
                    {
                        // Debug-Log, damit man im Problemfall sehen kann welche Felder fehlen, ohne sie automatisch zu füllen.
                        SmartLogDebug($"OrderflowThresholdManager: preserveAllMetrics=true -> Validierung überspringen für fullsnap_{regime}_{bias}. (fields may be null)", extraParts: ("bias", bias.ToString()));
                    }

                    return Clone(t);
                }

                var snapshot = new FullThresholdSnapshot
                {
                    Bullish = ResolveForBias(MarketDirectionalBias.BullishTrend),
                    Bearish = ResolveForBias(MarketDirectionalBias.BearishTrend),
                    Sideways = ResolveForBias(MarketDirectionalBias.Sideways),
                    Choppy = ResolveForBias(MarketDirectionalBias.Choppy),
                    CreatedAtUtc = DateTime.UtcNow,
                    HistoryVersion = historyVersion,
                    Regime = regime
                };

                PutFullSnapshotInCache(cacheKey, snapshot);
                SmartLogInfo($"OrderflowThresholdManager: erstellt FullThresholdSnapshot for {regime} historyVersion={historyVersion} preserveAll={preserveAllMetrics} detectedPattern={(detectedPattern?.ToString() ?? "GENERIC")}", extraParts: ("cacheKey", cacheKey));
                return snapshot;
            }
            catch (Exception ex)
            {
                SmartLogError($"OrderflowThresholdManager: Ausnahme beim Erstellen FullThresholdSnapshot: {ex.Message}", ex, extraParts: ("cacheKey", cacheKey));
                // Fallback: safe defaults for all biases
                var fallback = new FullThresholdSnapshot
                {
                    Bullish = Clone(CreateSafeDefaultThresholds(MapBiasToPatternCategory(MarketDirectionalBias.BullishTrend))),
                    Bearish = Clone(CreateSafeDefaultThresholds(MapBiasToPatternCategory(MarketDirectionalBias.BearishTrend))),
                    Sideways = Clone(CreateSafeDefaultThresholds(MapBiasToPatternCategory(MarketDirectionalBias.Sideways))),
                    Choppy = Clone(CreateSafeDefaultThresholds(MapBiasToPatternCategory(MarketDirectionalBias.Choppy))),
                    CreatedAtUtc = DateTime.UtcNow,
                    HistoryVersion = historyVersion,
                    Regime = regime
                };
                PutFullSnapshotInCache(cacheKey + "_fallback", fallback);
                return fallback;
            }
        }
        public FullThresholdSnapshot GetFullThresholdSnapshotForRegime(MarketRegime regime, bool preserveAllMetrics = false)
        {
            return GetFullThresholdSnapshotForRegime(regime, (OrderflowPatternType?)null, preserveAllMetrics);
        }

        private void PutInCache(string key, OrderflowThresholds thresholds)
        {
            try
            {
                // enqueue order, insert clone and enforce limit
                _cacheKeysQueue.Enqueue(key);
                _cache[key] = Clone(thresholds);
                EnforceCacheLimit();
            }
            catch (Exception ex)
            {
                // Defensive: caching darf die Hauptlogik nicht brechen
                SmartLogError($"OrderflowThresholdManager.PutInCache failed for key={key}: {ex.Message}", ex, extraParts: ("key", key));
            }
        }

        private void EnforceCacheLimit()
        {
            try
            {
                while (_cache.Count > _maxCacheEntries && _cacheKeysQueue.TryDequeue(out var oldKey))
                {
                    _cache.TryRemove(oldKey, out _);
                }
            }
            catch (Exception ex)
            {
                // Defensive: Fehler im Eviction-Mechanismus sollen die Threshold-Ermittlung nicht brechen
                SmartLogError($"OrderflowThresholdManager.EnforceCacheLimit fehlgeschlagen: {ex.Message}", ex, extraParts: ("maxEntries", _maxCacheEntries.ToString()));
            }
        }

        private void PutFullSnapshotInCache(string key, FullThresholdSnapshot snapshot)
        {
            try
            {
                _fullSnapshotKeysQueue.Enqueue(key);
                _fullSnapshotCache[key] = snapshot;
                EnforceFullSnapshotCacheLimit();
            }
            catch (Exception ex)
            {
                SmartLogError($"OrderflowThresholdManager.PutFullSnapshotInCache für Schlüssel fehlgeschlagen={key}: {ex.Message}", ex, extraParts: ("key", key));
            }
        }

        private void EnforceFullSnapshotCacheLimit()
        {
            try
            {
                while (_fullSnapshotCache.Count > _maxFullSnapshotEntries && _fullSnapshotKeysQueue.TryDequeue(out var oldKey))
                {
                    _fullSnapshotCache.TryRemove(oldKey, out _);
                }
            }
            catch (Exception ex)
            {
                SmartLogError($"OrderflowThresholdManager.EnforceFullSnapshotCacheLimit fehlgeschlagen: {ex.Message}", ex, extraParts: ("maxFullSnapshots", _maxFullSnapshotEntries.ToString()));
            }
        }

        public void ClearCache()
        {
            _cache.Clear();
            while (_cacheKeysQueue.TryDequeue(out _)) { }

            _fullSnapshotCache.Clear();
            while (_fullSnapshotKeysQueue.TryDequeue(out _)) { }
        }

        private static SetupConditionConfig CreateDefaultConditionConfig()
        {
            return new SetupConditionConfig
            {
                // Setze hier ggf. Default-Property-Werte, falls notwendig
            };
        }

        private static ModeSpecsEntry CreateDefaultModeSpecsEntry()
        {
            return new ModeSpecsEntry
            {
                // Default-Einstellungen falls benötigt
            };
        }

        private static ModeSpecsEntry CreateAllRelevantModeSpecsEntry()
        {
            // Versuche, alle GateMetric Flags als relevant zu markieren, damit der Pruner nichts nullt.
            // Falls GateMetric.All in deinem Projekt nicht existiert, ersetze durch OR aller Flags.
            return new ModeSpecsEntry
            {
                Relevant = GateMetric.All
            };
        }

        private static PatternCategory MapBiasToPatternCategory(MarketDirectionalBias bias)
        {
            return bias switch
            {
                MarketDirectionalBias.BullishTrend => PatternCategory.Breakout,
                MarketDirectionalBias.BearishTrend => PatternCategory.Breakout,
                MarketDirectionalBias.Sideways => PatternCategory.MeanReversion,
                MarketDirectionalBias.Choppy => PatternCategory.Reversal,
                _ => PatternCategory.Reversal
            };
        }
        private static OrderflowThresholds CreateSafeDefaultThresholds(PatternCategory category)
        {
            var t = new OrderflowThresholds
            {
                MinSignalsRequired = 1,
                ThVolBurstZ = DefaultVolBurstZ,
                ThCvdImpulseLong = DefaultCvdImpLong,
                ThCvdImpulseShort = DefaultCvdImpShort,
                ThCvdCoherence = DefaultCvdCoherence,
                ThTradeRateZBreakout = DefaultTradeRateZBreakout,
                ThInterTradeTimeZBull = DefaultIttZBull,
                ThInterTradeTimeZBear = DefaultIttZBear,
                ThAggPressureBreakoutBull = DefaultAggBreakBull,
                ThAggPressureBreakoutBear = DefaultAggBreakBear,
                ThEfficiency = DefaultEfficiency,
                MaxCounterDeltaShareBull = DefaultMaxCounterDeltaShare,
                MaxCounterDeltaShareBear = DefaultMaxCounterDeltaShare,
                ThStackedImbAnchoredRangeMinDirectional = 1,
                ThStackedImbAnyRangeMinDirectional = 1,
                ThOppositeAnchoredWeakMax = 0
            };
            if (category == PatternCategory.Breakout)
            {
                // z.B. Breakout härter machen (keine NullChecks nötig, Defaults gesetzt)
                t.ThAggPressureBreakoutBull = Math.Max(DefaultAggBreakBull, t.ThAggPressureBreakoutBull ?? DefaultAggBreakBull);
                t.ThAggPressureBreakoutBear = Math.Min(DefaultAggBreakBear, t.ThAggPressureBreakoutBear ?? DefaultAggBreakBear);
            }
            return t;
        }

        private static OrderflowThresholds Clone(OrderflowThresholds src)
        {
            if (src == null) return new OrderflowThresholds();
            return new OrderflowThresholds
            {
                MinSignalsRequired = src.MinSignalsRequired,
                ThVolBurstZ = src.ThVolBurstZ,
                ThAggPressureBreakoutBull = src.ThAggPressureBreakoutBull,
                ThAggPressureBreakoutBear = src.ThAggPressureBreakoutBear,
                ThCvdImpulseLong = src.ThCvdImpulseLong,
                ThCvdImpulseShort = src.ThCvdImpulseShort,
                ThCvdCoherence = src.ThCvdCoherence,
                ThTradeRateZBreakout = src.ThTradeRateZBreakout,
                ThInterTradeTimeZBull = src.ThInterTradeTimeZBull,
                ThInterTradeTimeZBear = src.ThInterTradeTimeZBear,
                ThEfficiency = src.ThEfficiency,
                MaxCounterDeltaShareBull = src.MaxCounterDeltaShareBull,
                MaxCounterDeltaShareBear = src.MaxCounterDeltaShareBear,
                ThStackedImbAnchoredRangeMinDirectional = src.ThStackedImbAnchoredRangeMinDirectional,
                ThStackedImbAnyRangeMinDirectional = src.ThStackedImbAnyRangeMinDirectional,
                ThOppositeAnchoredWeakMax = src.ThOppositeAnchoredWeakMax
            };
        }

        // --- Neue Validierungs-/Normalisierungs-Methode (zusammenfassende Logs) ---
        private void ValidateAndNormalizeThresholds(OrderflowThresholds t, string context)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));

            var missing = new List<string>();
            var fixedItems = new List<string>();

            // Lokal definierter Clamp-Helfer
            static decimal Clamp(decimal value, decimal min, decimal max) => value < min ? min : (value > max ? max : value);

            // --- AggPressureBreakout: müssen in [0,1] liegen ---
            if (!t.ThAggPressureBreakoutBull.HasValue)
            {
                t.ThAggPressureBreakoutBull = DefaultAggBreakBull;
                missing.Add(nameof(t.ThAggPressureBreakoutBull));
                fixedItems.Add($"{nameof(t.ThAggPressureBreakoutBull)}={t.ThAggPressureBreakoutBull:0.##}");
            }
            else
            {
                var clamped = Clamp(t.ThAggPressureBreakoutBull.Value, 0.0m, 1.0m);
                if (clamped != t.ThAggPressureBreakoutBull.Value)
                {
                    t.ThAggPressureBreakoutBull = clamped;
                    fixedItems.Add($"{nameof(t.ThAggPressureBreakoutBull)}={clamped:0.##}");
                }
            }

            if (!t.ThAggPressureBreakoutBear.HasValue)
            {
                t.ThAggPressureBreakoutBear = DefaultAggBreakBear;
                missing.Add(nameof(t.ThAggPressureBreakoutBear));
                fixedItems.Add($"{nameof(t.ThAggPressureBreakoutBear)}={t.ThAggPressureBreakoutBear:0.##}");
            }
            else
            {
                var clamped = Clamp(t.ThAggPressureBreakoutBear.Value, 0.0m, 1.0m);
                if (clamped != t.ThAggPressureBreakoutBear.Value)
                {
                    t.ThAggPressureBreakoutBear = clamped;
                    fixedItems.Add($"{nameof(t.ThAggPressureBreakoutBear)}={clamped:0.##}");
                }
            }

            // --- Efficiency: nicht negativ ---
            if (!t.ThEfficiency.HasValue || t.ThEfficiency < 0)
            {
                t.ThEfficiency = DefaultEfficiency;
                missing.Add(nameof(t.ThEfficiency));
                fixedItems.Add($"{nameof(t.ThEfficiency)}={t.ThEfficiency:0.##}");
            }

            // --- TradeRateZBreakout: pragmatisches Limit (Beispiel) ---
            if (!t.ThTradeRateZBreakout.HasValue)
            {
                t.ThTradeRateZBreakout = DefaultTradeRateZBreakout;
                missing.Add(nameof(t.ThTradeRateZBreakout));
                fixedItems.Add($"{nameof(t.ThTradeRateZBreakout)}={t.ThTradeRateZBreakout:0.##}");
            }
            else
            {
                var clamped = Clamp(t.ThTradeRateZBreakout.Value, -10m, 10m);
                if (clamped != t.ThTradeRateZBreakout.Value)
                {
                    t.ThTradeRateZBreakout = clamped;
                    fixedItems.Add($"{nameof(t.ThTradeRateZBreakout)}={clamped:0.##}");
                }
            }

            // --- VolBurstZ: plausibel >= 0 ---
            if (!t.ThVolBurstZ.HasValue || t.ThVolBurstZ < 0)
            {
                t.ThVolBurstZ = DefaultVolBurstZ;
                missing.Add(nameof(t.ThVolBurstZ));
                fixedItems.Add($"{nameof(t.ThVolBurstZ)}={t.ThVolBurstZ:0.##}");
            }

            // --- MaxCounterDeltaShare: im Bereich [0,1] ---
            if (!t.MaxCounterDeltaShareBull.HasValue)
            {
                t.MaxCounterDeltaShareBull = DefaultMaxCounterDeltaShare;
                missing.Add(nameof(t.MaxCounterDeltaShareBull));
                fixedItems.Add($"{nameof(t.MaxCounterDeltaShareBull)}={t.MaxCounterDeltaShareBull:0.##}");
            }
            else
            {
                var clamped = Clamp(t.MaxCounterDeltaShareBull.Value, 0m, 1m);
                if (clamped != t.MaxCounterDeltaShareBull.Value)
                {
                    t.MaxCounterDeltaShareBull = clamped;
                    fixedItems.Add($"{nameof(t.MaxCounterDeltaShareBull)}={clamped:0.##}");
                }
            }

            if (!t.MaxCounterDeltaShareBear.HasValue)
            {
                t.MaxCounterDeltaShareBear = DefaultMaxCounterDeltaShare;
                missing.Add(nameof(t.MaxCounterDeltaShareBear));
                fixedItems.Add($"{nameof(t.MaxCounterDeltaShareBear)}={t.MaxCounterDeltaShareBear:0.##}");
            }
            else
            {
                var clamped = Clamp(t.MaxCounterDeltaShareBear.Value, 0m, 1m);
                if (clamped != t.MaxCounterDeltaShareBear.Value)
                {
                    t.MaxCounterDeltaShareBear = clamped;
                    fixedItems.Add($"{nameof(t.MaxCounterDeltaShareBear)}={clamped:0.##}");
                }
            }

            // --- Stacked-Imbalance Mindestwerte ---
            if (!t.ThStackedImbAnchoredRangeMinDirectional.HasValue)
            {
                t.ThStackedImbAnchoredRangeMinDirectional = 1;
                missing.Add(nameof(t.ThStackedImbAnchoredRangeMinDirectional));
                fixedItems.Add($"{nameof(t.ThStackedImbAnchoredRangeMinDirectional)}={t.ThStackedImbAnchoredRangeMinDirectional}");
            }
            if (!t.ThStackedImbAnyRangeMinDirectional.HasValue)
            {
                t.ThStackedImbAnyRangeMinDirectional = 1;
                missing.Add(nameof(t.ThStackedImbAnyRangeMinDirectional));
                fixedItems.Add($"{nameof(t.ThStackedImbAnyRangeMinDirectional)}={t.ThStackedImbAnyRangeMinDirectional}");
            }

            // --- CVD Impulse Defaults ---
            if (!t.ThCvdImpulseLong.HasValue)
            {
                t.ThCvdImpulseLong = DefaultCvdImpLong;
                missing.Add(nameof(t.ThCvdImpulseLong));
                fixedItems.Add($"{nameof(t.ThCvdImpulseLong)}={t.ThCvdImpulseLong}");
            }
            if (!t.ThCvdImpulseShort.HasValue)
            {
                t.ThCvdImpulseShort = DefaultCvdImpShort;
                missing.Add(nameof(t.ThCvdImpulseShort));
                fixedItems.Add($"{nameof(t.ThCvdImpulseShort)}={t.ThCvdImpulseShort}");
            }

            // MinSignalsRequired: sinnvoller Mindestwert
            if (t.MinSignalsRequired < 1)
            {
                t.MinSignalsRequired = 1;
                fixedItems.Add($"{nameof(t.MinSignalsRequired)}={t.MinSignalsRequired}");
            }

            // --- Logging: zusammenfassen ---
            if (missing.Count == 0 && fixedItems.Count == 0)
            {
                // nichts geändert
                SmartLogDebug($"ValidateThresholds({context}): Alle Schwellenwerte sind vorhanden und gültig.", extraParts: ("context", context));
            }
            else
            {
                var summary = $"ValidateThresholds({context}): Standardeinstellungen festlegen für [{string.Join(",", missing)}]; angepasst [{string.Join(",", fixedItems)}]";

                // Severity wie vorgeschlagen
                var isWarn = missing.Count >= 4;

                // Rate limit pro Kontext
                var now = DateTime.UtcNow;
                bool allow = true;
                lock (_lastLogTimeByContext)
                {
                    if (_lastLogTimeByContext.TryGetValue(context, out var last))
                    {
                        if (now - last < _logCooldown)
                            allow = false;
                    }
                    if (allow)
                        _lastLogTimeByContext[context] = now;
                }

                if (!allow)
                {
                    // optional: aggregiere Zähler, oder nur stillschweigend unterdrücken
                    return;
                }

                if (isWarn)
                    SmartLogWarn(summary, extraParts: ("context", context));
                else
                    SmartLogDebug(summary, extraParts: ("context", context));
            }

            // Weitere Prüfungen / Logs nach Bedarf
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ClearCache();
        }

        #region SmartLogger helpers

        private void SmartLogInternal(
            string level,
            string message,
            Exception? ex = null,
            (string key, string value)[]? extraParts = null)
        {
            try
            {
                var parts = new List<(string, string)>();
                parts.Add(("level", level));
                if (extraParts != null)
                    parts.AddRange(extraParts);

                // Compose a compact signature using SmartLogger helper (string pairs)
                var sigRaw = SmartLogger.ComposeSignature(
                    parts.ToArray()
                );
                var sig = SmartLogger.ComputeHashHex(sigRaw);

                Action<string> backend = s =>
                {
                    // Map severity to LoggerHelper for actual emission via ILoggerSource (existing infra)
                    if (level == "ERROR")
                    {
                        LoggerHelper.LogError(_loggerSource, $"[OrderflowThresholdManager] {s}", ex);
                    }
                    else if (level == "WARN")
                    {
                        LoggerHelper.LogWarn(_loggerSource, $"[OrderflowThresholdManager] {s}");
                    }
                    else if (level == "INFO")
                    {
                        LoggerHelper.LogInfo(_loggerSource, $"[OrderflowThresholdManager] {s}");
                    }
                    else // DEBUG
                    {
                        LoggerHelper.LogDebug(_loggerSource, $"[OrderflowThresholdManager] {s}");
                    }
                };

                var msg = $"[{level}] {message}";
                SmartLogger.Instance.LogIfChanged(
                    category: SmartCategory,
                    sourceId: SmartSourceId,
                    barIndex: -1,
                    message: msg,
                    signature: sig,
                    backendLogAction: backend
                );
            }
            catch
            {
                // best-effort: fall back to LoggerHelper so we don't swallow important logs
                if (level == "ERROR")
                    LoggerHelper.LogError(_loggerSource, $"[OrderflowThresholdManager] {message}", ex);
                else if (level == "WARN")
                    LoggerHelper.LogWarn(_loggerSource, $"[OrderflowThresholdManager] {message}");
                else if (level == "INFO")
                    LoggerHelper.LogInfo(_loggerSource, $"[OrderflowThresholdManager] {message}");
                else
                    LoggerHelper.LogDebug(_loggerSource, $"[OrderflowThresholdManager] {message}");
            }
        }

        private void SmartLogInfo(string message, params (string key, string value)[] extraParts)
        {
            SmartLogInternal("INFO", message, null, extraParts);
        }
        private void SmartLogDebug(string message, params (string key, string value)[] extraParts)
        {
            SmartLogInternal("DEBUG", message, null, extraParts);
        }
        private void SmartLogWarn(string message, params (string key, string value)[] extraParts)
        {
            SmartLogInternal("WARN", message, null, extraParts);
        }
        private void SmartLogError(string message, Exception? ex = null, params (string key, string value)[] extraParts)
        {
            SmartLogInternal("ERROR", message, ex, extraParts);
        }

        #endregion
    }
}
