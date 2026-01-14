using System;
using System.Linq;
// using ATAS.DataFeedsCore; // Nicht direkt in der Engine benötigt, aber für Modelle
using MyNamespace.Strategies.Models;
using static MyNamespace.Strategies.Goldfluss3_3;
using Utils.Common.Logging; // LoggerHelper.Verwendung

namespace MyNamespace.Strategies.Orderflow
{

    

    public class RegimeDetectorConfig
    {
        public decimal EwmaAlpha { get; set; } = 0.15m;
        public int RollingWindowSize { get; set; } = 20; // optional for further stats
        public decimal ZThreshold { get; set; } = 4.0m;
        public decimal RatioHigh { get; set; } = 3.0m;
        public decimal RatioLow { get; set; } = 0.33m;
        public decimal SlopeThreshold { get; set; } = 1.5m; // relative slope threshold
        public int CooldownBars { get; set; } = 6;
        public int RequireClearConsecutive { get; set; } = 4;
        public decimal WeightZ { get; set; } = 1.5m;
        public decimal WeightRatio { get; set; } = 1.0m;
        public decimal WeightSlope { get; set; } = 0.4m;
        public decimal EwmaMinimumDenominator { get; set; } = 0.001m;
    }


        public class RegimeEvent
    {
        public string MetricName { get; set; }
        public DateTime Timestamp { get; set; }
        public long BarIndex { get; set; }
        public string Reason { get; set; } = "";
        public decimal Score { get; set; } = 0m;
        public override string ToString() => $"RegimeEvent[{MetricName} @ {BarIndex} | {Reason} | score={Score:F3}]";
    }

    public class RegimeDetector
    {
        private readonly string _name;
        private readonly RegimeDetectorConfig _cfg;
        private decimal _ewma;
        private decimal _ewmaVar; // for approx variance via EWMA
        private bool _initialized;
        private int _cooldownRemaining;
        private int _clearCounter;
        private RegimeEvent _lastEvent;
        
        public RegimeDetector(string name, RegimeDetectorConfig cfg = null)
        {
            

            _name = name ?? "metric";
            _cfg = cfg ?? new RegimeDetectorConfig();
            _ewma = 0m;
            _ewmaVar = 0m;
            _initialized = false;
            _cooldownRemaining = 0;
            _clearCounter = 0;
            _lastEvent = null;
        }

        // Exposed state
        public bool IsInRegime => _cooldownRemaining > 0;
        public RegimeEvent ConsumeLastEvent()
        {
            var e = _lastEvent;
            _lastEvent = null;
            return e;
        }

        public void Reset()
        {
            _ewma = 0m;
            _ewmaVar = 0m;
            _initialized = false;
            _cooldownRemaining = 0;
            _clearCounter = 0;
            _lastEvent = null;
        }

