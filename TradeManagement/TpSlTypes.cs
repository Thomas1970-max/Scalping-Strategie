using System.Collections.Generic;
using ATAS.DataFeedsCore;
using static MyNamespace.Strategies.Goldfluss3_3;// damit OrderDirections, LevelsSnapshot, Candle, VwapSnapshot gefunden werden (falls diese Typen dort definiert sind)
using ATAS.Indicators;
using ATAS.Strategies;
using ATAS.Indicators.Technical;


namespace MyNamespace.Strategies.TradeManagement
{
    public class TpSlResult
    {
        public decimal EntryPrice { get; set; }
        public decimal StopPrice { get; set; }
        public decimal TakeProfitPrice { get; set; }

        public bool ShouldExit { get; set; }
        public string ExitReason { get; set; } = "";
        

        // Management plan (BE stages, trailing config, early exits). Wird vom TradeManager verwendet.
        public ManagementPlan Management { get; set; } = new ManagementPlan();
    }

    public class SetupParams
    {
        // Baseline TP/SL
        public decimal? TpTicks { get; set; }
        public decimal? SlTicks { get; set; }

        // NEU: TP Calculation Strategy
        public string? TpType { get; set; } // z.B. "Ticks", "Level", "DynamicLevel"
        public string? TpLevelKey { get; set; } // z.B. "VWAP", "VWAPBAND1_UPPER", "VWAPBAND1_LOWER"
        public decimal? TpLevelOffsetTicks { get; set; } // Optionaler Offset vom Level in Ticks
        
        // NEU: Dynamische Level-basierte TP-Berechnung
        public bool UseDynamicLevelTp { get; set; } = false;
        public decimal DynamicLevelTpOffsetTicks { get; set; } = 1m; // Offset vom nächsten Level
        public decimal? MaxDynamicTpDistanceTicks { get; set; } = 12m; // Maximale Distanz für dynamisches TP
        public decimal? MinDynamicTpDistanceTicks { get; set; } = 0m;  // Minimale Distanz für dynamisches TP (Fallback wenn zu nah)

        // NEU: SL Calculation Strategy
        public string? SlType { get; set; } // z.B. "Ticks", "ATR", "Level"
        public string? SlLevelKey { get; set; } // z.B. "VWAP", "VWAPBAND1_UPPER", "VWAPBAND1_LOWER"
        public decimal? SlLevelOffsetTicks { get; set; } // Optionaler Offset vom Level in Ticks

        // NEU: Parameter für VolPerSecond-basierte SL-Berechnung, wenn SlType = "VolPerSecondScaled"
        public decimal? BaseSlTicksAtAvgVol { get; set; }
        public decimal? SlVolPerSecondScaleMultiplier { get; set; }
        public int? MinSlTicks { get; set; }
        public int? MaxSlTicks { get; set; }

        // Multi-Stage BreakEven (Triggers und Offsets in Ticks)
        public decimal? BeStage1Trigger { get; set; }    // in Ticks
        public decimal? BeStage1Offset { get; set; }     // in Ticks (move SL to Entry + offset)
        public decimal? BeStage2Trigger { get; set; }
        public decimal? BeStage2Offset { get; set; }
        public string? BreakEvenLevelsTrendConfig { get; set; }

        // Trailing Stop
        public string? TrailType { get; set; }    // "CANDLE_HL", "ATR", null
        public decimal? TrailOffset { get; set; } // in Ticks
        public decimal? TrailActivateAfterTicks { get; set; } // optional activation threshold

        // Vorzeitige Ausstiegsstufen: kommagetrennte Liste, z. B. "POC,VAH".
        public string? EarlyExitLevels { get; set; }
        // maximaler Abstand vom Einstiegsniveau bis zur Berücksichtigung des frühen Ausstiegs (in Ticks)
        public decimal? EarlyExitMaxDistTicks { get; set; }
        public bool? EarlyExitCloseAtLevel { get; set; }
        public decimal? ProximityTicksForExit { get; set; } = 2m;
        public bool? EarlyCloseAtLevel { get; set; } // Added property

        // Die folgenden Felder dienen der Informationsweitergabe vom Setup an den Calculator
        // und sollten nicht direkt zur TP/SL-Berechnung im Calculator verwendet werden,
        // es sei denn, sie dienen als FALLBACK oder INITIALER VORSCHLAG.
        public decimal SuggestedTargetPrice { get; set; } // Der vom Setup vorgeschlagene Zielpreis
        public decimal SuggestedStopLossPrice { get; set; } // Der vom Setup vorgeschlagene Stop-Loss-Preis

    }

