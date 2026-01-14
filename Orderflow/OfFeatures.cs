using System;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using static MyNamespace.Strategies.Goldfluss3_3;

namespace MyNamespace.Strategies.Orderflow
{

    
    public class SqueezeFeatures
    {
        // Zustand der Squeeze-Indikation
        public SqueezeStateEnum SqueezeState { get; set; } = SqueezeStateEnum.Unknown;

        // Roher Momentum-/Oscillator-Wert (z. B. Squeeze Momentum / Linear regression / momentum)
        public decimal MomentumVal { get; set; } = 0m;

        // Differenz zu vorherigem Bar (einfacher Slope)
        public decimal MomentumSlope { get; set; } = 0m;

        // Optionaler, normalisierter Volatility-/Confidence-Score in [0,1]
        public decimal VolatilityScore { get; set; } = 0.5m;

        // Anzahl Bars, die der aktuelle SqueezeState bereits andauert (für Hysterese checks)
        public int StateDurationBars { get; set; } = 0;

        public override string ToString()
        {
            return $"SqueezeState={SqueezeState}, Momentum={MomentumVal:F6}, Slope={MomentumSlope:F6}, VolScore={VolatilityScore:F2}, Duration={StateDurationBars}";
        }
    }

    public class OfFeatures
    {
        // Referenz auf das zugrundeliegende Snapshot (optional, aber nützlich für Debug/Tracing)
        public OvSnapshot Snapshot { get; set; } = new OvSnapshot();

        public int Bar;
        public decimal High;
        public decimal Low;
        public decimal Open;
        public decimal Close;

        // Grundmetriken
        public decimal CvdImpulse { get; set; }
        public decimal AggPressure { get; set; }
        public decimal Efficiency { get; set; }
        public decimal TradeRateZ { get; set; }
        public decimal VolBurstZ { get; set; }
        public decimal IttZ { get; set; }
        public decimal MaxCounterShareBull { get; set; }
        public decimal MaxCounterShareBear { get; set; }
        public bool SweepUpClosed { get; set; }
        public bool SweepDnClosed { get; set; }

        // Stacked Imbalance Metriken
        public int StackedBuyImbCount { get; set; }
        public int StackedSellImbCount { get; set; }
        public int StackedBuyImbTopCount { get; set; }
        public int StackedSellImbBottomCount { get; set; }

        // Abgeleitete Features
        public decimal SlopeCvd;
        public decimal SlopePressure;
        public decimal SlopeEff;
        public decimal SlopeTradeRate;

        public int PersistBull;
        public int PersistBear;

        public InflectionType InflectionCvd;
        public InflectionType InflectionPressure;

        public BurstClass VolBurstClass;
        public int VolBurstCooldownLeft;
        // --- NEU: Squeeze-Integration ---
        // Optional: wenn vorhanden, liefert Squeeze-Momentum und Zustand (On/Off)
        public SqueezeFeatures? Squeeze { get; set; }
        public override string ToString()
        {
            return $"CVD: {CvdImpulse}, AP: {AggPressure:F2}, PBull: {PersistBull}, PBear: {PersistBear}, SlopeCVD: {SlopeCvd:F2}, Eff: {Efficiency:F2}, VolBurst: {VolBurstClass}, TradeRateZ: {TradeRateZ:F2}";
        }
    }
}


