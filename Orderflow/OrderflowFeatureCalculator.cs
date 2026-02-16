using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using Utils.Common.Logging;
using static MyNamespace.Strategies.Goldfluss3_3;

namespace MyNamespace.Strategies.Orderflow
{
    public class OrderflowFeatureCalculator
    {

        private readonly OvSnapshotHistory _ovSnapshotHistory;
        private readonly OfFeaturesHistory _ofFeaturesHistory;    // Archivierung / FIFO-Puffer
        private readonly SetupConfiguration _setupConfig;
        private readonly int _maxFeaturesHistorySizeInitial;
        private int _volBurstCooldown = 0;
        private readonly ILoggerSource _loggerSource;
        private readonly object _syncObj = new object();

        private void LogInfo(string message)
        {
            if (ShouldSuppressNoisyLogStatic(message))
                return;
            LoggerHelper.LogInfo(_loggerSource, message);
        }

        private void LogDebug(string message)
        {
            if (ShouldSuppressNoisyLogStatic(message))
                return;
            LoggerHelper.LogDebug(_loggerSource, message);
        }

        private void LogWarn(string message)
        {
            LoggerHelper.LogWarn(_loggerSource, message);
        }

        private void LogError(string message)
        {
            LoggerHelper.LogError(_loggerSource, message);
        }

        public Dictionary<int, OfFeatures> FeaturesByBar { get; private set; } = new Dictionary<int, OfFeatures>();

        /// <summary>
        /// Konstruktor:
        /// - setupConfig sollte nie null sein (enthält globale Defaults wie InflectionMinDelta, Burst-Thresholds, BurstCooldownBars, etc.)
        /// - ofFeaturesHistory kann optional übergeben werden. Wenn null, wird ein neues OfFeaturesHistory mit Kapazität erstellt.
        /// - maxFeaturesHistorySize überschreibt die initiale Kapazität/Trimmgröße (optional).
        /// </summary>
        public OrderflowFeatureCalculator(
            OvSnapshotHistory ovSnapshotHistory,
            SetupConfiguration setupConfig,
            OfFeaturesHistory ofFeaturesHistory = null,
            int? maxFeaturesHistorySize = null,
            ILoggerSource loggerSource = null)
        {
            _loggerSource = loggerSource;

            _ovSnapshotHistory = ovSnapshotHistory ?? throw new ArgumentNullException(nameof(ovSnapshotHistory));
            _setupConfig = setupConfig ?? throw new ArgumentNullException(nameof(setupConfig));

            LogInfo($"[OrderflowFeatureCalculator] OrderflowFeatureCalculator ctor: setupConfig.AdaptiverLookbackBars={_setupConfig.AdaptiverLookbackBars}");

            // Bestimme initiale Max-Size (Prio: expl. Parameter > SetupConfiguration.AdaptiverLookbackBars (?) > Hardcode 128)
            int chosen = maxFeaturesHistorySize ?? Math.Max(128, _setupConfig.AdaptiverLookbackBars);
            _maxFeaturesHistorySizeInitial = Math.Max(10, chosen);

            _ofFeaturesHistory = ofFeaturesHistory ?? new OfFeaturesHistory(_maxFeaturesHistorySizeInitial, _loggerSource);

            LogInfo($"[OrderflowFeatureCalculator] OrderflowFeatureCalculator initialized. MaxFeaturesHistorySizeInitial={_maxFeaturesHistorySizeInitial}");
        }

        // Holt die letzten n OvSnapshots (älteste zuerst, neueste zuletzt).
        // Annahme: _ovSnapshotHistory.GetLast(n) liefert List<OvSnapshot> in dieser Reihenfolge.
        public List<OvSnapshot> GetLastNSnapshots(int n)
        {
            var res = _ovSnapshotHistory.GetLast(n);
            LogDebug($"[OrderflowFeatureCalculator] GetLastNSnapshots({n}) returned {res.Count} snapshots.");
            return res;
        }

        public bool TryGetLastOvSnapshot(out OvSnapshot s)
        {
            // TryGetRelative(0) sollte den aktuellsten liefern (wie in OfFeaturesHistory definiert).
            var ok = _ovSnapshotHistory.TryGetRelative(0, out s);
            LogDebug($"[OrderflowFeatureCalculator] TryGetLastOvSnapshot -> found={ok}, Bar={(s?.Bar.ToString() ?? "null")}");
            return ok;
        }

