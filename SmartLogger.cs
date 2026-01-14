using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Utils.Common.Logging;


namespace MyNamespace.Strategies
{
    public enum LogPolicy
    {
        OncePerBarIfChanged, // Standard: pro Bar nur loggen, wenn sich die Signatur geändert hat
        Always,               // Immer loggen
        OncePerBarAlways      // Pro Bar maximal 1x loggen, auch ohne Signaturvergleich
    }

    public sealed class SmartLogger
    {
        // Singleton-Instanz
        private static readonly Lazy<SmartLogger> _instance = new Lazy<SmartLogger>(() => new SmartLogger());
        public static SmartLogger Instance => _instance.Value;

        // Per-Category ? per-Bar ? last signature
        // category -> (barIndex -> signature)
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, string>> _lastSignatures
            = new ConcurrentDictionary<string, ConcurrentDictionary<int, string>>(StringComparer.Ordinal);

        // category -> policy (kann zur Laufzeit geändert werden)
        private readonly ConcurrentDictionary<string, LogPolicy> _categoryPolicies
            = new ConcurrentDictionary<string, LogPolicy>(StringComparer.Ordinal);

        // Optional: per-category per-source (feinere Granularität), nicht zwingend erforderlich
        // sourceKey ist z.B. "PatternSignaturer" oder "ThresholdsPruner"
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<int, string>>> _perSourceSignatures
            = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<int, string>>>(StringComparer.Ordinal);

        // Max gespeicherte Bars pro Category (Default)
        private int _maxBarsPerCategory = 2000;

        // Aufräum-Intervall (ms); im Hintergrund-Thread wird gelegentlich aufgeräumt
        private readonly int _cleanupIntervalMs = 10_000; // 10s

        // Ablaufsteuerung für Cleanup-Thread
        private readonly Timer _cleanupTimer;

        private SmartLogger()
        {
            // Default-Policies für einige sinnvolle Kategorien (kann überschrieben werden)
            _categoryPolicies.TryAdd("MarketState", LogPolicy.OncePerBarIfChanged);
            _categoryPolicies.TryAdd("PatternDetection", LogPolicy.OncePerBarIfChanged);
            _categoryPolicies.TryAdd("Thresholds", LogPolicy.OncePerBarIfChanged);
            _categoryPolicies.TryAdd("Pruner", LogPolicy.OncePerBarIfChanged);
            _categoryPolicies.TryAdd("Evaluators", LogPolicy.OncePerBarIfChanged);
            _categoryPolicies.TryAdd("Squeeze", LogPolicy.OncePerBarIfChanged);
            _categoryPolicies.TryAdd("IO", LogPolicy.Always);

            // Start Cleanup Timer
            _cleanupTimer = new Timer(CleanupCallback, null, _cleanupIntervalMs, _cleanupIntervalMs);
        }

        // Konfiguration: Max Bars pro Kategorie (optional)
        public void SetMaxBarsPerCategory(int maxBars)
        {
            if (maxBars <= 0) throw new ArgumentOutOfRangeException(nameof(maxBars));
            _maxBarsPerCategory = maxBars;
        }

        // Setze oder ändere Policy für Kategorie
        public void SetCategoryPolicy(string category, LogPolicy policy)
        {
            if (string.IsNullOrWhiteSpace(category)) throw new ArgumentNullException(nameof(category));
            _categoryPolicies.AddOrUpdate(category, policy, (_, __) => policy);
        }

        // Public API: LogIfChanged (einfacher Fall ohne sourceId)
        // backendLogAction ist z.B. s => _loggerSource.LogInfo(s)
        public void LogIfChanged(string category, int barIndex, string message, string signature, Action<string> backendLogAction)
        {
            LogIfChanged(category, null, barIndex, message, signature, backendLogAction);
        }

