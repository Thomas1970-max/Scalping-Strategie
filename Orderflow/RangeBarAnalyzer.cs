using System;
using System.Collections.Generic;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using Utils.Common.Logging;

namespace MyNamespace.Strategies.Orderflow
{
    /// <summary>
    /// Universeller Range-Bar Analyzer für Pattern-Erkennung
    /// Kann von allen Pattern-Evaluatoren verwendet werden
    /// </summary>
    public static class RangeBarAnalyzer
    {
        // Persistente Variablen für Entry-Fenster über Aufrufe hinweg
        private static int? _persistentLastValidReversalAbsoluteBar = null;
        private static int _persistentLastValidReversalCurrentBar = -1;
        private static string _persistentLastValidReversalType = "";
        
        public static RangeBarAnalysisResult AnalyzeRangeBarPattern(
            OfFeaturesHistory history, 
            int currentBar, 
            OvSnapshot currentSnapshot, 
            OrderflowThresholds thresholds,
            ILoggerSource loggerSource = null)
        {
            LoggerHelper.LogInfo(loggerSource, $"[RangeBarAnalyzer] 🚪 ENTRY: history={history != null}, currentBar={currentBar}");

            if (history == null || currentBar < 3)
            {
                LoggerHelper.LogInfo(loggerSource, $"[RangeBarAnalyzer] ❌ EARLY RETURN: history={history == null}, currentBar={currentBar} < 3");
                return new RangeBarAnalysisResult { 
                    IsRangeMarket = false, 
                    HasValidReversalSetup = false,
                    Reason = "Insufficient history or bars" 
                };
            }

            var tickTrend = thresholds.RangeBarTrendSizeTicks;
            var tickReversal = thresholds.RangeBarReversalSizeTicks;
            decimal tickSize = thresholds.TickSizeDecimal ?? 0.25m;
            if (tickSize <= 0m)
                tickSize = 0.25m;

            // Bar-Typen für die letzten Bars sammeln
            var barTypes = new List<string>();
            var barIndices = new List<int>();
            
            // Vereinfachte Logik: Nur aktuelle Bar analysieren (später erweiterbar)
            string currentBarType = DetermineBarType(currentSnapshot, tickTrend, tickReversal, tickSize);
            barTypes.Add(currentBarType);
            barIndices.Add(currentBar);
            
            LoggerHelper.LogInfo(loggerSource, $"[RangeBarAnalyzer] 📊 CURRENT BAR: Type={currentBarType}, Bar={currentBar}");

            // Pattern-Erkennung
            bool hasValidLongSetup = false;
            bool hasValidShortSetup = false;
            string lastReversalType = "";
            List<string> lastTrendTypes = new List<string>();
            int? lastValidReversalAbsoluteBar = null;
            int? previousLastValidReversalAbsoluteBar = _persistentLastValidReversalAbsoluteBar;

            // Für jetzt: Vereinfachte Pattern-Erkennung (später erweiterbar auf mehrere Bars)
            if (currentBarType == "REVERSAL_BULL")
            {
                hasValidLongSetup = true;
                lastReversalType = "REVERSAL_BULL";
                lastTrendTypes = new List<string> { "TREND_BEAR", "TREND_BEAR" }; // Simuliert
                
                lastValidReversalAbsoluteBar = currentBar;
                _persistentLastValidReversalAbsoluteBar = lastValidReversalAbsoluteBar;
                _persistentLastValidReversalCurrentBar = currentBar;
                _persistentLastValidReversalType = "REVERSAL_BULL";
                
                LoggerHelper.LogInfo(loggerSource, $"[RangeBarAnalyzer] ✅ LONG SETUP FOUND: Reversal-Bar={currentBar}, Type=REVERSAL_BULL");
            }
            else if (currentBarType == "REVERSAL_BEAR")
            {
                hasValidShortSetup = true;
                lastReversalType = "REVERSAL_BEAR";
                lastTrendTypes = new List<string> { "TREND_BULL", "TREND_BULL" }; // Simuliert
                
                lastValidReversalAbsoluteBar = currentBar;
                _persistentLastValidReversalAbsoluteBar = lastValidReversalAbsoluteBar;
                _persistentLastValidReversalCurrentBar = currentBar;
                _persistentLastValidReversalType = "REVERSAL_BEAR";
                
                LoggerHelper.LogInfo(loggerSource, $"[RangeBarAnalyzer] ✅ SHORT SETUP FOUND: Reversal-Bar={currentBar}, Type=REVERSAL_BEAR");
            }

            // Entry-Fenster Management
            bool hasValidReversalSetup = hasValidLongSetup || hasValidShortSetup;
            
            // Wenn kein neues Pattern gefunden, aber vorheriges existiert
            if (!hasValidLongSetup && !hasValidShortSetup && previousLastValidReversalAbsoluteBar.HasValue)
            {
                lastValidReversalAbsoluteBar = previousLastValidReversalAbsoluteBar;
                hasValidReversalSetup = true;
                
                if (_persistentLastValidReversalType != null)
                {
                    lastReversalType = _persistentLastValidReversalType;
                    LoggerHelper.LogInfo(loggerSource, $"[RangeBarAnalyzer] ℹ️ ENTRY-FENSTER: Behalte vorheriges Pattern, Reversal-Bar={lastValidReversalAbsoluteBar}, Type={lastReversalType}");
                }
            }

            // Bars seit Reversal berechnen
            int barsSinceLastValidReversal = 999;
            if (lastValidReversalAbsoluteBar.HasValue)
            {
                barsSinceLastValidReversal = currentBar - lastValidReversalAbsoluteBar.Value;
                LoggerHelper.LogInfo(loggerSource, $"[RangeBarAnalyzer] ℹ️ ENTRY-FENSTER: Aktueller Bar={currentBar}, Reversal-Bar={lastValidReversalAbsoluteBar}, Bars seit Reversal={barsSinceLastValidReversal}");
            }

            // Entry-Fenster Schließung bei abgelaufenem Zeitfenster
            int maxBarsAfterReversalThreshold = thresholds.RangeBarMaxBarsAfterReversal;
            if (barsSinceLastValidReversal > maxBarsAfterReversalThreshold)
            {
                _persistentLastValidReversalAbsoluteBar = null;
                _persistentLastValidReversalCurrentBar = -1;
                _persistentLastValidReversalType = "";
                LoggerHelper.LogInfo(loggerSource, $"[RangeBarAnalyzer] 🔄 ENTRY-FENSTER: Zurückgesetzt - Bars seit Reversal={barsSinceLastValidReversal} > Max-Bars={maxBarsAfterReversalThreshold}");
                hasValidReversalSetup = false;
            }

            return new RangeBarAnalysisResult
            {
                IsRangeMarket = false,
                HasValidReversalSetup = hasValidReversalSetup,
                HasValidLongReversalSetup = hasValidLongSetup,
                HasValidShortReversalSetup = hasValidShortSetup,
                LastReversalType = lastReversalType,
                LastTrendTypes = lastTrendTypes,
                BarTypes = barTypes,
                ConsecutiveTrendBars = 0,
                BarsSinceLastReversal = barsSinceLastValidReversal,
                BarsSinceLastValidReversal = barsSinceLastValidReversal,
                Reason = hasValidReversalSetup ? "Valid reversal setup found" : "No valid reversal setup"
            };
        }