    public class ManagementPlan
    {
        public List<BreakEvenStage> BreakEvenStages { get; set; } = new List<BreakEvenStage>();
        public TrailingConfiguration Trailing { get; set; } = new TrailingConfiguration();
        public List<EarlyExitRule> EarlyExitRules { get; set; } = new List<EarlyExitRule>();

        // Optional: Debug-Informationen, woher die einzelnen Parameter stammen
        public List<string> DebugSourceMessages { get; set; } = new List<string>();
    }

    public class BreakEvenStage
    {
        // Trigger and Offset in Ticks
        public decimal TriggerTicks { get; set; }
        public decimal OffsetTicks { get; set; } // umziehen SL to Entry +/- Offset
    }

    public class TrailingConfiguration
    {
        public TrailingType TrailType { get; set; } = TrailingType.None;
        public decimal TrailOffsetTicks { get; set; } = 0; // Abstand in Ticks (für FixedTicks oder als Basis für ATR)
        public decimal TrailActivateAfterTicks { get; set; } = 0; // Aktivierung in Ticks vom Entry
                                                                  // public decimal AtrMultiplier { get; set; } = 0; // Falls Sie einen ATR-Multiplikator wollen
                                                                  // ... weitere Parameter, die für spezifische Trailing-Typen relevant sind
    }

    public enum TrailingType
    {
        None,       // Kein Trailing-Stop
        FixedTicks, // Fester Abstand in Ticks vom Best-Price
        ATR,        // Abstand basierend auf ATR
        CandleLowHigh, // Wie Ihr aktueller Manager (Low-Tick für Long, High+Tick für Short)
                       // ... weitere Trailing-Typen, falls Sie diese hinzufügen möchten
    }

    public class EarlyExitRule
    {
        // Level key, e.g. "POC", "VAH", "VAL", "DAY_HIGH" ...
        public string LevelKey { get; set; } = string.Empty;
        // Wenn wahr: die gesamte Position auf dem Niveau schließen (Gewinn mitnehmen). Wenn falsch: Stopp auf das Niveau verschieben (anziehen).
        public bool CloseAtLevel { get; set; } = false;
        // Gilt nur, wenn das Niveau innerhalb dieses Abstands (in Ticks) vom Einstieg liegt
        public decimal MaxDistanceTicks { get; set; } = decimal.MaxValue;
    }

    public class TpSlContext
    {
        public int Bar { get; set; }
        public OrderDirections Direction { get; set; }
        public LevelsSnapshot Levels { get; set; }
        public VwapSnapshot Vwap { get; set; }
        public VwapSnapshot VwapPrevious { get; set; }
        public ATAS.Indicators.IndicatorCandle Candle { get; set; }
        public decimal? TriggerLevel { get; set; }
        public decimal Tr { get; set; }    // price units (True Range)
        public decimal ER { get; set; }
        public decimal VolZ { get; set; }
        public decimal Coh01 { get; set; }
        public decimal Tick { get; set; }

        // NEU: Werte für Volatilität basierend auf VolPerSecond
        public decimal CurrentVolPerSecond { get; set; }
        public decimal AvgVolPerSecond { get; set; }

        public IDictionary<string, decimal> Params { get; set; } = new Dictionary<string, decimal>();
        public SetupParams SetupParams { get; set; } = new SetupParams();

        public decimal CurrentPrice { get; set; } = 0m;  // Aktueller Preis (z. B. Candle.Close oder Market-Price)

        public Action<string> Logger { get; set; } = msg => Console.WriteLine(msg);  // Default-Logger; setze in Strategy: ctx.Logger = (s) => this.LogInfo(s);

        // NEU: System-Status für separate TP/SL-Strategien
        public bool EnableIsBlocked { get; set; } = true;           // Level-System aktiv?
        public bool EnableMicroCompositeSystem { get; set; } = true; // MicroComposite-System aktiv?
        public bool WegFreiLong { get; set; } = true;              // Weg-Frei für Long-Trades
        public bool WegFreiShort { get; set; } = true;             // Weg-Frei für Short-Trades

    }

    public class SimpleLevel
    {
        public decimal Price { get; set; } = 0m;
        public bool IsResistance { get; set; } = false;  // true = Resistance, false = Support
        public bool IsSupport => !IsResistance;  // Hilfs-Property
    }

    public interface ITpSlCalculator
    {
        TpSlResult Calculate(decimal entryPrice, TpSlContext ctx);
    }
}