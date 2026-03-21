using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using Utils.Common.Logging;
using static MyNamespace.Strategies.Geldfluss3_3;

namespace MyNamespace.Strategies.Orderflow
{
    public class OfFeaturesHistory
    {
        private readonly List<OfFeatures> _featuresHistory;
        private readonly int _maxCapacity;
        private readonly ILoggerSource _loggerSource;
        // NEU: Konstruktor, der die Kapazität entgegennimmt
        public OfFeaturesHistory(int capacity = 256, ILoggerSource loggerSource = null)
        {
            _loggerSource = loggerSource;
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), "Die Kapazität muss positiv sein.");
            _maxCapacity = capacity;
            _featuresHistory = new List<OfFeatures>(capacity);
        }

        /// <summary>
        /// Fügt ein OfFeatures-Objekt zur Historie hinzu.
        /// </summary>
        /// <param name="features">Das zu speichernde OfFeatures-Objekt.</param>
        public void Add(OfFeatures features)
        {
            // Alte API bleibt erhalten, führt aber dieselbe Logik aus.
            AddAndReturnRemoved(features);
        }
        /// Fügt ein OfFeatures-Objekt zur Historie hinzu und gibt ggf. die Bar des entfernten Eintrags zurück (bei Kapazitätsüberschreitung).
        ///

        public int? AddAndReturnRemoved(OfFeatures features)
        {
            if (_loggerSource != null) LoggerHelper.LogDebug(_loggerSource, "[OfFeaturesHistory] AddAndReturnRemoved: adding...");

            if (features == null)
            {
                LoggerHelper.LogWarn(_loggerSource, "[OfFeaturesHistory] OfFeaturesHistory.AddAndReturnRemoved: Versuch, null OfFeatures hinzuzufügen — Ignoriere.");
                return null;
            }

            _featuresHistory.Add(features);
            LoggerHelper.LogDebug(_loggerSource, $"[OfFeaturesHistory] OfFeaturesHistory.AddAndReturnRemoved: Added bar={features.Bar}. Count after add={_featuresHistory.Count}");

            if (_featuresHistory.Count > _maxCapacity)
            {
                var removed = _featuresHistory[0];
                int? removedBar = removed?.Bar;
                _featuresHistory.RemoveAt(0);
                LoggerHelper.LogInfo(_loggerSource, $"[OfFeaturesHistory] OfFeaturesHistory: Kapazität überschritten. Älteste entfernt. bar={removedBar}. NewCount={_featuresHistory.Count}");
                return removedBar;
            }

            return null;
        }
        /// <summary>
        /// Ursprüngliche Methode: "Relative" Zugriff mit Offset relativ zum Ende.
        /// Semantik (bestehend): offset wird zu (Count - 1 + offset) gerechnet.
        /// Typischer Gebrauch in deinem Code war: offset = -1 => vorheriger, offset = 0 => aktuellster.
        /// Diese Methode bleibt erhalten, wird aber hier explizit dokumentiert.
        /// </summary>
        /// <param name="offset">Offset relativ zum Ende (z. B. -1 = vorheriger, 0 = aktuellster)</param>
        public bool TryGetRelative(int offset, out OfFeatures features)
        {
            features = default(OfFeatures);
            int index = (_featuresHistory.Count - 1) + offset; // offset ist in der Regel negativ (-1 für vorherigen)
            if (index >= 0 && index < _featuresHistory.Count)
            {
                features = _featuresHistory[index];
                return true;
            }
            return false;
        }

        /// <summary>
        /// Neu: Klarere, intuitive Methode zum Zugriff vom Ende:
        /// offsetFromEnd: 0 = aktuellster, 1 = vorletzter, 2 = vor-vorletzter, usw.
        /// Diese Variante ist leichter lesbar als die negative-offset-Konvention.
        /// </summary>
        public bool TryGetFromEnd(int offsetFromEnd, out OfFeatures features)
        {
            features = default(OfFeatures);
            if (offsetFromEnd < 0)
            {
                LoggerHelper.LogDebug(_loggerSource, $"[OfFeaturesHistory] TryGetFromEnd: invalid offsetFromEnd={offsetFromEnd}");
                return false;
            }
            int index = _featuresHistory.Count - 1 - offsetFromEnd;
            if (index >= 0 && index < _featuresHistory.Count)
            {
                features = _featuresHistory[index];
                LoggerHelper.LogDebug(_loggerSource, $"[OfFeaturesHistory] TryGetFromEnd: hit offset={offsetFromEnd} -> bar={features?.Bar}");
                return true;
            }
            
            LoggerHelper.LogDebug(_loggerSource, $"[OfFeaturesHistory] TryGetFromEnd: miss offset={offsetFromEnd}. Count={_featuresHistory.Count}");
            return false;
        }

        /// <summary>
        /// Try-Pattern für GetOfFeatures: sicherer Zugriff ohne null-Rückgabe.
        /// barsAgo: 0 = aktuellster, 1 = vorheriger.
        /// </summary>
        public bool TryGetOfFeatures(int barsAgo, out OfFeatures features)
        {
            features = default(OfFeatures);
            if (barsAgo < 0 || barsAgo >= _featuresHistory.Count)
                return false;
            features = _featuresHistory[_featuresHistory.Count - 1 - barsAgo];
            return true;
        }

        /// <summary>
        /// Ruft die letzten 'count' OfFeatures-Objekte aus der Historie ab.
        /// </summary>
        /// <param name="count">Anzahl der abzurufenden Objekte.</param>
        /// <returns>Eine List von OfFeatures-Objekten (ältester zuerst).</returns>
        public List<OfFeatures> GetLast(int count)
        {
            if (count <= 0)
            {
                LoggerHelper.LogDebug(_loggerSource, $"[OfFeaturesHistory] GetLast: angeforderte Anzahl <=0 ({count}) -> leere Liste zurückgeben");
                return new List<OfFeatures>();
            }
            int startIndex = Math.Max(0, _featuresHistory.Count - count);
            int actualCount = Math.Min(count, _featuresHistory.Count - startIndex);
            LoggerHelper.LogDebug(_loggerSource, $"[OfFeaturesHistory] GetLast: angefordert={count}, zurückgegeben={actualCount}, Gesamtanzahl={_featuresHistory.Count}");

            return _featuresHistory.GetRange(startIndex, actualCount);
        }

        public int Count => _featuresHistory.Count;
        public bool IsEmpty => _featuresHistory.Count == 0;
        public int Available => _featuresHistory.Count; // Identisch mit Count

        /// <summary>
        /// Ruft das aktuellste OfFeatures-Objekt ab oder null (bei leerer Historie).
        /// </summary>
        public OfFeatures GetLastFeatures() => _featuresHistory.LastOrDefault();

        /// <summary>
        /// Ruft den OfFeatures-Satz für einen Bar in der Vergangenheit ab.
        /// barsAgo = 0 ist der aktuellste Bar, barsAgo = 1 der vorletzte, usw.
        /// Diese Methode gibt null zurück wenn außerhalb des Bereichs (nur sinnvoll wenn OfFeatures eine class ist).
        /// </summary>
        public OfFeatures GetOfFeatures(int barsAgo)
        {
            if (barsAgo < 0 || barsAgo >= _featuresHistory.Count)
            {
                return null; // Außerhalb des gültigen Bereichs
            }
            return _featuresHistory[_featuresHistory.Count - 1 - barsAgo];
        }

        /// <summary>
        /// Ruft eine Liste von OfFeatures für einen bestimmten Lookback-Zeitraum ab.
        /// Die Liste enthält die neuesten 'lookbackBars' OfFeatures-Objekte, beginnend mit dem ältesten.
        /// </summary>
        public List<OfFeatures> GetLastOfFeatures(int lookbackBars)
        {
            if (lookbackBars <= 0) return new List<OfFeatures>();

            int startIndex = Math.Max(0, _featuresHistory.Count - lookbackBars);
            int count = Math.Min(lookbackBars, _featuresHistory.Count);

            return _featuresHistory.GetRange(startIndex, count);
        }

        /// <summary>
        /// Berechnet den Durchschnitt von CvdImpulse über die letzten N Bars.
        /// </summary>
        public decimal GetAverageCvdImpulse(int lookbackBars)
        {
            var lastFeatures = GetLastOfFeatures(lookbackBars);
            if (!lastFeatures.Any()) return 0m;
            return lastFeatures.Average(f => f.CvdImpulse);
        }

        /// <summary>
        /// Berechnet die durchschnittliche Effizienz über die letzten N Bars.
        /// </summary>
        public decimal GetRecentEfficiencyAverage(int lookbackBars)
        {
            var lastFeatures = GetLastOfFeatures(lookbackBars);
            if (!lastFeatures.Any()) return 0m;
            return lastFeatures.Average(f => f.Efficiency);
        }

        /// <summary>
        /// Ermittelt die Anzahl der aufeinanderfolgenden Bullish-Persistenz-Bars.
        /// Es wird angenommen, dass 'PersistBull' > 0 eine bullish persistente Bar anzeigt.
        /// </summary>
        public int GetConsecutiveBullishPersistence(int maxLookback)
        {
            int count = 0;
            for (int i = 0; i < maxLookback; i++)
            {
                var feature = GetOfFeatures(i);
                if (feature != null && feature.PersistBull > 0)
                {
                    count++;
                }
                else
                {
                    break; // Kette unterbrochen oder kein Feature vorhanden
                }
            }
            return count;
        }

        /// <summary>
        /// Ermittelt die Anzahl der aufeinanderfolgenden Bearish-Persistenz-Bars.
        /// Es wird angenommen, dass 'PersistBear' > 0 eine bearish persistente Bar anzeigt.
        /// </summary>
        public int GetConsecutiveBearishPersistence(int maxLookback)
        {
            int count = 0;
            for (int i = 0; i < maxLookback; i++)
            {
                var feature = GetOfFeatures(i);
                if (feature != null && feature.PersistBear > 0)
                {
                    count++;
                }
                else
                {
                    break; // Kette unterbrochen oder kein Feature vorhanden
                }
            }
            return count;
        }

        /// <summary>
        /// Sucht ein OfFeatures-Objekt nach Bar-Index (lineare Suche vom Ende nach vorne).
        /// Return: true + out feature wenn gefunden, sonst false.
        /// Komplexität: O(n) – für moderate Kapazitäten akzeptabel.
        /// </summary>
        public bool TryGetByBar(int bar, out OfFeatures features)
        {
            features = null;
            for (int i = _featuresHistory.Count - 1; i >= 0; i--)
            {
                var f = _featuresHistory[i];
                if (f != null && f.Bar == bar)
                {
                    features = f;
                    return true;
                }
            }
            return false;
        }
        
        /// <summary>
        /// Entfernt alle gespeicherten OfFeatures.
        /// </summary>
        public void Clear()
        {
            LoggerHelper.LogInfo(_loggerSource, $"[OfFeaturesHistory] OfFeaturesHistory.Clear: Verlauf löschen. ZurückZählen={_featuresHistory.Count}");           
            _featuresHistory.Clear();
        }

        // Optional: defensive Debug-Assert um sicherzustellen, dass OfFeatures ein Referenztyp ist.
        // Falls OfFeatures zukünftig ein struct wird, kannst du diesen Assert entfernen oder anpassen.
        [Conditional("DEBUG")]
        private void AssertOfFeaturesIsClass()
        {
            Debug.Assert(!typeof(OfFeatures).IsValueType, "OfFeatures ist ein Werttyp; einige Methoden basieren auf der Null-Rückgabesemantik.");
        }
    }
}