        // Main update per bar
        public void Update(decimal value, long barIndex, DateTime ts)
        {
            // Angepasster Log-Aufruf: _name als SourceId, dann vollständig formatierter String
            //LoggerHelper.LogInfo(_name, $"[RegimeDetector] Update aufgerufen. Bar={barIndex}, Value={value:F3}, EWMA={_ewma:F3}, EWMAVar={_ewmaVar:F3}, Cooldown={_cooldownRemaining}");

            // Update EWMA & EW variance (simple exponential variance estimator)
            if (!_initialized)
            {
                _ewma = value;
                _ewmaVar = 0m;
                _initialized = true;
                // Angepasster Log-Aufruf
                //LoggerHelper.LogInfo(_name, $"[RegimeDetector] Initialisiert: EWMA={_ewma:F3}, EWMAVar={_ewmaVar:F3}");
            }
            else
            {
                var alpha = _cfg.EwmaAlpha;
                var delta = value - _ewma;
                _ewma += alpha * delta;
                // EW variance: var <- (1-alpha)*(var + alpha*delta^2)
                var deltaSq = delta * delta;
                _ewmaVar = (1m - alpha) * (_ewmaVar + alpha * deltaSq);
            }

            // approximate std dev (guarded)
            var std = (decimal)Math.Sqrt((double)Math.Max(0m, _ewmaVar));
            decimal zScore = std > 0m ? (value - _ewma) / std : 0m; // approximate z with ewma estimates

            // Sicherer Nenner für Ratio und SlopeRel, um Division durch (nahezu) Null zu vermeiden
            decimal safeEwmaAbs = Math.Max(_cfg.EwmaMinimumDenominator, Math.Abs(_ewma));

            // ratio to ewma (guard)
            decimal ratio = Math.Abs(value / safeEwmaAbs); // Verwendet den sicheren Nenner

            // slope proxy: relative delta normalized to ewma
            decimal slopeRel = Math.Abs((value - _ewma) / safeEwmaAbs); // Verwendet den sicheren Nenner

            // Angepasster Log-Aufruf
            //LoggerHelper.LogInfo(_name, $"[RegimeDetector] Bar={barIndex}: ZScore={zScore:F3} (Th={_cfg.ZThreshold:F3}), Ratio={ratio:F3} (ThH={_cfg.RatioHigh:F3}, ThL={_cfg.RatioLow:F3}), SlopeRel={slopeRel:F3} (Th={_cfg.SlopeThreshold:F3})");

            // compute heuristic scoring
            decimal scoreZ = Math.Max(0m, (Math.Abs(zScore) - _cfg.ZThreshold) / Math.Max(1m, _cfg.ZThreshold));
            decimal scoreRatio = 0m;
            if (ratio > _cfg.RatioHigh) scoreRatio = (ratio - _cfg.RatioHigh) / _cfg.RatioHigh;
            else if (ratio < _cfg.RatioLow) scoreRatio = (_cfg.RatioLow - ratio) / _cfg.RatioLow;

            decimal scoreSlope = Math.Max(0m, (slopeRel - _cfg.SlopeThreshold) / Math.Max(1m, _cfg.SlopeThreshold));

            decimal combined = _cfg.WeightZ * scoreZ + _cfg.WeightRatio * scoreRatio + _cfg.WeightSlope * scoreSlope;

            // Angepasster Log-Aufruf
            //LoggerHelper.LogInfo(_name, $"[RegimeDetector] Bar={barIndex}: ScoreZ={scoreZ:F3}, ScoreRatio={scoreRatio:F3}, ScoreSlope={scoreSlope:F3}, CombinedScore={combined:F3} (TriggerTh=0.600)");

            // threshold for 'enter' (tunable). Here: any strong z or combined > 0.6 triggers.
            bool strongZ = Math.Abs(zScore) >= _cfg.ZThreshold;
            bool combinedTrigger = combined >= 0.6m;

            // Cooldown / hysteresis handling
            if ((strongZ || combinedTrigger) && _cooldownRemaining == 0)
            {
                // Enter regime
                _cooldownRemaining = _cfg.CooldownBars;
                _clearCounter = 0;
                _lastEvent = new RegimeEvent
                {
                    MetricName = _name,
                    BarIndex = barIndex,
                    Timestamp = ts,
                    Reason = strongZ ? "ZThreshold" : "CombinedTrigger",
                    Score = combined
                };
                // Angepasster Log-Aufruf
                //LoggerHelper.LogInfo(_name, $"[RegimeDetector] *** SHOCK DETECTED *** Bar={barIndex}, Reason={_lastEvent.Reason}, Score={_lastEvent.Score:F3}, Cooldown für {_cfg.CooldownBars} Bars gestartet.");
            }
            else if (_cooldownRemaining > 0)
            {
                _cooldownRemaining--;
                // Angepasster Log-Aufruf
                //LoggerHelper.LogInfo(_name, $"[RegimeDetector] Cooldown aktiv. Bar={barIndex}, verbleibende Bars: {_cooldownRemaining}");
            }
            else
            {
                // not in regime: check clear counter to ensure stable clear
                if (Math.Abs(zScore) < (_cfg.ZThreshold * 0.8m) && ratio > _cfg.RatioLow && ratio < _cfg.RatioHigh)
                {
                    _clearCounter++;
                    // Angepasster Log-Aufruf
                    //LoggerHelper.LogInfo(_name, $"[RegimeDetector] ClearCounter inkrementiert. Bar={barIndex}, Counter={_clearCounter} (Required={_cfg.RequireClearConsecutive})");
                    if (_clearCounter >= _cfg.RequireClearConsecutive)
                    {
                        // cleared; nothing special to emit currently
                        _clearCounter = 0;
                        _lastEvent = null;
                        // Angepasster Log-Aufruf
                        //LoggerHelper.LogInfo(_name, $"[RegimeDetector] Regime cleared. Bar={barIndex}.");
                    }
                }
                else
                {
                    if (_clearCounter > 0)
                        // Angepasster Log-Aufruf
                        //LoggerHelper.LogInfo(_name, $"[RegimeDetector] ClearCounter reset. Bar={barIndex}.");
                    _clearCounter = 0;
                }
            }

            // Note: IsInRegime reads _cooldownRemaining > 0
        }
    }
}






