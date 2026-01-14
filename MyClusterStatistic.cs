using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using ATAS.Indicators;
using ATAS.Indicators.Technical;
using System.Drawing;
// OFT.Attributes wird nicht mehr benötigt, da [Parameter] entfernt wurde.
// using OFT.Attributes; 

// Utils.Common.Logging wird beibehalten, falls Sie Log-Meldungen verwenden möchten.
using Utils.Common.Logging;
using System.Windows.Media;



namespace MyNamespace.Strategies
{
    public class MyClusterStatistic : Indicator
    {
        #region Nested types

        public enum DataType
        {
            Ask,
            Bid,
            Delta,
            DeltaVolume,
            SessionDelta,
            SessionDeltaVolume,
            MaxDelta,
            MinDelta,
            DeltaChange,
            Volume,
            VolumeSecond,
            SessionVolume,
            Trades,
            Height,
            Time,
            Duration,
            None
        }

        private struct MaxValues
        {
            public decimal MaxAsk { get; set; }
            public decimal MaxBid { get; set; }
            public decimal MaxSessionDelta { get; set; }
            public decimal MaxDeltaPerVolume { get; set; }
            public decimal MaxSessionDeltaPerVolume { get; set; }
            public decimal MaxDelta { get; set; }
            public decimal MinDelta { get; set; }
            public decimal MaxMaxDelta { get; set; }
            public decimal MaxMinDelta { get; set; }
            public decimal MaxVolume { get; set; }
            public decimal MaxTicks { get; set; }
            public decimal MaxDuration { get; set; }
            public decimal CumVolume { get; set; }
            public decimal MaxDeltaChange { get; set; }
            public decimal MaxHeight { get; set; }
            public decimal MaxVolumeSec { get; set; }
        }

        #endregion

        #region Fields

        // KORREKTUR: Die [Parameter]-Attribute wurden entfernt.
        // Die DataSeries sind als öffentliche Eigenschaften definiert und werden im Konstruktor hinzugefügt,
        // was ausreichen sollte, damit ATAS sie als Ausgabedatenreihen erkennt.
        public ValueDataSeries CandleDurations { get; set; } = new("durations");

        public ValueDataSeries VolPerSecond { get; set; } = new("VolPerSecond");

        public ValueDataSeries CandleHeights { get; set; } = new("heights");

        public ValueDataSeries CumulativeDelta { get; set; } = new("cDelta");

        public ValueDataSeries CumulativeDeltaPerVolume { get; set; } = new("DeltaPerVol");

        public ValueDataSeries CumulativeVolume { get; set; } = new("cVolume");

        public ValueDataSeries BarDeltaPerVolume { get; set; } = new("BarDeltaPerVol");

        // ADDED: Konfiguration für zeitkonstanten EMA
        [Browsable(true)]
        [DisplayName("VPS EMA t (min)")]
        [Description("Zeitkonstante in Minuten für den zeitkonstanten EMA über VolPerSecond.")]
        public int VpsEmaTauMinutes { get; set; } = 10;

        // ADDED: Ergebnis-Serien für EMA (und optional Std-Abweichung)
        [Browsable(false)]
        public ValueDataSeries EmaVolPerSecond { get; set; } = new("EmaVolPerSecond");

        [Browsable(false)]
        public ValueDataSeries EmaVolPerSecondStd { get; set; } = new("EmaVolPerSecondStd");

        // Interne Felder für die Berechnung (bleiben unverändert)
        private decimal _cumVolume;
        private int _lastBar = -1;
        private decimal _maxAsk;
        private decimal _maxBid;
        private decimal _maxDelta;
        private decimal _maxDeltaChange;
        private decimal _maxDeltaPerVolume;
        private decimal _maxDuration;
        private decimal _maxHeight;
        private decimal _maxMaxDelta;
        private decimal _maxMinDelta;
        private decimal _maxSessionDelta;
        private decimal _maxSessionDeltaPerVolume;
        private decimal _maxTicks;
        private decimal _maxVolume;
        private decimal _minDelta;
        // ADDED: EMA-State
        private decimal _emaVps = -1m;
        private decimal _ewmaVar = 0m;

        #endregion

        #region ctor

