using System; // Für [Flags] Attribut, falls GateMetric ein Flags-Enum ist
using static MyNamespace.Strategies.Goldfluss3_3;
using MyNamespace.Strategies.Orderflow;
using System.Diagnostics.Metrics;
using System.Drawing;
using System.ComponentModel;

namespace MyNamespace.Strategies.Models 
{

    public enum PatternCategory
    {
        Unknown,
        Reversal,     // Für Muster wie Bounce, Pullback, etc.
        Continuation, // Für Muster wie TrendFollow
        Breakout,
        MeanReversion,
        // Füge hier weitere Kategorien hinzu, falls deine Muster weitere generische Typen abdecken
    }

    //Dies ist ein Enum, das die Volatilität des Marktes beschreibt.
    public enum MarketRegime { Fast, Normal, Slow, None }


    //Dieses Enum wird die übergeordnete, direktionale Tendenz des Marktes erfassen, was in meiner ursprünglichen Idee das "globale MarketRegime" war.
    public enum MarketDirectionalBias
    {
        Undefined = 0,           // Keine klare Tendenz erkennbar
        BullishTrend = 1,       // Starker Aufwärtstrend
        BearishTrend = 2,       // Starker Abwärtstrend
        Sideways = 3,           // Konsolidierung / Seitwärtsbewegung
        Choppy = 4,             // Volatiler, richtungsloser Markt mit vielen Oszillationen
        
    }

    

    [Flags] // Wenn dies ein Flags-Enum ist, um mehrere Werte kombinieren zu können
    public enum GateMetric
    {
        None = 0,
        VolBurstZ = 1 << 0,
        AggPressureDirectional = 1 << 1,
        CvdImpulse = 1 << 2,
        CvdCoherence = 1 << 3,
        TradeRateZ = 1 << 4,
        InterTradeTimeZ = 1 << 5,
        Efficiency = 1 << 6,
        StackedImbalanceAnyDirectional = 1 << 7,
        StackedImbalanceAnchoredDirectional = 1 << 8,
        StackedImbalanceOppositeAnchoredWeak = 1 << 9,
        MaxCounterDeltaShare = 1 << 10, // Beispiel, falls benötigt
        BarDeltaPerVolume = 1 << 11,
        VolPerSecond = 1 << 12,
        All = ~0
        // ... weitere Gate-Metriken
    }

    public enum OrderflowPatternType
    {
        None,       
        GeneralTrendDetection, // Neu für Bias-Erkennung
        GeneralRangeDetection, // Neu für Bias-Erkennung
        PotentialLongReversalBounce, // Z.B. Orderflow-Muster für Reversal Long
        PotentialShortReversalBounce, // Orderflow-Muster für Reversal Short
        PotentialLongTrendContinuation,
        PotentialShortTrendContinuation,
        PotentialLongBreakout,
        PotentialShortBreakout,
        PotentialLongPullback, 
        PotentialShortPullback, 
        // ... weitere Muster wie AbsorptionAheadOfMove, MeanReversion etc.
    }
    
    public enum BurstClass
    {
        None = 0,
        Minor = 1,
        Major = 2
    }

    public enum InflectionType
    {
        None = 0,
        Bullish = 1,
        Bearish = 2
    }

    
    public enum TradeProfile
    {
        [Description("Vorsichtig — strengere Filter, weniger Trades")]
        Conservative,

        [Description("Neutral — Standardverhalten")]
        Neutral,

        [Description("Aggressiv — lockerere Filter, mehr Trades")]
        Aggressive
    }
}