        public decimal ComputeSlope(List<OvSnapshot> window, Func<OvSnapshot, decimal> selector)
        {
            int n = window?.Count ?? 0;
            if (n < 2)
            {
                LogDebug($"[OrderflowFeatureCalculator] ComputeSlope: Fenster zu klein (n={n}). Zurückkehren 0.");
                return 0m;
            }

            decimal sumX = 0m, sumY = 0m, sumXX = 0m, sumXY = 0m;
            for (int i = 0; i < n; i++)
            {
                decimal x = i;
                decimal y = selector(window[i]);
                sumX += x; sumY += y;
                sumXX += x * x;
                sumXY += x * y;
            }

            decimal denom = (n * sumXX - sumX * sumX);
            if (denom == 0)
            {
                LogWarn("[OrderflowFeatureCalculator] ComputeSlope: denom == 0 (degenerate window). Returning 0.");
                return 0m;
            }
            decimal slope = (n * sumXY - sumX * sumY) / denom;
            LogDebug($"[OrderflowFeatureCalculator] ComputeSlope: n={n}, slope={slope:F2}");
            return slope;
        }

        // Klarere Implementierung: prüft die letzten Bars sequentiell
        public int ComputePersistence(Func<OvSnapshot, bool> predicate, int maxDepth)
        {
            if (_ovSnapshotHistory == null || _ovSnapshotHistory.IsEmpty)
            {
                LogDebug("[OrderflowFeatureCalculator] ComputePersistence: history empty or null => 0");
                return 0;
            }
            int count = 0;
            for (int i = 0; i < maxDepth; i++)
            {
                // barsAgo: 0 = aktuellster, 1 = vorheriger
                if (!_ovSnapshotHistory.TryGetRelative(-i, out var s))
                {
                    LogDebug($"[OrderflowFeatureCalculator] ComputePersistence: no snapshot at -{i}, stopping. count={count}");
                    break; // i=0 => aktuellster (offset 0), i=1 => vorheriger (offset -1), ...
                }
                bool ok = predicate(s);
                LogDebug($"[OrderflowFeatureCalculator] ComputePersistence: checking snapshot Bar={s.Bar}, predicate={ok}");
                if (ok) count++;
                else break;
            }
            LogDebug($"[OrderflowFeatureCalculator] ComputePersistence result={count} (maxDepth={maxDepth})");
            return count;
        }

        public InflectionType DetectInflection(List<OvSnapshot> w, Func<OvSnapshot, decimal> selector, decimal minDeltaAbs = 0.05m)
        {
            int n = w?.Count ?? 0;
            if (n < 3)
            {
                LogDebug($"[OrderflowFeatureCalculator] DetectInflection: window too small n={n}. Returning None.");
                return InflectionType.None;
            }

            decimal a = selector(w[n - 3]);
            decimal b = selector(w[n - 2]);
            decimal c = selector(w[n - 1]);

            decimal d1 = b - a;
            decimal d2 = c - b;

            bool signChange = (d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0);
            if (!signChange)
            {
                LogDebug($"[OrderflowFeatureCalculator] DetectInflection: no sign change (d1={d1:F2}, d2={d2:F2}).");
                return InflectionType.None;
            }

            decimal amp = Math.Abs(d1) + Math.Abs(d2);
            if (amp < minDeltaAbs)
            {
                LogDebug($"[OrderflowFeatureCalculator] DetectInflection: amplitude {amp:F2} < minDeltaAbs {minDeltaAbs} => None.");
                return InflectionType.None;
            }

            if (d1 < 0 && d2 > 0)
            {
                LogInfo($"[OrderflowFeatureCalculator] DetectInflection: Bullish detected (d1={d1:F2}, d2={d2:F2}, amp={amp:F2}).");
                return InflectionType.Bullish;
            }
            if (d1 > 0 && d2 < 0)
            {
                LogInfo($"[OrderflowFeatureCalculator] DetectInflection: Bearish detected (d1={d1:F2}, d2={d2:F2}, amp={amp:F2}).");
                return InflectionType.Bearish;
            }

            return InflectionType.None;
        }

       

        private BurstClass ClassifyVolumeBurst(in OvSnapshot last, decimal thrMinor = 1.0m, decimal thrMajor = 2.0m)
        {
            decimal z = last.VolBurstZ;
            decimal az = Math.Abs(z);
            LogDebug($"[OrderflowFeatureCalculator] ClassifyVolumeBurst: Bar={last.Bar}, VolBurstZ={z}, thresholds minor={thrMinor}, major={thrMajor}");

            if (az >= thrMajor)
            {
                LogInfo($"[OrderflowFeatureCalculator] ClassifyVolumeBurst: Major burst detected Bar={last.Bar}, z={z}");
                return BurstClass.Major;
            }
            if (az >= thrMinor)
            {
                LogInfo($"[OrderflowFeatureCalculator] ClassifyVolumeBurst: Minor burst detected Bar={last.Bar}, z={z}");
                return BurstClass.Minor;
            }
            return BurstClass.None;
        }

