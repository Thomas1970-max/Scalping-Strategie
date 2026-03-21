using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using MyNamespace.Strategies.Orderflow;
using Utils.Common.Logging;
using static MyNamespace.Strategies.Geldfluss3_3;

namespace MyNamespace.Strategies.MarketAnalysis
{
    public static class TimeZoneConverter
    {
        private static readonly Dictionary<string, string> _exchangeToTimeZone =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Chicago Mercantile Exchange", "Central Standard Time" },
            { "CME", "Central Standard Time" },
            { "New York Stock Exchange", "Eastern Standard Time" },
            { "NYSE", "Eastern Standard Time" },
            { "EUREX", "W. Europe Standard Time" },
            { "ICE", "W. Europe Standard Time" },
            { "London Stock Exchange", "GMT Standard Time" },
            { "LSE", "GMT Standard Time" },
            { "Hong Kong Stock Exchange", "China Standard Time" },
        };

        // Der Action-Parameter erlaubt das Injizieren eines Logging-Mechanismus
        public static TimeZoneInfo GetTimeZoneInfoFromExchange(string exchangeName, Action<string> logWarn = null)
        {
            if (string.IsNullOrEmpty(exchangeName))
                return null;

            if (_exchangeToTimeZone.TryGetValue(exchangeName, out string timeZoneId))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                }
                catch (Exception ex)
                {
                    logWarn?.Invoke($"TimeZoneConverter: Konnte TimeZone '{timeZoneId}' für '{exchangeName}' nicht laden: {ex.Message}");
                }
            }
            // Optional: Warnung ausgeben, wenn keine Zeitzone gefunden wurde
            // logWarn?.Invoke($"TimeZoneConverter: Unbekannter Exchange '{exchangeName}'. Keine TimeZone gefunden.");
            return null;
        }
    }
}


