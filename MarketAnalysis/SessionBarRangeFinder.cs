using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.DataFeedsCore;
using ATAS.Indicators;
using MyNamespace.Strategies.Models;
using MyNamespace.Strategies.Orderflow;
using Utils.Common.Logging;
using static MyNamespace.Strategies.Goldfluss3_3;

namespace MyNamespace.Strategies.MarketAnalysis
{
    public class SessionBarRangeFinder
    {
        private readonly ISessionProvider _sessionProvider;
        private readonly ICandleProvider _candleProvider;
        private readonly IInstrumentInfoProvider _instrumentInfoProvider;

       
    // Logging als Delegate, damit sowohl Action als auch Action<string> akzeptiert wird
    private readonly Delegate _logInfo;
        private readonly Delegate _logWarn;

        public SessionBarRangeFinder(
            ISessionProvider sessionProvider,
            ICandleProvider candleProvider,
            IInstrumentInfoProvider instrumentInfoProvider,
            Delegate logInfo = null,
            Delegate logWarn = null)
        {
            _sessionProvider = sessionProvider ?? throw new ArgumentNullException(nameof(sessionProvider));
            _candleProvider = candleProvider ?? throw new ArgumentNullException(nameof(candleProvider));
            _instrumentInfoProvider = instrumentInfoProvider ?? throw new ArgumentNullException(nameof(instrumentInfoProvider));

            _logInfo = logInfo;
            _logWarn = logWarn;
        }

        // Hilfsmethode: versucht zuerst Aufruf mit einem string-Argument, fallback auf parameterlosen Aufruf
        private void SafeLog(Delegate dlg, string message)
        {
            if (dlg == null) return;

            try
            {
                var paramCount = dlg.Method.GetParameters().Length;
                if (paramCount == 0)
                    dlg.DynamicInvoke();
                else
                    dlg.DynamicInvoke(message);
            }
            catch (ArgumentException)
            {
                try { dlg.DynamicInvoke((object)message); } catch { /* swallow */ }
            }
            catch
            {
                // Logging darf keine Ausnahmen die Hauptlogik zerstören
            }
        }

        public (int sessionStartBar, int sessionEndBar) FindSessionBarRange(int barIndex)
        {
            if (barIndex < 0 || barIndex >= _candleProvider.DataSeriesCount)
            {
                SafeLog(_logInfo, $"SessionBarRangeFinder: barIndex {barIndex} außerhalb des zulässigen Bereichs oder negativ ? (-1,-1)");
                return (-1, -1);
            }

            int sessionEndBar = barIndex;
            int sessionStartBar = barIndex;
            while (sessionStartBar > 0 && !_sessionProvider.IsNewSession(sessionStartBar))
            {
                sessionStartBar--;
            }

            IndicatorCandle startCandle = _candleProvider.GetCandle(sessionStartBar);
            IndicatorCandle endCandle = _candleProvider.GetCandle(sessionEndBar);

            TimeZoneInfo chartDisplayTimeZone = null;

            if (_instrumentInfoProvider.InstrumentInfo != null)
            {
                try
                {
                    string exchangeName = _instrumentInfoProvider.InstrumentInfo.GetType().GetProperty("Exchange")?.GetValue(_instrumentInfoProvider.InstrumentInfo) as string;

                    if (!string.IsNullOrEmpty(exchangeName))
                    {
                        // TimeZoneConverter bleibt unverändert; erwartet einen Delegate für Warnungen, wir übergeben SafeLog-wrapped Delegate
                        chartDisplayTimeZone = TimeZoneConverter.GetTimeZoneInfoFromExchange(exchangeName, arg =>
                        {
                            // TimeZoneConverter ruft möglicherweise Action<string> oder Action auf — wir leiten an _logWarn weiter
                            SafeLog(_logWarn, arg?.ToString() ?? "null");
                        });

                        int? tzId = _instrumentInfoProvider.InstrumentInfo.GetType().GetProperty("TimeZone")?.GetValue(_instrumentInfoProvider.InstrumentInfo) as int?;
                        if (chartDisplayTimeZone == null && tzId.HasValue && tzId.Value == 1)
                        {
                            try
                            {
                                chartDisplayTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
                            }
                            catch (Exception fallbackEx)
                            {
                                SafeLog(_logWarn, $"SessionBarRangeFinder: Fallback failed: {fallbackEx.Message}");
                            }
                        }
                    }
                    else
                    {
                        SafeLog(_logWarn, "SessionBarRangeFinder: InstrumentInfo.Exchange ist null oder leer.");
                    }
                }
                catch (Exception ex)
                {
                    SafeLog(_logWarn, $"SessionBarRangeFinder: Fehler beim Zugriff auf InstrumentInfo.Exchange: {ex.Message}");
                }
            }

            DateTime? loggedStartTime = null;
            DateTime? loggedEndTime = null;

            if (chartDisplayTimeZone != null && startCandle != null && endCandle != null)
            {
                try
                {
                    DateTime startUtc = startCandle.Time.Kind == DateTimeKind.Unspecified
                                        ? DateTime.SpecifyKind(startCandle.Time, DateTimeKind.Utc)
                                        : startCandle.Time.ToUniversalTime();
                    DateTime endUtc = endCandle.Time.Kind == DateTimeKind.Unspecified
                                      ? DateTime.SpecifyKind(endCandle.Time, DateTimeKind.Utc)
                                      : endCandle.Time.ToUniversalTime();

                    loggedStartTime = TimeZoneInfo.ConvertTimeFromUtc(startUtc, chartDisplayTimeZone);
                    loggedEndTime = TimeZoneInfo.ConvertTimeFromUtc(endUtc, chartDisplayTimeZone);
                }
                catch (Exception ex)
                {
                    SafeLog(_logWarn, $"SessionBarRangeFinder: Fehler bei Konvertierung: {ex.Message}. Verwende Originalzeiten.");
                    loggedStartTime = startCandle?.Time;
                    loggedEndTime = endCandle?.Time;
                }
            }
            else
            {
                SafeLog(_logInfo, "SessionBarRangeFinder: Keine TimeZone gefunden. Verwende unkonvertierte Zeiten.");
                loggedStartTime = startCandle?.Time;
                loggedEndTime = endCandle?.Time;
            }

            return (sessionStartBar, sessionEndBar);
        }
    }
}