        /// <summary>
        /// ComputeFeaturesForLastBar verwendet die Values aus SetupConfiguration als Defaults.
        /// Du kannst die Parameter überschreiben, falls du spezielle Tests fahren willst.
        /// </summary>
        public OfFeatures ComputeFeaturesForLastBar(
            OvSnapshot currentOvSnapshot,
            int slopeWin = 8,
            int inflectionWin = 6,
            int persistDepth = 8,
            decimal inflectionMinDelta = 0.05m)
        {
            if (currentOvSnapshot == null)
            {
                LogError("[OrderflowFeatureCalculator] ComputeFeaturesForLastBar mit Null aufgerufen currentOvSnapshot. Verwerfen.");
                throw new ArgumentNullException(nameof(currentOvSnapshot));
            }

            LogDebug($"[OrderflowFeatureCalculator] ComputeFeaturesForLastBar: start Bar={currentOvSnapshot.Bar}, slopeWin={slopeWin}, inflectionWin={inflectionWin}");

            if (_ovSnapshotHistory == null || _ovSnapshotHistory.Count < 2)
            {
                LogWarn($"[OrderflowFeatureCalculator] ComputeFeaturesForLastBar: snapshot history short (Count={_ovSnapshotHistory?.Count ?? 0}). Berechnung nach bestem Bemühen (Fenster können kleiner sein).");
                // Weiter: Berechnungen mit geringem Aufwand und kleinen Fenstern zulassen
            }

            int resolvedPersistDepth = persistDepth;
            decimal resolvedInflectionMinDelta = inflectionMinDelta;

            var wSlope = GetLastNSnapshots(slopeWin);
            var wInfl = GetLastNSnapshots(inflectionWin);

            if (wSlope.Count < 2)
            {
                LogWarn($"[OrderflowFeatureCalculator] ComputeFeaturesForLastBar: Neigungsfenster zu klein ({wSlope.Count}) for slopeWin={slopeWin}. Die Ergebnisse der Neigung werden 0.");
            }
            if (wInfl.Count < 3)
            {
                LogDebug($"[OrderflowFeatureCalculator] ComputeFeaturesForLastBar: inflection window small ({wInfl.Count}) for inflectionWin={inflectionWin}. Inflection detection may return None.");
            }

            var f = new OfFeatures
            {
                Snapshot = currentOvSnapshot,
                Bar = currentOvSnapshot.Bar,
                High = currentOvSnapshot.High,
                Low = currentOvSnapshot.Low,
                Open = currentOvSnapshot.Open,
                Close = currentOvSnapshot.Close,    

                
                CvdImpulse = currentOvSnapshot.CvdImpulse,
                AggPressure = currentOvSnapshot.AggPressure,
                Efficiency = currentOvSnapshot.Efficiency,
                TradeRateZ = currentOvSnapshot.TradeRateZ,
                VolBurstZ = currentOvSnapshot.VolBurstZ,
                IttZ = currentOvSnapshot.IttZ,
                MaxCounterShareBull = currentOvSnapshot.MaxCounterShareBull,
                MaxCounterShareBear = currentOvSnapshot.MaxCounterShareBear,
                SweepUpClosed = currentOvSnapshot.SweepUpClosed,
                SweepDnClosed = currentOvSnapshot.SweepDnClosed,

                StackedBuyImbCount = currentOvSnapshot.StackedBuyImbCount,
                StackedSellImbCount = currentOvSnapshot.StackedSellImbCount,
                StackedBuyImbTopCount = currentOvSnapshot.StackedBuyImbTopCount,
                StackedSellImbBottomCount = currentOvSnapshot.StackedSellImbBottomCount,
            };

            f.SlopeCvd = ComputeSlope(wSlope, s => s.CvdImpulse);
            f.SlopePressure = ComputeSlope(wSlope, s => s.AggPressure);
            f.SlopeEff = ComputeSlope(wSlope, s => s.Efficiency);
            f.SlopeTradeRate = ComputeSlope(wSlope, s => s.TradeRateZ);

            f.PersistBull = ComputePersistence(s => s.AggPressure > 0m, resolvedPersistDepth);
            f.PersistBear = ComputePersistence(s => s.AggPressure < 0m, resolvedPersistDepth);

            f.InflectionCvd = DetectInflection(wInfl, s => s.CvdImpulse, resolvedInflectionMinDelta);
            f.InflectionPressure = DetectInflection(wInfl, s => s.AggPressure, resolvedInflectionMinDelta);

            // Wichtig: reine Berechnung, keine Seiteneffekte (kein _volBurstCooldown-Update, kein FeaturesByBar/History-Write)
            // Burst wird nicht final gesetzt hier; wird in CalculateFeatures erneut bewertet und in f.VolBurstClass gesetzt.
            return f;
        }


