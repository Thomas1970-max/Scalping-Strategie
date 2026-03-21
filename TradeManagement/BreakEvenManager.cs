using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.DataFeedsCore;
using Utils.Common.Logging;
using static MyNamespace.Strategies.Geldfluss3_3; // Damit OrderDirections gefunden werden

namespace MyNamespace.Strategies.TradeManagement
{
    public class BreakEvenManager
    {
        private readonly List<BreakEvenStage> _stages;
        private int _lastActivatedIndex = -1;
        private readonly decimal _entryPrice;
        private readonly OrderDirections _direction;
        private readonly decimal _tickSizePriceUnit;

        // Internes Tracking des Best-Preises (Extrem seit Entry) für intrabar-Profit
        private decimal _internalBestPrice;

        public BreakEvenManager(decimal entryPrice, OrderDirections direction, decimal tickSize, IEnumerable<BreakEvenStage> stages, decimal initialBestPrice = 0m)
        {
            _entryPrice = entryPrice;
            _direction = direction;
            _tickSizePriceUnit = tickSize > 0m ? tickSize : 0.01m; // Fallback zur Sicherheit
            _stages = stages?.OrderBy(s => s.TriggerTicks).ToList() ?? new List<BreakEvenStage>();

            // Initialisiere internes Best
            _internalBestPrice = initialBestPrice > 0m ? initialBestPrice : entryPrice;
        }

        // currentPrice = letzter Tickpreis
        // currentSl     = aktueller SL (Trigger oder Preis)
        // bestPrice     = externer Best-Wert (optional; 0 => ignorieren)
        public decimal? OnPriceUpdate(decimal currentPrice, decimal currentSl, decimal bestPrice = 0m)
        {
            if (currentPrice <= 0m)
                return null;

            // 1) Internes Extrem fortschreiben – FIX: Wenn bestPrice >0, priorisiere es (globales Best), sonst nur current
            if (_direction == OrderDirections.Buy)
            {
                if (bestPrice > 0m)
                {
                    _internalBestPrice = Math.Max(_internalBestPrice, Math.Max(currentPrice, bestPrice));
                    this.LogDebug($"[BE-Manager] Long: Internal Best updated to {_internalBestPrice} (curr={currentPrice}, bestPassed={bestPrice})");  // NEU: Optional Debug
                }
                else
                    _internalBestPrice = Math.Max(_internalBestPrice, currentPrice);
            }
            else  // Sell/Short
            {
                if (bestPrice > 0m)
                {
                    _internalBestPrice = Math.Min(_internalBestPrice, Math.Min(currentPrice, bestPrice));
                    this.LogDebug($"[BE-Manager] Short: Internal Best updated to {_internalBestPrice} (curr={currentPrice}, bestPassed={bestPrice})");  // NEU
                }
                else
                    _internalBestPrice = Math.Min(_internalBestPrice, currentPrice);
            }

            // 2) Bewegte Distanz – NEU: Nutze immer _internalBestPrice (jetzt sync't mit global, wenn bestPrice>0)
            decimal moved = (_direction == OrderDirections.Buy)
                ? (_internalBestPrice - _entryPrice)
                : (_entryPrice - _internalBestPrice);

            this.LogDebug($"[BE-Manager] Moved calc: {_internalBestPrice} vs Entry {_entryPrice} = {moved:F2}");  // NEU: Für Threshold-Debug

            // 3) Nächste Stufe prüfen (unverändert, aber mit besserem moved)
            for (int i = _lastActivatedIndex + 1; i < _stages.Count; i++)
            {
                var s = _stages[i];
                var triggerDistance = s.TriggerTicks * _tickSizePriceUnit;
                if (triggerDistance <= 0m)
                    continue;

                if (moved >= triggerDistance)
                {
                    var offsetDistance = s.OffsetTicks * _tickSizePriceUnit;
                    var newStop = (_direction == OrderDirections.Buy)
                        ? (_entryPrice + offsetDistance)
                        : (_entryPrice - offsetDistance);

                    bool shouldModify =
                        (_direction == OrderDirections.Buy && newStop > currentSl) ||
                        (_direction == OrderDirections.Sell && newStop < currentSl);

                    if (shouldModify)
                    {
                        _lastActivatedIndex = i;
                        this.LogInfo($"[BE-Manager] Stage {i + 1} triggered: Moved={moved:F2} >= Th={triggerDistance:F2}, NewStop={newStop}");  // NEU: Info für Trigger
                        return newStop;
                    }
                    else
                    {
                        this.LogDebug($"[BE-Manager] Stage {i + 1} reached but no improve: NewStop={newStop} vs CurrSL={currentSl}");
                        continue;
                    }
                }
            }

            return null;
        }


        public bool IsCompleted => _lastActivatedIndex >= (_stages.Count - 1);

        public decimal InternalBestPrice => _internalBestPrice;
    }
}