        // Public API: LogIfChanged mit optionaler sourceId (feinere Granularität)
        public void LogIfChanged(string category, string sourceId, int barIndex, string message, string signature, Action<string> backendLogAction)
        {
            if (string.IsNullOrWhiteSpace(category)) throw new ArgumentNullException(nameof(category));
            if (backendLogAction == null) throw new ArgumentNullException(nameof(backendLogAction));

            var policy = _categoryPolicies.TryGetValue(category, out var p) ? p : LogPolicy.OncePerBarIfChanged;

            // Normalize inputs
            signature = signature ?? string.Empty;
            sourceId = sourceId ?? string.Empty;

            if (policy == LogPolicy.Always)
            {
                backendLogAction(message);
                return;
            }

            if (policy == LogPolicy.OncePerBarAlways && sourceId == string.Empty)
            {
                // simpler semantics: per category per bar max 1 log (regardless of signature)
                var catDict = _lastSignatures.GetOrAdd(category, _ => new ConcurrentDictionary<int, string>());
                // TryAdd returns false if exists -> suppress
                if (catDict.TryAdd(barIndex, "__logged__"))
                {
                    backendLogAction(message);
                }
                return;
            }

            if (string.IsNullOrEmpty(sourceId))
            {
                // category-level signature comparison
                var catDict = _lastSignatures.GetOrAdd(category, _ => new ConcurrentDictionary<int, string>());
                if (ShouldLogAndUpdate(catDict, barIndex, signature))
                {
                    backendLogAction(message);
                }
            }
            else
            {
                // source-level signature comparison inside category
                var catSources = _perSourceSignatures.GetOrAdd(category, _ => new ConcurrentDictionary<string, ConcurrentDictionary<int, string>>(StringComparer.Ordinal));
                var sourceDict = catSources.GetOrAdd(sourceId, _ => new ConcurrentDictionary<int, string>());
                if (ShouldLogAndUpdate(sourceDict, barIndex, signature))
                {
                    backendLogAction(message);
                }
            }
        }

        // Kernlogik: entscheidet, ob geloggt werden soll; aktualisiert state falls nötig
        private bool ShouldLogAndUpdate(ConcurrentDictionary<int, string> barDict, int barIndex, string signature)
        {
            while (true)
            {
                // Existiert bereits Signatur für diesen Bar?
                if (barDict.TryGetValue(barIndex, out var existing))
                {
                    if (existing == signature)
                    {
                        // Keine Änderung => unterdrücken
                        return false;
                    }

                    // Versuche zu ersetzen (CAS-ähnlich)
                    if (barDict.TryUpdate(barIndex, signature, existing))
                    {
                        return true;
                    }

                    // TryUpdate failed, Loop erneut
                    continue;
                }
                else
                {
                    // Versuche hinzuzufügen; wenn ein anderer Thread zuerst war, dann Loop
                    if (barDict.TryAdd(barIndex, signature))
                    {
                        // Möglicherweise Überschreitung der Größe wird später im Cleanup behandelt
                        return true;
                    }

                    continue;
                }
            }
        }

        // Compute compact SHA1 hex hash für große Signaturen (Hilfsmethode)
        public static string ComputeHashHex(string input)
        {
            if (input == null) input = string.Empty;
            using (var sha1 = SHA1.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(input);
                var hash = sha1.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.AppendFormat("{0:x2}", b);
                return sb.ToString();
            }
        }

        // Optionaler Helper: Compose signature aus key-value; nützlich um deterministisch zu bleiben
        // Beispiel: ComposeSignature(("regime","Slow"),("pattern","GENERIC"),("hv","12"))
        public static string ComposeSignature(params (string key, string value)[] parts)
        {
            if (parts == null || parts.Length == 0) return string.Empty;
            var sb = new StringBuilder();
            foreach (var (key, value) in parts)
            {
                sb.Append(key ?? string.Empty);
                sb.Append('=');
                sb.Append(value ?? string.Empty);
                sb.Append(';');
            }
            return sb.ToString();
        }

        // Cleanup: wird periodisch ausgeführt. Entfernt alte Bars, falls zu groß.
        private void CleanupCallback(object state)
        {
            try
            {
                // Clean category-level dictionaries
                foreach (var kv in _lastSignatures)
                {
                    CleanupCategoryDict(kv.Value);
                }

                // Clean per-source dictionaries
                foreach (var catKv in _perSourceSignatures)
                {
                    foreach (var srcKv in catKv.Value)
                    {
                        CleanupCategoryDict(srcKv.Value);
                    }
                }
            }
            catch
            {
                // Swallow exceptions to keep Timer alive. Logging hier würde zu Rekursion führen.
            }
        }

        private void CleanupCategoryDict(ConcurrentDictionary<int, string> dict)
        {
            try
            {
                if (dict.Count <= _maxBarsPerCategory) return;

                // Entferne die ältesten Einträge; bestimme Anzahl zu entfernen
                var toRemoveCount = Math.Max(1, dict.Count - _maxBarsPerCategory / 2);

                // hole die ältesten Keys (konservativ)
                var orderedKeys = dict.Keys.OrderBy(k => k).Take(toRemoveCount).ToList();

                foreach (var k in orderedKeys)
                {
                    dict.TryRemove(k, out _);
                }
            }
            catch
            {
                // Ignore
            }
        }

        // Optional: Clear caches (z.B. für Tests)
        public void ClearCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return;
            _lastSignatures.TryRemove(category, out _);
            _perSourceSignatures.TryRemove(category, out _);
        }

        public void ClearAll()
        {
            _lastSignatures.Clear();
            _perSourceSignatures.Clear();
        }
    }
}