        /// <summary>
        /// Berechnet OfFeatures für den aktuellen OvSnapshot und archiviert in _ofFeaturesHistory.
        /// Liefert das berechnete OfFeatures zurück (oder null, falls nicht möglich).
        /// </summary>
        public OfFeatures CalculateFeatures(
            OvSnapshot currentOvSnapshot,
            int slopeWin = 12,
            int inflectionWin = 5,
            int? persistDepth = null,
            decimal? inflectionMinDelta = null,
            decimal? burstMinor = null,
            decimal? burstMajor = null,
            int? burstCooldownBars = null,
            bool persistToHistory = true,
            int? minSnapshotsToPersist = null,
            bool persistEarlyOverride = false)
        {
            if (currentOvSnapshot == null)
            {
                LogError("[OrderflowFeatureCalculator] CalculateFeatures called with null currentOvSnapshot. Throwing.");
                throw new ArgumentNullException(nameof(currentOvSnapshot));
            }

            LogDebug($"[OrderflowFeatureCalculator] CalculateFeatures: start Bar={currentOvSnapshot.Bar}, persistToHistory={persistToHistory}");

            // Berechne rein (keine Seiteneffekte)
            var f = ComputeFeaturesForLastBar(
            currentOvSnapshot,
            slopeWin: slopeWin,
            inflectionWin: inflectionWin,
            persistDepth: persistDepth ?? _setupConfig.AdaptiverLookbackBars,
            inflectionMinDelta: inflectionMinDelta ?? _setupConfig.InflectionMinDelta);

            if (f == null)
            {
                LogWarn($"[OrderflowFeatureCalculator] CalculateFeatures: ComputeFeaturesForLastBar returned null for Bar={currentOvSnapshot.Bar}. Skipping persistence.");
                return null;
            }

            // Synchronisiere alle State‑Änderungen (FeaturesByBar, _volBurstCooldown, Trimmen, History)
            lock (_syncObj)
            {
                // VolBurst / Cooldown-Handling (Stateful): gleiche Logik wie vorher, aber zentralisiert hier
                int resolvedBurstCooldown = burstCooldownBars ?? _setupConfig.BurstCooldownBars;
                decimal resolvedBurstMinor = burstMinor ?? _setupConfig.BurstMinorZ;
                decimal resolvedBurstMajor = burstMajor ?? _setupConfig.BurstMajorZ;
                LogDebug($"[OrderflowFeatureCalculator] CalculateFeatures: entering Bar={currentOvSnapshot.Bar}, _volBurstCooldown(before)={_volBurstCooldown}, resolvedBurstMinor={resolvedBurstMinor}, resolvedBurstMajor={resolvedBurstMajor}");
                var burstClass = ClassifyVolumeBurst(currentOvSnapshot, resolvedBurstMinor, resolvedBurstMajor);
                LogDebug($"[OrderflowFeatureCalculator] CalculateFeatures: Bar={currentOvSnapshot.Bar} rawBurstClass={burstClass}, VolBurstZ={currentOvSnapshot.VolBurstZ:F4}");

                if (_volBurstCooldown > 0)
                {
                    // Protokoll vor Unterdrückung/Dekrementierung
                    LogDebug($"[OrderflowFeatureCalculator] CalculateFeatures: Bar={currentOvSnapshot.Bar} - cooldown active (_volBurstCooldown(before)={_volBurstCooldown}). rawBurstClass={burstClass}{(burstClass != BurstClass.None ? " -> suppressing raw burst" : " (no raw burst to suppress)")}, VolBurstZ={currentOvSnapshot.VolBurstZ:F4}");


                    // die existentielle Logik beibehalten
                    _volBurstCooldown--;
                    f.VolBurstClass = BurstClass.None;
                    f.VolBurstCooldownLeft = _volBurstCooldown;

                    // Log after decrement so man die Veränderung sieht
                    LogDebug($"[OrderflowFeatureCalculator] CalculateFeatures: Bar={currentOvSnapshot.Bar} - suppression applied. _volBurstCooldown(after)={_volBurstCooldown}, assigned VolBurstClass={f.VolBurstClass}");
                }
                else
                {
                    // Keine Cooldown - assigniere das burstClass und logge
                    f.VolBurstClass = burstClass;
                    if (burstClass != BurstClass.None)
                    {
                        _volBurstCooldown = resolvedBurstCooldown;
                        LogDebug($"[OrderflowFeatureCalculator] CalculateFeatures: burstClass={burstClass} triggered for Bar={currentOvSnapshot.Bar}. Setting cooldown={_volBurstCooldown}");
                    }
                    f.VolBurstCooldownLeft = _volBurstCooldown;

                   
                    // Log the assignment outcome explicitly
                    LogDebug($"[OrderflowFeatureCalculator] CalculateFeatures: Bar={currentOvSnapshot.Bar} - no cooldown. rawBurstClass={burstClass}, assigned VolBurstClass={f.VolBurstClass}, VolBurstCooldownLeft={f.VolBurstCooldownLeft}");
                }

                // Update FeaturesByBar (immer updaten, damit Strategy aktuellen Wert hat)
                FeaturesByBar[f.Bar] = f;
                LogDebug($"[OrderflowFeatureCalculator] CalculateFeatures: stored OfFeatures for Bar={f.Bar}. FeaturesByBar.Count={FeaturesByBar.Count}");

                // Trim FeaturesByBar basierend auf initial konfigurierter Max-Size                
                while (FeaturesByBar.Count > _maxFeaturesHistorySizeInitial)
                {
                    var oldestKey = FeaturesByBar.Keys.Min();
                    LogDebug($"[OrderflowFeatureCalculator] CalculateFeatures: trimming FeaturesByBar. Removing Bar={oldestKey}");
                    FeaturesByBar.Remove(oldestKey);
                }

                // Persistence policy: decide, ob wir in _ofFeaturesHistory schreiben dürfen
                int requiredSnapshots = minSnapshotsToPersist
                    ?? (_setupConfig != null && _setupConfig.MinSnapshotsToPersist > 0 ? _setupConfig.MinSnapshotsToPersist : 2);
                if (requiredSnapshots <= 0) requiredSnapshots = 2;

                bool allowPersist = persistEarlyOverride || ((_ovSnapshotHistory != null) && (_ovSnapshotHistory.Count >= requiredSnapshots));

                if (persistToHistory && allowPersist)
                {
                    // Idempotenzprüfung: last history entry check
                    bool alreadyInHistory = false; // einmalig deklarieren, keine Doppeldeklaration
                    try
                    {
                        if (_ofFeaturesHistory != null && _ofFeaturesHistory.Count > 0)
                        {
                            if (_ofFeaturesHistory.TryGetRelative(0, out var last) && last != null && last.Bar == f.Bar)
                            {
                                alreadyInHistory = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogWarn($"[OrderflowFeatureCalculator] CalculateFeatures: warning while checking history for idempotence: {ex.Message}");
                        alreadyInHistory = false;
                    }

                    if (!alreadyInHistory)
                    {
                        try
                        {
                            _ofFeaturesHistory.Add(f);
                            LogDebug($"[OrderflowFeatureCalculator] CalculateFeatures: persisted OfFeatures Bar={f.Bar} to history. HistoryCount={_ofFeaturesHistory.Count}");
                        }
                        catch (Exception ex)
                        {
                            LogError($"[OrderflowFeatureCalculator] CalculateFeatures: exception while adding to OfFeaturesHistory: {ex.Message}");
                        }
                    }
                    else
                    {
                        LogDebug($"[OrderflowFeatureCalculator] CalculateFeatures: skipping persistence for Bar={f.Bar} (already in history).");
                    }
                }
                else if (persistToHistory && !allowPersist)
                {
                    LogDebug($"[OrderflowFeatureCalculator] CalculateFeatures: skipping persistence for Bar={f.Bar} because snapshot history too short (Count={_ovSnapshotHistory?.Count ?? 0}, required={requiredSnapshots}). Use persistEarlyOverride to force.");
                }
            } // lock

            return f;
        }

        public OfFeatures GetLastComputedFeature()
        {
            if (FeaturesByBar.Count == 0)
            {
                LogDebug("[OrderflowFeatureCalculator] GetLastComputedFeature: none present. Returning null.");
                return null;
            }
            int k = FeaturesByBar.Keys.Max();
            LogDebug($"[OrderflowFeatureCalculator] GetLastComputedFeature -> Bar={k}");
            return FeaturesByBar[k];
        }
    }
}

