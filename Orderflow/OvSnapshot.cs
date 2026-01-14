using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using static MyNamespace.Strategies.Goldfluss3_3;

namespace MyNamespace.Strategies.Orderflow
{
    public class OvSnapshot
    {
        public int Bar;
        public DateTime Time;
        public decimal Open;
        public decimal High; 
        public decimal Low;
        public decimal Close;
        public decimal Volume;
        public decimal Delta;
        public decimal Ask;
        public decimal Bid;
        public string MarketRegime { get; set; } = "None";

        public decimal MaxCounterShareBull, MaxCounterShareBear;
        public decimal VolBurstZ, CvdImpulse, CvdCoherence;
        public decimal AggPressure, TradeRateZ, Efficiency;
        public decimal BuyTrades, SellTrades, TotalTrades;
        public decimal IttZ;
        public bool SweepUpClosed, SweepDnClosed;

        // NEU: Felder aus MyClusterStatistic
        public decimal CandleDuration;
        public decimal VolPerSecond;
        public decimal EmaVolPerSecond;
        public decimal EmaVolPerSecondStd;
        public decimal CumulativeDelta;
        public decimal CumulativeVolume;
        public decimal BarDeltaPerVolume;
        // NEU: Stacked Imbalance Metriken
        public int StackedBuyImbCount;          // längster Buy-Stack irgendwo im Bar
        public int StackedSellImbCount;         // längster Sell-Stack irgendwo im Bar
        public int StackedBuyImbTopCount;       // Buy-Stack direkt unter High (anchored)
        public int StackedSellImbBottomCount;   // Sell-Stack direkt über Low (anchored)

        // NEU: Parameter zur Nachvollziehbarkeit
        public decimal StackedImbRatioPct;          // z.B. 300 => 3.0x
        public int StackedImbRangeMin;          // Mindestlänge Stack
        public decimal StackedImbMinVolPerLevel;// MinVol pro Level
        public int StackedImbMaxDepthTicks;     // Tiefe für anchored-Check
    }
}


