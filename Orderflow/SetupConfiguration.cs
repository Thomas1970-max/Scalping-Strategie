using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using static MyNamespace.Strategies.Geldfluss3_3;

namespace MyNamespace.Strategies.Orderflow
{
    /// <summary>
    /// Repräsentiert die globale Konfiguration für eine Strategie.
    /// Enthält globale Einstellungen, Handelszeiten, adaptive Parameter und eine Sammlung
    /// von muster-spezifischen Bedingungskonfigurationen.
    /// </summary>
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class SetupConfiguration
    {
        [Browsable(false)]
        public OrderflowThresholds UiThresholds { get; set; } = new OrderflowThresholds();

        // Collections / komplexe Strukturen ausgeblendet im PropertyGrid
        [Browsable(false)]
        public OrderflowThresholds ReversalOrderflowThresholds { get; set; } = new OrderflowThresholds();

        [Browsable(false)]
        public Dictionary<OrderflowPatternType, OrderflowThresholds> PatternDefaultThresholds { get; set; } = new Dictionary<OrderflowPatternType, OrderflowThresholds>();

        [Browsable(false)]
        public List<TradingSession> TradingSessions { get; set; } = new List<TradingSession>();

        // Einfache Flags / Zahlen (ausgeblendet)
        [Browsable(false)]
        public bool EnableTimeFilter { get; set; }

        [Browsable(false)]
        public bool CancelOrdersAtSessionEnd { get; set; } = true;

        [Browsable(false)]
        public bool EnableOrderTimeout { get; set; } = false;

        [Browsable(false)]
        public int OrderTimeoutBars { get; set; } = 0;

        [Browsable(false)]
        public int ReversionBandProximityTicks { get; set; }

        [Browsable(false)]
        public int ReversionMinMoveTicks { get; set; }

        [Browsable(false)]
        public int? ReboundMinMoveTicks { get; set; }

        [Browsable(false)]
        public int? StopPadTicks { get; set; }

        [Browsable(false)]
        public int? DriftToleranceTicks { get; set; }

        [Browsable(false)]
        public int SlTicks { get; set; }

        [Browsable(false)]
        public int MaxRangeBandWidth { get; set; } // In Ticks

        [Browsable(false)]
        public decimal VolPerSecondHighActivityMultiplier { get; set; } // Für highVol Prüfung

        [Browsable(false)]
        public decimal RangeReversalTicks { get; set; } = 4m;

        [Browsable(false)]
        public decimal SmaFlatnessFactor { get; set; } = 0.2m;

        [Browsable(false)]
        public bool AdaptiverOrderflowAktiv { get; set; } = false;

        [Browsable(false)]
        public int AdaptiverLookbackBars { get; set; } = 20;

        [Browsable(false)]
        public double AdaptiverPerzentilStark { get; set; } = 0.80;

        [Browsable(false)]
        public double AdaptiverPerzentilNeutral { get; set; } = 0.60;

        [Browsable(false)]
        public bool UseOvScore { get; set; } = true;

        [Browsable(false)]
        public bool UseSignalCountGate { get; set; } = true;

        [Browsable(false)]
        public bool FeatureGatesAktiv { get; set; } = true;

        [Browsable(false)]
        public decimal MindestSteigungAggPressure { get; set; } = 0.00m;

        [Browsable(false)]
        public int MindestPersistenzBars { get; set; } = 2;

        [Browsable(false)]
        public decimal InflectionMinDelta { get; set; } = 0.05m;

        [Browsable(false)]
        public decimal BurstMinorZ { get; set; } = 1.5m;

        [Browsable(false)]
        public decimal BurstMajorZ { get; set; } = 2.5m;

        [Browsable(false)]
        public int BurstCooldownBars { get; set; } = 4;

        [Browsable(false)]
        public int MinSnapshotsToPersist { get; set; } = 3;

        // Konstruktor zur Initialisierung mit Standard-Condition-Configs
        public SetupConfiguration()
        {
            // no-op
        }
    }
}



