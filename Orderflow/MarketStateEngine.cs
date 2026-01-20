using System;
using System.Linq;
// using ATAS.DataFeedsCore; // Nicht direkt in der Engine benötigt, aber für Modelle
using MyNamespace.Strategies.Models;
using static MyNamespace.Strategies.Goldfluss3_3;
using MyNamespace.Strategies;
using Utils.Common.Logging; // LoggerHelper.Verwendung
using ATAS.Indicators;
using ATAS.Indicators.Technical;
using System.Diagnostics.Metrics;
using static MyNamespace.Strategies.Models.DetectedOrderflowPattern;

namespace MyNamespace.Strategies.Orderflow
{
    public sealed class MarketStateEngineSettings
    {
        public decimal TickSize { get; init; } = 0.25m;

        // Bar-Body-Definition (in Ticks)
        public decimal TrendBodyTicks { get; init; } = 6m;
        public decimal ReversalBodyTicks { get; init; } = 9m;
        public decimal BodyTickTolerance { get; init; } = 0.25m;

        // optional auch andere „Magie-Zahlen“ hier reinziehen
        public int RangeWindowN { get; init; } = 30;
        public int ConfirmBarsSame { get; init; } = 2;         // Bestätigung, wenn candidate == current
        public int ConfirmBarsOpposite { get; init; } = 1;     // Schnellere Bestätigung für gegenläufige, dominante Kandidaten
        public int TrendRunThreshold { get; init; } = 4;
    }
    public class MarketStateEngine
    {
        private MarketDirectionalBias _currentBias = MarketDirectionalBias.Undefined;
        private readonly OfFeaturesHistory _ofFeaturesHistory;
        private readonly OrderflowThresholdManager _thresholdManager;
        private readonly ILoggerSource _loggerSource;
        private readonly RegimeDetector _volBurstDetector;
        private readonly RegimeDetector _tradeRateDetector;
        private readonly RegimeDetector _cvdImpulseDetector;

        // Letzter erzeugter Snapshot (wird bei jedem Update neu gesetzt)
        private volatile MarketState _currentMarketState;

        public MarketState CurrentMarketState => _currentMarketState;
        public event Action<MarketState> MarketStateUpdated;

        // Neue Default-Werte (aus deiner Auswertung)
        private const decimal DefaultAggPressure = 0.481415m;   // AggPressure default (≈ 0.48)
        private const decimal DefaultCvdImpulse = -5.645m;      // CvdImpulse default (≈ -5.645)
        private const decimal DefaultVolBurstZ = 0.3349m;       // VolBurstZ default (≈ 0.33)
        private const decimal DefaultEfficiency = 0.42m;        // Efficiency default (≈ 0.42)
        private const int DefaultPersistBear = 0;               // PersistBear default (setze niedrig, sonst Bear nie möglich)

        private readonly RangeStructureDetector _rangeDetector;
        private MarketStateEngineSettings _settings;
        private decimal _tickSize; // aus Settings abgeleitet (damit Update möglich ist)
        private readonly decimal _effTrendThreshold = 0.55m;
        private readonly decimal _effSideThreshold = 0.35m;
        private readonly decimal _flipHighThreshold = 0.55m; // hohe Flip-Rate => choppy
        private readonly decimal _revShareHigh = 0.45m; // hoher Anteil Reversalbars => range/sideways
        private int _confirmCount = 0; // zählt Bestätigungs‑Bars für aktuellen Kandidaten
        private MarketDirectionalBias _pendingBias = MarketDirectionalBias.Undefined; // bias, der bestätigt werden soll

        // Neue Variablen zur Hysterese / Undefined-Handling
        private int _undefinedPersistCount = 0;
        private const decimal DOMINANCE_DELTA = 0.03m; // absolute Differenz für relative Dominanz
        private const decimal MIN_DOMINANT_SCORE = 0.35m; // minimale absolute Score für Dominanz-Fall

        //Warum: _rangeDetector kapselt Berechnung von runT/flipRate/revShare/eff über Fenster N._confirmCount/_pendingBias implementieren einfache Hysterese (M).


        public MarketStateEngine(
        OfFeaturesHistory? ofHistory,
        OrderflowThresholdManager thresholdManager,
        ILoggerSource loggerSource, MarketStateEngineSettings settings)
        {
            _ofFeaturesHistory = ofHistory ?? new OfFeaturesHistory();
            _thresholdManager = thresholdManager ?? throw new ArgumentNullException(nameof(thresholdManager));
            _loggerSource = loggerSource ?? throw new ArgumentNullException(nameof(loggerSource));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            _tickSize = _settings.TickSize > 0m ? _settings.TickSize : 1m;

            _rangeDetector = new RangeStructureDetector(_settings.RangeWindowN);
            if (ofHistory == null)
            {
                LoggerHelper.LogWarn(_loggerSource, "[MarketStateEngine] ofHistory war beim Erstellen null – unter Verwendung eines leeren OfFeaturesHistory-Fallbacks.");
            }

            LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] initialisiert. Initial bias: {_currentBias}");

            // Detector init (defaults tuned)

            // 1. VolBurstZ Detector
            var volCfg = new RegimeDetectorConfig
            {
                EwmaAlpha = 0.07m,
                ZThreshold = 6.0m,
                RatioHigh = 3.0m,
                RatioLow = 0.33m,
                SlopeThreshold = 1.5m,
                CooldownBars = 8,
                RequireClearConsecutive = 4,
                WeightZ = 1.5m,
                WeightRatio = 1.0m,
                WeightSlope = 0.4m,
                EwmaMinimumDenominator = 0.01m
            };
            _volBurstDetector = new RegimeDetector("VolBurstZ", volCfg);

            // 2. TradeRateZ Detector
            var trCfg = new RegimeDetectorConfig
            {
                EwmaAlpha = 0.10m,
                ZThreshold = 5.0m,
                RatioHigh = 2.0m,
                RatioLow = 0.5m,
                SlopeThreshold = 1.2m,
                CooldownBars = 6,
                RequireClearConsecutive = 3,
                WeightZ = 1.5m,
                WeightRatio = 1.0m,
                WeightSlope = 0.4m,
                EwmaMinimumDenominator = 0.1m
            };
            _tradeRateDetector = new RegimeDetector("TradeRateZ", trCfg);

            // 3. CvdImpulse Detector
            var cvdCfg = new RegimeDetectorConfig
            {
                EwmaAlpha = 0.05m,
                ZThreshold = 5.0m,
                RatioHigh = 3.0m,
                RatioLow = 0.33m,
                SlopeThreshold = 1.0m,
                CooldownBars = 4,
                RequireClearConsecutive = 2,
                WeightZ = 1.5m,
                WeightRatio = 1.0m,
                WeightSlope = 0.4m,
                EwmaMinimumDenominator = 10.0m
            };
            _cvdImpulseDetector = new RegimeDetector("CvdImpulse", cvdCfg);
        }

        public MarketDirectionalBias CurrentBias => _currentBias;

        private string FormatThresholds(OrderflowThresholds t)
        {
            if (t == null) return "null";
            return $"ThCvdImpulseLong={t.ThCvdImpulseLong}, ThCvdImpulseShort={t.ThCvdImpulseShort}, " +
                   $"ThAggPressureBreakoutBear={t.ThAggPressureBreakoutBear}, ThVolBurstZ={t.ThVolBurstZ}, ThEfficiency={t.ThEfficiency}, " +
                   $"ThTradeRateZBreakout={t.ThTradeRateZBreakout}, MaxCounterDeltaShareBull={t.MaxCounterDeltaShareBull}, MaxCounterDeltaShareBear={t.MaxCounterDeltaShareBear}, " +
                   $"ThStackedImbAnyRangeMinDirectional={t.ThStackedImbAnyRangeMinDirectional}";
        }

