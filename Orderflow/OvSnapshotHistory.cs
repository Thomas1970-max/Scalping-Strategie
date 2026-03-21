using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using Utils.Common.Logging;
using static MyNamespace.Strategies.Geldfluss3_3;

namespace MyNamespace.Strategies.Orderflow
{
    // Diese Klasse speichert NUR OvSnapshot-Objekte.
    public class OvSnapshotHistory
    {
        private readonly List<OvSnapshot> _snapshots;
        private readonly int _maxCapacity; // Um die Liste auf eine maximale Größe zu beschränken
        private readonly ILoggerSource _loggerSource;
        public OvSnapshotHistory(int capacity = 256, ILoggerSource loggerSource = null) // Standardkapazität von 256
        {
            _loggerSource = loggerSource;
            if (capacity <= 0)
            {
                LoggerHelper.LogError(_loggerSource, $"[OvSnapshotHistory] OvSnapshotHistory ctor: invalid capacity {capacity}. Throwing.");
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
            }

            _maxCapacity = capacity;
            _snapshots = new List<OvSnapshot>(capacity);

            LoggerHelper.LogInfo(_loggerSource, $"[OvSnapshotHistory] OvSnapshotHistory created. Capacity={_maxCapacity}");
        }

        public void Add(OvSnapshot snapshot)
        {
            if (snapshot == null)
            {
                LoggerHelper.LogWarn(_loggerSource, "[OvSnapshotHistory] OvSnapshotHistory.Add called with null snapshot. Ignored.");
                return;
            }

            _snapshots.Add(snapshot);
            LoggerHelper.LogDebug(_loggerSource, $"[OvSnapshotHistory] OvSnapshotHistory.Add: added snapshot Bar={snapshot.Bar}. Count={_snapshots.Count}");

            // Wenn die Liste die maximale Kapazität überschreitet, entfernen wir das älteste Element
            if (_snapshots.Count > _maxCapacity)
            {
                // Log vor dem Entfernen, damit man nachvollziehen kann, daß Trimming passierte
                LoggerHelper.LogInfo(_loggerSource, $"[OvSnapshotHistory] OvSnapshotHistory capacity exceeded ({_snapshots.Count}/{_maxCapacity}). Removing oldest snapshot Bar={_snapshots.FirstOrDefault()?.Bar}");
                _snapshots.RemoveAt(0);
            }
        }


        // Ruft ein OvSnapshot-Objekt relativ zum Ende der Historie ab.
        // offset = 0 ist der aktuellste, offset = -1 ist der vorletzte, usw.
        public bool TryGetRelative(int offset, out OvSnapshot snapshot)
        {
            snapshot = default(OvSnapshot);
            // Der Offset ist für historische Balken in der Regel negativ (-1 für den vorherigen, -2 für den vorletzten Balken).
            // Die Indexberechnung ist korrekt für diese Interpretation.
            int index = (_snapshots.Count - 1) + offset;
            if (index >= 0 && index < _snapshots.Count)
            {
                snapshot = _snapshots[index];
                LoggerHelper.LogDebug(_loggerSource, $"[OvSnapshotHistory] OvSnapshotHistory.TryGetRelative: offset={offset} -> index={index}, returned Bar={snapshot?.Bar}");
                return true;
            }

            LoggerHelper.LogDebug(_loggerSource, $"[OvSnapshotHistory] OvSnapshotHistory.TryGetRelative: offset={offset} -> index={index} out of range (Count={_snapshots.Count}). Returning false.");
            return false;
        }

        // Ruft die letzten 'count' OvSnapshots aus der Historie ab.
        // Die Liste ist vom ältesten zum neuesten sortiert.
        public List<OvSnapshot> GetLast(int count)
        {
            if (count <= 0)
            {
                LoggerHelper.LogDebug(_loggerSource, $"[OvSnapshotHistory] OvSnapshotHistory.GetLast called with non-positive count={count}. Returning empty list.");
                return new List<OvSnapshot>();
            }

            int startIndex = Math.Max(0, _snapshots.Count - count);
            int actualCount = Math.Min(count, _snapshots.Count - startIndex);

            // Verwenden Sie GetRange für eine performante Kopie des benötigten Bereichs
            var result = _snapshots.GetRange(startIndex, actualCount);
            LoggerHelper.LogDebug(_loggerSource, $"[OvSnapshotHistory] OvSnapshotHistory.GetLast: requested={count}, returned={result.Count}, startIndex={startIndex}");
            return result;
        }

        public int Count => _snapshots.Count;
        public bool IsEmpty => _snapshots.Count == 0;

        public OvSnapshot GetLastSnapshot()
        {
            var last = _snapshots.LastOrDefault();
            LoggerHelper.LogDebug(_loggerSource, $"[OvSnapshotHistory] OvSnapshotHistory.GetLastSnapshot -> Bar={(last?.Bar.ToString() ?? "null")}");
            return last;
        }
        public void ReplaceLast(OvSnapshot snapshot)
        {
            if (snapshot == null)
            {
                LoggerHelper.LogWarn(_loggerSource, "[OvSnapshotHistory] OvSnapshotHistory.ReplaceLast called with null snapshot. Ignored.");
                return;
            }

            if (_snapshots.Count == 0)
            {
                LoggerHelper.LogInfo(_loggerSource, $"[OvSnapshotHistory] OvSnapshotHistory.ReplaceLast: history empty, adding snapshot Bar={snapshot.Bar}");
                Add(snapshot);
                return;
            }

            LoggerHelper.LogDebug(_loggerSource, $"[OvSnapshotHistory] OvSnapshotHistory.ReplaceLast replacing Bar={_snapshots.Last().Bar} with Bar={snapshot.Bar}");
            _snapshots[_snapshots.Count - 1] = snapshot;
        }

    }
}