        private static string DetermineBarType(OvSnapshot snapshot, int tickTrend, int tickReversal, decimal tickSize)
        {
            if (snapshot == null)
                return "UNKNOWN";
            
            decimal priceMovement = Math.Abs(snapshot.Close - snapshot.Open);
            int ticks = (int)(priceMovement / tickSize);
            
            if (ticks >= tickReversal)
                return snapshot.Close > snapshot.Open ? "REVERSAL_BULL" : "REVERSAL_BEAR";
            else if (ticks >= tickTrend)
                return snapshot.Close > snapshot.Open ? "TREND_BULL" : "TREND_BEAR";
            
            return "UNKNOWN";
        }
    }

    /// <summary>
    /// Result-Klasse für Range-Bar Analyse
    /// Enthält alle Pattern-Informationen für Evaluatoren
    /// </summary>
    public class RangeBarAnalysisResult
    {
        public bool IsRangeMarket { get; set; }
        public bool HasValidReversalSetup { get; set; }
        public bool HasValidLongReversalSetup { get; set; }
        public bool HasValidShortReversalSetup { get; set; }
        public string LastReversalType { get; set; } = "";
        public List<string> LastTrendTypes { get; set; } = new List<string>();
        public List<string> BarTypes { get; set; } = new List<string>();
        public int ConsecutiveTrendBars { get; set; }
        public int BarsSinceLastReversal { get; set; }
        public int BarsSinceLastValidReversal { get; set; }
        public string Reason { get; set; } = "";
    }
}