        private void LogCheck(string prefix, string name, decimal value, decimal? threshold, string comparator, bool result)
        {
            LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] {prefix}: {name}={value:F6} {comparator} {threshold?.ToString() ?? "null"} => {result}");
        }

        private OrderflowThresholds GetThresholdsForBias(FullThresholdSnapshot snapshot, MarketDirectionalBias bias)
        {
            return bias switch
            {
                MarketDirectionalBias.BullishTrend => snapshot.Bullish ?? new OrderflowThresholds(),
                MarketDirectionalBias.BearishTrend => snapshot.Bearish ?? new OrderflowThresholds(),
                MarketDirectionalBias.Sideways => snapshot.Sideways ?? new OrderflowThresholds(),
                MarketDirectionalBias.Choppy => snapshot.Choppy ?? new OrderflowThresholds(),
                _ => new OrderflowThresholds(),
            };
        }

        private static decimal Clamp01(decimal v)
        {
            if (v < 0m) return 0m;
            if (v > 1m) return 1m;
            return v;
        }

        public MarketState UpdateState(
        OfFeatures currentOfFeatures,
        MarketRegime currentMarketRegime,
        DetectedOrderflowPattern detectedPattern)
        {
            var entryPattern = (detectedPattern?.Type == OrderflowPatternType.None) ? "GENERIC" : detectedPattern?.Type.ToString() ?? "GENERIC";
            var entrySig = SmartLogger.ComposeSignature(("bar", (currentOfFeatures?.Bar ?? -1).ToString()), ("regime", currentMarketRegime.ToString()), ("pattern", entryPattern));
            SmartLogger.Instance.LogIfChanged(
                category: "MarketState",
                sourceId: "MarketStateEngine",
                barIndex: currentOfFeatures?.Bar ?? -1,
                message: $"MarketStateEngine.UpdateState aufgerufen. bar={currentOfFeatures?.Bar ?? -1}, Regime={currentMarketRegime}, Pattern={entryPattern}",
                signature: entrySig,
                backendLogAction: s => LoggerHelper.LogInfo(_loggerSource, $"[MarketStateEngine] {s}")
            );

            if (currentOfFeatures == null)
            {
                LoggerHelper.LogWarn(_loggerSource, "[MarketStateEngine] currentOfFeatures ist null — Abbruch vor Detector-Updates.");
                return null;
            }

            // Update detectors (bleiben, aber Schock-Handling wird NICHT verwendet)
            try
            {
                _volBurstDetector.Update(currentOfFeatures.VolBurstZ, currentOfFeatures.Bar, DateTime.UtcNow);
                _tradeRateDetector.Update(currentOfFeatures.TradeRateZ, currentOfFeatures.Bar, DateTime.UtcNow);
                _cvdImpulseDetector.Update(currentOfFeatures.CvdImpulse, currentOfFeatures.Bar, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                LoggerHelper.LogWarn(_loggerSource, $"[MarketStateEngine] RegimeDetector update failed: {ex.Message}");
            }

            // WICHTIG: Schock-Logik deaktiviert — wir ignorieren Detectors für State-Anpassungen aktuell.
            bool isShocked = false;

            MarketDirectionalBias previousBias = _currentBias;

            var newState = new MarketState
            {
                Regime = currentMarketRegime,
                TimestampUtc = DateTime.UtcNow,
                DirectionalBias = _currentBias,
                Confidence = 0m
            };

            var patternHint = (detectedPattern?.Type == OrderflowPatternType.None) ? (OrderflowPatternType?)null : detectedPattern.Type;
            var snapshot = _thresholdManager.GetFullThresholdSnapshotForRegime(currentMarketRegime, patternHint);

            var snapshotPattern = patternHint?.ToString() ?? "GENERIC";
            var snapshotSig = SmartLogger.ComposeSignature(("bar", currentOfFeatures.Bar.ToString()), ("regime", currentMarketRegime.ToString()), ("hv", snapshot.HistoryVersion.ToString() ?? "-1"), ("pattern", snapshotPattern));
            SmartLogger.Instance.LogIfChanged(
                category: "Thresholds",
                sourceId: "MarketStateEngine",
                barIndex: currentOfFeatures.Bar,
                message: $"MarketStateEngine: Verwendung FullThresholdSnapshot für Regime={currentMarketRegime}, HistoryVersion={snapshot.HistoryVersion}, PatternHint={snapshotPattern}",
                signature: snapshotSig,
                backendLogAction: s => LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] {s}")
            );

            var thBullBase = snapshot.Bullish ?? new OrderflowThresholds();
            var thBearBase = snapshot.Bearish ?? new OrderflowThresholds();
            var thChoppyBase = snapshot.Choppy ?? new OrderflowThresholds();
            var thSideBase = snapshot.Sideways ?? new OrderflowThresholds();

            // Lokale Kopien (keine Schock-Änderungen mehr)
            var localThBull = thBullBase.Clone();
            var localThBear = thBearBase.Clone();
            var localThChoppy = thChoppyBase.Clone();
            var localThSide = thSideBase.Clone();

            int persistOffsetLocal = 0;
            DetectedOrderflowPattern detectedPatternLocal = detectedPattern;

            // --- Logging Thresholds ---
            var thresholdsSig = SmartLogger.ComposeSignature(("bar", currentOfFeatures.Bar.ToString()), ("hv", snapshot.HistoryVersion.ToString() ?? "-1"));
            SmartLogger.Instance.LogIfChanged(
                category: "Thresholds",
                sourceId: "MarketStateEngine",
                barIndex: currentOfFeatures.Bar,
                message: $"Thresholds (Regime={currentMarketRegime}): Bullish: {FormatThresholds(localThBull)}",
                signature: thresholdsSig + SmartLogger.ComposeSignature(("which", "Bullish")),
                backendLogAction: s => LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] {s}")
            );
            SmartLogger.Instance.LogIfChanged(
                category: "MarketState",
                sourceId: "MarketStateEngine",
                barIndex: currentOfFeatures.Bar,
                message: $"Thresholds (Regime={currentMarketRegime}): Bearish: {FormatThresholds(localThBear)}",
                signature: thresholdsSig + SmartLogger.ComposeSignature(("which", "Bearish")),
                backendLogAction: s => LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] {s}")
            );
            SmartLogger.Instance.LogIfChanged(
                category: "MarketState",
                sourceId: "MarketStateEngine",
                barIndex: currentOfFeatures.Bar,
                message: $"Thresholds (Regime={currentMarketRegime}): Choppy: {FormatThresholds(localThChoppy)}",
                signature: thresholdsSig + SmartLogger.ComposeSignature(("which", "Choppy")),
                backendLogAction: s => LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] {s}")
            );
            SmartLogger.Instance.LogIfChanged(
                category: "MarketState",
                sourceId: "MarketStateEngine",
                barIndex: currentOfFeatures.Bar,
                message: $"Thresholds (Regime={currentMarketRegime}): Sideways: {FormatThresholds(localThSide)}",
                signature: thresholdsSig + SmartLogger.ComposeSignature(("which", "Sideways")),
                backendLogAction: s => LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] {s}")
            );


            // --- Squeeze-Score Berechnung via SqueezeUtils ---
            var squeeze = currentOfFeatures.Squeeze;
            var squeezeVolScore = SqueezeUtils.MapSqueezeToVolScore(squeeze);
            var squeezeDirScore = SqueezeUtils.GetDirScore(squeeze);

            var squeezeSig = SmartLogger.ComposeSignature(("bar", currentOfFeatures.Bar.ToString()), ("squeezeReady", SqueezeUtils.IsSqueezeReady(squeeze).ToString()), ("volScore", squeezeVolScore.ToString("F2")), ("dirScore", squeezeDirScore.ToString("F3")));
            SmartLogger.Instance.LogIfChanged(
                category: "MarketState",
                sourceId: "Squeeze",
                barIndex: currentOfFeatures.Bar,
                message: $"Squeeze: {squeeze?.ToString() ?? "null"}, VolScore={squeezeVolScore:F2}, DirScore={squeezeDirScore:F3}, IsReady={SqueezeUtils.IsSqueezeReady(squeeze)}",
                signature: squeezeSig,
                backendLogAction: s => LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] {s}")
            );

            // --- RANGE STRUCTURE: AddBar (nur bei abgeschlossenen Rangebars) ---
            try
            {
                var barDir =
                    currentOfFeatures.Close > currentOfFeatures.Open ? 1 :
                    currentOfFeatures.Close < currentOfFeatures.Open ? -1 : 0;

                decimal body = Math.Abs(currentOfFeatures.Close - currentOfFeatures.Open);
                decimal bodyTicksDecimal = _tickSize > 0m ? body / _tickSize : 0m;

                // vorhandene Settings (wie in deinem ursprünglichen Code)
                decimal tolDecimal = _settings.BodyTickTolerance;
                decimal trendTicksDecimal = _settings.TrendBodyTicks;
                decimal revTicksDecimal = _settings.ReversalBodyTicks;

                // Arbeit mit ganzen Ticks (robuster)
                int ticks = (int)Math.Round(bodyTicksDecimal, MidpointRounding.AwayFromZero);
                int trendTicksInt = (int)Math.Round(trendTicksDecimal, MidpointRounding.AwayFromZero);
                int revTicksInt = (int)Math.Round(revTicksDecimal, MidpointRounding.AwayFromZero);

                // Toleranz in ganzen Ticks (einstellbar)
                int tickTolerance = 0; // 0 = strikt, setze 1 für lockerere Erkennung

                bool isTrendBar;
                if (Math.Abs(ticks - trendTicksInt) <= tickTolerance)
                    isTrendBar = true;
                else if (Math.Abs(ticks - revTicksInt) <= tickTolerance)
                    isTrendBar = false;
                else
                {
                    // Fallback‑Regeln: near‑trend oder basierend auf Efficiency
                    if (ticks >= trendTicksInt - 1) isTrendBar = true;
                    else isTrendBar = currentOfFeatures.Efficiency > 0.60m;
                }

                // Neue AddBar-Signatur erwartet ticks als int
                _rangeDetector.AddBar(barDir, isTrendBar, ticks);

                LoggerHelper.LogDebug(
                    _loggerSource,
                    $"[MarketStateEngine] Range.AddBar: barDir={barDir}, isTrendBar={isTrendBar}, bodyTicks={bodyTicksDecimal:F3}, ticks={ticks} " +
                    $"(trend={trendTicksInt}, rev={revTicksInt}, tickTol={tickTolerance}, tolDecimal={tolDecimal}), eff={currentOfFeatures.Efficiency:F3}"
                );
            }
            catch (Exception ex)
            {
                LoggerHelper.LogWarn(_loggerSource, $"[MarketStateEngine] RangeStructureDetector update failed: {ex.Message}");
            }



            // --- Compute RangeStructure Features & Logging ---
            var rsFeat = _rangeDetector.ComputeFeatures();
            // Erwartung: ComputeFeatures sollte nun RunTrendUp / RunTrendDown zusätzlich zu RunTrend liefern.
            // Wenn nicht vorhanden, bleiben die bisherigen Werte erhalten (backwards compatible).

            // Berechne sichere lokale Norms für RunTrendUp/Down. Wenn ComputeFeatures nur RunTrend liefert, verwenden wir fallback.
            decimal runTrendNorm = rsFeat.Window > 0 ? Clamp01(rsFeat.RunTrend / (decimal)Math.Max(6, _settings.TrendRunThreshold + 2)) : 0m;
            // Fallback: setze runTrendUp/Down auf runTrendNorm (Kompatibilität)
            decimal runTrendUpNorm = runTrendNorm;
            decimal runTrendDownNorm = runTrendNorm;

            LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] RangeStruct: RunTrend={rsFeat.RunTrend}, RunTrendUp={(rsFeat.RunTrend != 0 ? runTrendUpNorm.ToString("F3") : "NA")}, RunTrendDown={(rsFeat.RunTrend != 0 ? runTrendDownNorm.ToString("F3") : "NA")}, flip={rsFeat.FlipRate:F3}, rev={rsFeat.RevShare:F3}, eff={rsFeat.Efficiency:F3}, N={rsFeat.Window}");

            if (_currentBias == MarketDirectionalBias.Undefined)
            {
                if (_ofFeaturesHistory == null || _ofFeaturesHistory.Count < 3)
                {
                    LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] waiting for history to fill (need >=10). Current: {_ofFeaturesHistory?.Count ?? 0}");
                    newState.DirectionalBias = MarketDirectionalBias.Undefined;
                    newState.Confidence = 0m;

                    _currentMarketState = newState;
                    MarketStateUpdated?.Invoke(_currentMarketState);

                    var updSig = SmartLogger.ComposeSignature(("bar", currentOfFeatures.Bar.ToString()), ("hv", snapshot.HistoryVersion.ToString() ?? "-1"), ("bias", newState.DirectionalBias.ToString()));
                    SmartLogger.Instance.LogIfChanged(
                        category: "MarketState",
                        sourceId: "MarketStateEngine",
                        barIndex: currentOfFeatures.Bar,
                        message: $"MarketStateEngine: MarketStateUpdated invoked for bar={currentOfFeatures.Bar} (insufficient history).",
                        signature: updSig,
                        backendLogAction: s => LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] {s}")
                    );

                    // statt return: erlauben wir initiale Erkennung auch ohne Historie; RangeFeatures werden dann 0 sein
                    // (nur Warnung, kein return).
                    LoggerHelper.LogWarn(_loggerSource, "[MarketStateEngine] Weniger als minimale Historie: Fortfahren mit begrenzter Anfangserkennung.");
                }

                // --- Anpassung: sinnvolle Defaults verwenden, wenn Thresholds null sind ---
                decimal thCvdLongBull = localThBull.ThCvdImpulseLong ?? DefaultCvdImpulse;
                decimal thAggBull = localThBull.ThAggPressureBreakoutBear ?? DefaultAggPressure;
                decimal thVolBull = localThBull.ThVolBurstZ ?? DefaultVolBurstZ;
                decimal thEffBull = localThBull.ThEfficiency ?? DefaultEfficiency;
                int persistReqInitialBull = Math.Max(1, (localThBull.ThPersistBull ?? 1)) + persistOffsetLocal;
                int persistReqInitialBear = Math.Max(0, (localThBear.ThPersistBear ?? DefaultPersistBear)) + persistOffsetLocal;

                LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] MarketStateEngine-Anfängliche Erkennungsschwellen (BullishTrend): CvdLong={thCvdLongBull}, Agg>{thAggBull}, Vol>{thVolBull}, Eff>{thEffBull}, Persist>={persistReqInitialBull}");

                bool condCvd = currentOfFeatures.CvdImpulse > thCvdLongBull;
                LogCheck("Initial", "CvdImpulse", currentOfFeatures.CvdImpulse, thCvdLongBull, ">", condCvd);

                // AggPressure: hier direkte Vergleichs-Schwelle (statt 1 - x), weil Defaults nun in 0..1 liegen.
                bool condAgg = currentOfFeatures.AggPressure > thAggBull;
                LogCheck("Initial", "AggPressure", currentOfFeatures.AggPressure, thAggBull, ">", condAgg);

                bool condPersist = currentOfFeatures.PersistBull >= persistReqInitialBull;
                LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] Initial: PersistBull={currentOfFeatures.PersistBull} >= {persistReqInitialBull} => {condPersist}");

                bool condSlope = currentOfFeatures.SlopeCvd > 0m;
                LogCheck("Initial", "SlopeCvd", currentOfFeatures.SlopeCvd, 0m, ">", condSlope);

                bool condVol = currentOfFeatures.VolBurstZ > thVolBull;
                LogCheck("Initial", "VolBurstZ", currentOfFeatures.VolBurstZ, thVolBull, ">", condVol);

                bool condEff = currentOfFeatures.Efficiency > thEffBull;
                LogCheck("Initial", "Efficiency", currentOfFeatures.Efficiency, thEffBull, ">", condEff);

                int bullPossible = 6;
                int bullSatisfied = 0;
                var bullDetails = new System.Text.StringBuilder();
                bullSatisfied += condCvd ? 1 : 0; bullDetails.Append($"CvdImpulse={currentOfFeatures.CvdImpulse:F6} > {thCvdLongBull:F6} => {condCvd}; ");
                bullSatisfied += condAgg ? 1 : 0; bullDetails.Append($"AggPressure={currentOfFeatures.AggPressure:F6} > {thAggBull:F6} => {condAgg}; ");
                bullSatisfied += condPersist ? 1 : 0; bullDetails.Append($"PersistBull={currentOfFeatures.PersistBull} >= {persistReqInitialBull} => {condPersist}; ");
                bullSatisfied += condSlope ? 1 : 0; bullDetails.Append($"SlopeCvd={currentOfFeatures.SlopeCvd:F6} > 0 => {condSlope}; ");
                bullSatisfied += condVol ? 1 : 0; bullDetails.Append($"VolBurstZ={currentOfFeatures.VolBurstZ:F6} > {thVolBull:F6} => {condVol}; ");
                bullSatisfied += condEff ? 1 : 0; bullDetails.Append($"Efficiency={currentOfFeatures.Efficiency:F6} > {thEffBull:F6} => {condEff}; ");
                LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] Initial Bull Checks: zufrieden={bullSatisfied}/{bullPossible}. Details: {bullDetails.ToString()}");
                int bullRequire = 4;
                bool isBullishInitial = bullSatisfied >= bullRequire;

                // Squeeze HARTES GATE entfernt: Squeeze unterstützt keinen Ausbruch blockiert initial nicht mehr
                if (isBullishInitial)
                {
                    _currentBias = MarketDirectionalBias.BullishTrend;
                    LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] MarketStateEngine: Initial detection set Bias -> BullishTrend at bar {currentOfFeatures.Bar} (bullSatisfied={bullSatisfied}/{bullPossible})");
                }
                else
                {
                    decimal thCvdShortBear = localThBear.ThCvdImpulseShort ?? DefaultCvdImpulse;
                    decimal thAggBear = localThBear.ThAggPressureBreakoutBear ?? DefaultAggPressure;
                    decimal thVolBear = localThBear.ThVolBurstZ ?? DefaultVolBurstZ;
                    decimal thEffBear = localThBear.ThEfficiency ?? DefaultEfficiency;

                    LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] MarketStateEngine-Anfängliche Erkennungsschwellen (BearishTrend): CvdShort={thCvdShortBear}, Agg<{thAggBear}, Vol>{thVolBear}, Eff>{thEffBear}, Persist>={persistReqInitialBear}");

                    bool condCvdB = currentOfFeatures.CvdImpulse < thCvdShortBear;
                    LogCheck("Initial", "CvdImpulse (bear)", currentOfFeatures.CvdImpulse, thCvdShortBear, "<", condCvdB);

                    bool condAggB = currentOfFeatures.AggPressure < thAggBear;
                    LogCheck("Initial", "AggPressure (bear)", currentOfFeatures.AggPressure, thAggBear, "<", condAggB);

                    bool condPersistB = currentOfFeatures.PersistBear >= persistReqInitialBear;
                    LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] Initial: PersistBear={currentOfFeatures.PersistBear} >= {persistReqInitialBear} => {condPersistB}");

                    bool condSlopeB = currentOfFeatures.SlopeCvd < 0m;
                    LogCheck("Initial", "SlopeCvd (bear)", currentOfFeatures.SlopeCvd, 0m, "<", condSlopeB);

                    bool condVolB = currentOfFeatures.VolBurstZ > thVolBear;
                    LogCheck("Initial", "VolBurstZ (bear)", currentOfFeatures.VolBurstZ, thVolBear, ">", condVolB);

                    bool condEffB = currentOfFeatures.Efficiency > thEffBear;
                    LogCheck("Initial", "Efficiency (bear)", currentOfFeatures.Efficiency, thEffBear, ">", condEffB);

                    int bearPossible = 6;
                    int bearSatisfied = 0;
                    var bearDetails = new System.Text.StringBuilder();
                    bearSatisfied += condCvdB ? 1 : 0; bearDetails.Append($"CvdImpulse={currentOfFeatures.CvdImpulse:F6} < {thCvdShortBear:F6} => {condCvdB}; ");
                    bearSatisfied += condAggB ? 1 : 0; bearDetails.Append($"AggPressure={currentOfFeatures.AggPressure:F6} < {thAggBear:F6} => {condAggB}; ");
                    bearSatisfied += condPersistB ? 1 : 0; bearDetails.Append($"PersistBear={currentOfFeatures.PersistBear} >= {persistReqInitialBear} => {condPersistB}; ");
                    bearSatisfied += condSlopeB ? 1 : 0; bearDetails.Append($"SlopeCvd={currentOfFeatures.SlopeCvd:F6} < 0 => {condSlopeB}; ");
                    bearSatisfied += condVolB ? 1 : 0; bearDetails.Append($"VolBurstZ={currentOfFeatures.VolBurstZ:F6} > {thVolBear:F6} => {condVolB}; ");
                    bearSatisfied += condEffB ? 1 : 0; bearDetails.Append($"Efficiency={currentOfFeatures.Efficiency:F6} > {thEffBear:F6} => {condEffB}; ");

                    LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] Initial Bear Checks: satisfied={bearSatisfied}/{bearPossible}. Details: {bearDetails.ToString()}");
                    int bearRequire = 4;
                    bool isBearishInitial = bearSatisfied >= bearRequire;

                    // Squeeze HARTES GATE entfernt: Squeeze unterstützt keinen Ausbruch blockiert initial nicht mehr
                    if (isBearishInitial)
                    {
                        _currentBias = MarketDirectionalBias.BearishTrend;
                        LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] MarketStateEngine: Initial detection set Bias -> BearishTrend at bar {currentOfFeatures.Bar} (bearSatisfied={bearSatisfied}/{bearPossible})");
                    }
                    else
                    {
                        // --- Choppy (Initial) ---
                        decimal thChoppyEff = localThChoppy.ThEfficiency ?? DefaultEfficiency;
                        decimal thChoppyCvdHalf = Math.Abs(localThChoppy.ThCvdImpulseLong ?? DefaultCvdImpulse) / 2m;
                        decimal thChoppyRate = Math.Abs(localThChoppy.ThTradeRateZBreakout ?? Math.Abs(currentOfFeatures.TradeRateZ));

                        LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] MarketStateEngine-Anfängliche Erkennungsschwellen (Choppy): Eff<{thChoppyEff}, Abs(Cvd)<{thChoppyCvdHalf}, |TradeRateZ|>{thChoppyRate}");

                        bool condChoppyEff = currentOfFeatures.Efficiency < thChoppyEff;
                        LogCheck("Initial", "Efficiency (choppy)", currentOfFeatures.Efficiency, thChoppyEff, "<", condChoppyEff);

                        bool condChoppyCvd = Math.Abs(currentOfFeatures.CvdImpulse) < thChoppyCvdHalf;
                        LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] Initial: Abs(Cvd)={Math.Abs(currentOfFeatures.CvdImpulse):F6} < {thChoppyCvdHalf:F6} => {condChoppyCvd}");

                        bool condChoppyRate = Math.Abs(currentOfFeatures.TradeRateZ) > thChoppyRate;
                        LogCheck("Initial", "TradeRateZ (choppy)", Math.Abs(currentOfFeatures.TradeRateZ), thChoppyRate, ">", condChoppyRate);

                        bool condChoppyCounter = currentOfFeatures.MaxCounterShareBull > (localThChoppy.MaxCounterDeltaShareBull ?? 0m)
                                                && currentOfFeatures.MaxCounterShareBear > (localThChoppy.MaxCounterDeltaShareBear ?? 0m);
                        LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] Initial: MaxCounterShareBull={currentOfFeatures.MaxCounterShareBull:F6}, MaxCounterShareBear={currentOfFeatures.MaxCounterShareBear:F6}, condBoth={condChoppyCounter}");


                        bool isChoppyInitial = currentMarketRegime == MarketRegime.Fast && condChoppyEff && condChoppyCvd && (condChoppyRate || condChoppyCounter);

                        if (isChoppyInitial)
                        {
                            _currentBias = MarketDirectionalBias.Choppy;
                            LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] MarketStateEngine: Initial detection set bias to Choppy at bar {currentOfFeatures.Bar}");
                        }
                        else
                        {
                            // --- Sideways (Initial) ---
                            decimal thSideCvdQuarter = Math.Abs(localThSide.ThCvdImpulseLong ?? DefaultCvdImpulse) / 4m;
                            decimal thSideVol = localThSide.ThVolBurstZ ?? DefaultVolBurstZ;
                            decimal thSideEff = localThSide.ThEfficiency ?? DefaultEfficiency;
                            LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] MarketStateEngine-Anfängliche Erkennungsschwellen (Sideways): Abs(Cvd)<{thSideCvdQuarter}, Agg in (0.4,0.6) approx, Vol<{thSideVol}, Eff<{thSideEff}");

                            bool condSideCvd = Math.Abs(currentOfFeatures.CvdImpulse) < thSideCvdQuarter;
                            LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] Initial: Abs(Cvd)={Math.Abs(currentOfFeatures.CvdImpulse):F6} < {thSideCvdQuarter:F6} => {condSideCvd}");

                            bool condSideAgg = currentOfFeatures.AggPressure > 0.4m && currentOfFeatures.AggPressure < 0.6m;
                            LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] Initial: AggPressure={currentOfFeatures.AggPressure:F6} > 0.4 < 0.6 => {condSideAgg}");

                            bool condSideVol = currentOfFeatures.VolBurstZ < thSideVol;
                            LogCheck("Initial", "VolBurstZ (sideways)", currentOfFeatures.VolBurstZ, thSideVol, "<", condSideVol);

                            bool condSideEff = currentOfFeatures.Efficiency < thSideEff;
                            LogCheck("Initial", "Efficiency (sideways)", currentOfFeatures.Efficiency, thSideEff, "<", condSideEff);

                            bool condSidePersist = currentOfFeatures.PersistBull < (2 + persistOffsetLocal) && currentOfFeatures.PersistBear < (2 + persistOffsetLocal);
                            LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] Initial: PersistBull={currentOfFeatures.PersistBull}, PersistBear={currentOfFeatures.PersistBear}, condBothPersist={condSidePersist}");

                            LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] Initial Choppy Checks: eff<{thChoppyEff} => {condChoppyEff}, abs(Cvd)={Math.Abs(currentOfFeatures.CvdImpulse):F6} < {thChoppyCvdHalf:F6} => {condChoppyCvd}, tradeRateCond => {condChoppyRate}, countersCond => {condChoppyCounter}");
                            bool isSidewaysInitial =
                                (currentMarketRegime == MarketRegime.Normal || currentMarketRegime == MarketRegime.Slow)
                                && condSideCvd && condSideAgg && condSideVol && condSideEff && condSidePersist;

                            if (isSidewaysInitial)
                            {
                                _currentBias = MarketDirectionalBias.Sideways;
                                LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] MarketStateEngine: Initial detection set bias to Sideways at bar {currentOfFeatures.Bar}");
                                LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] Initial Sideways Checks: AbsCvd<{thSideCvdQuarter} => {condSideCvd}, AggCenter => {condSideAgg}, Vol<{thSideVol} => {condSideVol}, Eff<{thSideEff} => {condSideEff}, PersistBoth<{2 + persistOffsetLocal} => {condSidePersist}");
                            }
                        }
                    }
                }
            }
            // --- Zustandsübergänge (wie vorher), aber mit angepassten Default-Threshold-Resolves ---
            if (_currentBias != MarketDirectionalBias.Undefined)
            {
                var transSig = SmartLogger.ComposeSignature(("bar", currentOfFeatures.Bar.ToString()), ("bias", _currentBias.ToString()));
                SmartLogger.Instance.LogIfChanged(
                    category: "MarketState",
                    sourceId: "MarketStateEngine",
                    barIndex: currentOfFeatures.Bar,
                    message: $"StateTransition: Bewertung von Übergängen für currentBias={_currentBias}, bar={currentOfFeatures.Bar}",
                    signature: transSig,
                    backendLogAction: s => LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] {s}")
                );

                var currentBiasThresholds = GetThresholdsForBias(snapshot, _currentBias);
                var breakoutThresholds = thBullBase;
                var reversionThresholds = thSideBase;

                // --- Ermittlung eines candidateBias erweitert mit RangeStructure-Unterstützung ---
                // Berechne gewichtete Scores für alle Regime (0..1)
                Func<decimal, decimal, decimal> norm = (val, max) => max <= 0m ? 0m : Clamp01(val / max);

                // Grundlegende Orderflow‑Contributions (normiert)
                decimal cvdStrength = 0m; // -1 .. +1 normalized to 0..1 per side later
                                          // Normalisiere CVD in einem ad-hoc-Bereich (absCap)
                decimal cvdCap = Math.Max(1m, Math.Abs(localThBull.ThCvdImpulseLong ?? DefaultCvdImpulse) * 2m); // heuristic cap
                cvdStrength = Clamp01((currentOfFeatures.CvdImpulse + cvdCap) / (2m * cvdCap)); // maps negative..positive into 0..1

                // AggPressure already in [0,1], map center to sideways
                decimal agg = Clamp01(currentOfFeatures.AggPressure);

                decimal eff = Clamp01(currentOfFeatures.Efficiency);
                decimal vol = Clamp01(currentOfFeatures.VolBurstZ / (DefaultVolBurstZ * 4m + 0.0001m)); // rough normalization
                decimal tradeRate = Clamp01(Math.Abs(currentOfFeatures.TradeRateZ) / 5m); // heuristic normalization

                // Range features (already 0..1-ish except RunTrend)
                decimal runTrendNorm_local = rsFeat.Window > 0 ? Clamp01(rsFeat.RunTrend / (decimal)Math.Max(6, _settings.TrendRunThreshold + 2)) : 0m;
                decimal flipRateNorm = Clamp01(rsFeat.FlipRate); // 0..1
                decimal revShareNorm = Clamp01(rsFeat.RevShare); // 0..1
                decimal rsEff = Clamp01(rsFeat.Efficiency); // 0..1

                // Verwende die bereits ermittelten Fallback-Variablen runTrendUp/runTrendDown
                decimal runTrendUpNorm_local = runTrendUpNorm;
                decimal runTrendDownNorm_local = runTrendDownNorm;

                // Wenn genug Rangebars vorhanden, priorisiere Range-Struktur höher
                decimal rangePriority = rsFeat.Window >= _settings.RangeWindowN ? 0.5m : 0.25m;

                // Scores für Bull / Bear / Sideways / Choppy mittels gewichteter Kombinationen
                decimal ofWeight = 1m - rangePriority;
                decimal rsWeight = rangePriority;

                // of bulls share: cvdStrength (towards 1), agg>0.5, eff, vol, slope positive and persist
                decimal ofBull = 0.0m;
                {
                    decimal wCvd = 0.35m;
                    decimal wAgg = 0.20m;
                    decimal wEff = 0.20m;
                    decimal wVol = 0.10m;
                    decimal wPersist = 0.15m;
                    decimal persistScore = Clamp01((decimal)currentOfFeatures.PersistBull / 5m);
                    decimal slopeScore = currentOfFeatures.SlopeCvd > 0m ? 1m : 0m;
                    decimal aggBull = Clamp01((agg - 0.5m) * 2m + 0.5m); // maps >0.5 to >0.5
                    ofBull = Clamp01(wCvd * cvdStrength + wAgg * aggBull + wEff * eff + wVol * vol + wPersist * persistScore * slopeScore);
                }

                // of bears: inverse cvdStrength, agg<0.5, eff, vol, slope negative
                decimal ofBear = 0.0m;
                {
                    decimal wCvd = 0.35m;
                    decimal wAgg = 0.20m;
                    decimal wEff = 0.20m;
                    decimal wVol = 0.10m;
                    decimal wPersist = 0.15m;
                    decimal persistScore = Clamp01((decimal)currentOfFeatures.PersistBear / 5m);
                    decimal slopeScore = currentOfFeatures.SlopeCvd < 0m ? 1m : 0m;
                    decimal cvdBear = 1m - cvdStrength;
                    decimal aggBear = Clamp01((0.5m - agg) * 2m + 0.5m);
                    ofBear = Clamp01(wCvd * cvdBear + wAgg * aggBear + wEff * eff + wVol * vol + wPersist * persistScore * slopeScore);
                }

                // of sideways: low efficiency, agg near center, low cvd abs, tradeRate moderate-high or counters present
                decimal ofSide = 0.0m;
                {
                    decimal wEff = 0.4m;
                    decimal wAggCenter = 0.25m;
                    decimal wCvdLow = 0.15m;
                    decimal wTrade = 0.10m;
                    decimal wCounters = 0.10m;
                    decimal effLow = 1m - eff;
                    decimal aggCenter = 1m - 2m * Math.Abs(agg - 0.5m);
                    aggCenter = Clamp01(aggCenter);
                    decimal cvdLow = 1m - Math.Abs(currentOfFeatures.CvdImpulse) / Math.Max(1m, Math.Abs(localThSide.ThCvdImpulseLong ?? DefaultCvdImpulse) / 4m + 0.0001m);
                    cvdLow = Clamp01(cvdLow);
                    decimal tradeScore = tradeRate;
                    decimal counterScore = (currentOfFeatures.MaxCounterShareBull > (localThSide.MaxCounterDeltaShareBull ?? 0m) && currentOfFeatures.MaxCounterShareBear > (localThSide.MaxCounterDeltaShareBear ?? 0m)) ? 1m : 0m;
                    ofSide = Clamp01(wEff * effLow + wAggCenter * aggCenter + wCvdLow * cvdLow + wTrade * tradeScore + wCounters * counterScore);
                }

                // of choppy: high flip rate, high revshare, low eff, high tradeRate
                decimal ofChoppy = 0.0m;
                {
                    decimal wFlip = 0.4m;
                    decimal wRev = 0.25m;
                    decimal wEffLow = 0.2m;
                    decimal wTrade = 0.15m;
                    decimal flip = flipRateNorm;
                    decimal rev = revShareNorm;
                    ofChoppy = Clamp01(wFlip * flip + wRev * rev + wEffLow * (1m - eff) + wTrade * tradeRate);
                }

                // rsScores:
                decimal rsBull = Clamp01(0.6m * runTrendUpNorm_local + 0.4m * rsEff); // prefer runTrendUp for bullish
                decimal rsBear = Clamp01(0.6m * runTrendDownNorm_local + 0.4m * rsEff); // prefer runTrendDown for bearish
                decimal rsSide = Clamp01(0.6m * revShareNorm + 0.4m * (1m - rsEff));
                decimal rsChoppy = Clamp01(0.6m * flipRateNorm + 0.4m * revShareNorm);

                // final mixed scores
                decimal scoreBull = Clamp01(ofWeight * ofBull + rsWeight * rsBull);
                decimal scoreBear = Clamp01(ofWeight * ofBear + rsWeight * rsBear);
                decimal scoreSide = Clamp01(ofWeight * ofSide + rsWeight * rsSide);
                decimal scoreChop = Clamp01(ofWeight * ofChoppy + rsWeight * rsChoppy);

                // Log subscores
                LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] Scores (OF/RS weights: {ofWeight:F2}/{rsWeight:F2}): ofBull={ofBull:F3}, rsBull={rsBull:F3}, bull={scoreBull:F3}; ofBear={ofBear:F3}, rsBear={rsBear:F3}, bear={scoreBear:F3}; ofSide={ofSide:F3}, rsSide={rsSide:F3}, side={scoreSide:F3}; ofChop={ofChoppy:F3}, rsChop={rsChoppy:F3}, chop={scoreChop:F3}");
                LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] RangeFeatures(window={rsFeat.Window}): run={rsFeat.RunTrend}, runUp={runTrendUpNorm_local:F3}, runDown={runTrendDownNorm_local:F3}, flip={rsFeat.FlipRate:F3}, rev={rsFeat.RevShare:F3}, eff={rsFeat.Efficiency:F3}");

                // Wenn genug Range-History, erhöhe Einfluss (rangePriority wurde gesetzt) - bereits berücksichtigt.
                // Entscheidung: wähle höchsten Score, aber mit relativer Dominanz-Regel wenn absolute Schwelle nicht erreicht
                decimal SCORE_THRESHOLD = rsFeat.Window >= _settings.RangeWindowN ? 0.6m : 0.5m;
                LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] Score verwendete Entscheidungsschwelle: {SCORE_THRESHOLD:F3} (RangeWindow={rsFeat.Window}, needed={_settings.RangeWindowN})");
                // Build ranking for dominance logic
                var scoresList = new[]
                {
                (bias: MarketDirectionalBias.BullishTrend, score: scoreBull),
                (bias: MarketDirectionalBias.BearishTrend, score: scoreBear),
                (bias: MarketDirectionalBias.Choppy, score: scoreChop),
                (bias: MarketDirectionalBias.Sideways, score: scoreSide)
            }.OrderByDescending(x => x.score).ToArray();

                var top = scoresList[0];
                var runnerUp = scoresList[1];

                MarketDirectionalBias candidateBias = MarketDirectionalBias.Undefined;

                // 1) strong absolute threshold
                if (top.score >= SCORE_THRESHOLD && top.score == scoresList.Max(s => s.score))
                {
                    candidateBias = top.bias;
                }
                else
                {
                    // 2) relative dominance rule: allow candidate if top - runnerUp >= DOMINANCE_DELTA and top.score > MIN_DOMINANT_SCORE
                    if (top.score - runnerUp.score >= DOMINANCE_DELTA && top.score >= MIN_DOMINANT_SCORE)
                    {
                        candidateBias = top.bias;
                        LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] Candidate selected by dominance rule: top={top.bias}, topScore={top.score:F3}, runnerUp={runnerUp.bias}, runnerUpScore={runnerUp.score:F3}, delta={top.score - runnerUp.score:F3}");
                    }
                    else
                    {
                        // explicit reason for undefined candidate
                        LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] Candidate undefined: top={top.bias}({top.score:F3}), runnerUp={runnerUp.bias}({runnerUp.score:F3}), delta={top.score - runnerUp.score:F3}, threshold={SCORE_THRESHOLD:F3}");
                        candidateBias = MarketDirectionalBias.Undefined;
                    }
                }

                LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] CandidateBias derived: {candidateBias}, scores: bull={scoreBull:F3}, bear={scoreBear:F3}, chop={scoreChop:F3}, side={scoreSide:F3}");

                // --- CONFIRMATION / HYSTERESE (M Bars) statt direkter Setzung ---
                if (candidateBias == _currentBias)
                {
                    // Kein Wechsel notwendig -> reset pending and undefined counters
                    _pendingBias = MarketDirectionalBias.Undefined;
                    _confirmCount = 0;
                    _undefinedPersistCount = 0;
                }
                else
                {
                    // If candidate is Undefined, don't set pending to Undefined; instead track undefined persistence & possibly decay confidence
                    if (candidateBias == MarketDirectionalBias.Undefined)
                    {
                        _undefinedPersistCount++;
                        LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] Candidate is Undefined; undefinedPersistCount={_undefinedPersistCount} at bar {currentOfFeatures.Bar}");

                        // If opposite direction shows dominance vs currentBias, decay confidence to allow faster flips later
                        bool oppositeDominantAgainstCurrent = false;
                        if (_currentBias == MarketDirectionalBias.BullishTrend && scoreBear - scoreBull >= DOMINANCE_DELTA && scoreBear > MIN_DOMINANT_SCORE)
                            oppositeDominantAgainstCurrent = true;
                        if (_currentBias == MarketDirectionalBias.BearishTrend && scoreBull - scoreBear >= DOMINANCE_DELTA && scoreBull > MIN_DOMINANT_SCORE)
                            oppositeDominantAgainstCurrent = true;

                        if (oppositeDominantAgainstCurrent && _undefinedPersistCount >= 1)
                        {
                            // Apply confidence decay to lower resistance to flip
                            // We don't store a persistent confidence here; we affect newState.Confidence later. Log for debug.
                            LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] Opposite dominance detected while candidate undefined; will apply confidence decay. currentBias={_currentBias}, bar={currentOfFeatures.Bar}");
                            // Note: we'll later reduce newState.Confidence after computing it below.
                        }

                        // Do not set _pendingBias to Undefined; keep previous pending if any.
                    }
                    else
                    {
                        // There is a concrete candidate (Bull/Bear/Chop/Side)
                        _undefinedPersistCount = 0;

                        if (_pendingBias != candidateBias)
                        {
                            // neuer Kandidat
                            _pendingBias = candidateBias;
                            _confirmCount = 1;

                            LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] Pending bias candidate={candidateBias} set (count=1) at bar {currentOfFeatures.Bar}");
                        }
                        else
                        {
                            // Fortführung der Bestätigung
                            _confirmCount++;
                            // Determine required confirmations based on whether this is opposite vs same
                            int required = candidateBias == _currentBias ? _settings.ConfirmBarsSame : _settings.ConfirmBarsOpposite;
                            LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] Pending bias candidate={candidateBias} confirmed count={_confirmCount} / {required} at bar {currentOfFeatures.Bar}");
                            if (_confirmCount >= required && candidateBias != MarketDirectionalBias.Undefined)
                            {
                                var previous = _currentBias;
                                _currentBias = _pendingBias;
                                _pendingBias = MarketDirectionalBias.Undefined;
                                _confirmCount = 0;
                                LoggerHelper.LogInfo(_loggerSource, $"[MarketStateEngine] MarketStateEngine: Bias committed from {previous} to {_currentBias} at bar {currentOfFeatures.Bar} after {required} confirmations.");
                            }
                        }
                    }
                }
            }

            // ---------- Endgültige Kennzahlen und Zuverlässigkeit erstellen ----------
            newState.DirectionalBias = _currentBias;

            int possible = 0;
            int satisfied = 0;

            if (newState.DirectionalBias == MarketDirectionalBias.BullishTrend)
            {
                var th = thBullBase;
                decimal thCvd = th.ThCvdImpulseLong ?? DefaultCvdImpulse;
                decimal thAgg = th.ThAggPressureBreakoutBear ?? DefaultAggPressure;
                decimal thVol = th.ThVolBurstZ ?? DefaultVolBurstZ;
                decimal thEff = th.ThEfficiency ?? DefaultEfficiency;

                possible = 6;
                satisfied += currentOfFeatures.CvdImpulse > thCvd ? 1 : 0;
                satisfied += currentOfFeatures.AggPressure > thAgg ? 1 : 0;
                satisfied += currentOfFeatures.PersistBull >= (3 + persistOffsetLocal) ? 1 : 0;
                satisfied += currentOfFeatures.SlopeCvd > 0m ? 1 : 0;
                satisfied += currentOfFeatures.Efficiency > thEff ? 1 : 0;
                satisfied += currentOfFeatures.VolBurstZ > thVol ? 1 : 0;
            }
            else if (newState.DirectionalBias == MarketDirectionalBias.BearishTrend)
            {
                var th = thBearBase;
                decimal thCvd = th.ThCvdImpulseShort ?? DefaultCvdImpulse;
                decimal thAgg = th.ThAggPressureBreakoutBear ?? DefaultAggPressure;
                decimal thVol = th.ThVolBurstZ ?? DefaultVolBurstZ;
                decimal thEff = th.ThEfficiency ?? DefaultEfficiency;

                possible = 6;
                satisfied += currentOfFeatures.CvdImpulse < thCvd ? 1 : 0;
                satisfied += currentOfFeatures.AggPressure < thAgg ? 1 : 0;
                satisfied += currentOfFeatures.PersistBear >= (3 + persistOffsetLocal) ? 1 : 0;
                satisfied += currentOfFeatures.SlopeCvd < 0m ? 1 : 0;
                satisfied += currentOfFeatures.Efficiency > thEff ? 1 : 0;
                satisfied += currentOfFeatures.VolBurstZ > thVol ? 1 : 0;
            }
            else
            {
                var thSideLocal = thSideBase;
                decimal thSideEff = thSideLocal.ThEfficiency ?? DefaultEfficiency;
                decimal thSideCvdQuarter = Math.Abs(thSideLocal.ThCvdImpulseLong ?? DefaultCvdImpulse) / 4m;
                decimal thSideVol = thSideLocal.ThVolBurstZ ?? DefaultVolBurstZ;

                possible = 4;
                satisfied += currentOfFeatures.Efficiency < thSideEff ? 1 : 0;
                satisfied += Math.Abs(currentOfFeatures.CvdImpulse) < thSideCvdQuarter ? 1 : 0;
                satisfied += currentOfFeatures.VolBurstZ < thSideVol ? 1 : 0;
                satisfied += (currentOfFeatures.PersistBull < (2 + persistOffsetLocal) && currentOfFeatures.PersistBear < (2 + persistOffsetLocal)) ? 1 : 0;
            }

            decimal confidence = 0m;
            if (possible > 0) confidence = (decimal)satisfied / (decimal)possible;
            newState.Confidence = Clamp01(confidence);

            // Apply confidence decay if we had undefined candidate but opposite dominance earlier
            if (_undefinedPersistCount > 0)
            {
                // find scores to check opposite dominance; recompute quickly here to avoid passing values
                // NOTE: this is a light-weight heuristic: if the opposite direction displays dominance vs currentBias, we decay.
                // Recompute of/bear quick:
                decimal quickCvdCap = Math.Max(1m, Math.Abs(localThBull.ThCvdImpulseLong ?? DefaultCvdImpulse) * 2m);
                decimal quickCvdStrength = Clamp01((currentOfFeatures.CvdImpulse + quickCvdCap) / (2m * quickCvdCap));
                decimal quickAgg = Clamp01(currentOfFeatures.AggPressure);
                decimal quickEff = Clamp01(currentOfFeatures.Efficiency);
                decimal quickVol = Clamp01(currentOfFeatures.VolBurstZ / (DefaultVolBurstZ * 4m + 0.0001m));
                decimal quickTradeRate = Clamp01(Math.Abs(currentOfFeatures.TradeRateZ) / 5m);

                // quick ofBull / ofBear
                decimal quickOfBull = Clamp01(0.35m * quickCvdStrength + 0.20m * Clamp01((quickAgg - 0.5m) * 2m + 0.5m) + 0.20m * quickEff + 0.10m * quickVol + 0.15m * Clamp01((decimal)currentOfFeatures.PersistBull / 5m) * (currentOfFeatures.SlopeCvd > 0m ? 1m : 0m));
                decimal quickOfBear = Clamp01(0.35m * (1m - quickCvdStrength) + 0.20m * Clamp01((0.5m - quickAgg) * 2m + 0.5m) + 0.20m * quickEff + 0.10m * quickVol + 0.15m * Clamp01((decimal)currentOfFeatures.PersistBear / 5m) * (currentOfFeatures.SlopeCvd < 0m ? 1m : 0m));

                // simple dominance detection
                if ((_currentBias == MarketDirectionalBias.BullishTrend && quickOfBear - quickOfBull >= DOMINANCE_DELTA && quickOfBear >= MIN_DOMINANT_SCORE) ||
                    (_currentBias == MarketDirectionalBias.BearishTrend && quickOfBull - quickOfBear >= DOMINANCE_DELTA && quickOfBull >= MIN_DOMINANT_SCORE))
                {
                    // decay
                    decimal oldConf = newState.Confidence ?? 0m;
                    newState.Confidence = Clamp01(oldConf * 0.75m);
                    LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] Confidence decayed due to undefined candidate with opposite dominance. oldConf={oldConf:F3}, newConf={newState.Confidence:F3} at bar {currentOfFeatures.Bar}");
                }
            }

            // --- NEU: Anpassung der Confidence durch Squeeze (WEICH, nur Richtungsanteil) ---
            // Volatilität wird weiterhin extern / durch VolBurstDetektor ermittelt; Squeeze darf nur sehr moderate Richtungs‑Unterstützung geben.
            decimal squeezeWeight = 0.10m; // max. weich (10%)
            if (SqueezeUtils.IsSqueezeReady(currentOfFeatures.Squeeze))
            {
                var dirStrength = Math.Min(1m, Math.Abs(SqueezeUtils.GetDirScore(currentOfFeatures.Squeeze)));
                // Verwende nur Richtungsstärke aus Squeeze, nicht VolScore (Vol wird anderswo gemessen)
                var squeezeComposite = dirStrength;
                var mixed = newState.Confidence * (1m - squeezeWeight) + squeezeComposite * squeezeWeight;
                newState.Confidence = Clamp01(mixed ?? 0m);
                LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] Squeeze soft blending applied (dir only): dirStrength={dirStrength:F3}, oldConf={confidence:F3}, newConf={newState.Confidence:F3}");
            }
            else
            {
                LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] Squeeze not ready (null/unknown/warmup). Skipping squeeze blending for bar={currentOfFeatures.Bar}");
            }

            // --- NEU: RangeScore Mixing in Confidence ---
            // bestimme Kontext (pendingBias bevorzugt, sonst current)
            var biasContext = _pendingBias != MarketDirectionalBias.Undefined ? _pendingBias : _currentBias;
            decimal rangeScore = 0m;
            if (biasContext == MarketDirectionalBias.BullishTrend || biasContext == MarketDirectionalBias.BearishTrend)
            {
                decimal runNorm = 0m;
                if (biasContext == MarketDirectionalBias.BullishTrend) runNorm = runTrendUpNorm;
                else runNorm = runTrendDownNorm;
                rangeScore = Clamp01(runNorm * 0.7m + rsFeat.Efficiency * 0.3m);
            }
            else if (biasContext == MarketDirectionalBias.Choppy)
            {
                rangeScore = Clamp01(rsFeat.FlipRate * 0.6m + rsFeat.RevShare * 0.4m);
            }
            else if (biasContext == MarketDirectionalBias.Sideways)
            {
                rangeScore = Clamp01(rsFeat.RevShare * 0.6m + (1m - rsFeat.Efficiency) * 0.4m);
            }
            else
            {
                rangeScore = 0m;
            }

            decimal rangeWeight = 0.25m; // Anteil der Range-Struktur an der finalen Confidence
            decimal oldConf2 = newState.Confidence ?? 0m;
            decimal blended = Clamp01(oldConf2 * (1m - rangeWeight) + rangeScore * rangeWeight);
            newState.Confidence = blended;
            LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] RangeScore blended: rangeScore={rangeScore:F3}, oldConfidence={oldConf2:F3}, newConfidence={blended:F3}");

            LoggerHelper.LogInfo(_loggerSource, $"[MarketStateEngine] MarketStateEngine: Endzustand bei bar={currentOfFeatures.Bar}: Bias={newState.DirectionalBias}, Confidence={newState.Confidence:F3}, possible={possible}, satisfied={satisfied}");

            newState.IsConsolidating = newState.DirectionalBias == MarketDirectionalBias.Sideways || newState.DirectionalBias == MarketDirectionalBias.Choppy;
            var biasThresh = GetThresholdsForBias(snapshot, newState.DirectionalBias);
            bool volBurstSignificant = currentOfFeatures.VolBurstZ > (biasThresh.ThVolBurstZ ?? DefaultVolBurstZ);
            bool cvdDirectionMatches = (newState.DirectionalBias == MarketDirectionalBias.BullishTrend && currentOfFeatures.CvdImpulse > 0)
                                       || (newState.DirectionalBias == MarketDirectionalBias.BearishTrend && currentOfFeatures.CvdImpulse < 0);
            newState.IsBreakingOut = volBurstSignificant && cvdDirectionMatches;

            // Entfernte Squeeze-basierte IsBreakingOut-Override (Squeeze darf kein hartes Gate mehr sein).

            if (newState.IsBreakingOut)
            {
                LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] MarketStateEngine: Breaking out detected at bar {currentOfFeatures.Bar}. Bias={newState.DirectionalBias}, VolBurstZ={currentOfFeatures.VolBurstZ:F6}, CvdImpulse={currentOfFeatures.CvdImpulse:F6}");
            }

            _currentMarketState = newState;
            MarketStateUpdated?.Invoke(_currentMarketState);

            var finalSig = SmartLogger.ComposeSignature(("bar", currentOfFeatures.Bar.ToString()), ("bias", newState.DirectionalBias.ToString()), ("conf", (newState.Confidence ?? 0m).ToString("F2")), ("hv", snapshot.HistoryVersion.ToString() ?? "-1"));
            SmartLogger.Instance.LogIfChanged(
                category: "MarketState",
                sourceId: "MarketStateEngine",
                barIndex: currentOfFeatures.Bar,
                message: $"MarketStateEngine: MarketStateUpdated aufgerufen für bar={currentOfFeatures.Bar}",
                signature: finalSig,
                backendLogAction: s => LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] {s}")
            );

            if (previousBias != _currentBias)
            {
                var changeSig = SmartLogger.ComposeSignature(("bar", currentOfFeatures.Bar.ToString()), ("from", previousBias.ToString()), ("to", _currentBias.ToString()), ("conf", (newState.Confidence ?? 0m).ToString("F2")));
                SmartLogger.Instance.LogIfChanged(
                    category: "MarketState",
                    sourceId: "MarketStateEngine",
                    barIndex: currentOfFeatures.Bar,
                    message: $"MarketStateEngine: Bias geändert von {previousBias} to {_currentBias} bei bar {currentOfFeatures.Bar}. Confidence={newState.Confidence:F2}",
                    signature: changeSig,
                    backendLogAction: s => LoggerHelper.LogInfo(_loggerSource, $"[MarketStateEngine] {s}")
                );
            }
            else
            {
                var remainSig = SmartLogger.ComposeSignature(("bar", currentOfFeatures.Bar.ToString()), ("bias", _currentBias.ToString()), ("conf", (newState.Confidence ?? 0m).ToString("F2")));
                SmartLogger.Instance.LogIfChanged(
                    category: "MarketState",
                    sourceId: "MarketStateEngine",
                    barIndex: currentOfFeatures.Bar,
                    message: $"MarketStateEngine: Bias remains {_currentBias} at bar {currentOfFeatures.Bar}. Confidence={newState.Confidence:F2}",
                    signature: remainSig,
                    backendLogAction: s => LoggerHelper.LogDebug(_loggerSource, $"[MarketStateEngine] {s}")
                );
            }

            return newState;
        }
    }
}
