using System;
using ATAS.DataFeedsCore;
using ATAS.Indicators; // Für Candle, IndicatorCandle

namespace MyNamespace.Strategies.TradeManagement
{
    // Nehmen Sie an, diese Enums und Klassen sind in TpSlTypes.cs oder einer ähnlichen Datei definiert
    // public enum TrailingType { None, FixedTicks, ATR, CandleLowHigh }
    // public class TrailingConfiguration { ... }


    public class TrailingStopManager
    {
        private OrderDirections _direction;
        private decimal _tickSize;
        private TrailingConfiguration _config; // NEU: Speichert die gesamte Trailing-Konfiguration

        // NEU: Konstruktor, der die TrailingConfiguration entgegennimmt
        public TrailingStopManager(OrderDirections dir, decimal tickSize, TrailingConfiguration config)
        {
            _direction = dir;
            _tickSize = tickSize;
            _config = config ?? throw new ArgumentNullException(nameof(config), "TrailingConfiguration cannot be null.");

            // Falls TrailType.None, ist kein Trailing aktiv
            if (_config.TrailType == TrailingType.None)
            {
                // Könnte auch bedeuten, dass der Manager gar nicht instanziiert wird,
                // aber zur Sicherheit kann man hier auch initialisieren, dass nichts passiert.
            }
        }

        // Initialize wird nicht mehr wirklich benötigt, da der aktuelle SL der SL der Order ist
        // public void Initialize(decimal initialStop) { /* ... */ }

        /// <summary>
        /// Berechnet einen potenziellen neuen Trailing-Stop-Preis basierend auf der aktuellen Marktsituation.
        /// </summary>
        /// <param name="currentBarIndex">Der aktuelle Bar-Index.</param>
        /// <param name="entryPrice">Der Einstiegspreis des Trades.</param>
        /// <param name="currentBestPrice">Der beste (höchste für Long, tiefste für Short) Preis seit dem Einstieg.</param>
        /// <param name="currentStopLossPrice">Der aktuell aktive Stop-Loss-Preis.</param>
        /// <param name="candle">Die aktuelle Kerze (ATAS.Indicators.Candle).</param>
        /// <returns>Der neue Trailing-Stop-Preis, falls eine Anpassung notwendig ist; sonst null.</returns>
        public decimal? ProcessTrailing(
            int currentBarIndex,
            decimal entryPrice,
            decimal currentBestPrice,
            decimal currentStopLossPrice,
            ATAS.Indicators.Candle candle)
        {
            if (_config.TrailType == TrailingType.None) return null; // Kein Trailing konfiguriert

            // Aktivierungsprüfung
            if (_config.TrailActivateAfterTicks > 0)
            {
                decimal priceDistanceToActivate = _config.TrailActivateAfterTicks * _tickSize;
                decimal movedProfit = (_direction == OrderDirections.Buy) ? (currentBestPrice - entryPrice) : (entryPrice - currentBestPrice);

                if (movedProfit < priceDistanceToActivate)
                {
                    return null; // Noch nicht aktiviert
                }
            }

            decimal potentialNewStopLoss = currentStopLossPrice; // Standardmäßig behalten wir den aktuellen SL
            bool shouldModify = false;

            switch (_config.TrailType)
            {
                case TrailingType.FixedTicks:
                    decimal fixedOffset = _config.TrailOffsetTicks * _tickSize;
                    if (_direction == OrderDirections.Buy)
                    {
                        potentialNewStopLoss = currentBestPrice - fixedOffset;
                        // Nur anpassen, wenn der neue SL besser ist (höher für Buy)
                        if (potentialNewStopLoss > currentStopLossPrice) shouldModify = true;
                    }
                    else // OrderDirections.Sell
                    {
                        potentialNewStopLoss = currentBestPrice + fixedOffset;
                        // Nur anpassen, wenn der neue SL besser ist (tiefer für Sell)
                        if (potentialNewStopLoss < currentStopLossPrice) shouldModify = true;
                    }
                    break;

                case TrailingType.CandleLowHigh:
                    if (candle == null) return null; // Benötigt Kerzendaten

                    if (_direction == OrderDirections.Buy)
                    {
                        // Bei Long: Traile mit dem Low der Kerze minus einen Tick, wenn die Kerze bullish ist
                        // Oder allgemein, wenn der Low einen besseren SL erlaubt
                        potentialNewStopLoss = candle.Low - _tickSize;
                        if (potentialNewStopLoss > currentStopLossPrice) shouldModify = true;
                    }
                    else // OrderDirections.Sell
                    {
                        // Bei Short: Traile mit dem High der Kerze plus einen Tick, wenn die Kerze bearish ist
                        // Oder allgemein, wenn der High einen besseren SL erlaubt
                        potentialNewStopLoss = candle.High + _tickSize;
                        if (potentialNewStopLoss < currentStopLossPrice) shouldModify = true;
                    }
                    break;

                // case TrailingType.ATR:
                //    // Hier würden Sie die ATR-Logik implementieren
                //    // Sie benötigen den aktuellen ATR-Wert, der übergeben werden müsste
                //    // Beispiel: potentialNewStopLoss = (_direction == OrderDirections.Buy) ? currentBestPrice - (atrValue * _config.AtrMultiplier) : currentBestPrice + (atrValue * _config.AtrMultiplier);
                //    // if ( (direction == Buy && potentialNewStopLoss > currentSl) || (direction == Sell && potentialNewStopLoss < currentSl) ) shouldModify = true;
                //    break;

                // ... weitere Trailing-Typen ...

                default:
                    // Unbekannter Trailing-Typ oder keiner definiert
                    return null;
            }

            // Sicherstellen, dass der SL nicht über den Entry-Preis zurückfällt,
            // wenn er einmal im Gewinnbereich war (optional, je nach Strategie)
            // if ( (direction == OrderDirections.Buy && potentialNewStopLoss < entryPrice && currentStopLossPrice >= entryPrice) ||
            //      (direction == OrderDirections.Sell && potentialNewStopLoss > entryPrice && currentStopLossPrice <= entryPrice) )
            // {
            //     potentialNewStopLoss = entryPrice; // Oder den ursprünglichen SL
            // }


            return shouldModify ? potentialNewStopLoss : null;
        }