        public MyClusterStatistic()
            : base(true)
        {
            DataSeries[0].IsHidden = true;
            ((ValueDataSeries)DataSeries[0]).VisualType = VisualMode.Hide;

            DataSeries.Add(CandleDurations);
            DataSeries.Add(VolPerSecond);
            DataSeries.Add(CandleHeights);
            DataSeries.Add(CumulativeDelta);
            DataSeries.Add(CumulativeDeltaPerVolume);
            DataSeries.Add(CumulativeVolume);
            DataSeries.Add(BarDeltaPerVolume);
            // ADDED: neue Serien registrieren
            DataSeries.Add(EmaVolPerSecond);
            DataSeries.Add(EmaVolPerSecondStd);

        }

        #endregion

        #region Protected methods

        protected override void OnCalculate(int bar, decimal value)
        {
            if (bar < 0) return;

            var candle = GetCandle(bar);
            var candleSeconds = Convert.ToDecimal((candle.LastTime - candle.Time).TotalSeconds);

            if(candleSeconds <= 0)
    {
                //this.LogWarn($"[MyClusterStatistic {this.GetHashCode()}] Invalid or non-positive candle duration for bar {bar}. Defaulting to 1 second. Candle time: {candle.Time}, LastTime: {candle.LastTime}, VolPerSecond HashCode (in indicator): {VolPerSecond.GetHashCode()}");
                candleSeconds = 1m;
            }

            VolPerSecond[bar] = candle.Volume / candleSeconds;

            if (bar == 0)
            {
                _cumVolume = 0;
                _maxVolume = 0;
                _maxDelta = 0;
                _maxMaxDelta = 0;
                _maxMinDelta = 0;
                _maxDeltaChange = 0;
                _minDelta = decimal.MaxValue;
                _maxHeight = 0;
                _maxTicks = 0;
                _maxDuration = 0;
                _maxSessionDelta = 0;
                _maxDeltaPerVolume = 0;
                _maxSessionDeltaPerVolume = 0;
                _maxBid = _maxAsk = 0;
                CumulativeDelta[bar] = candle.Delta;
                CumulativeVolume[bar] = candle.Volume;

                CandleHeights[bar] = candle.High - candle.Low;
                CandleDurations[bar] = (int)Math.Max(1, (candle.LastTime - candle.Time).TotalSeconds);
                BarDeltaPerVolume[bar] = candle.Volume == 0 ? 0m : Math.Abs(candle.Delta * 100m / candle.Volume);

                // ADDED: EMA-Startzustand für den ersten Bar
                _emaVps = VolPerSecond[bar];
                _ewmaVar = 0m;

                // Synchronisiere die versteckte Standardserie, falls externe Logik DataSeries[0] prüft
                DataSeries[0][bar] = 0m;
            }
            else
            {
                BarDeltaPerVolume[bar] = candle.Volume is 0
                    ? 0
                    : Math.Abs(candle.Delta * 100m / candle.Volume);

                if (IsNewSession(bar))
                {
                    _cumVolume = CumulativeVolume[bar] = candle.Volume;
                    CumulativeDelta[bar] = candle.Delta;

                    // ADDED: Session-Reset des EMA bei neuem Handelstag/Session
                    _emaVps = VolPerSecond[bar];
                    _ewmaVar = 0m;
                }
                else
                {
                    _cumVolume = CumulativeVolume[bar] = CumulativeVolume[bar - 1] + candle.Volume;
                    CumulativeDelta[bar] = CumulativeDelta[bar - 1] + candle.Delta;
                }

                _maxSessionDelta = Math.Max(Math.Abs(CumulativeDelta[bar]), _maxSessionDelta);
                _maxAsk = Math.Max(candle.Ask, _maxAsk);
                _maxBid = Math.Max(candle.Bid, _maxBid);

                var prevCandle = GetCandle(bar - 1);
                _maxDeltaChange = Math.Max(Math.Abs(candle.Delta - prevCandle.Delta), _maxDeltaChange);
                _maxDelta = Math.Max(Math.Abs(candle.Delta), _maxDelta);
                _maxMaxDelta = Math.Max(Math.Abs(candle.MaxDelta), _maxMaxDelta);
                _maxMinDelta = Math.Max(Math.Abs(candle.MinDelta), _maxMinDelta);
                _maxVolume = Math.Max(candle.Volume, _maxVolume);
                _minDelta = Math.Min(candle.MinDelta, _minDelta);

                _maxDeltaPerVolume = candle.Volume is 0
                    ? 0
                    : Math.Max(Math.Abs(100 * candle.Delta / candle.Volume), _maxDeltaPerVolume);

                var candleHeight = candle.High - candle.Low;
                _maxHeight = Math.Max(candleHeight, _maxHeight);
                CandleHeights[bar] = candleHeight;

                _maxTicks = Math.Max(candle.Ticks, _maxTicks);

                CandleDurations[bar] = (int)(candle.LastTime - candle.Time).TotalSeconds;
                _maxDuration = Math.Max(CandleDurations[bar], _maxDuration);
            }

            // ADDED: Zeitkonstanter EMA über VolPerSecond
            // alpha = 1 - exp(-?t/t), ?t = candleSeconds, t = VpsEmaTauMinutes * 60
            var tauSec = Math.Max(1m, (decimal)VpsEmaTauMinutes * 60m);
            var alpha = 1m - (decimal)Math.Exp(-(double)(candleSeconds / tauSec));
            if (alpha < 0.0001m) alpha = 0.0001m;

            var vps = VolPerSecond[bar];
            if (_emaVps < 0)
            {
                _emaVps = vps;
                _ewmaVar = 0m;
            }
            else
            {
                var diff = vps - _emaVps;
                _emaVps += alpha * diff;
                _ewmaVar = (1 - alpha) * (_ewmaVar + alpha * diff * diff);
                if (_ewmaVar < 0) _ewmaVar = 0m;
            }

            EmaVolPerSecond[bar] = _emaVps;
            EmaVolPerSecondStd[bar] = (decimal)Math.Sqrt((double)_ewmaVar);

            _lastBar = bar;

            // Am Ende: Stelle sicher, dass die versteckte Standardserie ebenfalls den Index für diesen Bar hat.
            // Das verhindert Inkonsistenzen beim Abfragen von DataSeries[0].Count außerhalb dieses Indikators.
            // Wenn DataSeries[0] bereits einen Wert für diesen bar hatte, überschreiben wir ihn mit 0m (harmless).
            DataSeries[0][bar] = 0m;

            // Optionales Debug-Log (auskommentiert)
            // if (bar % 500 == 0)
            //     this.LogInfo($"[MyClusterStatistic] bar={bar} VPS={VolPerSecond[bar]:F2} EMA={EmaVolPerSecond[bar]:F2} STD={EmaVolPerSecondStd[bar]:F2} " +
            // 
        }