        // Überladung für IndicatorCandle, falls benötigt
        public decimal? ProcessTrailing(
            int currentBarIndex,
            decimal entryPrice,
            decimal currentBestPrice,
            decimal currentStopLossPrice,
            ATAS.Indicators.IndicatorCandle indicatorCandle)
        {
            // Die Logik ist im Wesentlichen dieselbe wie für ATAS.Indicators.Candle,
            // nur dass Sie hier indicatorCandle.Open, .High, .Low, .Close verwenden würden.
            // Um Redundanz zu vermeiden, könnten Sie eine private Helfermethode erstellen,
            // die die Kerzendaten (Open, High, Low, Close) als generische Parameter akzeptiert.

            if (_config.TrailType == TrailingType.None) return null;

            // Aktivierungsprüfung
            if (_config.TrailActivateAfterTicks > 0)
            {
                decimal priceDistanceToActivate = _config.TrailActivateAfterTicks * _tickSize;
                decimal movedProfit = (_direction == OrderDirections.Buy) ? (currentBestPrice - entryPrice) : (entryPrice - currentBestPrice);

                if (movedProfit < priceDistanceToActivate)
                {
                    return null;
                }
            }

            decimal potentialNewStopLoss = currentStopLossPrice;
            bool shouldModify = false;

            switch (_config.TrailType)
            {
                case TrailingType.FixedTicks:
                    decimal fixedOffset = _config.TrailOffsetTicks * _tickSize;
                    if (_direction == OrderDirections.Buy)
                    {
                        potentialNewStopLoss = currentBestPrice - fixedOffset;
                        if (potentialNewStopLoss > currentStopLossPrice) shouldModify = true;
                    }
                    else // OrderDirections.Sell
                    {
                        potentialNewStopLoss = currentBestPrice + fixedOffset;
                        if (potentialNewStopLoss < currentStopLossPrice) shouldModify = true;
                    }
                    break;

                case TrailingType.CandleLowHigh:
                    if (indicatorCandle == null) return null;

                    if (_direction == OrderDirections.Buy)
                    {
                        potentialNewStopLoss = indicatorCandle.Low - _tickSize;
                        if (potentialNewStopLoss > currentStopLossPrice) shouldModify = true;
                    }
                    else // OrderDirections.Sell
                    {
                        potentialNewStopLoss = indicatorCandle.High + _tickSize;
                        if (potentialNewStopLoss < currentStopLossPrice) shouldModify = true;
                    }
                    break;

                default:
                    return null;
            }
            return shouldModify ? potentialNewStopLoss : null;
        }
    }
}