        #endregion

        #region Private methods

        private MaxValues CreateMaxValues()
        {
            decimal maxVolumeSec = 0m;

            
            // Sicherstellen, dass CurrentBar - 1 >= 0 bevor auf MAX zugegriffen wird.
            if (CurrentBar > 0 && VolPerSecond != null)
            {
                try
                {
                    maxVolumeSec = VolPerSecond.MAX(CurrentBar - 1, CurrentBar - 1);
                }
                catch
                {
                    // Fallback: wenn MAX aus irgendeinem Grund fehlschlägt, benutze letzten Wert falls vorhanden
                    if (VolPerSecond.Count > CurrentBar - 1)
                        maxVolumeSec = VolPerSecond[CurrentBar - 1];
                    else
                        maxVolumeSec = 0m;
                }
            }

            return new MaxValues
            {
                MaxAsk = _maxAsk,
                MaxBid = _maxBid,
                MaxSessionDelta = _maxSessionDelta,
                MaxDeltaPerVolume = _maxDeltaPerVolume,
                MaxSessionDeltaPerVolume = _maxSessionDeltaPerVolume,
                MaxDelta = _maxDelta,
                MinDelta = _minDelta,
                MaxMaxDelta = _maxMaxDelta,
                MaxMinDelta = _maxMinDelta,
                MaxVolume = _maxVolume,
                MaxTicks = _maxTicks,
                MaxDuration = _maxDuration,
                CumVolume = _cumVolume,
                MaxDeltaChange = _maxDeltaChange,
                MaxHeight = _maxHeight,
                MaxVolumeSec = maxVolumeSec
            };
        }

        #endregion
    }
}


