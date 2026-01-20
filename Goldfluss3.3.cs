using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;
using ATAS.Indicators;
using ATAS.Indicators.Technical;         
using ATAS.Strategies.Chart;             
using ATAS.DataFeedsCore;
using Utils.Common.Logging;                
using ATAS.Indicators.Drawing;
using System.Drawing;                    
using System.Windows.Media;
using System.Reflection;
using ATAS.Strategies;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyNamespace.Strategies.TradeManagement;
using MyNamespace.Strategies.Orderflow;
using MyNamespace.Strategies.Models;
using MyNamespace.Strategies.MarketAnalysis;
using OFT.Attributes;
using OFTParameter = OFT.Attributes.ParameterAttribute;
using DevExpress.Xpf.Bars;
using DevExpress.Xpf.Bars.Native;
using System.Diagnostics;
using System.Reflection.Emit;
using static ATAS.Indicators.Technical.SampleProperties;
using static MyNamespace.Strategies.Goldfluss3_3;
using System.IO;
using System.Runtime.ConstrainedExecution;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Threading.Tasks;
using System.Xml.Linq;
using Utils.Common;
using DevExpress.Xpf.Core.Native;
using MyNamespace.Strategies;
using System.Collections.Generic;
using System.Collections;
using static MyNamespace.Strategies.Goldfluss3_3.SetupRegistry;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using DevExpress.Xpf.Editors.Internal;
using System.Net;
using System.Runtime.InteropServices.JavaScript;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Globalization;
using System.Reflection; // F?r den Property-Dump
using System.Collections.Generic; // Neu: F?r das Dictionary


// Stellen Sie sicher, dass dieser Namespace korrekt ist oder ?ndern Sie ihn bei Bedarf
namespace MyNamespace.Strategies
{
    // Sie k?nnen eine FeatureId hinzuf?gen, wenn Sie die Strategie verteilen m?chten
    // [FeatureId("Bitte-Eine-Einzigaertige-GUID-Hier-Einfuegen")] // Ersetzen Sie dies durch eine neue, eindeutige GUID f?r Ihre Strategie



    [DisplayName("Goldfluss 3.3")] // Angepasster Name f?r ATAS
    [Category("Meine Strategien")] // Kategorie in ATAS
    public class Goldfluss3_3 : ChartStrategy
    {
        private readonly SMA _sma = new SMA();
        private readonly VWAP _vwap = new VWAP();

        private readonly ValueDataSeries _vwapSeries = new ValueDataSeries("VWAP")
        {
            Color = System.Drawing.Color.Purple.Convert(), // Choose a distinct color
            VisualType = VisualMode.Line,
            Width = 2
        };

        public bool ShowZoneRects { get; set; } = true;
        public bool ShowZoneCenters { get; set; } = true; // eine Centerline je finaler Zone



        /// <summary>
        /// Eine Hilfsklasse zum Verfolgen signifikanter, noch nicht 'abgearbeiteter' Preis-Levels.
        /// </summary>
        private class TrackedLevel
        {
            public enum LevelRemovalCondition
            {
                /// <summary>Wird sofort bei der ersten Ber?hrung entfernt (IsActive = false gesetzACt).</summary>
                OnTouch,
                /// <summary>Wird nach einer bestimmten Anzahl von Ber?hrungen entfernt.</summary>
                AfterMultipleTouches,
                /// <summary>Wird am Ende des Tages, an dem es erstellt/verfolgt wurde, entfernt.</summary>
                EndOfDay,
                /// <summary>Wird nach MaxDaysForSignificantLevels entfernt, falls nicht bereits durch Preisaktion abgearbeitet.</summary>
                AfterMaxDays
            }

            public decimal Value { get; }
            public DateTime LevelDate { get; }
            public string Label { get; }


            public bool IsActive { get; private set; } // True, wenn das Level noch nicht vom Preis 'abgearbeitet' wurde.
            public LevelRemovalCondition RemovalCondition { get; } // Die Bedingung, unter der das Level entfernt wird
            public int TouchCount { get; private set; } // Z?hlt die Ber?hrungen f?r AfterMultipleTouches
            public int SupportTouchCount { get; private set; } = 0;
            public int ResistanceTouchCount { get; private set; } = 0;
            public int LastTouchBarIndex { get; set; }
            public TrackedLevel(decimal value, DateTime levelDate, string label, LevelRemovalCondition removalCondition)
            {
                Value = value;
                LevelDate = levelDate;
                Label = label;
                IsActive = true;
                this.RemovalCondition = removalCondition;
                this.TouchCount = 0;
                ResistanceTouchCount = 0;
                LastTouchBarIndex = -1;

            }


            public void MarkAsWorkedOff()
            {
                IsActive = false;
            }

            public void IncrementSupportTouch()
            {
                SupportTouchCount++;
                TouchCount++; // Optional: Gesamtz?hler erh?hen
            }

            public void IncrementResistanceTouch()
            {
                ResistanceTouchCount++;
                TouchCount++; // Optional: Gesamtz?hler erh?hen
            }
        }


        private readonly List<TrackedLevel> _untouchedLevels = new List<TrackedLevel>();

        // NEU: Ein Dictionary zum Verwalten der gezeichneten Linien f?r signifikante Levels
        private readonly Dictionary<string, HorizontalLine> _significantLines = new Dictionary<string, HorizontalLine>();

        
        private DateTime _lastProcessedDay = DateTime.MinValue;

       


        // Diese Instanz liefert uns die statischen Werte des Vortages.
        private readonly DynamicLevels _dailyLevels = new DynamicLevels();
        private readonly DailyLines _dailyLines = new DailyLines();


        private readonly Pivots _pivots = new Pivots();


        private MyClusterStatistic _myClusterStatistic;

        const int VpsSmaLookback = 20;


        // DataSeries zum Speichern unserer berechneten Werte
        private Dictionary<int, decimal> _volBurstZ = new Dictionary<int, decimal>();
        private Dictionary<int, decimal> _cvdImpulse = new Dictionary<int, decimal>();
        private Dictionary<int, decimal> _cvdCoherence = new Dictionary<int, decimal>();
        private Dictionary<int, decimal> _aggPressure = new Dictionary<int, decimal>();
        private Dictionary<int, decimal> _tradeRateZ = new Dictionary<int, decimal>();
        private Dictionary<int, decimal> _efficiency = new Dictionary<int, decimal>();
        // F?r Actual/Bid/Buy/Sell Trades Daten, die f?r CounterDeltaShare verwendet werden k?nnen
        private Dictionary<int, decimal> _buyTradesSeries = new Dictionary<int, decimal>();
        private Dictionary<int, decimal> _sellTradesSeries = new Dictionary<int, decimal>();
        private Dictionary<int, decimal> _totalTradesSeries = new Dictionary<int, decimal>();
        private Dictionary<int, decimal> _maxCounterShareBull = new Dictionary<int, decimal>();
        private Dictionary<int, decimal> _maxCounterShareBear = new Dictionary<int, decimal>();

        // ITT Z
        
        private Dictionary<int, decimal> _ittZ_raw = new();   // ms/Trade Z-Score
        private Dictionary<int, decimal> _ittZ_bull = new();  // Vorzeichen gedreht (Tempo-bull)
        private Dictionary<int, decimal> _ittZ_bear = new();  // Tempo-bear (optional)

        // Sweep
        private Dictionary<int, int> _sweepDir = new();       // +1=Up, -1=Down, 0=None
        private Dictionary<int, bool> _sweepUp = new();
        private Dictionary<int, bool> _sweepDn = new();


        private readonly ValueDataSeries _imbalanceSeries = new("Buy-Sell Imbalance") { Color = Colors.Blue };
        private readonly ValueDataSeries _entrySignalSeries = new("Entry Signal") { Color = Colors.Orange, VisualType = VisualMode.Square, Width = 3, ShowZeroValue = false };


        // Tempor?re Z?hler f?r die aktuelle Kerze
        private int _currentBarBuyTrades = 0;
        private int _currentBarSellTrades = 0;
        private int _lastCalculatedBar = -1;

        // Interne Zust?nde
        private decimal _cvdCum = 0m;
        private decimal _prevClose = 0m;
        private decimal _prevCVD = 0m;


        // Rollende Pufferspeicher
        private readonly Queue<decimal> _volWin = new();
        private readonly Queue<decimal> _tradeRateWin = new();
        private readonly Queue<(decimal dCVD, decimal dPx)> _cohWin = new();
        private readonly Queue<decimal> _erAbsIncr = new(); // |?Price| f?r ER
        private readonly Queue<decimal> _ittWin = new();

        // Diese Variablen halten die Werte f?r den abgeschlossenen Vortag (PDH, PDL, PDC)
        private decimal _previousDayHigh;
        private decimal _previousDayLow;
        private decimal _previousDayClose;
        private decimal _previousDayOpen;

        private decimal _currentDayOpen;
        private decimal _currentDayHigh;
        private decimal _currentDayLow;
        private decimal _currentDayClose;

        private int _lastSessionStartBar = -1;

        


        

        private const int TradeRateZ_Lookback = 20;
        private readonly Dictionary<(SetupKind setup, MarketRegime regime), OrderflowThresholds> _adaptiveCache = new();




        

        private OvSnapshot ovSnapshot;
        private bool _hasOvLastClosed;

        

        public enum SweepSide { Up, Down }

        public struct ClusterAgg
        {
            public decimal Price;
            public decimal Ask; // Market Buys
            public decimal Bid; // Market Sells
        }

        // Feintuning (US-Range 4/2 Ticks)
        public double Sweep_MaxSeconds = 1.2;   // Fenstergr??e
        public int Sweep_MinLevels = 6;     // min. Ticks in Sweep-Richtung
        public int Sweep_MaxOppRetraceLevels = 1;     // max. Gegenlevels
        public decimal Sweep_MinAggRatio = 0.70m; // Gesamt Ask/(Ask+Bid) bzw. Bid/(Ask+Bid)
        public decimal Sweep_LevelDominance = 0.70m; // Dominanz pro Level am Pfad
        public decimal Sweep_PathPurityFrac = 0.75m; // Anteil dominanter Levels am Pfad
        public decimal Sweep_ZeroOppFracMin = 0.15m; // min. Anteil Levels mit ~0 Gegenseite (optional)
        public decimal Sweep_ImbalanceRatioMin = 2.5m;  // stacked imbalance Schwelle pro Level (optional)
        public int Sweep_ImbalanceRunMin = 3;     // min. konsekutive Imbalance-Levels
        public bool UseSpeedGate = true;
        public decimal Sweep_MinTradeRateZ = 1.5m;

        public class LevelsSnapshot
        {
            // Previous day
            public decimal PreviousDayHigh { get; set; } = 0m;
            public decimal PreviousDayLow { get; set; } = 0m;
            public decimal PreviousDayClose { get; set; } = 0m;
            public decimal PreviousDayOpen { get; set; } = 0m;
            public decimal PreviousDayPOC { get; set; } = 0m;
            public decimal PreviousDayVAH { get; set; } = 0m;
            public decimal PreviousDayVAL { get; set; } = 0m;

            // Current/day-in-progress
            public decimal CurrentPOC { get; set; } = 0m;
            public decimal CurrentVAH { get; set; } = 0m;
            public decimal CurrentVAL { get; set; } = 0m;


            // neu: Block-Flags
            public bool IsBlockedLong { get; set; }
            public bool IsBlockedShort { get; set; }

            // optional: Positions der letzten Blocker
            public decimal? LastBlockResistance { get; set; }
            public decimal? LastBlockSupport { get; set; }

            // Runde Marken: genau zwei m?gliche Werte (below / above). 0 = nicht vorhanden
            public decimal? RoundLevelBelow { get; set; }
            public decimal? RoundLevelAbove { get; set; }      // h?here runde Marke (levelAbove)

            // Pivot / Support / Resistance
            public decimal PP { get; set; } = 0m;
            public decimal S1 { get; set; } = 0m;
            public decimal S2 { get; set; } = 0m;
            public decimal S3 { get; set; } = 0m;
            public decimal R1 { get; set; } = 0m;
            public decimal R2 { get; set; } = 0m;
            public decimal R3 { get; set; } = 0m;

            // M-levels (M1..M4)
            public decimal M1 { get; set; } = 0m;
            public decimal M2 { get; set; } = 0m;
            public decimal M3 { get; set; } = 0m;
            public decimal M4 { get; set; } = 0m;

            // All historical session highs / lows (neueste zuerst)
            public List<SessionLevel> SessionHighs { get; } = new List<SessionLevel>();
            public List<SessionLevel> SessionLows { get; } = new List<SessionLevel>();



            // Fallback / Extras f?r beliebige Labels (case-insensitive)
            public Dictionary<string, decimal> Extra { get; } = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> Meta { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Konfigurationsobjekt f?r das Scoring je Setup
            
            

            public bool TryGetExtra(string key, out decimal value)
            {
                return Extra.TryGetValue(key, out value);
            }

            public bool TryGetMeta(string key, out string? value)
            {
                return Meta.TryGetValue(key, out value);
            }

            public string GetMetaOrDefault(string key, string defaultValue = "")
                => TryGetMeta(key, out var v) && !string.IsNullOrEmpty(v) ? v : defaultValue;
            public string GetExtraAsString(string key, string defaultValue = "")
                => TryGetExtra(key, out var val) ? val.ToString(System.Globalization.CultureInfo.InvariantCulture) : defaultValue; 

            public bool HasAnyPOC => CurrentPOC > 0m || PreviousDayPOC > 0m;
            public SessionLevel? LatestSessionHigh => SessionHighs.Count > 0 ? SessionHighs[0] : null;
            public SessionLevel? LatestSessionLow => SessionLows.Count > 0 ? SessionLows[0] : null;
        }

        // Oben in der Klasse: Private Felder (ersetzen deinen _vwap)
       
        
        private VwapSnapshot _prevSessionSnapshot;  // F?r PreviousDay (letzter Session-Wert)

        // Defaults (wie ATAS)
        private decimal _stdev = 1m;    // Band1
        private decimal _stdev1 = 2m;   // Band2
        private decimal _stdev2 = 3m;   // Band3
        private VWAPMode _twapMode = VWAPMode.VWAP;  // Default VWAP
        private VolumeType _volumeMode = VolumeType.Total;  // Default Total

        // Enum-Definitionen (kopiere aus ATAS-Code, falls nicht vorhanden)
        public enum VWAPMode { VWAP = 0, TWAP = 1 }
        public enum VolumeType { Total, Bid, Ask }
        private ValueDataSeries _vwapLineSeries;     // VWAP-Linie
        private ValueDataSeries _upperBand1Series;  // Upper Band1
        private ValueDataSeries _lowerBand1Series;  // Lower Band1
        private ValueDataSeries _upperBand2Series;  // Upper Band2
        private ValueDataSeries _lowerBand2Series;  // Lower Band2
        private ValueDataSeries _upperBand3Series;  // Upper Band3
        private ValueDataSeries _lowerBand3Series;  // Lower Band3
        private ValueDataSeries _totalVolToCloseSeries;
        private ValueDataSeries _totalVolumeSeries;
        private ValueDataSeries _sumSrcSrcVolSeries;

        private int _targetBar = 0;  // Session-Start-Bar (wie Original)
        private int _zeroBar = 0;    // Aktueller Reset-Bar
        private string _periodType = "Daily";
        public class VwapSnapshot
        {
            public decimal Current { get; set; }
            public decimal LowerBand3 { get; set; }
            public decimal UpperBand3 { get; set; }
            public decimal LowerBand2 { get; set; }
            public decimal UpperBand2 { get; set; }
            public decimal LowerBand1 { get; set; }
            public decimal UpperBand1 { get; set; }

            // PreviousDay (alle mit Defaults)
            public decimal PreviousDayCurrent { get; set; } = 0m;
            public decimal PreviousDayLowerBand3 { get; set; } = 0m;
            public decimal PreviousDayUpperBand3 { get; set; } = 0m;
            public decimal PreviousDayLowerBand2 { get; set; } = 0m;
            public decimal PreviousDayUpperBand2 { get; set; } = 0m;
            public decimal PreviousDayLowerBand1 { get; set; } = 0m;
            public decimal PreviousDayUpperBand1 { get; set; } = 0m;

            private bool _isValid = false;
            public bool IsValid
            {
                get { return _isValid; }
                set { _isValid = value; }
            }

            // Erweiterte Validate: Checks alle Bands + Prev
            public void Validate(bool allowZeroForReplay = false)
            {
                // NaN-Checks (erweitert auf alle Bands)
                bool anyNaN = double.IsNaN((double)Current) ||
                              double.IsNaN((double)UpperBand1) || double.IsNaN((double)LowerBand1) ||
                              double.IsNaN((double)UpperBand2) || double.IsNaN((double)LowerBand2) ||
                              double.IsNaN((double)UpperBand3) || double.IsNaN((double)LowerBand3);

                // Positive Checks
                bool allPositiveCurrent = Current > 0m && UpperBand1 > 0m && LowerBand1 > 0m;
                bool allPositive = allPositiveCurrent &&
                                   UpperBand2 > 0m && LowerBand2 > 0m &&
                                   UpperBand3 > 0m && LowerBand3 > 0m;

                // Hierarchy-Checks (korrigiert)
                // B1: Upper1 > Lower1
                bool band1Valid = UpperBand1 > LowerBand1;
                // B2: Upper2 > Lower2 und Upper2 ?ber Upper1, Lower2 unter Lower1
                bool band2Valid = UpperBand2 > LowerBand2 &&
                                  UpperBand2 > UpperBand1 &&
                                  LowerBand2 < LowerBand1;
                // B3: Upper3 > Lower3 und Upper3 ?ber Upper2, Lower3 unter Lower2
                bool band3Valid = UpperBand3 > LowerBand3 &&
                                  UpperBand3 > UpperBand2 &&
                                  LowerBand3 < LowerBand2;

                bool validHierarchy = band1Valid && band2Valid && band3Valid;

                // Prev-Validit?t (wie gehabt)
                bool prevAllPositive = PreviousDayCurrent > 0m &&
                                       PreviousDayUpperBand1 > 0m && PreviousDayLowerBand1 > 0m &&
                                       PreviousDayUpperBand2 > 0m && PreviousDayLowerBand2 > 0m &&
                                       PreviousDayUpperBand3 > 0m && PreviousDayLowerBand3 > 0m;

                bool prevHierarchy = PreviousDayUpperBand1 > PreviousDayLowerBand1 &&
                                     PreviousDayUpperBand2 > PreviousDayLowerBand2 &&
                                     PreviousDayUpperBand3 > PreviousDayLowerBand3;

                bool prevValid = prevAllPositive && prevHierarchy;

                // Final IsValid
                if (anyNaN)
                {
                    _isValid = false;
                }
                else if (allowZeroForReplay &&
                         Current == 0m &&
                         UpperBand1 == 0m && LowerBand1 == 0m &&
                         UpperBand2 == 0m && LowerBand2 == 0m &&
                         UpperBand3 == 0m && LowerBand3 == 0m)
                {
                    _isValid = true;  // Replay: Erlaube flachen Snapshot
                }
                else if (allPositive && validHierarchy)
                {
                    _isValid = true;  // Voll valid
                }
                else if (!allPositive && prevValid)
                {
                    _isValid = true;  // Fallback zu Prev
                }
                else
                {
                    _isValid = false;
                }
            }

            // Neu: Sichere Klon-Methode (f?r Prev-Snapshot)
            public VwapSnapshot Clone()
            {
                return new VwapSnapshot
                {
                    Current = this.Current,
                    LowerBand3 = this.LowerBand3,
                    UpperBand3 = this.UpperBand3,
                    LowerBand2 = this.LowerBand2,
                    UpperBand2 = this.UpperBand2,
                    LowerBand1 = this.LowerBand1,
                    UpperBand1 = this.UpperBand1,
                    PreviousDayCurrent = this.PreviousDayCurrent,
                    PreviousDayLowerBand3 = this.PreviousDayLowerBand3,
                    PreviousDayUpperBand3 = this.PreviousDayUpperBand3,
                    PreviousDayLowerBand2 = this.PreviousDayLowerBand2,
                    PreviousDayUpperBand2 = this.PreviousDayUpperBand2,
                    PreviousDayLowerBand1 = this.PreviousDayLowerBand1,
                    PreviousDayUpperBand1 = this.PreviousDayUpperBand1,
                    IsValid = this.IsValid
                };
            }

            // Erweiterter ToString (f?r Logs)
            public override string ToString()
            {
                decimal width1 = UpperBand1 - LowerBand1;
                decimal width2 = UpperBand2 - LowerBand2;
                decimal width3 = UpperBand3 - LowerBand3;
                bool prevValid = PreviousDayCurrent > 0m && PreviousDayUpperBand1 > PreviousDayLowerBand1;
                return $"VwapSnapshot: Current={Current:F2}, B1(U={UpperBand1:F2}/L={LowerBand1:F2},W={width1:F2}) | B2(W={width2:F2}) | B3(W={width3:F2}), IsValid={IsValid}, PrevValid={prevValid} (Current={PreviousDayCurrent:F2})";
            }
        }

        struct ClusterLevel
        {
            public decimal Price;
            public decimal TotalVol;
            public decimal AskVol;
            public decimal BidVol;
        }
        // Konfiguration/Schwellen (je Instrument kalibrierbar)
        public class PathConfig
        {
            public int RequiredLVNsInPath { get; set; } = 1;         // mind. 1 LVN-Korridor im Pfad
            public bool RelaxVAEdgesWhenOutsideValue { get; set; } = true; // VA-Kanten au?erhalb Value lockern
            public decimal HVNStrengthVsPOC { get; set; } = 0.50m;    // HVN gilt als stark, wenn Vol >= 50% von POCVol
            public decimal HVNStrengthVsMedian { get; set; } = 1.20m; // HVN stark, wenn Vol >= 1.2 * MedianVol (falls vorhanden)
            public decimal MinProminenceVsMedian { get; set; } = 0.15m; // Prominenz >= 0.15 * MedianVol (falls vorhanden)
            public int MinBlockerDistanceTicksVsRisk { get; set; } = 1; // Blocker muss weiter entfernt sein als RiskTicks * Faktor (1 = gleich Risk)
        }
        public class MicroComposite
        {
            public decimal POC;
            public decimal POCVol;
            public decimal VAH;
            public decimal VAL;

            public List<decimal> HVNs { get; set; } = new();
            public List<decimal> LVNs { get; set; } = new();

            public decimal TotalVol;

            // Immer initialisieren
            public List<(decimal Start, decimal End)> HVNZones { get; set; } = new();
            public List<(decimal Start, decimal End)> LVNZones { get; set; } = new();

            public SortedDictionary<decimal, decimal> LevelVols { get; set; } = new();
        }

        private bool _mcDirty = true;
        
        const bool PreferRightOnTie = true;    // Gleichstand in der VA-Expansion: rechts bevorzugen
        const bool POCPreferHigherPrice = true;// Bei POC-Tie: h?heren Preis bevorzugen?
        const bool UseDenseLadder = false;     // true: l?ckenloses Tick-Raster mit 0-Volumen



        // Optionale Konfigurationsfelder (falls nicht schon vorhanden)
        private int _mcSmoothTicks = 3;   // Gl?ttung ?ber ?3 Ticks
        private int _mcTopNPeaks = 6;     // max. Anzahl HVN-/LVN-Zentren

        SortedDictionary<decimal, decimal> _mcHist = new();
        Queue<int> _mcBars = new();
        
        int SmoothingTicks = 1;     // Gl?ttung
        int TopNPeaks = 4;          // Anzahl HVNs/LVNs

        private MicroComposite _currentMC;
        private MicroComposite _prevMC;
        private PathConfig GetPathConfig()
        {
            return new PathConfig
            {
                RequiredLVNsInPath = RequiredLVNsInPath,
                RelaxVAEdgesWhenOutsideValue = RelaxVAEdgesWhenOutsideValue,
                HVNStrengthVsPOC = HVNStrengthVsPOC,
                HVNStrengthVsMedian = HVNStrengthVsMedian,
                MinProminenceVsMedian = MinProminenceVsMedian,
                MinBlockerDistanceTicksVsRisk = MinBlockerDistanceTicksVsRisk
            };
        }
        
        private bool _deferManagerInit = false;

        // Trading hours control
        private bool _isInsideTradingHours = false;

        private const decimal VOL_LOW_MULT = 0.90m;  // unter 90% des EMA => eher langsam
        private const decimal VOL_HIGH_MULT = 1.30m;  // ab 130% des EMA => schnell
        private const decimal VOL_ABS_FLOOR = 0.50m;  // Mindestaktivit?t, um "schnell" zu z?hlen
        // Z-Score Grenzen(optional, falls EWMA-Std verf?gbar)
        private const decimal VOL_SLOW_Z = -0.40m;
        private const decimal VOL_FAST_Z = 0.80m;
        // Aktivit?t ?ber Trades/Sekunde (aus deinem TradeRate)
        private const decimal TR_SLOW_MAX = 12m;    // <= 12 Trades/s => langsam
        private const decimal TR_FAST_MIN = 30m;    // >= 30 Trades/s => schnell
        // Bar-Abschlussgeschwindigkeit (Sekunden pro Bar)
        private const decimal BARSEC_SLOW_MIN = 10m;   // >= 10s/Bar => langsam
        private const decimal BARSEC_FAST_MAX = 3m;    // <= 3s/Bar  => schnell

        // Orders & Position state
        private Order? _pullbackOrder;
        private Order? _entryOrder;
        private Order? _marketOrder;
        private Order? _tpOrder;
        private Order? _slOrder;
        

        private bool HasActiveEntryOrder => _entryOrder != null;
        private bool _positionOpen = false;
        private bool _isExitPlacementPending = false;
        private int _entryBarIndex = -1;
        private int _armedBarIndex = -1;   // Bar, an dem das Arming stattfand
        private int _pullbackBarIndex = -1;
        private int _fillBarIndex = -1;
        private decimal _entryFillPrice = 0m;
        private decimal _currentAtrValue;
        private decimal _currentvwap;
        private decimal _prevvwap;
        private decimal _tickSize;
        // Progress tracking for BE/Trail

        private decimal _currentTriggerLevel;

        private bool _signalCheckedForThisBarFirstTick = false;


        // Globale Variablen (in deiner cBot/Indicator-Klasse)
        private bool _orderTimeoutEnabled;   // Gespeichert pro Order (setup-spezifisch)
        private int _orderTimeoutBars;       // Gespeichert pro Order (setup-spezifisch)
        private bool _orderEnableTimeFilter;                  // Gespeichert pro Order
        private List<TradingSession> _orderTradingSessions;   // Neu: Liste der Sessions pro Order (ersetzt _orderSessionEndTime)
        private readonly List<TradingSession> _globalSessions = new List<TradingSession>();
        private bool _orderCancelAtSessionEnd;                // Gespeichert pro Order
        private bool _isInsideOrderSession;                   // Trackt, ob in IRGENDEINER Session
        public enum SetupKind { None = 0, S1 = 1, S2 = 2, S3 = 3, S4 = 4, S5 = 5, S6 = 6 /* ... */ }
        private SetupKind _currentTimeoutOwnerSetup = SetupKind.None;
        private SetupKind _currentSetup = SetupKind.None;
        // Pro Setup: optionaler adaptiver Builder (kann null sein)
        private readonly Dictionary<SetupKind, Func<MarketRegime, OrderflowThresholds>?> _adaptiveBuilderBySetup = new();

        // Pro Setup: Basis-Thresholds (aus OnInitialize)
        private readonly Dictionary<SetupKind, OrderflowThresholds?> _baseThBySetup = new();

       

        // Optional: pro Setup Policy-Flag (z. B. VolBurst zwingend)
        private readonly Dictionary<SetupKind, bool> _requireVolBurstBySetup = new();

        
        private class SetupRuntime
        {
            public int BestScore;
            public bool ApprovedHys;
            public OrderDirections BestDir;
            public string Tier = "";
        }
        private readonly Dictionary<SetupKind, SetupRuntime> _rtBySetup = new();
        private TimeSpan? GlobalSession1StartTime { get; set; }
        private TimeSpan? GlobalSession1EndTime { get; set; }
        private TimeSpan? GlobalSession2StartTime { get; set; }
        private TimeSpan? GlobalSession2EndTime { get; set; }


        // Diese Variablen sind "Arbeitsvariablen" f?r den laufenden Tag

        private DateTime _lastPivotLevelAddDay = DateTime.MinValue;

        private RenderFont _axisFont = new("Arial", 11F, System.Drawing.FontStyle.Regular, GraphicsUnit.Point, 204);
        private RenderPen _renderPen = new(System.Drawing.Color.CornflowerBlue, 2);
        private System.Drawing.Color _axisTextColor = System.Drawing.Color.White;
        private RenderFont _font = new("Arial", 10);
        private System.Drawing.Color _lineColor = System.Drawing.Color.CornflowerBlue;
        private int _width = 2;
        private int Length = 300;
        private System.Drawing.Color _textColor = System.Drawing.Color.CornflowerBlue;


        private MyNamespace.Strategies.TradeManagement.BreakEvenManager? _beManager;
        private MyNamespace.Strategies.TradeManagement.TrailingStopManager? _trailManager; // falls vorhanden
        private decimal _bestSinceEntry;
        private bool _isLongTrade;
        private bool _managersInitialized = false;
        private int _lastProcessedBarIndex = -1;
        private int _lastTimeoutCheckBarIndex = -1;
        private int _lastResearchBar = 0;
        private int _lastTradeBar = 0;

        private bool _isBreakEvenCompleted = false;
        private int _breakEvenLevelReached = 0;

        // Live/History-Gates
        private int _liveStartBar = -1;
        private bool _entryLogicLockedUntilLive = true;
        private bool _onlyFromLive = false; // falls du eine Option daf?r hast
        private int _lastEvalBar = -1;    // Entprellen: nur 1x pro Bar
        private int _lastSeenBar = -1;    // h?chster bisher gesehener Bar-Index
        private int _lastProcessedBar = -1;
        
        
        private int Coherence_Lookback = 50;   // Fenster f?r Korrelation
        private int ER_Lookback = 20;          // Fenster f?r Efficiency Ratio
        private int _counterDeltaShareLookback = 5;
        // Speichert den Bar-Index, dessen High/Low f?r den letzten Trailing-Stop verwendet wurde.
        private int _lastSlSetBarIndex = -1;
        private int _lastBarCalculatedForVolumeProfile = -1;


        private int _lastBarIdx = -1;  // Trackt letzte verarbeitete Bar
        public enum WeightProfile { Conservative, Balanced, Aggressive }

        private decimal _currentBarVolPerSecond;
        private decimal _currentBarAvgVolPerSecond;
        
        private bool _historicalAnalysisPerformed = false; // Stellt sicher, dass die Analyse nur einmal l?uft


        private bool _isPullbackMode = false; // Flag, ob wir in Pullback-Trailing sind
        private decimal _pullbackTriggerPrice; // Speichert den initialen Limit-Preis f?r Ber?hrungspr?fung

        // =================================================================
        // Klassenfelder f?r den Strategie-Zustand
        // =================================================================
        private VwapSnapshot _currentVwapSnapshot;
        private VwapSnapshot _prevVwapSnapshot;
        private VwapSnapshot _previousDayVwapSnapshot;
        private LevelsSnapshot _currentLevelsSnapshot;
        private SetupParams _activeTradeSetupParams;
        private TpSlCalculator _tpSlCalculator;
        private TpSlResult? _lastTpSlResult;
        private ManagementPlan _currentManagementPlan;
        // Manager f?r die dynamische Anpassung
        private BreakEvenManager _breakEvenManager;
        private TrailingStopManager _trailingManager;
        private IndicatorCandle _currentBar;
        private OrderDirections _currentTradeDirection;
        private ATAS.Indicators.IndicatorCandle _currentCandleData;

        private SetupConfiguration _setup1Config;
        private SetupConfiguration _setup2Config;
        private SetupConfiguration _setup3Config;
        private SetupConfiguration _setup4Config;
        private SetupConfiguration _setup5Config;
        private SetupConfiguration _setup6Config;

        private MarketRegime _currentRegime = MarketRegime.Normal;

        
        // =========================================================================
        // Stacked-Imbalance 
        // =========================================================================
        public enum ImbalanceSide { BuyAskBid, SellBidAsk }

        public struct StackedImbParams
        {
            // identisch zum ATAS-Verst?ndnis: Prozent! 300 => Faktor 3.0
            public decimal ImbalanceRatioPct;      // z.B. 300
            public int ImbalanceRangeMin;      // z.B. 3
            public int ImbalanceVolumeMin;     // z.B. 30
            public bool IgnoreZeroValues;      // z.B. false
            public int MaxDepthTicksAnchored;  // z.B. 6 (nur f?r top/bottom-anchored)
        }

        // Ergebnis-Serien (decimal f?r Konsistenz mit GetOr0)
        private readonly Dictionary<int, decimal> _stackedBuyImbCount = new(); // l?ngster Buy-Stack irgendwo im Bar
        private readonly Dictionary<int, decimal> _stackedSellImbCount = new(); // l?ngster Sell-Stack irgendwo im Bar
        private readonly Dictionary<int, decimal> _stackedBuyImbTopCount = new(); // direkt unter High (anchored)
        private readonly Dictionary<int, decimal> _stackedSellImbBottomCount = new(); // direkt ?ber Low (anchored)

        // --- Stacked-Imbalance Parameter (Defaults analog ATAS) ---
        private decimal _imbalanceRatioPct = 300; // 300% => Faktor 3.0
        private int _imbalanceRangeMin = 2;   // Mindestl?nge eines zusammenh?ngenden Stacks
        private int _imbalanceVolumeMin = 30;  // Mindestvolumen pro Level
        private bool _imbIgnoreZeroValues = false;
        private int _imbMaxDepthTicksAnchored = 3;   // f?r Range-Bars (5?8 Ticks) typ. 5?6



        private ATAS.Indicators.IndicatorCandle? AsIndicatorCandle(object c) => c as ATAS.Indicators.IndicatorCandle;
        private ATAS.Indicators.Candle? AsBaseCandle(object c) => c as ATAS.Indicators.Candle;
        private sealed class CandleSnap
        {
            public DateTime Time;
            public DateTime LastTime; // falls nicht verf?gbar, sp?ter berechnet/ersetzt
            public decimal Volume;
            public decimal High;
            public decimal Low;
        }

        // ==== Dynamic VWAP Caps (ES) ====
        private decimal _softCapEma = 0m;
        private decimal _hardCapEma = 0m;
        private decimal _emaVolLong = 0m;      // langsame Referenz f?r Vol/sec-EMA

        // Baselines in Punkten
        private const decimal SOFT_BASE_FAST = 12m;
        private const decimal SOFT_BASE_NORM = 18m;
        private const decimal SOFT_BASE_SLOW = 24m;
        private const decimal HARD_BASE = 36m;

        // Clamps in Punkten
        private const decimal SOFT_MIN = 10m, SOFT_MAX = 26m;
        private const decimal HARD_MIN = 28m, HARD_MAX = 46m;

        // Skalierung (je h?her Vol ? Caps kleiner)
        private const decimal SOFT_SCALE = 0.35m;
        private const decimal HARD_SCALE = 0.25m;

        // Gl?ttungen
        private const decimal CAPS_EMA_ALPHA = 0.20m;
        private const decimal EMA_LONG_ALPHA = 0.02m;

       

        private CandleSnap ToSnap(IndicatorCandle ic)
        {
            // Die meisten Builds haben diese Properties direkt am IndicatorCandle:
            // Time, LastTime, Volume, High, Low
            var snap = new CandleSnap
            {
                Time = ic.Time,
                Volume = ic.Volume,
                High = ic.High,
                Low = ic.Low,
                LastTime = ic.Time // Default, ggf. gleich korrigieren
            };

            // Falls deine IndicatorCandle kein LastTime hat, lassen wir es bei Time
            // und ermitteln die Dauer sp?ter mit einer eigenen Serie.
            var p = ic.GetType().GetProperty("LastTime");
            if (p != null)
            {
                var lt = p.GetValue(ic);
                if (lt is DateTime dt)
                    snap.LastTime = dt;
            }

            return snap;
        }

        // =========================================================================
        // ATAS BENUTZERKONFIGURIERBARE PARAMETER F?R "COMMON ENTRY CONDITIONS"
        // Diese werden im ATAS Properties Fenster unter "Common Conditions - AggPressure" angezeigt
        // =========================================================================
        

        [Category("Konfiguration")]
        [DisplayName("Setup Configuration")]
        [Description("Alle orderflow-bezogenen Einstellungen")]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public SetupConfiguration SetupConfig { get; set; } = new SetupConfiguration();

        [Category("Reversal Settings")]
        [DisplayName("Reversal Thresholds")]
        [Description("Reversal-spezifische Orderflow-Schwellenwerte f?r ReversalBounce Pattern")]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public OrderflowThresholds ReversalThresholds { get; set; } = new OrderflowThresholds();


        [Display(Name = "Enable Order Timeout", GroupName = "Order Timeout", Order = 100)]
        [Description("Aktiviert das automatische L?schen von Entry-Orders nach einer bestimmten Anzahl von Bars.")] // F?ge Beschreibung hinzu
        public bool EnableOrderTimeout { get; set; } = true;

        [Display(Name = "Timeout (Bars)", GroupName = "Order Timeout", Order = 110)]
        [Range(1, 100, ErrorMessage = "Please enter a value between 1 and 100.")]
        [Description("Anzahl der Bars, nach denen eine nicht gef?llte Entry-Order gel?scht wird.")] // F?ge Beschreibung hinzu
        public int OrderTimeoutBars { get; set; } = 2;


        [OFTParameter]
        [Category("Orderflow - MarketStateEngine")]
        [DisplayName("Trend Body (Ticks)")]
        [Description("Body-Gr??e (in Ticks), die als Trend-Bar gewertet wird (z.B. 6).")]
        [DefaultValue(6)]
        [Range(1, 100)]
        public int Parameter_TrendBodyTicks { get; set; } = 6;

        [OFTParameter]
        [Category("Orderflow - MarketStateEngine")]
        [DisplayName("Reversal Body (Ticks)")]
        [Description("Body-Gr??e (in Ticks), die als Reversal-Bar gewertet wird (z.B. 9).")]
        [DefaultValue(9)]
        [Range(1, 200)]
        public int Parameter_ReversalBodyTicks { get; set; } = 9;

        [OFTParameter]
        [Category("Orderflow - MarketStateEngine")]
        [DisplayName("Body Tick Tolerance")]
        [Description("Toleranz (in Ticks) f?r Rundung/Artefakte, z.B. 0.25.")]
        public decimal Parameter_BodyTickTolerance { get; set; } = 0.25m;

        [OFTParameter]
        [Category("CSV Export")]
        [DisplayName("CSV Export aktivieren")]
        [Description("Aktiviert/deaktiviert den CSV-Export der OvSnapshots")]
        public bool EnableCsvExport { get; set; } = true;

        [OFTParameter]
        [Category("CSV Export")]
        [DisplayName("T?gliche CSV-Dateien")]
        [Description("Erstellt f?r jeden Handelstag eine separate CSV-Datei")]
        public bool UseDailyCsvFiles { get; set; } = true;

        [OFTParameter]
        [Category("CSV Export")]
        [DisplayName("CSV-Dateien ?berschreiben")]
        [Description("??berschreibt existierende CSV-Dateien statt anzuh?ngen (n?tzlich f?r Backtests)")]
        public bool OverwriteExistingCsv { get; set; } = false;


        // ... F?GE HIER WEITERE GLOBALE [OFTParameter]-PROPERTIES F?R ALLE COMMON CONDITIONS HINZU,
        // DIE DU ?BER DAS ATAS-UI STEUERN M?CHTEST (z.B. VolBurst, CvdImpulseRisingSequence etc.)
        // Stelle sicher, dass f?r jede "UseXyz"-Flag in SetupConditionConfig ein
        // entsprechender "Parameter_UseXyz" hier definiert ist, wenn es global einstellbar sein soll.
        // =========================================================================


        // =========================================================================
        // ALLES, WAS BLEIBT | Anfang
        // =========================================================================
        private ModeSpec _currentModeSpec; // Beispiel: muss in Ihrer Strategie gesetzt werden               
        private Dictionary<OrderflowPatternType, OrderflowThresholds> _patternDefaultThresholds;

        private SqueezeMomentumCalculator _squeezeCalc;

        private decimal _pdPOC, _pdVAH, _pdVAL;
        private ResearchCollector _research;

        private BackgroundCsvWriter _csvWriter;
        private string _csvPath;
        private string _currentCsvDate;
        private string _storedBacktestDate; // Gespeichertes Backtest-Datum wie im ResearchCollector

        private const string CsvHeader =
        "BarIndex,Time_ISO,Instrument,Open,High,Low,Close,Volume,Delta,Ask,Bid,MaxCounterShareBull,MaxCounterShareBear,VolBurstZ,CvdImpulse,CvdCoherence,AggPressure,TradeRateZ," +
        "Efficiency,BuyTrades,SellTrades,TotalTrades,IttZ,SweepUp,SweepDn,StackedBuyImbCount,StackedSellImbCount,StackedBuyImbTopCount,StackedSellImbBottomCount,StackedImbRatioPct,StackedImbMinVolPerLevel," +
        "StackedImbRangeMin,StackedImbMaxDepthTicks,CandleDuration,VolPerSecond,EmaVolPerSecond,EmaVolPerSecondStd,CumulativeDelta,CumulativeVolume,MarketRegime,BarDeltaPerVolume," +
        // neu: History/CreatedAtUtc
        "HistoryVersion,ThresholdsCreatedAtUtc,ThresholdsSnapshot,PrunerNullifiedFields,FinalDecision,MetCriteriaList,MetHardList,MetRelList,MetCriteriaCount,PossibleCriteriaCount,DetectReason,CompositeScore,CvdMean30,CvdStd30,VolBurstMax3,VolBurstAge,InflectionCount3,SoftVolBurstFlag,SoftNegativeFlag," +
        // neu: Pattern- und OfFeatures-Felder (Text/Num)
        "PatternType,PatternDirection,PatternCategory,PatternLevel,PatternConfidence,PatternScore,PatternCombinedConf,VolBurstClass,VolBurstCooldownLeft,SlopeCvd,SlopePressure,SlopeEff,SlopeTradeRate,PersistBull,PersistBear,InflectionCvd,InflectionPressure," +
        "BacktestRunId,CommitHash,FeatureFlags";



        // =========================================================================
        // ALLES, WAS BLEIBT | Ende
        // =========================================================================
        private int _volPhaseWindow = 14;
        private int _minConsecutiveForSwitch = 3;
        private decimal _highEnterMult = 1.2m; // Multiplier to enter Fast
        private decimal _highExitMult = 1.05m; // Multiplier to remain in Fast (hysteresis)
        private decimal _lowEnterMult = 0.8m; // Multiplier to enter Slow
        private decimal _lowExitMult = 0.95m; // Multiplier to remain in Slow (hysteresis)
        private MarketRegime _lastRegime = MarketRegime.Normal;
        private int _regimeConsecutiveCount = 0;
        private int _regimeCooldown = 0; // optional cooldown counter

        // ========================================================
        // PRIVATE MEMBER-VARIABLEN (Zustand der Strategie)
        // ========================================================    
        private readonly ILoggerSource _loggerSource;        
        private SetupConditionConfig _commonConditionsSpecificConfig;
        private SetupConfiguration _strategySetup;

        private MarketDirectionalBias _currentDirectionalBias;
        private MarketState _currentMarketState;
        private MarketStructureContext _marketStructureContext;
        private PatternSignaturer _patternSignaturer;
        private ReversalBouncePatternEvaluator _reversalEvaluator;
        private OvSnapshotHistory _ovSnapshotHistory;
        private OfFeaturesHistory _ofFeaturesHistory;
        private Dictionary<int, OfFeatures> _ofFeaturesByBar;

        readonly object _ofFeaturesSync = new object();
        private readonly List<int> _ofFeaturesBarList;
        private readonly Dictionary<int, int> _ofFeaturesBarMap = new Dictionary<int, int>(); // fungiert als Set (Wert wird nicht verwendet)

        private OrderflowFeatureCalculator _featureCalculator;
        private MarketStateEngine _marketStateEngine;
        private MarketRegimeDetails _marketRegimeDetails;
        private Action<MarketState>? _marketStateUpdatedHandler;
        // Wichtig: kein Lazy mehr (weil wir Initialize direkt aufrufen).
        private OrderflowThresholdManager? _thresholdManager;
        private IThresholdsResolver _thresholdsResolver;


        private int _featuresHistoryCapacity;

        //Volumenprofil Vortag
        private VolumeProfileGenerator _volumeProfileGenerator;
        private SessionBarRangeFinder _sessionBarRangeFinder;
        // Volumenprofil
       
        private void AddFeatureAndSync(OfFeatures feature)
        {
            if (feature == null)
            {
                this.LogWarn("[AddFeatureAndSync] feature ist null -> Abbruch.");
                return;
            }

            // History wird ben?tigt; kann nicht hier neu zugewiesen werden wenn readonly.
            if (_ofFeaturesHistory == null)
            {
                this.LogWarn("[AddFeatureAndSync] _ofFeaturesHistory ist null -> Abbruch (initialisiere im Konstruktor).");
                return;
            }

            // Pr?fe die readonly-Collections; falls null -> Abbruch (sollte durch Konstruktor initialisiert sein)
            if (_ofFeaturesByBar == null || _ofFeaturesBarList == null || _ofFeaturesBarMap == null)
            {
                this.LogWarn("[AddFeatureAndSync] One of required collections is null (_ofFeaturesByBar/_ofFeaturesBarList/_ofFeaturesBarMap). Abbruch (initialisieren im Konstruktor).");
                return;
            }

            // _ofFeaturesSync ist readonly und muss ebenfalls im Konstruktor gesetzt worden sein.
            if (_ofFeaturesSync == null)
            {
                // Falls das Lock-Objekt doch null ist, kann man nicht weitermachen (readonly sollte verhindern)
                this.LogWarn("[AddFeatureAndSync] _ofFeaturesSync ist null -> Abbruch (sollte readonly im Deklarator oder Konstruktor gesetzt werden).");
                return;
            }

            lock (_ofFeaturesSync)
            {
                try
                {
                    // 1) F?ge in die History (verwende die History-API)
                    int? removedBar = null;
                    try
                    {
                        removedBar = _ofFeaturesHistory.AddAndReturnRemoved(feature);
                    }
                    catch (Exception exAdd)
                    {
                        this.LogWarn($"[AddFeatureAndSync] Fehler beim Hinzuf?gen zu _ofFeaturesHistory: {exAdd.GetType().Name}: {exAdd.Message}");
                        return;
                    }
                    
                     // 2) Dictionary updaten (letzter ?berschreibt)
                     _ofFeaturesByBar[feature.Bar] = feature;

                    // 3) List/Map: Falls Bar noch nicht bekannt, am Ende anh?ngen
                    if (!_ofFeaturesBarMap.ContainsKey(feature.Bar))
                    {
                        _ofFeaturesBarList.Add(feature.Bar);
                        _ofFeaturesBarMap[feature.Bar] = 1;
                    }

                    // 4) Falls History ein ?ltestes Element entfernte, markiere dessen Bar als entfernt (Lazily)
                    if (removedBar.HasValue)
                    {
                        int rb = removedBar.Value;
                        _ofFeaturesByBar.Remove(rb);
                        _ofFeaturesBarMap.Remove(rb);
                        // physische Entfernung aus List erfolgt im Trim-Fallback weiter unten
                    }

                    // 5) Trim-Fallback: kompaktiere die List falls sie zu stark gewachsen ist
                    int historyCount = _ofFeaturesHistory?.Count ?? 0;
                    const double maxGrowFactor = 2.0;
                    if (historyCount <= 0) historyCount = 1;
                    if (_ofFeaturesBarList.Count > Math.Max(512, (int)(historyCount * maxGrowFactor)))
                    {
                        var newList = new System.Collections.Generic.List<int>(_ofFeaturesBarList.Count);
                        foreach (var b in _ofFeaturesBarList)
                        {
                            if (_ofFeaturesBarMap.ContainsKey(b))
                            {
                                newList.Add(b);
                            }
                        }
                        _ofFeaturesBarList.Clear();
                        _ofFeaturesBarList.AddRange(newList);
                    }

                    // 6) Konsistenz-Check am Ende (optional)
                    try
                    {
                        VerifyOfFeaturesConsistency();
                    }
                    catch (Exception exVerify)
                    {
                        //this.LogWarn($"[AddFeatureAndSync] VerifyOfFeaturesConsistency warf Exception: {exVerify.GetType().Name}: {exVerify.Message}");
                    }

                    //this.LogDebug($"[AddFeatureAndSync] Feature added OK for bar={feature.Bar}. HistoryCount={_ofFeaturesHistory.Count}, dictCount={_ofFeaturesByBar.Count}");
                }
                catch (Exception exOuter)
                {
                    //this.LogWarn($"[AddFeatureAndSync] Unerwartete Exception: {exOuter.GetType().Name}: {exOuter.Message}\n{exOuter.StackTrace}");
                }
            }
        }
        private static string GetEvaluatorLabel(object o)
        {
            if (o == null) return "null";
            // falls es IPatternEvaluator ist, nimm die Direction direkt
            if (o is IPatternEvaluator pe)
            {
                return $"{pe.GetType().Name}[{pe.Direction}]";
            }

            // Fallback: versuche eine Direction-Property per Reflection
            var type = o.GetType();
            var dirProp = type.GetProperty("Direction") ?? type.GetProperty("Side");
            var dir = "n/a";
            try
            {
                dir = dirProp != null ? dirProp.GetValue(o)?.ToString() ?? "n/a" : "n/a";
            }
            catch
            {
                dir = "n/a";
            }
            return $"{type.Name}[{dir}]";
        }


        private void RebuildOfFeaturesByBarFromHistory()
        {
            // _ofFeaturesSync readonly muss initialisiert sein
            if (_ofFeaturesSync == null)
            {
                this.LogWarn("[RebuildOfFeaturesByBarFromHistory] _ofFeaturesSync ist null -> Abbruch (initialisieren im Konstruktor).");
                return;
            }

            lock (_ofFeaturesSync)
            {
                // Pr?fe readonly-Collections
                if (_ofFeaturesByBar == null || _ofFeaturesBarList == null || _ofFeaturesBarMap == null)
                {
                    this.LogWarn("[RebuildOfFeaturesByBarFromHistory] One of required collections is null -> Abbruch (initialisieren im Konstruktor).");
                    return;
                }
                if (_ofFeaturesHistory == null)
                {
                    this.LogWarn("[RebuildOfFeaturesByBarFromHistory] _ofFeaturesHistory ist null -> keine Rekonstruktion m?glich.");
                    return;
                }
               
                _ofFeaturesByBar.Clear();
                _ofFeaturesBarList.Clear();
                _ofFeaturesBarMap.Clear();

                List<OfFeatures> all = null;
                try
                {
                    all = _ofFeaturesHistory.GetLast(_ofFeaturesHistory.Count);
                }
                catch (Exception ex)
                {
                    this.LogWarn($"[RebuildOfFeaturesByBarFromHistory] GetLast warf Exception: {ex.GetType().Name}: {ex.Message}");
                    all = null;
                }

                if (all == null || all.Count == 0)
                {
                    this.LogDebug("[RebuildOfFeaturesByBarFromHistory] Keine Eintr?ge in History zum Rebuild.");
                    return;
                }

                foreach (var f in all)
                {
                    if (f == null) continue;

                    _ofFeaturesByBar[f.Bar] = f;

                    if (!_ofFeaturesBarMap.ContainsKey(f.Bar))
                    {
                        _ofFeaturesBarList.Add(f.Bar);
                        _ofFeaturesBarMap[f.Bar] = 1;
                    }
                }

                // Konsistenzpr?fung nach Rebuild
                try
                {
                    VerifyOfFeaturesConsistency();
                }
                catch (Exception exVerify)
                {
                    this.LogWarn($"[RebuildOfFeaturesByBarFromHistory] VerifyOfFeaturesConsistency warf Exception: {exVerify.GetType().Name}: {exVerify.Message}");
                }
            }
        }
        // F?gt feature zu History und synchronisiert das Schnellzugriffs-Dictionary mit O(1)-Trim.
        // Erwartung: Bars wachsen monoton. Bei Updates einer existierenden Bar wird das Objekt ersetzt (kein Enqueue).

        private void VerifyOfFeaturesConsistency()
        {
            if (_ofFeaturesSync == null)
            {
                LoggerHelper.LogWarn(this, "[VerifyOfFeaturesConsistency] _ofFeaturesSync ist null -> Abbruch (initialisieren im Konstruktor).");
                return;
            }

            lock (_ofFeaturesSync)
            {
                bool severe = false;

                
                int historyCount = _ofFeaturesHistory?.Count ?? 0;
                int dictCount = _ofFeaturesByBar?.Count ?? 0;
                int listCount = _ofFeaturesBarList?.Count ?? 0;
                int mapCount = _ofFeaturesBarMap?.Count ?? 0;

                if (dictCount > historyCount)
                {
                    LoggerHelper.LogError(this, $"[VerifyOfFeaturesConsistency] _ofFeaturesByBar.Count({dictCount}) > HistoryCount({historyCount})");
                    severe = true;
                }

                if (mapCount > historyCount)
                {
                    LoggerHelper.LogError(this, $"[VerifyOfFeaturesConsistency] _ofFeaturesBarMap.Count({mapCount}) > HistoryCount({historyCount})");
                    severe = true;
                }

                if (_ofFeaturesByBar != null && _ofFeaturesBarMap != null)
                {
                    foreach (var key in _ofFeaturesByBar.Keys.ToList()) // ToList um "Collection modified" zu vermeiden
                    {
                        if (!_ofFeaturesBarMap.ContainsKey(key))
                        {
                            LoggerHelper.LogError(this, $"[VerifyOfFeaturesConsistency] Bar {key} in _ofFeaturesByBar, aber nicht in Map/List.");
                            severe = true;
                        }
                    }
                }

                if (_ofFeaturesHistory != null && _ofFeaturesBarMap != null && _ofFeaturesBarMap.Count > 0)
                {
                    var historyBars = new HashSet<int>();
                    try
                    {
                        var all = _ofFeaturesHistory.GetLast(_ofFeaturesHistory.Count);
                        if (all != null)
                        {
                            foreach (var f in all)
                            {
                                if (f == null) continue;
                                historyBars.Add(f.Bar);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.LogWarn(this, $"[VerifyOfFeaturesConsistency] enumeration of _ofFeaturesHistory failed: {ex.GetType().Name}: {ex.Message}");
                    }

                    foreach (var b in _ofFeaturesBarMap.Keys.ToList())
                    {
                        if (!historyBars.Contains(b))
                        {
                            LoggerHelper.LogWarn(this, $"[VerifyOfFeaturesConsistency] Bar {b} in Map, aber nicht in History.");
                            // nicht automatisch severe; kann legit sein (lazy removal)
                        }
                    }
                }

                if (_ofFeaturesBarList != null && _ofFeaturesBarMap != null && _ofFeaturesByBar != null)
                {
                    foreach (var bar in _ofFeaturesBarList.ToList())
                    {
                        if (_ofFeaturesBarMap.ContainsKey(bar) && !_ofFeaturesByBar.ContainsKey(bar))
                        {
                            LoggerHelper.LogError(this, $"[VerifyOfFeaturesConsistency] Bar {bar} in Map/List, aber nicht im Dictionary.");
                            severe = true;
                        }
                    }
                }

                if (severe)
                {
                    LoggerHelper.LogError(this, "[VerifyOfFeaturesConsistency] severe inconsistency detected -> forcing rebuild");
                    try
                    {
                        RebuildOfFeaturesByBarFromHistory();
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.LogError(this, $"[RebuildOfFeaturesByBarFromHistory] RebuildOfFeaturesByBarFromHistory failed: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }

        private static object ConvertToTargetType(decimal src, Type targetType)
        {
            if (targetType == typeof(decimal) || targetType == typeof(decimal?)) return src;
            if (targetType == typeof(double) || targetType == typeof(double?)) return Convert.ToDouble(src);
            if (targetType == typeof(float) || targetType == typeof(float?)) return Convert.ToSingle(src);
            if (targetType == typeof(string)) return src.ToString("G");
            var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (underlying.IsEnum) return Enum.Parse(underlying, src.ToString());
            return Convert.ChangeType(src, underlying, CultureInfo.InvariantCulture);
        }

        private void OnMarketStateUpdated(MyNamespace.Strategies.Models.MarketState state)
        {
            try
            {
                _currentMarketState = state;
                this.LogInfo($"[MarketStateUpdated] New state: Bias={state.DirectionalBias}, Confidence={state.Confidence:F2}, Regime={state.Regime}");
                // weitere Reaktionen...
            }
            catch (Exception ex)
            {
                this.LogWarn($"[MarketStateUpdated] Fehler im Handler: {ex.Message}");
            }
        }

        //Datensammler

        // Research-Settings
        private bool _researchEnabled = true;            // zum Test aktiv
        private int _researchHorizonBars = 10;
        private int _researchOutcomeTicks = 10;
        private int _researchProximityTicks = 8;         // f?r Level-N?he-Filter bei Kandidatenerzeugung
        private int _researchPathCheckTicks = 12; // Pfad-/Hindernis-Check (kannst du ?berschreiben)
        private string _researchTimeframeLabel = "1m";

        public int OutcomeTicks { get; set; } = 12;
        public int StopTicks { get; set; } = 10;
        public string ResearchBaseDir { get; set; } = @"C:\Users\User\Documents\Strategieauswertung";

        
        private decimal _lastFinalScore = 0;
        private bool _lastApproved = false;
        // Laufzeit

        private string ToCsvLine(
        OvSnapshot s,
        string instrument = "UNKNOWN",
        string thresholdsSnapshotJson = "",
        int historyVersion = -1,
        DateTime? thresholdsCreatedAtUtc = null,
        string prunerNullifiedFields = "",
        bool finalDecision = false,
        string metCriteriaList = "",
        string metHardList = "",
        string metRelList = "",
        int metCriteriaCount = -1, // neu
        int possibleCriteriaCount = -1, // neu
        string detectReason = "", // neu
        double compositeScore = double.NaN,
        double cvdMean30 = double.NaN,
        double cvdStd30 = double.NaN,
        double volBurstMax3 = double.NaN,
        int volBurstAge = -1,
        int inflectionCount3 = 0,
        bool softVolBurstFlag = false,
        bool softNegativeFlag = false,
        string patternType = "",
        string patternDirection = "",
        string patternCategory = "",
        double patternLevel = double.NaN,
        double patternConfidence = double.NaN,
        double patternScore = double.NaN,
        double patternCombinedConf = double.NaN,
        string volBurstClass = "",
        int volBurstCooldownLeft = -1,
        double slopeCvd = double.NaN,
        double slopePressure = double.NaN,
        double slopeEff = double.NaN,
        double slopeTradeRate = double.NaN,
        int persistBull = 0,
        int persistBear = 0,
        string inflectionCvd = "",
        string inflectionPressure = "",
        string backtestRunId = "",
        string commitHash = "",
        string featureFlags = ""
        )
        {
            // Helper formatters for decimal and double parts
            Func<decimal, string> fmtDec4 = d => d.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
            Func<decimal, string> fmtDec6 = d => d.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
            Func<decimal, string> fmtDec0 = d => d.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
            Func<string, string> esc = EscapeCsvCell;

            var fields = new List<string>();

            // Core fields (decimal fields formatted accordingly)
            fields.Add(((long)s.Bar).ToString(System.Globalization.CultureInfo.InvariantCulture));                                // BarIndex
            fields.Add(s.Time.ToString("O", System.Globalization.CultureInfo.InvariantCulture));                                   // Time_ISO
            fields.Add(esc(instrument ?? "UNKNOWN"));                                                                              // Instrument
            fields.Add(fmtDec4(s.Open));                                                                                           // Open
            fields.Add(fmtDec4(s.High));                                                                                           // High
            fields.Add(fmtDec4(s.Low));                                                                                            // Low
            fields.Add(fmtDec4(s.Close));                                                                                          // Close
            fields.Add(fmtDec4(s.Volume));                                                                                         // Volume
            fields.Add(fmtDec4(s.Delta));                                                                                          // Delta
            fields.Add(fmtDec4(s.Ask));                                                                                            // Ask
            fields.Add(fmtDec4(s.Bid));                                                                                            // Bid

            fields.Add(fmtDec6(s.MaxCounterShareBull));                                                                            // MaxCounterShareBull
            fields.Add(fmtDec6(s.MaxCounterShareBear));                                                                            // MaxCounterShareBear
            fields.Add(fmtDec6(s.VolBurstZ));                                                                                      // VolBurstZ
            fields.Add(fmtDec6(s.CvdImpulse));                                                                                     // CvdImpulse
            fields.Add(fmtDec6(s.CvdCoherence));                                                                                   // CvdCoherence
            fields.Add(fmtDec6(s.AggPressure));                                                                                    // AggPressure
            fields.Add(fmtDec6(s.TradeRateZ));                                                                                     // TradeRateZ

            // Efficiency (decimal)
            fields.Add(fmtDec4(s.Efficiency));                                                                                     // Efficiency

            fields.Add(((long)s.BuyTrades).ToString(System.Globalization.CultureInfo.InvariantCulture));                           // BuyTrades
            fields.Add(((long)s.SellTrades).ToString(System.Globalization.CultureInfo.InvariantCulture));                          // SellTrades
            fields.Add(((long)s.TotalTrades).ToString(System.Globalization.CultureInfo.InvariantCulture));                         // TotalTrades

            fields.Add(fmtDec6(s.IttZ));                                                                                           // IttZ

            fields.Add(s.SweepUpClosed ? "1" : "0");                                                                                // SweepUp
            fields.Add(s.SweepDnClosed ? "1" : "0");                                                                                // SweepDn

            fields.Add(((int)s.StackedBuyImbCount).ToString(System.Globalization.CultureInfo.InvariantCulture));                    // StackedBuyImbCount
            fields.Add(((int)s.StackedSellImbCount).ToString(System.Globalization.CultureInfo.InvariantCulture));                   // StackedSellImbCount
            fields.Add(((int)s.StackedBuyImbTopCount).ToString(System.Globalization.CultureInfo.InvariantCulture));                 // StackedBuyImbTopCount
            fields.Add(((int)s.StackedSellImbBottomCount).ToString(System.Globalization.CultureInfo.InvariantCulture));             // StackedSellImbBottomCount

            fields.Add(fmtDec6(s.StackedImbRatioPct));                                                                              // StackedImbRatioPct
            fields.Add(fmtDec6(s.StackedImbMinVolPerLevel));                                                                        // StackedImbMinVolPerLevel
            fields.Add(((int)s.StackedImbRangeMin).ToString(System.Globalization.CultureInfo.InvariantCulture));                    // StackedImbRangeMin
            fields.Add(((int)s.StackedImbMaxDepthTicks).ToString(System.Globalization.CultureInfo.InvariantCulture));               // StackedImbMaxDepthTicks

            fields.Add(fmtDec6(s.CandleDuration));                                                                                  // CandleDuration

            // VolPerSecond, EmaVolPerSecond, EmaVolPerSecondStd (decimal)
            fields.Add(fmtDec4(s.VolPerSecond));                                                                                    // VolPerSecond
            fields.Add(fmtDec4(s.EmaVolPerSecond));                                                                                 // EmaVolPerSecond
            fields.Add(fmtDec4(s.EmaVolPerSecondStd));                                                                              // EmaVolPerSecondStd

            fields.Add(fmtDec6(s.CumulativeDelta));                                                                                 // CumulativeDelta
            fields.Add(fmtDec6(s.CumulativeVolume));                                                                                // CumulativeVolume

            fields.Add(esc(s.MarketRegime ?? "None"));                                                                              // MarketRegime

            fields.Add(s.BarDeltaPerVolume.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));                      // BarDeltaPerVolume

            // thresholds/history/pruner/decision/metfields
            fields.Add(historyVersion > 0 ? historyVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty); // HistoryVersion
            fields.Add(thresholdsCreatedAtUtc.HasValue ? thresholdsCreatedAtUtc.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture) : string.Empty); // ThresholdsCreatedAtUtc
            fields.Add(esc(thresholdsSnapshotJson));                                                                                // ThresholdsSnapshot
            fields.Add(esc(prunerNullifiedFields));                                                                                 // PrunerNullifiedFields
            fields.Add(finalDecision ? "1" : "0"); // FinalDecision
            fields.Add(esc(metCriteriaList)); // MetCriteriaList
            fields.Add(esc(metHardList)); // MetHardList
            fields.Add(esc(metRelList)); // MetRelList
            fields.Add(metCriteriaCount >= 0 ? metCriteriaCount.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty); // MetCriteriaCount
            fields.Add(possibleCriteriaCount >= 0 ? possibleCriteriaCount.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty); // PossibleCriteriaCount
            fields.Add(esc(detectReason)); // DetectReason

            // composite/cvd/volburst Numerische Felder ? diese haben eine doppelte Signatur: Beibehaltung der NaN-Behandlung
            fields.Add(double.IsNaN(compositeScore) ? string.Empty : compositeScore.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)); // CompositeScore
            fields.Add(double.IsNaN(cvdMean30) ? string.Empty : cvdMean30.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));           // CvdMean30
            fields.Add(double.IsNaN(cvdStd30) ? string.Empty : cvdStd30.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));             // CvdStd30
            fields.Add(double.IsNaN(volBurstMax3) ? string.Empty : volBurstMax3.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));     // VolBurstMax3

            fields.Add(volBurstAge.ToString(System.Globalization.CultureInfo.InvariantCulture));                                           // VolBurstAge
            fields.Add(inflectionCount3.ToString(System.Globalization.CultureInfo.InvariantCulture));                                     // InflectionCount3
            fields.Add(softVolBurstFlag ? "1" : "0");                                                                                  // SoftVolBurstFlag
            fields.Add(softNegativeFlag ? "1" : "0");                                                                                  // SoftNegativeFlag

            // Pattern/feature fields
            fields.Add(esc(patternType));                                                                                            // PatternType
            fields.Add(esc(patternDirection));                                                                                       // PatternDirection
            fields.Add(esc(patternCategory));                                                                                        // PatternCategory
            fields.Add(double.IsNaN(patternLevel) ? string.Empty : patternLevel.ToString("F4", System.Globalization.CultureInfo.InvariantCulture));   // PatternLevel (double arg)
            fields.Add(double.IsNaN(patternConfidence) ? string.Empty : patternConfidence.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)); // PatternConfidence
            fields.Add(double.IsNaN(patternScore) ? string.Empty : patternScore.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));       // PatternScore
            fields.Add(double.IsNaN(patternCombinedConf) ? string.Empty : patternCombinedConf.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)); // PatternCombinedConf

            fields.Add(esc(volBurstClass));                                                                                           // VolBurstClass
            fields.Add(volBurstCooldownLeft.ToString(System.Globalization.CultureInfo.InvariantCulture));                               // VolBurstCooldownLeft

            // Slope fields are double in signature; keep NaN check
            fields.Add(double.IsNaN(slopeCvd) ? string.Empty : slopeCvd.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));        // SlopeCvd
            fields.Add(double.IsNaN(slopePressure) ? string.Empty : slopePressure.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)); // SlopePressure
            fields.Add(double.IsNaN(slopeEff) ? string.Empty : slopeEff.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));      // SlopeEff
            fields.Add(double.IsNaN(slopeTradeRate) ? string.Empty : slopeTradeRate.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)); // SlopeTradeRate

            fields.Add(persistBull.ToString(System.Globalization.CultureInfo.InvariantCulture));                                            // PersistBull
            fields.Add(persistBear.ToString(System.Globalization.CultureInfo.InvariantCulture));                                            // PersistBear

            fields.Add(esc(inflectionCvd));                                                                                            // InflectionCvd
            fields.Add(esc(inflectionPressure));                                                                                         // InflectionPressure

            // Backtest/commit/flags
            fields.Add(esc(backtestRunId));                                                                                              // BacktestRunId
            fields.Add(esc(commitHash));                                                                                                 // CommitHash
            fields.Add(esc(featureFlags));                                                                                               // FeatureFlags

            return string.Join(",", fields);
        }




        // Hilfsfunktion zum einfachen CSV-Zellen-Escaping (keine heavy libs, minimal)
        private static string EscapeCsvCell(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            if (input.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
            {
                return "\"" + input.Replace("\"", "\"\"") + "\"";
            }

            return input;
        }






        public class MarketRegimeDetails
        {
            public MarketRegime Regime { get; set; } // Das finale Regime (Fast, Slow, Normal)
            public int FastVotes { get; set; }
            public int SlowVotes { get; set; }
            public bool IsHighVol { get; set; }
            public decimal Vps { get; set; } // Volatility per Second
            public decimal VpsEma { get; set; } // EMA of Volatility per Second
            public decimal VpsStd { get; set; } // Standardabweichung der Volatility per Second
            public decimal ZScore { get; set; } // Z-Score der VPS
            public decimal TradesPerSecZ { get; set; } // Trade Rate Z-Score
            public decimal SecondsPerBar { get; set; } // Dauer der Bar in Sekunden

            // Optional: Wenn Sie die Schwellenwerte auch loggen m?chten
            // public decimal VolLowMult { get; set; }
            // public decimal VolHighMult { get; set; }
            // public decimal VolFastZ { get; set; }
            // ...
        }

        
        

            // =========================================================================
            // Bounce-Erkennung - Anfang
            // =========================================================================
            public enum BouncePhase
        {
            Idle,
            RejectionEval,      // innerhalb des Rejection-Fensters
            EntryConfirmation    // nach Ende des Fensters (optional f?r Continuation-Checks)
        }

        public enum BounceSide
        {
            None,
            UpperBand, // z. B. VAH, VWAP+Band oben
            LowerBand  // z. B. VAL, VWAP-Band unten
        }

        public struct BounceWindowParams
        {
            public int ConfirmationBars { get; set; }
            public int ProximityTicks { get; set; }
            public int MaxPenetrationTicks { get; set; }
            public int MinReboundTicks { get; set; }
            public int MinApproachTicksFromPoc { get; set; }
            public int MagnetTicksToPoc { get; set; }
            public int MinDistToPocForEntry { get; set; }
            public int StopPadTicks { get; set; }
            public int PreTouchMaxDistanceTicks { get; set; }   // Distanz zur Kante, innerhalb der ein Reversal ohne Touch erlaubt ist
            public int RangeReversalTicks { get; set; }         // Mindest-Reversal-Bewegung (weg von der Kante), aggregiert ?ber Fenster

        }

        public struct BounceContext
        {
            public int Bar;
            public decimal TickSize;

            public decimal UpperBand;   // VAH oder VWAP+Band
            public decimal LowerBand;   // VAL oder VWAP-Band
            public decimal POC;         // optional (kann 0 sein, wenn nicht genutzt)
            public decimal Close;
            public decimal High;
            public decimal Low;
            public decimal PrevHigh;
            public decimal PrevLow;
            public decimal PrevClose;
            public bool InValueArea;    // true/false, je nach Setup-Kontext (VA-basiert)
        }
        public struct BounceConfirmResult
        {
            public bool ConfirmLower;   // Long an Unterer Bandkante (z. B. VAL)
            public bool ConfirmUpper;   // Short an Oberer Bandkante (z. B. VAH)
            public int FromBar;
            public int ToBar;
            public BounceSide SideTouched;
            public string Detail;
        }

        public struct BounceEvalCounters
        {
            public int EvalTradesCount;
            public int EvalPositiveTrades;
            public int NoAggSellerConsec;
            public bool IntrabarReclaimed;
            public bool DwellOk;
            public DateTime EvalStartUtc;
        }

        private BounceContext? _ctxSetup6;

        private readonly SetupRegistry _setups = new();
        // Pro Setup ein Detector-Container (Key kann SetupName oder SetupName+BandId sein)
       
              

       


        // Richtung-Enum
        private enum AggressorSide { Buy, Sell }
        public sealed class SetupRegistry
        {
            public delegate IEnumerable<BounceContext> SetupContextBuilderIC(
                int bar,
                ATAS.Indicators.IndicatorCandle c,
                ATAS.Indicators.IndicatorCandle p,
                decimal tick);

            private readonly Dictionary<string, SetupContextBuilderIC> _setupBuilders = new();
            private readonly Dictionary<string, BounceWindowParams> _setupParams = new();

            // Optionaler Puffer (kann bleiben oder entfernt werden)
            private BounceContext _ctxSetup6;

            // Registrierung eines Setups
            public void Register(string setupKey, SetupContextBuilderIC builder, BounceWindowParams prm)
            {
                _setupBuilders[setupKey] = builder;
                _setupParams[setupKey] = prm;
            }

            // Zugriff auf Builder + Params
            public bool TryGet(string setupKey, out SetupContextBuilderIC builder, out BounceWindowParams prm)
            {
                builder = _setupBuilders.TryGetValue(setupKey, out var b) ? b : null;
                prm = _setupParams.TryGetValue(setupKey, out var p) ? p : default;
                return builder != null;
            }

            // Schl?ssel-Liste (f?r Iteration/Existenzcheck)
            public IEnumerable<string> Keys => _setupBuilders.Keys;
        }
        private decimal? _longTriggerOverride;
        private decimal? _shortTriggerOverride;
        private int? _reclaimTicksOverride;
        private int? _nearTicksOverride;

        // =========================================================================
        // Bounce-Erkennung - Ende
        // =========================================================================

        private enum EntryState
        {
            Idle,
            TouchArmed,
            RejectionEval,
            PullbackPlaced,
            EntryPlaced, // NEU: generische Platzierung f?r tickgenaue Entries
            ContinuationPlaced,
            Filled,
            Cancelled
        }

        // Parameter-Defaults
        private int EntryProxTicks = 1;
        private int EntryVersatzTicks = 0;     // 0 ruhig, 1 schnell (sp?ter adaptiv)
        private int CancelAwayTicks = 4;
        private int RejectionWindowMs = 10;  // Zeitfenster f?r Rejection-Eval
        private int TradesWindow = 12;        // optional, wenn wir intrabar Trades z?hlen
        private int AggSellersQuietTrades = 6; // sp?ter mit Prints nutzbar
        private int StopTriggerTicks = 1;
        private int StopLimitOffsetTicks = 1; // sp?ter adaptiv

        // Laufzeit-Felder
        private EntryState _entryState = EntryState.Idle;
        private DateTime _touchTsUtc = DateTime.MinValue; // Zeitpunkt des Band-Touch/Arm
        private DateTime _evalStartUtc = DateTime.MinValue;

        private int _evalTradesCount = 0;            // wenn wir intrabar z?hlen (sp?ter)
        private int _evalPositiveTrades = 0;         // M von N (sp?ter)
        private int _noAggSellerConsec = 0;          // (sp?ter mit Prints)

        private bool _evalPositive = false;          // Ergebnis der Evaluation
        private bool _intrabarReclaimed = false;     // Reclaim-Kriterium erf?llt?
        private bool _dwellOk = false;               // Dwell-Kriterium erf?llt?

        // Entry-Kontext
        private bool _entryIsLong = false;           // true = Long, false = Short
        private int _signalBarIndex = -1;
        private decimal _signalBarHigh = 0m;
        private decimal _signalBarLow = 0m;

        private int _entryBandIdx = 0;               // 1 oder 2
        private decimal _entryBandLevel = 0m;        // LB/UB je Richtung
        private decimal _entryTargetLevel = 0m;      // z. B. VWAP

        // Marktgeschwindigkeit/Vol-Z (optional, sp?ter genutzt)
        private string _entryMarketSpeed = "Normal";
        private decimal _entryVolZ = 0m;

        // OF-Gate-Ergebnisse (letzte geschlossene Kerze)
        private bool _ofBullOKLastClosed = false;
        private bool _ofBearOKLastClosed = false;
        // Neue Gate-Ergebnis-Flags
        private bool _ofBounceBullOKLastClosed;
        private bool _ofBounceBearOKLastClosed;
        private bool _ofContBullOKLastClosed;
        private bool _ofContBearOKLastClosed;

        
        private enum SetupPhase { Idle, RejectionEval, EntryConfirmation }
        private SetupPhase _phase = SetupPhase.Idle;
        private bool _isInRejectionWindow = false;

        

        private SortedDictionary<decimal, decimal> _vwHist = new();
        private bool _vwActive = false;
        private int _vwStartBar = -1;
        private int _vwLastBar = -1;
        private int _vwDirection = 0;
        private decimal _vwPrevPOC = 0m;
        private decimal _vwPOC = 0m;
        // Output: nur Flag (und optional Preis)
        public bool VwSignal { get; private set; } = false;
        public decimal VwSignalPrice { get; private set; } = 0m; // optional, falls du ihn brauchst
        public void ClearVwSignal()
        {
            VwSignal = false;
            VwSignalPrice = 0m;
        }
        private bool _vwPendingReset = false;
        public int VwSignalDir = 0; // +1 Long, -1 Short, 0 none
        private int TicksBetween(decimal a, decimal b)
        {
            if (_tickSize <= 0m) return 0; // Guard: kein TickSize bekannt
                                           // Vorzeichen tr?gt Richtung: a>b -> positiv (Up), a<b -> negativ (Down)
            var raw = (a - b) / _tickSize;
            return (int)Math.Round(raw, MidpointRounding.AwayFromZero);
        }

        private int TicksBetweenAbs(decimal a, decimal b) => Math.Abs(TicksBetween(a, b));

        private static T SafeGet<T>(IDictionary<int, T> dic, int bar, T defaultValue = default)
                => dic != null && dic.TryGetValue(bar, out var v) ? v : defaultValue;

        private int SweepStreak(Dictionary<int, int> dir, int closed)
        {
            if (dir == null) return 0;
            int streak = 0;
            int last = 0;
            for (int i = closed; i >= 0; i--)
            {
                if (!dir.TryGetValue(i, out var d) || d == 0) break;
                if (streak == 0) last = d;
                if (d != last) break;
                streak++;
            }
            return streak;
        }

        private (string key, decimal price, int distTicks) FindNearestRelevantLevel(decimal price, LevelsSnapshot snap)
        {
            var candidates = new List<(string k, decimal?)>
            {
                ("PDH", snap.PreviousDayHigh), ("PDL", snap.PreviousDayLow),
                ("PDPOC", snap.PreviousDayPOC),
                ("POC", snap.CurrentPOC), ("VAH", snap.CurrentVAH), ("VAL", snap.CurrentVAL),
                ("PP", snap.PP), ("R1", snap.R1), ("R2", snap.R2), ("R3", snap.R3),
                ("S1", snap.S1), ("S2", snap.S2), ("S3", snap.S3),
                ("RoundBelow", snap.RoundLevelBelow), ("RoundAbove", snap.RoundLevelAbove),
                ("LastBlockRes", snap.LastBlockResistance), ("LastBlockSup", snap.LastBlockSupport),
            };

            string bestKey = ""; decimal bestPrice = 0m; int bestTicks = int.MaxValue;
            foreach (var (k, v) in candidates)
            {
                if (!v.HasValue || v.Value <= 0m) continue;
                var ticks = Math.Abs(TicksBetween(price, v.Value));
                if (ticks < bestTicks) { bestTicks = ticks; bestKey = k; bestPrice = v.Value; }
            }
            return bestTicks == int.MaxValue ? ("", 0m, -1) : (bestKey, bestPrice, bestTicks);
        }

        private int DistIfSet(decimal entry, decimal level)
            => level > 0m ? Math.Abs(TicksBetween(entry, level)) : 0;

       
        private void SetResearchEnabled(bool enabled)
        {
            if (enabled == _researchEnabled) return;
            _researchEnabled = enabled;

            if (_researchEnabled)
            {
                            
                
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Research",
                    $"{(InstrumentInfo?.Instrument ?? "Unknown")}_{DateTime.UtcNow:yyyyMMdd}.csv"
                );

                decimal tickSize = 0.25m; // z. B. Instrument.MasterInstrument.TickSize;
                int outcomeTicks = 12;
                int stopTicks = 10;

                _research = new ResearchCollector(
                    tickSize,
                    outcomeTicks,
                    stopTicks,
                    @"C:\Users\User\Documents\Strategieauswertung"
                );

                this.LogInfo("[Research] Collector enabled");
            }
            else
            {
                try { _research?.Flush(); } catch { }
                _research = null;
                this.LogInfo("[Research] Collector disabled");
            }
        }


        

        [Display(Name = "Entry Timeout (Bars)", GroupName = "Entry Settings", Order = 1)]
        [Range(1, 200)]
        public int EntryTimeoutBars { get; set; } = 30;

        [Display(Name = "Pullback Timeout (Bars)", GroupName = "Entry Settings", Order = 2)]
        [Range(1, 200)]
        public int PullbackTimeoutBars { get; set; } = 20;

                
        [Display(Name = "VolZ Lookback (VolZ_Lookback)", GroupName = "Entry Settings", Order = 4)]
        [Description("Lookback-Perioden f?r den Volumen-Z-Score-Berechnung. Definiert die maximale Window-Gr??e f?r die Queue. Z.B. 30 f?r 1-Min-Charts in Scalping-Strategien.")]
        [Range(10, 100)]  // Optional: Min/Max f?r die UI (entferne, wenn ATAS das nicht unterst?tzt)
        public int VolZ_Lookback { get; set; } = 30;


        [Display(Name = "EWMA Alpha (EwmaAlpha)", GroupName = "Entry Settings", Order = 5)]  // Gruppiert mit VolZ_Lookback, Order=5 f?r Reihenfolge
        [Description("Gewichtungsfaktor f?r den Exponentially Weighted Moving Average (EWMA) im Z-Score. H?here Werte machen es reaktiver auf neue Daten. Z.B. 0.2 f?r M1-Scalping.")]
        [Range(0.01, 0.5)]  // Optional: Min/Max f?r die UI (entferne, wenn ATAS das nicht unterst?tzt)
        public double EwmaAlpha { get; set; } = 0.2;  // Default auf 0.2, wie in deiner Vorlage

        


        


        [Display(Name = "Aktiviere Pullback Order",
        GroupName = "Entry Settings",
        Description = "Aktiviert die Limit Pullback Logik statt direkter Stop-Entry.")]
        public bool EnablePullback { get; set; } = true;

        [Display(Name = "Trailing aktivieren",
         GroupName = "Entry Settings",
         Description = "Aktiviert das kontinuierliche Trailing nach der initialen Ber?hrung und Neuplatzierung der Pullback-Order. Deaktiviere, um nur einmalig nach Ber?hrung zu platzieren.")]
        public bool EnableContinuousTrailing { get; set; } = true; // Default: true, um bestehendes Verhalten beizubehalten


        [Display(Name = "Pullback Platzierung (Ticks)",
         GroupName = "Entry Settings",
         Description = "Anzahl Ticks unter/?ber dem Referenzpreis f?r die initiale Limit Pullback Order (z.B. 5). 0 deaktiviert Pullback.")]
        public int PullbackTicksInitial { get; set; } = 2; // Default: 5 Ticks Pullback

        [Display(Name = "Pullback Trail (Ticks)",
                 GroupName = "Entry Settings",
                 Description = "Anzahl Ticks ?ber/unter dem Preis f?r die trailing StopLimit nach Ber?hrung (z.B. 3).")]
        public int PullbackTicksTrail { get; set; } = 3; // Default: 3 Ticks f?r Trailing




        [Display(Name = "Break-Even Stop Aktivieren",
                GroupName = "Break-Even Settings",
                Description = "Aktiviert den Break Even Stop")]
        public bool EnableBreakEven { get; set; } = true;

       
               

        // Parameter f?r die Handelsmenge (Optional, n?tzlich)
        [Display(Name = "Handelsmenge (Lots)",
                 GroupName = "Handelssettings",
                 Order = 1500)]
        [Range(0.1, 100.0, ErrorMessage = "Handelsmenge muss zwischen 0.1 und 100.0 liegen.")] // Passen Sie den Range ggf. an
        [Description("Die Menge (in Lots oder Einheiten) pro Trade.")]
        public decimal HandelsMenge { get; set; } = 1.0m; // Standard 1.0 Kontrakt

       

        [Display(Name = "SMA Periode",
         GroupName = "SMA Settings",
         Order = 115)] // So wird der Parameter in der UI sinnvoll einsortiert
        [Range(1, 500, ErrorMessage = "Die SMA Periode muss zwischen 1 und 500 liegen.")]
        [Description("Periode f?r den Simple Moving Average (SMA), der als Trendfilter f?r das Signal dient.")]
        public int SmaPeriod { get; set; } = 50; // Standardwert ist 10

        [Display(Name = "VWAP Periode", // Wenn VWAP einen Zeitraum unterst?tzt, andernfalls entfernen oder anpassen.
          GroupName = "VWAP Settings",
          Order = 1360)]
        public int VWAPPeriod { get; set; } = 1; // Standardzeitraum; geeigneten Wert in den ATAS-Dokumenten ?berpr?fen



        [Display(Name = "B?nder anzeigen",
            GroupName = "VWAP Einstellungen",
            Order = 1)]
        public bool ShowVWAPBands { get; set; } = false; // Standardm??ig auf true setzen, um B?nder anzuzeigen

        [Display(Name = "Round Numbers Stufen",
         GroupName = "Strategie-Parameter",
         Order = 1000)]
        public decimal PointStep { get; set; } = 50;


        // =========================================================================
        // Level-Parameter Einstellungen
        // =========================================================================
        
        // Entry-Blocker Parameter
        [Display(Name = "Level-System aktivieren", 
                 GroupName = "Level-Parameter ? Entry-Blocker",
                 Description = "Aktiviert das komplette Level-System: Berechnet signifikante Preislevel, ?berwacht Ber?hrungen und blockiert Einstiege bei Level-N?he. Dies ist der Master-Schalter f?r alle Level-bezogenen Funktionen.",
                 Order = 1)]
        public bool EnableIsBlocked { get; set; } = true;

        [Display(Name = "Mindestabstand f?r Entry (Ticks)", 
                 GroupName = "Level-Parameter ? Entry-Blocker",
                 Description = "Verhindert den Einstieg, wenn der Preis zu nah an signifikanten Zonen ist. Dies betrifft die 'IsTooCloseForEntry' Logik. H?here Werte machen die Strategie selektiver.",
                 Order = 2)]
        [Range(0.1, 1000)]
        public decimal ProximityTicksForEntry { get; set; } = 8;

        // Signifikante Level Parameter
        [Display(Name = "Level visualisieren", 
                 GroupName = "Level-Parameter ? Signifikante Level",
                 Description = "Zeigt signifikante Preislevel im Chart an. Ben?tigt 'Level-System aktivieren'. Wenn Visualisierung ohne Berechnung gew?nscht, wird die Level-Berechnung automatisch aktiviert.",
                 Order = 1)]
        public bool EnableSignificantPreviousLevels { get; set; } = true;

        [Display(Name = "Anzahl Tage f?r signifikante Levels", 
                 GroupName = "Level-Parameter ? Signifikante Level",
                 Description = "Definiert ?ber wie viele Tage zur?ck signifikante Preislevel (Tageshoch/-tief, Vortages-POC) f?r die Handelsentscheidung ber?cksichtigt werden. Mehr Tage geben mehr Referenzpunkte.",
                 Order = 2)]
        [Range(1, 100)]
        public int MaxDaysForSignificantLevels { get; set; } = 5;

        [Display(Name = "Mindestabstand Kerzen-Ber?hrungen", 
                 GroupName = "Level-Parameter ? Signifikante Level",
                 Description = "Definiert den Mindestabstand in Kerzen, bevor eine neue Ber?hrung desselben Levels gez?hlt wird. Verhindert, dass schnelle Preisfluktuationen um ein Level als multiple Ber?hrungen gewertet werden.",
                 Order = 3)]
        [Range(1, 50)]
        public int MinCandleSeparationForTouches { get; set; } = 5;



        [Display(Name = "MicroComposite ?ber N Bars", GroupName = "Visualisation",
        Description = "Definiert die Gr??e des rollierenden Volumenprofils in Kerzen",
        Order = 7)]
        public int M { get; set; } = 100; // Standardwert anpassen
                                         
        [Display(Name = "Value Area %", GroupName = "Visualisation", Order = 7)]
        public decimal ValueAreaPct { get; set; } = 0.70m; // auf 0.682m setzen, wenn ATAS-so

        
        // =========================================================================
        // MicroComposite Einstellungen
        // =========================================================================
        
        // MicroComposite ? System Einstellungen
        [Display(Name = "MicroComposite-System aktivieren", 
                 GroupName = "MicroComposite ? System",
                 Description = "Aktiviert das komplette MicroComposite-System: Berechnet Volumenprofile, pr?ft Weg-Frei-Blocker und zeigt HVN/LVN-Zonen an. Dies ist der Master-Schalter f?r alle MicroComposite-bezogenen Funktionen.",
                 Order = 1)]
        public bool EnableMicroCompositeSystem { get; set; } = true;

        [Display(Name = "MicroComposite visualisieren", 
                 GroupName = "MicroComposite ? System",
                 Description = "Zeigt MicroComposite-Levels (HVN/LVN-Zonen, POC, VAH/VAL) im Chart an. Ben?tigt 'MicroComposite-System aktivieren'. Wenn Visualisierung ohne Berechnung gew?nscht, wird das MicroComposite-System automatisch aktiviert.",
                 Order = 2)]
        public bool ShowMicroCompositeLevels { get; set; } = true;
        
        // MicroComposite ? Einstellungen (Datenqualit?t)
        [Display(Name = "Top HVN Zonen", 
                 GroupName = "MicroComposite ? Einstellungen",
                 Description = "Maximale Anzahl an HVN-Zonen, die nach Scoring behalten werden. Weniger HVNs ausw?hlen; verringert ?berlagerungen und h?lt die wichtigsten, kompakten Zonen im Fokus.",
                 Order = 10)]
        [Range(1, 50)]
        public int TopNHVNs { get; set; } = 4;

        [Display(Name = "Top LVN Zonen", 
                 GroupName = "MicroComposite ? Einstellungen",
                 Description = "Maximale Anzahl an LVN-Zonen, die nach Scoring behalten werden",
                 Order = 11)]
        [Range(1, 50)]
        public int TopNLVNs { get; set; } = 4;

        [Display(Name = "Mindestbreite Zone (Ticks)", 
                 GroupName = "MicroComposite ? Einstellungen",
                 Description = "L?sst schmale, klare HVNs zu (nicht zu niedrig setzen, sonst Rauschen).",
                 Order = 12)]
        [Range(1, 100)]
        public int MinZoneTicks { get; set; } = 3;

        [Display(Name = "Minimale Prominenz", 
                 GroupName = "MicroComposite ? Einstellungen",
                 Description = "Hebt die Qualit?t; indirekt oft schmalere Zonen, weil flache, breitgezogene 'H?gel' rausfallen. (0..1)",
                 Order = 13)]
        [Range(0.0, 1.0)]
        public decimal MinProminence { get; set; } = 0.14m;

        [Display(Name = "Min. Volumenanteil", 
                 GroupName = "MicroComposite ? Einstellungen",
                 Description = "Filtert Zonen mit sehr wenig Volumenanteil. (0..1)",
                 Order = 14)]
        [Range(0.0, 1.0)]
        public decimal MinVolShare { get; set; } = 0.005m;

        [Display(Name = "Min. Breite relativ VA", 
                 GroupName = "MicroComposite ? Einstellungen",
                 Description = "Mindestbreite einer Zone relativ zur Value-Area-Breite (0..1)",
                 Order = 15)]
        [Range(0.0, 1.0)]
        public decimal MinWidthPctVA { get; set; } = 0.02m;

        [Display(Name = "Merge-Gap (Ticks)", 
                 GroupName = "MicroComposite ? Einstellungen",
                 Description = "Zonen in diesem Tick-Abstand werden zusammengef?hrt. Klein halten, damit benachbarte Kandidaten/Zonen nicht zu einer sehr breiten Zone zusammengef?hrt werden.",
                 Order = 16)]
        [Range(0, 20)]
        public int GapTicks { get; set; } = 3;

        [Display(Name = "Max. Distanzgewicht (Ticks)", 
                 GroupName = "MicroComposite ? Einstellungen",
                 Description = "Skalierung der Entfernung zum aktuellen Preis im Score",
                 Order = 20)]
        [Range(1, 100)]
        public int MaxDistTicks { get; set; } = 20;

        [Display(Name = "Smoothing (Ticks)", 
                 GroupName = "MicroComposite ? Einstellungen",
                 Description = "Triangular Smoothing-Spanne f?r die Volumenreihe. Weniger Gl?ttung macht Peaks schmaler und Zonen k?rzer.",
                 Order = 21)]
        [Range(1, 50)]
        public int SmoothTicks { get; set; } = 3;

        // Zonen-Begrenzungs-Parameter
        [Display(Name = "Zonen an Value Area klemmen", 
                 GroupName = "MicroComposite ? Einstellungen",
                 Description = "Schneidet alle HVN/LVN-Zonen an den Value-Area-Grenzen (VAL/VAH) zu. Dies verhindert, dass Zonen ?ber die wichtigsten Handelsbereiche hinausragen und sorgt f?r saubere, definierte Zonengrenzen.",
                 Order = 22)]
        public bool ClampZonesToVA { get; set; } = false;

        [Display(Name = "Zonenbreite begrenzen aktiv", 
                 GroupName = "MicroComposite ? Einstellungen",
                 Description = "Aktiviert eine harte Obergrenze f?r die maximale Breite von HVN/LVN-Zonen. N?tzlich um ?berbreite Zonen zu vermeiden, die durch Volumen-Schwankungen entstehen k?nnen.",
                 Order = 23)]
        public bool EnableCapZoneWidth { get; set; } = false;

        [Display(Name = "Maximale Zonenbreite (Ticks)", 
                 GroupName = "MicroComposite ? Einstellungen",
                 Description = "Die maximale Breite einer HVN/LVN-Zone in Ticks, symmetrisch um die Zonenmitte. Kleinere Werte erzeugen engere, pr?zisere Zonen; gr??ere Werte erlauben breitere Handelsbereiche.",
                 Order = 24)]
        [Range(1, 50)]
        public int CapZoneWidthTicks { get; set; } = 6;

        // MicroComposite ? Weg-Frei-Einstellungen (Handelslogik)
        [Display(Name = "Mindest-LVNs im Pfad", 
                 GroupName = "MicroComposite ? Weg-Frei-Einstellungen",
                 Description = "Wie viele LVN-Korridore (Low Volume Nodes) m?ssen im Preispfad vorhanden sein, damit ein Handel als g?ltig gilt. LVNs sind 'd?nne' Stellen im Volumenprofil, die der Preis leicht durchqueren kann. H?here Werte machen die Strategie selektiver.",
                 Order = 1)]
        [Range(0, 10)]
        public int RequiredLVNsInPath { get; set; } = 1;

        [Display(Name = "VA-Kanten au?erhalb Value lockern", 
                 GroupName = "MicroComposite ? Weg-Frei-Einstellungen",
                 Description = "Wenn der aktuelle Preis au?erhalb der Value Area (VA) liegt, werden die VA-Kanten (VAL/VAH) als weniger strenge Blocker behandelt. Dies erm?glicht Trades, auch wenn der Preis kurz au?erhalb der wichtigsten Handelszone ist.",
                 Order = 2)]
        public bool RelaxVAEdgesWhenOutsideValue { get; set; } = true;

        [Display(Name = "HVN-St?rke vs. POC (%)", 
                 GroupName = "MicroComposite ? Weg-Frei-Einstellungen",
                 Description = "Ein HVN (High Volume Node) gilt als 'starker Blocker', wenn sein Volumen mindestens dieser Prozentsatz des POC-Volumens betr?gt. Der POC (Point of Control) ist das Preislevel mit dem h?chsten Volumen. H?here Werte machen die Blocker-Bewertung strenger.",
                 Order = 3)]
        [Range(0.1, 1.0)]
        public decimal HVNStrengthVsPOC { get; set; } = 0.50m;

        [Display(Name = "HVN-St?rke vs. Median (Faktor)", 
                 GroupName = "MicroComposite ? Weg-Frei-Einstellungen",
                 Description = "Ein HVN gilt als 'stark', wenn sein Volumen mindestens dieser Faktor mal dem Durchschnittsvolumen aller Preislevel entspricht. Beispiel: 1.2 bedeutet, das HVN muss 20% mehr Volumen als der Durchschnitt haben. Dies hilft, wirklich signifikante Volumenpunkte zu identifizieren.",
                 Order = 4)]
        [Range(0.5, 3.0)]
        public decimal HVNStrengthVsMedian { get; set; } = 1.20m;

        [Display(Name = "Mindest-Prominenz vs. Median (%)", 
                 GroupName = "MicroComposite ? Weg-Frei-Einstellungen",
                 Description = "Die 'Prominenz' misst, wie deutlich sich ein Volumenpeak von seiner Umgebung abhebt. Dieser Wert bestimmt die minimale Prominenz im Verh?ltnis zum Medianvolumen. H?here Werte filtern nur die deutlichsten Peaks heraus und ignorieren kleine Volumenvariationen.",
                 Order = 5)]
        [Range(0.05, 0.5)]
        public decimal MinProminenceVsMedian { get; set; } = 0.15m;

        [Display(Name = "Mindest-Abstand Blocker vs. Risk (Faktor)", 
                 GroupName = "MicroComposite ? Weg-Frei-Einstellungen",
                 Description = "Ein Blocker (HVN, POC, VA-Kante) muss mindestens diesen Faktor mal dem Risk-Ticks Abstand vom aktuellen Preis entfernt sein. Beispiel: 1 bedeutet der Blocker muss weiter entfernt sein als die Risk-Distanz. H?here Werte erlauben Trades n?her an Blockern.",
                 Order = 6)]
        [Range(0.5, 5.0)]
        public int MinBlockerDistanceTicksVsRisk { get; set; } = 1;

        [Display(Name = "D_min (Ticks bis HVN/POC)", 
                 GroupName = "MicroComposite ? Weg-Frei-Einstellungen",
                 Description = "Die fundamentale Risikodistanz in Ticks. Dies ist die Basis f?r alle Weg-Frei-Berechnungen und definiert den Mindestabstand zu wichtigen Volumenleveln. H?here Werte machen die Strategie konservativer und verhindern Trades in volatilen Bereichen.",
                 Order = 7)]
        [Range(1, 100)]
        public int DMinTicks { get; set; } = 8;

        [Display(Name = "Range Lookback (Bars)", GroupName = "Entry Settings",
        Description = "f?r Setup1 Range-Definition")]
        [Range(5, 500)]
        public int RangeLookbackBars { get; set; } = 50;


        // Helpers

        private int ToTickIndex(decimal price)
        {
            if (_tickSize <= 0m) return 0;
            var k = Math.Round(price / _tickSize, MidpointRounding.AwayFromZero);
            return (int)k;
        }
        private decimal FromTickIndex(int idx) => idx * _tickSize;

        public sealed class PriceVolRow
        {
            public decimal Price { get; init; }
            public decimal Volume { get; init; }
        }


        decimal RoundToTick(decimal price)
        {
            if (_tickSize <= 0m)
            {
                this.LogInfo("[RoundToTick] _tickSize ist 0 ? Verwende ungerundeten Preis.");
                return price;
            }
            // Wichtig: AwayFromZero statt ToEven
            var k = Math.Round(price / _tickSize, MidpointRounding.AwayFromZero);
            return k * _tickSize;
        }
        private decimal TickUp(decimal price) => RoundToTick(price + _tickSize);
        private decimal TickDn(decimal price) => RoundToTick(price - _tickSize);
        // 0 => null (deaktiviert) beibehalten f?r interne decimal?-Schwellen
        // UI double -> interne decimal?; 0 bedeutet "deaktiviert" => null
        private static decimal? AsNullableThreshold(double v)
            => v == 0.0 ? (decimal?)null : (decimal)v;

        // F?r Anteile, die nie "deaktiviert" sein sollen, ggf. direkt decimal
        private static decimal AsDecimal(double v) => (decimal)v;

        private void DrawLabelOnPriceAxis(RenderContext context, string text, int y, RenderFont font, System.Drawing.Color backColor, System.Drawing.Color foreColor)
        {
            // Sicherheitscheck, falls das Label leer ist
            if (string.IsNullOrEmpty(text))
                return;

            var size = context.MeasureString(text, font);
            int x = ChartInfo.PriceChartContainer.Region.Width - size.Width - 10; // 10px Padding
            var rect = new System.Drawing.Rectangle(x, y - size.Height / 2, size.Width + 10, size.Height); // Etwas Padding f?r besseres Aussehen
            context.FillRectangle(backColor, rect); // Hintergrund f?llen
            context.DrawString(text, font, foreColor, rect.X + 5, rect.Y); // Text zeichnen
        }
        private decimal GetLastPrice()
        {
            if (_lastCalculatedBar >= 0)
            {
                var c = GetCandle(_lastCalculatedBar);
                if (c != null) return c.Close;
            }
            return 0m;
        }
        private void PerformInitialHistoricalAnalysis()
        {
            // Sicherstellen, dass diese Methode nur einmal ausgef?hrt wird.
            if (_historicalAnalysisPerformed)
                return;

            this.LogInfo("[Historische Analyse] Starte einmalige Analyse f?r unber?hrte historische Levels...");

            // Wir ben?tigen mindestens so viele Daten, wie wir zur?ckblicken wollen.
            if (CurrentBar < 2)
            {
                this.LogInfo("[Historische Analyse] Nicht gen?gend Daten f?r die Analyse vorhanden.");

                return;
            }

            var sessionStarts = new List<int>();
            for (int i = 0; i < CurrentBar; i++)
            {
                if (IsNewSession(i))
                    sessionStarts.Add(i);
            }

            // Wenn keine Sessions gefunden, abbrechen (sollte selten vorkommen)
            if (sessionStarts.Count < 2)
            {
                this.LogInfo("[Historische Analyse] Keine Sessions gefunden.");
                _historicalAnalysisPerformed = true;
                return;
            }

            int lastSessionIndex = sessionStarts.Count - 1;
            int firstSessionToAnalyzeIndex = Math.Max(0, lastSessionIndex - MaxDaysForSignificantLevels);

            // 3. Iteriere durch die historischen Sessions
            for (int i = firstSessionToAnalyzeIndex; i < lastSessionIndex; i++)
            {
                int sessionStartBar = sessionStarts[i];
                int nextSessionStartBar = sessionStarts[i + 1];
                int sessionEndBar = nextSessionStartBar - 1;
                DateTime sessionDate = GetCandle(sessionStartBar).Time.Date;

                // Berechne Hoch und Tief der Session
                decimal sessionHigh = 0;
                decimal sessionLow = decimal.MaxValue;
                for (int bar = sessionStartBar; bar <= sessionEndBar; bar++)
                {
                    var candle = GetCandle(bar);
                    sessionHigh = Math.Max(sessionHigh, candle.High);
                    sessionLow = Math.Min(sessionLow, candle.Low);
                }

                // 4. Pr?fe, ob diese Levels "unber?hrt" sind
                // Ein Level ist unber?hrt, wenn der Kurs nach seiner Entstehung nicht mehr dorthin zur?ckgekehrt ist.


                bool isHighUntouched = true;
                bool isLowUntouched = true;

                for (int bar = nextSessionStartBar; bar < CurrentBar; bar++)
                {
                    var candle = GetCandle(bar);
                    if (candle.High >= sessionHigh)
                    {
                        isHighUntouched = false;

                    }
                    if (candle.Low <= sessionLow)
                    {
                        isLowUntouched = false;
                    }
                    // Wenn beide ber?hrt wurden, k?nnen wir die Pr?fung f?r diese Session abbrechen
                    if (!isHighUntouched && !isLowUntouched) break;
                }

                // 5. F?ge die unber?hrten Levels zur Liste hinzu
                if (isHighUntouched)
                {
                    string label = $"Hoch {sessionDate:dd.MM.yy}";
                    _untouchedLevels.Add(new TrackedLevel(sessionHigh, sessionDate, label, TrackedLevel.LevelRemovalCondition.AfterMaxDays));
                    this.LogInfo($"[Historische Analyse] Unber?hrtes Hoch {label} ({sessionHigh}) hinzugef?gt.");
                }
                if (isLowUntouched)
                {
                    string label = $"Tief {sessionDate:dd.MM.yy}";
                    _untouchedLevels.Add(new TrackedLevel(sessionLow, sessionDate, label, TrackedLevel.LevelRemovalCondition.AfterMaxDays));
                    this.LogInfo($"[Historische Analyse] Unber?hrtes Tief {label} ({sessionLow}) hinzugef?gt.");
                }
                
            }

            // Abschluss-Log & Flag erst nach vollst?ndiger Verarbeitung
            this.LogInfo($"[Historische Analyse] Analyse abgeschlossen. {_untouchedLevels.Count} unber?hrte historische Levels hinzugef?gt.");
            _historicalAnalysisPerformed = true;
        }

        private decimal CalculateCounterShare(decimal askVol, decimal bidVol, decimal delta, decimal coh01, bool isBullish)
        {
            decimal totalVol = askVol + bidVol;
            if (totalVol == 0m) return 0m;

            decimal counterVol = isBullish ? bidVol : askVol;
            decimal counterShare = counterVol / totalVol; // Immer 0..1

            // Einheitliche Koh?renzlogik (Reduktion bei hoher Koh?renz)
            decimal cohImpact = 1m - (coh01 * 0.5m); // coh01=1 ? 0.5, coh01=0 ? 1.0
            return counterShare * cohImpact; // Maximal 1.0
        }

        // Optionale Erweiterung mit Footprint (falls Sie detailliertere Level-Analyse wollen, z.B. Max-Counter pro Level):
        // Ersetzen Sie CalculateCounterShare durch diese Version, die EnumerateClusterLevels nutzt.
        private decimal CalculateCounterShareWithFootprint(int barIndex, decimal coh01, bool isBullish)
        {
            var levels = EnumerateClusterLevels(barIndex).ToList();
            if (!levels.Any()) return 0m;

            decimal totalAskVol = 0m, totalBidVol = 0m;
            foreach (var level in levels)
            {
                totalAskVol += level.AskVol;
                totalBidVol += level.BidVol;
            }


            
            decimal totalVol = totalAskVol + totalBidVol;
            if (totalVol == 0m) return 0m;

            decimal counterVol = isBullish ? totalBidVol : totalAskVol;
            decimal counterShare = counterVol / totalVol; // Immer 0..1

            // Gleiche Koh?renzbehandlung
            decimal cohImpact = 1m - (coh01 * 0.5m);
            return counterShare * cohImpact; // Maximal 1.0
        }

        // Hilfs-Methode: GetVolume (exakt wie ATAS)
        private decimal GetVolume(int bar)
        {
            var candle = GetCandle(bar);
            if (candle == null) return 0m;
            return _volumeMode switch
            {
                VolumeType.Total => candle.Volume,
                VolumeType.Bid => candle.Bid,    // Falls ATAS Bid/Ask hat; sonst fallback zu Volume
                VolumeType.Ask => candle.Ask,
                _ => candle.Volume
            };
        }

        
        public Goldfluss3_3()
        {
            _loggerSource = this as ILoggerSource ?? throw new InvalidOperationException("Strategy must implement ILoggerSource or provide a logger source.");
            // Lock - Objekt: kann inline beim Feld deklariert werden; hier optional nochmal setzen
            // _ofFeaturesSync = new object(); // nur erlaubt, wenn nicht inline initialisiert
            _ofFeaturesBarList = new List<int>();
            // Falls _ofFeaturesBarMap inline schon initialisiert ist, entferne diese Zeile.
            // _ofFeaturesBarMap = new Dictionary<int, int>(); // falls nicht inline
            _ofFeaturesByBar = new Dictionary<int, OfFeatures>();
            // _ofFeaturesHistory: falls readonly und sinnvoll, initialisieren; ansonsten lass es null und handle im Code
            _ofFeaturesHistory = new OfFeaturesHistory();
            
        }


        // Wird einmalig beim Laden der Strategie aufgerufen (Beibehalten)
        protected override void OnInitialize()
        {
            base.OnInitialize();

            this.LogInfo("[OnInitialize] Goldfluss 3.3 initialisiert.");



            if (!EnableBreakEven)
            {
                _isBreakEvenCompleted = true;
                this.LogInfo("[Init] Break-Even deaktiviert ? markiere Phase als abgeschlossen f?r Trailing.");
            }
            DataSeries[0].IsHidden = true;
            DenyToChangePanel = true;
            EnableCustomDrawing = true;
            SubscribeToDrawingEvents(DrawingLayouts.Historical | DrawingLayouts.LatestBar);
            DrawAbovePrice = true;
            // =========================================================================
            // KONSTRUKTOR UND INITIALISIERUNG (korrigierte Reihenfolge)
            // =========================================================================
            _tickSize = InstrumentInfo?.TickSize ?? 0.25m;
            object loggerSource = this;
            

            // Initialisiere den SessionBarRangeFinder
            Action<string> infoStr = msg => System.Diagnostics.Trace.WriteLine(msg);
            Action<string> warnStr = msg => System.Diagnostics.Trace.WriteLine("WARN: " + msg);
            Action<string> debugStr = msg => Trace.WriteLine("DEBUG: " + msg);


            _strategySetup = SetupConfig ?? new SetupConfiguration();






            // 1) Resolver erstellen und Feld setzen
            _thresholdsResolver = new ThresholdsResolver(_loggerSource);

            // 2) Manager mit den drei Parametern erstellen
            _thresholdManager = new OrderflowThresholdManager(_thresholdsResolver, _loggerSource, _strategySetup);

            // Erst die histories/collections anlegen, die andere Komponenten ben?tigen:
            _ovSnapshotHistory = new OvSnapshotHistory(256, _loggerSource);
            _ofFeaturesHistory = new OfFeaturesHistory(capacity: 500, this);  

            // Jetzt den Feature-Calculator anlegen, weil er die OvSnapshot-Historie und StrategyConfig braucht
            _featureCalculator = new OrderflowFeatureCalculator(_ovSnapshotHistory, _strategySetup, ofFeaturesHistory: _ofFeaturesHistory, loggerSource: _loggerSource);

            // Cluster-Statistik kann unabh?ngig sein
            _myClusterStatistic = new MyClusterStatistic();

            // Jetzt explizit initialisieren ? ?bergibt abh?ngige Ressourcen an den Manager
            _thresholdManager.Initialize(_ofFeaturesHistory, _featureCalculator, _commonConditionsSpecificConfig);

            _patternSignaturer = new PatternSignaturer(_thresholdsResolver, _thresholdManager, _loggerSource, _strategySetup, new[] { _reversalEvaluator });
            // MarketStateEngine braucht history und thresholdManager -> danach anlegen
            var mseSettings = new MarketStateEngineSettings
            {
                TickSize = _tickSize,

                TrendBodyTicks = Parameter_TrendBodyTicks,
                ReversalBodyTicks = Parameter_ReversalBodyTicks,
                BodyTickTolerance = Parameter_BodyTickTolerance,

                // optional: falls du diese ebenfalls als ATAS-Parameter anlegen willst
                // RangeWindowN = Parameter_RangeWindowN,
                // ConfirmBars = Parameter_ConfirmBars,
                // TrendRunThreshold = Parameter_TrendRunThreshold
            };
            
            _marketStateEngine = new MarketStateEngine(_ofFeaturesHistory, _thresholdManager, this, mseSettings);           
            _marketStateUpdatedHandler = OnMarketStateUpdated;
            _marketStateEngine.MarketStateUpdated += _marketStateUpdatedHandler;

            // Initialisiere den ReversalBouncePatternEvaluator
            _reversalEvaluator = new ReversalBouncePatternEvaluator(OrderDirections.Buy, this);
            
            // Adaptive Thresholds werden vom PatternSignaturer geliefert (keine statischen Overrides)
            this.LogInfo($"[INIT] ReversalThresholds initialisiert: CVD Impulse Long = {ReversalThresholds.ReversalThCvdImpulseLong_UI}");


            

            // Now prepare per-bar containers (gr??en sinnvoll initialisieren)
            _ofFeaturesByBar = new Dictionary<int, OfFeatures>(_ofFeaturesHistory != null ? Math.Max(16, _ofFeaturesHistory.Count) : 512);
            

            
            // ... (Deine bestehenden Initialisierungen in Configure f?r _featureCalculator etc.) ...

            Add(_myClusterStatistic);

            _squeezeCalc = new SqueezeMomentumCalculator
            {
                BBPeriod = 20,
                BBMultFactor = 2.0m,
                KCPeriod = 20,
                KCMultFactor = 1.5m
            };


            Add(_sma);
            Add(_pivots);

            Add(_dailyLines);
            Add(_dailyLevels);

            _dailyLevels.PeriodFrame = DynamicLevels.Period.Daily;
            _dailyLevels.Days = 1;
            _dailyLevels.Type = DynamicLevels.MiddleClusterType.Volume;
            _dailyLevels.Filter = 0;

            _vwap.VWAPOnly = !ShowVWAPBands;

            _vwap.StDev = 1;
            _vwap.StDev1 = 2;
            _vwap.StDev2 = 3;
            _vwap.Type = VWAP.VWAPPeriodType.Daily;

            Add(_vwap);

            DataSeries.Add(_vwap.DataSeries[0]);

            if (ShowVWAPBands)
            {
                DataSeries.Add(_vwap.DataSeries[6]); // Upper Std1
                DataSeries.Add(_vwap.DataSeries[5]); // Lower Std1
                DataSeries.Add(_vwap.DataSeries[4]); // Upper Std2
                DataSeries.Add(_vwap.DataSeries[3]); // Lower Std2
                DataSeries.Add(_vwap.DataSeries[2]); // Upper Std3
                DataSeries.Add(_vwap.DataSeries[1]); // Lower Std3
            }

            _prevSessionSnapshot = null;
            _currentVwapSnapshot = null;

            if (EnableIsBlocked)
            {
                PerformInitialHistoricalAnalysis();
            }

            
            _sma.Period = Math.Max(1, SmaPeriod);

            DataSeries.Add(_entrySignalSeries);

            DataSeries.Add(_imbalanceSeries);

            

            _pivots.PivotRange = Pivots.Period.Daily;

            DataSeries[0].IsHidden = true;





            _pullbackOrder = _tpOrder = _slOrder = _entryOrder = null;
            _positionOpen = false;
            _isExitPlacementPending = false;
            _entryBarIndex = _fillBarIndex = -1;
            _armedBarIndex = -1;
            _signalCheckedForThisBarFirstTick = false;
            _lastProcessedBarIndex = -1;

            // Reset day data
            _previousDayOpen = _previousDayHigh = _previousDayLow = _previousDayClose = 0;
            _currentDayOpen = _currentDayHigh = _currentDayLow = _currentDayClose = 0;

            _tpSlCalculator = new TpSlCalculator();

            _tickSize = InstrumentInfo?.TickSize ?? 0.25m;  // Fallback-Wert, z.B. f?r Gold-Futures (anpassen an dein Instrument!)
            if (_tickSize == 0m)
            {
                this.LogInfo("[OnInitialize] TickSize konnte nicht initialisiert werden ? Fallback auf 0.25 verwendet.");
            }
                        

            if (_researchEnabled)
            {
                string instrumentName = (InstrumentInfo?.Instrument ?? InstrumentInfo?.ToString() ?? "Unknown");
                foreach (var ch in System.IO.Path.GetInvalidFileNameChars())
                    instrumentName = instrumentName.Replace(ch, '_');

                string outDir = @"C:\Users\User\source\repos\Scalping-Strategie\TradeManagement";
                System.IO.Directory.CreateDirectory(outDir); // sicherstellen, dass Ordner existiert

                string csvPath = System.IO.Path.Combine(outDir, $"{instrumentName}_{_researchTimeframeLabel}_research.csv");
                decimal tickSize = 0.25m; // z. B. Instrument.MasterInstrument.TickSize;
                int outcomeTicks = 12;    // dein TP-Benchmark
                int stopTicks = 10;       // dein SL-Benchmark

                _research = new ResearchCollector(
                    tickSize,
                    outcomeTicks,
                    stopTicks,
                    @"C:\Users\User\Documents\Strategieauswertung"
                );


            }

           

            // CSV-Export vorbereiten, aber erst im OnCalculate() wirklich initialisieren
            if (EnableCsvExport)
            {
                // Instrumentnamen sicher für Dateiname machen
                string instrumentName = (InstrumentInfo?.Instrument ?? InstrumentInfo?.ToString() ?? "Unknown");
                foreach (var ch in System.IO.Path.GetInvalidFileNameChars())
                    instrumentName = instrumentName.Replace(ch, '_');

                // Ausgabeordner
                string outDir = @"C:\Users\User\Documents\Strategieauswertung";
                System.IO.Directory.CreateDirectory(outDir);

                // Dateiname generieren basierend auf Einstellungen
                string timeframeLabel = _researchTimeframeLabel ?? "TF"; // falls du ein Label hast
                
                if (UseDailyCsvFiles)
                {
                    // NOCH NICHTS erstellen - warten auf erste gültige Bar im OnCalculate()
                    this.LogInfo("[OnInitialize] CSV Export enabled - will be initialized on first valid bar");
                }
                else
                {
                    // Ursprüngliches Verhalten - statischer Dateiname
                    _csvPath = System.IO.Path.Combine(outDir, $"{instrumentName}_{timeframeLabel}_ovsnapshots.csv");
                    
                    // Falls Überschreiben aktiviert und Datei existiert, löschen
                    if (OverwriteExistingCsv && System.IO.File.Exists(_csvPath))
                    {
                        try
                        {
                            System.IO.File.Delete(_csvPath);
                            this.LogInfo($"[OnInitialize] Existing CSV file deleted: {_csvPath}");
                        }
                        catch (Exception ex)
                        {
                            this.LogWarn($"[OnInitialize] Could not delete existing CSV file {_csvPath}: {ex.Message}");
                        }
                    }

                    // Erzeuge BackgroundCsvWriter (schreibt Header falls Datei neu)
                    _csvWriter = new BackgroundCsvWriter(_csvPath, CsvHeader);
                    this.LogInfo($"[InitializeDailyCsvWriter] CSV Writer created -> path={_csvPath}");
                    
                    // Aktuelles Datum für Tageswechsel-Erkennung speichern
                    _currentCsvDate = GetCurrentBarDate();
                }
            }



        }


        // =========================================================================
        // Volatilit?t ermitteln | Anfang
        // =========================================================================
        private MarketRegimeDetails GetCurrentMarketRegime(int bar, OvSnapshot currentSnapshot)
        {
            // Holen Sie sich die aktuellen Kerzen f?r die Bar 'bar'
            var c = GetCandle(bar);
            var p = GetCandle(bar - 1); // Vorherige Kerze f?r secondsPerBar

            if (c == null || p == null)
            {
                this.LogWarn($"[MarketRegime] Regime kann nicht bestimmt werden: Kerzendaten fehlen f?r bar {bar}. Standardm??ig auf ?Normal?.");
                return new MarketRegimeDetails { Regime = MarketRegime.Normal };
            }

            if (_myClusterStatistic == null)
            {
                this.LogInfo("[MarketRegime] Volatilit?t GESPERRT: _myClusterStatistic ist null. Standardm??ig auf ?Normal\" gesetzt.");
                return new MarketRegimeDetails { Regime = MarketRegime.Normal };
            }
            if (_myClusterStatistic.VolPerSecond.Count <= bar || _myClusterStatistic.EmaVolPerSecond.Count <= bar)
            {
                this.LogInfo($"[MarketRegime] Volatilit?t GESPERRT: Indikatorreihe nicht bereit (bar={bar}). Standardm??ig auf ?Normal\".");
                return new MarketRegimeDetails { Regime = MarketRegime.Normal };
            }

            // --- bestehende Einzelbar-Werte ---
            decimal vps = _myClusterStatistic.VolPerSecond[bar];
            decimal ema = _myClusterStatistic.EmaVolPerSecond[bar];
            decimal std = (_myClusterStatistic.EmaVolPerSecondStd != null && _myClusterStatistic.EmaVolPerSecondStd.Count > bar)
                ? _myClusterStatistic.EmaVolPerSecondStd[bar] : 0m;

            decimal z = (std > 0m) ? (vps - ema) / std : 0m;
            decimal tradesPerSec = currentSnapshot.TradeRateZ; // Annahme: TradeRateZ ist hier verf?gbar und aktuell

            decimal secondsPerBar = 0m;
            if (c.Time > p.Time)
            {
                secondsPerBar = (decimal)(c.Time - p.Time).TotalSeconds;
            }
            else
            {
                this.LogWarn($"[MarketRegime] Ung?ltige Zeitspanne f?r Balken {bar}. Standardwert f?r secondsPerBar auf 0.");
                secondsPerBar = 0m;
            }

            // --- Konstanten (k?nnen Sie als Felder konfigurieren) ---
            const decimal VOL_FAST_Z = 1.0m;
            const decimal VOL_SLOW_Z = -1.0m;
            const decimal TR_FAST_MIN = 1.0m;     // Beispiel
            const decimal TR_SLOW_MAX = -1.0m;    // Beispiel
            const decimal BARSEC_FAST_MAX = 5.0m; // Beispiel
            const decimal BARSEC_SLOW_MIN = 45.0m;// Beispiel

            // --- Phase-basierte Volatilit?tsbewertung (SMA ?ber Fenster + Hysterese/Persistenz) ---
            // Erwartete private Felder in der Klasse (Defaults siehe Kommentar weiter unten):
            // int _volPhaseWindow = 14;
            // int _minConsecutiveForSwitch = 3;
            // decimal _highEnterMult = 1.2m;
            // decimal _highExitMult = 1.05m;
            // decimal _lowEnterMult = 0.8m;
            // decimal _lowExitMult = 0.95m;
            // MarketRegime _lastRegime = MarketRegime.Normal;
            // int _regimeConsecutiveCount = 0;

            int volPhaseWindow = Math.Max(1, _volPhaseWindow); // sch?tze gegen 0
            int startIdx = Math.Max(0, bar - volPhaseWindow + 1);
            int count = bar - startIdx + 1;

            var values = new List<decimal>(count);
            for (int i = startIdx; i <= bar; i++)
            {
                if (_myClusterStatistic.VolPerSecond.Count > i)
                    values.Add(_myClusterStatistic.VolPerSecond[i]);
                else
                    values.Add(0m);
            }

            decimal phaseMu = 0m;
            decimal phaseSigma = 0m;
            if (values.Count > 0)
            {
                decimal sum = 0m;
                foreach (var vv in values) sum += vv;
                phaseMu = sum / values.Count;

                decimal varSum = 0m;
                foreach (var vv in values) varSum += (vv - phaseMu) * (vv - phaseMu);
                phaseSigma = (decimal)Math.Sqrt((double)(varSum / values.Count)); // population std
            }

            decimal vpsCurrent = vps;
            decimal zPhase = (phaseSigma > 0m) ? (vpsCurrent - phaseMu) / phaseSigma : 0m;

            // Hysteresis-Multiplikatoren (als Felder konfigurierbar)
            decimal lowEnterMult = _lowEnterMult;
            decimal lowExitMult = _lowExitMult;
            decimal highEnterMult = _highEnterMult;
            decimal highExitMult = _highExitMult;

            decimal lowBandPhase = phaseMu * lowEnterMult;
            decimal lowBandExit = phaseMu * lowExitMult;
            decimal highBandPhase = phaseMu * highEnterMult;
            decimal highBandExit = phaseMu * highExitMult;

            // Unterst?tzende Stimmen (erhalten bleiben, nutzen aber Phase-B?nder)
            int fastVotesLocal = 0, slowVotesLocal = 0;
            if (vpsCurrent >= highBandPhase) fastVotesLocal++; else if (vpsCurrent < lowBandPhase) slowVotesLocal++;
            if (phaseSigma > 0m && zPhase >= VOL_FAST_Z) fastVotesLocal++; else if (phaseSigma > 0m && zPhase <= VOL_SLOW_Z) slowVotesLocal++;
            if (tradesPerSec >= TR_FAST_MIN) fastVotesLocal++; else if (tradesPerSec <= TR_SLOW_MAX) slowVotesLocal++;
            if (secondsPerBar > 0m && secondsPerBar <= BARSEC_FAST_MAX) fastVotesLocal++; else if (secondsPerBar >= BARSEC_SLOW_MIN) slowVotesLocal++;

            string candidateSpeed = (fastVotesLocal >= 2 && slowVotesLocal < 2) ? "Fast" :
                                     (slowVotesLocal >= 2 && fastVotesLocal < 2) ? "Slow" : "Normal";

            MarketRegime candidateRegime = MapMarketSpeedToEnum(candidateSpeed);

            // Persistenz / Hysterese-Logik: require consecutive confirmations before switching
            if (candidateRegime == _lastRegime)
            {
                _regimeConsecutiveCount++;
            }
            else
            {
                _regimeConsecutiveCount = 1;
            }

            bool acceptSwitch = false;
            switch (candidateRegime)
            {
                case MarketRegime.Fast:
                    if (_lastRegime == MarketRegime.Fast)
                    {
                        acceptSwitch = (vpsCurrent >= highBandExit) || (_regimeConsecutiveCount >= _minConsecutiveForSwitch);
                    }
                    else
                    {
                        acceptSwitch = (vpsCurrent >= highBandPhase && _regimeConsecutiveCount >= _minConsecutiveForSwitch)
                                       || (vpsCurrent >= highBandPhase * 1.2m); // sehr starker Impuls
                    }
                    break;

                case MarketRegime.Slow:
                    if (_lastRegime == MarketRegime.Slow)
                    {
                        acceptSwitch = (vpsCurrent <= lowBandExit) || (_regimeConsecutiveCount >= _minConsecutiveForSwitch);
                    }
                    else
                    {
                        acceptSwitch = (vpsCurrent <= lowBandPhase && _regimeConsecutiveCount >= _minConsecutiveForSwitch)
                                       || (vpsCurrent <= lowBandPhase * 0.8m);
                    }
                    break;

                default: // Normal
                         // Normal, falls weder Fast noch Slow gen?gend Best?tigung haben
                    acceptSwitch = (_regimeConsecutiveCount >= _minConsecutiveForSwitch) || (_lastRegime == MarketRegime.Normal);
                    break;
            }

            MarketRegime finalRegime;
            if (acceptSwitch)
            {
                finalRegime = candidateRegime;
                _lastRegime = finalRegime;
            }
            else
            {
                finalRegime = _lastRegime;
            }

            // Update aktuelle Felder (phaseMu statt kurzer EMA als Durchschnitt verwenden)
            _currentBarVolPerSecond = vpsCurrent;
            _currentBarAvgVolPerSecond = phaseMu;

            this.LogInfo($"[MarketRegime] PhaseVol: bar={bar + 1} VPS={vpsCurrent:F2} PhaseMu={phaseMu:F2} PhaseStd={phaseSigma:F2} zPhase={zPhase:F2} Candidate={candidateSpeed} Final={finalRegime} Count={_regimeConsecutiveCount}");

            var finalRegimeDetails = new MarketRegimeDetails
            {
                Regime = finalRegime,
                FastVotes = fastVotesLocal,
                SlowVotes = slowVotesLocal,
                IsHighVol = finalRegime == MarketRegime.Fast,
                Vps = vpsCurrent,
                VpsEma = phaseMu,
                VpsStd = phaseSigma,
                ZScore = zPhase,
                TradesPerSecZ = tradesPerSec,
                SecondsPerBar = secondsPerBar
            };

            return finalRegimeDetails;

        }

        // Hilfsmethode zur Abbildung des Strings auf das Enum
        private MarketRegime MapMarketSpeedToEnum(string marketSpeedString)
        {
            switch (marketSpeedString)
            {
                case "Fast":
                    return MarketRegime.Fast;
                case "Slow":
                    return MarketRegime.Slow;
                case "Normal":
                default:
                    return MarketRegime.Normal;
            }
        }
        // =========================================================================
        // Volatilit?t ermitteln | Ende
        // =========================================================================


        // Window = VolZ_Lookback, Alpha = EwmaAlpha
        // Diese Funktion ist daf?r gedacht, dein CVD-Impuls-Signal in ein Z-normalisiertes Ma? zu bringen ? analog zu Volumen-Z oder TradeRate-Z ? aber auf Basis einer eigenen Historienstruktur (_ofFeaturesHistory), nicht auf deinem ?blichen Queue/Window.
        private decimal ComputeCvdZNormFromHistory(decimal currentImpulse)
        {
            if (_ofFeaturesHistory == null || _ofFeaturesHistory.IsEmpty) return 0m;

            int cap = Math.Max(10, VolZ_Lookback);
            var window = new List<decimal>(cap);

            int avail = Math.Min(_ofFeaturesHistory.Available, cap);
            for (int i = -avail; i < 0; i++)
            {
                if (_ofFeaturesHistory.TryGetRelative(i, out var s))
                    window.Add(s.CvdImpulse);
            }

            if (window.Count == 0) return 0m;

            decimal z = RobustifiedExponentialZScore(
                /* windowObj: */ window,
                /* alpha: */ (double)EwmaAlpha,
                /* x: */ currentImpulse,
                /* useRobustScale: */ true,
                /* robustBlend: */ 0.8,
                /* maxAbsZ: */ 50.0,
                /* madEps: */ 1e-12
            );

            return Clamp(z, -8m, 8m);
        }



        private void UpdateLastClosedOv(OvSnapshot snapshot)
        {
            ovSnapshot = snapshot;
            _hasOvLastClosed = true;
        }

        private bool TryGetCandleSafe(int idx, out IndicatorCandle? ic)
        {
            ic = null;

            var last = CurrentBar;
            if (last < 0) return false;
            if (idx < 0 || idx > last) return false;

            ic = GetCandle(idx); // liefert IndicatorCandle in deiner Build
            return ic != null;
        }

        private bool TryGetBarIndices(int bar, out int bLive, out int bClosed)
        {
            bLive = -1; bClosed = -1;
            var last = CurrentBar;
            if (last < 0) return false;
            bLive = Math.Min(bar, last);
            bClosed = bLive - 1;
            if (bClosed < 0) return false;
            return true;
        }

        private decimal GetSecondsForBar(int idx, CandleSnap s)
        {
            var secs = (decimal)(s.LastTime - s.Time).TotalSeconds;
            if (secs <= 0)
            {
                if (_myClusterStatistic?.CandleDurations?.Count > idx)
                {
                    var d = _myClusterStatistic.CandleDurations[idx];
                    if (d > 0) return d;
                }
                return 1m; // harte Untergrenze
            }
            return secs;
        }


        private IndicatorCandle TryGetCandleAtOrBefore(int requestedIdx)
        {
            for (int i = requestedIdx; i >= 0; i--)
            {
                try
                {
                    return GetCandle(i);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Index noch nicht verf?gbar -> eine Kerze fr?her probieren
                    continue;
                }
            }
            return null;
        }

        private bool TryExtractCandle(object raw, out ATAS.Indicators.IndicatorCandle? ic, out ATAS.Indicators.Candle? c)
        {
            ic = AsIndicatorCandle(raw);
            c = AsBaseCandle(raw);
            if (ic != null || c != null) return true;

            var t = raw.GetType();
            // nur einmalig nach bekannten Property-Namen suchen
            var prop = t.GetProperty("Candle", BindingFlags.Instance | BindingFlags.Public)
                    ?? t.GetProperty("BaseCandle", BindingFlags.Instance | BindingFlags.Public)
                    ?? t.GetProperty("InnerCandle", BindingFlags.Instance | BindingFlags.Public);

            if (prop != null)
            {
                var inner = prop.GetValue(raw);
                ic = AsIndicatorCandle(inner!);
                c = AsBaseCandle(inner!);
                return ic != null || c != null;
            }
            return false;
        }
        
        
        // Deine bestehende Methode erweitern (in deiner Strategy-Klasse)
        private decimal ReadVwapSeriesSafely(int seriesIndex, int bar)
        {
            var count = _vwap?.DataSeries?.Count ?? 0;
            if (_vwap == null || _vwap.DataSeries == null || count <= seriesIndex || bar < 0)
            {
                this.LogWarn($"[VWAP SafeRead ERROR] Invalid params: bar={bar}, idx={seriesIndex}, Count={count}");
                return 0m;
            }

            try
            {
                // Guard: Wenn der Indikator f?r diesen Bar noch nicht berechnet hat, lies ggf. bar-1
                object rawObj = _vwap.DataSeries[seriesIndex][bar];
                decimal val = rawObj != null ? Convert.ToDecimal(rawObj) : 0m;

                if (val == 0m && bar > 0)
                {
                    var prevObj = _vwap.DataSeries[seriesIndex][bar - 1];
                    var prevVal = prevObj != null ? Convert.ToDecimal(prevObj) : 0m;
                    if (prevVal != 0m)
                    {
                        //this.LogDebug($"[VWAP SafeRead] Current bar={bar} not ready for Series[{seriesIndex}]. Using bar-1 value.");
                        return prevVal;
                    }
                }

                if (bar % 10 == 0 || val == 0m)
                    //this.LogInfo($"[VWAP SafeRead DEBUG] Bar={bar}, Series[{seriesIndex}] raw={rawObj} ? val={val:F4} (Zero? {val == 0m})");

                if (double.IsNaN((double)val) || val <= 0m)
                    return 0m;

                return val;
            }
            catch (Exception ex)
            {
                this.LogError($"[VWAP SafeRead ERROR] Series[{seriesIndex}][{bar}] ex: {ex.Message}");
                return 0m;
            }
        }

        


        //Diese Methode wird von der Handelsplattform(z.B.ATAS) automatisch f?r jeden einzelnen Trade aufgerufen, der im Markt ausgef?hrt wird.
        //Ihre einzige Aufgabe ist es, die Anzahl der Kauf- und Verkaufsgesch?fte innerhalb der aktuellen Kerze (Bar) zu z?hlen.

        protected override void OnNewTrade(MarketDataArg args)
        {
            // Kein "IsTrade"-Check n?tig, da die Methode nur f?r Trades aufgerufen wird
            if (args.Direction.ToString() == "Buy")
            {
                _currentBarBuyTrades++;
            }
            else
            {
                // Dies f?ngt "Sell" ab
                _currentBarSellTrades++;
            }
        }

        // Wert oder 0m (Dictionary)
        private static decimal GetOr0(Dictionary<int, decimal>? d, int i)
            => (d != null && d.TryGetValue(i, out var v)) ? v : 0m;

       
        // Bool aus Dictionary, oder false
        private static bool GetOrFalse(Dictionary<int, bool>? d, int i)
            => (d != null && d.TryGetValue(i, out var v)) && v;

        // Key-Check (falls weiterhin ben?tigt)
        private static bool HasKey<TKey>(Dictionary<int, TKey>? d, int i)
            => d != null && d.ContainsKey(i);



        //volZ(Volumen Z-Score) :
        //Misst, wie stark das Volumen der aktuellen Kerze vom Durchschnitt der letzten VolZ_Lookback(100) Kerzen abweicht.
        //Berechnung: decimal volZ = ZScore(vol, _volWin);
        //Ein hoher Wert(> 1.8 im Log-Beispiel) deutet auf einen signifikanten Volumen-Burst hin.
        //cvdCoherence(CVD Coherence):
        //Misst die Korrelation zwischen der Preisbewegung(?Price) und der Bewegung des kumulativen Deltas(?CVD) ?ber die letzten Coherence_Lookback(50) Kerzen.Der Wert wird auf eine Skala von 0 bis 1 gemappt.
        //Berechnung: decimal coh01 = (corr + 1m) / 2m;
        //Ein hoher Wert(> 0.65) bedeutet, dass Preis und Delta stark in die gleiche Richtung laufen(z.B.steigender Preis bei positivem Delta), was auf einen gesunden Trend hindeutet.
        //aggPressure(Aggregated Pressure):
        //Gibt den prozentualen Anteil des aggressiven Kaufvolumens(Ask-Volumen) am Gesamtvolumen(Ask + Bid) an.
        //Berechnung: decimal pressure = (askVol + bidVol) > 0 ? askVol / (askVol + bidVol) : 0.5m;
        //Ein Wert nahe 1 bedeutet hohen Kaufdruck, ein Wert nahe 0 bedeutet hohen Verkaufsdruck.F?r einen Long-Trade wird ein hoher Wert(> 0.64) erwartet.
        //tradeRateZ(Trade Rate Z-Score):
        //Misst, wie stark die Anzahl der Trades pro Sekunde von der durchschnittlichen Rate der letzten TradeRateZ_Lookback(100) Kerzen abweicht.
        //Berechnung: decimal tradeRateZ = ZScore(tradeRate, _tradeRateWin);
        //Ein hoher Wert(> 1.5) signalisiert eine stark erh?hte Handelsaktivit?t.
        //efficiency(Efficiency Ratio):
        //Die Kaufman Efficiency Ratio(ER) misst die Effizienz der Preisbewegung.Sie vergleicht die Netto-Preisbewegung ?ber einen Zeitraum(ER_Lookback = 20 Kerzen) mit der Summe der absoluten Preisbewegungen in diesem Zeitraum.
        //Berechnung: decimal ER = (sumAbs > 0 && bar >= ER_Lookback) ? Clamp(netChange / sumAbs, 0m, 1m) : 0m;
        //Ein hoher Wert(> 2.0 - scheint hier ein anderer Ma?stab als der Standard 0-1 zu sein) deutet auf eine trendstarke, effiziente Bewegung hin, w?hrend ein niedriger Wert auf eine seitw?rts gerichtete, ineffiziente Bewegung hindeutet.
        //MaxCounterShareBull, MaxCounterShareBear
        //Die Methode berechnet den Wert von MaxCounterShareBull, MaxCounterShareBear als angepassten Anteil des Counter-Volumens(Bid bei bullisher Richtung, Ask bei bearisher) am Gesamtvolumen der Cluster-Levels eines Bars, wobei der Anteil um einen Koh?renz-Faktor(basierend auf coh01) reduziert wird,
        //um einen Wert zwischen 0 und 1 zu erzeugen.Falls keine Levels oder Volumen vorhanden sind, gibt sie 0 zur?ck.

        // Diese Methode dient als Adapter zur Cluster-API der Plattform. Ihr Zweck ist es, die detaillierten Volumendaten auf jeder einzelnen Preisebene innerhalb einer bestimmten Kerze (barIndex) zu extrahieren. Man nennt dies auch das "Footprint" der Kerze.

        IEnumerable<ClusterLevel> EnumerateClusterLevels(int barIndex)
        {
            // 1) Hole die Indikator-Kerze f?r den gegebenen Index.
            // "pc" ist bereits das Objekt, das wir brauchen.
            var pc = GetCandle(barIndex);
            if (pc == null)
            {
                yield break;  // Oder return Enumerable.Empty<ClusterLevel>();
            }

            // 2) Iteriere direkt ?ber die Preis-Level der geholten Kerze "pc".
            // Die komplizierte Erstellung einer neuen IndicatorCandle ist nicht notwendig.
            //this.LogInfo($"[EnumerateClusterLevels] Versuche, ?ber pvi in pc.GetAllPriceLevels() f?r barIndex {barIndex} zu iterieren...");

            foreach (var pvi in pc.GetAllPriceLevels())
            {
                //this.LogInfo($"[EnumerateClusterLevels] Innerhalb der pvi-Schleife. Price: {pvi.Price}, Volume: {pvi.Volume}");

                decimal price = RoundToTick(pvi.Price);
                decimal vol = 0m;

                if (pvi.Volume > 0)
                    vol = pvi.Volume;
                else if (pvi.Ask > 0 || pvi.Bid > 0)
                    vol = pvi.Ask + pvi.Bid;

                if (vol > 0)
                    yield return new ClusterLevel
                    {
                        Price = price,
                        TotalVol = vol,
                        AskVol = pvi.Ask,  // Direkt aus pvi
                        BidVol = pvi.Bid   // Direkt aus pvi
                    };
            }

            //this.LogInfo($"[EnumerateClusterLevels] Iteration ?ber pvi f?r barIndex {barIndex} abgeschlossen.");
        }


        // =========================================================================
        // Volumen | rollierendes Profil | Anfang
        // =========================================================================
        SortedDictionary<decimal, decimal> NormalizeToTicks(SortedDictionary<decimal, decimal> h, decimal tick, Func<decimal, decimal> round)
        {
            var n = new SortedDictionary<decimal, decimal>();
            if (h == null) return n;
            foreach (var kv in h)
            {
                var p = round(kv.Key);
                if (!n.ContainsKey(p)) n[p] = kv.Value; else n[p] += kv.Value;
            }
            return n;
        }


        //Diese Methode nimmt das von UpdateMicroCompositeRolling aufbereitete Histogramm und f?hrt eine vollst?ndige Analyse durch, um ein "Micro-Composite"-Profil zu erstellen.Dieses Profil identifiziert die wichtigsten Preislevel basierend auf dem Volumen.
        //Gl?ttung(Smoothing): Zuerst wird das Histogramm optional gegl?ttet(smoothTicks > 0). Dabei wird der Volumenwert jedes Preislevels durch den Durchschnitt der umliegenden Preislevel ersetzt.Dies reduziert Rauschen und macht die Hauptvolumenbereiche deutlicher.
        //POC(Point of Control): Findet das Preislevel mit dem absolut h?chsten Volumen im(gegl?tteten) Histogramm.
        //Value Area(VAH/VAL): Berechnet den Preisbereich, in dem 70% des gesamten Volumens im Fenster gehandelt wurden.Das Ergebnis sind die Obergrenze(VAH - Value Area High) und die Untergrenze(VAL - Value Area Low).
        //HVNs(High Volume Nodes) : Identifiziert weitere signifikante Preislevel mit hohem Volumen.Dies sind die "Peaks" im Volumenprofil, die eine bestimmte Mindestprominenz(minProminence) im Vergleich zum POC-Volumen aufweisen.
        //LVNs(Low Volume Nodes): Identifiziert Preislevel mit sehr niedrigem Volumen.Die Methode verwendet hier eine einfache Heuristik, indem sie nach "T?lern" (lokalen Minima) im Volumenprofil sucht, bei denen ein Preislevel weniger Volumen hat als seine direkten Nachbarn.

        // Hauptfunktion angepasst: VA aus Rohdaten, Peaks optional aus Rohdaten (ATAS) oder Gl?ttung nur f?r Darstellung
        // Optional: Volumenprofil des noch nicht fertigen Bars (wird NUR f?r VAH/VAL ausgeschlossen)
        MicroComposite BuildMicroCompositeFromHist(
            SortedDictionary<decimal, decimal> hist,
            int smoothTicks,                 // wird intern von this.SmoothTicks ersetzt
            int topNPeaks,                   // nicht ben?tigt; Scoring nutzt TopNHVNs/TopNLVNs
            SortedDictionary<decimal, decimal> developingHist = null,
            decimal valueAreaFraction = 0.70m)
        {
            var mc = new MicroComposite();

            // Guards
            if (hist == null || hist.Count == 0) { this.LogInfo("[BuildMicroCompositeFromHist] MC: hist leer -> return"); return mc; }

            decimal tick = (_tickSize > 0m) ? _tickSize : 0m;
            if (tick <= 0m) { this.LogInfo("[BuildMicroCompositeFromHist] MC: TickSize noch 0/unbekannt -> Abbruch"); return mc; }

            // Men?-Parameter einlesen (Chart-Menu steuert Verhalten)
            int minZoneTicks = Math.Max(1, this.MinZoneTicks);
            decimal minProminence = Math.Max(0m, this.MinProminence);
            decimal minVolShare = Math.Max(0m, this.MinVolShare);
            decimal minWidthPctVA = Math.Max(0m, this.MinWidthPctVA);
            int gapTicks = Math.Max(0, this.GapTicks);
            int topNHVNs = Math.Max(1, this.TopNHVNs);
            int topNLVNs = Math.Max(1, this.TopNLVNs);
            int maxDistTicks = Math.Max(1, this.MaxDistTicks);
            int menuSmooth = Math.Max(1, this.SmoothTicks); // nutzt das Men?

            // NEU: Men?schalter f?r Cap/Klemmung
            bool clampToVA = this.ClampZonesToVA;
            bool enableCap = this.EnableCapZoneWidth;
            int capTicks = Math.Max(1, this.CapZoneWidthTicks);

            // AwayFromZero wie ATAS
            decimal RoundToTick(decimal p)
            {
                if (tick <= 0m) return p;
                var q = p / tick;
                var r = Math.Round(q, 0, MidpointRounding.AwayFromZero);
                return r * tick;
            }

            SortedDictionary<decimal, decimal> NormalizeToTicks(SortedDictionary<decimal, decimal> h)
            {
                var n = new SortedDictionary<decimal, decimal>();
                if (h == null) return n;
                foreach (var kv in h)
                {
                    var p = RoundToTick(kv.Key);
                    if (!n.ContainsKey(p)) n[p] = kv.Value; else n[p] += kv.Value;
                }
                return n;
            }

            var normAll = NormalizeToTicks(hist);
            var normDev = NormalizeToTicks(developingHist);

            decimal sumAll = normAll.Values.Sum();
            decimal sumDev = normDev.Values.Sum();
            //this.LogInfo($"MC: Start - tick={tick}, Levels(All)={normAll.Count}, Levels(Dev)={normDev.Count}, SumAll={sumAll}, SumDev={sumDev}");
            if (normAll.Count == 0) { this.LogInfo("[BuildMicroCompositeFromHist] MC: normAll leer -> return"); return mc; }

            // Achse min..max
            var minP = normAll.Keys.Min();
            var maxP = normAll.Keys.Max();
            var prices = new List<decimal>();
            for (decimal p = minP; p <= maxP; p += tick) prices.Add(p);
            if (prices.Count == 0) { this.LogInfo("[BuildMicroCompositeFromHist] MC: prices leer -> return"); return mc; }
            int last = prices.Count - 1;
            //this.LogInfo($"MC: Axis {minP} .. {maxP} ({prices.Count} levels)");

            // Completed = All - Developing (nur f?r VA/POC)
            var normCompleted = new SortedDictionary<decimal, decimal>(normAll);
            int negClamped = 0; decimal negSum = 0m;
            if (normDev != null && normDev.Count > 0)
            {
                foreach (var kv in normDev)
                {
                    if (!normCompleted.ContainsKey(kv.Key)) continue;
                    var after = normCompleted[kv.Key] - kv.Value;
                    if (after < 0m) { negClamped++; negSum += (-after); after = 0m; }
                    normCompleted[kv.Key] = after;
                }
            }
            if (negClamped > 0) this.LogInfo($"[BuildMicroCompositeFromHist] MC: Completed negative Levels={negClamped}, clampedSum={negSum}");

            decimal AllAt(int i) => (i >= 0 && i <= last && normAll.TryGetValue(prices[i], out var vA)) ? vA : 0m;
            decimal CompletedAt(int i) => (i >= 0 && i <= last && normCompleted.TryGetValue(prices[i], out var vC)) ? vC : 0m;

            // Totals
            decimal totalCompleted = 0m, totalAll = 0m;
            for (int i = 0; i < prices.Count; i++) { totalAll += AllAt(i); totalCompleted += CompletedAt(i); }

            // Fallback nur wenn Completed==0
            bool useAllForVA = (totalCompleted == 0m);
            //this.LogInfo($"MC: Totals -> All={totalAll}, Completed={totalCompleted} (targetVA={((useAllForVA ? totalAll : totalCompleted) * valueAreaFraction)})");
            if (useAllForVA) this.LogInfo("[BuildMicroCompositeFromHist] MC: Completed == 0 -> VA/POC aus ALL (Fallback).");

            // POC (Quelle wie VA)
            int pocIdx = 0; decimal pocVol = decimal.MinValue;
            for (int i = 0; i < prices.Count; i++)
            {
                var v = useAllForVA ? AllAt(i) : CompletedAt(i);
                if (v > pocVol) { pocVol = v; pocIdx = i; }
            }

            // VA-Berechnung
            decimal targetVA = (useAllForVA ? totalAll : totalCompleted) * valueAreaFraction;
            var included = new bool[prices.Count];
            included[pocIdx] = true;
            decimal StartVolAt(int i) => useAllForVA ? AllAt(i) : CompletedAt(i);
            decimal cum = StartVolAt(pocIdx);
            int L = pocIdx - 1, R = pocIdx + 1;

            while (cum < targetVA && (L >= 0 || R <= last))
            {
                decimal vL = (L >= 0) ? StartVolAt(L) : -1m;
                decimal vR = (R <= last) ? StartVolAt(R) : -1m;

                if (vL < 0m && vR < 0m) break;

                if (vL >= 0m && vR >= 0m && vL == vR)
                {
                    if (R <= last) { included[R] = true; cum += vR; R++; }
                    if (cum >= targetVA) break;
                    if (L >= 0) { included[L] = true; cum += vL; L--; }
                }
                else if (vL > vR && L >= 0)
                {
                    included[L] = true; cum += vL; L--;
                }
                else if (R <= last)
                {
                    included[R] = true; cum += vR; R++;
                }
                else break;
            }

            decimal vah = prices[pocIdx], val = prices[pocIdx];
            for (int i = 0; i < prices.Count; i++)
            {
                if (!included[i]) continue;
                if (prices[i] < val) val = prices[i];
                if (prices[i] > vah) vah = prices[i];
            }

            mc.POC = prices[pocIdx];
            mc.VAH = vah;
            mc.VAL = val;

            // Basis-Serie f?r HVN/LVN: ALL
            mc.LevelVols ??= new SortedDictionary<decimal, decimal>();
            mc.LevelVols.Clear();
            for (int i = 0; i <= last; i++)
                mc.LevelVols[prices[i]] = AllAt(i);

            mc.TotalVol = mc.LevelVols.Values.Sum();
            mc.POCVol = mc.LevelVols.TryGetValue(mc.POC, out var pocVolTmp) ? pocVolTmp : 0m;

            // Triangular-Smoothing (Menu)
            decimal[] SmoothTriangular(decimal[] y, int span)
            {
                if (span <= 1 || y.Length == 0) return (decimal[])y.Clone();
                int half = Math.Max(1, span);
                var weights = new List<int>();
                for (int i = 1; i <= half; i++) weights.Add(i);
                for (int i = half - 1; i >= 1; i--) weights.Add(i);
                int radius = weights.Count / 2;

                var ys = new decimal[y.Length];
                for (int i = 0; i < y.Length; i++)
                {
                    decimal s = 0m; int wsum = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int wi = Math.Abs(k);
                        int wv = weights[wi];
                        int j = i + k;
                        if (j < 0 || j >= y.Length) continue;
                        s += y[j] * wv;
                        wsum += wv;
                    }
                    ys[i] = (wsum > 0) ? (s / wsum) : y[i];
                }
                return ys;
            }

            var raw = prices.Select(p => mc.LevelVols[p]).ToArray();
            var smooth = SmoothTriangular(raw, menuSmooth);
            decimal S(int i) => smooth[i];

            // Extrema etc. (unver?ndert)
            List<(int idx, bool isMax)> RawExtrema()
            {
                var ex = new List<(int idx, bool isMax)>();
                int i = 1;
                while (i < last)
                {
                    int dir = Math.Sign(S(i) - S(i - 1));
                    if (dir == 0)
                    {
                        int Lf = i, Rf = i;
                        while (Rf < last && S(Rf + 1) == S(i)) Rf++;
                        int c = (Lf + Rf) / 2;
                        decimal left = S(Math.Max(0, Lf - 1));
                        decimal right = S(Math.Min(last, Rf + 1));
                        decimal vc = S(c);
                        if (left < vc && right < vc) ex.Add((c, true));
                        else if (left > vc && right > vc) ex.Add((c, false));
                        i = Rf + 1;
                        continue;
                    }
                    int j = i + 1;
                    while (j <= last && Math.Sign(S(j) - S(j - 1)) == dir) j++;
                    int k = j - 1;
                    ex.Add((k, dir > 0));
                    i = j;
                }

                ex = ex.OrderBy(e => e.idx).ToList();
                var outL = new List<(int idx, bool isMax)>();
                foreach (var e in ex)
                {
                    if (outL.Count == 0) { outL.Add(e); continue; }
                    var prev = outL[^1];
                    if (prev.isMax == e.isMax)
                    {
                        bool takeNew = e.isMax ? S(e.idx) > S(prev.idx) : S(e.idx) < S(prev.idx);
                        if (takeNew) outL[^1] = e;
                    }
                    else outL.Add(e);
                }
                return outL;
            }

            var exAlt = RawExtrema();

            void EnsureEdgeValleys(List<(int idx, bool isMax)> exlist)
            {
                if (exlist.Count == 0) { exlist.Add((0, false)); exlist.Add((last, false)); return; }
                if (exlist[0].isMax) exlist.Insert(0, (0, false));
                if (exlist[^1].isMax) exlist.Add((last, false));
                if (exlist[0].isMax == true) exlist[0] = (0, false);
                if (exlist[^1].isMax == true) exlist[^1] = (last, false);
            }
            EnsureEdgeValleys(exAlt);

            var valleys = exAlt.Where(e => !e.isMax).Select(e => e.idx).Distinct().OrderBy(i => i).ToList();
            if (valleys.Count == 0 || valleys[0] != 0) valleys.Insert(0, 0);
            if (valleys[^1] != last) valleys.Add(last);

            List<int> peaks = new();
            for (int k = 0; k < valleys.Count - 1; k++)
            {
                int a = valleys[k], b = valleys[k + 1];
                if (b - a < 2) continue;
                int p = a + 1; decimal pv = S(p);
                for (int i = a + 1; i < b; i++)
                    if (S(i) > pv) { pv = S(i); p = i; }
                peaks.Add(p);
            }

            // Schwellen (optional ins Men? heben)
            decimal alphaH = 0.55m;
            decimal betaV = 0.35m;

            (int L2, int R2) GrowToTicks(int L2, int R2)
            {
                while ((R2 - L2 + 1) < minZoneTicks && (L2 > 0 || R2 < last))
                {
                    if (L2 > 0) L2--;
                    if (R2 < last) R2++;
                }
                return (L2, R2);
            }

            // HVN-Segmente (voll, mit Peak)
            var hvnIntervalsFull = new List<(int L, int R, int P)>();
            for (int k = 0; k < peaks.Count; k++)
            {
                int a = valleys[k], b = valleys[k + 1];
                int p = peaks[k];

                decimal vL = S(a), vR = S(b), sP = S(p);
                decimal thrL = vL + alphaH * (sP - vL);
                decimal thrR = vR + alphaH * (sP - vR);

                int Lh = p;
                for (int i = p; i >= a; i--) { Lh = i; if (S(i) < thrL) { Lh = Math.Min(p - 1, i + 1); break; } }
                int Rh = p;
                for (int i = p; i <= b; i++) { Rh = i; if (S(i) < thrR) { Rh = Math.Max(p + 1, i - 1); break; } }

                Lh = Math.Max(a, Math.Min(Lh, p));
                Rh = Math.Min(b, Math.Max(Rh, p));

                (Lh, Rh) = GrowToTicks(Lh, Rh);
                Lh = Math.Max(a, Lh);
                Rh = Math.Min(b, Rh);

                if (hvnIntervalsFull.Count > 0)
                {
                    var prev = hvnIntervalsFull[^1];
                    if (Lh <= prev.R)
                        Lh = Math.Min(b, prev.R + 1);
                }
                if (Lh <= Rh) hvnIntervalsFull.Add((Lh, Rh, p));
            }

            // LVN vorl?ufig
            var lvnIntervals = new List<(int L, int R)>();
            if (valleys.Count >= 3)
            {
                for (int k = 1; k < valleys.Count - 1; k++)
                {
                    int v = valleys[k];

                    int pLIdx = Math.Max(0, Math.Min(peaks.Count - 1, k - 1));
                    int pRIdx = Math.Max(0, Math.Min(peaks.Count - 1, k));
                    if (peaks.Count == 0) continue;

                    int pL = peaks[pLIdx];
                    int pR = peaks[pRIdx];
                    int a = valleys[k - 1];
                    int b = valleys[k + 1];

                    decimal sV = S(v);
                    decimal thrL = sV + betaV * (S(pL) - sV);
                    decimal thrR = sV + betaV * (S(pR) - sV);

                    int Ll = v, Rl = v;

                    for (int i = v; i >= a; i--) { Ll = i; if (S(i) >= thrL) { Ll = Math.Min(v, i + 1); break; } }
                    for (int i = v; i <= b; i++) { Rl = i; if (S(i) >= thrR) { Rl = Math.Max(v, i - 1); break; } }

                    (Ll, Rl) = GrowToTicks(Ll, Rl);

                    foreach (var h in hvnIntervalsFull.Select(x => (x.L, x.R)))
                    {
                        if (Rl < h.L || Ll > h.R) continue;
                        if (Ll <= h.L && Rl >= h.R)
                        {
                            int leftLen = h.L - Ll;
                            int rightLen = Rl - h.R;
                            if (leftLen >= rightLen) Rl = h.L - 1; else Ll = h.R + 1;
                        }
                        else if (Ll < h.L && Rl >= h.L) Rl = h.L - 1;
                        else if (Ll <= h.R && Rl > h.R) Ll = h.R + 1;
                    }

                    if (Ll < a) Ll = a;
                    if (Rl > b) Rl = b;
                    if (Ll <= Rl) lvnIntervals.Add((Ll, Rl));
                }
            }

            List<(int L, int R)> SubtractUnion(List<(int L, int R)> src, List<(int L, int R)> cut)
            {
                var result = new List<(int L, int R)>();
                foreach (var s in src)
                {
                    int curL = s.L, curR = s.R;
                    foreach (var c in cut)
                    {
                        if (curR < c.L || curL > c.R) continue;
                        if (c.L <= curL && c.R >= curR) { curL = curR + 1; break; }
                        if (c.L > curL && c.R < curR)
                        {
                            result.Add((curL, c.L - 1));
                            curL = c.R + 1;
                        }
                        else if (c.L <= curL) curL = c.R + 1;
                        else if (c.R >= curR) curR = c.L - 1;
                        if (curL > curR) break;
                    }
                    if (curL <= curR) result.Add((curL, curR));
                }
                return result;
            }
            lvnIntervals = SubtractUnion(lvnIntervals, hvnIntervalsFull.Select(h => (h.L, h.R)).ToList());

            // Gap-Merge (parametrierbar)
            List<(int L, int R)> MergeWithGap(List<(int L, int R)> zs, int gap)
            {
                if (zs == null || zs.Count == 0) return new();
                zs = zs.OrderBy(z => z.L).ToList();
                var outL = new List<(int L, int R)>();
                var cur = zs[0];
                for (int i = 1; i < zs.Count; i++)
                {
                    var z = zs[i];
                    if (z.L <= cur.R + gap) cur = (Math.Min(cur.L, z.L), Math.Max(cur.R, z.R));
                    else { outL.Add(cur); cur = z; }
                }
                outL.Add(cur);
                return outL;
            }

            // Scoring-Hilfen
            decimal ZoneVol(int L0, int R0)
            {
                decimal s = 0m;
                for (int i = L0; i <= R0; i++) s += mc.LevelVols[prices[i]];
                return s;
            }
            // KORREKT: VA-Breite in Ticks
            int VAWidthTicks = Math.Max(1, (int)Math.Round((mc.VAH - mc.VAL) / tick, MidpointRounding.AwayFromZero) + 1);

            decimal DistanceWeight(decimal price, decimal curPrice)
            {
                int distTicks = (int)Math.Abs((price - curPrice) / tick);
                return (decimal)(1.0 / (1.0 + (double)distTicks / maxDistTicks));
            }
            var curPrice = mc.POC; // kein ChartInfo

            // Diagnose: Vorfilter-Z?hlungen
            //this.LogInfo($"MC: raw peaks={peaks.Count}, valleys={valleys.Count}, hvnRaw={hvnIntervalsFull.Count}, lvnRaw={lvnIntervals.Count}");

            // HVN scoren + filtern
            int hvnRejectedWidth = 0, hvnRejectedWidthPct = 0, hvnRejectedProm = 0, hvnRejectedVol = 0;
            var hvnScored = new List<(int L, int R, int P, decimal score)>();
            foreach (var h in hvnIntervalsFull)
            {
                int Lh = h.L, Rh = h.R, P = h.P;
                int width = Rh - Lh + 1;
                decimal widthPctVA = VAWidthTicks > 0 ? (decimal)width / VAWidthTicks : 0m;

                int pkIdx = Math.Max(0, peaks.IndexOf(P));
                int a = valleys[Math.Max(0, pkIdx)];
                int b = valleys[Math.Min(valleys.Count - 1, pkIdx + 1)];
                decimal pv = S(P);
                decimal baseV = Math.Min(S(a), S(b));
                decimal prom = (pv > 0m) ? (pv - baseV) / pv : 0m;

                decimal volShare = mc.TotalVol > 0 ? ZoneVol(Lh, Rh) / mc.TotalVol : 0m;

                //this.LogInfo($"HVN cand: L={prices[Lh]} R={prices[Rh]} P={prices[P]} width={width} widthPctVA={widthPctVA:F3} prom={prom:F3} volShare={volShare:F3}");

                if (width < minZoneTicks) { hvnRejectedWidth++; continue; }
                if (widthPctVA < minWidthPctVA) { hvnRejectedWidthPct++; continue; }
                if (prom < minProminence) { hvnRejectedProm++; continue; }
                if (volShare < minVolShare) { hvnRejectedVol++; continue; }

                decimal centerPrice = prices[(Lh + Rh) / 2];
                decimal sc = 0.45m * prom + 0.35m * volShare + 0.10m * widthPctVA + 0.10m * DistanceWeight(centerPrice, curPrice);
                hvnScored.Add((Lh, Rh, P, sc));
            }

            var hvnIntervals = hvnScored
                .OrderByDescending(h => h.score)
                .Take(topNHVNs)
                .OrderBy(h => h.L)
                .Select(h => (h.L, h.R))
                .ToList();

            // LVN scoren + filtern
            int lvnRejectedWidth = 0, lvnRejectedWidthPct = 0;
            var lvnScored = new List<(int L, int R, int V, decimal score)>();
            foreach (var l in lvnIntervals)
            {
                int Ll = l.L, Rl = l.R;
                int width = Rl - Ll + 1;
                decimal widthPctVA = VAWidthTicks > 0 ? (decimal)width / VAWidthTicks : 0m;
                if (width < minZoneTicks) { lvnRejectedWidth++; continue; }
                if (widthPctVA < minWidthPctVA) { lvnRejectedWidthPct++; continue; }

                int V = (Ll + Rl) / 2;
                int k = Math.Max(0, peaks.FindLastIndex(p => p <= Rl));
                int pL = (k >= 0) ? peaks[Math.Max(0, k)] : V;
                int pR = (k + 1 < peaks.Count) ? peaks[k + 1] : V;
                decimal peakRef = Math.Max(S(pL), S(pR));
                decimal sV = S(V);
                decimal depth = (peakRef > 0m) ? (peakRef - sV) / peakRef : 0m;

                decimal volShare = mc.TotalVol > 0 ? ZoneVol(Ll, Rl) / mc.TotalVol : 0m;
                decimal centerPrice = prices[(Ll + Rl) / 2];

                decimal sc = 0.5m * depth + 0.2m * (1m - volShare) + 0.2m * (1m - widthPctVA) + 0.1m * DistanceWeight(centerPrice, curPrice);
                lvnScored.Add((Ll, Rl, V, sc));

                //this.LogInfo($"LVN cand: L={prices[Ll]} R={prices[Rl]} V={prices[V]} width={width} widthPctVA={widthPctVA:F3} depth={depth:F3} volShare={volShare:F3}");
            }

            var lvnFiltered = lvnScored
                .OrderByDescending(l => l.score)
                .Take(topNLVNs)
                .OrderBy(l => l.L)
                .Select(l => (l.L, l.R))
                .ToList();

            // --- NEU: Helpers f?r Clamp/Cap (lokale Funktionen) ---
            (int L, int R)? IntersectIdx((int L, int R) z, int lo, int hi)
            {
                int Lx = Math.Max(z.L, lo);
                int Rx = Math.Min(z.R, hi);
                if (Lx > Rx) return null;
                return (Lx, Rx);
            }
            (int L, int R) CapAroundCenter((int L, int R) z, int maxW, int minIdx, int maxIdx)
            {
                if (maxW <= 0) return z;
                int w = z.R - z.L + 1;
                if (w <= maxW) return z;
                int c = (z.L + z.R) / 2;
                int half = (maxW - 1) / 2;
                int Lx = Math.Max(minIdx, c - half);
                int Rx = Lx + maxW - 1;
                if (Rx > maxIdx) { Rx = maxIdx; Lx = Math.Max(minIdx, Rx - maxW + 1); }
                return (Lx, Rx);
            }

            // --- NEU: VA-Indizes vorbereiten ---
            var priceToIndex = new Dictionary<decimal, int>(prices.Count);
            for (int i = 0; i < prices.Count; i++) priceToIndex[prices[i]] = i;
            int idxVAL = priceToIndex[mc.VAL];
            int idxVAH = priceToIndex[mc.VAH];
            int vaLo = Math.Min(idxVAL, idxVAH);
            int vaHi = Math.Max(idxVAL, idxVAH);

            // --- NEU: Cap/Klemmung anwenden (nach Scoring, vor Disjunkt/Merge) ---
            if (clampToVA || enableCap)
            {
                List<(int L, int R)> Apply(List<(int L, int R)> zs)
                {
                    var outL = new List<(int L, int R)>(zs.Count);
                    foreach (var z in zs)
                    {
                        (int L, int R)? zz = z;
                        if (clampToVA) zz = IntersectIdx(z, vaLo, vaHi);
                        if (zz == null) continue;
                        var zc = enableCap ? CapAroundCenter(zz.Value, capTicks, 0, last) : zz.Value;
                        if (zc.L <= zc.R) outL.Add(zc);
                    }
                    return outL;
                }
                hvnIntervals = Apply(hvnIntervals);
                lvnFiltered = Apply(lvnFiltered);
                //this.LogInfo($"Post Clamp/Cap -> clampVA={clampToVA}, cap={enableCap}:{capTicks}, hvn={hvnIntervals.Count}, lvn={lvnFiltered.Count}");
            }

            // Disjunkt zu HVN halten + Merge mit Gap
            lvnFiltered = SubtractUnion(lvnFiltered, hvnIntervals);
            hvnIntervals = MergeWithGap(hvnIntervals, gapTicks);
            lvnFiltered = MergeWithGap(lvnFiltered, gapTicks);

            // Touching-Merge
            List<(int L, int R)> MergeIdx(List<(int L, int R)> zs)
            {
                if (zs == null || zs.Count == 0) return new();
                zs = zs.OrderBy(z => z.L).ToList();
                var outL = new List<(int L, int R)>();
                var cur = zs[0];
                for (int i = 1; i < zs.Count; i++)
                {
                    var z = zs[i];
                    if (z.L <= cur.R + 1) cur = (Math.Min(cur.L, z.L), Math.Max(cur.R, z.R));
                    else { outL.Add(cur); cur = z; }
                }
                outL.Add(cur);
                return outL;
            }

            hvnIntervals = MergeIdx(hvnIntervals);
            lvnFiltered = MergeIdx(lvnFiltered);

            // Bounds-Normalisierung
            List<(int L, int R)> NormalizeIntervals(List<(int L, int R)> intervals, int maxIndex)
            {
                var result = new List<(int L, int R)>(intervals?.Count ?? 0);
                if (intervals == null) return result;

                foreach (var (L0, R0) in intervals)
                {
                    int l = Math.Max(0, Math.Min(L0, maxIndex));
                    int r = Math.Max(0, Math.Min(R0, maxIndex));
                    if (l > r) { var tmp = l; l = r; r = tmp; }
                    if (l <= r) result.Add((l, r));
                }
                return result;
            }
            hvnIntervals = NormalizeIntervals(hvnIntervals, last);
            lvnFiltered = NormalizeIntervals(lvnFiltered, last);

            // Diagnose: Nach Filterung
            //this.LogInfo($"BuildMC: prices.Count={prices.Count}, hvnRaw={hvnIntervalsFull.Count}, hvnKept={hvnIntervals.Count} (rej: width={hvnRejectedWidth}, widthPctVA={hvnRejectedWidthPct}, prom={hvnRejectedProm}, vol={hvnRejectedVol}), lvnRaw={lvnIntervals.Count}, lvnKept={lvnFiltered.Count} (rej: width={lvnRejectedWidth}, widthPctVA={lvnRejectedWidthPct})");

            if (hvnIntervals.Any(z => z.L < 0 || z.R > last))
                this.LogInfo("[BuildMicroCompositeFromHist] BuildMC: WARN hvn out of bounds after normalize");
            if (lvnFiltered.Any(z => z.L < 0 || z.R > last))
                this.LogInfo("[BuildMicroCompositeFromHist] BuildMC: WARN lvn out of bounds after normalize");
            if (peaks.Any(i => i < 0 || i > last))
                this.LogInfo("[BuildMicroCompositeFromHist] BuildMC: WARN peaks out of bounds");
            if (valleys.Any(i => i < 0 || i > last))
                this.LogInfo("[BuildMicroCompositeFromHist] BuildMC: WARN valleys out of bounds");

            // HVN/LVN-Zonen f?llen
            mc.HVNZones ??= new();
            mc.HVNZones.Clear();
            foreach (var z in hvnIntervals)
                mc.HVNZones.Add((Start: prices[z.L], End: prices[z.R]));

            mc.LVNZones ??= new();
            mc.LVNZones.Clear();
            foreach (var z in lvnFiltered)
                mc.LVNZones.Add((Start: prices[z.L], End: prices[z.R]));

            // Peaks/Valleys
            IEnumerable<int> InBounds(IEnumerable<int> idxs) => (idxs ?? Array.Empty<int>()).Where(i => i >= 0 && i <= last);

            mc.HVNs ??= new();
            mc.HVNs.Clear();
            mc.HVNs.AddRange(InBounds(peaks).Select(i => prices[i]));

            mc.LVNs ??= new();
            mc.LVNs.Clear();
            mc.LVNs.AddRange(InBounds(valleys).Where(i => i != 0 && i != last).Select(i => prices[i]));

            // Logging
            var hvnStr = string.Join(" | ", mc.HVNZones.Select(z => $"[{z.Item1},{z.Item2}]"));
            var lvnStr = string.Join(" | ", mc.LVNZones.Select(z => $"[{z.Item1},{z.Item2}]"));
            //this.LogInfo($"MC: Zones HVN={hvnStr}");
            //this.LogInfo($"MC: Zones LVN={lvnStr}");

            return mc;
        }


        //Diese Methode ist f?r die Verwaltung der Daten in einem rollierenden Zeitfenster verantwortlich. Ihre Aufgabe ist es, das Volumenprofil-Histogramm (_mcHist) immer auf dem neuesten Stand zu halten, sodass es nur die letzten M Kerzen (Bars) widerspiegelt.
        //AddBarToHist(bar, +1);: F?gt die Volumendaten der neuesten Kerze zum Histogramm _mcHist hinzu.
        //_mcBars.Enqueue(bar);: Speichert den Index der neuesten Kerze in einer Warteschlange(_mcBars).
        //if (_mcBars.Count > M): Pr?ft, ob die Anzahl der gespeicherten Kerzen die definierte Fenstergr??e M ?berschreitet.
        //int oldBar = _mcBars.Dequeue();: Wenn das Fenster voll ist, wird die ?lteste Kerze aus der Warteschlange entfernt.
        //AddBarToHist(oldBar, -1);: Die Volumendaten dieser ?ltesten Kerze werden wieder aus dem Histogramm _mcHist entfernt(subtrahiert).
        //Im Wesentlichen: Diese Methode sorgt daf?r, dass das Histogramm _mcHist immer genau die Volumenverteilung der letzten M Kerzen enth?lt, indem sie bei jeder neuen Kerze die neueste hinzuf?gt und die ?lteste entfernt.
        void UpdateMicroCompositeRolling(int bar)
        {
            // Log f?r das Hinzuf?gen des neuen Bars
            //this.LogInfo($"MicroComposite Update: [Info] Rollierendes Fenster: Neuer Bar #{bar} wird hinzugef?gt.");

            if (_tickSize == 0m) { this.LogInfo("[UpdateMicroCompositeRolling] TickSize null in UpdateMicroCompositeRolling"); return; }


            AddBarToHist(bar, +1);
            _mcBars.Enqueue(bar);
            if (_mcBars.Count > M)
            {
                int oldBar = _mcBars.Dequeue();
                AddBarToHist(oldBar, -1);
            }
            // Optional: Log der aktuellen Gr??e des Fensters
            //this.LogInfo($"MicroComposite Update: [Info] Rollierendes Fenster: Aktuelle Gr??e nach Update: {_mcBars.Count}/{M} Bars.");
            _mcDirty = true;
            RebuildCurrentMicroComposite(bar);
        }

        private void RebuildCurrentMicroComposite(int barContext = -1)
        {
            if (_tickSize <= 0m || _mcHist == null || _mcHist.Count == 0)
            {
                _currentMC = null;
                _mcDirty = false;
                return;
            }
            _currentMC = BuildMicroCompositeFromHist(_mcHist, _mcSmoothTicks, _mcTopNPeaks);
            _mcDirty = false;
        }

        //diese Hilfsmethode berechnet und aktualisiert ein aggregiertes Volumenprofil, das in der Variable _mcHist(vermutlich f?r "Micro-Composite History") gespeichert wird.
        //Im Detail tut sie Folgendes:
        //Durchlaufen der Preis-Cluster: Die Methode iteriert durch alle detaillierten Preis- und Volumeneinheiten (sogenannte ClusterLevel) innerhalb eines einzelnen Bars (identifiziert durch barIndex).
        //Preis runden: F?r jedes Cluster wird der Preis(cl.Price) auf die n?chste Tick-Gr??e gerundet(RoundToTick). Dies standardisiert die Preislevel.
        //Volumen hinzuf?gen oder abziehen: Das Kernst?ck ist die Zeile _mcHist[p] += sign* cl.TotalVol;. Sie modifiziert das Gesamtvolumen f?r das gerundete Preislevel p in der _mcHist-Map.
        //Wenn sign +1 ist, wird das Volumen des aktuellen Clusters zum historischen Gesamtvolumen auf diesem Preislevel hinzugef?gt.
        //Wenn sign -1 ist, wird das Volumen abgezogen.
        //Aufr?umen: Wenn das Gesamtvolumen f?r ein Preislevel auf null oder weniger f?llt (nach einer Subtraktion), wird dieser Preiseintrag aus der _mcHist-Map entfernt, um sie sauber zu halten.
        //Zweck im Gesamtkontext: Diese Methode wird verwendet, um ein rollierendes Volumenprofil zu erstellen. Wie in der Methode UpdateMicroCompositeRolling zu sehen ist, wird beim Hinzuf?gen eines neuen Bars AddBarToHist mit sign = +1 aufgerufen und beim Entfernen des ?ltesten Bars mit sign = -1.So enth?lt _mcHist immer das aggregierte Volumenprofil eines gleitenden Zeitfensters.

        void AddBarToHist(int barIndex, int sign)
        {


            // Log beim Eintritt in die Methode, um den Zweck des Aufrufs zu kennen (+1 = hinzuf?gen, -1 = entfernen)
            string operation = sign > 0 ? "HINZUGEF?GT" : "ENTFERNT";
            //this.LogInfo($"MicroComposite AddBar: [Info] AddBarToHist f?r Bar #{barIndex}: Volumen wird {operation}.");

            foreach (var cl in EnumerateClusterLevels(barIndex))
            {
                if (cl.TotalVol <= 0) continue;
                decimal p = RoundToTick(cl.Price);

                decimal previousVol = _mcHist.ContainsKey(p) ? _mcHist[p] : 0m;

                if (!_mcHist.ContainsKey(p)) _mcHist[p] = 0m;
                _mcHist[p] += sign * cl.TotalVol;

                // Log f?r jede einzelne Volumen?nderung. Dies kann sehr gespr?chig sein!
                // this.LogInfo($"MicroComposite AddBar: [Info]  -> Preis {p}: Volumen von {previousVol} auf {_mcHist[p]} ge?ndert (Delta: {sign * cl.TotalVol}).");

                if (_mcHist[p] <= 0)
                {
                    _mcHist.Remove(p);
                    // Log f?r das Entfernen eines Preislevels aus dem Histogramm
                    //this.LogInfo($"MicroComposite AddBar: [Info]  -> Preis {p} aus Histogramm entfernt, da Volumen 0 oder negativ wurde.");
                }
            }
            // Log am Ende, um die Gesamtgr??e des Histogramms zu sehen
            //this.LogInfo($"MicroComposite AddBar: [Info] AddBarToHist f?r Bar #{barIndex} abgeschlossen. _mcHist enth?lt jetzt {_mcHist.Count} Preislevel.");
        }
        //die Hilfsmethode CalculatePOC_VAH_VAL berechnet drei wichtige Kennzahlen aus einem Volumenprofil (dargestellt als SortedDictionary<decimal, decimal> hist):
        // ATAS-nahe Value-Area aus ROH-Daten (70% ab POC, Nachbar mit gr??erem Vol zuerst; bei Gleichheit rechts)
        // ATAS-nahe VA: ab POC, Schritt f?r Schritt zu den Nachbarn mit gr??erem Volumen
        // Einzige VA-Methode: Roh-Histogramm, Nachbar mit gr??erem Volumen zuerst, mit Logs
        // Passe ValueAreaPct falls n?tig auf 0.70m an; Tie-Break je nach ATAS-Einstellung.
        private void CalculatePOC_VAH_VAL(SortedDictionary<decimal, decimal> hist,
          out decimal poc, out decimal vah, out decimal val, bool log = true) // kleiner, privater Zusatz-Parameter
        {
            poc = vah = val = 0m;
            if (hist == null || hist.Count == 0)
            {
                if (log) this.LogInfo("[CalculateValueArea] VA: leeres Histogramm.");
                return;
            }

            const decimal ValueAreaPct = 0.70m;
            const bool PreferRightOnTie = true;
            const decimal epsVol = 1m;

            var tick = (_tickSize > 0m) ? _tickSize : 0.25m;

            decimal VolAt(SortedDictionary<decimal, decimal> map, decimal price)
                => map.TryGetValue(price, out var v) ? v : 0m;

            // Einheitliche Normalisierung via globalem RoundToTick (kein floor!)
            var norm = new SortedDictionary<decimal, decimal>();
            foreach (var kv in hist)
            {
                var p = RoundToTick(kv.Key); // nutzt eure globale Implementierung
                if (!norm.ContainsKey(p)) norm[p] = kv.Value;
                else norm[p] += kv.Value;
            }
            hist = norm;

            // POC: gr??tes Volumen, bei Gleichstand h?herer Preis (deterministisch)
            var pocKvp = hist
                .OrderByDescending(kv => kv.Value)
                .ThenByDescending(kv => kv.Key)
                .First();
            poc = pocKvp.Key;

            decimal total = hist.Sum(kv => kv.Value);
            decimal target = total * ValueAreaPct;

            // Preisraster exakt min..max (ohne +tick/2)
            var minP = hist.Keys.Min();
            var maxP = hist.Keys.Max();

            var prices = new List<decimal>();
            for (decimal p = minP; p <= maxP; p += tick) prices.Add(p);

            int c = prices.IndexOf(poc);
            if (c < 0)
            {
                // out-Parameter nicht in Lambda verwenden -> lokale Kopie
                var pocVal = poc;
                // Tolerante Suche ohne Lambda
                for (int i = 0; i < prices.Count; i++)
                {
                    if (Math.Abs(prices[i] - pocVal) < tick / 2m)
                    {
                        c = i;
                        break;
                    }
                }
                if (c < 0)
                {
                    if (log) this.LogInfo("[CalculateValueArea] VA: POC nicht im Raster.");
                    return;
                }
            }


            int L = c, R = c;
            vah = val = poc;
            decimal cum = VolAt(hist, poc);
            int steps = 0;

            if (log)
            {
                decimal vLeft0 = (L > 0) ? VolAt(hist, prices[L - 1]) : -1m;
                decimal vRight0 = (R < prices.Count - 1) ? VolAt(hist, prices[R + 1]) : -1m;
                this.LogInfo($"[CalculateValueArea] VA start: POC={poc} Vol={VolAt(hist, poc)}, total={total}, target={target}, bins={prices.Count}, L0={vLeft0}, R0={vRight0}");
            }

            while (cum < target && (L > 0 || R < prices.Count - 1))
            {
                decimal vLeft = (L > 0) ? VolAt(hist, prices[L - 1]) : -1m;
                decimal vRight = (R < prices.Count - 1) ? VolAt(hist, prices[R + 1]) : -1m;

                if (vLeft < 0m && vRight < 0m) break;

                if (vLeft >= 0m && vRight >= 0m && Math.Abs(vLeft - vRight) <= epsVol)
                {
                    // Bei Gleichheit: beide Seiten aufnehmen (symmetrisch, ATAS-nah), verhindert Drift
                    if (R < prices.Count - 1)
                    {
                        R++;
                        var addVolR = VolAt(hist, prices[R]);
                        cum += addVolR;
                        vah = prices[R];
                        steps++;
                        if (log) this.LogInfo($"[CalculateValueArea] VA step {steps}: RIGHT {vah} +{addVolR} cum={cum}");
                    }
                    if (cum >= target) break;

                    if (L > 0)
                    {
                        L--;
                        var addVolL = VolAt(hist, prices[L]);
                        cum += addVolL;
                        val = prices[L];
                        steps++;
                        if (log) this.LogInfo($"[CalculateValueArea] VA step {steps}: LEFT {val} +{addVolL} cum={cum}");
                    }
                }
                else
                {
                    bool chooseLeft = vLeft > vRight;
                    if (chooseLeft && L > 0)
                    {
                        L--;
                        var addVol = VolAt(hist, prices[L]);
                        cum += addVol;
                        val = prices[L];
                        steps++;
                        if (log) this.LogInfo($"[CalculateValueArea] VA step {steps}: LEFT {val} +{addVol} cum={cum}");
                    }
                    else if (!chooseLeft && R < prices.Count - 1)
                    {
                        R++;
                        var addVol = VolAt(hist, prices[R]);
                        cum += addVol;
                        vah = prices[R];
                        steps++;
                        if (log) this.LogInfo($"[CalculateValueArea] VA step {steps}: RIGHT {vah} +{addVol} cum={cum}");
                    }
                    else break;
                }
            }

            if (val > vah) { var t = val; val = vah; vah = t; }

            if (log) this.LogInfo($"[CalculateValueArea] VA done: VAL={val}, VAH={vah}, cum={cum}/{target}, steps={steps}");
        }






        // Hilfsfunktion: MicroComposite aus dem rollierenden Histogramm bauen
        private MicroComposite? GetRollingMicroComposite()
        {
            if (_mcDirty) RebuildCurrentMicroComposite(_lastCalculatedBar);
            return _currentMC;
        }
        // =========================================================================
        // Volumen | POC Profil
        // =========================================================================

        private static bool IsBull(decimal open, decimal close) => close > open;
        private static bool IsBear(decimal open, decimal close) => close < open;


        // Variante von AddBarToHist, die in ein ?bergebenes Histogramm schreibt
        private void AddBarToHistCustom(SortedDictionary<decimal, decimal> hist, int barIndex, int sign)
        {
            foreach (var cl in EnumerateClusterLevels(barIndex))
            {
                if (cl.TotalVol <= 0) continue;
                decimal p = RoundToTick(cl.Price);
                if (!hist.ContainsKey(p)) hist[p] = 0m;
                hist[p] += sign * cl.TotalVol;
                if (hist[p] <= 0m) hist.Remove(p);
            }
        }
        private bool HasSufficientWindow(SortedDictionary<decimal, decimal> hist, int minBins, decimal minVol)
        {
            if (hist == null || hist.Count < minBins) return false;
            decimal sum = hist.Values.Sum();
            return sum >= minVol;
        }
        private decimal ComputePocFromHist(SortedDictionary<decimal, decimal> hist)
        {
            if (hist == null || hist.Count == 0) return 0m;
            CalculatePOC_VAH_VAL(hist, out decimal poc, out _, out _, log: false); // keine VA-Logs hier
            return poc;
        }

        private void VW_Reset()
        {
            _vwHist.Clear();
            _vwActive = false;
            _vwStartBar = -1;
            _vwLastBar = -1;
            _vwDirection = 0;
            _vwPrevPOC = 0m;
            _vwPOC = 0m;
        }

        private void VW_TryStart(int bar)
        {
            if (bar < 1 || _vwActive) return;

            var prev = GetCandle(bar - 1);
            var curr = GetCandle(bar);

            bool prevBull = IsBull(prev.Open, prev.Close);
            bool prevBear = IsBear(prev.Open, prev.Close);
            bool currBull = IsBull(curr.Open, curr.Close);
            bool currBear = IsBear(curr.Open, curr.Close);

            if ((prevBull && currBull) || (prevBear && currBear))
            {
                _vwActive = true;
                _vwStartBar = bar - 1;
                _vwLastBar = bar - 1;
                _vwDirection = prevBull ? +1 : -1;
                _vwHist.Clear();

                AddBarToHistCustom(_vwHist, _vwStartBar, +1);
                if (!HasSufficientWindow(_vwHist, minBins: 5, minVol: 50m))
                {
                    // zu klein, noch kein POC berechnen, keine Logs
                    _vwPOC = 0m;
                    _vwPrevPOC = 0m;
                    return;
                }
                _vwPOC = ComputePocFromHist(_vwHist);
                _vwPrevPOC = _vwPOC;
            }
        }

        private void VW_UpdateOnBar(int bar)
        {
            if (!_vwActive) return;

            AddBarToHistCustom(_vwHist, _vwStartBar, +1);
            if (!HasSufficientWindow(_vwHist, minBins: 5, minVol: 50m))
            {
                // zu klein, noch kein POC berechnen, keine Logs
                _vwPOC = 0m;
                _vwPrevPOC = 0m;
                return;
            }
            _vwPOC = ComputePocFromHist(_vwHist);
            _vwPrevPOC = _vwPOC;
        }

        private void HandleVolumeWindowFeature(int bar)
        {
            if (!_vwActive) VW_TryStart(bar);
            if (!_vwActive) return;

            var curr = GetCandle(bar);
            bool currBull = IsBull(curr.Open, curr.Close);
            bool currBear = IsBear(curr.Open, curr.Close);

            // Umkehr relativ zur Fenster-Richtung pr?fen ? VOR jeglichem Histogramm-Update mit der Umkehrkerze
            bool reversal =
                (_vwDirection == +1 && currBear) ||
                (_vwDirection == -1 && currBull);

            if (reversal)
            {
                // WICHTIG: Die Umkehrkerze NICHT ins Histogramm aufnehmen.
                // POC-Check basiert ausschlie?lich auf dem Fenster bis _vwLastBar (Kerze vor der Umkehr)
                decimal bodyLow = Math.Min(curr.Open, curr.Close);
                decimal bodyHigh = Math.Max(curr.Open, curr.Close);

                bool pocInsideBody = _vwPOC >= bodyLow && _vwPOC <= bodyHigh;
                bool pocChanged = _vwPOC != 0m && _vwPrevPOC != 0m && _vwPOC != _vwPrevPOC;

                if (pocInsideBody && pocChanged)
                {
                    VwSignal = true;
                    VwSignalPrice = _vwPOC;

                    // Richtung aus der Umkehr ableiten (entgegengesetzt zur Fenster-Richtung)
                    VwSignalDir = (_vwDirection == +1 && currBear) ? -1
                                : (_vwDirection == -1 && currBull) ? +1
                                : 0;

                    _vwPendingReset = true; // Reset verz?gert ausf?hren (wie gehabt)
                }
                else
                {
                    // Kein g?ltiges Signal -> sofort reset
                    VW_Reset();
                }

                return; // Fr?hzeitiger Exit: keine Aktualisierung mit der Umkehrkerze
            }

            // Keine Umkehr: jetzt den aktuellen Bar in das Fenster aufnehmen und POC aktualisieren
            VW_UpdateOnBar(bar);
        }

        // =========================================================================
        // Volumen | rollierendes Profil | Ende
        // =========================================================================



        // =========================================================================
        // OV | Berechnung Schwellenwerte
        // =========================================================================


        //Diese beiden Methoden verwalten ein "gleitendes Datenfenster" (sliding window) mit einer festen maximalen Gr??e.
        //Zweck: Sie werden verwendet, um eine Warteschlange(Queue) mit den letzten max Werten zu f?llen.Eine Warteschlange ist eine Datenstruktur, bei der das erste hinzugef?gte Element auch das erste ist, das wieder entfernt wird(First-In, First-Out).
        //Funktionsweise:
        //q.Enqueue(v): Ein neuer Wert v(entweder eine einzelne decimal-Zahl oder ein Paar von decimal-Zahlen) wird am Ende der Warteschlange hinzugef?gt.
        //while (q.Count > max) q.Dequeue();: Wenn die Anzahl der Elemente in der Warteschlange die definierte maximale Gr??e(max) ?berschreitet, wird das ?lteste Element vom Anfang der Warteschlange entfernt.Dies wird so lange wiederholt, bis die Gr??e wieder max entspricht.
        //Im Wesentlichen halten diese Methoden eine stets aktuelle Liste der letzten max Datenpunkte vor, was f?r die Berechnung von gleitenden Indikatoren (wie gleitende Durchschnitte oder, wie hier, f?r den Z-Score) unerl?sslich ist.

        private void PushWindow(Queue<decimal> q, decimal v, int max)
        {
            if (max < 1) max = 1;
            if (q.Count >= max) q.Dequeue();
            q.Enqueue(v);
        }

        private void PushWindow(Queue<(decimal a, decimal b)> q, (decimal a, decimal b) v, int max)
        {
            if (max < 1) max = 1;
            if (q.Count >= max) q.Dequeue();
            q.Enqueue(v);
        }

        private decimal RobustifiedExponentialZScore(object windowObj, double alpha = 0.1, decimal x = 0m, bool useRobustScale = true, double robustBlend = 0.8, double maxAbsZ = 100.0, double madEps = 1e-12)
        {
            // Defensive checks
            if (windowObj == null)
            {
                //this.LogInfo("RobustifiedExponentialZScore: window == null -> return 0");
                return 0m;
            }

            Type wType = windowObj.GetType();
            if (wType == typeof(decimal) || wType == typeof(double) || wType == typeof(float) ||
                wType == typeof(int) || wType == typeof(long) || wType == typeof(short) ||
                wType == typeof(uint) || wType == typeof(ulong) || wType == typeof(byte))
            {
                try
                {
                    //this.LogWarn($"RobustifiedExponentialZScore: windowObj is a numeric scalar of type {wType.FullName} (value={windowObj}). Likely a caller bug ? expected IEnumerable. Returning 0.");
                }
                catch
                {
                    //this.LogWarn("RobustifiedExponentialZScore: windowObj is a numeric scalar. Returning 0.");
                }
                return 0m;
            }

            // Materialize window into decimals
            var valuesList = new List<decimal>();
            try
            {
                //this.LogDebug($"RobustifiedExponentialZScore: windowObj type = {wType.FullName}");

                if (windowObj is IEnumerable<decimal> decEnum)
                {
                    foreach (var v in decEnum) valuesList.Add(v);
                }
                else if (windowObj is System.Collections.IEnumerable nonGenEnum)
                {
                    foreach (var obj in nonGenEnum)
                    {
                        if (obj == null)
                        {
                            //this.LogWarn("RobustifiedExponentialZScore: encountered null element in window; skipping");
                            continue;
                        }

                        if (obj is decimal d) valuesList.Add(d);
                        else
                        {
                            try
                            {
                                decimal conv = Convert.ToDecimal(obj);
                                valuesList.Add(conv);
                            }
                            catch (Exception ex)
                            {
                                //this.LogWarn($"RobustifiedExponentialZScore: cannot convert element of type {obj.GetType().FullName} to decimal: {ex.Message}. Aborting and returning 0.");
                                return 0m;
                            }
                        }
                    }
                }
                else
                {
                    string[] candidateMembers = new[] { "Values", "Items", "Buffer", "Array", "ToArray", "ToList", "GetValues", "GetItems", "Snapshot" };
                    bool materialized = false;

                    foreach (var name in candidateMembers)
                    {
                        var prop = wType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                        if (prop != null)
                        {
                            var val = prop.GetValue(windowObj);
                            if (val is System.Collections.IEnumerable en)
                            {
                                foreach (var obj in en)
                                {
                                    if (obj == null) continue;
                                    try { valuesList.Add(obj is decimal dd ? dd : Convert.ToDecimal(obj)); }
                                    catch { valuesList.Clear(); break; }
                                }
                                if (valuesList.Count > 0) { materialized = true; break; }
                            }
                        }

                        var field = wType.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (field != null)
                        {
                            var val = field.GetValue(windowObj);
                            if (val is System.Collections.IEnumerable en)
                            {
                                foreach (var obj in en)
                                {
                                    if (obj == null) continue;
                                    try { valuesList.Add(obj is decimal dd ? dd : Convert.ToDecimal(obj)); }
                                    catch { valuesList.Clear(); break; }
                                }
                                if (valuesList.Count > 0) { materialized = true; break; }
                            }
                        }

                        var method = wType.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
                        if (method != null && method.GetParameters().Length == 0)
                        {
                            try
                            {
                                var val = method.Invoke(windowObj, null);
                                if (val is System.Collections.IEnumerable en)
                                {
                                    foreach (var obj in en)
                                    {
                                        if (obj == null) continue;
                                        try { valuesList.Add(obj is decimal dd ? dd : Convert.ToDecimal(obj)); }
                                        catch { valuesList.Clear(); break; }
                                    }
                                    if (valuesList.Count > 0) { materialized = true; break; }
                                }
                            }
                            catch { /* ignore invocation errors and continue */ }
                        }
                    }

                    if (!materialized)
                    {
                        var ifaces = wType.GetInterfaces();
                        foreach (var iface in ifaces)
                        {
                            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                            {
                                if (windowObj is System.Collections.IEnumerable altEn)
                                {
                                    foreach (var obj in altEn)
                                    {
                                        if (obj == null) continue;
                                        try { valuesList.Add(obj is decimal dd ? dd : Convert.ToDecimal(obj)); }
                                        catch { valuesList.Clear(); break; }
                                    }
                                    if (valuesList.Count > 0) { materialized = true; break; }
                                }
                            }
                        }
                    }

                    if (!materialized)
                    {
                        var memberNames = string.Join(", ", wType.GetMembers(BindingFlags.Public | BindingFlags.Instance).Take(10).Select(m => m.Name));
                        //this.LogWarn($"RobustifiedExponentialZScore: windowObj is not IEnumerable and no candidate member found -> type={wType.FullName}, sampleMembers=[{memberNames}] -> return 0");
                        return 0m;
                    }
                }
            }
            catch (Exception ex)
            {
                //this.LogWarn($"RobustifiedExponentialZScore: Exception while materializing window: {ex.Message}");
                return 0m;
            }

            int n = valuesList.Count;
            if (n < 2)
            {
                //this.LogInfo($"RobustifiedExponentialZScore: Window too small (n={n}) -> return 0");
                return 0m;
            }

            if (!(alpha > 0.0 && alpha <= 1.0))
            {
                //this.LogInfo($"RobustifiedExponentialZScore: invalid alpha={alpha}, fallback to 0.1");
                alpha = 0.1;
            }

            // 1) EWMA mean
            decimal ewmaMean = valuesList[0];
            for (int i = 1; i < n; i++)
            {
                ewmaMean = (decimal)alpha * valuesList[i] + (1m - (decimal)alpha) * ewmaMean;
            }

            // 2) EWMA variance (weights)
            decimal sumWeights = 0m;
            decimal weightedSumSqDiff = 0m;
            for (int i = 0; i < n; i++)
            {
                decimal weight = (decimal)Math.Pow(1 - alpha, n - 1 - i);
                decimal diff = valuesList[i] - ewmaMean;
                weightedSumSqDiff += weight * diff * diff;
                sumWeights += weight;
            }
            decimal ewmaVariance = (sumWeights > 0m) ? weightedSumSqDiff / sumWeights : 0m;
            if (ewmaVariance < 0m)
            {
                //this.LogInfo("RobustifiedExponentialZScore: ewmaVariance negative -> set to 0");
                ewmaVariance = 0m;
            }
            decimal ewmaSd = (decimal)Math.Sqrt((double)ewmaVariance);

            // 3) Scaled MAD
            double scaledMadDouble = double.NaN;
            try
            {
                var valsDouble = new double[n];
                for (int i = 0; i < n; i++) valsDouble[i] = (double)valuesList[i];
                scaledMadDouble = RobustStatisticsHelper.ComputeScaledMAD(valsDouble);
            }
            catch (Exception ex)
            {
                //this.LogWarn($"RobustifiedExponentialZScore: ComputeScaledMAD failed: {ex.Message}");
                scaledMadDouble = double.NaN;
            }

            decimal scaledMad = 0m;
            if (!double.IsNaN(scaledMadDouble) && double.IsFinite(scaledMadDouble))
                scaledMad = (decimal)scaledMadDouble;

            // 4) Determine finalScale via blending
            decimal finalScale = 0m;
            double blend = Math.Max(0.0, Math.Min(1.0, robustBlend)); // clamp to [0,1]

            if (useRobustScale)
            {
                if (scaledMad > (decimal)madEps)
                {
                    decimal blended = (decimal)blend * scaledMad + (1m - (decimal)blend) * ewmaSd;
                    finalScale = blended;
                }
                else
                {
                    finalScale = ewmaSd;
                }
            }
            else
            {
                finalScale = (scaledMad > (decimal)madEps) ? scaledMad : ewmaSd;
            }

            // 5) Safety: if finalScale <= 0 -> return 0
            if (finalScale <= 0m)
            {
                //this.LogInfo("RobustifiedExponentialZScore: finalScale <= 0 -> return 0");
                return 0m;
            }

            // 6) fallback-scale policy (prevents giant z_raw due to tiny finalScale)
            double finalScaleDouble = (double)finalScale;
            double scaleUsed = finalScaleDouble;
            if (finalScaleDouble < madEps)
            {
                scaleUsed = madEps;
                //this.LogWarn($"RobustifiedExponentialZScore: finalScale {finalScaleDouble:E} < madEps {madEps:E} -> using fallback scale {scaleUsed:E}. windowType={wType.FullName}, sampleCount={n}");
                // Optionally: you could instead return 0 here. We use fallback scale to still produce a stable z.
            }

            // 7) Compute raw z in double for diagnostics / clamping decisions
            double numeratorDouble = (double)(x - ewmaMean);
            double zRaw = numeratorDouble / scaleUsed;

            // Debug logging when |z_raw| is large (tunable threshold)
            const double debugThreshold = 10.0;
            if (Math.Abs(zRaw) > debugThreshold)
            {
                //this.LogInfo($"RobustifiedExponentialZScore DEBUG: windowType={wType.FullName}, n={n}, x={x}, ewmaMean={ewmaMean}, numerator={(decimal)numeratorDouble}, finalScale(decimal)={finalScale}, finalScale(double)={finalScaleDouble:E}, scaledMad={scaledMad}, ewmaSd={ewmaSd}, madEps={madEps:E}, z_raw={zRaw:F4}");
            }

            // 8) Clamp z to maxAbsZ
            double zClampedDouble = zRaw;
            if (!double.IsNaN(maxAbsZ) && double.IsFinite(maxAbsZ) && maxAbsZ > 0.0)
            {
                if (Math.Abs(zRaw) > maxAbsZ)
                {
                    double clamped = Math.Sign(zRaw) * maxAbsZ;
                    //this.LogInfo($"RobustifiedExponentialZScore: z clamped from {zRaw:F4} to {clamped:F4} (maxAbsZ={maxAbsZ})");
                    zClampedDouble = clamped;
                }
            }

            return (decimal)zClampedDouble;
        }

        private decimal ExponentialZScore(decimal x, IEnumerable<decimal> window, double alpha = 0.1)
        {
            // EWMA-Implementierung: Gewichtete Mean und Variance f?r Z-Score
            // Annahme: window ist geordnet, neueste Werte zuletzt (wie in deiner Queue)

            if (!window.Any())
            {
                this.LogInfo("[ExponentialZScore] Leeres Window ? Return 0");  // DEBUG-LOG
                return 0m;
            }

            // Konvertiere zu Liste f?r einfache Iteration (neueste zuletzt)
            var values = window.ToList();
            int n = values.Count;
            if (n < 2)
            {
                this.LogInfo($"[ExponentialZScore] Window zu klein (n={n}) ? Return 0");  // DEBUG-LOG
                return 0m;
            }

            // DEBUG-LOG: Eingabe-Zusammenfassung
            decimal winMin = values.Min();
            decimal winMax = values.Max();
            decimal winAvg = values.Average();
            //this.LogInfo($"ExponentialZScore: Alpha={alpha}, WindowSize={n}, Min={winMin}, Max={winMax}, Avg={winAvg}, Current x={x}");

            // Berechne EWMA-Mean (rekursiv, startet mit ?ltestem)
            decimal ewmaMean = values[0];  // ?lteste zuerst
            for (int i = 1; i < n; i++)
            {
                ewmaMean = (decimal)alpha * values[i] + (1m - (decimal)alpha) * ewmaMean;
            }

            // DEBUG-LOG: Nach Mean-Berechnung
            //this.LogInfo($"ExponentialZScore: EWMA Mean={ewmaMean}");

            // Berechne EWMA-Variance (exakte gewichtete Formel f?r Genauigkeit)
            decimal sumWeights = 0m;
            decimal weightedSumSqDiff = 0m;
            for (int i = 0; i < n; i++)
            {
                // Gewicht: H?her f?r neuere Werte (i gr??er, da neueste zuletzt)
                decimal weight = (decimal)Math.Pow(1 - alpha, n - 1 - i);
                decimal diff = values[i] - ewmaMean;
                weightedSumSqDiff += weight * diff * diff;
                sumWeights += weight;
            }
            decimal ewmaVariance = (sumWeights > 0) ? weightedSumSqDiff / sumWeights : 0m;

            if (ewmaVariance < 0)
            {
                ewmaVariance = 0m;
                this.LogInfo("[ExponentialZScore] Variance negativ ? auf 0 gesetzt");  // DEBUG-LOG
            }
            decimal ewmaSd = (decimal)Math.Sqrt((double)ewmaVariance);
            if (ewmaSd == 0)
            {
                this.LogInfo("[ExponentialZScore] SD=0 ? Return 0");  // DEBUG-LOG
                return 0m;
            }

            // DEBUG-LOG: Nach Variance/SD
            //this.LogInfo($"ExponentialZScore: EWMA Variance={ewmaVariance}, SD={ewmaSd}");

            // Z-Score OHNE Inclusion von x im Mean (rein historisch ? Fix f?r Bias)
            decimal zScore = (x - ewmaMean) / ewmaSd;

            // DEBUG-LOG: Vor Return
            //this.LogInfo($"[Z] bar={CurrentBar}, x={x}, WindowSize={values.Count}, Mean={ewmaMean}, SD={ewmaSd}, Z={zScore}");

            return zScore;
        }

        //Diese Methode berechnet den Korrelationskoeffizienten (insbesondere den Pearson-Korrelationskoeffizienten) zwischen zwei Reihen von Dezimalwerten (x und y).
        //Sie verwendet die Summen der Werte, ihrer Quadrate und ihrer Produkte, um Kovarianz und Varianzen zu bestimmen und daraus den Korrelationskoeffizienten abzuleiten. Das Ergebnis wird zwischen -1 und 1 begrenzt.
        private decimal Corr(IEnumerable<(decimal x, decimal y)> pairs)
        {
            int n = 0;
            decimal sumX = 0m, sumY = 0m, sumXX = 0m, sumYY = 0m, sumXY = 0m;
            foreach (var (x, y) in pairs)
            {
                n++;
                sumX += x; sumY += y;
                sumXX += x * x; sumYY += y * y;
                sumXY += x * y;
            }
            if (n < 2) return 0m;
            decimal cov = (sumXY - (sumX * sumY) / n) / (n - 1);
            decimal varX = (sumXX - (sumX * sumX) / n) / (n - 1);
            decimal varY = (sumYY - (sumY * sumY) / n) / (n - 1);
            if (varX <= 0 || varY <= 0) return 0m;
            decimal r = (decimal)((double)cov / Math.Sqrt((double)(varX * varY)));
            return Clamp(r, -1m, 1m);
        }

        //Diese einfache Methode berechnet die Summe aller Dezimalwerte in einer gegebenen Sequenz (seq).
        private decimal Sum(IEnumerable<decimal> seq)
        {
            decimal s = 0m; foreach (var v in seq) s += v; return s;
        }

        //Diese Methode ruft den Schlusskurs der Kerze am angegebenen bar-Index ab.
        //Sie bietet eine "sichere" R?ckgabe, indem sie _prevClose (den vorherigen Schlusskurs) zur?ckgibt, falls der bar-Index ung?ltig ist (kleiner als 0) oder keine Kerzendaten f?r diesen Index verf?gbar sind.
        private decimal GetCloseSafe(int bar)
        {
            if (bar < 0) return _prevClose;
            var c = GetCandle(bar);
            return c != null ? c.Close : _prevClose;
        }


        //Die Clamp-Methode sorgt daf?r, dass ein Dezimalwert v innerhalb eines angegebenen Bereichs bleibt, der durch lo (Untergrenze) und hi (Obergrenze) definiert ist.
        //Wenn v kleiner als lo ist, gibt die Methode lo zur?ck.
        //Wenn v gr??er als hi ist, gibt die Methode hi zur?ck.
        //Andernfalls (wenn v bereits zwischen lo und hi liegt), gibt sie v unver?ndert zur?ck.
        private decimal Clamp(decimal v, decimal lo, decimal hi) => v < lo ? lo : (v > hi ? hi : v);

        private IEnumerable<int> BarsInWindowBySeconds(int bar, double secondsBack)
        {
            var tEnd = GetCandle(bar).Time;
            var tStart = tEnd.AddSeconds(-secondsBack);
            for (int j = bar; j >= 0; j--)
            {
                var c = GetCandle(j);
                if (c == null) yield break;
                if (c.Time < tStart) yield break;
                yield return j;
            }
        }

        private List<ClusterAgg> GetClusterWindowAggregate(int bar, double secondsBack)
        {
            var dict = new Dictionary<decimal, ClusterAgg>();
            foreach (var j in BarsInWindowBySeconds(bar, secondsBack))
            {
                foreach (var cl in EnumerateClusterLevels(j))
                {
                    if (!dict.TryGetValue(cl.Price, out var agg))
                        agg = new ClusterAgg { Price = cl.Price };
                    agg.Ask += cl.AskVol;
                    agg.Bid += cl.BidVol;
                    dict[cl.Price] = agg;
                }
            }
            return dict.Values.OrderBy(x => x.Price).ToList();
        }

        private decimal GetTradeRateZ(int bar)
        {
            // Annahme: _tradeRateZ ist Dictionary<int, decimal>
            return GetOr0(_tradeRateZ, bar);
        }

        // Sweep-Approx aus Candle-Daten
        private bool DetectSweepFromClusters(int bar, SweepSide side)
        {
            var cNow = GetCandle(bar);
            if (cNow == null) return false;

            var tick = InstrumentInfo?.TickSize ?? _tickSize;
            if (tick <= 0) return false;

            // 1) Fenster sammeln
            var clusters = GetClusterWindowAggregate(bar, Sweep_MaxSeconds);
            if (clusters.Count == 0) return false;

            // 2) Startpreis = Open der ?ltesten Bar im Fenster, High/Low ?ber Fenster
            int oldestIdx = BarsInWindowBySeconds(bar, Sweep_MaxSeconds).LastOrDefault();
            var oldest = GetCandle(oldestIdx);
            if (oldest == null) return false;

            decimal start = oldest.Open;
            decimal hi = decimal.MinValue, lo = decimal.MaxValue;
            foreach (var j in BarsInWindowBySeconds(bar, Sweep_MaxSeconds))
            {
                var cc = GetCandle(j);
                if (cc == null) continue;
                hi = Math.Max(hi, cc.High);
                lo = Math.Min(lo, cc.Low);
            }
            if (hi == decimal.MinValue || lo == decimal.MaxValue) return false;

            // 3) Levels relativ zum Start
            int upLevels = (int)Math.Floor((hi - start) / tick);
            int downLevels = (int)Math.Floor((start - lo) / tick);

            // 4) Richtungsspezifische Mindestbedingungen
            decimal pathFrom, pathTo;
            if (side == SweepSide.Up)
            {
                if (upLevels < Sweep_MinLevels) return false;
                if (downLevels > Sweep_MaxOppRetraceLevels) return false;
                pathFrom = start + tick;
                pathTo = hi;
            }
            else
            {
                if (downLevels < Sweep_MinLevels) return false;
                if (upLevels > Sweep_MaxOppRetraceLevels) return false;
                pathFrom = lo;
                pathTo = start - tick;
            }

            // 5) Pfad-Purity, Zero-Opposite, stacked imbalance
            int pathLevels = 0;
            int dominantLevels = 0;
            int zeroOppLevels = 0;

            int bestImbRun = 0, currImbRun = 0;

            decimal totalAsk = 0m, totalBid = 0m;

            foreach (var lvl in clusters)
            {
                if (lvl.Price < Math.Min(pathFrom, pathTo) || lvl.Price > Math.Max(pathFrom, pathTo))
                    continue;

                pathLevels++;
                totalAsk += lvl.Ask;
                totalBid += lvl.Bid;

                decimal sum = lvl.Ask + lvl.Bid;
                if (sum <= 0) continue;

                decimal dom = side == SweepSide.Up ? (lvl.Ask / sum) : (lvl.Bid / sum);
                if (dom >= Sweep_LevelDominance) dominantLevels++;

                // Zero-Opposite (tolerant, z. B. <= 1 Kontrakt als "zero")
                bool zeroOpp = side == SweepSide.Up ? (lvl.Bid <= 1m) : (lvl.Ask <= 1m);
                if (zeroOpp) zeroOppLevels++;

                // Stacked imbalance pro Level (Ask/Bid Verh?ltnis)
                decimal ratio = side == SweepSide.Up
                    ? ((lvl.Bid > 0m) ? (lvl.Ask / lvl.Bid) : decimal.MaxValue)
                    : ((lvl.Ask > 0m) ? (lvl.Bid / lvl.Ask) : decimal.MaxValue);
                if (ratio >= Sweep_ImbalanceRatioMin)
                {
                    currImbRun++;
                    bestImbRun = Math.Max(bestImbRun, currImbRun);
                }
                else
                {
                    currImbRun = 0;
                }
            }

            if (pathLevels <= 0) return false;

            decimal purity = (decimal)dominantLevels / pathLevels;
            decimal zeroFrac = (decimal)zeroOppLevels / pathLevels;

            if (purity < Sweep_PathPurityFrac) return false;
            if (zeroFrac < Sweep_ZeroOppFracMin) return false;              // optional, ggf. abschw?chen/abschalten
            if (bestImbRun < Sweep_ImbalanceRunMin) return false;           // optional, falls zu streng: Kommentar entfernen

            // 6) Gesamt-Aggressor-Anteil im Fenster
            decimal total = totalAsk + totalBid;
            if (total <= 0m) return false;

            decimal aggRatio = side == SweepSide.Up ? (totalAsk / total) : (totalBid / total);
            if (aggRatio < Sweep_MinAggRatio) return false;

            // Optional: Speed-Gate via TradeRateZ
            bool speedOk = true;
            if (UseSpeedGate)
            {
                decimal z = GetTradeRateZ(bar); // nutzt deinen GetOr0-Helper
                speedOk = z >= Sweep_MinTradeRateZ;
            }

            return speedOk;
        }

        // =========================================================================
        // Stacked-Imbalance | Anfang
        // =========================================================================
        
        public struct StackedImbalanceResult
        {
            // l?ngster Stack irgendwo im Bar
            public int BuyCountMax;
            public int SellCountMax;

            // anchored am Bar-Extrem (direkt unter dem High bzw. ?ber dem Low)
            public int BuyCountTopAnchored;
            public int SellCountBottomAnchored;

            // optional: Preise der anchored Stacks (kannst du bei Bedarf loggen)
            public decimal[] BuyTopAnchoredPrices;
            public decimal[] SellBottomAnchoredPrices;

            public int TotalPairs;
            public int BuyPairsCount;
            public int SellPairsCount;
            public decimal AvgBuyImbVol;
            public decimal AvgSellImbVol;
            public decimal BaseVolMedian;

            public decimal ImbalanceScore;
            public string ImbalanceScoreLabel;

            // Guard: zu wenige Preis-Level f?r Imbalance-Berechnung
            public bool InsufficientLevels;
        }

        private List<decimal[]> BuildVolumesArrayForBar(int bar)
        {
            var res = new List<decimal[]>();

            var cndl = GetCandle(bar);
            if (cndl == null) return res;

            // TickSize sicher bestimmen (kein InstrumentInfo!)
            decimal ts = InstrumentInfo?.TickSize ?? _tickSize;
            if (ts <= 0m) return res;

            for (var price = cndl.Low; price <= cndl.High; price += ts)
            {
                var vi = cndl.GetPriceVolumeInfo(price);
                if (vi == null) continue;
                res.Add(new[] { price, vi.Bid, vi.Ask });
            }
            return res;
        }

        private StackedImbalanceResult ComputeStackedImbalanceForBar(int bar, StackedImbParams p)
        {
            var vols = BuildVolumesArrayForBar(bar);
            int n = vols.Count;
            var res = new StackedImbalanceResult
            {
                BuyTopAnchoredPrices = Array.Empty<decimal>(),
                SellBottomAnchoredPrices = Array.Empty<decimal>()
            };

            if (n < 2)
            {
                res.InsufficientLevels = true;
                return res;
            }

            decimal ratioFactor = p.ImbalanceRatioPct / 100m;
            res.TotalPairs = n - 1;

            // Flags: wo liegt eine Imbalance vor?
            // Buy (Bid/Ask): Bid(i+1) > Ask(i) * ratio AND Bid(i+1) > minVol
            // Sell (Ask/Bid): Ask(i+1) > Bid(i) * ratio AND Ask(i+1) > minVol
            var buyImb = new bool[n];  // i refers to pair (i, i+1)
            var sellImb = new bool[n];

            var buyImbVolumes = new List<decimal>();
            var sellImbVolumes = new List<decimal>();
            var baseVolumes = new List<decimal>(n * 2);

            for (int i = 0; i < n - 1; i++)
            {
                decimal bidHigh = vols[i + 1][1];
                decimal askLow = vols[i][2];
                baseVolumes.Add((bidHigh + askLow) / 2m);

                decimal bidFilter = askLow * ratioFactor;
                if (!(p.IgnoreZeroValues && bidFilter == 0m))
                {
                    if (bidHigh > bidFilter && bidHigh > p.ImbalanceVolumeMin)
                    {
                        buyImb[i] = true;
                        buyImbVolumes.Add(bidHigh);
                    }
                }

                decimal askHigh = vols[i + 1][2];
                decimal bidLow = vols[i][1];
                baseVolumes.Add((askHigh + bidLow) / 2m);
                decimal askFilter = bidLow * ratioFactor;
                if (!(p.IgnoreZeroValues && askFilter == 0m))
                {
                    if (askHigh > askFilter && askHigh > p.ImbalanceVolumeMin)
                    {
                        sellImb[i] = true;
                        sellImbVolumes.Add(askHigh);
                    }
                }
            }

            // l?ngster zusammenh?ngender Block irgendwo
            res.BuyCountMax = LongestConsecutiveTrue(buyImb);
            res.SellCountMax = LongestConsecutiveTrue(sellImb);

            res.BuyPairsCount = buyImbVolumes.Count;
            res.SellPairsCount = sellImbVolumes.Count;
            res.AvgBuyImbVol = buyImbVolumes.Count > 0 ? buyImbVolumes.Average() : 0m;
            res.AvgSellImbVol = sellImbVolumes.Count > 0 ? sellImbVolumes.Average() : 0m;
            res.BaseVolMedian = ComputeMedian(baseVolumes);

            if (res.BuyPairsCount > 0 && res.AvgBuyImbVol < p.ImbalanceVolumeMin)
            {
                res.BuyPairsCount = 0;
                res.AvgBuyImbVol = 0m;
            }

            if (res.SellPairsCount > 0 && res.AvgSellImbVol < p.ImbalanceVolumeMin)
            {
                res.SellPairsCount = 0;
                res.AvgSellImbVol = 0m;
            }

            res.ImbalanceScore = ComputeImbalanceScore(res, p, out var scoreLabel);
            res.ImbalanceScoreLabel = scoreLabel;

            // anchored: direkt unter High ? i = n-2 downward, begrenzt durch MaxDepthTicksAnchored
            res.BuyCountTopAnchored = 0;
            var topBuyPrices = new List<decimal>();
            int maxPairsTop = Math.Max(0, Math.Min(p.MaxDepthTicksAnchored, n - 1)); // Anzahl Paare
            for (int k = 0; k < maxPairsTop; k++)
            {
                int i = (n - 2) - k;
                if (i < 0) break;

                if (buyImb[i])
                {
                    res.BuyCountTopAnchored++;
                    // Preislevel im Stack f?r Buy: wir nehmen den oberen Preis der Paarung (i+1)
                    topBuyPrices.Add(vols[i + 1][0]);
                }
                else break;
            }
            res.BuyTopAnchoredPrices = topBuyPrices.Count > 0 ? topBuyPrices.ToArray() : Array.Empty<decimal>();

            // anchored: direkt ?ber Low ? i = 0 upward, begrenzt
            res.SellCountBottomAnchored = 0;
            var bottomSellPrices = new List<decimal>();
            int maxPairsBottom = Math.Max(0, Math.Min(p.MaxDepthTicksAnchored, n - 1));
            for (int i = 0; i < maxPairsBottom; i++)
            {
                if (i >= n - 1) break;
                if (sellImb[i])
                {
                    res.SellCountBottomAnchored++;
                    // Preislevel f?r Sell: unterer Preis der Paarung (i)
                    bottomSellPrices.Add(vols[i][0]);
                }
                else break;
            }
            res.SellBottomAnchoredPrices = bottomSellPrices.Count > 0 ? bottomSellPrices.ToArray() : Array.Empty<decimal>();

            return res;
        }

        private int LongestConsecutiveTrue(bool[] arr)
        {
            int best = 0, cur = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i]) { cur++; if (cur > best) best = cur; }
                else cur = 0;
            }
            return best;
        }

        private decimal ComputeMedian(List<decimal> values)
        {
            if (values == null || values.Count == 0) return 0m;
            values.Sort();
            int mid = values.Count / 2;
            if (values.Count % 2 == 0)
                return (values[mid - 1] + values[mid]) / 2m;
            return values[mid];
        }

        private decimal ComputeImbalanceScore(StackedImbalanceResult result, StackedImbParams p, out string label)
        {
            const decimal wCoverage = 0.5m;
            const decimal wAnchored = 0.3m;
            const decimal wVolume = 0.2m;
            const decimal eps = 1e-6m;

            if (result.TotalPairs <= 1)
            {
                label = "insufficient";
                return 0m;
            }

            decimal coverageBuy = Clamp01((decimal)result.BuyCountMax / result.TotalPairs);
            decimal coverageSell = Clamp01((decimal)result.SellCountMax / result.TotalPairs);

            decimal anchoredBuy = p.MaxDepthTicksAnchored > 0
                ? Clamp01((decimal)result.BuyCountTopAnchored / p.MaxDepthTicksAnchored)
                : 0m;
            decimal anchoredSell = p.MaxDepthTicksAnchored > 0
                ? Clamp01((decimal)result.SellCountBottomAnchored / p.MaxDepthTicksAnchored)
                : 0m;

            decimal volBuyNorm = result.BaseVolMedian > 0m ? result.AvgBuyImbVol / (result.BaseVolMedian + eps) : 0m;
            decimal volSellNorm = result.BaseVolMedian > 0m ? result.AvgSellImbVol / (result.BaseVolMedian + eps) : 0m;

            decimal volBuyScore = Clamp(volBuyNorm - 1m, -1m, 1m);
            decimal volSellScore = Clamp(volSellNorm - 1m, -1m, 1m);
            decimal volBuyScore01 = (volBuyScore + 1m) / 2m;
            decimal volSellScore01 = (volSellScore + 1m) / 2m;

            decimal weightedBuy = (wCoverage * coverageBuy) + (wAnchored * anchoredBuy) + (wVolume * volBuyScore01);
            decimal weightedSell = (wCoverage * coverageSell) + (wAnchored * anchoredSell) + (wVolume * volSellScore01);
            decimal raw = weightedBuy - weightedSell;
            decimal score = raw / (wCoverage + wAnchored + wVolume);
            score = Clamp(score, -1m, 1m);

            if (score >= 0.5m) label = "strong_buy";
            else if (score >= 0.2m) label = "buy";
            else if (score <= -0.5m) label = "strong_sell";
            else if (score <= -0.2m) label = "sell";
            else label = "neutral";

            return score;
        }

        private static decimal Clamp01(decimal value) => Clamp(value, 0m, 1m);

        private static decimal Clamp(decimal value, decimal min, decimal max)
            => value < min ? min : (value > max ? max : value);
        // =========================================================================
        // Stacked-Imbalance | Ende
        // =========================================================================

        private bool IsRealtimeBar(int bar)
        {
            // In ATAS: CurrentBar ist der aktuelle (live) Bar-Index.
            // Historische Bars haben bar < CurrentBar - 1
            return bar == CurrentBar - 1;  // Oder: return !IsHistorical; wenn ATAS das Property hat
        }





        private void CheckAndRecreateCsvWriterIfNeeded()
        {
            if (!EnableCsvExport || !UseDailyCsvFiles)
                return;
                
            // Wenn noch kein Writer existiert, erstmalig erstellen
            if (_csvWriter == null)
            {
                // WICHTIG: Nur erstellen, wenn wir wirklich gültige Bars haben
                if (CurrentBar >= 0)
                {
                    this.LogInfo("[CheckAndRecreateCsvWriterIfNeeded] CSV Writer will be created on first valid bar");
                    InitializeDailyCsvWriter();
                }
                else
                {
                    // Noch keine gültigen Bars, warten
                    return;
                }
                return;
            }
                
            // WICHTIG: Nicht mehr GetCurrentBarDate() aufrufen!
            // Verwende das gespeicherte Datum für die gesamte Session
            // Das verhindert die "Index out of range" Fehler
            return;
        }

        private void InitializeDailyCsvWriter()
        {
            try
            {
                // WICHTIG: Verwende das Backtest-Datum von der ersten gültigen Bar
                string backtestDate = GetBacktestDateFromFirstBar();
                
                // Instrumentnamen sicher für Dateiname machen
                string instrumentName = (InstrumentInfo?.Instrument ?? InstrumentInfo?.ToString() ?? "Unknown");
                foreach (var ch in System.IO.Path.GetInvalidFileNameChars())
                    instrumentName = instrumentName.Replace(ch, '_');
                
                string timeframeLabel = _researchTimeframeLabel ?? "TF";
                string outDir = @"C:\Users\User\Documents\Strategieauswertung";
                System.IO.Directory.CreateDirectory(outDir);
                
                _csvPath = System.IO.Path.Combine(outDir, $"{instrumentName}_{timeframeLabel}_ovsnapshots_{backtestDate}.csv");
                
                // Falls Überschreiben aktiviert und Datei existiert, löschen
                if (OverwriteExistingCsv && System.IO.File.Exists(_csvPath))
                {
                    try
                    {
                        System.IO.File.Delete(_csvPath);
                        this.LogInfo($"[OnInitialize] Existing CSV file deleted: {_csvPath}");
                    }
                    catch (Exception ex)
                    {
                        this.LogWarn($"[OnInitialize] Could not delete existing CSV file {_csvPath}: {ex.Message}");
                    }
                }
                
                _csvWriter = new BackgroundCsvWriter(_csvPath, CsvHeader);
                _currentCsvDate = backtestDate;
                
                this.LogInfo($"[InitializeDailyCsvWriter] CSV Writer created -> path={_csvPath}");
            }
            catch (Exception ex)
            {
                this.LogError($"[InitializeDailyCsvWriter] Failed to initialize CSV writer: {ex.Message}");
            }
        }

        private string GetBacktestDateFromFirstBar()
        {
            try
            {
                // WICHTIG: Wir haben jetzt gültige Bars (CurrentBar >= 0)
                // Wir brauchen das Datum von der ersten DATEN-Bar, nicht von CurrentBar
                // CurrentBar gibt oft das aktuelle Systemdatum zurück während der Initialisierung
                
                if (CurrentBar >= 0)
                {
                    // Versuche, das Datum von der ersten Daten-Bar zu bekommen (Bar 0)
                    // Das ist der Anfang der tatsächlichen Backtest-Daten
                    var firstDataCandle = GetCandle(0);
                    if (firstDataCandle != null)
                    {
                        var localTime = firstDataCandle.Time;
                        var utcTime = firstDataCandle.Time.ToUniversalTime();
                        var result = utcTime.ToString("yyyy-MM-dd");
                        
                        // Prüfen, ob das Datum sinnvoll ist (nicht das heutige Datum)
                        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                        if (result != today)
                        {
                            this.LogInfo($"[GetBacktestDateFromFirstBar] INFO: Using FirstDataBar (Bar 0) - Local={localTime:O}, UTC={utcTime:O}, Result={result}");
                            
                            // Speichere dieses Datum für zukünftige Verwendung
                            _storedBacktestDate = result;
                            this.LogInfo($"[GetBacktestDateFromFirstBar] INFO: Stored first data bar date: {_storedBacktestDate}");
                            
                            return result;
                        }
                        else
                        {
                            this.LogInfo("[GetBacktestDateFromFirstBar] INFO: FirstDataBar returned today's date, trying to find actual backtest data");
                            
                            // Versuche, eine Bar in der Mitte des Datensatzes zu finden
                            // Das sollte das echte Backtest-Datum sein
                            return GetBacktestDateFromMiddleOfData();
                        }
                    }
                    else
                    {
                        this.LogInfo("[GetBacktestDateFromFirstBar] INFO: GetCandle(0) returned null, trying middle of data");
                        return GetBacktestDateFromMiddleOfData();
                    }
                }
            }
            catch (Exception ex)
            {
                this.LogWarn($"[GetBacktestDateFromFirstBar] Failed: {ex.Message}");
            }
            
            // Absoluter Fallback
            var fallback = DateTime.UtcNow.ToString("yyyy-MM-dd");
            _storedBacktestDate = fallback;
            this.LogInfo($"[GetBacktestDateFromFirstBar] INFO: Using fallback UTC date: {fallback}");
            return fallback;
        }

        private string GetBacktestDateFromMiddleOfData()
        {
            try
            {
                // Versuche, eine Bar in der Mitte des Datensatzes zu finden
                // Das sollte das echte Backtest-Datum sein, nicht die historischen Startdaten
                if (CurrentBar >= 10)
                {
                    var middleCandle = GetCandle(CurrentBar / 2);
                    if (middleCandle != null)
                    {
                        var localTime = middleCandle.Time;
                        var utcTime = middleCandle.Time.ToUniversalTime();
                        var result = utcTime.ToString("yyyy-MM-dd");
                        
                        this.LogInfo($"[GetBacktestDateFromMiddleOfData] INFO: Using middle bar (Bar {CurrentBar / 2}) - Local={localTime:O}, UTC={utcTime:O}, Result={result}");
                        
                        // Prüfen, ob das Datum sinnvoll ist
                        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                        if (result != today)
                        {
                            _storedBacktestDate = result;
                            this.LogInfo($"[GetBacktestDateFromMiddleOfData] INFO: Stored middle bar date: {_storedBacktestDate}");
                            return result;
                        }
                    }
                }
                
                // Fallback: Versuche die aktuelle Bar (wenn sie nicht heute ist)
                var currentCandle = GetCandle(CurrentBar);
                if (currentCandle != null)
                {
                    var localTime = currentCandle.Time;
                    var utcTime = currentCandle.Time.ToUniversalTime();
                    var result = utcTime.ToString("yyyy-MM-dd");
                    
                    var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                    if (result != today)
                    {
                        this.LogInfo($"[GetBacktestDateFromMiddleOfData] INFO: Using CurrentBar as fallback - Local={localTime:O}, UTC={utcTime:O}, Result={result}");
                        _storedBacktestDate = result;
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                this.LogWarn($"[GetBacktestDateFromMiddleOfData] Failed: {ex.Message}");
            }
            
            // Absoluter Fallback
            var fallback = DateTime.UtcNow.ToString("yyyy-MM-dd");
            _storedBacktestDate = fallback;
            this.LogInfo($"[GetBacktestDateFromMiddleOfData] INFO: Using absolute fallback: {fallback}");
            return fallback;
        }

        private string GetStoredBacktestDate()
        {
            // Wenn wir bereits ein gespeichertes Datum haben, verwende es
            if (!string.IsNullOrEmpty(_storedBacktestDate))
            {
                this.LogInfo($"[GetStoredBacktestDate] INFO: Using stored backtest date: {_storedBacktestDate}");
                return _storedBacktestDate;
            }
            
            // WICHTIG: Wie ResearchCollector - speichere die Zeit BEIM ERSTEN AUFRUF
            // und verwende sie für die gesamte Session
            try
            {
                this.LogInfo("[GetStoredBacktestDate] INFO: No stored date available, capturing current bar time");
                
                // Versuche, die Zeit von der aktuellen Bar zu bekommen (wie ResearchCollector)
                if (CurrentBar >= 0)
                {
                    var currentCandle = GetCandle(CurrentBar);
                    if (currentCandle != null)
                    {
                        var localTime = currentCandle.Time;
                        var utcTime = currentCandle.Time.ToUniversalTime();
                        var result = utcTime.ToString("yyyy-MM-dd");
                        
                        // Speichere das Datum für die gesamte Session
                        _storedBacktestDate = result;
                        this.LogInfo($"[GetStoredBacktestDate] INFO: Captured and stored backtest date from current bar: {_storedBacktestDate}");
                        return _storedBacktestDate;
                    }
                    else
                    {
                        this.LogInfo("[GetStoredBacktestDate] INFO: GetCandle(CurrentBar) returned null");
                    }
                }
                else
                {
                    this.LogInfo("[GetStoredBacktestDate] INFO: CurrentBar < 0, no valid bars yet");
                }
            }
            catch (Exception ex)
            {
                this.LogWarn($"[GetStoredBacktestDate] Failed to capture backtest date: {ex.Message}");
            }
            
            // Wenn alles fehlschlägt, verwenden Sie das aktuelle Datum
            var fallback = DateTime.UtcNow.ToString("yyyy-MM-dd");
            _storedBacktestDate = fallback;
            this.LogInfo($"[GetStoredBacktestDate] INFO: Using fallback UTC date: {_storedBacktestDate}");
            return _storedBacktestDate;
        }

        private string GetCurrentBarDate()
        {
            try
            {
                // WICHTIG: Nur wenn wir wirklich gültige Bars haben
                if (CurrentBar >= 0)
                {
                    // Versuche, die Zeit von der aktuellen Bar zu bekommen
                    var currentCandle = GetCandle(CurrentBar);
                    if (currentCandle != null)
                    {
                        var localTime = currentCandle.Time;
                        var utcTime = currentCandle.Time.ToUniversalTime();
                        var result = utcTime.ToString("yyyy-MM-dd");
                        
                        this.LogInfo($"[GetCurrentBarDate] INFO: Using CurrentBar - CurrentBar={CurrentBar}, Local={localTime:O}, UTC={utcTime:O}, Result={result}");
                        return result;
                    }
                    else
                    {
                        this.LogInfo("[GetCurrentBarDate] INFO: GetCandle(CurrentBar) returned null, trying first bar");
                    }
                }
                else
                {
                    // WICHTIG: Kein Error-Log mehr, wenn CurrentBar < 0 - das ist normal während der Initialisierung
                    this.LogDebug("[GetCurrentBarDate] DEBUG: CurrentBar < 0, no valid bars yet, using stored date");
                    
                    // Wenn noch keine gültigen Bars da sind, verwende das gespeicherte Datum
                    if (!string.IsNullOrEmpty(_storedBacktestDate))
                    {
                        this.LogDebug($"[GetCurrentBarDate] DEBUG: Using stored backtest date: {_storedBacktestDate}");
                        return _storedBacktestDate;
                    }
                }
                
                // Fallback: Versuche, die Zeit von der ersten Bar zu bekommen
                if (CurrentBar >= 0)
                {
                    var firstCandle = GetCandle(0);
                    if (firstCandle != null)
                    {
                        var localTime = firstCandle.Time;
                        var utcTime = firstCandle.Time.ToUniversalTime();
                        var result = utcTime.ToString("yyyy-MM-dd");
                        
                        this.LogInfo($"[GetCurrentBarDate] INFO: Using FirstCandle - Local={localTime:O}, UTC={utcTime:O}, Result={result}");
                        
                        // Speichere dieses Datum für zukünftige Verwendung
                        if (string.IsNullOrEmpty(_storedBacktestDate))
                        {
                            _storedBacktestDate = result;
                            this.LogInfo($"[GetCurrentBarDate] INFO: Stored first candle date: {_storedBacktestDate}");
                        }
                        
                        return result;
                    }
                    else
                    {
                        this.LogInfo("[GetCurrentBarDate] INFO: GetCandle(0) returned null");
                    }
                }
            }
            catch (Exception ex)
            {
                // Nur noch als Debug loggen, nicht als Warn - das ist während der Initialisierung normal
                this.LogDebug($"[GetCurrentBarDate] DEBUG: Could not get bar date: {ex.Message}");
            }
            
            // Absoluter Fallback auf gespeichertes Datum oder aktuelles Datum
            if (!string.IsNullOrEmpty(_storedBacktestDate))
            {
                this.LogDebug($"[GetCurrentBarDate] DEBUG: Using stored backtest date as fallback: {_storedBacktestDate}");
                return _storedBacktestDate;
            }
            
            var fallback = DateTime.UtcNow.ToString("yyyy-MM-dd");
            this.LogDebug($"[GetCurrentBarDate] DEBUG: Using absolute fallback UTC date: {fallback}");
            return fallback;
        }

        private string GetBacktestDateFromChart()
        {
            try
            {
                // Methode 2: Versuche, das Datum von der ersten Bar zu bekommen
                if (CurrentBar >= 0)
                {
                    var firstCandle = GetCandle(0);
                    if (firstCandle != null)
                    {
                        var localTime = firstCandle.Time;
                        var utcTime = firstCandle.Time.ToUniversalTime();
                        var result = utcTime.ToString("yyyy-MM-dd");
                        
                        this.LogInfo($"[GetCurrentBarDate] INFO: Using FirstCandle - Local={localTime:O}, UTC={utcTime:O}, Result={result}");
                        
                        // Wenn auch das erste Bar das heutige Datum hat, versuchen wir es mit einer anderen Methode
                        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                        if (result == today)
                        {
                            this.LogInfo($"[GetCurrentBarDate] INFO: FirstCandle also returned today's date, trying chart-based method");
                            return GetDateFromChartInfo();
                        }
                        
                        return result;
                    }
                    else
                    {
                        this.LogInfo("[GetCurrentBarDate] INFO: GetCandle(0) returned null");
                    }
                }
                
                // Methode 3: Versuche, das Datum aus ChartInfo zu bekommen
                return GetDateFromChartInfo();
            }
            catch (Exception ex)
            {
                this.LogWarn($"[GetCurrentBarDate] GetBacktestDateFromChart failed: {ex.Message}");
            }
            
            // Absoluter Fallback auf aktuelles Datum
            var fallback = DateTime.UtcNow.ToString("yyyy-MM-dd");
            this.LogInfo($"[GetCurrentBarDate] INFO: Using absolute fallback UTC date: {fallback}");
            return fallback;
        }

        private string GetDateFromChartInfo()
        {
            try
            {
                // WICHTIG: Wir brauchen das Datum vom LETZTEN BAR im Chart (der eigentliche Test-Tag)
                // nicht vom Anfang oder von der Mitte
                
                this.LogInfo($"[GetCurrentBarDate] INFO: Trying to get date from LAST BAR in chart");
                
                if (CurrentBar >= 0)
                {
                    // Das ist der entscheidende Punkt: Wir nehmen die LETZTE Bar (CurrentBar)
                    // Das ist der Tag, der eigentlich getestet wird
                    var lastCandle = GetCandle(CurrentBar);
                    if (lastCandle != null)
                    {
                        var localTime = lastCandle.Time;
                        var utcTime = lastCandle.Time.ToUniversalTime();
                        var result = utcTime.ToString("yyyy-MM-dd");
                        
                        this.LogInfo($"[GetCurrentBarDate] INFO: Using LAST Bar ({CurrentBar}) - Local={localTime:O}, UTC={utcTime:O}, Result={result}");
                        
                        // Prüfen, ob das Ergebnis sinnvoll ist (nicht das heutige Datum)
                        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                        if (result == today)
                        {
                            this.LogInfo($"[GetCurrentBarDate] INFO: Last bar also returned today's date - ATAS limitation detected");
                            
                            // Wenn ATAS wirklich immer das heutige Datum liefert, versuchen wir einen anderen Ansatz:
                            // Wir könnten das Datum manuell aus dem Chart-Titel oder einer anderen Quelle extrahieren
                            // Aber vorerst geben wir das heutige Datum zurück mit entsprechender Log-Meldung
                            this.LogInfo($"[GetCurrentBarDate] INFO: ATAS limitation: Cannot extract real backtest date, using current date");
                        }
                        else
                        {
                            this.LogInfo($"[GetCurrentBarDate] INFO: SUCCESS: Got backtest date from last bar: {result}");
                        }
                        
                        return result;
                    }
                    else
                    {
                        this.LogInfo($"[GetCurrentBarDate] INFO: GetCandle(CurrentBar) returned null");
                    }
                }
                else
                {
                    this.LogInfo($"[GetCurrentBarDate] INFO: CurrentBar < 0, no bars available");
                }
                
                this.LogInfo("[GetCurrentBarDate] INFO: ChartInfo method failed, no alternative available");
            }
            catch (Exception ex)
            {
                this.LogWarn($"[GetCurrentBarDate] GetDateFromChartInfo failed: {ex.Message}");
            }
            
            // Wenn alles fehlschlägt, verwenden wir das aktuelle Datum
            var fallback = DateTime.UtcNow.ToString("yyyy-MM-dd");
            this.LogInfo($"[GetCurrentBarDate] INFO: All methods failed, using current UTC date: {fallback}");
            return fallback;
        }

        // Hauptlogik pro Bar und pro Tick auf dem letzten Bar
        protected override void OnCalculate(int bar, decimal value)
        {
            

            int maxIdx = CurrentBar;

            if (bar < 0 || maxIdx < 0 || bar > maxIdx || InstrumentInfo == null || InstrumentInfo.Instrument == null || _tickSize == 0m)
            {
                this.LogInfo($"OnCalculate: Ungültiger Zustand (bar={bar}, maxIdx={maxIdx}, InstrumentInfo={InstrumentInfo != null}, Instrument={InstrumentInfo?.Instrument != null}, _tickSize={_tickSize}) -> überspringe.");
                return;
            }
            
            // Tageswechsel prüfen und ggf. neuen Writer erstellen
            CheckAndRecreateCsvWriterIfNeeded();
            int b = bar;

            var c = GetCandle(bar);
            if (c == null) // Explizite Null-Pr?fung f?r aktuelle Kerze
            {
                this.LogWarn($"OnCalculate: Aktuelle Kerze f?r Bar {bar} ist null. ?berspringe weitere Verarbeitung.");
                return;
            }

            var p = (b > 0) ? GetCandle(b - 1) : c;
            if (p == null) // Explizite Null-Pr?fung f?r vorherige Kerze
            {
                this.LogWarn($"OnCalculate: Vorherige Kerze f?r Bar {b - 1} ist null. ?berspringe weitere Verarbeitung.");
                return;
            }

            double seconds = (c.Time - p.Time).TotalSeconds;
            if (seconds <= 0) seconds = 1;


            if (_entryLogicLockedUntilLive && maxIdx > _liveStartBar)
            {
                _entryLogicLockedUntilLive = false;
                this.LogInfo("[Init] Historienphase beendet ? Einstiegslogik freigegeben.");
            }

            if (_lastCalculatedBar == -1)
            {
                _lastCalculatedBar = bar;
                this.LogDebug("[OnCalculate] Init: first bar={0}", bar);
                return; // Fr?hzeitiger Ausstieg f?r die Initialisierung des ersten Balkens
            }

            if (_featureCalculator == null)
            {
                this.LogError("OrderflowFeatureCalculator wurde nicht initialisiert. ?berspringe Orderflow-Berechnungen.");
                // Da dies ein kritischer Fehler ist, k?nnte man hier auch einen "return" in Betracht ziehen,
                // wenn die folgenden Berechnungen stark davon abh?ngen.
                // F?r diesen Codeabschnitt belassen wir es vorerst bei einem Log und pr?fen sp?ter,
                // ob Folgefehler ohne _featureCalculator auftreten.
            }

            if (bar > _lastCalculatedBar) // erster Abschluss nach Initialisierung
            {
                // Die vorherige Kerze (bar-1) ist jetzt geschlossen => Index = _lastCalculatedBar
                _buyTradesSeries[_lastCalculatedBar] = _currentBarBuyTrades;
                _sellTradesSeries[_lastCalculatedBar] = _currentBarSellTrades;
                _totalTradesSeries[_lastCalculatedBar] = _currentBarBuyTrades + _currentBarSellTrades;



                // Optional: Signal der NEUEN Kerze vorinitialisieren
                _entrySignalSeries[bar] = 0;

                // MicroComposite auf Basis der abgeschlossenen Kerze aktualisieren
                int mcBaseBar = _lastCalculatedBar;
                UpdateMicroCompositeRolling(mcBaseBar);
                _currentMC = BuildMicroCompositeFromHist(_mcHist, SmoothingTicks, TopNPeaks);

                // Feature auf abgeschlossener Kerze ausf?hren
                HandleVolumeWindowFeature(mcBaseBar);



                // Z?hler f?r die NEUE Kerze zur?cksetzen
                _currentBarBuyTrades = 0;
                _currentBarSellTrades = 0;

                // WICHTIG: lastCalculatedBar fortschreiben
                _lastCalculatedBar = bar;
            }


            bool vwSignal = VwSignal;
            int vwEntryDir = vwSignal ? -_vwDirection : 0;
            decimal vwEntryPrice = vwSignal ? VwSignalPrice : 0m;


            decimal buyTrades = _buyTradesSeries.GetValueOrDefault(bar);
            decimal sellTrades = _sellTradesSeries.GetValueOrDefault(bar);
            decimal totalTrades = _buyTradesSeries.GetValueOrDefault(bar) + _sellTradesSeries.GetValueOrDefault(bar);

            // Rufen Sie die Liste aller Trades f?r die aktuelle Kerze ab

            var totalTradesEffizient = c.Ticks;
            var imbalanceValue = _imbalanceSeries[bar]; // Den bereits berechneten Imbalance-Wert abrufen

            if (imbalanceValue > 10 && totalTrades > 20)
            {
                _entrySignalSeries[bar] = GetCandle(bar).Close;
            }
            else
            {
                _entrySignalSeries[bar] = 0;
            }

            // Aktualisiere den Index der zuletzt verarbeiteten Kerze
            _lastCalculatedBar = bar;


            // 1) Rohdaten aus Candle
            decimal vol = c.Volume;                    // Gesamtvolumen Bar
            decimal delta = c.Delta;                   // AskVol - BidVol
            decimal askVol = c.Ask;                    // Ask-Volumen
            decimal bidVol = c.Bid;                    // Bid-Volumen
            seconds = (c.Time - p.Time).TotalSeconds;
            if (seconds <= 0) seconds = 1;             // fallback

            decimal close = GetCloseSafe(bar);
            _prevClose = close;

            // 2) Z-Score Volumenburst

            // 2) Z-Score Volumenburst (robustified: EWMA-Mean + robust/blended Scale)
            decimal volZ = RobustifiedExponentialZScore(
            windowObj: _volWin,
            alpha: (double)EwmaAlpha,
            x: vol,
            useRobustScale: true,
            robustBlend: 0.8,
            maxAbsZ: 20.0,
            madEps: 1e-2
            );
            _volBurstZ[b] = volZ;
            PushWindow(_volWin, vol, VolZ_Lookback);
            //this.LogInfo($"Bar={bar}, Vol={vol}, VolZ={volZ}, Alpha={EwmaAlpha}, Lookback={VolZ_Lookback}, WindowCount={_volWin.Count}");

            // 3) CVD Impuls (Ableitung des kumulativen Delta)
            _cvdCum += delta;
            decimal cvdImp = _cvdCum - _prevCVD;       // ?CVD
            _cvdImpulse[b] = cvdImp;
            _prevCVD = _cvdCum;

            // 4) CVD Coherence: Korrelation von ?CVD und ?Price im Fenster, gemappt auf 0..1
            decimal dCVD = cvdImp;
            decimal dPx = close - p.Close;
            PushWindow(_cohWin, (dCVD, dPx), Coherence_Lookback);
            decimal corr = Corr(_cohWin);
            decimal coh01 = (corr + 1m) / 2m;          // [-1..1] -> [0..1]
            _cvdCoherence[b] = coh01;

            // 5) Aggregierter Druck: Anteil Aggressor-K?ufe (Ask) am Gesamtvolumen
            decimal pressure = (askVol + bidVol) > 0 ? askVol / (askVol + bidVol) : 0.5m; // 0..1
            // Optional: Trades-Info einmischen (gewichtete Mischung):
            // decimal tradeSkew = (buyTrades + sellTrades) > 0 ? (decimal)buyTrades / (buyTrades + sellTrades) : 0.5m;
            // pressure = 0.7m * pressure + 0.3m * tradeSkew;
            _aggPressure[b] = pressure;

            // 6) Trade-Rate Z (Trades pro Sekunde) ? robustified (EWMA-Mean + robust/blended Scale)
            // Hinweise / Empfehlungen
            //Parameter k?nnen anders gesetzt werden, falls TradeRate-Verteilung st?rker/ leichter rauscherf?llt ist. Vorschl?ge:
            //Wenn TradeRate sehr volatil: robustBlend h?her(z.B. 0.9) ? mehr Gewicht auf MAD.
            //Wenn schnelle Reaktion wichtiger: robustBlend niedriger(z.B. 0.6) ? mehr Gewicht auf EWMA-SD.
            //maxAbsZ anpassen nach beobachteten Z-Extremen(20..200).
            decimal tradeRate = (decimal)totalTradesEffizient / (decimal)seconds;
            decimal tradeRateZ = RobustifiedExponentialZScore(
            windowObj: _tradeRateWin,
            alpha: (double)EwmaAlpha,
            x: tradeRate,
            useRobustScale: true,
            robustBlend: 0.8,
            maxAbsZ: 50.0,
            madEps: 1e-2
            );
            _tradeRateZ[b] = tradeRateZ;
            PushWindow(_tradeRateWin, tradeRate, TradeRateZ_Lookback);

            // 7) Efficiency Ratio (Kaufman, 0..1)
            decimal absMove = Math.Abs(close - p.Close);
            PushWindow(_erAbsIncr, absMove, ER_Lookback);
            decimal netChange = 0m;
            if (b >= ER_Lookback)
            {
                // statt GetCloseSafe(bar - ER_Lookback) sicherstellen, dass Index >= 0 ist
                var past = GetCandle(bar - ER_Lookback);     // hier garantiert bar - ER_Lookback >= 0
                netChange = Math.Abs(close - past.Close);
            }
            decimal sumAbs = Sum(_erAbsIncr);
            decimal ER = (sumAbs > 0 && bar >= ER_Lookback) ? Clamp(netChange / sumAbs, 0m, 1m) : 0m;
            _efficiency[b] = ER;

            // 8) MaxCounterDeltaShare (verbesserte CVD-basierte Berechnung mit Coherence-Gewichtung)
            const int CounterLookback = 5; // Anpassen nach Bedarf (z.B. 3-10 Kerzen)
            decimal maxCounterShareBull = 0m;
            decimal maxCounterShareBear = 0m;

            for (int i = 0; i < CounterLookback; i++)
            {
                int pastBar = b - i;
                if (pastBar < 0) continue;

                var pastC = GetCandle(pastBar);
                if (pastC == null) continue;

                // Volumen-Daten f?r einfache Berechnung
                decimal pastAskVol = pastC.Ask;
                decimal pastBidVol = pastC.Bid;
                decimal pastDelta = pastC.Delta;
                decimal pastCoh01 = _cvdCoherence.ContainsKey(pastBar) ? _cvdCoherence[pastBar] : 0.5m;

                decimal bullShare, bearShare;

                // PERFORMANCE-BOOST: Nur aktuelle Bar mit Footprint berechnen
                if (i == 0) // Aktuelle Bar (letzte Kerze)
                {
                    bullShare = CalculateCounterShareWithFootprint(pastBar, pastCoh01, true);
                    bearShare = CalculateCounterShareWithFootprint(pastBar, pastCoh01, false);
                }
                else // Historische Bars mit einfacher Methode
                {
                    bullShare = CalculateCounterShare(pastAskVol, pastBidVol, pastDelta, pastCoh01, true);
                    bearShare = CalculateCounterShare(pastAskVol, pastBidVol, pastDelta, pastCoh01, false);
                }

                // Debug-Logging bei hohen Werten
                if (bullShare > 0.5m || bearShare > 0.5m)
                {
                    //this.LogInfo($"[DEBUG] Bar {pastBar}: bullShare={bullShare:F4}, bearShare={bearShare:F4}, coh={pastCoh01:F2}");
                }

                maxCounterShareBull = Math.Max(maxCounterShareBull, bullShare);
                maxCounterShareBear = Math.Max(maxCounterShareBear, bearShare);
            }


            // Sicherheitspr?fung (sollte eigentlich nicht n?tig sein)
            maxCounterShareBull = Math.Min(maxCounterShareBull, 1.0m);
            maxCounterShareBear = Math.Min(maxCounterShareBear, 1.0m);

            // Speichere die Max-Werte
            _maxCounterShareBull[b] = maxCounterShareBull;
            _maxCounterShareBear[b] = maxCounterShareBear;

            // 9) ITT Z ? Tempo aus Candle-Fenster (z. B. 15 Sekunden)
            // itt Z (Inter-Trade-Time, robustified: EWMA-Mean + robust/blended Scale)
            totalTradesEffizient = c.Ticks; // decimal
            decimal secondsDec = (decimal)seconds; // cast von double -> decimal
            decimal ittMsApprox = (secondsDec * 1000m) / Math.Max(1m, totalTradesEffizient);

            // Z-Score robustified (object-first Signatur)
            double alpha = (double)EwmaAlpha;
            decimal ittZ = RobustifiedExponentialZScore(
            windowObj: _ittWin,
            alpha: (double)EwmaAlpha,
            x: ittMsApprox,
            useRobustScale: true,
            robustBlend: 0.8,
            maxAbsZ: 50.0,
            madEps: 1e-1
            );

            // Ergebnisse wie vorher speichern
            _ittZ_raw[bar] = ittZ;
            _ittZ_bull[bar] = -ittZ;
            _ittZ_bear[bar] = +ittZ;

            // Push ins Fenster (Lookback wie zuvor ? ggf. auf IttZ_Lookback anpassen)
            PushWindow(_ittWin, ittMsApprox, TradeRateZ_Lookback);


            // 10) Sweep
            bool sweepUp = DetectSweepFromClusters(bar, SweepSide.Up);
            bool sweepDn = DetectSweepFromClusters(bar, SweepSide.Down);

            _sweepUp[bar] = sweepUp;
            _sweepDn[bar] = sweepDn;
            _sweepDir[bar] = sweepUp ? 1 : (sweepDn ? -1 : 0);



            // 11) Stacked Imbalance (pro Bar-Level-Footprint, passend f?r Range-Bars 5?8 Ticks)
            var pImb = new StackedImbParams
            {
                ImbalanceRatioPct = _imbalanceRatioPct,
                ImbalanceVolumeMin = _imbalanceVolumeMin,
                IgnoreZeroValues = _imbIgnoreZeroValues,
                MaxDepthTicksAnchored = _imbMaxDepthTicksAnchored
            };
            var r = ComputeStackedImbalanceForBar(b, pImb);

            // Optional: Mindestl?nge global anwenden (falls gew?nscht)
            int buyMax = r.BuyCountMax >= _imbalanceRangeMin ? r.BuyCountMax : 0;
            int sellMax = r.SellCountMax >= _imbalanceRangeMin ? r.SellCountMax : 0;
            // Anchored-Counts meist ohne Mindestl?nge:
            int buyTop = r.BuyCountTopAnchored;
            int sellBottom = r.SellCountBottomAnchored;

            // Ergebnisse in die vorhandenen readonly-Serien schreiben (keine Neuzuweisung)
            _stackedBuyImbCount[b] = (decimal)buyMax;
            _stackedSellImbCount[b] = (decimal)sellMax;
            _stackedBuyImbTopCount[b] = (decimal)buyTop;
            _stackedSellImbBottomCount[b] = (decimal)sellBottom;
            if (_stackedBuyImbCount == null || _stackedSellImbCount == null || _stackedBuyImbTopCount == null || _stackedSellImbBottomCount == null)
            {
                this.LogWarn($"[OnCalculate-Imbalance] Serien null: buy={_stackedBuyImbCount == null}, sell={_stackedSellImbCount == null}, top={_stackedBuyImbTopCount == null}, bottom={_stackedSellImbBottomCount == null}");
            }



            _currentCandleData = c;


            bool stopTick = false; // statt fr?her returns

            // === Tickgenaue Armed-Entry-Ausl?sung (VOR dem Guard) ===
            if (_entryState == EntryState.TouchArmed && !HasLiveEntryOrder())
            {


                // Arm-Timeout relativ zum Arming-Bar
                if (_orderTimeoutEnabled && _orderTimeoutBars > 0 && _armedBarIndex >= 0)
                {
                    if (bar > (_armedBarIndex + _orderTimeoutBars))
                    {
                        this.LogInfo($"[OnCalculate-EntryArm] CANCEL by timeout: armedBar={_armedBarIndex} now={bar} max={_armedBarIndex + _orderTimeoutBars}");
                        CancelArmedEntry();
                    }
                }

                if (_entryState == EntryState.TouchArmed) // k?nnte durch Cancel ge?ndert worden sein
                {
                    decimal finalLongTrigger = ComputeFinalLongTrigger(_tickSize);
                    decimal finalShortTrigger = ComputeFinalShortTrigger(_tickSize);

                    // Preisquelle f?r Ausl?sepr?fung (Last; ggf. BestAsk/BestBid verwenden)
                    decimal triggerCheckPriceLong = Security?.BestAskPrice ?? 0m;
                    decimal triggerCheckPriceShort = Security?.BestBidPrice ?? 0m;

                    if (_entryIsLong && triggerCheckPriceLong >= finalLongTrigger)
                    {
                        PlaceEntry(OrderDirections.Buy, finalLongTrigger, bar);
                        _entryBarIndex = bar;
                        _entryState = EntryState.EntryPlaced;

                        // Overrides leeren, um Doppel-Trigger zu vermeiden
                        _longTriggerOverride = null;
                        _shortTriggerOverride = null;

                        stopTick = true; // nach Platzierung in diesem Tick keine heavy work mehr
                    }
                    else if (!_entryIsLong && triggerCheckPriceShort <= finalShortTrigger)
                    {
                        PlaceEntry(OrderDirections.Sell, finalShortTrigger, bar);
                        _entryBarIndex = bar;
                        _entryState = EntryState.EntryPlaced;

                        _longTriggerOverride = null;
                        _shortTriggerOverride = null;

                        stopTick = true;
                    }
                }
            }


            // Fr?her Guard: schweren Teil ggf. ?berspringen, aber NICHT returnen
            if (_lastProcessedBar == bar)
            {
                this.LogDebug($"[OnCalculate] SKIPPED heavy work: Already processed bar={bar}.");
                stopTick = true; // nur markieren
            }
            else
            {
                _lastProcessedBar = bar;

                // 1) Pending Exit zuerst aufl?sen
                if (_isExitPlacementPending)
                {
                    bool exitsExist =
                        _tpOrder != null && _slOrder != null &&
                        !IsFilledOrDone(_tpOrder) && !IsFilledOrDone(_slOrder);

                    if (exitsExist)
                    {
                        _isExitPlacementPending = false;

                        if (_positionOpen && !_managersInitialized)
                        {
                            try
                            {
                                InitializeManagersAfterEntry(_lastTpSlResult);
                                this.LogInfo("[OnCalculate] Pending cleared: TP/SL already placed (external). Managers initialized.");

                                if (EnableBreakEven)
                                {
                                    this.LogInfo("[OnCalculate] Calling ProcessBreakEvenTick() after pending clear.");
                                    try { ProcessBreakEvenTick(); }
                                    catch (Exception ex) { this.LogWarn($"[OnCalculate] ProcessBreakEvenTick Exception: {ex.Message}"); }
                                }
                            }
                            catch (Exception ex)
                            {
                                this.LogWarn($"[OnCalculate] InitializeManagersAfterEntry failed after pending-clear: {ex.Message}");
                                _managersInitialized = false;
                            }
                        }
                        else
                        {
                            this.LogInfo("[OnCalculate] Pending cleared: TP/SL already placed (external).");
                        }

                        stopTick = true; // statt return; wir beenden sp?ter
                    }
                    else
                    {
                        // Fallback-Platzierung
                        if (_entryFillPrice <= 0m)
                        {
                            var srcForPx = _marketOrder ?? _entryOrder ?? _pullbackOrder;
                            var pxTry = srcForPx != null ? TryGetFillPrice(srcForPx, Security) : 0m;
                            if (pxTry > 0m) _entryFillPrice = pxTry;
                        }

                        var srcOrder = _marketOrder ?? _entryOrder ?? _pullbackOrder;
                        var qty = 0m;
                        if (srcOrder != null && IsFilledOrDone(srcOrder))
                        {
                            qty = srcOrder.Filled();
                            if (qty <= 0m) qty = GetFilledQuantity(srcOrder);
                        }

                        if (_entryFillPrice > 0m && qty > 0m)
                        {
                            var candle = TryGetCandleAtOrBefore(bar) ?? _currentCandleData;
                            var levels = BuildLevelsSnapshot(_untouchedLevels);
                            if (_fillBarIndex < 0) _fillBarIndex = bar;
                            var dir = _isLongTrade ? OrderDirections.Buy : OrderDirections.Sell;

                            try
                            {
                                PlaceTpSlOrders(bar, _fillBarIndex, levels, candle, /*isPullback*/ false, _entryFillPrice, dir);
                                _isExitPlacementPending = false;
                                _managersInitialized = false;
                                this.LogInfo("[OnCalculate] TP/SL placed (pending resolved).");
                            }
                            catch (Exception ex)
                            {
                                this.LogWarn($"[OnCalculate] PlaceTpSlOrders FAILED: {ex.Message} ? retry next tick");
                            }
                        }

                        stopTick = true; // statt return
                    }
                }

                // 2) BestSinceEntry aktualisieren (falls Position offen)
                if (_positionOpen)
                {
                    var last = Security?.LastTradePrice ?? 0m;
                    if (last > 0m)
                    {
                        if (_isLongTrade) _bestSinceEntry = Math.Max(_bestSinceEntry, last);
                        else _bestSinceEntry = Math.Min(_bestSinceEntry, last);
                        this.LogInfo($"[OnCalculate] Tick-Update: Last={last}, BestSinceEntry={_bestSinceEntry} (Long={_isLongTrade})");
                    }
                }

                // 3) Manager-Init (Fallback) und 4) BreakEven-Tick
                if (_slOrder != null && _tpOrder != null && _positionOpen && !_managersInitialized && !_isExitPlacementPending)
                {
                    InitializeManagersAfterEntry(_lastTpSlResult);
                    this.LogInfo("[OnCalculate] Managers initialized in fallback.");
                    if (EnableBreakEven)
                    {
                        this.LogInfo("[OnCalculate] Calling ProcessBreakEvenTick() post-init.");
                        try { ProcessBreakEvenTick(); }
                        catch (Exception ex) { this.LogWarn($"[OnCalculate] ProcessBreakEvenTick Exception: {ex.Message}"); }
                    }
                }
                else if (_positionOpen && _managersInitialized && !_isExitPlacementPending && EnableBreakEven)
                {
                    this.LogInfo("[OnCalculate] Calling ProcessBreakEvenTick() per tick.");
                    try { ProcessBreakEvenTick(); }
                    catch (Exception ex) { this.LogWarn($"[OnCalculate] ProcessBreakEvenTick Exception: {ex.Message}"); }
                }

                // 5) Bar-Close-Trailing
                if (bar != _lastProcessedBarIndex)
                {
                    try
                    {
                        if (bar > 0)
                            ProcessTrailingOnBarClose(bar - 1);
                    }
                    catch (Exception ex)
                    {
                        this.LogWarn($"[OnCalculate-ProcessTrailingOnBarClose] Exception: {ex.Message}");
                    }

                    _lastProcessedBarIndex = bar;
                    _signalCheckedForThisBarFirstTick = false;
                }

                // 6) Timeout-Check
                if (bar != _lastTimeoutCheckBarIndex)
                {
                    _lastTimeoutCheckBarIndex = bar;

                    if (_pullbackOrder != null || _entryOrder != null)
                    {
                        if (_currentTimeoutOwnerSetup != SetupKind.None)
                        {
                            bool isTimeoutEnabled = _orderTimeoutEnabled;
                            int timeoutBars = _orderTimeoutBars;

                            if (isTimeoutEnabled && _entryBarIndex >= 0)
                            {
                                this.LogInfo($"[OnCalculate-TimeoutDiag INIT] enabled={_orderTimeoutEnabled} entryBarIndex={_entryBarIndex} timeoutBars={_orderTimeoutBars}");

                                int timeoutBarIndex = _entryBarIndex + timeoutBars;
                                if (bar >= timeoutBarIndex)
                                {
                                    bool anyOpen = false;

                                    if (_pullbackOrder != null)
                                    {
                                        var st = _pullbackOrder.Status();
                                        if (st != OrderStatus.Filled && st != OrderStatus.Canceled)
                                        {
                                            anyOpen = true;
                                            CancelOrder(_pullbackOrder);
                                        }
                                    }

                                    if (_entryOrder != null)
                                    {
                                        var st = _entryOrder.Status();
                                        if (st != OrderStatus.Filled && st != OrderStatus.Canceled)
                                        {
                                            anyOpen = true;
                                            this.LogWarn(
                                                $"[OnCalculate-TimeoutDiag] bar={bar} entryBarIndex={_entryBarIndex} timeoutBars={_orderTimeoutBars} " +
                                                $"timeoutBarIndex={_entryBarIndex + _orderTimeoutBars} nowMinusEntry={bar - _entryBarIndex} " +
                                                $"owner={_currentTimeoutOwnerSetup} entryOrderId={_entryOrder?.Id} entryStatus={_entryOrder?.Status()} " +
                                                $"pullOrderId={_pullbackOrder?.Id} pullStatus={_pullbackOrder?.Status()} " +
                                                $"thread='{System.Threading.Thread.CurrentThread.Name ?? ""}'"
                                            );
                                            CancelOrder(_entryOrder);
                                        }
                                    }

                                    if (anyOpen)
                                        this.LogWarn($"[OnCalculate-Timeout {_currentTimeoutOwnerSetup}] Bar={bar}, Timeout-Bar={timeoutBarIndex} ? Orders werden storniert.");

                                    var stPull = _pullbackOrder?.Status();
                                    var stEntry = _entryOrder?.Status();
                                    bool pullInactive = _pullbackOrder == null || stPull == OrderStatus.Filled || stPull == OrderStatus.Canceled;
                                    bool entryInactive = _entryOrder == null || stEntry == OrderStatus.Filled || stEntry == OrderStatus.Canceled;

                                    if (pullInactive && entryInactive)
                                    {
                                        _pullbackOrder = null;
                                        _entryOrder = null;
                                        _orderTimeoutEnabled = false;
                                        _orderTimeoutBars = 0;
                                        _entryBarIndex = -1;
                                        _currentTimeoutOwnerSetup = SetupKind.None;
                                    }
                                }
                            }
                        }
                    }
                }
            }



            // WICHTIG: Pr?fen, ob eine neue Handelssession beginnt (basierend auf Instrumenteneinstellungen)
            // Dies ist analog zur IsNewSession-Methode im DailyLines Indikator, wenn CustomSession ausgeschaltet ist.
            bool isNewSession = IsNewSession(bar);

            if (isNewSession)
            {
                // Eine neue Session hat begonnen
                // Die Werte des vorherigen Tages sind nun die abgeschlossenen Werte des _currentDay...
                if (_lastSessionStartBar != -1) // Nur wenn bereits eine Session abgeschlossen wurde
                {
                    _previousDayOpen = _currentDayOpen;
                    _previousDayHigh = _currentDayHigh;
                    _previousDayLow = _currentDayLow;
                    _previousDayClose = GetCandle(_lastSessionStartBar).Close; // Close der letzten Kerze der vorherigen Session

                    // Korrektur: Das Close des Vortages ist das Close des letzten Balkens des Vortages.
                    // ATAS-Indikatoren arbeiten oft mit dem Close des letzten Balkens VOR dem NewSession-Balken.
                    // Wenn der letzte Balken des Vortages der Balken (bar-1) war, dann ist dessen Close der korrekte Wert.
                    if (bar > 0)
                    {
                        _previousDayClose = GetCandle(bar - 1).Close;
                    }
                }

                // Initialisieren der Werte f?r den neuen Tag
                _currentDayOpen = c.Open;
                _currentDayHigh = c.High;
                _currentDayLow = c.Low;
                _currentDayClose = c.Close; // Aktualisiert sich weiter
                _lastSessionStartBar = bar; // Speichern des Startbalkens der aktuellen Session
            }
            else
            {
                // Innerhalb der aktuellen Session
                // Aktualisieren der aktuellen Tages-High und -Low
                _currentDayHigh = Math.Max(_currentDayHigh, c.High);
                _currentDayLow = Math.Min(_currentDayLow, c.Low);
                _currentDayClose = c.Close; // Das aktuelle Close ist immer das Close des aktuellen Balkens
            }




            // VAH, VAL, POC Werte abrufen ===
            // Abrufen der aktuellen Werte vom "_dailyLevels" Indikator.
            // Wir schauen auf den Wert der *aktuellen Kerze* (bar), da dieser sich dynamisch ?ndert.
            decimal currentPOC = 0m;
            decimal currentVAH = 0m;
            decimal currentVAL = 0m;

            // --- R?ckw?rtssuche f?r POC (DataSeries[0]) ---
            for (int i = bar; i >= 0; i--)
            {
                // ?berpr?fen, ob der Index f?r diese DataSeries g?ltig ist
                // (WICHTIG, da DataSeries.Count durch _targetBar begrenzt sein kann)
                if (_dailyLevels.DataSeries[0].Count > i)
                {
                    decimal val = (decimal)_dailyLevels.DataSeries[0][i];
                    if (val != 0m)
                    {
                        currentPOC = val;
                        break; // Letzten Nicht-Null-POC gefunden
                    }
                }
            }
            //this.LogInfo($"[DEBUG] Bar {bar}: R?ckw?rtssuche POC ergibt: {currentPOC}");
            // --- R?ckw?rtssuche f?r VAH (DataSeries[2]) ---
            for (int i = bar; i >= 0; i--)
            {
                if (_dailyLevels.DataSeries[2].Count > i)
                {
                    decimal val = (decimal)_dailyLevels.DataSeries[2][i];
                    if (val != 0m)
                    {
                        currentVAH = val;
                        break; // Letzten Nicht-Null-VAH gefunden
                    }
                }
            }
            //this.LogInfo($"[DEBUG] Bar {bar}: R?ckw?rtssuche VAH ergibt: {currentVAH}");
            // --- R?ckw?rtssuche f?r VAL (DataSeries[3]) ---
            for (int i = bar; i >= 0; i--)
            {
                if (_dailyLevels.DataSeries[3].Count > i)
                {
                    decimal val = (decimal)_dailyLevels.DataSeries[3][i];
                    if (val != 0m)
                    {
                        currentVAL = val;
                        break; // Letzten Nicht-Null-VAL gefunden
                    }
                }
            }
            //this.LogInfo($"[DEBUG] Bar {bar}: R?ckw?rtssuche VAL ergibt: {currentVAL}");



            // === SCHRITT 2: Den Beginn eines neuen Tages KORREKT erkennen ===
            // Wir vergleichen den reinen Datumsteil (ohne Uhrzeit) der aktuellen und der vorherigen Kerze.
            if (c.Time.Date != p.Time.Date)
            {

                // Micro-Composite zur?cksetzen (neuer Tageskontext)
                _mcHist.Clear();
                _mcBars.Clear();


                // Berechne VAH, VAL, POC des Vortages manuell
                if (bar > 0)
                {
                    // Die GetPreviousDayVolumeProfile Methode gibt ein Tuple zur?ck
                    (_pdPOC, _pdVAH, _pdVAL) = _volumeProfileGenerator.GetPreviousDayVolumeProfile(bar - 1);
                }
                else
                {
                    // Wenn es keine vorherige Bar gibt (z.B. am Anfang des Charts bei bar = 0),
                    // sollten die Werte zur?ckgesetzt werden.
                    _pdPOC = 0m;
                    _pdVAH = 0m;
                    _pdVAL = 0m;
                }

                bool hasPreviousDayProfile = (_pdPOC != 0m && _pdVAH != 0m && _pdVAL != 0m);

                if (EnableIsBlocked)
                {



                    // Entferne alte Previous Day Levels (PdPOC, PdVAH, PdVAL)
                    _untouchedLevels.RemoveAll(l => l.Label == "POC gestern" || l.Label == "VAH gestern" || l.Label == "VAL gestern" ||
                    l.Label == "Tageshoch gestern" || l.Label == "Tagestief gestern" || l.Label == "Schlusskurs gestern" || l.Label == "Er?ffnungskurs gestern");



                    // Entferne Levels, die am Ende des Tages ablaufen
                    _untouchedLevels.RemoveAll(l => l.IsActive && l.RemovalCondition == TrackedLevel.LevelRemovalCondition.EndOfDay);

                    // Entferne Levels, die ?lter als MaxDaysForSignificantLevels sind
                    _untouchedLevels.RemoveAll(l => l.IsActive && l.RemovalCondition == TrackedLevel.LevelRemovalCondition.AfterMaxDays && (c.Time.Date - l.LevelDate).TotalDays > MaxDaysForSignificantLevels);

                    // F?ge die neuen Previous Day Levels hinzu (nur einmal pro neuem Tag)
                    _untouchedLevels.Add(new TrackedLevel(_previousDayHigh, _lastProcessedDay, "Tageshoch gestern", TrackedLevel.LevelRemovalCondition.EndOfDay));
                    _untouchedLevels.Add(new TrackedLevel(_previousDayLow, _lastProcessedDay, "Tagestief gestern", TrackedLevel.LevelRemovalCondition.EndOfDay));
                    _untouchedLevels.Add(new TrackedLevel(_previousDayClose, _lastProcessedDay, "Schlusskurs gestern", TrackedLevel.LevelRemovalCondition.EndOfDay));
                    _untouchedLevels.Add(new TrackedLevel(_previousDayOpen, _lastProcessedDay, "Er?ffnungskurs gestern", TrackedLevel.LevelRemovalCondition.EndOfDay));
                    // F?ge die profilbasierten Levels nur hinzu, wenn sie g?ltig sind


                    // F?ge die profilbasierten Levels nur hinzu, wenn sie g?ltig sind

                    if (hasPreviousDayProfile)
                    {
                        _untouchedLevels.Add(new TrackedLevel(_pdPOC, _lastProcessedDay, "POC gestern", TrackedLevel.LevelRemovalCondition.EndOfDay));
                        _untouchedLevels.Add(new TrackedLevel(_pdVAH, _lastProcessedDay, "VAH gestern", TrackedLevel.LevelRemovalCondition.EndOfDay));
                        _untouchedLevels.Add(new TrackedLevel(_pdVAL, _lastProcessedDay, "VAL gestern", TrackedLevel.LevelRemovalCondition.EndOfDay));
                    }
                }






                // 2. Die Arbeitsvariablen f?r den neuen Tag zur?cksetzen.
                _currentDayHigh = c.High;
                _currentDayLow = c.Low;
                _currentDayOpen = c.Open;

            }
            else
            {
                // === Wir sind noch am selben Tag ===
                // Wir aktualisieren laufend das Hoch und Tief des aktuellen Tages.
                _currentDayHigh = Math.Max(_currentDayHigh, c.High);
                _currentDayLow = Math.Min(_currentDayLow, c.Low);
                _currentDayClose = c.Close;
            }






            ///Die Pivot-Werte abrufen ===
            // Wir greifen auf die DataSeries des Indikators zu, um die Werte zu bekommen.
            // .Last() gibt uns den Wert f?r die aktuelle Kerze.
            decimal pp = (decimal)_pivots.DataSeries[0][bar];
            decimal s1 = (decimal)_pivots.DataSeries[1][bar];
            decimal s2 = (decimal)_pivots.DataSeries[2][bar];
            decimal s3 = (decimal)_pivots.DataSeries[3][bar];
            decimal r1 = (decimal)_pivots.DataSeries[4][bar];
            decimal r2 = (decimal)_pivots.DataSeries[5][bar];
            decimal r3 = (decimal)_pivots.DataSeries[6][bar];
            decimal m1 = (decimal)_pivots.DataSeries[7][bar];
            decimal m2 = (decimal)_pivots.DataSeries[8][bar];
            decimal m3 = (decimal)_pivots.DataSeries[9][bar];
            decimal m4 = (decimal)_pivots.DataSeries[10][bar];

            // Berechnen Sie die runden Zahlen direkt mit dem Punkt-Schritt ===
            var currentPrice = c.Close;
            decimal levelBelow = Math.Floor(currentPrice / PointStep) * PointStep;
            decimal levelAbove = levelBelow + PointStep;

            // *** HINZUGEF?GT: Dynamische Level (Current POC, Runde Zahlen) zum Zeichnen hinzuf?gen/aktualisieren ***
            if (EnableIsBlocked)
            {
                // 1. Alte dynamische Levels f?r Current POC und Runde Zahlen von der vorherigen Kerze entfernen (jeden Bar, da sie dynamisch sind).
                _untouchedLevels.RemoveAll(l => l.Label == "POC aktuell" || l.Label == "VAH aktuell" || l.Label == "VAL aktuell" || l.Label == "runde Marke");

                // 2. Die neuen, aktuellen dynamischen Levels hinzuf?gen (jeden Bar aktualisieren).
                _untouchedLevels.Add(new TrackedLevel(currentVAH, c.Time.Date, "VAH aktuell", TrackedLevel.LevelRemovalCondition.EndOfDay));
                _untouchedLevels.Add(new TrackedLevel(currentVAL, c.Time.Date, "VAL aktuell", TrackedLevel.LevelRemovalCondition.EndOfDay));
                _untouchedLevels.Add(new TrackedLevel(currentPOC, c.Time.Date, "POC aktuell", TrackedLevel.LevelRemovalCondition.EndOfDay));
                _untouchedLevels.Add(new TrackedLevel(levelAbove, c.Time.Date, "runde Marke", TrackedLevel.LevelRemovalCondition.EndOfDay));
                _untouchedLevels.Add(new TrackedLevel(levelBelow, c.Time.Date, "runde Marke", TrackedLevel.LevelRemovalCondition.EndOfDay));


                // Pivots nur einmal pro Tag hinzuf?gen, wenn noch nicht geschehen
                if (c.Time.Date != _lastPivotLevelAddDay)
                {
                    if (pp > 0)
                    {
                        _untouchedLevels.Add(new TrackedLevel(pp, c.Time.Date, "PP", TrackedLevel.LevelRemovalCondition.EndOfDay));
                        _untouchedLevels.Add(new TrackedLevel(s1, c.Time.Date, "S1", TrackedLevel.LevelRemovalCondition.EndOfDay));
                        _untouchedLevels.Add(new TrackedLevel(s2, c.Time.Date, "S2", TrackedLevel.LevelRemovalCondition.EndOfDay));
                        _untouchedLevels.Add(new TrackedLevel(s3, c.Time.Date, "S3", TrackedLevel.LevelRemovalCondition.EndOfDay));
                        _untouchedLevels.Add(new TrackedLevel(r1, c.Time.Date, "R1", TrackedLevel.LevelRemovalCondition.EndOfDay));
                        _untouchedLevels.Add(new TrackedLevel(r2, c.Time.Date, "R2", TrackedLevel.LevelRemovalCondition.EndOfDay));
                        _untouchedLevels.Add(new TrackedLevel(r3, c.Time.Date, "R3", TrackedLevel.LevelRemovalCondition.EndOfDay));
                        _untouchedLevels.Add(new TrackedLevel(m1, c.Time.Date, "M1", TrackedLevel.LevelRemovalCondition.EndOfDay));
                        _untouchedLevels.Add(new TrackedLevel(m2, c.Time.Date, "M2", TrackedLevel.LevelRemovalCondition.EndOfDay));
                        _untouchedLevels.Add(new TrackedLevel(m3, c.Time.Date, "M3", TrackedLevel.LevelRemovalCondition.EndOfDay));
                        _untouchedLevels.Add(new TrackedLevel(m4, c.Time.Date, "M4", TrackedLevel.LevelRemovalCondition.EndOfDay));

                        _lastPivotLevelAddDay = c.Time.Date; // Merken, dass f?r diesen Tag hinzugef?gt wurde
                    }
                }
            }

            // --- NEU: Erstellen der Liste aller signifikanten Levels f?r CheckEntrySignal ---
            //List<TrackedLevel> allSignificantLevels = new List<TrackedLevel>();
            // Pivots hinzuf?gen (nur wenn sie berechnet wurden)







            if (EnableIsBlocked)
            {

                decimal previousClose = p.Close;

                // Durchlaufen Sie die Liste r?ckw?rts, um sicheres Entfernen zu erm?glichen,
                // falls Sie sp?ter entscheiden, abarbeitete Levels tats?chlich zu entfernen statt nur zu markieren.
                for (int i = _untouchedLevels.Count - 1; i >= 0; i--)
                {
                    var level = _untouchedLevels[i];
                    if (level.IsActive) // Nur aktive Levels pr?fen
                    {
                        bool isTouchCooldownOver = (level.LastTouchBarIndex == -1) || (bar - level.LastTouchBarIndex >= MinCandleSeparationForTouches);

                        if (isTouchCooldownOver)
                        {

                            bool touched = false;
                            string touchType = string.Empty;
                            // NEU: Kontextbasierte Ber?hrungspr?fung
                            // Als Unterst?tzung (von oben kommend): Preis f?llt auf das Level und prallt ab
                            if (c.Low <= level.Value && previousClose > level.Value)
                            {
                                touched = true;
                                touchType = "Unterst?tzung";
                                level.IncrementSupportTouch(); // Neu: Support-Z?hler erh?hen
                                this.LogDebug($"[OnCalculate-Markante Levels] Level '{level.Label}' ({level.Value}) vom {level.LevelDate.ToShortDateString()} ber?hrt als {touchType} bei Bar {bar} (Akt. Low: {c.Low}, Prev. Close: {previousClose}). Support-Touches: {level.SupportTouchCount}");
                            }
                            // Als Widerstand (von unten kommend): Preis steigt auf das Level und prallt ab
                            else if (c.High >= level.Value && previousClose < level.Value)
                            {
                                touched = true;
                                touchType = "Widerstand";
                                level.IncrementResistanceTouch(); // Neu: Resistance-Z?hler erh?hen
                                this.LogDebug($"[OnCalculate-Markante Levels] Level '{level.Label}' ({level.Value}) vom {level.LevelDate.ToShortDateString()} ber?hrt als {touchType} bei Bar {bar} (Akt. High: {c.High}, Prev. Close: {previousClose}). Resistance-Touches: {level.ResistanceTouchCount}");
                            }

                            if (touched)
                            {
                                // === KORREKTUR: Speichere den Bar-Index der aktuellen Ber?hrung ===
                                level.LastTouchBarIndex = bar;

                                // Entferne/markiere nur Levels mit spezifischen RemovalConditions als abgearbeitet
                                if (level.RemovalCondition == TrackedLevel.LevelRemovalCondition.OnTouch)
                                {
                                    level.MarkAsWorkedOff();
                                    this.LogDebug($"[OnCalculate-Markante Levels] Level '{level.Label}' ({level.Value}) abgearbeitet (OnTouch als {touchType}).");
                                }
                                else if (level.RemovalCondition == TrackedLevel.LevelRemovalCondition.AfterMultipleTouches &&
                                         (level.SupportTouchCount >= 3 || level.ResistanceTouchCount >= 3)) // Beispiel: 3 Ber?hrungen pro Typ
                                {
                                    level.MarkAsWorkedOff();
                                    this.LogDebug($"[OnCalculate-Markante Levels] Level '{level.Label}' ({level.Value}) abgearbeitet (AfterMultipleTouches, Support: {level.SupportTouchCount}, Resistance: {level.ResistanceTouchCount}).");
                                }
                                // Levels mit EndOfDay oder AfterMaxDays werden prim?r durch Zeitbedingungen entfernt,
                                // k?nnen aber auch durch Preisaktion als 'worked off' markiert werden, wenn die Logik dies erfordert.
                                // F?r die aktuelle Anforderung markieren wir sie nur bei den oben genannten Bedingungen als 'worked off'.
                            }
                        }
                    }
                }
                // Nach der ?berpr?fung alle als inaktiv markierten Levels aus der Liste entfernen
                _untouchedLevels.RemoveAll(l => !l.IsActive);
            }
            // 1) Signifikante Levels bestimmen (lokale Variable, readonly Feld bleibt unber?hrt)



            // 2) Snapshot aus den gefilterten Levels bauen
            _currentLevelsSnapshot = BuildLevelsSnapshot(_untouchedLevels);

            if (stopTick) return;

            // 3) Entry-Blocker berechnen
            var entryProxPx = ProximityTicksForEntry * _tickSize;
            var (isBlockedLong, blockRes) = IsCloseToResistance(p.Close, _untouchedLevels, entryProxPx);
            var (isBlockedShort, blockSup) = IsCloseToSupport(p.Close, _untouchedLevels, entryProxPx);

            // 4) Flags ins Snapshot schreiben
            _currentLevelsSnapshot.IsBlockedLong = isBlockedLong;
            _currentLevelsSnapshot.IsBlockedShort = isBlockedShort;

            if (isBlockedLong && !_currentLevelsSnapshot.LastBlockResistance.HasValue && blockRes != null && blockRes.Value > 0m)
                _currentLevelsSnapshot.LastBlockResistance = blockRes.Value;
            if (isBlockedShort && !_currentLevelsSnapshot.LastBlockSupport.HasValue && blockSup != null && blockSup.Value > 0m)
                _currentLevelsSnapshot.LastBlockSupport = blockSup.Value;

            // optional f?r Logging
            _currentLevelsSnapshot.Extra["IsBlockedLong"] = isBlockedLong ? 1m : 0m;
            _currentLevelsSnapshot.Extra["IsBlockedShort"] = isBlockedShort ? 1m : 0m;


            // Guards
            if (bar < 0 || _vwap == null) return;

            // Update deiner Custom-Serie (Haupt-VWAP)
            _vwapSeries[bar] = ReadVwapSeriesSafely(0, bar);

            // Aktueller Snapshot (Current Bar)
            bool replayMode = true;

            // === Snapshot-Erstellung ===
            if (isNewSession)
            {
                _prevSessionSnapshot = _currentVwapSnapshot?.Clone() ?? new VwapSnapshot();

                // Prev-Snapshot validieren (mit Replay-Flag)
                _prevSessionSnapshot.Validate(allowZeroForReplay: replayMode);

                //this.LogInfo($"[VWAP Log] New Session at Bar={bar} ? Full Prev Snapshot saved (Valid={_prevSessionSnapshot.IsValid})");
            }

            // === Current Snapshot bauen ===
            _currentVwapSnapshot = new VwapSnapshot
            {
                Current = ReadVwapSeriesSafely(0, bar),

                UpperBand1 = ReadVwapSeriesSafely(6, bar), // Upper Std1
                LowerBand1 = ReadVwapSeriesSafely(5, bar), // Lower Std1

                UpperBand2 = ReadVwapSeriesSafely(4, bar), // Upper Std2
                LowerBand2 = ReadVwapSeriesSafely(3, bar), // Lower Std2

                UpperBand3 = ReadVwapSeriesSafely(2, bar), // Upper Std3
                LowerBand3 = ReadVwapSeriesSafely(1, bar), // Lower Std3

                // Full Prev-Set (erweitert)
                PreviousDayCurrent = _prevSessionSnapshot?.Current ?? 0m,
                PreviousDayUpperBand1 = _prevSessionSnapshot?.UpperBand1 ?? 0m,
                PreviousDayLowerBand1 = _prevSessionSnapshot?.LowerBand1 ?? 0m,
                PreviousDayUpperBand2 = _prevSessionSnapshot?.UpperBand2 ?? 0m,
                PreviousDayLowerBand2 = _prevSessionSnapshot?.LowerBand2 ?? 0m,
                PreviousDayUpperBand3 = _prevSessionSnapshot?.UpperBand3 ?? 0m,
                PreviousDayLowerBand3 = _prevSessionSnapshot?.LowerBand3 ?? 0m
            };

            // === Validate (einmalig) ===
            _currentVwapSnapshot.Validate(allowZeroForReplay: replayMode);

            // === DIRECT DEBUG + SNAPSHOT LOG + SCAN (nur bei %10 oder NewSession) ===
            if (bar % 10 == 0 || isNewSession)
            {
                var candle = GetCandle(bar);
                if (candle != null)
                {
                    // Variablen
                    var candleclose = candle.Close;
                    decimal volume = GetVolume(bar);
                    decimal deltaVwap = _currentVwapSnapshot.Current - candleclose;

                    // Safe Direct Read (gleiche Guarded-Quelle wie Snapshot)
                    decimal directVwap = 0m;
                    try
                    {
                        directVwap = ReadVwapSeriesSafely(0, bar); // Serie 0 = Current
                    }
                    catch (Exception ex)
                    {
                        this.LogError($"[VWAP Direct ERROR] Safe read failed at bar={bar}: {ex.Message}");
                    }

                    // Optional: Rohwert (diagnostisch) mit Guards
                    decimal rawVwap = 0m;
                    int dsCount = _vwap?.DataSeries?.Count ?? 0;
                    if (dsCount > 0)
                    {
                        var ds0 = _vwap.DataSeries[0];
                        if (ds0 != null && bar >= 0 && bar < ds0.Count)
                        {
                            var obj = ds0[bar];
                            if (obj != null)
                            {
                                try { rawVwap = Convert.ToDecimal(obj); } catch { /* ignore conversion errors */ }
                            }
                        }
                    }

                    // Direct-Logs
                    //this.LogInfo($"[VWAP Direct DEBUG] Bar={bar}, Time={candle.Time:HH:mm}, Close={candleclose:F4}, Volume={volume:F0}, SafeDirect={directVwap:F4}, RawDS0={rawVwap:F4} (RawZero? {rawVwap == 0m})");
                    //this.LogInfo($"[VWAP Direct DEBUG] DataSeries.Count={dsCount} (erwarte ~7+, aber 21=OK), IsNewSession={isNewSession}");

                    // Series-Scan mit Count-Guards
                    // Vor dem Scan: dynamische Toleranzen
                    decimal width1 = Math.Abs(_currentVwapSnapshot.UpperBand1 - _currentVwapSnapshot.LowerBand1);
                    decimal currentTolerance = 3.0m; // ~12 Ticks (ES)
                    decimal bandScanTolerance = Math.Min(12.0m, Math.Max(6.0m, width1));

                    // Optional: ATR-gest?tzt (falls atr14 verf?gbar)
                    // decimal atrAdj = 0.2m * atr14;
                    // bandScanTolerance = Math.Min(12.0m, Math.Max(6.0m, Math.Max(width1, atrAdj)));

                    // Series-Scan
                    if (bar % 10 == 0 && dsCount > 0)
                    {
                        //this.LogInfo("[VWAP Series SCAN] Checking all Series[0-20] for non-zero values at bar=" + bar);
                        for (int i = 0; i < Math.Min(21, dsCount); i++)
                        {
                            var dsi = _vwap.DataSeries[i];
                            if (dsi == null || bar < 0 || bar >= dsi.Count) continue;

                            var obj = dsi[bar];
                            if (obj == null) continue;

                            decimal val;
                            try { val = Convert.ToDecimal(obj); } catch { continue; }

                            decimal tol = (i == 0) ? currentTolerance : bandScanTolerance;

                            if (val > 0m && Math.Abs(val - candleclose) <= tol) ;
                            //this.LogInfo($"[VWAP Series SCAN HIT] Series[{i}]={val:F4} (nahe Close={candleclose:F2}, tol={tol:F2})");
                            else if (val > 0m) ;
                            //this.LogDebug($"[VWAP Series SCAN] Series[{i}]={val:F4} (weit von Close, tol={tol:F2})");
                        }
                    }


                    // Vergleich nur mit dem sicheren Wert (wenn > 0)
                    if (_currentVwapSnapshot != null && directVwap > 0m)
                    {
                        bool match = Math.Abs(directVwap - _currentVwapSnapshot.Current) < 0.0001m;
                        //this.LogInfo($"[VWAP Direct DEBUG] SafeDirect vs Snapshot.Current: {directVwap:F4} == {_currentVwapSnapshot.Current:F4}? {match} (Diff={directVwap - _currentVwapSnapshot.Current:+0.0000;-0.0000;0}) | Snapshot.IsValid={_currentVwapSnapshot.IsValid}");
                    }

                    // Snapshot-Log
                    //this.LogInfo($"[VWAP Log] Bar={bar}, Time={candle.Time:HH:mm}, Close={candleclose:F2} | VWAP Current={_currentVwapSnapshot.Current:F2} (Delta={deltaVwap:+0.00;-0.00;0}) | {_currentVwapSnapshot}");

                    // Optional: Warn bei Invalid (au?er Replay)
                    if (!_currentVwapSnapshot.IsValid && !replayMode)
                        this.LogWarn($"[OnCalculate-VWAP Log WARN] Invalid Snapshot at bar={bar} ? Skipping signals? Check VWAP-Indikator.");
                }
            }

            // Previous Bar Snapshot (f?r Direction-Checks, z.B. Bullish/Bearish)
            _prevVwapSnapshot = null;
            if (bar > 0)
            {
                _prevVwapSnapshot = new VwapSnapshot
                {
                    Current = ReadVwapSeriesSafely(0, bar - 1),
                    UpperBand1 = ReadVwapSeriesSafely(6, bar - 1),
                    LowerBand1 = ReadVwapSeriesSafely(5, bar - 1),
                    UpperBand2 = ReadVwapSeriesSafely(4, bar - 1),
                    LowerBand2 = ReadVwapSeriesSafely(3, bar - 1),
                    UpperBand3 = ReadVwapSeriesSafely(2, bar - 1),
                    LowerBand3 = ReadVwapSeriesSafely(1, bar - 1),

                    PreviousDayCurrent = _prevSessionSnapshot?.Current ?? 0m,
                    PreviousDayUpperBand1 = _prevSessionSnapshot?.UpperBand1 ?? 0m,
                    PreviousDayLowerBand1 = _prevSessionSnapshot?.LowerBand1 ?? 0m,
                    PreviousDayUpperBand2 = _prevSessionSnapshot?.UpperBand2 ?? 0m,
                    PreviousDayLowerBand2 = _prevSessionSnapshot?.LowerBand2 ?? 0m,
                    PreviousDayUpperBand3 = _prevSessionSnapshot?.UpperBand3 ?? 0m,
                    PreviousDayLowerBand3 = _prevSessionSnapshot?.LowerBand3 ?? 0m
                };
            }





            //LogVpsSnapshot(bar);



            // Fr?h raus, wenn global gelockt
            if (_entryLogicLockedUntilLive)
            {
                this.LogInfo($"[OnCalculate] lockedUntilLive=true -> skip at bar={bar}");
                return;
            }

            bool isNewBar = (bar != _lastSeenBar);

            if (isNewBar)
            {
                int closed = bar - 1;      // der vorherige Bar ist jetzt geschlossen
                //this.LogInfo($"[OC] OnCalculate NEW BAR start: current={bar}, closed={closed}");
                //this.LogInfo($"--- BAR START (intern: {bar}, display: {bar + 1}) ---");

                // ========== PHASE 1: VALIDATIONS ==========
                //this.LogInfo($"[isNewBar check] bar={bar} isNewBar={(bar != _lastSeenBar)} beforeLastSeen={_lastSeenBar}");
                _lastSeenBar = bar;
                if (isNewBar)
                {
                    _lastSeenBar = bar;
                    //this.LogInfo($"[isNewBar true] _lastSeenBar set to {bar}"); }

                    if (closed < 0)
                    {
                        this.LogInfo($"[OnCalculate] closed would be {closed} -> skip");
                        return;
                    }

                    var closedCandle = GetCandle(closed);
                    if (_myClusterStatistic == null)
                    {
                        this.LogInfo($"[OnCalculate] MyClusterStatistic Instanz ist NULL f?r Bar {bar}. ?berspringe.");
                        return;
                    }
                    if (_myClusterStatistic.VolPerSecond == null)
                    {
                        this.LogInfo($"[OnCalculate] MyClusterStatistic.VolPerSecond Serie ist NULL f?r Bar {bar}. Dies deutet auf ein Problem im Indikator-Konstruktor hin. ?berspringe.");
                        return;
                    }

                    // --- ENTSCHEIDENDER SYNCHRONISATIONS-CHECK ---
                    // Wir verlassen uns direkt auf die 'Count'-Eigenschaft unserer Output-Serie.
                    // Wenn wir Daten f?r 'closed' ben?tigen (z.B. bar 2635), dann muss die VolPerSecond-Serie
                    // mindestens 2636 Elemente (Indizes 0 bis 2635) haben.
                    if (_myClusterStatistic.VolPerSecond.Count <= closed)
                    {
                        //this.LogInfo($"[OC] MyClusterStatistic's VolPerSecond.Count ist {_myClusterStatistic.VolPerSecond.Count}, ben?tigt mindestens {closed + 1} f?r geschlossenen Bar {closed}. " + $"DataSeries[0].Count (aktuell) ist {_myClusterStatistic.DataSeries[0].Count}. ?berspringe Bewertung.");
                        return;
                    }

                    // Wenn dieser Punkt erreicht wird, ist VolPerSecond.Count > closed.
                    // Das bedeutet, dass VolPerSecond[closed] ein g?ltiger Index ist.
                    // Der "unerwartet"-Log von vorhin ist hier nicht mehr n?tig, da wir uns auf VolPerSecond.Count verlassen.
                    // Sie k?nnen ihn optional als Warnung beibehalten, um die Inkonsistenz von DataSeries[0].Count zu protokollieren.
                    if (_myClusterStatistic.DataSeries[0].Count <= closed)
                    {
                        this.LogWarn($"[OnCalculate WARN - Discrepancy] Indikator's DataSeries[0].Count ist {_myClusterStatistic.DataSeries[0].Count}, welches unzureichend f?r geschlossenen Bar {closed} ist. " + $"ABER, VolPerSecond.Count ist {_myClusterStatistic.VolPerSecond.Count}, welches ausreicht. Fahre mit der Auswertung fort.");
                    }

                    // ========== PHASE 2: SNAPSHOT & HISTORY ==========

                    // Diagnose: welcher Z-Bar ist der letzte geschriebene?
                    int latestZBar = (_volBurstZ != null && _volBurstZ.Count > 0) ? _volBurstZ.Keys.Max() : -1;
                    this.LogInfo($"[OnCalculate] newBar: current={bar}, closed={closed}, lastEval={_lastEvalBar}, latestZBar={latestZBar}");

                    if (_lastEvalBar == closed)
                    {
                        this.LogInfo($"[OnCalculate] debounce: already evaluated closed={closed}");
                        return;
                    }

                    if (!HasKey(_volBurstZ, closed))
                    {
                        this.LogInfo($"[OnCalculate] postpone: VolZ for closed={closed} not ready (latestZBar={latestZBar})");
                        return;
                    }
                    this.LogDebug($"[OnCalculate-TRIGGER] EvaluateSignalsAndOrders(closed={closed}, time={GetCandle(closed).Time:O})");


                    this.LogDebug($"[OnCalculate] Reading closed bar values: closed={closed} ; VolZ present={HasKey(_volBurstZ, closed)} ; VolPerSecond[{closed}] exists={_myClusterStatistic.VolPerSecond.Count > closed}");

                    // WERTE DER ABGESCHLOSSENEN KERZE LESEN
                    decimal maxBull = GetOr0(_maxCounterShareBull, closed);
                    decimal maxBear = GetOr0(_maxCounterShareBear, closed);
                    decimal Volumenburst = GetOr0(_volBurstZ, closed);
                    decimal CVDImpuls = GetOr0(_cvdImpulse, closed);
                    decimal CVDCoherence = GetOr0(_cvdCoherence, closed);
                    decimal DruckAnteil = GetOr0(_aggPressure, closed);
                    decimal TradeRate = GetOr0(_tradeRateZ, closed);
                    decimal Effizienz = GetOr0(_efficiency, closed);

                    // Trades der abgeschlossenen Kerze
                    decimal buyTradesClosed = GetOr0(_buyTradesSeries, closed);
                    decimal sellTradesClosed = GetOr0(_sellTradesSeries, closed);
                    decimal totalTradesClosed = buyTradesClosed + sellTradesClosed;
                    decimal IttZ = GetOr0(_ittZ_raw, closed);
                    bool sweepUpClosed = GetOrFalse(_sweepUp, closed);
                    bool sweepDnClosed = GetOrFalse(_sweepDn, closed);
                    decimal stackedBuyCount = GetOr0(_stackedBuyImbCount, closed);
                    decimal stackedSellCount = GetOr0(_stackedSellImbCount, closed);
                    decimal stackedBuyTopCount = GetOr0(_stackedBuyImbTopCount, closed);
                    decimal stackedSellBottomCount = GetOr0(_stackedSellImbBottomCount, closed);

                    // HINZUGEF?GT: Werte aus MyClusterStatistic (angenommen, sie sind als Series verf?gbar)
                    decimal candleDuration = _myClusterStatistic.CandleDurations[closed];
                    decimal volPerSecond = _myClusterStatistic.VolPerSecond[closed];
                    decimal emaVolPerSecond = _myClusterStatistic.EmaVolPerSecond[closed];
                    decimal emaVolPerSecondStd = _myClusterStatistic.EmaVolPerSecondStd[closed];
                    decimal cumulativeDelta = _myClusterStatistic.CumulativeDelta[closed];
                    decimal cumulativeVolume = _myClusterStatistic.CumulativeVolume[closed];
                    decimal barDeltaPerVolume = _myClusterStatistic.BarDeltaPerVolume[closed];
                    // Stacked imbalance Zusatzfelder: stelle sicher, dass _imbalanceRatioPct decimal ist
                    decimal stackedImbRatioPct = _imbalanceRatioPct; // bereits decimal, wie gew?nscht


                    this.LogDebug($"[OnCalculate SNAP] Bar={closed} Time={closedCandle.Time:O} High={closedCandle.High:F2} Low={closedCandle.Low:F2} Close={closedCandle.Close:F2} " +
                        $"Volume={closedCandle.Volume:F2} Delta={closedCandle.Delta:F2} VolBurstZ={Volumenburst:F4} CvdImpulse={CVDImpuls:F4} AggPressure={DruckAnteil:F4} TradeRateZ={TradeRate:F4}");

                    //this.LogInfo($"[OC] call Evaluate: current={bar}, closed={closed}, VolZ={Volumenburst:F6}");
                    // Snapshot aktualisieren 
                    ovSnapshot = new OvSnapshot
                    {
                        Bar = closed,
                        Open = closedCandle.Open,
                        High = closedCandle.High,
                        Low = closedCandle.Low,
                        Time = closedCandle.Time,
                        Close = closedCandle.Close,
                        Volume = closedCandle.Volume,
                        Delta = closedCandle.Delta,
                        Ask = closedCandle.Ask,
                        Bid = closedCandle.Bid,
                        MaxCounterShareBull = maxBull,
                        MaxCounterShareBear = maxBear,
                        VolBurstZ = Volumenburst,
                        CvdImpulse = CVDImpuls,
                        CvdCoherence = CVDCoherence,
                        AggPressure = DruckAnteil,
                        TradeRateZ = TradeRate,
                        Efficiency = Effizienz,
                        // KORREKTUR: Verwende die 'Closed'-Variablen hier
                        BuyTrades = buyTradesClosed,
                        SellTrades = sellTradesClosed,
                        TotalTrades = totalTradesClosed,
                        IttZ = IttZ,
                        SweepUpClosed = sweepUpClosed,
                        SweepDnClosed = sweepDnClosed,
                        // HINZUGEF?GT: MyClusterStatistic Werte
                        CandleDuration = candleDuration,
                        VolPerSecond = volPerSecond,
                        EmaVolPerSecond = emaVolPerSecond,
                        EmaVolPerSecondStd = emaVolPerSecondStd,
                        CumulativeDelta = cumulativeDelta,
                        CumulativeVolume = cumulativeVolume,
                        BarDeltaPerVolume = barDeltaPerVolume,
                        // NEU: Stacked Imbalance
                        StackedBuyImbCount = (int)stackedBuyCount,
                        StackedSellImbCount = (int)stackedSellCount,
                        StackedBuyImbTopCount = (int)stackedBuyTopCount,
                        StackedSellImbBottomCount = (int)stackedSellBottomCount,
                        StackedImbMinVolPerLevel = _imbalanceVolumeMin,
                        StackedImbRatioPct = _imbalanceRatioPct,
                        StackedImbRangeMin = _imbalanceRangeMin,
                        StackedImbMaxDepthTicks = _imbMaxDepthTicksAnchored
                    };




                    this.LogInfo(string.Format(CultureInfo.InvariantCulture,
                        "[OnCalculate_SNAP] Bar={0} Time={1:O} High={2:F2} Low={3:F2} Close={4:F2} Volume={5:F2} Delta={6:F2} Ask={7:F2} Bid={8:F2} " +
                        "MaxBull={9:F4} MaxBear={10:F4} VolBurstZ={11:F4} CvdImpulse={12:F4} CvdCoherence={13:F4} AggPressure={14:F4} TradeRateZ={15:F4} Efficiency={16:F4} " +
                        "BuyTrades={17} SellTrades={18} TotalTrades={19} IttZ={20:F4} SweepUp={21} SweepDn={22} " +
                        "StackedBuyCount={23} StackedSellCount={24} StackedBuyTop={25} StackedSellBottom={26} StackedImbRatioPct={27:F4} StackedImbMinVolPerLevel={28} StackedImbRangeMin={29} StackedImbMaxDepthTicks={30} " +
                        "CandleDuration={31:F4} VolPerSecond={32:F4} EmaVolPerSecond={33:F4} EmaVolPerSecondStd={34:F4} CumulativeDelta={35:F4} CumulativeVolume={36:F4} BarDeltaPerVolume={37:F6}",
                        ovSnapshot.Bar, ovSnapshot.Time,
                        ovSnapshot.High, ovSnapshot.Low, ovSnapshot.Close, ovSnapshot.Volume, ovSnapshot.Delta, ovSnapshot.Ask, ovSnapshot.Bid,
                        ovSnapshot.MaxCounterShareBull, ovSnapshot.MaxCounterShareBear, ovSnapshot.VolBurstZ, ovSnapshot.CvdImpulse, ovSnapshot.CvdCoherence, ovSnapshot.AggPressure, ovSnapshot.TradeRateZ, ovSnapshot.Efficiency,
                        ovSnapshot.BuyTrades, ovSnapshot.SellTrades, ovSnapshot.TotalTrades, ovSnapshot.IttZ, ovSnapshot.SweepUpClosed, ovSnapshot.SweepDnClosed,
                        ovSnapshot.StackedBuyImbCount, ovSnapshot.StackedSellImbCount, ovSnapshot.StackedBuyImbTopCount, ovSnapshot.StackedSellImbBottomCount, ovSnapshot.StackedImbRatioPct, ovSnapshot.StackedImbMinVolPerLevel, ovSnapshot.StackedImbRangeMin, ovSnapshot.StackedImbMaxDepthTicks,
                        ovSnapshot.CandleDuration, ovSnapshot.VolPerSecond, ovSnapshot.EmaVolPerSecond, ovSnapshot.EmaVolPerSecondStd, ovSnapshot.CumulativeDelta, ovSnapshot.CumulativeVolume, ovSnapshot.BarDeltaPerVolume
                    ));



                    // --- In die History einf?gen (replace wenn Bar schon existiert) ---
                    var last = _ovSnapshotHistory.GetLastSnapshot();
                    if (last != null && last.Bar == closed)
                    {
                        _ovSnapshotHistory.ReplaceLast(ovSnapshot);
                        this.LogInfo($"[OnCalculate] Letzten Snapshot ersetzt f?r bar {closed} in OvSnapshotHistory");
                    }
                    else
                    {
                        _ovSnapshotHistory.Add(ovSnapshot);
                        this.LogInfo($"[OnCalculate-OF-FEAT DEBUG] _ovSnapshotHistory.Count={_ovSnapshotHistory.Count}, _ofFeaturesHistory.Count={(_ofFeaturesHistory != null ? _ofFeaturesHistory.Count.ToString() : "NULL")}");
                    }

                    UpdateLastClosedOv(ovSnapshot);

                    // ========== PHASE 3: MARKET REGIME ==========

                    // **********************************************************************************
                    // HIER AUFRUFEN: Berechnung des Marktregimes f?r den geschlossenen Balken
                    // **********************************************************************************
                    MarketRegime currentMarketRegime = MarketRegime.None;

                    _marketRegimeDetails = GetCurrentMarketRegime(closed, ovSnapshot);
                    if (_marketRegimeDetails == null)
                    {
                        this.LogWarn($"[OnCalculate] GetCurrentMarketRegime returned null for bar {closed}");
                    }
                    else
                    {
                        currentMarketRegime = _marketRegimeDetails.Regime;
                        this.LogInfo($"[OnCalculate] Market Regime for bar {closed + 1} calculated: {currentMarketRegime}");
                        this.LogDebug($"[OnCalculate] MarketRegimeDetails for bar {closed}: Regime={_marketRegimeDetails.Regime}");
                    }
                    // **********************************************************************************

                    // Setze das Feld im Snapshot (string)
                    ovSnapshot.MarketRegime = currentMarketRegime.ToString();

                    
                    // ========== PHASE 4: FEATURES ==========

                    this.LogDebug($"[OnCalculate-OF-FEAT] Aufruf von FeatureCalculator f?r bar {closed} (slopeWin=12, inflectionWin=5, persistDepth=20)");

                    // Aufruf des FeatureCalculators mit dem aktuellen OvSnapshot und der OfHistory
                    var feat = _featureCalculator?.CalculateFeatures(
                        currentOvSnapshot: ovSnapshot,
                        slopeWin: 12,
                        inflectionWin: 5,
                        persistDepth: 20,
                        inflectionMinDelta: 0.05m,
                        burstMinor: 1.5m,
                        burstMajor: 2.5m,
                        burstCooldownBars: 4,
                        persistToHistory: true);

                    if (feat == null)
                    {
                        this.LogDebug("[OnCalculate-OF-FEAT] Berechnung ergab null (keine Historie oder Fehler).");
                        this.LogDebug($"[OnCalculate-OF-FEAT DEBUG] _ovSnapshotHistory.Count={_ovSnapshotHistory.Count}, _ofFeaturesHistory.Count={(_ofFeaturesHistory != null ? _ofFeaturesHistory.Count.ToString() : "NULL")}");
                    }
                    else
                    {
                        this.LogDebug($"[OnCalculate-OF-FEAT] Features computed for bar {closed}: VolBurstClass={feat.VolBurstClass}, VolBurstZ={feat.VolBurstZ:F4}, InflectionPressure={feat.InflectionPressure}");
                        AddFeatureAndSync(feat);
                    }

                    // ===== HIER: SQUEEZE CALCULATOR USAGE =====
                    try
                    {
                        // Stelle sicher, dass _squeezeCalc initialisiert ist (in OnInitialize)
                        if (_squeezeCalc == null)
                        {
                            // defensiv neu anlegen, falls vergessen
                            _squeezeCalc = new SqueezeMomentumCalculator
                            {
                                BBPeriod = 10,
                                BBMultFactor = 2.0m,
                                KCPeriod = 10,
                                KCMultFactor = 1.5m
                            };
                            this.LogDebug("[OnCalculate-SQUEEZE] _squeezeCalc war null -> neu initialisiert (defensive).");
                        }

                        this.LogDebug($"[OnCalculate-SQUEEZE] _squeezeCalc!=null={_squeezeCalc != null}, _squeezeCalc.Count={_squeezeCalc?.Count}");

                        // Warmup: benutze jetzt public Count aus _squeezeCalc
                        int requiredWarmup = Math.Max(_squeezeCalc.BBPeriod, _squeezeCalc.KCPeriod);
                        int requiredForLinReg = _squeezeCalc.KCPeriod;
                        int required = Math.Max(requiredWarmup, requiredForLinReg);

                        var squeezeCandle = new Candle(
                            o: closedCandle.Open,
                            h: closedCandle.High,
                            l: closedCandle.Low,
                            c: closedCandle.Close
                        );

                        SqueezeResult rawSqRes = null;

                        try
                        {
                            // WICHTIG: Update muss immer aufgerufen werden, damit _squeezeCalc.Count w?chst
                            rawSqRes = _squeezeCalc.Update(squeezeCandle);
                        }
                        catch (Exception ex)
                        {
                            this.LogWarn($"[OnCalculate-SQUEEZE] Update failed for bar {closed}: {ex.GetType().Name}: {ex.Message}");
                            rawSqRes = new SqueezeResult
                            {
                                MomentumVal = 0m,
                                MomentumSlope = 0m,
                                State = SqueezeStateEnum.Unknown,
                                StateDurationBars = 0
                            };
                        }

                        // Logge das rohe Ergebnis (post-Update) f?r Debug/Monitoring
                        if (rawSqRes != null)
                        {
                            //this.LogInfo($"[SQUEEZE] raw (post-Update) bar={closed} rawState={rawSqRes.State} rawMom={rawSqRes.MomentumVal:F6} rawSlope={rawSqRes.MomentumSlope:F6} rawDur={rawSqRes.StateDurationBars}");
                        }

                        // Bestimme das effective Ergebnis, das extern (feat / ovSnapshot) verwendet wird.
                        // W?hrend Warmup (Count < required) liefert effective Unknown/0 ? raw bleibt komplett erhalten in Logs.
                        SqueezeResult effectiveRes;
                        if (_squeezeCalc.Count < required)
                        {
                            this.LogDebug($"[OnCalculate-SQUEEZE] Warmup not reached AFTER update: _squeezeCalc.Count={_squeezeCalc.Count}, required={required}. Using Unknown as effective result.");
                            effectiveRes = new SqueezeResult
                            {
                                MomentumVal = 0m,
                                MomentumSlope = 0m,
                                State = SqueezeStateEnum.Unknown,
                                StateDurationBars = 0
                            };
                        }
                        else
                        {
                            effectiveRes = rawSqRes ?? new SqueezeResult
                            {
                                MomentumVal = 0m,
                                MomentumSlope = 0m,
                                State = SqueezeStateEnum.Unknown,
                                StateDurationBars = 0
                            };
                        }

                        // robustes Mapping: benutze OfFeatures.Squeeze (wenn vorhanden) und erg?nze OvSnapshot Felder falls gew?nscht
                        if (effectiveRes != null)
                        {
                            //this.LogInfo($"[SQUEEZE] effective bar={closed} state={effectiveRes.State} mom={effectiveRes.MomentumVal:F4} slope={effectiveRes.MomentumSlope:F4} dur={effectiveRes.StateDurationBars}");

                            try
                            {
                                // --- OfFeatures.Squeeze ---
                                if (feat != null)
                                {
                                    if (feat.Squeeze == null)
                                        feat.Squeeze = new SqueezeFeatures();

                                    // effective Werte setzen (g?ltig erst nach Warmup)
                                    feat.Squeeze.SqueezeState = (SqueezeStateEnum)Enum.Parse(typeof(SqueezeStateEnum), effectiveRes.State.ToString());
                                    feat.Squeeze.MomentumVal = effectiveRes.MomentumVal;
                                    feat.Squeeze.MomentumSlope = effectiveRes.MomentumSlope;
                                    feat.Squeeze.StateDurationBars = effectiveRes.StateDurationBars;

                                    // Optional: falls SqueezeFeatures Platz f?r rohe Werte hat, setze sie ebenfalls
                                    // (wenn nicht vorhanden, kann diese Zeile entfallen)
                                    // try { feat.Squeeze.RawMomentum = rawSqRes?.MomentumVal ?? 0m; } catch { }
                                }

                                // --- OvSnapshot: optional hinzuf?gen (damit Logs/externes System die Werte direkt hat) ---
                                if (ovSnapshot != null)
                                {
                                    var snType = ovSnapshot.GetType();

                                    // Setze effective Momentum / State (sichtbar f?r externe Systeme erst nach Warmup)
                                    var snMomProp = snType.GetProperty("SqueezeMomentum");
                                    if (snMomProp != null)
                                        snMomProp.SetValue(ovSnapshot, ConvertToTargetType(effectiveRes.MomentumVal, snMomProp.PropertyType));

                                    var snStateProp = snType.GetProperty("SqueezeState");
                                    if (snStateProp != null)
                                        snStateProp.SetValue(ovSnapshot, effectiveRes.State.ToString());

                                    // OPTIONAL: wenn vorhanden, setze auch die rohen Werte, damit du intern die Berechnung sehen kannst
                                    var snRawMomProp = snType.GetProperty("SqueezeRawMomentum");
                                    if (snRawMomProp != null)
                                        snRawMomProp.SetValue(ovSnapshot, ConvertToTargetType(rawSqRes?.MomentumVal ?? 0m, snRawMomProp.PropertyType));

                                    var snRawStateProp = snType.GetProperty("SqueezeRawState");
                                    if (snRawStateProp != null)
                                        snRawStateProp.SetValue(ovSnapshot, rawSqRes?.State.ToString() ?? SqueezeStateEnum.Unknown.ToString());
                                }
                            }
                            catch (Exception ex)
                            {
                                this.LogDebug($"[OnCalculate-SQUEEZE] mapping exception (bar {closed}): {ex.GetType().Name}: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception exS)
                    {
                        this.LogWarn($"[OnCalculate-SQUEEZE] Unexpected exception while using _squeezeCalc for bar {closed}: {exS.GetType().Name}: {exS.Message}");
                    }

                    //Deklaration von detectedPattern an den Anfang des try-Blocks verschoben
                    MyNamespace.Strategies.Models.DetectedOrderflowPattern detectedPattern = new MyNamespace.Strategies.Models.DetectedOrderflowPattern();
                    PatternEvaluationResult detectedPatternEval = null;
                    try
                    {
                        // --- Lokale Sicherungen / Fallbacks ---
                        var localCurrentRegime = (currentMarketRegime == null) ? MarketRegime.None : currentMarketRegime;
                        var localCurrentDirectionalBias = (_marketStateEngine != null) ? _marketStateEngine.CurrentBias : MarketDirectionalBias.Undefined;
                        var localCurrentMarketStateForDetector = (_marketStateEngine != null) ? _marketStateEngine.CurrentMarketState : null;
                        var localCurrentMarketStructureContext = (_marketStructureContext != null) ? _marketStructureContext : null;


                        // ofFeaturesByBar: falls nicht vorhanden, baue aus _ofFeaturesHistory (sichere Variante ?ber GetLast)
                        Dictionary<int, OfFeatures> ofFeaturesByBarLocal = new Dictionary<int, OfFeatures>();
                        int groupsCount = 0;

                        if (_ofFeaturesHistory != null)
                        {
                            try
                            {
                                // Hole alle Elemente sicher ?ber die vorhandene API (?ltester zuerst)
                                var all = _ofFeaturesHistory.GetLast(_ofFeaturesHistory.Count);

                                if (all == null || all.Count == 0)
                                {
                                    this.LogDebug("[OnCalculate NEW LOG] _ofFeaturesHistory.GetLast(...) lieferte keine Elemente.");
                                }
                                else
                                {
                                    // Gruppiere nach Bar und konvertiere zu Liste, damit Count stabil ist
                                    var groups = all.Where(f => f != null).GroupBy(f => f.Bar).ToList();
                                    groupsCount = groups.Count;

                                    // Log: Anzahl Gruppen und bis zu 5 Sample-Keys
                                    var sampleKeys = groups.Count == 0 ? string.Empty : string.Join(',', groups.Take(5).Select(g => g.Key));
                                    this.LogDebug($"[OnCalculate NEW LOG] groups from history = {groups.Count}; sample keys = {sampleKeys}");

                                    // Baue Dictionary: pro Bar das zuletzt auftretende OfFeatures (j?ngstes innerhalb der Gruppe)
                                    ofFeaturesByBarLocal = groups.ToDictionary(g => g.Key, g => g.Last());
                                }
                            }
                            catch (Exception ex)
                            {
                                this.LogDebug($"[OnCalculate NEW LOG] Fehler beim Aufbau von ofFeaturesByBarLocal aus _ofFeaturesHistory: {ex.GetType().Name}: {ex.Message}");
                                ofFeaturesByBarLocal = new Dictionary<int, OfFeatures>();
                                groupsCount = 0;
                            }
                        }
                        else
                        {
                            this.LogDebug("[OnCalculate NEW LOG] _ofFeaturesHistory ist null; ofFeaturesByBar bleibt leer.");
                        }

                        // Erstellen Sie Momentaufnahmen und Funktions?bersichten (defensiver Zugriff).
                        var snapshot = feat?.Snapshot;
                        var snapExists = snapshot != null;
                        var snapLow = snapExists ? snapshot.Low.ToString("F2") : "n/a";
                        var snapHigh = snapExists ? snapshot.High.ToString("F2") : "n/a";
                        var snapClose = snapExists ? snapshot.Close.ToString("F2") : "n/a";
                        // Gehen Sie nicht davon aus, dass f?r diesen Snapshot-Typ Felder f?r offene oder gestapelte Ungleichgewichte vorhanden sind.
                        var snapVolBurstZ = snapExists ? feat.VolBurstZ.ToString("F2") : "n/a";
                        var snapStackedBuy = snapExists ? (snapshot.GetType().GetProperty("StackedBuyImbCount") != null ? snapshot.GetType().GetProperty("StackedBuyImbCount").GetValue(snapshot)?.ToString() ?? "n/a" : "n/a") : "n/a";
                        var snapStackedSell = snapExists ? (snapshot.GetType().GetProperty("StackedSellImbCount") != null ? snapshot.GetType().GetProperty("StackedSellImbCount").GetValue(snapshot)?.ToString() ?? "n/a" : "n/a") : "n/a";
                        var snapCvdImpulse = snapExists ? (snapshot.GetType().GetProperty("CvdImpulse") != null ? snapshot.GetType().GetProperty("CvdImpulse").GetValue(snapshot)?.ToString() ?? "n/a" : "n/a") : "n/a";

                        // Funktions?bersicht aus feat (falls verf?gbar) ? Vermeiden Sie Null-Bedingungen bei Enums/Dezimalzahlen.
                        var featVolBurstClass = feat != null ? feat.VolBurstClass.ToString() : "NULL";
                        var featVolBurstZ = feat != null ? feat.VolBurstZ.ToString("F2") : "NULL";
                        var featInflection = feat != null ? feat.InflectionPressure.ToString() : "NULL";
                        // Optionale Schl?sselindikatoren durch Reflexion (defensiv)
                        var featMomentum = "n/a";
                        var featImbalance = "n/a";
                        if (feat != null)
                        {
                            var mprop = feat.GetType().GetProperty("Momentum");
                            if (mprop != null) featMomentum = mprop.GetValue(feat)?.ToString() ?? "n/a";
                            var iprop = feat.GetType().GetProperty("Imbalance");
                            if (iprop != null) featImbalance = iprop.GetValue(feat)?.ToString() ?? "n/a";
                        }

                        // Market context
                        var marketStateBias = localCurrentMarketStateForDetector != null ? localCurrentMarketStateForDetector.ToString() : "null";
                        var marketStateConf = localCurrentMarketStateForDetector != null ?
                            (localCurrentMarketStateForDetector.GetType().GetProperty("Confidence") != null ? localCurrentMarketStateForDetector.GetType().GetProperty("Confidence").GetValue(localCurrentMarketStateForDetector)?.ToString() ?? "null" : "null")
                            : "null";

                        // Konfiguration / Anzahl der Evaluatoren (defensiv ? direkter Zugriff versuchen, Fallback auf Reflexion eines wahrscheinlich privaten Feldes)
                        var configuredPatternCount = _strategySetup?.PatternConditionConfigs?.Count ?? 0;
                        int evaluatorCount = 0;
                        string evaluatorPreview = string.Empty;
                        try
                        {
                            var labels = new List();




                            // DIAG: Quelle ermitteln
                            var evalsProp = _patternSignaturer?.GetType().GetProperty("Evaluators");
                            if (evalsProp != null)
                            {
                                this.LogDebug("[OnCalculate DIAG] _patternSignaturer Zeigt ?ffentliche Eigenschaften ?Evaluators? an ? Lesen aus Eigenschaften.");
                            }
                            else
                            {
                                this.LogDebug("[OnCalculate DIAG] _patternSignaturer Gibt die ?ffentliche Eigenschaft ?Evaluators? NICHT frei ? versucht, auf private Felder zur?ckzugreifen..");
                            }

                            // Sammle und logge beide Quellen parallel f?r Diagnose (wenn vorhanden)
                            try
                            {
                                if (evalsProp != null)
                                {
                                    var evalsVal = evalsProp.GetValue(_patternSignaturer) as System.Collections.IEnumerable;
                                    if (evalsVal != null)
                                    {
                                        int i = 0;
                                        var propParts = new List<string>();
                                        foreach (var e in evalsVal)
                                        {
                                            var id = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(e);
                                            string typeName = e?.GetType().Name ?? "null";
                                            string dir = "n/a";
                                            try
                                            {
                                                if (e is IPatternEvaluator pe) dir = pe.Direction.ToString();
                                                else
                                                {
                                                    var propDir = e?.GetType().GetProperty("Direction");
                                                    if (propDir != null) dir = propDir.GetValue(e)?.ToString() ?? "n/a";
                                                }
                                            }
                                            catch { dir = "err"; }
                                            propParts.Add($"prop#{i}:{typeName}[{dir}]id={id}");
                                            i++;
                                        }
                                        this.LogDebug("[OnCalculate DIAG] evaluators (property) = " + string.Join(" | ", propParts));
                                    }
                                    else
                                    {
                                        this.LogDebug("[OnCalculate DIAG] evaluators property returned null or not IEnumerable");
                                    }
                                }
                            }
                            catch (Exception exPropDiag)
                            {
                                this.LogDebug("[OnCalculate DIAG] reading evaluators property failed: " + exPropDiag.Message);
                            }

                            // private field fallback - auch separat loggen
                            try
                            {
                                var field = _patternSignaturer?.GetType().GetField("_evaluators", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (field != null)
                                {
                                    var val = field.GetValue(_patternSignaturer) as System.Collections.IList;
                                    if (val != null)
                                    {
                                        var fieldParts = new List<string>();
                                        for (int i = 0; i < val.Count; i++)
                                        {
                                            var e = val[i];
                                            var id = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(e);
                                            string typeName = e?.GetType().Name ?? "null";
                                            string dir = "n/a";
                                            try
                                            {
                                                if (e is IPatternEvaluator pe) dir = pe.Direction.ToString();
                                                else
                                                {
                                                    var propDir = e?.GetType().GetProperty("Direction");
                                                    if (propDir != null) dir = propDir.GetValue(e)?.ToString() ?? "n/a";
                                                }
                                            }
                                            catch { dir = "err"; }
                                            fieldParts.Add($"field#{i}:{typeName}[{dir}]id={id}");
                                        }
                                        this.LogDebug("[OnCalculate DIAG] evaluators (field _evaluators) = " + string.Join(" | ", fieldParts));
                                    }
                                    else
                                    {
                                        this.LogDebug("[OnCalculate DIAG] private field _evaluators returned null or not IList");
                                    }
                                }
                                else
                                {
                                    this.LogDebug("[OnCalculate DIAG] no private field named _evaluators found on _patternSignaturer");
                                }
                            }
                            catch (Exception exFieldDiag)
                            {
                                this.LogDebug("[OnCalculate DIAG] reading private field _evaluators failed: " + exFieldDiag.Message);
                            }

                            // Jetzt wie bisher: kompakte Preview (bevorzuge Property, fallback auf Feld)
                            int localEvaluatorCount = 0;
                            var localLabels = new List<string>();
                            if (evalsProp != null)
                            {
                                var evalsVal = evalsProp.GetValue(_patternSignaturer) as System.Collections.IEnumerable;
                                if (evalsVal != null)
                                {
                                    foreach (var e in evalsVal)
                                    {
                                        localEvaluatorCount++;
                                        if (localLabels.Count < 6)
                                            localLabels.Add(GetEvaluatorLabel(e));
                                    }
                                }
                            }
                            else
                            {
                                var field = _patternSignaturer?.GetType().GetField("_evaluators", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                var val = field?.GetValue(_patternSignaturer) as System.Collections.IList;
                                if (val != null)
                                {
                                    localLabels = new List<string>();
                                    localEvaluatorCount = val.Count;
                                    foreach (var e in val)
                                    {
                                        if (localLabels.Count < 6)
                                            localLabels.Add(GetEvaluatorLabel(e));
                                    }
                                }
                            }

                            evaluatorCount = localEvaluatorCount;
                            evaluatorPreview = localLabels.Count == 0 ? (evaluatorCount == 0 ? "n/a" : "unnamed") : string.Join(",", localLabels);
                            // Ende try
                        }
                        catch (Exception ex)
                        {
                            // defensiv: Fehler loggen, aber nicht die ganze OnCalculate abbrechen
                            this.LogDebug("[OnCalculate DIAG] evaluator introspection failed: " + ex.Message);
                            evaluatorCount = 0;
                            evaluatorPreview = "n/a";
                        }

                        // history counts
                        string ofHistCountStr = (_ofFeaturesHistory != null) ? _ofFeaturesHistory.Count.ToString() : "NULL";
                        var ofFeaturesByBarCount = ofFeaturesByBarLocal?.Count ?? 0;

                        // final compact Detect-Inputs log (single-line summary)
                        this.LogDebug(
                            $"[OnCalculate-Detect-Inputs] bar={(feat == null ? -1 : feat.Bar)} time={DateTime.UtcNow:O} historyCount={ofHistCountStr} ofFeaturesByBar={ofFeaturesByBarCount} groups={groupsCount} " +
                            $"snapshotExists={snapExists} Low={snapLow} High={snapHigh} Close={snapClose} VolBurstZ={snapVolBurstZ} StackedBuyImbCount={snapStackedBuy} StackedSellImbCount={snapStackedSell} CvdImpulse={snapCvdImpulse} " +
                            $"VolBurstClass={featVolBurstClass} VolBurstZ={featVolBurstZ} Inflection={featInflection} Momentum={featMomentum} Imbalance={featImbalance} " +
                            $"currentRegime={localCurrentRegime} bias={localCurrentDirectionalBias} marketState={marketStateBias} " +
                            $"configuredPatterns={configuredPatternCount} evaluators={evaluatorCount} [types={evaluatorPreview}]"
                        );


                        // Optional multiline debug detail (only when debug enabled) ? more verbose info
                        try
                        {
                            var isDebugEnabledMethod = this.GetType().GetMethod("IsDebugEnabled", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                            bool isDebug = true;
                            if (isDebugEnabledMethod != null)
                            {
                                isDebug = (bool)isDebugEnabledMethod.Invoke(this, null);
                            }

                            if (isDebug)
                            {
                                // Log first up to 5 ofFeaturesByBar keys and counts per key
                                try
                                {
                                    var keys = ofFeaturesByBarLocal?.Keys.Take(5).ToList() ?? new List<int>();
                                    var perKeyCounts = new List<string>();
                                    foreach (var k in keys)
                                    {
                                        perKeyCounts.Add($"{k}");
                                    }
                                    this.LogDebug($"[OnCalculate-Detect-Inputs-DETAIL] ofFeaturesByBar sample keys = {string.Join(',', perKeyCounts)}");
                                }
                                catch
                                {
                                    // swallow
                                }

                                // Log full marketState object if present (defensive)
                                if (localCurrentMarketStateForDetector != null)
                                {
                                    try
                                    {
                                        this.LogDebug($"[OnCalculate-Detect-Inputs-DETAIL] currentMarketState={localCurrentMarketStateForDetector}");
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch
                        {
                            // ignore debug-check failures
                        }


                        // ========== PHASE 5: PATTERN DETECTION & MARKET STATE ==========

                        // detectedPattern vorbereiten (Default nutzt die in der Klasse definierten Defaults)
                        //var detectedPattern = new MyNamespace.Strategies.Models.DetectedOrderflowPattern();
                        // Hinweis: IsDetected ist readonly und ergibt sich aus Type == OrderflowPatternType.None.


                        if (_patternSignaturer == null)
                        {
                            this.LogWarn("[OnCalculate NEW LOG] _patternSignaturer ist null; DetectDominantOrderflowPattern wird nicht aufgerufen.");
                        }
                        else if (feat == null)
                        {
                            this.LogWarn("[OnCalculate NEW LOG] feat ist null; DetectDominantOrderflowPattern wird nicht aufgerufen.");
                        }
                        else
                        {
                            // Sorge daf?r, dass wir keine null in nicht-nullbare Parameter geben.
                            // ofFeaturesByBarLocal ist bereits ein Dictionary (niemals null wegen Initialisierung), aber defensiv:
                            var safeOfFeaturesByBar = ofFeaturesByBarLocal ?? new Dictionary<int, OfFeatures>();
                            // _ofFeaturesHistory kann null sein; wenn die Signatur nicht-null erwartet, entscheide:
                            // - Wenn DetectDominantOrderflowPattern akzeptiert, dass history null sein kann: ?bergebe sie direkt.
                            // - Wenn nicht, ?bergebe eine leere Liste/Wrapper oder breche den Aufruf ab.
                            // Hier: wir pr?fen & brechen ab, falls history null und Signatur nicht-nullable ist.
                            if (_ofFeaturesHistory == null)
                            {
                                this.LogDebug("[OnCalculate NEW LOG] _ofFeaturesHistory ist null; DetectDominantOrderflowPattern wird mit leerer History aufgerufen.");
                                // Falls die Signatur non-null f?r history erwartet, kannst du statt null ein leeres OfFeaturesHistory-Objekt ?bergeben,
                                // z.B. new OfFeaturesHistory(0) falls sinnvoll. Hier ?bergeben wir null nur wenn Signatur nullable ist.
                            }

                            // F?hre den Aufruf durch (alle ben?tigten Parameter ?bergeben).
                            // Stelle sicher, dass die Reihenfolge der Parameter mit der Methodensignatur ?bereinstimmt:
                            int? currentBar = closed; // closed ist der geschlossene Bar-Index, den Du oben berechnet hast

                            var result = _patternSignaturer.DetectDominantOrderflowPattern(
                                feat.Bar,
                                _ofFeaturesHistory,
                                safeOfFeaturesByBar,
                                localCurrentRegime,
                                localCurrentDirectionalBias,
                                localCurrentMarketStateForDetector,
                                localCurrentMarketStructureContext,
                                currentBar   // <-- neu: explizite ?bergabe des Bar-Index
                            );
                            // Ergebnis-Handling: behalte detectedPattern (DetectedOrderflowPattern) f?r UpdateState,
                            // erstelle zus?tzlich detectedPatternEval (PatternEvaluationResult) f?r CSV / weitere Konsumenten.
                            

                            // result kann null sein -> weiter behandeln wie bisher
                            if (result == null)
                            {
                                this.LogDebug("[OnCalculate NEW LOG] DetectDominantOrderflowPattern returned NULL -> treat as no pattern.");

                                
                                // detectedPattern bleibt null (oder du kannst ein leeres DetectedOrderflowPattern setzen, falls n?tig)
                                detectedPattern = null;
                                // Erzeuge ein konsistentes PatternEvaluationResult.NotDetected, damit CSV-Code nicht null checks ?berall braucht
                                detectedPatternEval = PatternEvaluationResult.NotDetected(
                                    OrderflowPatternType.None,
                                    "No pattern (detector returned null)"
                                );
                            }
                            else
                            {
                                // original object (behalte f?r UpdateState)
                                detectedPattern = result;

                                // Adapter: konstruiere PatternEvaluationResult, verwendet Reasons / MatchedValues / Counts / DetectReason
                                try
                                {
                                    detectedPatternEval = result.ToPatternEvaluationResult();
                                }
                                catch (Exception exConv)
                                {
                                    this.LogWarn($"[OnCalculate NEW LOG] ToPatternEvaluationResult() failed: {exConv.GetType().Name}: {exConv.Message}");
                                    // Fallback: minimal NotDetected/Detected je nach IsDetected
                                    detectedPatternEval = detectedPattern.IsDetected
                                        ? PatternEvaluationResult.Detected(detectedPattern.Type, detectedPattern.ConfidenceScore, detectedPattern.Reasons, detectedPattern.MatchedCriteriaValues, detectedPattern.MetHardConditions, detectedPattern.MetRelevantConditions, detectedPattern.MetDiagnosticConditions, detectedPattern.MetCriteriaCount, detectedPattern.PossibleCriteriaCount, detectedPattern.DetectReason)
                                        : PatternEvaluationResult.NotDetected(detectedPattern.Type, detectedPattern.DetectReason ?? "Conversion failed");
                                }

                                this.LogDebug($"[OnCalculate NEW LOG] Pattern result: IsDetected={detectedPatternEval.IsDetected}, Type={detectedPatternEval.PatternType}, Confidence={detectedPatternEval.ConfidenceScore:F2}");
                            }
                        }

                        // NEW LOG: Vor UpdateState-Ausf?hrung Loggen
                        this.LogDebug($"[OnCalculate NEW LOG] Before MarketStateEngine.UpdateState: patternDetected={detectedPattern.IsDetected}, featBar={(feat == null ? -1 : feat.Bar)}, currentRegime={localCurrentRegime}");

                        // MarketStateEngine aufrufen und eventuelle Exceptions loggen
                        MyNamespace.Strategies.Models.MarketState newMarketState = null;
                        if (_marketStateEngine == null)
                        {
                            this.LogWarn("[OnCalculate NEW LOG] _marketStateEngine ist null; UpdateState wird nicht aufgerufen.");
                        }
                        else
                        {
                            try
                            {
                                this.LogDebug($"[OnCalculate NEW LOG] Calling MarketStateEngine.UpdateState for bar={(feat == null ? -1 : feat.Bar)}");
                                // UpdateState verwendet weiterhin detectedPattern (DetectedOrderflowPattern)
                                newMarketState = _marketStateEngine.UpdateState(feat, localCurrentRegime, detectedPattern);

                                if (newMarketState == null)
                                {
                                    this.LogDebug("[OnCalculate NEW LOG] MarketStateEngine.UpdateState returned null.");
                                }
                                else
                                {
                                    this.LogDebug($"[OnCalculate NEW LOG] MarketStateEngine updated state: NewState={(newMarketState.ToString() ?? "object")}");
                                }
                            }
                            catch (Exception exState)
                            {
                                this.LogWarn($"[OnCalculate NEW LOG] MarketStateEngine threw in UpdateState for bar {(feat == null ? -1 : feat.Bar)}: {exState.GetType().Name}: {exState.Message}\n{exState.StackTrace}");
                            }
                        }

                        // Wenn ein neuer State zur?ckkam, in lokale Variable ?bernehmen
                        if (newMarketState != null)
                        {
                            _currentMarketState = newMarketState;
                            this.LogDebug($"[OnCalculate NEW LOG] _currentMarketState set (bar={(feat == null ? -1 : feat.Bar)})");
                        }
                        else
                        {
                            this.LogDebug($"[OnCalculate NEW LOG] _currentMarketState unchanged (bar={(feat == null ? -1 : feat.Bar)})");
                        }
                    }
                    catch (Exception exOuter)
                    {
                        this.LogWarn($"[OnCalculate NEW LOG] Unexpected exception in pattern/marketstate block: {exOuter.GetType().Name}: {exOuter.Message}\n{exOuter.StackTrace}");
                    }

                    try
                    {
                        this.LogDebug($"[OnCalculate-DBG] bar={bar} _csvWriter=={_csvWriter == null} ovSnapshot=={(ovSnapshot == null)} InstrumentInfo=={(InstrumentInfo == null)} detectedPattern=={(detectedPattern == null)} feat=={(feat == null)}");
                    }
                    catch (Exception ex) { this.LogError($"[OnCalculate] dbg log failed: {ex}"); }

                    // --- SERIALIZE THRESHOLDS SNAPSHOT (FULL) using System.Text.Json ---
                    string thresholdsSnapshotJson = string.Empty;
                    try
                    {
                        var thresholdsResultLocal = _thresholdsResolver?.LastAdaptiveThresholdsResult;
                        if (thresholdsResultLocal != null)
                        {
                            var jsonOpts = new System.Text.Json.JsonSerializerOptions
                            {
                                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                                WriteIndented = false
                            };

                            thresholdsSnapshotJson = System.Text.Json.JsonSerializer.Serialize(thresholdsResultLocal, jsonOpts);
                        }
                    }
                    catch (Exception ex)
                    {
                        this.LogWarn($"[OnCalculate-CSV] Failed to serialize LastAdaptiveThresholdsResult with System.Text.Json: {ex.Message}");
                        thresholdsSnapshotJson = string.Empty;
                    }
                    // Robustified CSV extraction + enqueue (use instead of previous block)
                    try
                    {
                        // Defensive pre-checks
                        if (ovSnapshot == null)
                        {
                            this.LogDebug("[OnCalculate-CSV] ovSnapshot == null -> skipping CSV for this OnCalculate call.");
                            return; // nothing to do for this bar
                        }


                        if (_csvWriter == null)
                        {
                            this.LogDebug("[OnCalculate-CSV] _csvWriter == null -> skipping CSV enqueue (writer not available).");
                            // continue normal strategy execution (don't return if you need other logic below)
                        }

                        // Telemetrie / run metadata (sichere Defaults)
                        string backtestRunId = string.Empty;
                        string commitHash = string.Empty;
                        string featureFlags = string.Empty;

                        // Metafelder defaults
                        string metCriteriaListLocal = string.Empty;
                        string metHardListLocal = string.Empty;
                        string metRelListLocal = string.Empty;

                        // Composite / rolling stats defaults
                        double compositeScore = double.NaN;
                        double cvdMean30 = double.NaN;
                        double cvdStd30 = double.NaN;
                        double volBurstMax3 = double.NaN;
                        int volBurstAge = -1;
                        int inflectionCount3 = 0;
                        bool softVolBurstFlag = false;
                        bool softNegativeFlag = false;
                        bool finalDecision = false;

                        // Pattern/feature defaults
                        string patternType = "";
                        string patternDirection = "";
                        string patternCategory = "";
                        double patternLevel = double.NaN;
                        double patternConfidence = double.NaN;
                        double patternScore = double.NaN;
                        double patternCombinedConf = double.NaN;

                        string volBurstClass = "";
                        int volBurstCooldownLeft = -1;
                        double slopeCvd = double.NaN;
                        double slopePressure = double.NaN;
                        double slopeEff = double.NaN;
                        double slopeTradeRate = double.NaN;
                        int persistBull = 0;
                        int persistBear = 0;
                        string inflectionCvd = "";
                        string inflectionPressure = "";

                        // Extract pattern safely
                        try
                        {
                            if (detectedPatternEval != null)
                            {
                                try { patternType = detectedPatternEval.PatternType.ToString(); } catch { patternType = "UNKNOWN"; }
                                // Direction und Category sind Eigenschaften von DetectedOrderflowPattern, nicht PatternEvaluationResult.
                                // Falls Direction/Category wichtig f?r CSV, nimm sie weiterhin aus detectedPattern (falls vorhanden):
                                try { patternDirection = detectedPattern?.Direction.ToString() ?? string.Empty; } catch { patternDirection = "UNKNOWN"; }
                                try { patternCategory = detectedPattern?.Category.ToString() ?? string.Empty; } catch { patternCategory = "UNKNOWN"; }

                               
                                // Level kommt aus DetectedOrderflowPattern (nicht in PatternEvaluationResult) -> nimm detectedPattern.Level
                                try { if (detectedPattern?.Level.HasValue == true) patternLevel = (double)detectedPattern.Level.Value; } catch { patternLevel = double.NaN; }

                                try { patternConfidence = (double)detectedPatternEval.ConfidenceScore; } catch { patternConfidence = double.NaN; }
                                // Score & CombinedConfidence live in DetectedOrderflowPattern ? verwende detectedPattern falls vorhanden
                                try { patternScore = detectedPattern?.Score ?? double.NaN; } catch { patternScore = double.NaN; }
                                try { patternCombinedConf = detectedPattern != null ? (double)detectedPattern.CombinedConfidence : double.NaN; } catch { patternCombinedConf = double.NaN; }

                                // Human readable summary: prefer PatternEvaluationResult.Reasons/DetectReason
                                try
                                {
                                    metCriteriaListLocal = !string.IsNullOrEmpty(detectedPatternEval.DetectReason)
                                        ? detectedPatternEval.DetectReason
                                        : (detectedPatternEval.Reasons != null && detectedPatternEval.Reasons.Any()
                                            ? string.Join(" | ", detectedPatternEval.Reasons)
                                            : (detectedPattern != null ? detectedPattern.ToString() : string.Empty));
                                }
                                catch { metCriteriaListLocal = string.Empty; }

                                // F?r Met-Listen (Hard/Relevant/Diagnostic) nutze detectedPatternEval.* (sie sind List<EvaluatedConditionDetail>)
                                try { metHardListLocal = detectedPatternEval.MetHardConditions != null ? string.Join(" | ", detectedPatternEval.MetHardConditions.Select(x => x.ToString())) : string.Empty; } catch { metHardListLocal = string.Empty; }
                                try { metRelListLocal = detectedPatternEval.MetRelevantConditions != null ? string.Join(" | ", detectedPatternEval.MetRelevantConditions.Select(x => x.ToString())) : string.Empty; } catch { metRelListLocal = string.Empty; }
                            }
                        }
                        catch (Exception exPat)
                        {
                            this.LogWarn($"[OnCalculate CSV_EXTRA] Exception reading detectedPattern: {exPat}");
                        }

                        // Extract OfFeatures (feat) safely - if variable 'feat' is not in scope, try to obtain via feature calculator if available
                        OfFeatures f = null;
                        try
                        {
                            // 'feat' may be in your method scope; try to use it via dynamic lookup:
                            // if you have a local variable named feat, the compiler will use it; otherwise this line does nothing.
                            f = feat ?? null;
                        }
                        catch
                        {
                            f = null;
                        }

                        // If no local feat, try fallback accessor if you have an OrderflowFeatureCalculator instance named orderflowFeatureCalculator
                        try
                        {
                            if (f == null)
                            {
                                // Uncomment/adapt if you have an instance:
                                // f = orderflowFeatureCalculator?.GetLastComputedFeature();
                            }
                        }
                        catch { /* ignore */ }

                        if (f != null)
                        {
                            try { volBurstClass = f.VolBurstClass.ToString(); } catch { volBurstClass = ""; }
                            try { volBurstCooldownLeft = f.VolBurstCooldownLeft; } catch { volBurstCooldownLeft = -1; }
                            try { slopeCvd = (double)f.SlopeCvd; } catch { slopeCvd = double.NaN; }
                            try { slopePressure = (double)f.SlopePressure; } catch { slopePressure = double.NaN; }
                            try { slopeEff = (double)f.SlopeEff; } catch { slopeEff = double.NaN; }
                            try { slopeTradeRate = (double)f.SlopeTradeRate; } catch { slopeTradeRate = double.NaN; }
                            try { persistBull = f.PersistBull; } catch { persistBull = 0; }
                            try { persistBear = f.PersistBear; } catch { persistBear = 0; }
                            try { inflectionCvd = f.InflectionCvd.ToString(); } catch { inflectionCvd = ""; }
                            try { inflectionPressure = f.InflectionPressure.ToString(); } catch { inflectionPressure = ""; }
                        }

                        // --- GET PRUNER NULLIFIED FIELDS FROM ThresholdsResolver LAST RESULT ---
                        // This reads the LastAdaptiveThresholdsResult property (must be implemented thread-safe on the resolver).
                        string prunerNullifiedFieldsLocal = string.Empty;
                        try
                        {
                            prunerNullifiedFieldsLocal = _thresholdsResolver?.LastAdaptiveThresholdsResult?.NullifiedJoined ?? string.Empty;
                        }
                        catch (Exception exPrunerRead)
                        {
                            this.LogWarn($"[OnCalculate-CSV] Failed to read pruner info from thresholdsResolver: {exPrunerRead.Message}");
                            prunerNullifiedFieldsLocal = string.Empty;
                        }

                        // Compose and enqueue CSV line (if writer available)
                        string csvLine = null;
                        
                        // Zus?tzliche Null-Pr?fungen vor der CSV-Verarbeitung
                        if (ovSnapshot == null)
                        {
                            this.LogError("[OnCalculate-CSV_ERROR] ovSnapshot is null - skipping CSV processing");
                        }
                        else if (_csvWriter == null)
                        {
                            this.LogError("[OnCalculate-CSV_ERROR] _csvWriter is null - skipping CSV processing");
                        }
                        else
                        {
                            try
                            {
                            // extract historyVersion + createdAt from thresholds result (defensive)
                            int hv = -1;
                            DateTime? createdAt = null;
                            try
                            {
                                var thresholdsResultLocal = _thresholdsResolver?.LastAdaptiveThresholdsResult;
                                if (thresholdsResultLocal != null)
                                {
                                    try { hv = thresholdsResultLocal.HistoryVersion ?? -1; } catch { hv = -1; }
                                    try { createdAt = thresholdsResultLocal.CreatedAtUtc; } catch { createdAt = null; }
                                }
                            }
                            catch { hv = -1; createdAt = null; }

                            csvLine = ToCsvLine(
                                ovSnapshot,
                                InstrumentInfo?.Instrument ?? "UNKNOWN",
                                thresholdsSnapshotJson: thresholdsSnapshotJson,
                                historyVersion: hv,
                                thresholdsCreatedAtUtc: createdAt,
                                prunerNullifiedFields: prunerNullifiedFieldsLocal,
                                finalDecision: finalDecision,
                                metCriteriaList: metCriteriaListLocal,
                                metHardList: metHardListLocal,
                                metRelList: metRelListLocal,
                                compositeScore: compositeScore,
                                cvdMean30: cvdMean30,
                                cvdStd30: cvdStd30,
                                volBurstMax3: volBurstMax3,
                                volBurstAge: volBurstAge,
                                inflectionCount3: inflectionCount3,
                                softVolBurstFlag: softVolBurstFlag,
                                softNegativeFlag: softNegativeFlag,
                                // pattern/feature fields
                                patternType: patternType,
                                patternDirection: patternDirection,
                                patternCategory: patternCategory,
                                patternLevel: patternLevel,
                                patternConfidence: patternConfidence,
                                patternScore: patternScore,
                                patternCombinedConf: patternCombinedConf,
                                volBurstClass: volBurstClass,
                                volBurstCooldownLeft: volBurstCooldownLeft,
                                slopeCvd: slopeCvd,
                                slopePressure: slopePressure,
                                slopeEff: slopeEff,
                                slopeTradeRate: slopeTradeRate,
                                persistBull: persistBull,
                                persistBear: persistBear,
                                inflectionCvd: inflectionCvd,
                                inflectionPressure: inflectionPressure,
                                backtestRunId: backtestRunId,
                                commitHash: commitHash,
                                featureFlags: featureFlags
                            );
                        }
                        catch (Exception exFmt)
                        {
                            this.LogError($"[OnCalculate-CSV_ERROR] ToCsvLine formatting failed for Bar={ovSnapshot.Bar}. Exception: {exFmt}");
                            csvLine = null;
                        }

                        if (!string.IsNullOrEmpty(csvLine) && _csvWriter != null)
                        {
                            try
                            {
                                _csvWriter.EnqueueLine(csvLine);
                                this.LogDebug($"[OnCalculate-CSV] Enqueued CSV line for Bar={ovSnapshot.Bar}.");
                            }
                            catch (Exception exEnq)
                            {
                                this.LogError($"[OnCalculate-CSV_ERROR] EnqueueLine failed for Bar={ovSnapshot.Bar}. Exception: {exEnq}");
                            }
                        }
                        } // Ende des else-Blocks f?r CSV-Verarbeitung
                    }
                    catch (NullReferenceException nre)
                    {
                        // Log full context to help debug exact null target
                        this.LogError($"[CSV_FATAL] NullReferenceException in CSV block. ovSnapshot=={(ovSnapshot == null)} _csvWriter=={(_csvWriter == null)} InstrumentInfo=={(InstrumentInfo == null)} detectedPattern=={(detectedPattern == null)}. Exception: {nre}");
                        this.LogError("[OnCalculate] NullReferenceException in CSV block: " + nre.ToString());
                    }
                    catch (Exception ex)
                    {
                        this.LogError($"[CSV_FATAL] Unexpected exception in CSV block: {ex}");
                    }


                    // ========== PHASE 6: SIGNAL EVALUATION ==========


                    this.LogInfo($"[OnCalculate] Calling EvaluateSignalsAndOrders for closed={closed}, lastEvalBar(before)={_lastEvalBar}");
                    //this.LogInfo($"[DEBUG] Vor EvaluateSignalsAndOrders: Bar={bar}, POC={currentPOC}, VAH={currentVAH}, VAL={currentVAL}");
                    //this.LogInfo($"[DBG] Enter isNewBar: bar={bar}, closed={closed}, now={DateTime.UtcNow:O}, CurrentBar={CurrentBar}, _lastEvalBar={_lastEvalBar}");
                    EvaluateSignalsAndOrders(
                        closed, ovSnapshot, _ofFeaturesHistory, isBlockedLong, isBlockedShort,
                        _currentLevelsSnapshot, currentPOC, currentVAH, currentVAL, vwSignal, vwEntryDir, vwEntryPrice, currentMarketRegime, detectedPattern, _currentMarketState);
                    _lastEvalBar = closed;

                    this.LogInfo($"[OnCalculate] EvaluateSignalsAndOrders completed for closed={closed}; setting _lastEvalBar={closed}");
                }


                ClearVwSignal();
                if (_vwPendingReset) { VW_Reset(); _vwPendingReset = false; }


                if (!IsRealtimeBar(bar)) return;


                // Zeitfilter-Logik: Pr?fen, ob Orders am Ende der Session gel?scht werden sollen.
                // -----------------------------------------------------------------------------------

                // Pr?fen, ob der Zeitfilter ?berhaupt aktiviert ist.
                // Pr?fen, ob eine Order existiert und TimeFilter f?r diese Order aktiviert ist.
                if ((_pullbackOrder != null || _entryOrder != null) && _orderEnableTimeFilter)
                {
                    // Aktuellen Status f?r den aktuellen Bar abrufen (setup-spezifisch, ?ber alle Sessions).
                    bool isCurrentlyInSession = IsWithinAnyTradingSession(bar, _orderTradingSessions);

                    // Der entscheidende Moment: Wir waren vorher IN einer Handelssitzung (irgendeiner) und sind es JETZT NICHT MEHR.
                    // Das bedeutet, ALLE Handelssitzungen sind zu Ende (f?r diese Order).
                    if (_isInsideOrderSession && !isCurrentlyInSession && _orderCancelAtSessionEnd)
                    {
                        this.LogInfo($"[OnCalculate] Alle Handelssitzungen beendet (Bar-Zeit: {c.Time:HH:mm}, Sessions: {_orderTradingSessions.Count}). L?sche Pending-Orders.");
                        // Pr?fen, ob eine offene Entry-Order existiert und diese l?schen.
                        if (_pullbackOrder != null && _pullbackOrder.State == OrderStates.Active)
                        {
                            this.LogInfo($"[OnCalculate] L?sche offene Pullback-Order ID: {_pullbackOrder.Id}");
                            CancelOrder(_pullbackOrder);
                            // Referenz wird in OnOrderChanged() auf null gesetzt
                        }

                        if (_entryOrder != null && _entryOrder.State == OrderStates.Active)
                        {
                            this.LogInfo($"[OnCalculate] Lösche offene Entry-Order ID: {_entryOrder.Id}");
                            CancelOrder(_entryOrder);
                            // Referenz wird in OnOrderChanged() auf null gesetzt
                        }
                    }

                    // Den Zustand f?r die n?chste Pr?fung aktualisieren (setup-spezifisch).
                    _isInsideOrderSession = isCurrentlyInSession;
                }

                // ?? TIMEOUT-CHECK ?? (Robust, beibehalten)
                // Pr?fe auf jedem Bar (Latest closed bar). Verwende explizit LatestClosedBar = CurrentBar - 1
                int latestClosedBar = CurrentBar - 1;

                // Nur pr?fen, wenn Timeout aktiviert ist UND mindestens eine Order existiert
                if (EnableOrderTimeout && (_entryOrder != null || _pullbackOrder != null))
                {
                    if (_entryBarIndex >= 0 && OrderTimeoutBars > 0) // defensive Vorbedingungen
                    {
                        int timeoutBarIndex = _entryBarIndex + OrderTimeoutBars; // $$timeoutBarIndex = _entryBarIndex + OrderTimeoutBars$$

                        // Timeout nur pr?fen auf dem LatestClosedBar (oder dem erwarteten "bar" Parameter)
                        if (latestClosedBar >= timeoutBarIndex)
                        {
                            // Hole Status nur wenn Order nicht null (safe)
                            OrderStatus? entryStatus = null;
                            OrderStatus? pullbackStatus = null;

                            if (_entryOrder != null)
                            {
                                try { entryStatus = _entryOrder.Status(); }
                                catch (Exception ex) { this.LogWarn($"[OnCalculate-Timeout Check] Could not read _entryOrder.Status(): {ex.Message}"); }
                            }
                            if (_pullbackOrder != null)
                            {
                                try { pullbackStatus = _pullbackOrder.Status(); }
                                catch (Exception ex) { this.LogWarn($"[OnCalculate-Timeout Check] Could not read _pullbackOrder.Status(): {ex.Message}"); }
                            }

                            // Hilfsfunktion: ist eine Order noch "aktiv" (wird gef?llt oder offen gehalten)?
                            Func<OrderStatus?, bool> IsActive = st =>
                                st.HasValue && st != OrderStatus.Filled && st != OrderStatus.Canceled;

                            bool entryIsActive = IsActive(entryStatus);
                            bool pullbackIsActive = IsActive(pullbackStatus);

                            // Wenn MINDESTENS eine der Orders noch aktiv ist -> canceln
                            if (entryIsActive || pullbackIsActive)
                            {
                                if (entryIsActive && _entryOrder != null)
                                {
                                    this.LogWarn($"[OnCalculate-Timeout Check] Bar={latestClosedBar} >= timeoutBar={timeoutBarIndex}. Cancelling active entry order Id={_entryOrder.Id}, Status={entryStatus}");
                                    try { CancelOrder(_entryOrder); }
                                    catch (Exception ex) { this.LogWarn($"[OnCalculate-Timeout Check] CancelOrder(_entryOrder) threw: {ex.Message}"); }
                                }

                                if (pullbackIsActive && _pullbackOrder != null)
                                {
                                    this.LogWarn($"[OnCalculate-Timeout Check] Bar={latestClosedBar} >= timeoutBar={timeoutBarIndex}. Cancelling active pullback order Id={_pullbackOrder.Id}, Status={pullbackStatus}");
                                    try { CancelOrder(_pullbackOrder); }
                                    catch (Exception ex) { this.LogWarn($"[OnCalculate-Timeout Check] CancelOrder(_pullbackOrder) threw: {ex.Message}"); }
                                }

                                // Optional: belasse das Nullsetzen den OnOrderChanged Callbacks, die den Cancel best?tigt haben.
                                // Wenn du sofort intern aufr?umen willst (z.B. in Replay/Offline-Modus), kannst du:
                                // _entryOrder = null; _pullbackOrder = null; _entryBarIndex = -1;
                            }
                            else if ((entryStatus == OrderStatus.Canceled) || (pullbackStatus == OrderStatus.Canceled))
                            {
                                // Wenn die Orders bereits gecancelt/rejected sind, stelle interne Felder sicher zur?ck
                                if (_entryOrder != null || _pullbackOrder != null)
                                {
                                    this.LogInfo($"[OnCalculate-Timeout Check] Orders already canceled/rejected by exchange at Bar={latestClosedBar}. Clearing internal references.");
                                    _entryOrder = null;
                                    _pullbackOrder = null;
                                    _entryBarIndex = -1;
                                }
                            }
                            // sonst: Orders sind gef?llt oder in einem finalen/anderen Status ? nichts zu tun
                        }
                    }
                    else
                    {
                        // Falls _entryBarIndex nicht gesetzt oder OrderTimeoutBars nicht > 0, logge optional
                        this.LogDebug($"[OnCalculate-Timeout Check] Skipping timeout calculation - _entryBarIndex={_entryBarIndex}, OrderTimeoutBars={OrderTimeoutBars}");
                    }
                }

                // Optional: Micro-Composite aus dem aktuellen Hist berechnen
                var mc = _currentMC ?? GetRollingMicroComposite();

                if (mc == null)
                {
                    this.LogWarn("OnCalculate-MicroComposite ist nicht verf?gbar (null). Operation wird ?bersprungen oder alternative Logik ausf?hren.");
                }
                else
                {
                    // mc verwenden
                    // z.B. var poc = mc.POC; oder andere Auswertungen
                }




                if (bar != _lastProcessedBarIndex)
                {
                    if (_researchEnabled && _research != null && bar >= 0)
                    {
                        var current = GetCandle(bar);
                        _research.UpdateBar(new ResearchBarInput
                        {
                            BarIndex = bar,
                            Time = current.Time,
                            High = current.High,
                            Low = current.Low,
                            Close = current.Close
                        });
                    }
                }



                var po = _pullbackOrder;       // lokale Momentaufnahme
                if (po == null || _positionOpen) return;

                OrderStates state;
                try
                {
                    state = po.State;          // kann bei gleichzeitiger Disposition/?nderung werfen
                }
                catch
                {
                    return;                    // defensiv: behandle als nicht aktiv
                }

                if (state != OrderStates.Active) return;



                var currentMarketPrice = Security.LastTradePrice;
                if (currentMarketPrice <= 0)  // FIX: Ung?ltiger Preis ? Abbruch
                {
                    this.LogWarn("[OnCalculate] Pullback-Trailing: Ung?ltiger Market-Price, skip.");
                    return;
                }
                bool shouldModifyOrder = false;
                decimal newTrailPrice = 0;

                if (po != null && _pullbackTriggerPrice <= 0m)
                {
                    var poprice = po.Price;
                    if (poprice > 0m)
                    {
                        if (_tickSize > 0m)
                        {
                            var steps = poprice / _tickSize;
                            var aligned =
                                (po.Direction == OrderDirections.Buy)
                                ? Math.Ceiling(steps) * _tickSize
                                : Math.Floor(steps) * _tickSize;

                            _pullbackTriggerPrice = aligned;
                        }
                        else
                        {
                            _pullbackTriggerPrice = poprice;
                        }

                        this.LogInfo($"[OnCalculate] Pullback: Trigger war 0, neu gesetzt auf {_pullbackTriggerPrice} aus Order.Price.");
                    }
                    else
                    {
                        this.LogWarn("[OnCalculate] Pullback: Trigger und Order.Price sind ung?ltig. Skip.");
                        return;
                    }
                }


                if (po.Direction == OrderDirections.Buy)
                {
                    // Hinweis: du nutzt Last, daher Log auf Last anpassen oder Bid/Ask besorgen
                    if (!_isPullbackMode && currentMarketPrice <= _pullbackTriggerPrice + _tickSize)
                    {
                        this.LogInfo($"[OnCalculate] Long Pullback ber?hrt (Last={currentMarketPrice} <= Trigger={_pullbackTriggerPrice}). Wechsle zu trailing StopLimit.");
                        CancelOrder(po);

                        decimal newStopPrice = (decimal)(currentMarketPrice - (PullbackTicksTrail * _tickSize)); // x Ticks unter aktuellem Preis                    
                        _pullbackOrder = CreateStopLimitOrder(OrderDirections.Buy, newStopPrice);
                        _isPullbackMode = true;
                        OpenOrder(_pullbackOrder);
                        return; // wichtig: diesen Tick hier beenden
                    }
                    else if (_isPullbackMode && EnableContinuousTrailing && currentMarketPrice < po.TriggerPrice)
                    {
                        decimal potential = (decimal)currentMarketPrice + (PullbackTicksTrail * _tickSize);
                        if (potential < po.TriggerPrice)
                        {
                            newTrailPrice = potential;
                            shouldModifyOrder = true;
                        }
                    }
                }
                else if (po.Direction == OrderDirections.Sell)
                {
                    if (!_isPullbackMode && currentMarketPrice >= _pullbackTriggerPrice - _tickSize)
                    {
                        this.LogInfo($"[OnCalculate] Short Pullback ber?hrt (Last={currentMarketPrice} >= Trigger={_pullbackTriggerPrice}). Wechsle zu trailing StopLimit.");
                        CancelOrder(po);

                        decimal newStopPrice = (decimal)currentMarketPrice - (PullbackTicksTrail * _tickSize);
                        _pullbackOrder = CreateStopLimitOrder(OrderDirections.Sell, newStopPrice);
                        _isPullbackMode = true;
                        OpenOrder(_pullbackOrder);
                        return; // wichtig
                    }
                    else if (_isPullbackMode && EnableContinuousTrailing && currentMarketPrice > po.TriggerPrice)
                    {
                        decimal potential = (decimal)currentMarketPrice - (PullbackTicksTrail * _tickSize);
                        if (potential > po.TriggerPrice)
                        {
                            newTrailPrice = potential;
                            shouldModifyOrder = true;
                        }
                    }
                }

                if (shouldModifyOrder)
                {
                    this.LogInfo("[OnCalculate-Pullback-Order Trail] Passe Preis an.");
                    try
                    {
                        var modifiedOrder = po.Clone();
                        modifiedOrder.Price = newTrailPrice;
                        modifiedOrder.TriggerPrice = newTrailPrice;
                        ModifyOrder(po, modifiedOrder);
                        _pullbackOrder = modifiedOrder; // Referenz aktualisieren
                    }
                    catch (Exception ex)
                    {
                        var id = po?.Id ?? "N/A";
                        this.LogWarn($"[OnCalculate-Pullback-Order Trail] Fehler beim ?ndern der Pullback-Order ID={id}: {ex.Message}");
                    }
                }
            }
        }

        

        
        private void LogVpsSnapshot(int bar)
        {
            if (_myClusterStatistic == null) return;
            if (!TryGetBarIndices(bar, out var bLive, out var bClosed)) return;

            if (!TryGetCandleSafe(bLive, out var icLive)) return;
            if (!TryGetCandleSafe(bClosed, out var icClosed)) return;

            var sLive = ToSnap(icLive!);
            var sClosed = ToSnap(icClosed!);

            decimal vpsLive = 0m, vpsClosed = 0m, cdClosed = 0m, hClosed = 0m;

            if (_myClusterStatistic.VolPerSecond.Count > bLive)
                vpsLive = _myClusterStatistic.VolPerSecond[bLive];
            if (_myClusterStatistic.VolPerSecond.Count > bClosed)
                vpsClosed = _myClusterStatistic.VolPerSecond[bClosed];
            if (_myClusterStatistic.CumulativeDelta.Count > bClosed)
                cdClosed = _myClusterStatistic.CumulativeDelta[bClosed];
            if (_myClusterStatistic.CandleHeights.Count > bClosed)
                hClosed = _myClusterStatistic.CandleHeights[bClosed];

            var secsLive = GetSecondsForBar(bLive, sLive);
            if (secsLive <= 0) secsLive = 1m;
            var vpsRecLive = sLive.Volume / secsLive;

            if (bLive % 50 == 0)
            {
                var vpsCount = _myClusterStatistic.VolPerSecond.Count;
                var cdCount = _myClusterStatistic.CumulativeDelta.Count;
                var hCount = _myClusterStatistic.CandleHeights.Count;

                this.LogInfo($"[VPS] bar={bar} liveIdx={bLive} closedIdx={bClosed} " +
                         $"vpsLive={vpsLive:F2} vpsClosed={vpsClosed:F2} vpsRecLive={vpsRecLive:F2} " +
                         $"CD(closed)={cdClosed:F0} H(closed)={hClosed:F2} " +
                         $"volLive={sLive.Volume} secsLive={secsLive:F1} hi/lo(closed)={sClosed.High}/{sClosed.Low} " +
                         $"SeriesCounts vps={vpsCount} cd={cdCount} h={hCount}");
            }
        }

        private LevelsSnapshot BuildLevelsSnapshot(IEnumerable<TrackedLevel> trackedLevels)
        {
            var snap = new LevelsSnapshot();
            if (trackedLevels == null) return snap;

            var roundCandidates = new List<decimal>();
            const decimal roundTolerance = 0.0001m;

            // Mapping: exakte Labels -> Assignments
            var map = new Dictionary<string, Action<decimal>>(StringComparer.OrdinalIgnoreCase)
            {
                // Previous day
                ["Tageshoch gestern"] = v => snap.PreviousDayHigh = v,
                ["Tagestief gestern"] = v => snap.PreviousDayLow = v,
                ["Schlusskurs gestern"] = v => snap.PreviousDayClose = v,
                ["Er?ffnungskurs gestern"] = v => snap.PreviousDayOpen = v,
                ["Eroeffnungskurs gestern"] = v => snap.PreviousDayOpen = v,

                // POC / VAH / VAL (inkl. h?ufiger Varianten)
                ["POC gestern"] = v => snap.PreviousDayPOC = v,
                ["POV gestern"] = v => snap.PreviousDayPOC = v, // typo variant
                ["VAH gestern"] = v => snap.PreviousDayVAH = v,
                ["VAL gestern"] = v => snap.PreviousDayVAL = v,

                ["POC aktuell"] = v => snap.CurrentPOC = v,
                ["POC"] = v => snap.CurrentPOC = v,
                ["VAH aktuell"] = v => snap.CurrentVAH = v,
                ["VAH"] = v => snap.CurrentVAH = v,
                ["VAL aktuell"] = v => snap.CurrentVAL = v,
                ["VAL"] = v => snap.CurrentVAL = v,

                // Pivot / S / R
                ["PP"] = v => snap.PP = v,
                ["S1"] = v => snap.S1 = v,
                ["S2"] = v => snap.S2 = v,
                ["S3"] = v => snap.S3 = v,
                ["R1"] = v => snap.R1 = v,
                ["R2"] = v => snap.R2 = v,
                ["R3"] = v => snap.R3 = v,

                // M-levels
                ["M1"] = v => snap.M1 = v,
                ["M2"] = v => snap.M2 = v,
                ["M3"] = v => snap.M3 = v,
                ["M4"] = v => snap.M4 = v,
            };

            foreach (var tl in trackedLevels)
            {
                
                if (tl == null || tl.Value <= 0m)
                {
                    //this.LogInfo($"DEBUG BuildLevelsSnapshot: Skipping Level due to null or zero value: Label='{tl?.Label}', Value={tl?.Value}");
                    continue;
                }
                var raw = (tl.Label ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(raw)) continue;

                // 1) exakte Mappings
                if (map.TryGetValue(raw, out var assign))
                {
                    assign(tl.Value);
                    continue;
                }

                // 2) Pr?fix-Labels: Session High/Low (z. B. "Hoch dd.MM.yy" / "Tief dd.MM.yy")
                if (raw.StartsWith("Hoch ", StringComparison.OrdinalIgnoreCase))
                {
                    snap.SessionHighs.Add(new SessionLevel
                    {
                        Date = tl.LevelDate,
                        Value = tl.Value,
                        Label = raw
                    });
                    continue;
                }
                if (raw.StartsWith("Tief ", StringComparison.OrdinalIgnoreCase))
                {
                    if (!snap.SessionLows.Any(s => s.Date.Date == tl.LevelDate.Date && Math.Abs(s.Value - tl.Value) <= roundTolerance))
                    {
                        snap.SessionLows.Add(new SessionLevel
                        {
                            Date = tl.LevelDate,
                            Value = tl.Value,
                            Label = raw
                        });
                    }
                    continue;
                }

                // 3) Runde Marke: erlaubte Varianten (kann mehrfach vorkommen)
                if (raw.IndexOf("runde Marke", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    raw.Equals("runde Marke", StringComparison.OrdinalIgnoreCase) ||
                    raw.IndexOf("runde Marke (below)", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    raw.IndexOf("runde Marke (above)", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    roundCandidates.Add(tl.Value);
                    continue;
                }

                // 4) Blocker / heuristische Erkennung
                if (raw.IndexOf("block", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    raw.IndexOf("blocker", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Versuche zu unterscheiden: long / short / resistance / support
                    if (raw.IndexOf("long", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        raw.IndexOf("resist", StringComparison.OrdinalIgnoreCase) >= 0) // resistance Hinweise
                    {
                        snap.IsBlockedLong = true;
                        snap.LastBlockResistance = tl.Value;
                    }
                    else if (raw.IndexOf("short", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             raw.IndexOf("support", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        snap.IsBlockedShort = true;
                        snap.LastBlockSupport = tl.Value;
                    }
                    else
                    {
                        // unbestimmt: setze beide optional
                        snap.LastBlockResistance ??= tl.Value;
                        snap.LastBlockSupport ??= tl.Value;
                    }
                    continue;
                }

                // 5) Fallback: extras speichern (raw key, falls noch nicht vorhanden)
                if (!snap.Extra.ContainsKey(raw))
                {
                    snap.Extra[raw] = tl.Value;
                }
            }

            // RoundLevels: falls Kandidaten vorhanden, ordnen -> below/above
            if (roundCandidates.Count > 0)
            {
                roundCandidates.Sort();
                snap.RoundLevelBelow = roundCandidates.First();
                snap.RoundLevelAbove = roundCandidates.Last();
                if (roundCandidates.Count == 1)
                {
                    snap.RoundLevelAbove = snap.RoundLevelBelow;
                }
            }

            // Synchronisiere wichtige typed properties zus?tzlich in Extra (Backwards Compatibility)
            void PutExtraIfPositive(string key, decimal? val)
            {
                if (val.HasValue && val.Value > 0m)
                {
                    snap.Extra[key] = val.Value;
                    snap.Extra[key + "_PRICE"] = val.Value;
                    snap.Extra[key.ToUpperInvariant()] = val.Value;
                }
            }

            PutExtraIfPositive("POC", snap.CurrentPOC > 0m ? snap.CurrentPOC : (snap.PreviousDayPOC > 0m ? snap.PreviousDayPOC : (decimal?)null));
            PutExtraIfPositive("VAH", snap.CurrentVAH > 0m ? snap.CurrentVAH : (snap.PreviousDayVAH > 0m ? snap.PreviousDayVAH : (decimal?)null));
            PutExtraIfPositive("VAL", snap.CurrentVAL > 0m ? snap.CurrentVAL : (snap.PreviousDayVAL > 0m ? snap.PreviousDayVAL : (decimal?)null));
            PutExtraIfPositive("PP", snap.PP > 0m ? snap.PP : (decimal?)null);
            PutExtraIfPositive("R1", snap.R1 > 0m ? snap.R1 : (decimal?)null);
            PutExtraIfPositive("R2", snap.R2 > 0m ? snap.R2 : (decimal?)null);
            PutExtraIfPositive("R3", snap.R3 > 0m ? snap.R3 : (decimal?)null);
            PutExtraIfPositive("S1", snap.S1 > 0m ? snap.S1 : (decimal?)null);
            PutExtraIfPositive("S2", snap.S2 > 0m ? snap.S2 : (decimal?)null);
            PutExtraIfPositive("S3", snap.S3 > 0m ? snap.S3 : (decimal?)null);
            if (snap.RoundLevelBelow.HasValue) PutExtraIfPositive("ROUND_LEVEL_BELOW", snap.RoundLevelBelow);
            if (snap.RoundLevelAbove.HasValue) PutExtraIfPositive("ROUND_LEVEL_ABOVE", snap.RoundLevelAbove);

            // Meta f?r Booleans (optional)
            if (snap.IsBlockedLong) snap.Meta["IS_BLOCKED_LONG"] = "true";
            if (snap.IsBlockedShort) snap.Meta["IS_BLOCKED_SHORT"] = "true";

            // Sort session lists by Date descending (newest first)
            snap.SessionHighs.Sort((a, b) => b.Date.CompareTo(a.Date));
            snap.SessionLows.Sort((a, b) => b.Date.CompareTo(a.Date));

            return snap;
        }


        


                        
        // Lokale Replikation der Regime-Defaults (?ffentlich aufrufbar)
        private static void AdjustRegimeThresholdsLocal(OrderflowThresholds t, MarketRegime regime)
        {
            if (t == null) return;
            switch (regime)
            {
                case MarketRegime.Fast:
                    if (t.ThTradeRateZBreakout.HasValue)
                        t.ThTradeRateZBreakout = Math.Max(0.45m, t.ThTradeRateZBreakout.Value);
                    if (t.ThEfficiency.HasValue)
                        t.ThEfficiency = Math.Max(0.05m, t.ThEfficiency.Value);
                    break;

                case MarketRegime.Slow:
                    if (t.ThVolBurstZ.HasValue && t.ThVolBurstZ.Value > 0m)
                        t.ThVolBurstZ = Math.Max(1.3m, t.ThVolBurstZ.Value);
                    if (t.ThTradeRateZBreakout.HasValue)
                        t.ThTradeRateZBreakout = Math.Max(0.30m, t.ThTradeRateZBreakout.Value);
                    break;

                case MarketRegime.Normal:
                default:
                    break;
            }
        }

        private bool GetCurrentIsTrendRegime()
        {
            var mc = GetRollingMicroComposite();
            if (mc == null || _tickSize <= 0m) return false;

            var c = _lastCalculatedBar >= 0 ? GetCandle(_lastCalculatedBar) : null;
            decimal price = c?.Close ?? 0m;
            if (price <= 0m) return false;

            bool outsideVA = price > mc.VAH || price < mc.VAL;

            // Orderflow-Metriken direkt lesen (kein ?-Operator)
            decimal volBurstZ = ovSnapshot.VolBurstZ;      // z.B. Z-Score des Volumenbursts
            decimal tradeRateZ = ovSnapshot.TradeRateZ;     // z.B. Z-Score der TradeRate
            decimal efficiency01 = ovSnapshot.Efficiency;   // 0..1 Effizienzma?
            decimal cvdImpulse = ovSnapshot.CvdImpulse;     // positiver/negativer Impuls

            // Lokale Schwellen initialisieren
            var th = new OrderflowThresholds
            {
                ThVolBurstZ = 1.0m,
                ThTradeRateZBreakout = 0.40m,
                ThEfficiency = 0.10m,
                ThCvdImpulseLong = 1.00m,
                ThCvdImpulseShort = 1.00m
            };

            // Regime-Defaults anwenden (lokale, ?ffentliche Replikation)
            AdjustRegimeThresholdsLocal(th, _currentRegime);

            // Vergleiche nur decimal mit decimal
            bool volOk = th.ThVolBurstZ.HasValue ? volBurstZ >= th.ThVolBurstZ.Value : volBurstZ >= 1.0m;
            bool trOk = th.ThTradeRateZBreakout.HasValue ? tradeRateZ >= th.ThTradeRateZBreakout.Value : tradeRateZ >= 0.40m;
            bool effOk = th.ThEfficiency.HasValue ? efficiency01 >= th.ThEfficiency.Value : efficiency01 >= 0.10m;

            // CVD-Threshold richtungsabh?ngig w?hlen
            decimal thCvd = cvdImpulse >= 0m
                ? (th.ThCvdImpulseLong ?? 1.0m)
                : (th.ThCvdImpulseShort ?? 1.0m);

            bool cvdOk = Math.Abs(cvdImpulse) >= thCvd;

            // Impuls-Block: mindestens zwei der drei Signale (VolBurst, TradeRate, CVD) + Effizienz
            int impulseHits = 0;
            if (volOk) impulseHits++;
            if (trOk) impulseHits++;
            if (cvdOk) impulseHits++;
            bool impulseStrong = impulseHits >= 2 && effOk;

            // Bar-Range relativ zur Value Area
            int vaSpanTicks = Math.Max(1, TicksBetweenAbs(mc.VAH, mc.VAL));
            int barRangeTicks = Math.Max(0, TicksBetweenAbs(c?.High ?? price, c?.Low ?? price));

            // Regimeabh?ngige Range-Schwelle
            int rangeTh = _currentRegime switch
            {
                MarketRegime.Fast => Math.Max(6, (int)Math.Round(vaSpanTicks * 0.20m, MidpointRounding.AwayFromZero)),
                MarketRegime.Normal => Math.Max(8, (int)Math.Round(vaSpanTicks * 0.25m, MidpointRounding.AwayFromZero)),
                MarketRegime.Slow => Math.Max(10, (int)Math.Round(vaSpanTicks * 0.30m, MidpointRounding.AwayFromZero)),
                _ => Math.Max(8, (int)Math.Round(vaSpanTicks * 0.25m, MidpointRounding.AwayFromZero)),
            };
            bool bigRange = barRangeTicks >= rangeTh;

            // Entscheidungslogik nach Regime
            return _currentRegime switch
            {
                MarketRegime.Fast => outsideVA || impulseStrong || bigRange,
                MarketRegime.Normal => outsideVA || (impulseStrong && bigRange),
                MarketRegime.Slow => outsideVA && (impulseStrong || bigRange),
                _ => outsideVA
            };
        }
        private bool GetCurrentIsInsideValue()
        {
            if (_currentMC == null || _tickSize <= 0m) return false;

            var c = _lastCalculatedBar >= 0 ? GetCandle(_lastCalculatedBar) : null;
            decimal price = c?.Close ?? 0m;
            if (price <= 0m) return false;

            // Sicherstellen, dass val <= vah
            decimal vah = _currentMC.VAH;
            decimal val = _currentMC.VAL;
            if (val > vah)
            {
                var tmp = val;
                val = vah;
                vah = tmp;
            }

            // Toleranz von 1 Tick, um Rauschen zu reduzieren
            decimal tol = _tickSize;

            // Inside Value = Preis zwischen VAL und VAH (inkl. Toleranz)
            return price >= (val - tol) && price <= (vah + tol);
        }


        // Neu: getrennte Gates
        private bool IsOrderflowValidForBounce(bool isLong)
            => isLong ? _ofBounceBullOKLastClosed : _ofBounceBearOKLastClosed;

        private bool IsOrderflowValidForContinuation(bool isLong)
            => isLong ? _ofContBullOKLastClosed : _ofContBearOKLastClosed;


        /// <summary>
        /// Pr?ft, ob der Zeitstempel eines Bars innerhalb der definierten Handelssitzungen liegt.
        /// </summary>
        /// <param name="bar">Der Index des zu pr?fenden Bars.</param>
        /// <returns>True, wenn der Handel erlaubt ist, sonst False.</returns>
        private bool IsWithinStrategyTradingHours(int bar, SetupConfiguration strategyConfig) // Umbenannt, da es jetzt f?r die gesamte Strategie gilt
        {
            //this.LogInfo($"[TimeFilter] enter (bar={bar}, time={GetCandle(bar).Time:O}, enabled={strategyConfig?.EnableTimeFilter})");

            // Ohne Strategie-Konfiguration oder wenn der Zeitfilter nicht aktiviert ist: niemals blockieren
            if (strategyConfig == null || !strategyConfig.EnableTimeFilter)
                return true;

            // 1) Explizit in der Strategie-Konfiguration definierte Sessions haben Priorit?t
            // Diese sind jetzt die "einheitlichen" Handelszeiten f?r die gesamte Strategie.
            if (strategyConfig.TradingSessions != null && strategyConfig.TradingSessions.Count > 0)
            {
                bool withinConfiguredSessions = IsWithinAnyTradingSession(bar, strategyConfig.TradingSessions);
                // Deaktiviere Log-Meldung in Produktivumgebung f?r Performance
                // if (!withinConfiguredSessions)
                //    this.LogInfo($"[TimeFilter] Au?erhalb konfigurierter Strategie-Handelszeiten (bar={bar}, time={GetCandle(bar).Time:O}).");
                return withinConfiguredSessions;
            }

            // 2) Fallback: Wenn in der Strategie-Konfiguration keine spezifischen Sessions definiert sind,
            //    greifen wir auf die globalen (z.B. vom Chart/Instrument kommenden) Sessions zur?ck.
            bool withinGlobal = IsWithinAnyTradingSession(bar); // Verwendet die _globalSessions der Strategie
                                                                //this.LogInfo($"[TimeFilter] using GLOBAL (platform/chart default) -> {withinGlobal} (bar={bar}, time={GetCandle(bar).Time:O})");
            return withinGlobal;
        }

        // Deine bestehenden Hilfsmethoden bleiben, wie sie sind:

        // Hilfs-Overload: Bar -> Zeit (nutzt die _globalSessions deiner Strategie-Klasse)
        private bool IsWithinAnyTradingSession(int bar)
        {
            // Falls keine globalen Sessions gepflegt sind: lieber nicht blockieren
            if (_globalSessions == null || _globalSessions.Count == 0) // _globalSessions ist ein Member deiner Strategie-Klasse
                return true;

            return IsWithinAnyTradingSession(bar, _globalSessions);
        }

        // Kern-Logik zum Pr?fen einer Liste von Sessions
        private bool IsWithinAnyTradingSession(int bar, List<TradingSession> sessions)
        {
            var barTime = GetCandle(bar).Time.TimeOfDay; // Angenommen GetCandle(bar).Time liefert DateTime
            foreach (var session in sessions)
            {
                // Logik zur Pr?fung, ob die barTime in der Session liegt (inkl. ?ber Mitternacht gehende Sessions)
                if (session.EndTime < session.StartTime) // Session geht ?ber Mitternacht
                {
                    if (barTime >= session.StartTime || barTime < session.EndTime)
                    {
                        return true;
                    }
                }
                else // Session geht nicht ?ber Mitternacht
                {
                    if (barTime >= session.StartTime && barTime < session.EndTime)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private decimal FindNextSignificantLevel(decimal entryPrice, OrderDirections direction, LevelsSnapshot levelsSnapshot, decimal currentPOC, decimal currentVAH, decimal currentVAL, bool logCandidates = false)
        {
            if (levelsSnapshot == null)
            {
                this.LogWarn("[LEVEL] levelsSnapshot ist null - verwende Standard-TP");
                return direction == OrderDirections.Buy ? entryPrice + 15 * _tickSize : entryPrice - 15 * _tickSize;
            }

            var significantLevels = new List<(decimal Level, string Source)>();
            
            // Sammle alle relevanten Levels
            if (currentPOC > 0) significantLevels.Add((currentPOC, "CurrentPOC"));
            if (currentVAH > 0) significantLevels.Add((currentVAH, "CurrentVAH"));
            if (currentVAL > 0) significantLevels.Add((currentVAL, "CurrentVAL"));
            
            // F?ge bekannte Level aus levelsSnapshot hinzu
            if (levelsSnapshot.CurrentPOC > 0) significantLevels.Add((levelsSnapshot.CurrentPOC, "Levels.CurrentPOC"));
            if (levelsSnapshot.CurrentVAH > 0) significantLevels.Add((levelsSnapshot.CurrentVAH, "Levels.CurrentVAH"));
            if (levelsSnapshot.CurrentVAL > 0) significantLevels.Add((levelsSnapshot.CurrentVAL, "Levels.CurrentVAL"));
            if (levelsSnapshot.PreviousDayPOC > 0) significantLevels.Add((levelsSnapshot.PreviousDayPOC, "Levels.PrevPOC"));
            if (levelsSnapshot.PreviousDayVAH > 0) significantLevels.Add((levelsSnapshot.PreviousDayVAH, "Levels.PrevVAH"));
            if (levelsSnapshot.PreviousDayVAL > 0) significantLevels.Add((levelsSnapshot.PreviousDayVAL, "Levels.PrevVAL"));
            if (levelsSnapshot.PreviousDayHigh > 0) significantLevels.Add((levelsSnapshot.PreviousDayHigh, "Levels.PrevHigh"));
            if (levelsSnapshot.PreviousDayLow > 0) significantLevels.Add((levelsSnapshot.PreviousDayLow, "Levels.PrevLow"));
            if (levelsSnapshot.PreviousDayClose > 0) significantLevels.Add((levelsSnapshot.PreviousDayClose, "Levels.PrevClose"));
            
            // Pivot-Levels
            if (levelsSnapshot.PP > 0) significantLevels.Add((levelsSnapshot.PP, "Pivot.PP"));
            if (levelsSnapshot.R1 > 0) significantLevels.Add((levelsSnapshot.R1, "Pivot.R1"));
            if (levelsSnapshot.R2 > 0) significantLevels.Add((levelsSnapshot.R2, "Pivot.R2"));
            if (levelsSnapshot.R3 > 0) significantLevels.Add((levelsSnapshot.R3, "Pivot.R3"));
            if (levelsSnapshot.S1 > 0) significantLevels.Add((levelsSnapshot.S1, "Pivot.S1"));
            if (levelsSnapshot.S2 > 0) significantLevels.Add((levelsSnapshot.S2, "Pivot.S2"));
            if (levelsSnapshot.S3 > 0) significantLevels.Add((levelsSnapshot.S3, "Pivot.S3"));
            
            // M-Levels
            if (levelsSnapshot.M1 > 0) significantLevels.Add((levelsSnapshot.M1, "M.M1"));
            if (levelsSnapshot.M2 > 0) significantLevels.Add((levelsSnapshot.M2, "M.M2"));
            if (levelsSnapshot.M3 > 0) significantLevels.Add((levelsSnapshot.M3, "M.M3"));
            if (levelsSnapshot.M4 > 0) significantLevels.Add((levelsSnapshot.M4, "M.M4"));
            
            // Runde Marken
            if (levelsSnapshot.RoundLevelBelow.HasValue && levelsSnapshot.RoundLevelBelow.Value > 0)
                significantLevels.Add((levelsSnapshot.RoundLevelBelow.Value, "Round.Below"));
            if (levelsSnapshot.RoundLevelAbove.HasValue && levelsSnapshot.RoundLevelAbove.Value > 0)
                significantLevels.Add((levelsSnapshot.RoundLevelAbove.Value, "Round.Above"));
            
            // Session Highs/Lows
            foreach (var sessionHigh in levelsSnapshot.SessionHighs.Where(sh => sh.Value > 0))
                significantLevels.Add((sessionHigh.Value, $"SessionHigh.{sessionHigh.Date:yyyyMMdd}"));
            foreach (var sessionLow in levelsSnapshot.SessionLows.Where(sl => sl.Value > 0))
                significantLevels.Add((sessionLow.Value, $"SessionLow.{sessionLow.Date:yyyyMMdd}"));
            
            // Extra-Levels (beliebige zus?tzliche Levels)
            foreach (var extraLevel in levelsSnapshot.Extra.Where(kv => kv.Value > 0))
                significantLevels.Add((extraLevel.Value, $"Extra.{extraLevel.Key}"));

            // Tracked Levels (wie IsBlocked/OnRender)
            if (_untouchedLevels != null && _untouchedLevels.Count > 0)
            {
                foreach (var level in _untouchedLevels.Where(l => l != null && l.IsActive && l.Value > 0m))
                {
                    significantLevels.Add((level.Value, $"Tracked.{level.Label}"));
                }
            }
            
            // NEU: MicroComposite Levels (falls verfügbar)
            var mcForLevels = _currentMC ?? GetRollingMicroComposite();
            if (mcForLevels != null)
            {
                // Dynamischer POC, VAH, VAL aus MicroComposite
                if (mcForLevels.POC > 0)
                    significantLevels.Add((mcForLevels.POC, "MC.POC"));
                if (mcForLevels.VAH > 0)
                    significantLevels.Add((mcForLevels.VAH, "MC.VAH"));
                if (mcForLevels.VAL > 0)
                    significantLevels.Add((mcForLevels.VAL, "MC.VAL"));
                
                // HVN/LVN Zonen (nur HVN-Zonen) -> Kanten nutzen (entspricht gerenderten Rechtecken)
                foreach (var hvnZone in mcForLevels.HVNZones)
                {
                    if (hvnZone.Start > 0) significantLevels.Add((hvnZone.Start, "MC.HVNZone.Start"));
                    if (hvnZone.End > 0) significantLevels.Add((hvnZone.End, "MC.HVNZone.End"));
                }
                
                // LVN (Low Volume Nodes) werden entfernt - nur HVN f?r TP-Berechnung verwenden
                
                // LVN-Zonen werden entfernt
                
                this.LogDebug($"[LEVEL] MicroComposite hinzugef?gt: POC={_currentMC.POC:F2}, VAH={_currentMC.VAH:F2}, VAL={_currentMC.VAL:F2}, " +
                             $"HVNs={_currentMC.HVNs.Count}, LVNs={_currentMC.LVNs.Count}");
            }

            // Filtere und sortiere Levels basierend auf Richtung
            if (logCandidates)
            {
                this.LogInfo($"[LEVEL] Kandidaten ({direction}) vor Filter: " +
                              string.Join(" | ", significantLevels.Select(l => $"{l.Level:F2}:{l.Source}")));
            }

            var relevantLevels = direction == OrderDirections.Buy
                ? significantLevels.Where(level => level.Level > entryPrice).OrderBy(level => level.Level).ToList()
                : significantLevels.Where(level => level.Level < entryPrice).OrderByDescending(level => level.Level).ToList();

            if (relevantLevels.Count == 0)
            {
                this.LogInfo($"[LEVEL] Kein signifikantes Level gefunden f?r {direction} (TP) bei {entryPrice:F2} - verwende Standard-15 Ticks");
            }

            var nextLevel = relevantLevels.First();
            var tpPrice = direction == OrderDirections.Buy 
                ? nextLevel.Level - _tickSize  // Ein Tick unter dem Level f?r Long
                : nextLevel.Level + _tickSize;  // Ein Tick ?ber dem Level f?r Short

            // Bestimme die Art des Levels f?r besseres Logging
            string levelType = "Unbekannt";
            if (mcForLevels != null)
            {
                if (Math.Abs(nextLevel.Level - mcForLevels.POC) < _tickSize * 2) levelType = "MC-POC";
                else if (Math.Abs(nextLevel.Level - mcForLevels.VAH) < _tickSize * 2) levelType = "MC-VAH";
                else if (Math.Abs(nextLevel.Level - mcForLevels.VAL) < _tickSize * 2) levelType = "MC-VAL";
                else if (mcForLevels.HVNs.Any(h => Math.Abs(nextLevel.Level - h) < _tickSize * 2)) levelType = "MC-HVN";
                else if (mcForLevels.LVNs.Any(l => Math.Abs(nextLevel.Level - l) < _tickSize * 2)) levelType = "MC-LVN";
                else if (mcForLevels.HVNZones.Any(z => nextLevel.Level >= z.Start && nextLevel.Level <= z.End)) levelType = "MC-HVN-Zone";
                else if (mcForLevels.LVNZones.Any(z => nextLevel.Level >= z.Start && nextLevel.Level <= z.End)) levelType = "MC-LVN-Zone";
            }
            
            if (levelType == "Unbekannt")
            {
                // Pr?fe statische Levels
                if (Math.Abs(nextLevel.Level - currentPOC) < _tickSize * 2) levelType = "POC";
                else if (Math.Abs(nextLevel.Level - currentVAH) < _tickSize * 2) levelType = "VAH";
                else if (Math.Abs(nextLevel.Level - currentVAL) < _tickSize * 2) levelType = "VAL";
                else if (Math.Abs(nextLevel.Level - levelsSnapshot.PP) < _tickSize * 2) levelType = "PP";
                else if (Math.Abs(nextLevel.Level - levelsSnapshot.R1) < _tickSize * 2) levelType = "R1";
                else if (Math.Abs(nextLevel.Level - levelsSnapshot.S1) < _tickSize * 2) levelType = "S1";
                else if (levelsSnapshot.SessionHighs.Any(sh => Math.Abs(nextLevel.Level - sh.Value) < _tickSize * 2)) levelType = "Session-High";
                else if (levelsSnapshot.SessionLows.Any(sl => Math.Abs(nextLevel.Level - sl.Value) < _tickSize * 2)) levelType = "Session-Low";
            }

            this.LogInfo($"[LEVEL] N?chstes signifikantes Level f?r {direction} (TP): {nextLevel.Level:F2} ({levelType}), TP bei {tpPrice:F2} (Dynamic), Source={nextLevel.Source}");
            return tpPrice;
        }

        private void EvaluateSignalsAndOrders(int closed, OvSnapshot ovLastClosed, Orderflow.OfFeaturesHistory ofFeaturesHistory, bool isBlockedLong, bool isBlockedShort, LevelsSnapshot levelsSnapshot, decimal currentPOC_Explicit, decimal currentVAH_Explicit, decimal currentVAL_Explicit, bool vwSignal, int vwEntryDir, decimal vwEntryPrice, MarketRegime currentMarketRegime, DetectedOrderflowPattern detectedPattern, MarketState currentMarketState)
        {
            //this.LogInfo($"[DBG-EVAL] Start EvaluateSignalsAndOrders(closed={closed}) at {DateTime.UtcNow:O}");

            if (closed < 0 || closed >= CurrentBar) return;
            //this.LogInfo($"[DBG] Pre-Eval bar={bar}, hasZ=true, Z={Volumenburst:F6}, CurrentBar={CurrentBar}");

            var c = GetCandle(closed);
            var p = closed > 0 ? GetCandle(closed - 1) : c;
            double seconds = (c.Time - p.Time).TotalSeconds;
            if (seconds <= 0) seconds = 1;
            //this.LogInfo($"[Eval] bar={bar}, time={c.Time:O}");
            bool inSession = IsWithinAnyTradingSession(closed);
            var input = null as ResearchBarInput;
            var tickSize = InstrumentInfo?.TickSize ?? _tickSize;
            int riskTicks = GetRiskTicks();
            bool wegFreiLong = true;
            bool wegFreiShort = true;
            string wegFreiLongBlocker = string.Empty;
            string wegFreiShortBlocker = string.Empty;
            
            if (EnableMicroCompositeSystem)
            {
                var mcForPath = _currentMC ?? GetRollingMicroComposite();
                wegFreiLong = IsPathFreeLongEnhanced(c.Close, DMinTicks, riskTicks, mcForPath, null, tickSize, GetPathConfig(), out wegFreiLongBlocker);
                wegFreiShort = IsPathFreeShortEnhanced(c.Close, DMinTicks, riskTicks, mcForPath, null, tickSize, GetPathConfig(), out wegFreiShortBlocker);
            }



            // Debug TradingManager Orders
            var tmOrders = TradingManager.Orders.ToList();

            foreach (var tmOrder in tmOrders)
            {
                //this.LogInfo($"[DEBUG-TM] Order: {tmOrder.Direction} {tmOrder.Type} {tmOrder.State} @ {tmOrder.Price}");
            }


            CleanupInactiveOrders();


            var t = GetCandle(closed).Time;
            // this.LogInfo($"[EVAL] enter bar={bar}, time={t:O}, liveStart={_liveStartBar}, onlyFromLive={_onlyFromLive}");              


            if (_entryLogicLockedUntilLive)
            {
                this.LogInfo($"[EvaluateSignalAndOrders-Eval-Guard] lockedUntilLive: skip (bar={closed})");
                return;
            }


            //this.LogInfo($"[EVAL] timeCheck inSession={inSession}");
            if (!inSession)
            {
                this.LogInfo("[EVAL] guard: outside session");
                return;
            }

            // 1) Pending-Orders timeouten
            HandlePendingTimeouts(closed);
            //this.LogInfo("[Eval] Pending timeouts handled");
            // 2) Weg-Frei Checks (MC / DMinTicks)              



            // 2. Pr?fen des Trading Managers (grundlegende Absicherung)
            if (TradingManager == null)
            {
                this.LogInfo($"[EvaluateSignalAndOrders] TradingManager ist null!");
                return;
            }

            // 3. Pr?fen auf ausreichend historische Kerzen
            if (closed < 2)
            {
                return;
            }

            // 4. Konsolidierte Pr?fung auf offene Positionen oder aktive/pendente Orders.
            //    Dies ist kritisch, um Mehrfach-Trades zu vermeiden, wenn die Strategie nur einen Trade gleichzeitig erlaubt.
            bool hasOpenPosition = CurrentPosition != 0; // Ihre aktuelle Position
            bool hasActiveTradingManagerOrders = TradingManager.Orders.Any(o => o.State == OrderStates.Active); // Generische aktive Orders
            bool hasPendingEntryOrders = false;

            if (_pullbackOrder != null)
            {
                // Nur Active Orders blockieren neue Trades
                hasPendingEntryOrders = (_pullbackOrder.State == OrderStates.Active);

                // ? BEREINIGE INAKTIVE ORDERS (None, Done, Failed)
                if ((_pullbackOrder.State == OrderStates.None ||
                     _pullbackOrder.State == OrderStates.Done ||
                     _pullbackOrder.State == OrderStates.Failed) &&
                     !_isExitPlacementPending)  // MINIMALER FIX: Sch?tze bei Pending (Trade l?uft)
                {
                    this.LogInfo($"[Cleanup] Removing inactive Pullback Order: {_pullbackOrder.Direction} {_pullbackOrder.State} (State reset skipped, da Pending=true)");
                    _pullbackOrder = null;
                    // KEINEN Reset von _activeTradeSetupParams hier (Pullback-spezifisch; falls vorhanden, f?ge hinzu: && !_isExitPlacementPending)
                }
                else if (_isExitPlacementPending)
                {
                    this.LogDebug("[Cleanup] Skipped Pullback-Reset: Pending Trade aktiv (TP/SL wartet).");
                }
            }

            if (_entryOrder != null && !hasPendingEntryOrders)
            {
                // Nur Active Orders blockieren neue Trades
                hasPendingEntryOrders = (_entryOrder.State == OrderStates.Active);

                // ? BEREINIGE INAKTIVE ORDERS (None, Done, Failed)
                if ((_entryOrder.State == OrderStates.None ||
                     _entryOrder.State == OrderStates.Done ||
                     _entryOrder.State == OrderStates.Failed) &&
                     !_isExitPlacementPending)  // MINIMALER FIX: Sch?tze bei Pending (Trade l?uft nach Fill)
                {
                    this.LogInfo($"[Cleanup] Removing inactive Entry Order: {_entryOrder.Direction} {_entryOrder.State}");
                    _entryOrder = null;
                    _activeTradeSetupParams = null;  // Bleibt, aber nur bei !Pending
                    _entryBarIndex = -1;
                }
                else if (_isExitPlacementPending)
                {
                    this.LogDebug("[Cleanup] Skipped Entry-Reset: Pending Trade aktiv (TP/SL wartet nach Fill).");
                }
            }

            bool hasPendingExitOrders = false;

            if (_tpOrder != null)
            {
                hasPendingExitOrders = (_tpOrder.State == OrderStates.Active);

                // Bereinige inaktive TP Orders
                if ((_tpOrder.State == OrderStates.None ||
                     _tpOrder.State == OrderStates.Done ||
                     _tpOrder.State == OrderStates.Failed) &&
                     !_isExitPlacementPending)  // MINIMALER FIX: Sch?tze w?hrend Trade
                {
                    this.LogInfo($"[Cleanup] Removing inactive TP Order: {_tpOrder.State}");
                    _tpOrder = null;
                    // Optional: Wenn TP-Reset Params betrifft, f?ge hier _activeTradeSetupParams = null; hinzu (mit !Pending)
                }
                else if (_isExitPlacementPending)
                {
                    this.LogDebug("[Cleanup] Skipped TP-Reset: Pending Trade aktiv.");
                }
            }

            if (_slOrder != null && !hasPendingExitOrders)
            {
                hasPendingExitOrders = (_slOrder.State == OrderStates.Active);

                // Bereinige inaktive SL Orders
                if ((_slOrder.State == OrderStates.None ||
                     _slOrder.State == OrderStates.Done ||
                     _slOrder.State == OrderStates.Failed) &&
                     !_isExitPlacementPending)  // MINIMALER FIX: Sch?tze w?hrend Trade
                {
                    this.LogInfo($"[Cleanup] Removing inactive SL Order: {_slOrder.State}");
                    _slOrder = null;
                    // Optional: Reset-Logik falls n?tig
                }
                else if (_isExitPlacementPending)
                {
                    this.LogDebug("[Cleanup] Skipped SL-Reset: Pending Trade aktiv.");
                }
            }

            if (hasOpenPosition || hasActiveTradingManagerOrders || hasPendingEntryOrders || hasPendingExitOrders)
            {
                this.LogInfo($"[GUARD] Skip: Pos={CurrentPosition}, TMOrders={hasActiveTradingManagerOrders}, " +
                             $"EntryOrders={hasPendingEntryOrders}, ExitOrders={hasPendingExitOrders}");
                return;
            }
            //this.LogInfo($"[Eval] bar={bar}, time={GetCandle(bar).Time:O}");

            //this.LogInfo($"[DEBUG-GUARDS] " + $"hasOpenPosition: {hasOpenPosition} " + $"hasActiveTMOrders: {hasActiveTradingManagerOrders} " + $"hasPendingEntryOrders: {hasPendingEntryOrders} " + $"hasPendingExitOrders: {hasPendingExitOrders}");

            // ? ZEIGE WELCHER GUARD BLOCKIERT
            if (hasOpenPosition)
            {
                this.LogInfo($"[GUARD-BLOCK] BLOCKED BY OPEN POSITION: {CurrentPosition}");
                return;
            }

            if (hasActiveTradingManagerOrders)
            {
                this.LogInfo($"[GUARD-BLOCK] BLOCKED BY ACTIVE TM ORDERS");
                return;
            }

            if (hasPendingEntryOrders)
            {
                this.LogInfo($"[GUARD-BLOCK] BLOCKED BY PENDING ENTRY ORDERS");
                return;
            }

            if (hasPendingExitOrders)
            {
                this.LogInfo($"[GUARD-BLOCK] BLOCKED BY PENDING EXIT ORDERS");
                return;
            }

            // ? WENN WIR HIER SIND, SIND ALLE GUARDS OK
            //this.LogInfo($"[DEBUG] ALL GUARDS PASSED - PROCEEDING WITH SIGNAL EVALUATION");     

            // ========== SCHRITT 4: TRADE-SETUP VALIDIERUNG (Long/Short - robust) ==========
            // Konfigurierbare Faktoren (lade idealerweise aus Strategy-Config/_thresholdManager)
            decimal marketStatePerfectMatchFactor = 1.0m;    // Keine Anpassung
            decimal marketStateGoodMatchFactor = 0.8m;       // Leichte Abwertung
            decimal marketStateAcceptableMatchFactor = 0.6m; // Deutlichere Abwertung (optional)
            decimal marketStateRiskyMatchFactor = 0.4m;      // Hohe Abwertung
            decimal marketStateNoMatchFactor = 0.01m;       // Niemals 0 ? vermeide komplettes Hard-Blocking

            // Weitere Schutz-/Policy-Parameter (konfigurierbar)
            decimal minFactorFloor = 0.01m;                    // Absoluter Floor (niemals 0)
            decimal strongConfidenceOverrideThreshold = 0.80m; // Ab hier evtl. Override statt voller Block

            bool isLongSetupValid = false;
            bool isShortSetupValid = false;

            // Grundlegende Pr?fung f?r alle erkannten Muster
            if (detectedPattern.IsDetected && detectedPattern.ConfidenceScore >= 0.30m)
            {
                decimal rawConfidence = detectedPattern.ConfidenceScore;
                decimal factor = 1m;

                // Defensive: sichere Faktoren-Funktion
                Func<decimal, decimal> safe = x => (x <= 0m) ? minFactorFloor : x;

                // Normalize input factors (optional, sch?tzt vor 0/negativen Werten)
                marketStatePerfectMatchFactor = safe(marketStatePerfectMatchFactor);
                marketStateGoodMatchFactor = safe(marketStateGoodMatchFactor);
                marketStateAcceptableMatchFactor = safe(marketStateAcceptableMatchFactor);
                marketStateRiskyMatchFactor = safe(marketStateRiskyMatchFactor);
                marketStateNoMatchFactor = safe(marketStateNoMatchFactor);

                // Bestimme factor basierend auf Direction + Pattern + Market Bias
                if (detectedPattern.Direction == OrderDirections.Buy)
                {
                    switch (detectedPattern.Type)
                    {
                        case OrderflowPatternType.PotentialLongTrendContinuation:
                        case OrderflowPatternType.PotentialLongPullback:
                            if (currentMarketState.DirectionalBias == MarketDirectionalBias.BullishTrend)
                                factor = marketStatePerfectMatchFactor;
                            else if (currentMarketState.DirectionalBias == MarketDirectionalBias.Sideways ||
                                     currentMarketState.DirectionalBias == MarketDirectionalBias.Choppy)
                                factor = marketStateGoodMatchFactor;
                            else
                                factor = marketStateNoMatchFactor; // BearishTrend -> contra
                            break;

                        case OrderflowPatternType.PotentialLongBreakout:
                            if (currentMarketState.DirectionalBias == MarketDirectionalBias.Sideways)
                                factor = marketStatePerfectMatchFactor;
                            else if (currentMarketState.DirectionalBias == MarketDirectionalBias.Choppy)
                                factor = marketStateGoodMatchFactor;
                            else
                                factor = marketStateNoMatchFactor;
                            break;

                        case OrderflowPatternType.PotentialLongReversalBounce:
                            // ReversalLong in BullishTrend ist oft ein legitimer Bounce -> moderate Behandlung
                            if (currentMarketState.DirectionalBias == MarketDirectionalBias.Sideways ||
                                currentMarketState.DirectionalBias == MarketDirectionalBias.Choppy)
                                factor = marketStatePerfectMatchFactor;
                            else if (currentMarketState.DirectionalBias == MarketDirectionalBias.BearishTrend)
                                factor = marketStateRiskyMatchFactor; // Counter-Trend, riskant
                            else if (currentMarketState.DirectionalBias == MarketDirectionalBias.Undefined)
                                factor = marketStateRiskyMatchFactor; // unsicher
                            else if (currentMarketState.DirectionalBias == MarketDirectionalBias.BullishTrend)
                                factor = marketStateGoodMatchFactor; // Bounce in Trend -> allow (nicht hart blocken)
                            break;

                        default:
                            factor = marketStateNoMatchFactor;
                            this.LogWarn($"[ADJ-CONF] Unknown Long PatternType: {detectedPattern.Type}. Applying no-match factor.");
                            break;
                    }
                }
                else if (detectedPattern.Direction == OrderDirections.Sell)
                {
                    switch (detectedPattern.Type)
                    {
                        case OrderflowPatternType.PotentialShortTrendContinuation:
                        case OrderflowPatternType.PotentialShortPullback:
                            if (currentMarketState.DirectionalBias == MarketDirectionalBias.BearishTrend)
                                factor = marketStatePerfectMatchFactor;
                            else if (currentMarketState.DirectionalBias == MarketDirectionalBias.Sideways ||
                                     currentMarketState.DirectionalBias == MarketDirectionalBias.Choppy)
                                factor = marketStateGoodMatchFactor;
                            else
                                factor = marketStateNoMatchFactor; // BullishTrend -> contra
                            break;

                        case OrderflowPatternType.PotentialShortBreakout:
                            if (currentMarketState.DirectionalBias == MarketDirectionalBias.Sideways)
                                factor = marketStatePerfectMatchFactor;
                            else if (currentMarketState.DirectionalBias == MarketDirectionalBias.Choppy)
                                factor = marketStateGoodMatchFactor;
                            else
                                factor = marketStateNoMatchFactor;
                            break;

                        case OrderflowPatternType.PotentialShortReversalBounce:
                            if (currentMarketState.DirectionalBias == MarketDirectionalBias.Sideways ||
                                currentMarketState.DirectionalBias == MarketDirectionalBias.Choppy)
                                factor = marketStatePerfectMatchFactor;
                            else if (currentMarketState.DirectionalBias == MarketDirectionalBias.BullishTrend)
                                factor = marketStateRiskyMatchFactor; // Counter-Trend
                            else if (currentMarketState.DirectionalBias == MarketDirectionalBias.Undefined)
                                factor = marketStateRiskyMatchFactor;
                            else if (currentMarketState.DirectionalBias == MarketDirectionalBias.BearishTrend)
                                factor = marketStateGoodMatchFactor; // Bounce in Trend -> allow
                            break;

                        default:
                            factor = marketStateNoMatchFactor;
                            this.LogWarn($"[ADJ-CONF] Unknown Short PatternType: {detectedPattern.Type}. Applying no-match factor.");
                            break;
                    }
                }
                else
                {
                    // Unknown direction ? sehr defensiv
                    factor = marketStateNoMatchFactor;
                    this.LogWarn($"[ADJ-CONF] Unknown Pattern Direction: {detectedPattern.Direction}. Applying no-match factor.");
                }

                // High-confidence override: starke raw-Signale nicht komplett wegdr?cken
                if (rawConfidence >= strongConfidenceOverrideThreshold && factor <= minFactorFloor * 2m)
                {
                    factor = Math.Max(factor, marketStateRiskyMatchFactor);
                    this.LogInfo($"[ADJ-CONF] Strong rawConfidence override applied: raw={rawConfidence:F2}, newFactor={factor:F2}");
                }

                // Anwenden Floor und Berechnung
                decimal appliedFactor = Math.Max(factor, minFactorFloor);
                decimal adjustedConfidence = rawConfidence * appliedFactor;

                // Einheitliches, vollst?ndiges Logging
                this.LogInfo($"[ADJ-CONF] Pattern={detectedPattern.Type}, Dir={detectedPattern.Direction}, Bias={currentMarketState.DirectionalBias}, " +
                             $"Raw={rawConfidence:F2}, Factor={factor:F4}, AppliedFactor={appliedFactor:F4}, Adjusted={adjustedConfidence:F4}");

                // Final pr?fen gegen Threshold (kann adaptiv sein)
                if (adjustedConfidence >= 0.30m)
                {
                    if (detectedPattern.Direction == OrderDirections.Buy)
                    {
                        isLongSetupValid = true;
                        this.LogInfo($"[SETUP-LONG] Long Setup valid (adjusted confidence): Pattern={detectedPattern.Type}, Bias={currentMarketState.DirectionalBias}, FinalConfidence={adjustedConfidence:F2}");
                    }
                    else
                    {
                        isShortSetupValid = true;
                        this.LogInfo($"[SETUP-SHORT] Short Setup valid (adjusted confidence): Pattern={detectedPattern.Type}, Bias={currentMarketState.DirectionalBias}, FinalConfidence={adjustedConfidence:F2}");
                    }
                }
                else
                {
                    this.LogInfo($"[SETUP-BLOCKED] Pattern {detectedPattern.Type} with bias {currentMarketState.DirectionalBias} blocked due to low adjusted confidence ({adjustedConfidence:F2})");
                }
            }

            // ========== ZUSÄTZLICHER CHECK: isBlockedLong/isBlockedShort (Schritt 4 Fortsetzung) ==========
            if (EnableIsBlocked) // Prüfen, ob die Blockade-Funktion aktiviert ist
            {
                if (isLongSetupValid && isBlockedLong)
                {
                    isLongSetupValid = false; // Blockiert das Long-Setup
                    this.LogInfo($"[SETUP-LONG-BLOCKED] Long Setup blockiert, da 'isBlockedLong' TRUE ist (ProximityTicksForEntry: {ProximityTicksForEntry}).");
                }

                if (isShortSetupValid && isBlockedShort)
                {
                    isShortSetupValid = false; // Blockiert das Short-Setup
                    this.LogInfo($"[SETUP-SHORT-BLOCKED] Short Setup blockiert, da 'isBlockedShort' TRUE ist (ProximityTicksForEntry: {ProximityTicksForEntry}).");
                }
            }

            // ========== WEG-FREI CHECK (MC System) ==========
            if (EnableMicroCompositeSystem)
            {
                if (isLongSetupValid && !wegFreiLong)
                {
                    isLongSetupValid = false;
                    this.LogInfo($"[SETUP-LONG-BLOCKED] Long Setup blockiert, da Weg nicht frei (DMinTicks: {DMinTicks}).");
                }

                if (isShortSetupValid && !wegFreiShort)
                {
                    isShortSetupValid = false;
                    this.LogInfo($"[SETUP-SHORT-BLOCKED] Short Setup blockiert, da Weg nicht frei (DMinTicks: {DMinTicks}).");
                }
            }

            // ========== COMBINED BLOCK CHECK LOG ==========
            // Log-Zeile erstellen, wenn Abstand zu wenig und Entry blockiert wird
            if (EnableIsBlocked && EnableMicroCompositeSystem)
            {
                if (detectedPattern.Type == OrderflowPatternType.None)
                {
                    this.LogInfo($"[SETUP-DIR] Pattern=None -> Setup-Blocker übersprungen (Bar={closed}).");
                    return;
                }
                bool isLongSetup = detectedPattern.Direction == OrderDirections.Buy;
                bool isShortSetup = detectedPattern.Direction == OrderDirections.Sell;
                var setupLabel = isLongSetup ? "Long" : isShortSetup ? "Short" : "Unknown";

                this.LogInfo($"[SETUP-DIR] Pattern={detectedPattern.Type} Dir={detectedPattern.Direction} Setup={setupLabel} Bar={closed}");

                bool blockedByIsBlocked = isLongSetup ? isBlockedLong : isShortSetup ? isBlockedShort : false;
                bool blockedByWegFrei = isLongSetup ? !wegFreiLong : isShortSetup ? !wegFreiShort : false;

                if (blockedByIsBlocked || blockedByWegFrei)
                {
                    var blockerInfo = string.Empty;
                    if (blockedByWegFrei)
                    {
                        var wegFreiBlocker = isLongSetup ? wegFreiLongBlocker : isShortSetup ? wegFreiShortBlocker : string.Empty;
                        if (!string.IsNullOrWhiteSpace(wegFreiBlocker) &&
                            (wegFreiBlocker.StartsWith("MC.POC", StringComparison.OrdinalIgnoreCase) ||
                             wegFreiBlocker.StartsWith("MC.VAH", StringComparison.OrdinalIgnoreCase) ||
                             wegFreiBlocker.StartsWith("MC.VAL", StringComparison.OrdinalIgnoreCase) ||
                             wegFreiBlocker.StartsWith("MC.HVN", StringComparison.OrdinalIgnoreCase)))
                        {
                            blockerInfo += $", WegFreiBlocker={wegFreiBlocker}";
                        }
                        else if (!string.IsNullOrWhiteSpace(wegFreiBlocker) &&
                                 wegFreiBlocker.StartsWith("LVN_PATH", StringComparison.OrdinalIgnoreCase))
                        {
                            blockerInfo += $", WegFreiReason={wegFreiBlocker}";
                        }
                    }
                    if (blockedByIsBlocked)
                    {
                        if (isLongSetup && levelsSnapshot?.LastBlockResistance.HasValue == true)
                        {
                            blockerInfo += $", IsBlockedLevel={levelsSnapshot.LastBlockResistance.Value:F2} (Resistance)";
                            blockerInfo += ", IsLongBlocker=True";
                        }
                        if (isShortSetup && levelsSnapshot?.LastBlockSupport.HasValue == true)
                        {
                            blockerInfo += $", IsBlockedLevel={levelsSnapshot.LastBlockSupport.Value:F2} (Support)";
                            blockerInfo += ", IsShortBlocker=True";
                        }
                    }
                    this.LogInfo(
                        $"[SETUP-BLOCKED] Blocker aktiv (blockiert, wenn min einer TRUE): Setup={setupLabel}, " +
                        $"IsBlocked={blockedByIsBlocked}, WegFrei={!blockedByWegFrei}, " +
                        $"BlockedByIsBlocked={blockedByIsBlocked}, BlockedByWegFrei={blockedByWegFrei}, " +
                        $"ProximityTicksForEntry={ProximityTicksForEntry}, DMinTicks={DMinTicks}{blockerInfo}"
                    );
                }
            }

            // Debug-Ausgaben nach der Setup-Validierung
            if (isLongSetupValid)
            {
                this.LogInfo($"[SETUP-VALID] Ein g?ltiges Long-Setup wurde erkannt! Weiter mit zus?tzlichen Checks...");
            }
            else if (isShortSetupValid) // Verwende else if, da nur ein Setup gleichzeitig g?ltig sein sollte
            {
                this.LogInfo($"[SETUP-VALID] Ein g?ltiges Short-Setup wurde erkannt! Weiter mit zus?tzlichen Checks...");
            }
            else
            {
                this.LogDebug("[SETUP-VALID] Derzeit kein g?ltiges Long- oder Short-Setup nach Pattern/Bias-Kriterien.");
            }



            // ========== SCHRITT 5: ORDERPLATZIERUNG ==========
            if (isLongSetupValid)
            {
                decimal longEntryPrice = 0;
                var setup = new SetupParams(); // Standard-Setup-Parameter
                string entryTriggerLevelName = string.Empty;
                decimal entryTriggerLevel = 0;

                // Sicherstellen, dass levelsSnapshot nicht null ist, bevor darauf zugegriffen wird
                if (levelsSnapshot == null)
                {
                    this.LogWarn("[ORDER-LONG] levelsSnapshot ist null. Kann keinen Einstiegspreis bestimmen.");
                    return;
                }

                switch (detectedPattern.Type)
                {
                    case OrderflowPatternType.PotentialLongTrendContinuation:
                    case OrderflowPatternType.PotentialLongPullback:
                        // F?r Pullbacks/Trend-Continuation: Limit-Order etwas unter VAL
                        // ACHTUNG: Die Offset-Werte (z.B. -1 oder -3 Ticks) sollten in den Strategie-Parametern definierbar sein.
                        longEntryPrice = c.Close - 1 * tickSize;
                        entryTriggerLevelName = "VAL";
                        entryTriggerLevel = levelsSnapshot.CurrentVAL;
                        // Setup-Parameter f?r Trend/Pullback Long
                        setup = new SetupParams
                        {
                            TpTicks = 15, // Beispiel: Gr??erer TP f?r Trend-Trades
                            SlTicks = 8,
                            BreakEvenLevelsTrendConfig = "7:1;15:5",
                            TrailType = "CANDLE_HL",
                            TrailActivateAfterTicks = 5,
                            EarlyExitLevels = "POC",
                            ProximityTicksForExit = 1m,
                            EarlyExitMaxDistTicks = 50m,
                            EarlyExitCloseAtLevel = false,
                        };
                        break;

                    case OrderflowPatternType.PotentialLongBreakout:
                        // F?r Breakouts: 
                        longEntryPrice = p.Close - 1 * tickSize;
                        entryTriggerLevelName = "VAH";
                        entryTriggerLevel = levelsSnapshot.CurrentVAH;
                        // Setup-Parameter f?r Breakout Long
                        setup = new SetupParams
                        {
                            TpTicks = 10, // Beispiel: Kleinerer TP, schnelle Gewinne
                            SlTicks = 5,
                            BreakEvenLevelsTrendConfig = "3:1;8:3",
                            TrailType = "FIXED_TICKS", // Beispiel: Fester Tick-Trail
                            TrailActivateAfterTicks = 2,
                            EarlyExitLevels = "VAH", // Oder ein anderes Level
                            ProximityTicksForExit = 0.5m,
                            EarlyExitMaxDistTicks = 30m,
                            EarlyExitCloseAtLevel = true, // Beispiel: Limit am Level
                        };
                        break;

                    case OrderflowPatternType.PotentialLongReversalBounce:
                        
                        longEntryPrice = c.Close + 1 * tickSize;
                        
                        // Berechne dynamischen TP basierend auf n?chstem signifikanten Level
                        var dynamicTpLevel = FindNextSignificantLevel(longEntryPrice, OrderDirections.Buy, levelsSnapshot, currentPOC_Explicit, currentVAH_Explicit, currentVAL_Explicit, logCandidates: true);
                        var dynamicTpTicks = (dynamicTpLevel - longEntryPrice) / tickSize;
                        
                        // Setup-Parameter f?r Reversal Long mit dynamischem TP und festem SL
                        setup = new SetupParams
                        {
                            TpType = "Vorgeschlagen", // NEU: TP-Typ setzen
                            SlTicks = 8, // Feste 8 Ticks SL
                            BreakEvenLevelsTrendConfig = "6:1;10:4",
                            TrailType = "CANDLE",
                            TrailActivateAfterTicks = 4,
                            EarlyExitLevels = "", // Mehrere Levels m?glich
                            ProximityTicksForExit = 1m,
                            EarlyExitMaxDistTicks = 40m,
                            EarlyExitCloseAtLevel = false,
                            
                            // NEU: Dynamische Level-TP Konfiguration
                            UseDynamicLevelTp = true,
                            DynamicLevelTpOffsetTicks = 1m,
                            SuggestedTargetPrice = dynamicTpLevel,
                            MaxDynamicTpDistanceTicks = 12m // NEU: Maximale Distanz auf 12 Ticks setzen
                        };
                        
                        this.LogInfo($"[DYNAMIC-TP] Long Reversal: Entry={longEntryPrice:F2}, TP={dynamicTpLevel:F2} ({dynamicTpTicks:F1} Ticks), SL=fest 8 Ticks");
                        break;

                    default:
                        this.LogWarn($"[ORDER-LONG] Unbekannter OrderflowPatternType f?r Long-Setup: {detectedPattern.Type}. Kein Einstieg.");
                        return;
                }

                _activeTradeSetupParams = setup;
                _entryBarIndex = closed;
                this.LogInfo($"[DBG-EVAL] About to PlaceEntry: dir={(isLongSetupValid ? "Buy" : "Sell")}, price={longEntryPrice}, closed={closed}");
                this.LogInfo($"[ORDER-LONG] Platzierung: Pattern={detectedPattern.Type}, Bias={currentMarketState.DirectionalBias}, " +
                             $"TriggerLevel={entryTriggerLevelName}={entryTriggerLevel:F2}, EntryPrice={longEntryPrice:F2}.");
                PlaceEntry(OrderDirections.Buy, longEntryPrice, closed);
                return; // Beende die Methode nach Orderplatzierung
            }
            else if (isShortSetupValid)
            {
                decimal shortEntryPrice = 0;
                var setup = new SetupParams(); // Standard-Setup-Parameter
                string entryTriggerLevelName = string.Empty;
                decimal entryTriggerLevel = 0;

                // Sicherstellen, dass levelsSnapshot nicht null ist, bevor darauf zugegriffen wird
                if (levelsSnapshot == null)
                {
                    this.LogWarn("[ORDER-SHORT] levelsSnapshot ist null. Kann keinen Einstiegspreis bestimmen.");
                    return;
                }

                switch (detectedPattern.Type)
                {
                    case OrderflowPatternType.PotentialShortTrendContinuation:
                    case OrderflowPatternType.PotentialShortPullback:
                        // F?r Pullbacks/Trend-Continuation: Limit-Order etwas ?ber VAH
                        shortEntryPrice = c.Close + 4 * tickSize;
                        entryTriggerLevelName = "VAH";
                        entryTriggerLevel = levelsSnapshot.CurrentVAH;
                        // Setup-Parameter f?r Trend/Pullback Short (analog Long)
                        setup = new SetupParams
                        {
                            TpTicks = 15,
                            SlTicks = 8,
                            BreakEvenLevelsTrendConfig = "7:1;15:5",
                            TrailType = "CANDLE_HL",
                            TrailActivateAfterTicks = 5,
                            EarlyExitLevels = "POC",
                            ProximityTicksForExit = 1m,
                            EarlyExitMaxDistTicks = 50m,
                            EarlyExitCloseAtLevel = false,
                        };
                        break;

                    case OrderflowPatternType.PotentialShortBreakout:
                        // F?r Breakouts: Stop-Limit Order etwas unter VAL
                        shortEntryPrice = c.Close - 1 * tickSize;
                        entryTriggerLevelName = "VAL";
                        entryTriggerLevel = levelsSnapshot.CurrentVAL;
                        // Setup-Parameter f?r Breakout Short (analog Long)
                        setup = new SetupParams
                        {
                            TpTicks = 10,
                            SlTicks = 5,
                            BreakEvenLevelsTrendConfig = "3:1;8:3",
                            TrailType = "FIXED_TICKS",
                            TrailActivateAfterTicks = 2,
                            EarlyExitLevels = "VAL",
                            ProximityTicksForExit = 0.5m,
                            EarlyExitMaxDistTicks = 30m,
                            EarlyExitCloseAtLevel = true,
                        };
                        break;

                    case OrderflowPatternType.PotentialShortReversalBounce:
                        // F?r Reversal Bounce: Limit-Order leicht unter VAH (oder anderem Widerstand)
                        shortEntryPrice = c.Close - 1 * tickSize;
                       
                        // Berechne dynamischen TP basierend auf n?chstem signifikanten Level
                        var dynamicTpLevelShort = FindNextSignificantLevel(shortEntryPrice, OrderDirections.Sell, levelsSnapshot, currentPOC_Explicit, currentVAH_Explicit, currentVAL_Explicit, logCandidates: true);
                        var dynamicTpTicksShort = (shortEntryPrice - dynamicTpLevelShort) / tickSize;
                        
                        // Setup-Parameter f?r Reversal Short mit dynamischem TP und festem SL
                        setup = new SetupParams
                        {
                            TpType = "Vorgeschlagen", // NEU: TP-Typ setzen
                            SlTicks = 8, // Feste 8 Ticks SL
                            BreakEvenLevelsTrendConfig = "6:1;10:4",
                            TrailType = "CANDLE",
                            TrailActivateAfterTicks = 4,
                            EarlyExitLevels = "",
                            ProximityTicksForExit = 1m,
                            EarlyExitMaxDistTicks = 40m,
                            EarlyExitCloseAtLevel = false,
                            
                            // NEU: Dynamische Level-TP Konfiguration
                            UseDynamicLevelTp = true,
                            DynamicLevelTpOffsetTicks = 1m,
                            SuggestedTargetPrice = dynamicTpLevelShort,
                            MaxDynamicTpDistanceTicks = 12m // NEU: Maximale Distanz auf 12 Ticks setzen
                        };
                        
                        this.LogInfo($"[DYNAMIC-TP] Short Reversal: Entry={shortEntryPrice:F2}, TP={dynamicTpLevelShort:F2} ({dynamicTpTicksShort:F1} Ticks), SL=fest 8 Ticks");
                        break;

                    default:
                        this.LogWarn($"[ORDER-SHORT] Unbekannter OrderflowPatternType f?r Short-Setup: {detectedPattern.Type}. Kein Einstieg.");
                        return;
                }

                _activeTradeSetupParams = setup;
                _entryBarIndex = closed;
                this.LogInfo($"[DBG-EVAL] About to PlaceEntry: dir={(isLongSetupValid ? "Buy" : "Sell")}, price={shortEntryPrice}, closed={closed}");
                this.LogInfo($"[ORDER-SHORT] Platzierung: Pattern={detectedPattern.Type}, Bias={currentMarketState.DirectionalBias}, " +
                             $"TriggerLevel={entryTriggerLevelName}={entryTriggerLevel:F2}, EntryPrice={shortEntryPrice:F2}.");
                PlaceEntry(OrderDirections.Sell, shortEntryPrice, closed);
                return; // Beende die Methode nach Orderplatzierung
            }
        }






        // 2) Preis?Volumen-Zeilen aus einem IndicatorCandle extrahieren
        // Approximation der Preiszeilen aus Bar-Gesamtsummen
        // - bodyBias: Anteil des Volumens, der in den Kerzenk?rper geht (Rest in die Dochte)
        // - deltaSkew: wie stark Delta die Verteilung nach oben/unten verschiebt
        // Liefert (Price, Volume)-Paare aus einer IndicatorCandle mittels ATAS-API.
        // Verwendet bevorzugt GetAllPriceLevels(), fallback auf GetPriceVolumeInfo(price).
        // Liefert (Price, Volume)-Paare aus einer IndicatorCandle mittels ATAS-API.
        // Nutzt die GetAllPriceLevels() Methode, die eine Collection von PriceVolumeInfo Objekten zur?ckgibt.
        private IEnumerable<(decimal Price, decimal Volume)> EnumerateClusterRows(IndicatorCandle c)
        {
            if (c == null) yield break;

            var priceLevels = c.GetAllPriceLevels();
            if (priceLevels == null) yield break;

            foreach (var pv in priceLevels)
            {
                if (pv == null) continue;

                decimal price = (decimal)pv.Price;
                decimal vol = (decimal)pv.Volume;
                if (vol <= 0m) vol = (decimal)pv.Ask + (decimal)pv.Bid;

                if (vol > 0m)
                    yield return (RoundToTick(price), vol);
            }
        }



        // 3) Histogramm aus Cluster-Zeilen (Tick-Index-basiert, robust und schnell)
        private Dictionary<int, decimal> BuildVolumeProfileFromClusters(int startBar, int endBar)
        {
            var hist = new Dictionary<int, decimal>(4096);

            for (int i = startBar; i <= endBar; i++)
            {
                var c = GetCandle(i);
                if (c == null) continue;

                foreach (var (price, vol) in EnumerateClusterRows(c))
                {
                    if (vol <= 0m) continue;
                    int idx = ToTickIndex(price);
                    if (hist.TryGetValue(idx, out var v)) hist[idx] = v + vol;
                    else hist[idx] = vol;
                }
            }

            return hist;
        }
        // 4) POC/VAH/VAL aus Tick-Index-Histogramm (deterministische Regeln)
        // hist: Dictionary<int, decimal> mit TickIndex -> Volume
        // valueAreaFraction: z. B. 0.70m
        // R?ckgabe: POC-Preis, VAH-Preis, VAL-Preis
        // hist: TickIndex -> Volume (nur Vortag)
        // valueAreaFraction: 0.70m (ATAS nutzt i. d. R. 70%)
        // hist: TickIndex -> Volume (nur Vortag)
        // valueAreaFraction: z. B. 0.70m
        private void CalculatePOC_VAH_VAL_FromTickIndexHist(
            Dictionary<int, decimal> hist,
            decimal valueAreaFraction,
            out decimal pocPrice,
            out decimal vahPrice,
            out decimal valPrice)
        {
            pocPrice = vahPrice = valPrice = 0m;
            if (hist == null || hist.Count == 0) return;

            decimal total = 0m;
            foreach (var v in hist.Values) total += v;
            if (total <= 0m) return;

            // POC-Index
            int pocIdx = 0; decimal pocVol = -1m;
            foreach (var kv in hist)
                if (kv.Value > pocVol) { pocVol = kv.Value; pocIdx = kv.Key; }

            decimal GetVol(int idx) => hist.TryGetValue(idx, out var v) ? v : 0m;

            int lo = pocIdx, hi = pocIdx;
            decimal cum = pocVol;
            decimal target = total * valueAreaFraction;

            // expandiere nur in tats?chlich vorhandene Nachbar-Buckets
            while (cum < target)
            {
                int nextLo = lo - 1;
                int nextHi = hi + 1;

                bool hasLo = hist.ContainsKey(nextLo);
                bool hasHi = hist.ContainsKey(nextHi);

                if (!hasLo && !hasHi) break;

                if (hasLo && !hasHi)
                {
                    decimal volLo = GetVol(nextLo);
                    lo = nextLo; cum += volLo;
                    continue;
                }
                if (!hasLo && hasHi)
                {
                    decimal volHi = GetVol(nextHi);
                    hi = nextHi; cum += volHi;
                    continue;
                }

                // beide vorhanden -> w?hle Seite, die dem Ziel n?her kommt
                decimal volLo2 = GetVol(nextLo);
                decimal volHi2 = GetVol(nextHi);

                decimal cumLo = cum + volLo2;
                decimal cumHi = cum + volHi2;

                decimal diffLo = Math.Abs(target - cumLo);
                decimal diffHi = Math.Abs(target - cumHi);

                bool takeLower;
                if (diffLo < diffHi) takeLower = true;
                else if (diffHi < diffLo) takeLower = false;
                else
                {
                    // Gleichstand: gr??ere Volumen-Seite,
                    // bleibt es gleich: obere Seite bevorzugen (gegen systematisch zu tiefe VAL)
                    if (volLo2 > volHi2) takeLower = true;
                    else if (volHi2 > volLo2) takeLower = false;
                    else takeLower = false;
                }

                if (takeLower)
                {
                    lo = nextLo; cum = cumLo;
                }
                else
                {
                    hi = nextHi; cum = cumHi;
                }
            }

            pocPrice = FromTickIndex(pocIdx);
            valPrice = FromTickIndex(lo);
            vahPrice = FromTickIndex(hi);
        }




        

        private (bool isBlockedLong, TrackedLevel blockingLevel) IsCloseToResistance(decimal currentPrice, List<TrackedLevel> _untouchedLevels, decimal threshold)
        {
            foreach (var level in _untouchedLevels)
            {
                // Widerstand ist ?ber dem aktuellen Preis, und der Abstand ist innerhalb des Schwellenwerts
                if (level.Value > currentPrice && (level.Value - currentPrice) <= threshold)
                {
                    return (true, level);
                }
            }
            return (false, null);
        }

        /// <summary>
        /// Pr?ft, ob der aktuelle Preis sich einem Unterst?tzungslevel von oben n?hert.
        /// Wird f?r den vorzeitigen Ausstieg aus Short-Trades verwendet.
        /// </summary>
        /// <param name="currentPrice">Der aktuelle Preis.</param>
        /// <param name="levels">Liste der signifikanten Level.</param>
        /// <param name="threshold">Der N?herungsschwellenwert in Punkten.</param>
        /// <returns>True, wenn sich der Preis einer Unterst?tzung n?hert, sonst False.</returns>
        private (bool isBlockedShort, TrackedLevel blockingLevel) IsCloseToSupport(decimal currentPrice, List<TrackedLevel> _untouchedLevels, decimal threshold)
        {
            foreach (var level in _untouchedLevels)
            {
                // Unterst?tzung ist unter dem aktuellen Preis, und der Abstand ist innerhalb des Schwellenwerts
                if (level.Value < currentPrice && (currentPrice - level.Value) <= threshold)
                {
                    return (true, level);
                }
            }
            return (false, null);
        }
        // =========================================================================
        // Volumen | WegFrei-Berechnung
        // =========================================================================
        private static bool IsInsideValue(MicroComposite mc, decimal price)
        {
            if (mc == null) return false;
            var lo = Math.Min(mc.VAL, mc.VAH);
            var hi = Math.Max(mc.VAL, mc.VAH);
            return price >= lo && price <= hi;
        }
        private int GetRiskTicks() => Math.Max(DMinTicks, 1);
        
        private static decimal? TryComputeMedianVol(SortedDictionary<decimal, decimal> levelVols)
        {
            if (levelVols == null || levelVols.Count == 0) return null;
            var sorted = levelVols.Values.OrderBy(v => v).ToList();
            int n = sorted.Count;
            return (n % 2 == 1) ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2m;
        }
        private int ValueMigrationDirection(MicroComposite prev, MicroComposite curr, decimal tickSize, int minShiftTicks = 2)
        {
            if (prev == null || curr == null || tickSize <= 0m) return 0;
            int shift = TicksBetween(curr.POC, prev.POC); // Vorzeichen tr?gt Richtung
            if (Math.Abs(shift) < minShiftTicks) return 0;
            return shift > 0 ? +1 : -1;
        }
        // Kleines Hilfs-Utility f?r Bereichscheck
        private static bool IsInRangeOben(decimal level, decimal currentPrice, decimal upperThreshold)
            => level > currentPrice && level <= upperThreshold;

        private static bool IsInRangeUnten(decimal level, decimal currentPrice, decimal lowerThreshold)
            => level < currentPrice && level >= lowerThreshold;

        // Wieviele LVN-Center liegen im Pfad?
        private static int CountLVNsInPathUp(IReadOnlyList<decimal> lvns, decimal currentPrice, decimal upperThreshold)
        {
            if (lvns == null) return 0;
            var arr = lvns as decimal[] ?? lvns.ToArray(); // Snapshot
            int cnt = 0;
            for (int i = 0; i < arr.Length; i++)
                if (IsInRangeOben(arr[i], currentPrice, upperThreshold)) cnt++;
            return cnt;
        }

        private static int CountLVNsInPathDown(IReadOnlyList<decimal> lvns, decimal currentPrice, decimal lowerThreshold)
        {
            if (lvns == null) return 0;
            var arr = lvns as decimal[] ?? lvns.ToArray();
            int cnt = 0;
            for (int i = 0; i < arr.Length; i++)
                if (IsInRangeUnten(arr[i], currentPrice, lowerThreshold)) cnt++;
            return cnt;
        }

        private static int CountLVNZoneEdgesInPathUp(IReadOnlyList<(decimal Start, decimal End)> zones, decimal currentPrice, decimal upperThreshold)
        {
            if (zones == null || zones.Count == 0) return 0;
            var arr = zones as (decimal Start, decimal End)[] ?? zones.ToArray();
            int cnt = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                var z = arr[i];
                bool intersects = !(z.End < currentPrice || z.Start > upperThreshold);
                if (intersects) { cnt++; continue; }
                if (IsInRangeOben(z.Start, currentPrice, upperThreshold) || IsInRangeOben(z.End, currentPrice, upperThreshold))
                    cnt++;
            }
            return cnt;
        }

        private static int CountLVNZoneEdgesInPathDown(IReadOnlyList<(decimal Start, decimal End)> zones, decimal currentPrice, decimal lowerThreshold)
        {
            if (zones == null || zones.Count == 0) return 0;
            var arr = zones as (decimal Start, decimal End)[] ?? zones.ToArray();
            int cnt = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                var z = arr[i];
                bool intersects = !(z.Start > currentPrice || z.End < lowerThreshold);
                if (intersects) { cnt++; continue; }
                if (IsInRangeUnten(z.Start, currentPrice, lowerThreshold) || IsInRangeUnten(z.End, currentPrice, lowerThreshold))
                    cnt++;
            }
            return cnt;
        }

        private static bool IsStrongHVN(
            decimal hvnPrice,
            MicroComposite mc,
            PathConfig cfg,
            decimal tickSize
        )
        {
            // Ohne LevelVols: konservativ -> behandle HVN als stark (POCVol vorhanden)
            if (mc.LevelVols == null || !mc.LevelVols.TryGetValue(hvnPrice, out var hvnVol))
                return true;

            var medianVol = TryComputeMedianVol(mc.LevelVols);
            decimal? prominence = null;

            // Nachbarpreise (?1 Tick)
            decimal left = hvnPrice - tickSize;
            decimal right = hvnPrice + tickSize;
            var vL = mc.LevelVols.TryGetValue(left, out var vl) ? vl : hvnVol;
            var vR = mc.LevelVols.TryGetValue(right, out var vr) ? vr : hvnVol;
            prominence = hvnVol - Math.Max(vL, vR);

            bool strongVsPOC = mc.POCVol > 0m && hvnVol >= cfg.HVNStrengthVsPOC * mc.POCVol;
            bool strongVsMedian = medianVol.HasValue && hvnVol >= cfg.HVNStrengthVsMedian * medianVol.Value;
            bool prominent = (medianVol.HasValue && prominence.HasValue) ? prominence.Value >= cfg.MinProminenceVsMedian * medianVol.Value : true;

            return (strongVsPOC || strongVsMedian) && prominent;
        }


        // Hilfsmethode f?r den "Weg frei"-Check bei Long-Einstieg (kein Widerstand oberhalb des aktuellen Preises)
        // Pr?ft, ob ein HVN oder POC aus _currentMC innerhalb von DMinTicks oberhalb des aktuellen Preises liegt
        // Long: pr?ft Blocker (POC/HVN/VAH) und verlangt mindestens einen LVN-Pfad
        // Long: LVN-Pfad, kontextabh?ngige VA/POC-Behandlung, HVN-St?rke-Filter, Mindestdistanz zum ersten Blocker
        private bool IsPathFreeLongEnhanced(
            decimal currentPrice,
            int dMinTicks,
            int riskTicks,
            MicroComposite mcCurr,
            MicroComposite mcPrev,
            decimal tickSize,
            PathConfig cfg,
            out string blocker
        )
        {
            blocker = string.Empty;
            if (mcCurr == null || tickSize <= 0m || dMinTicks <= 0)
            {
                //this.LogInfo($"PathLong: bypass mcCurr={mcCurr == null} tickSize={tickSize} dMinTicks={dMinTicks} ? true");
                return true;
            }

            decimal upperThreshold = currentPrice + (dMinTicks * tickSize);
            bool insideValue = IsInsideValue(mcCurr, currentPrice);

            // LVN-Count: kombiniere Punkte + Zonen (als Tupel konvertiert)
            int lvnCountPts = CountLVNsInPathUp(mcCurr.LVNs, currentPrice, upperThreshold);
            int lvnCountZones = mcCurr.LVNZones != null
                ? CountLVNZoneEdgesInPathUp(mcCurr.LVNZones, currentPrice, upperThreshold)
                : 0;
            int lvnCount = Math.Max(lvnCountPts, lvnCountZones);

            int migDir = ValueMigrationDirection(mcPrev, mcCurr, tickSize);

            //this.LogInfo($"PathLong: price={currentPrice:F2} upperTh={upperThreshold:F2} dMinTicks={dMinTicks} riskTicks={riskTicks} insideValue={insideValue} lvnCount={lvnCount} migDir={migDir} lvnPts={lvnCountPts} lvnZones={lvnCountZones}");

            if (lvnCount < cfg.RequiredLVNsInPath)
            {
                //this.LogInfo($"PathLong: lvnCount {lvnCount} < erforderlich {cfg.RequiredLVNsInPath} ? false");
                blocker = $"LVN_PATH<{cfg.RequiredLVNsInPath}";
                return false;
            }

            // POC blockt
            if (IsInRangeOben(mcCurr.POC, currentPrice, upperThreshold))
            {
                //this.LogInfo($"PathLong: POC {mcCurr.POC:F2} blocks in range ? false");
                blocker = $"MC.POC@{mcCurr.POC:F2}";
                return false;
            }

            // HVN-Blocker: zuerst Zonen (als Tupel), dann Punkt-HVNs
            decimal? closestStrongBlocker = null;

            if (mcCurr.HVNZones != null && mcCurr.HVNZones.Count > 0)
            {
                foreach (var z in mcCurr.HVNZones)
                {
                    bool intersects = !(z.End < currentPrice || z.Start > upperThreshold);
                    if (!intersects) continue;

                    decimal center = (z.Start + z.End) / 2m;
                    if (!IsStrongHVN(center, mcCurr, cfg, tickSize)) continue;

                    bool startIn = IsInRangeOben(z.Start, currentPrice, upperThreshold);
                    bool endIn = IsInRangeOben(z.End, currentPrice, upperThreshold);
                    decimal edge = startIn ? z.Start
                                  : endIn ? z.End
                                  : (TicksBetweenAbs(z.Start, currentPrice) <= TicksBetweenAbs(z.End, currentPrice) ? z.Start : z.End);

                    int dist = TicksBetweenAbs(edge, currentPrice);
                    //this.LogInfo($"PathLong: candidate HVN-Zone [{z.Start:F2}-{z.End:F2}] edge={edge:F2} distTicks={dist}");

                    if (closestStrongBlocker == null || dist < TicksBetweenAbs(closestStrongBlocker.Value, currentPrice))
                        closestStrongBlocker = edge;
                }
            }

            if (closestStrongBlocker == null && mcCurr.HVNs != null)
            {
                foreach (var hvn in mcCurr.HVNs)
                {
                    if (!IsInRangeOben(hvn, currentPrice, upperThreshold)) continue;
                    if (!IsStrongHVN(hvn, mcCurr, cfg, tickSize)) continue;

                    int dist = TicksBetweenAbs(hvn, currentPrice);
                    //this.LogInfo($"PathLong: candidate HVN {hvn:F2} distTicks={dist}");
                    if (closestStrongBlocker == null || dist < TicksBetweenAbs(closestStrongBlocker.Value, currentPrice))
                        closestStrongBlocker = hvn;
                }
            }

            // VAH-Block
            bool vahBlocks = IsInRangeOben(mcCurr.VAH, currentPrice, upperThreshold);
            if (vahBlocks)
            {
                //this.LogInfo($"PathLong: VAH {mcCurr.VAH:F2} in range; insideValue={insideValue} relax={cfg.RelaxVAEdgesWhenOutsideValue}");
                if (insideValue)
                {
                    blocker = $"MC.VAH@{mcCurr.VAH:F2}";
                    return false;
                }
                if (!cfg.RelaxVAEdgesWhenOutsideValue)
                {
                    blocker = $"MC.VAH@{mcCurr.VAH:F2}";
                    return false;
                }
            }

            if (closestStrongBlocker.HasValue)
            {
                int distTicks = TicksBetweenAbs(closestStrongBlocker.Value, currentPrice);
                int minAllowed = Math.Max(dMinTicks, riskTicks * cfg.MinBlockerDistanceTicksVsRisk);
                //this.LogInfo($"PathLong: closestStrongBlocker={closestStrongBlocker.Value:F2} distTicks={distTicks} dMinTicks={dMinTicks} riskTicks={riskTicks} minAllowed={minAllowed}");
                if (distTicks < minAllowed)
                {
                    blocker = $"MC.HVN@{closestStrongBlocker.Value:F2}";
                    return false;
                }
            }

            //this.LogInfo("PathLong: ? true");
            return true;
        }



        // Short analog
        private bool IsPathFreeShortEnhanced(
            decimal currentPrice,
            int dMinTicks,
            int riskTicks,
            MicroComposite mcCurr,
            MicroComposite mcPrev,
            decimal tickSize,
            PathConfig cfg,
            out string blocker
        )
        {
            blocker = string.Empty;
            if (mcCurr == null || tickSize <= 0m || dMinTicks <= 0)
            {
                //this.LogInfo($"PathShort: bypass mcCurr={mcCurr == null} tickSize={tickSize} dMinTicks={dMinTicks} ? true");
                return true;
            }

            decimal lowerThreshold = currentPrice - (dMinTicks * tickSize);
            bool insideValue = IsInsideValue(mcCurr, currentPrice);

            int lvnCountPts = CountLVNsInPathDown(mcCurr.LVNs, currentPrice, lowerThreshold);
            int lvnCountZones = mcCurr.LVNZones != null
                ? CountLVNZoneEdgesInPathDown(mcCurr.LVNZones, currentPrice, lowerThreshold)
                : 0;
            int lvnCount = Math.Max(lvnCountPts, lvnCountZones);


            int migDir = ValueMigrationDirection(mcPrev, mcCurr, tickSize);

            //this.LogInfo($"PathShort: price={currentPrice:F2} lowerTh={lowerThreshold:F2} dMinTicks={dMinTicks} riskTicks={riskTicks} insideValue={insideValue} lvnCount={lvnCount} migDir={migDir} lvnPts={lvnCountPts} lvnZones={lvnCountZones}");

            if (lvnCount < cfg.RequiredLVNsInPath)
            {
                //this.LogInfo($"PathShort: lvnCount {lvnCount} < erforderlich {cfg.RequiredLVNsInPath} ? false");
                blocker = $"LVN_PATH<{cfg.RequiredLVNsInPath}";
                return false;
            }

            if (IsInRangeUnten(mcCurr.POC, currentPrice, lowerThreshold))
            {
                //this.LogInfo($"PathShort: POC {mcCurr.POC:F2} blocks in range ? false");
                blocker = $"MC.POC@{mcCurr.POC:F2}";
                return false;
            }

            decimal? closestStrongBlocker = null;

            if (mcCurr.HVNZones != null && mcCurr.HVNZones.Count > 0)
            {
                foreach (var z in mcCurr.HVNZones)
                {
                    bool intersects = !(z.Start > currentPrice || z.End < lowerThreshold);
                    if (!intersects) continue;

                    decimal center = (z.Start + z.End) / 2m;
                    if (!IsStrongHVN(center, mcCurr, cfg, tickSize)) continue;

                    bool startIn = IsInRangeUnten(z.Start, currentPrice, lowerThreshold);
                    bool endIn = IsInRangeUnten(z.End, currentPrice, lowerThreshold);
                    decimal edge = endIn ? z.End
                                  : startIn ? z.Start
                                  : (TicksBetweenAbs(z.Start, currentPrice) <= TicksBetweenAbs(z.End, currentPrice) ? z.Start : z.End);

                    int dist = TicksBetweenAbs(edge, currentPrice);
                    //this.LogInfo($"PathShort: candidate HVN-Zone [{z.Start:F2}-{z.End:F2}] edge={edge:F2} distTicks={dist}");

                    if (closestStrongBlocker == null || dist < TicksBetweenAbs(closestStrongBlocker.Value, currentPrice))
                        closestStrongBlocker = edge;
                }
            }


            if (closestStrongBlocker == null && mcCurr.HVNs != null)
            {
                foreach (var hvn in mcCurr.HVNs)
                {
                    if (!IsInRangeUnten(hvn, currentPrice, lowerThreshold)) continue;
                    if (!IsStrongHVN(hvn, mcCurr, cfg, tickSize)) continue;

                    int dist = TicksBetweenAbs(hvn, currentPrice);
                    //this.LogInfo($"PathShort: candidate HVN {hvn:F2} distTicks={dist}");
                    if (closestStrongBlocker == null ||
                        TicksBetweenAbs(hvn, currentPrice) < TicksBetweenAbs(closestStrongBlocker.Value, currentPrice))
                    {
                        closestStrongBlocker = hvn;
                    }
                }
            }

            bool valBlocks = IsInRangeUnten(mcCurr.VAL, currentPrice, lowerThreshold);
            if (valBlocks)
            {
                //this.LogInfo($"PathShort: VAL {mcCurr.VAL:F2} in range; insideValue={insideValue} relax={cfg.RelaxVAEdgesWhenOutsideValue}");
                if (insideValue)
                {
                    blocker = $"MC.VAL@{mcCurr.VAL:F2}";
                    return false;
                }
                if (!cfg.RelaxVAEdgesWhenOutsideValue)
                {
                    blocker = $"MC.VAL@{mcCurr.VAL:F2}";
                    return false;
                }
            }

            if (closestStrongBlocker.HasValue)
            {
                int distTicks = TicksBetweenAbs(closestStrongBlocker.Value, currentPrice);
                int minAllowed = Math.Max(dMinTicks, riskTicks * cfg.MinBlockerDistanceTicksVsRisk);
                //this.LogInfo($"PathShort: closestStrongBlocker={closestStrongBlocker.Value:F2} distTicks={distTicks} dMinTicks={dMinTicks} riskTicks={riskTicks} minAllowed={minAllowed}");
                if (distTicks < minAllowed)
                {
                    blocker = $"MC.HVN@{closestStrongBlocker.Value:F2}";
                    return false;
                }
            }
            //this.LogInfo("PathShort: ? true");
            return true;
        }

        // Richtungsabh?ngiger Weg-Frei-Score ohne HVN/LVN: nur Headroom zu VA-Kanten und POC
        private decimal GetPathFreeLong()
        {
            var mc = _currentMC ?? GetRollingMicroComposite();
            if (mc == null || _tickSize <= 0m) return 1m;

            var price = GetLastPrice();
            if (price <= 0m) return 1m;

            int riskRef = GetRiskTicks();

            // Headroom nach oben: n?chste Blocker VAH/POC ?ber Preis
            int toVAH = TicksBetweenAbs(price, mc.VAH);
            int toPOC = (mc.POC > price) ? TicksBetweenAbs(price, mc.POC) : int.MaxValue;

            int headroomTicks = Math.Min(toVAH, toPOC);
            if (headroomTicks == int.MaxValue) headroomTicks = toVAH; // falls POC nicht dr?ber liegt

            // einfache Normierung: 0 bei 0 Ticks, >= riskRef -> ~0.8, bei 2*riskRef -> ~1.0
            decimal score = Math.Clamp((decimal)headroomTicks / (riskRef * 2m), 0m, 1m);
            return score;
        }

        private decimal GetPathFreeShort()
        {
            var mc = _currentMC ?? GetRollingMicroComposite();
            if (mc == null || _tickSize <= 0m) return 1m;

            var price = GetLastPrice();
            if (price <= 0m) return 1m;

            int riskRef = GetRiskTicks();

            // Headroom nach unten: n?chste Blocker VAL/POC unter Preis
            int toVAL = TicksBetweenAbs(price, mc.VAL);
            int toPOC = (mc.POC < price) ? TicksBetweenAbs(price, mc.POC) : int.MaxValue;

            int headroomTicks = Math.Min(toVAL, toPOC);
            if (headroomTicks == int.MaxValue) headroomTicks = toVAL; // falls POC nicht drunter liegt

            decimal score = Math.Clamp((decimal)headroomTicks / (riskRef * 2m), 0m, 1m);
            return score;
        }
        // =========================================================================
        // VWAP | Bounce-Berechnung
        // =========================================================================
        // Preis -> Band1 oder Band2 klassifizieren
        private int DetectBounceBand2(decimal price,
                              decimal vwapCurrent,
                              decimal vwapUpperBand1, decimal vwapLowerBand1,
                              decimal vwapUpperBand2, decimal vwapLowerBand2,
                              decimal tick,
                              int proximityTicks = 2) // NEW
        {
            // --- CHANGED: N?he-Schwelle aus Param statt hardcoded 3 ---
            if (proximityTicks < 1) proximityTicks = 1;                  // Guard
            decimal proximityPx = proximityTicks * tick;                 // CHANGED

            // Distanz zur n?chstgelegenen Kante von Band1 vs Band2
            decimal d1 = Math.Min(Math.Abs(price - vwapLowerBand1), Math.Abs(price - vwapUpperBand1));
            decimal d2 = Math.Min(Math.Abs(price - vwapLowerBand2), Math.Abs(price - vwapUpperBand2));

            // Wenn nah an einer Kante: nimm das n?here Band
            if (Math.Min(d1, d2) <= proximityPx)
                return d1 <= d2 ? 1 : 2;

            // Fallback: grob per Lage relativ zu VWAP und Band-Grenzen
            bool above = price >= vwapCurrent;
            if (above)
                return price <= vwapUpperBand1 ? 1 : 2;
            else
                return price >= vwapLowerBand1 ? 1 : 2;
        }

        private int DetectBounceBand3(decimal price,
                              decimal vwapCurrent,
                              decimal vwapUpperBand1, decimal vwapLowerBand1,
                              decimal vwapUpperBand2, decimal vwapLowerBand2,
                              decimal vwapUpperBand3, decimal vwapLowerBand3,
                              decimal tick)
        {
            decimal proximityPx = 3m * tick;

            // Distanz zur jeweils n?chsten Kante je Band
            decimal d1 = Math.Min(Math.Abs(price - vwapLowerBand1), Math.Abs(price - vwapUpperBand1));
            decimal d2 = Math.Min(Math.Abs(price - vwapLowerBand2), Math.Abs(price - vwapUpperBand2));
            decimal d3 = Math.Min(Math.Abs(price - vwapLowerBand3), Math.Abs(price - vwapUpperBand3));

            // Falls nahe an irgendeiner Band-Kante: nimm das n?chste
            decimal min = Math.Min(d1, Math.Min(d2, d3));
            if (min <= proximityPx)
            {
                if (min == d1) return 1;
                if (min == d2) return 2;
                return 3;
            }

            // Grobe Klassifikation ?ber VWAP-Lage und obere/untere Grenzen
            bool above = price >= vwapCurrent;
            if (above)
            {
                if (price <= vwapUpperBand1) return 1;
                if (price <= vwapUpperBand2) return 2;
                return 3;
            }
            else
            {
                if (price >= vwapLowerBand1) return 1;
                if (price >= vwapLowerBand2) return 2;
                return 3;
            }
        }

        struct EntryPlan
        {
            public decimal LimitPrice;     // Entry-Limit
            public string SlLevelKey;      // SL-Key f?r deinen Calculator
            public int BandIndex;          // 1/2/3
            public string BandSide;        // "LOWER" / "UPPER" / "VWAP"
        }

        EntryPlan MakeEntryPlanLong(
            int bandIndex,
            decimal priceForDetection,
            decimal tick,
            decimal vwapCurrent,
            decimal vwapLowerBand1, decimal vwapLowerBand2, decimal vwapLowerBand3,
            decimal vwapUpperBand1, decimal vwapUpperBand2, decimal vwapUpperBand3,
            int proximityTicks = 2
        )
        {

            // --- NEW: Mindest-Guard ---
            if (proximityTicks < 1) proximityTicks = 1;

            // VWAP zuerst, wenn sehr nahe
            if (Math.Abs(priceForDetection - vwapCurrent) <= proximityTicks * tick)
            {
                return new EntryPlan
                {
                    LimitPrice = vwapCurrent + tick,    // 1 Tick ?ber VWAP
                    SlLevelKey = "VWAP_LOWER_BAND1",    // konservativ: SL unter Band1
                    BandIndex = 0,
                    BandSide = "VWAP"
                };
            }

            // Long: LOWER-Seite des erkannten Bands
            decimal bandPx = bandIndex == 1 ? vwapLowerBand1
                            : bandIndex == 2 ? vwapLowerBand2
                            : vwapLowerBand3; // falls Band3 genutzt

            string slKey = bandIndex == 1 ? "VWAP_LOWER_BAND1"
                          : bandIndex == 2 ? "VWAP_LOWER_BAND2"
                          : "VWAP_LOWER_BAND3";

            return new EntryPlan
            {
                LimitPrice = bandPx + tick,
                SlLevelKey = slKey,
                BandIndex = bandIndex,
                BandSide = "LOWER"
            };
        }

        EntryPlan MakeEntryPlanShort(
            int bandIndex,
            decimal priceForDetection,
            decimal tick,
            decimal vwapCurrent,
            decimal vwapUpperBand1, decimal vwapUpperBand2, decimal vwapUpperBand3,
            decimal vwapLowerBand1, decimal vwapLowerBand2, decimal vwapLowerBand3,
            int proximityTicks = 3
        )
        {
            // --- NEW: Mindest-Guard ---
            if (proximityTicks < 1) proximityTicks = 1;

            // VWAP zuerst, wenn sehr nahe
            if (Math.Abs(priceForDetection - vwapCurrent) <= proximityTicks * tick)
            {
                return new EntryPlan
                {
                    LimitPrice = vwapCurrent - tick,   // 1 Tick unter VWAP
                    SlLevelKey = "VWAP_UPPER_BAND1",   // konservativ: SL ?ber Band1
                    BandIndex = 0,
                    BandSide = "VWAP"
                };
            }

            // Short: UPPER-Seite des erkannten Bands
            decimal bandPx = bandIndex == 1 ? vwapUpperBand1
                            : bandIndex == 2 ? vwapUpperBand2
                            : vwapUpperBand3;

            string slKey = bandIndex == 1 ? "VWAP_UPPER_BAND1"
                          : bandIndex == 2 ? "VWAP_UPPER_BAND2"
                          : "VWAP_UPPER_BAND3";

            return new EntryPlan
            {
                LimitPrice = bandPx - tick,
                SlLevelKey = slKey,
                BandIndex = bandIndex,
                BandSide = "UPPER"
            };
        }

        

        


        private decimal TryGetFillPrice(Order order, Security sec)
        {
            this.LogInfo($"[TryGetFillPrice] Starting - Order: {order.Id}, Direction: {order.Direction}, OrderPrice: {order.Price}");

            if (sec == null)
            {
                this.LogInfo($"[TryGetFillPrice] ERROR: Security is null");
                return 0m;
            }

            // Ohne echte Ausf?hrung KEIN Preis ableiten
            var filledQty = order.Filled(); // bzw. GetFilledQuantity(order)
            if (filledQty <= 0m)
            {
                this.LogWarn("[TryGetFillPrice] No executed quantity. Returning 0 to avoid fake fill on Done(Cancel/Timeout).");
                return 0m;
            }

            this.LogInfo($"[TryGetFillPrice] Security data - LastTradePrice: {sec.LastTradePrice}, BestAsk: {sec.BestAskPrice}, BestBid: {sec.BestBidPrice}, MarkPrice: {sec.MarkPrice}");

            // 1) letzter Trade-Preis (bevorzugt)
            if (sec.LastTradePrice.HasValue && sec.LastTradePrice.Value > 0m)
            {
                this.LogInfo($"[TryGetFillPrice] Using LastTradePrice: {sec.LastTradePrice.Value}");
                return sec.LastTradePrice.Value;
            }

            // 2) Best Ask/Bid je nach Richtung
            bool isBuy = order.Direction == OrderDirections.Buy;
            decimal bestAsk = sec.BestAskPrice;
            decimal bestBid = sec.BestBidPrice;

            this.LogInfo($"[TryGetFillPrice] Order is {(isBuy ? "BUY" : "SELL")} - BestAsk: {bestAsk}, BestBid: {bestBid}");

            if (isBuy)
            {
                if (bestAsk > 0m) return bestAsk;
                if (bestBid > 0m) return bestBid; // Fallback, wenn Ask fehlt
            }
            else
            {
                if (bestBid > 0m) return bestBid;
                if (bestAsk > 0m) return bestAsk; // Fallback, wenn Bid fehlt
            }

            // 3) Mark-Preis als weiterer Fallback
            if (sec.MarkPrice.HasValue && sec.MarkPrice.Value > 0m)
                return sec.MarkPrice.Value;

            // 4) Midprice
            if (bestBid > 0m && bestAsk > 0m)
                return (bestBid + bestAsk) / 2m;

            // 5) Order-Preis
            if (order.Price > 0m)
                return order.Price;

            return 0m;
        }



        private bool IsFilledOrDone(Order order)
        {
            var status = order.Status();

            // Vollst?ndig gef?llt
            if (status == OrderStatus.Filled)
                return true;

            // Teil-Fill nur, wenn tats?chlich > 0 ausgef?hrt
            if (status == OrderStatus.PartlyFilled)
                return order.Filled() > 0m;

            // Done ist ambivalent (Fill ODER Cancel/Timeout).
            // Nur als Fill werten, wenn wirklich etwas ausgef?hrt wurde.
            if (order.State == OrderStates.Done)
                return order.Filled() > 0m;

            return false;
        }



        private decimal GetFilledQuantity(Order order)
        {
            // Da es bei dir eine Extension-Methode ist, einfach aufrufen:
            return order.Filled();
        }

        // --- ORDER-UPDATES (Behandelt Zustands?nderungen von Orders) ------------- (Beibehalten)
        // Diese Methode wird automatisch von ATAS aufgerufen, wenn sich der Status einer Order ?ndert.
        protected override void OnOrderChanged(Order order)
        {
           
            base.OnOrderChanged(order);

            //this.LogInfo($"[OnOrderChanged] Order received - Id: {order?.Id}, Status: {order?.Status()}, Direction: {order?.Direction}, Type: {order?.Type}");

            if (order == null)
            {
                //this.LogWarn("[OnOrderChanged] Order is NULL - returning");
                return;
            }

            // DEBUG: Zeige aktuelle Order-Referenzen
            //this.LogInfo($"[OnOrderChanged] DEBUG: _entryOrder is {(_entryOrder == null ? "NULL" : $"Order ID {_entryOrder.Id}")}");
            //this.LogInfo($"[OnOrderChanged] DEBUG: _pullbackOrder is {(_pullbackOrder == null ? "NULL" : $"Order ID {_pullbackOrder.Id}")}");
            //this.LogInfo($"[OnOrderChanged] DEBUG: _isExitPlacementPending = {_isExitPlacementPending}");
            // Rebinding: gleiche Id, aber andere Instanz ? auf Runtime-Instanz binden
            if (_entryOrder != null && order.Id == _entryOrder.Id && !ReferenceEquals(order, _entryOrder))
            {
                this.LogInfo($"[OnOrderChanged] Rebinding _entryOrder auf Runtime-Instanz (ID={order.Id}).");
                _entryOrder = order;
            }
            if (_pullbackOrder != null && order.Id == _pullbackOrder.Id && !ReferenceEquals(order, _pullbackOrder))
            {
                this.LogInfo($"[OnOrderChanged] Rebinding _pullbackOrder auf Runtime-Instanz (ID={order.Id}).");
                _pullbackOrder = order;
            }
            if (_marketOrder != null && order.Id == _marketOrder.Id && !ReferenceEquals(order, _marketOrder))
            {
                this.LogInfo($"[OnOrderChanged] Rebinding _marketOrder (ID={order.Id})");
                _marketOrder = order;
            }
            if (_tpOrder != null && order.Id == _tpOrder.Id && !ReferenceEquals(order, _tpOrder))
            {
                this.LogInfo($"[OnOrderChanged] Rebinding _tpOrder auf Runtime-Instanz (ID={order.Id}).");
                _tpOrder = order;
            }
            if (_slOrder != null && order.Id == _slOrder.Id && !ReferenceEquals(order, _slOrder))
            {
                this.LogInfo($"[OnOrderChanged] Rebinding _slOrder auf Runtime-Instanz (ID={order.Id}).");
                _slOrder = order;
            }



            // ---- A) ENTRY-FILL (Market) -----
            if (_marketOrder != null && order.Id == _marketOrder.Id && IsFilledOrDone(order) && !_isExitPlacementPending)
            {
                this.LogInfo("[OnOrderChanged] Market order FILLED/DONE - processing...");

                var fillPrice = TryGetFillPrice(order, Security); // kann 0 sein; wenn 0 ? return und auf weiteres Event warten
                if (fillPrice <= 0m)
                {
                    this.LogWarn("[OnOrderChanged] Market-Fill erkannt, aber Fill-Preis noch 0. Warte auf n?chstes Event.");
                    return;
                }

                _positionOpen = true;
                _entryFillPrice = fillPrice;
                _isLongTrade = (order.Direction == OrderDirections.Buy);
                _bestSinceEntry = _entryFillPrice;

                _fillBarIndex = CurrentBar >= 0 ? CurrentBar : 0;

                _isExitPlacementPending = true;  // OnCalculate platziert TP/SL tickbasiert
                _managersInitialized = false;
                _entryState = EntryState.Filled;

                // Falls andere Referenzen dieselbe ID haben, bereinigen
                if (_pullbackOrder != null && order.Id == _pullbackOrder.Id) { _pullbackOrder = null; }
                if (_entryOrder != null && order.Id == _entryOrder.Id) { _entryOrder = null; }

                this.LogInfo($"[OnOrderChanged] Market gef?llt @ {_entryFillPrice:F5}. Exit-Platzierung pending (tickbasiert).");
                return;
            }


            // ---- B) ENTRY-FILL (Pullback ODER Entry != Market) -----
            if (
                    (
                        (_pullbackOrder != null && order.Id == _pullbackOrder.Id)
                        || (_entryOrder != null && order.Id == _entryOrder.Id && order.Type != OrderTypes.Market)
                    )
                    && IsFilledOrDone(order)
                    && !_isExitPlacementPending
                )
            {
                bool isPullback = (_pullbackOrder != null && order.Id == _pullbackOrder.Id);

                var qty = order.Filled() > 0m ? order.Filled() : GetFilledQuantity(order);
                if (qty <= 0m)
                {
                    this.LogWarn("[OnOrderChanged] Done ohne ausgef?hrte Menge ? kein Fill. Abbruch.");
                    return;
                }

                this.LogInfo($"[OnOrderChanged] {(isPullback ? "Pullback" : "Entry")} FILLED/DONE");

                var fillPrice = TryGetFillPrice(order, Security);
                if (fillPrice <= 0m)
                {
                    this.LogWarn("[OnOrderChanged] Fill erkannt, aber Fill-Preis==0. Warte auf n?chstes Event.");
                    return;
                }

                _positionOpen = true;
                _entryFillPrice = fillPrice;
                _isLongTrade = (order.Direction == OrderDirections.Buy);
                _bestSinceEntry = fillPrice;

                _fillBarIndex = CurrentBar >= 0 ? CurrentBar : 0;
                _lastSlSetBarIndex = _fillBarIndex;
                _breakEvenLevelReached = 0;
                _entryState = EntryState.Filled;

                // Referenzen bereinigen, wenn identisch
                if (_pullbackOrder != null && _entryOrder != null && _pullbackOrder.Id == _entryOrder.Id)
                {
                    this.LogInfo($"[OnOrderChanged] Clearing _entryOrder reference (same ID: {order.Id})");
                    _entryOrder = null;
                }

                // Fallback/Init via OnCalculate aktivieren
                _isExitPlacementPending = true;
                _managersInitialized = false;

                
                var dir = _isLongTrade ? OrderDirections.Buy : OrderDirections.Sell;
                var candle = TryGetCandleAtOrBefore(CurrentBar) ?? _currentCandleData;
                var levels = BuildLevelsSnapshot(_untouchedLevels);

                try
                {
                    PlaceTpSlOrders(CurrentBar, _fillBarIndex, levels, candle, isPullback, _entryFillPrice, dir);
                    this.LogInfo("[OnOrderChanged] Immediate PlaceTpSlOrders COMPLETED.");

                    // Ab hier KEINE Manager-Initialisierung und KEIN BreakEven-Aufruf.
                    // Wir heben nur das Pending-Flag auf, damit OnCalculate im n?chsten Tick ?bernimmt.
                    _isExitPlacementPending = false;
                    _managersInitialized = false;

                    this.LogInfo("[OnOrderChanged] Defer manager init and BE to OnCalculate (next tick).");
                }
                catch (Exception ex)
                {
                    this.LogWarn($"[OnOrderChanged] Immediate PlaceTpSlOrders FAILED: {ex.Message}. Fallback via OnCalculate.");
                    // _isExitPlacementPending bleibt true -> OnCalculate-Fallback greift
                }

                return;
            }



            // ---- E) POSITION GESCHLOSSEN (TP oder SL gef?llt) -----
            if ((order.Id == _tpOrder?.Id || order.Id == _slOrder?.Id) && order.Status() == OrderStatus.Filled)
            {
                _positionOpen = false;

                this.LogInfo($"[OnOrderChanged] Position wurde geschlossen. Order Id={order.Id} (Typ: {order.Type}) wurde GEF?LLT.");

                _pullbackOrder = null;
                _entryOrder = null;
                _marketOrder = null;
                _tpOrder = null;
                _slOrder = null;

                _entryFillPrice = 0;
                _breakEvenLevelReached = 0;
                _entryState = EntryState.Cancelled;
                ResetManagersState();
                ResetTradeState();

                this.LogInfo("===> Trade abgeschlossen. Strategie-Zustand wurde vollst?ndig zur?ckgesetzt. <===");
                return;
            }

            // ---- F) EXIT-ORDER GECANCELT ODER FAILED (z. B. durch OCO) -----
            if ((order.Id == _tpOrder?.Id || order.Id == _slOrder?.Id) &&
                (order.Status() == OrderStatus.Canceled || order.State == OrderStates.Failed))
            {
                this.LogInfo($"[OnOrderChanged] EXIT ORDER Id={order.Id} (Typ: {order.Type}) WURDE GECANCELT/FAILED. Wahrscheinlich durch OCO. Bereinige Zustand.");

                _positionOpen = false;

                _pullbackOrder = null;
                _entryOrder = null;
                _tpOrder = null;
                _slOrder = null;

                _entryFillPrice = 0;
                _breakEvenLevelReached = 0;
                _entryBarIndex = -1;
                _armedBarIndex = -1;
                _fillBarIndex = -1;
                _entryState = EntryState.Cancelled;
                ResetManagersState();

                this.LogInfo("===> Trade abgeschlossen (via OCO-Cancel/Failed). Strategie-Zustand wurde vollst?ndig zur?ckgesetzt. <===");
                return;
            }

            // ---- G) ENTRY/PULLBACK ORDER WURDE GECANCELT -----
            if ((_entryOrder != null && order.Id == _entryOrder.Id) || (_pullbackOrder != null && order.Id == _pullbackOrder.Id))
            {
                if (order.Status() == OrderStatus.Canceled)
                {
                    this.LogInfo($"[OnOrderChanged] Unsere Entry-Order Id={order.Id} wurde {order.Status()}.");

                    if (_tpOrder != null && _tpOrder.State == OrderStates.Active)
                    {
                        this.LogInfo($"[OnOrderChanged] Annullierung im Zusammenhang mit TP Order: {_tpOrder.Id}");
                        CancelOrder(_tpOrder);
                    }
                    if (_slOrder != null && _slOrder.State == OrderStates.Active)
                    {
                        this.LogInfo($"[OnOrderChanged] Annullierung im Zusammenhang mit SL Order: {_slOrder.Id}");
                        CancelOrder(_slOrder);
                    }

                    _positionOpen = false;
                    _pullbackOrder = null;
                    _entryOrder = null;
                    _tpOrder = null;
                    _slOrder = null;
                    _entryBarIndex = -1;
                    _armedBarIndex = -1;
                    _fillBarIndex = -1;
                    _breakEvenLevelReached = 0;
                    _entryState = EntryState.Cancelled;
                    ResetManagersState();

                    this.LogInfo("[OnOrderChanged] Alle Order-Referenzen wurden zur?ckgesetzt.");
                    ResetTradeState();
                    return;
                }
            }
        }


        private void PlaceTpSlOrders(int bar, int fillBarIndex, LevelsSnapshot levels, ATAS.Indicators.IndicatorCandle currentCandle, bool isPullback, decimal entryPrice, OrderDirections tradeDirection)
        {
            string methodSuffix = isPullback ? "-Pullback" : "-Initial";
            this.LogInfo($"[PlaceTpSlOrders{methodSuffix}] Starting - Bar: {bar}, FillBarIndex: {fillBarIndex}, TradeDir: {tradeDirection}, EntryPrice: {entryPrice:F5}, LevelsValid: {(levels != null ? "Yes" : "NO")}, CandleNull: {(currentCandle == null ? "Yes" : "No")}");

            // Gemeinsame Voraussetzungen (aus beiden alten Methoden) - mit erweitertem Logging
            if (_activeTradeSetupParams == null)
            {
                // Fallback: Setup1-Defaults setzen 
                var setup = new SetupParams
                {
                    TpTicks = 15,                    
                    SlTicks = 8,
                    BreakEvenLevelsTrendConfig = "10:2;15:10",  // Dein BE-Config
                    EarlyExitLevels = "",  // Deaktiviert
                    ProximityTicksForExit = null,
                    EarlyExitMaxDistTicks = null,
                    EarlyExitCloseAtLevel = null,
                    BeStage1Trigger = 10m,
                    TrailType = "NONE"
                };
                _activeTradeSetupParams = setup;

            }
            else
            {
                this.LogDebug($"[PlaceTpSlOrders{methodSuffix}] _activeTradeSetupParams OK: TpTicks={_activeTradeSetupParams.TpTicks}");
            }

            this.LogDebug($"[PlaceTpSlOrders{methodSuffix}] _activeTradeSetupParams OK: {(_activeTradeSetupParams != null ? "Valid" : "NULL")}");  // FIX: Kein SetupName-Zugriff, nur Null-Check

            if (currentCandle == null)
            {
                this.LogWarn($"[PlaceTpSlOrders{methodSuffix}] ABBRUCH: currentCandle == null. Retry im n?chsten Tick.");
                return;
            }
            this.LogDebug($"[PlaceTpSlOrders{methodSuffix}] currentCandle OK: High={currentCandle.High:F2}, Low={currentCandle.Low:F2}");

            if (levels == null)
            {
                this.LogWarn($"[PlaceTpSlOrders{methodSuffix}] WARN: levels == null - Fallback zu empty Snapshot? (Calculator k?nnte null returnen).");
                // Optional: levels = new LevelsSnapshot();  // F?ge das hinzu, wenn du einen Fallback brauchst
            }

            // Initialisiere Calculator falls nicht vorhanden (aus PlaceInitialTPAndSL)
            if (_tpSlCalculator == null)
            {
                _tpSlCalculator = new TpSlCalculator();
                this.LogDebug($"[PlaceTpSlOrders{methodSuffix}] _tpSlCalculator neu initialisiert.");
            }

            var tick = InstrumentInfo?.TickSize ?? 0.25m;  // Sichere Fallback

            // Signal-Kerze bestimmen und True Range berechnen (aus beiden alten Methoden, vereinigt) - unver?ndert, aber mit Log
            ATAS.Indicators.IndicatorCandle sig;
            int signalBarIndex = isPullback ? (CurrentBar - 2) : (CurrentBar - 2);
            try
            {
                if (CurrentBar >= 2)
                {
                    sig = GetCandle(signalBarIndex);
                }
                else if (CurrentBar >= 1)
                {
                    sig = GetCandle(CurrentBar - 1);
                    this.LogInfo($"[PlaceTpSlOrders{methodSuffix}] Wenige Bars, verwende Bar {CurrentBar - 1}");
                }
                else
                {
                    sig = GetCandle(CurrentBar);
                    this.LogInfo($"[PlaceTpSlOrders{methodSuffix}] Nur aktuelle Bar verf?gbar.");
                }
                this.LogDebug($"[PlaceTpSlOrders{methodSuffix}] SignalCandle OK: Index={signalBarIndex}, TR={Math.Max(sig?.High - sig?.Low ?? 0, tick):F5}");
            }
            catch (ArgumentOutOfRangeException ex)
            {
                this.LogWarn($"[PlaceTpSlOrders{methodSuffix}] GetCandle fehlgeschlagen: {ex.Message} - Fallback zu currentCandle.");
                sig = currentCandle;
            }

            decimal tr = sig != null ? Math.Max(sig.High - sig.Low, tick) : 3 * tick;

            // Pullback-spezifisch (aus PlaceExitOrders) - unver?ndert
            if (isPullback)
            {
                var lastClosedBar = GetCandle(fillBarIndex - 1);
                if (lastClosedBar == null)
                {
                    this.LogWarn($"[PlaceTpSlOrders-Pullback] ABBRUCH: lastClosedBar == null f?r Index {fillBarIndex - 1}.");
                    return;
                }

                if (_pullbackOrder == null)
                {
                    this.LogWarn("[PlaceTpSlOrders-Pullback] ABBRUCH: _pullbackOrder == null.");
                    return;
                }

                if (entryPrice <= 0m)
                {
                    entryPrice = _pullbackOrder.Price > 0m ? _pullbackOrder.Price : lastClosedBar.Close;
                    this.LogWarn($"[PlaceTpSlOrders-Pullback] EntryPrice korrigiert zu {entryPrice:F5} (Fallback: {(_pullbackOrder.Price > 0m ? "Pullback-Order" : "Bar-Close")}).");
                }
            }
            else
            {
                // Initial-spezifisch: CurrentBar setzen
                try
                {
                    _currentBar = GetCandle(CurrentBar);
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    this.LogWarn($"[PlaceTpSlOrders-Initial] GetCandle(CurrentBar={CurrentBar}) fehlgeschlagen: {ex.Message} - Fallback zu currentCandle.");
                    _currentBar = currentCandle;
                }
            }

            
            _entryFillPrice = entryPrice;
            _currentTradeDirection = tradeDirection;

            this.LogInfo($"[PlaceTpSlOrders{methodSuffix}] Entry final: {_entryFillPrice:F5}, Dir: {_currentTradeDirection}, ATR: {_currentAtrValue:F5}");

            // Context aufbauen - mit Validierungs-Logs
            // Weg-Frei Status f?r TP/SL berechnen
            bool wegFreiLong = true;
            bool wegFreiShort = true;
            
            if (EnableMicroCompositeSystem)
            {
                var tickSize = InstrumentInfo?.TickSize ?? _tickSize;
                int riskTicks = GetRiskTicks();
                wegFreiLong = IsPathFreeLongEnhanced(_entryFillPrice, DMinTicks, riskTicks, _currentMC, null, tickSize, GetPathConfig(), out _);
                wegFreiShort = IsPathFreeShortEnhanced(_entryFillPrice, DMinTicks, riskTicks, _currentMC, null, tickSize, GetPathConfig(), out _);
            }

            var tpSlContext = new TpSlContext
            {
                Bar = CurrentBar,
                Direction = _currentTradeDirection,
                Levels = levels ?? new LevelsSnapshot(),  // Fallback: Empty Snapshot, falls null
                Vwap = _currentVwapSnapshot ?? new VwapSnapshot { IsValid = false, Current = 0m },  // Fallback, falls null (verhindert NullRef)
                Candle = currentCandle ?? _currentCandleData,
                CurrentVolPerSecond = _currentBarVolPerSecond,
                AvgVolPerSecond = _currentBarAvgVolPerSecond,
                Tick = InstrumentInfo?.TickSize ?? 0.25m,
                SetupParams = _activeTradeSetupParams,
                TriggerLevel = _currentTriggerLevel,
                
                // NEU: System-Status f?r separate TP/SL-Strategien
                EnableIsBlocked = this.EnableIsBlocked,
                EnableMicroCompositeSystem = this.EnableMicroCompositeSystem,
                WegFreiLong = wegFreiLong,
                WegFreiShort = wegFreiShort
            };

            this.LogInfo($"[PlaceTpSlOrders{methodSuffix}] TpSlContext ready - LevelsNull: {levels == null}, VwapValid: {_currentVwapSnapshot?.IsValid ?? false}, SetupParams: {(_activeTradeSetupParams != null ? "OK" : "NULL")}, WegFreiLong: {wegFreiLong}, WegFreiShort: {wegFreiShort}");

            // Calculate TP/SL - mit erweitertem Catch
            TpSlResult tpSlResult;
            try
            {
                this.LogDebug($"[PlaceTpSlOrders{methodSuffix}] Calling _tpSlCalculator.Calculate(Entry={_entryFillPrice:F5}, Context).");
                tpSlResult = _tpSlCalculator.Calculate(_entryFillPrice, tpSlContext);
                _lastTpSlResult = tpSlResult;
                if (tpSlResult == null)
                {
                    this.LogWarn($"[PlaceTpSlOrders{methodSuffix}] ABBRUCH: Calculator returned NULL (ung?ltiger Context? Vwap/Levels invalid?).");
                    return;
                }
                this.LogInfo($"[PlaceTpSlOrders{methodSuffix}] Calculate SUCCESS - TP: {tpSlResult.TakeProfitPrice:F5}, SL: {tpSlResult.StopPrice:F5}");
            }
            catch (Exception ex)
            {
                this.LogWarn($"[PlaceTpSlOrders{methodSuffix}] ABBRUCH: Calculate fehlgeschlagen: {ex.Message} (Stack: {ex.StackTrace})");
                return;
            }

            // Close-Direction
            var closeDir = tradeDirection == OrderDirections.Buy ? OrderDirections.Sell : OrderDirections.Buy;

            // OCO Orders erstellen und platzieren - unver?ndert, aber mit Log
            string ocoGroupId = Guid.NewGuid().ToString();
            try
            {
                _tpOrder = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Direction = closeDir,
                    Type = OrderTypes.Limit,
                    Price = tpSlResult.TakeProfitPrice,
                    QuantityToFill = HandelsMenge,
                    OCOGroup = ocoGroupId
                };
                this.LogInfo($"[PlaceTpSlOrders{methodSuffix}] TP Order created: Price={tpSlResult.TakeProfitPrice:F5}, Qty={HandelsMenge}");

                _slOrder = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Direction = closeDir,
                    Type = OrderTypes.Stop,
                    TriggerPrice = tpSlResult.StopPrice,
                    Price = tpSlResult.StopPrice,
                    QuantityToFill = HandelsMenge,
                    OCOGroup = ocoGroupId
                };
                this.LogInfo($"[PlaceTpSlOrders{methodSuffix}] SL Order created: Trigger={tpSlResult.StopPrice:F5}, Price={tpSlResult.StopPrice:F5} (Qty={HandelsMenge})");

                _lastSlSetBarIndex = fillBarIndex;

                OpenOrder(_tpOrder);
                OpenOrder(_slOrder);
                this.LogDebug($"[PlaceTpSlOrders-Initial] _slOrder gesetzt: {_slOrder.Price} (Trigger={_slOrder.TriggerPrice})");
                _isExitPlacementPending = false;

                this.LogInfo($"[PlaceTpSlOrders{methodSuffix}] TP/SL SUCCESSFULLY PLACED & SENT!");
            }
            catch (Exception ex)
            {
                this.LogWarn($"[PlaceTpSlOrders{methodSuffix}] ABBRUCH: Order-Erstellung/Senden fehlgeschlagen: {ex.Message}");
                ResetManagersState();
                return;
            }

            if (isPullback)
            {
                this.LogInfo("[PlaceTpSlOrders-Pullback] Entry-Referenzen gecleared.");
            }

            this.LogInfo($"[PlaceTpSlOrders{methodSuffix}] COMPLETED - TP/SL for {(isPullback ? "Pullback" : "Initial")} Entry.");
        }


        private void HandlePendingTimeouts(int bar)
        {
            // Entry Timeout
            if (_entryOrder != null && _entryOrder.State == OrderStates.Active)
            {
                if (_entryBarIndex >= 0 && (bar - _entryBarIndex) >= EntryTimeoutBars)
                {
                    this.LogInfo($"[Timeout] Cancel Entry after {EntryTimeoutBars} bars.");
                    CancelOrder(_entryOrder);
                    _entryOrder = null;
                }
            }

            // Pullback Timeout
            if (_pullbackOrder != null && _pullbackOrder.State == OrderStates.Active)
            {
                if (_pullbackBarIndex >= 0 && (bar - _pullbackBarIndex) >= PullbackTimeoutBars)
                {
                    this.LogInfo($"[Timeout] Cancel Pullback after {PullbackTimeoutBars} bars.");
                    CancelOrder(_pullbackOrder);
                    _pullbackOrder = null;
                }
            }
        }
                     
                
        private void ResetTradeState()
        {
            this.LogInfo("ResetTradeState() aufgerufen: Setze alle Order- und Setup-Settings zur?ck.");

            try
            {
                this.LogInfo("ResetTradeState() aufgerufen: Setze alle Order- und Setup-Settings zur?ck.");

                // 1. Order-Referenzen zur?cksetzen
                _pullbackOrder = null;
                _entryOrder = null;
                _tpOrder = null;
                _slOrder = null;

                // 2. Position und Fill-Details zur?cksetzen
                _positionOpen = false;
                _entryFillPrice = 0;
                _breakEvenLevelReached = 0;

                // 3. Bar-Indexe zur?cksetzen
                _entryBarIndex = -1;
                _armedBarIndex = -1;
                _fillBarIndex = -1;

                // 4. Modi, Flags und Trigger zur?cksetzen
                _isPullbackMode = false;
                _pullbackTriggerPrice = 0;
                _isExitPlacementPending = false;

                // 5. Timeout-spezifische Resets
                _orderTimeoutEnabled = false;
                _orderTimeoutBars = 0;

                // 6. TimeFilter-spezifische Resets
                _orderEnableTimeFilter = false;

                // ? FIX: NULL-CHECK hinzuf?gen!
                if (_orderTradingSessions != null)
                {
                    _orderTradingSessions.Clear();
                }
                else
                {
                    this.LogInfo("[ResetTradeState] _orderTradingSessions is NULL - creating new list");
                    _orderTradingSessions = new List<TradingSession>(); // oder was auch immer der Typ ist
                }

                _orderCancelAtSessionEnd = false;
                _isInsideOrderSession = false;

                // ? WICHTIG: Setup-Parameter auch zur?cksetzen!
                _activeTradeSetupParams = null;
                _deferManagerInit = false;
                this.LogInfo("ResetTradeState() abgeschlossen: Alle Settings zur?ckgesetzt.");
            }
            catch (Exception ex)
            {
                this.LogError($"[ResetTradeState] ERROR in ResetTradeState(): {ex.Message}");
                this.LogError($"[ResetTradeState] StackTrace: {ex.StackTrace}");
            }
        }


        private void InitializeManagersAfterEntry(TpSlResult tpSlResult)
        {
            try
            {
                this.LogInfo("[InitializeManagersAfterEntry] Starte Manager-Initialisierung...");

                // **Basis-Validierung**
                if (_slOrder == null || _tpOrder == null || _entryFillPrice <= 0m)
                {
                    this.LogInfo($"[InitializeManagersAfterEntry] Basis-Validierung fehlgeschlagen: SL={_slOrder?.Id}, TP={_tpOrder?.Id}, Entry={_entryFillPrice}");
                    _managersInitialized = false;
                    return;
                }

                // **Kritischer Fix: _lastTpSlResult Null-Check**
                if (_lastTpSlResult == null)
                {
                    this.LogInfo("[InitializeManagersAfterEntry] FEHLER: _lastTpSlResult ist null!");
                    _managersInitialized = false;
                    return;
                }

                if (_lastTpSlResult.Management == null)
                {
                    this.LogInfo("[InitializeManagersAfterEntry] FEHLER: _lastTpSlResult.Management ist null!");
                    _managersInitialized = false;
                    return;
                }

                decimal entry = _entryFillPrice;
                decimal tp = _tpOrder.Price;
                decimal tick = InstrumentInfo?.TickSize ?? _tickSize;
                decimal now = Security?.LastTradePrice ?? entry;
                // NEU: Expliziter Fallback f?r _bestSinceEntry vor Init (sync mit now, falls Slippage)
                if (_bestSinceEntry <= 0m)
                {
                    _bestSinceEntry = entry;  // Starte bei Entry
                    if (now != entry)
                    {
                        // Update mit current now (post-Fill Preis)
                        if (_isLongTrade) _bestSinceEntry = Math.Max(_bestSinceEntry, now);
                        else _bestSinceEntry = Math.Min(_bestSinceEntry, now);
                        this.LogInfo($"[InitializeManagersAfterEntry] BestSinceEntry init zu {entry} und updated mit now={now} ? {_bestSinceEntry} (Long={_isLongTrade})");
                    }
                    else
                    {
                        this.LogInfo($"[InitializeManagersAfterEntry] BestSinceEntry init zu Entry={_bestSinceEntry} (now={now})");
                    }
                }
                else
                {
                    this.LogDebug($"[InitializeManagersAfterEntry] BestSinceEntry bereits gesetzt: {_bestSinceEntry}");
                }


                // Manager braucht Richtung im Sinne: OrderDirections.Buy == Long
                var positionDirection = (_slOrder.Direction == OrderDirections.Sell) ? OrderDirections.Buy : OrderDirections.Sell;
                if (_bestSinceEntry <= 0m)
                {
                    now = Security?.LastTradePrice ?? entry;
                    _bestSinceEntry = entry;  // Starte immer bei Entry (korrekt f?r "best since")
                    this.LogInfo($"[InitializeManagersAfterEntry] BestSinceEntry init zu Entry={_bestSinceEntry} (now={now})");  // NEU: Log f?r Konsistenz
                }
                else
                {
                    this.LogDebug($"[InitializeManagersAfterEntry] BestSinceEntry bereits gesetzt: {_bestSinceEntry}");  // NEU: Best?tige
                }

                // **1. BreakEvenManager initialisieren (mit Null-Checks)**
                if (_lastTpSlResult.Management.BreakEvenStages != null && _lastTpSlResult.Management.BreakEvenStages.Any())
                {
                    // NEU: Erweitere Konstruktor um initialBest (f?r sync mit globalem _bestSinceEntry)
                    _beManager = new MyNamespace.Strategies.TradeManagement.BreakEvenManager(
                        entryPrice: entry,
                        direction: positionDirection,
                        tickSize: tick,
                        stages: _lastTpSlResult.Management.BreakEvenStages,
                        initialBestPrice: _bestSinceEntry  // NEU: ?bergebe initialen Best (Name angepasst zu Konstruktor)
                    );

                    // KORRIGIERT: Stages-Log mit korrekten Properties (TriggerTicks statt ThresholdTicks)
                    string stagesLog = string.Join(", ", _lastTpSlResult.Management.BreakEvenStages.Select(s => $"Th={s.TriggerTicks}:Off={s.OffsetTicks}"));
                    this.LogInfo($"[BreakEven] Manager initialized with {_lastTpSlResult.Management.BreakEvenStages.Count} stages: {stagesLog}. InitialBest={_bestSinceEntry}");
                }
                else
                {
                    _beManager = null;
                    this.LogInfo("[BreakEven] No BreakEven stages configured. Manager set to null.");
                }

                // **2. TrailingStopManager initialisieren (mit Null-Checks)**
                if (_lastTpSlResult.Management.Trailing != null && _lastTpSlResult.Management.Trailing.TrailType != TrailingType.None)
                {
                    _trailManager = new MyNamespace.Strategies.TradeManagement.TrailingStopManager(
                        positionDirection,
                        tick,
                        _lastTpSlResult.Management.Trailing
                    );
                    this.LogInfo($"[Trailing] Manager initialized with type {_lastTpSlResult.Management.Trailing.TrailType}, offset {_lastTpSlResult.Management.Trailing.TrailOffsetTicks} ticks.");
                }
                else
                {
                    _trailManager = null;
                    this.LogInfo($"[Trailing] No Trailing configured. Manager set to null.");
                }

                // **3. Best-since-entry initialisieren (mit Null-Check)**
                
                
                _managersInitialized = true;
                this.LogInfo("[InitializeManagersAfterEntry] Manager-Initialisierung erfolgreich abgeschlossen.");
            }
            catch (Exception ex)
            {
                this.LogInfo($"[InitializeManagersAfterEntry] FEHLER: {ex.Message}");
                this.LogInfo($"[InitializeManagersAfterEntry] StackTrace: {ex.StackTrace}");
                _managersInitialized = false;

                // Cleanup bei Fehler
                _beManager = null;
                _trailManager = null;
            }
        }

        private void ResetManagersState()
        {
            _managersInitialized = false;
            _beManager = null;
            _trailManager = null;

            // Nur Manager/Best-Tracking zur?cksetzen ? Order-Refs werden an anderen Stellen bereits bereinigt.
            _bestSinceEntry = 0m;
            _isLongTrade = false;
        }


        private void ProcessBreakEvenTick()
        {
            this.LogDebug("[ProcessBreakEvenTick] ENTRY: Methode aufgerufen. (Tick-Time: " + DateTime.Now.ToString("HH:mm:ss.fff") + ")");  // NEU: Timestamp f?r Timing-Debug

            // Lokale Kopien (unver?ndert)
            var beMgr = _beManager;
            var sl = _slOrder;
            if (beMgr == null || sl == null)
            {
                this.LogDebug($"[ProcessBreakEvenTick] SKIPPED: beMgr={(beMgr == null ? "null" : "OK")}, _slOrder={(sl == null ? "null" : $"OK (Price={sl.Price})")}");
                return;
            }
            this.LogDebug($"[ProcessBreakEvenTick] Managers OK: beMgr non-null, SL Price={sl.Price} (Trigger={sl.TriggerPrice})");

            // Security.LastTradePrice (unver?ndert)
            decimal? maybePrice = Security.LastTradePrice;
            if (!maybePrice.HasValue)
            {
                this.LogDebug($"[ProcessBreakEvenTick] SKIPPED: LastTradePrice null (Security: {Security?.ToString() ?? "null"})");
                return;
            }
            decimal currentPrice = maybePrice.Value;
            this.LogDebug($"[ProcessBreakEvenTick] Price OK: Current={currentPrice}");  // Zeigt jeden Tick-Preis

            if (currentPrice <= 0m)
            {
                this.LogDebug($"[ProcessBreakEvenTick] SKIPPED: Invalid price <=0: {currentPrice}");
                return;
            }

            // BestSinceEntry updaten (global, intrabar) ? NEU: Log immer, um Ticks zu tracken
            decimal oldBest = _bestSinceEntry;
            if (_bestSinceEntry == 0m)
            {
                _bestSinceEntry = (_entryFillPrice > 0m) ? _entryFillPrice : currentPrice;
                this.LogDebug($"[ProcessBreakEvenTick] Initialized BestSinceEntry={_bestSinceEntry} (Entry={_entryFillPrice})");
            }
            else
            {
                if (_isLongTrade)
                    _bestSinceEntry = Math.Max(_bestSinceEntry, currentPrice);
                else
                    _bestSinceEntry = Math.Min(_bestSinceEntry, currentPrice);
                if (_bestSinceEntry != oldBest)
                {
                    this.LogInfo($"[ProcessBreakEvenTick] Global BestSinceEntry updated: {oldBest} -> {_bestSinceEntry} (Current={currentPrice}, Long={_isLongTrade})");  // INFO f?r Changes
                }
                else
                {
                    this.LogDebug($"[ProcessBreakEvenTick] Global BestSinceEntry unchanged: {_bestSinceEntry} (Current={currentPrice})");  // Debug f?r stille Ticks
                }
            }
            this.LogDebug($"[ProcessBreakEvenTick] Global BestSinceEntry final={_bestSinceEntry}");

            decimal currentSl = sl.TriggerPrice != 0m ? sl.TriggerPrice : sl.Price;
            this.LogDebug($"[ProcessBreakEvenTick] CurrentSL={currentSl}");

            // Manager aufrufen ? FIX: ?bergebe _bestSinceEntry als bestPrice (nicht 0m) f?r Sync
            decimal? candidateStop;
            try
            {
                var currentSlForMgr = (sl.TriggerPrice != 0m ? sl.TriggerPrice : sl.Price);

                candidateStop = beMgr.OnPriceUpdate(currentPrice, currentSlForMgr, _bestSinceEntry);  // FIX: _bestSinceEntry statt 0m

                // NEU: Log internal vs. global nach Update (f?r Konsistenz-Check)
                decimal internalBest = ((BreakEvenManager)beMgr).InternalBestPrice;  // Zugriff via Property
                if (Math.Abs(internalBest - _bestSinceEntry) > InstrumentInfo.TickSize * 0.1m)  // Toleranz
                {
                    this.LogWarn($"[ProcessBreakEvenTick] Sync-Issue: Global Best={_bestSinceEntry} vs. Internal={internalBest}");
                }

                this.LogDebug($"[ProcessBreakEvenTick] OnPriceUpdate called: Current={currentPrice}, BestPassed={_bestSinceEntry}, Candidate={(candidateStop.HasValue ? candidateStop.Value.ToString() : "null")}");

                // Profit-Kontext (unver?ndert, aber mit globalem Best)
                decimal profitDistance = _isLongTrade ? (_bestSinceEntry - _entryFillPrice) : (_entryFillPrice - _bestSinceEntry);
                decimal tickSize = InstrumentInfo?.TickSize ?? 0.25m;
                decimal profitTicks = profitDistance / tickSize;
                this.LogDebug($"[ProcessBreakEvenTick] Profit-Kontext (global): Distance={profitDistance:F2} ({profitTicks:F1} Ticks), Threshold=10 Ticks");
            }
            catch (Exception ex)
            {
                this.LogWarn($"[ProcessBreakEvenTick] BreakEvenManager.OnPriceUpdate threw: {ex.Message} | Stack: {ex.StackTrace}");
                return;
            }

            if (!candidateStop.HasValue)
            {
                decimal profitDistance = _isLongTrade ? (_bestSinceEntry - _entryFillPrice) : (_entryFillPrice - _bestSinceEntry);
                decimal tickSize = InstrumentInfo?.TickSize ?? 0.25m;
                decimal profitTicks = profitDistance / tickSize;
                this.LogDebug($"[ProcessBreakEvenTick] No candidate: Profit={profitDistance:F2} ({profitTicks:F1} Ticks) ? Threshold nicht reached?");
                return;
            }

            decimal candidate = candidateStop.Value;
            this.LogDebug($"[ProcessBreakEvenTick] Candidate received: {candidate}");

            // Verbesserung pr?fen
            bool shouldModify = _isLongTrade ? (candidate > currentSl) : (candidate < currentSl);
            if (!shouldModify)
            {
                this.LogInfo($"[BreakEven] Stage reached but no improvement. Candidate={candidate}, CurrentSL={currentSl} (Long={_isLongTrade})");
                return;
            }

            var modified = sl.Clone();
            modified.Price = candidate;
            modified.TriggerPrice = candidate;

            try
            {
                ModifyOrder(sl, modified);
                _slOrder = modified;  // Update local ref
                decimal profitDistance = _isLongTrade ? (_bestSinceEntry - _entryFillPrice) : (_entryFillPrice - _bestSinceEntry);
                this.LogInfo($"[BreakEven] SL moved {currentSl} -> {candidate} (Profit={profitDistance:F2}, Best={_bestSinceEntry}, Long={_isLongTrade})");
            }
            catch (Exception ex)
            {
                this.LogWarn($"[ProcessBreakEvenTick] ModifyOrder failed in BreakEven: {ex.Message} | Stack: {ex.StackTrace}");
            }

            this.LogDebug("[ProcessBreakEvenTick] EXIT: Methode beendet.");
        }


        private void ProcessTrailingOnBarClose(int lastClosedBar)
        {
            if (lastClosedBar < 0) return;
            if (CurrentBar >= 0 && lastClosedBar > CurrentBar) return;

            var sl = _slOrder;
            if (sl == null || sl.State != OrderStates.Active) return;
            if (sl == null || _fillBarIndex == -1 || lastClosedBar < _fillBarIndex) return;

            if (_entryFillPrice == 0m)
            {
                this.LogInfo($"[Trailing] _entryFillPrice is 0. Skipping trailing.");
                return;
            }

            // Wenn BreakEven noch aktiv und nicht komplett, blockiere Trailing
            // Diese Logik ist wichtig und bleibt bestehen.
            if (_beManager != null && !_beManager.IsCompleted)
            {
                this.LogInfo($"[Trailing] BreakEvenManager is still active. Skipping trailing for bar {lastClosedBar}.");
                return;
            }

            var trailMgr = _trailManager;
            if (trailMgr == null)
            {
                this.LogInfo($"[Trailing] TrailingStopManager is null. Skipping trailing for bar {lastClosedBar}.");
                return;
            }

            // rawCandle als object, damit die folgenden "is"-Checks legal sind
            // Wir ?bergeben den aktuellen Bar-Index, _entryFillPrice, _bestSinceEntry, sl.Price und die Kerze.
            object rawCandle = GetCandle(lastClosedBar);
            if (rawCandle == null)
            {
                this.LogInfo($"[Trailing] GetCandle({lastClosedBar}) returned null. Aborting trailing.");
                return;
            }

            ATAS.Indicators.IndicatorCandle? ic;
            ATAS.Indicators.Candle? c;
            if (!TryExtractCandle(rawCandle, out ic, out c))
            {
                this.LogInfo("[Trailing] Nicht unterst?tzter Kerzentyp und keine bekannte innere Kerzeneigenschaft - Abbruch.");
                return;
            }


            decimal currentStopLossPrice = sl.Price;
            decimal? candidateStop = null;

            try
            {
                int currentBarIndex = lastClosedBar;
                if (ic != null)
                    candidateStop = trailMgr.ProcessTrailing(currentBarIndex, _entryFillPrice, _bestSinceEntry, currentStopLossPrice, ic);
                else if (c != null)
                    candidateStop = trailMgr.ProcessTrailing(currentBarIndex, _entryFillPrice, _bestSinceEntry, currentStopLossPrice, c);
            }
            catch (Exception ex)
            {
                this.LogInfo($"[ProcessTrailingOnBarClose] TrailingStopManager.ProcessTrailing threw: {ex.Message}");
                return;
            }

            if (!candidateStop.HasValue) return;

            var candidate = candidateStop.Value;
            // Die Bedingung, ob modifiziert werden soll, ist jetzt bereits im TrailingStopManager enthalten.
            // Hier pr?fen wir nur noch, ob der zur?ckgegebene Candidate einen besseren SL darstellt,
            // um nicht unn?tige ModifyOrder-Aufrufe zu t?tigen, wenn der TrailingManager z.B.
            // nur den gleichen SL zur?ckgeben w?rde, aber eigentlich keine Modifikation intendiert war.
            bool shouldModify = _isLongTrade ? (candidate > currentStopLossPrice) : (candidate < currentStopLossPrice);

            if (!shouldModify)
            {
                this.LogInfo($"[Trailing] Candidate stop ({candidate}) does not improve current SL ({currentStopLossPrice}). No modification.");
                return;
            }

            // Sicherstellen, dass der neue SL auf Tick-Gr??e gerundet ist.
            // Dies ist wichtig, um Fehler bei der Order-Platzierung zu vermeiden.
            candidate = Math.Round(candidate / _tickSize) * _tickSize;

            // Nur modifizieren, wenn sich der Preis tats?chlich unterscheidet (nach Rundung)
            if (candidate == currentStopLossPrice)
            {
                this.LogInfo($"[Trailing] Candidate stop ({candidate}) is same as current SL ({currentStopLossPrice}) after rounding. No modification.");
                return;
            }

            // Da ModifyOrder einen Clone erwartet, ist die vorhandene Logik hier korrekt.
            var modified = sl.Clone();
            modified.Price = modified.TriggerPrice = candidate;

            try
            {
                ModifyOrder(sl, modified);
                _slOrder = modified; // Wichtig: Referenz auf die neue Order aktualisieren!
                this.LogInfo($"[Trailing] SL successfully moved from {currentStopLossPrice} to {candidate}");
            }
            catch (Exception ex)
            {
                this.LogInfo($"[ProcessTrailingOnBarClose] ModifyOrder failed in Trailing: {ex.Message}. Details: {ex.StackTrace}");
            }
        }



        // Diese Methode wird von ATAS aufgerufen, wenn das ChACart neu gezeichnet werden muss.
        protected override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            base.OnRender(context, layout);

            // Auto-Aktivierung: Wenn Visualisierung gew?nscht aber Level-System deaktiviert
            if (EnableSignificantPreviousLevels && !EnableIsBlocked)
            {
                EnableIsBlocked = true;
                this.LogInfo("[LEVEL-AUTO] Level-System automatisch aktiviert, da Visualisierung gew?nscht.");
            }

            // Auto-Aktivierung: Wenn Visualisierung gew?nscht aber MicroComposite-System deaktiviert
            if (ShowMicroCompositeLevels && !EnableMicroCompositeSystem)
            {
                EnableMicroCompositeSystem = true;
                this.LogInfo("[MC-AUTO] MicroComposite-System automatisch aktiviert, da Visualisierung gew?nscht.");
            }

            if (!EnableSignificantPreviousLevels && !ShowMicroCompositeLevels)
                return;

            int x1 = ChartInfo.PriceChartContainer.Region.Width - Length;
            int x2 = ChartInfo.PriceChartContainer.Region.Width;

            // 1) Vortages/Vorwochen-Levels
            if (EnableSignificantPreviousLevels && EnableIsBlocked)
            {
                var levels = _untouchedLevels?.ToArray();
                if (levels != null && levels.Length > 0)
                {
                    foreach (var level in levels)
                    {
                        if (!level.IsActive) continue;
                        int y = (int)ChartInfo.GetYByPrice(level.Value, false);
                        context.DrawLine(_renderPen, x1, y, x2, y);
                        DrawLabelOnPriceAxis(context, level.Label, y, _axisFont, _lineColor, _axisTextColor);
                    }
                }
            }

            // 2) MicroComposite-Levels/Zonen
            if (ShowMicroCompositeLevels && EnableMicroCompositeSystem)
            {
                var mc = _currentMC ?? GetRollingMicroComposite();
                if (mc != null)
                {
                    int yPOC = (int)ChartInfo.GetYByPrice(mc.POC, false);
                    int yVAH = (int)ChartInfo.GetYByPrice(mc.VAH, false);
                    int yVAL = (int)ChartInfo.GetYByPrice(mc.VAL, false);

                    // VA/POC Pens
                    var penPOC = new RenderPen(System.Drawing.Color.Orange, 2);
                    var penVA = new RenderPen(System.Drawing.Color.Gray, 1);

                    // Farben getauscht: HVN = Rot, LVN = Gr?n
                    var penHVNEdge = new RenderPen(System.Drawing.Color.FromArgb(180, System.Drawing.Color.IndianRed), 1);
                    penHVNEdge.DashPattern = new float[] { 4f, 3f };
                    var penLVNEdge = new RenderPen(System.Drawing.Color.FromArgb(180, System.Drawing.Color.ForestGreen), 1);
                    penLVNEdge.DashPattern = new float[] { 4f, 3f };

                    var penHVNCenter = new RenderPen(System.Drawing.Color.IndianRed, 2);
                    var penLVNCenter = new RenderPen(System.Drawing.Color.ForestGreen, 2);

                    // Value Area Band
                    if (yVAH != yVAL)
                    {
                        int top = Math.Min(yVAH, yVAL);
                        int bottom = Math.Max(yVAH, yVAL);
                        var vaRect = new System.Drawing.Rectangle(x1, top, x2 - x1, bottom - top);
                        context.FillRectangle(System.Drawing.Color.FromArgb(45, System.Drawing.Color.LightGray), vaRect);
                    }

                    // VAH/VAL Linien + Labels
                    context.DrawLine(penVA, x1, yVAH, x2, yVAH);
                    DrawLabelOnPriceAxis(context, "VAH-V", yVAH, _axisFont, System.Drawing.Color.Gray, _axisTextColor);

                    context.DrawLine(penVA, x1, yVAL, x2, yVAL);
                    DrawLabelOnPriceAxis(context, "VAL-V", yVAL, _axisFont, System.Drawing.Color.Gray, _axisTextColor);

                    // POC Linie + Label
                    context.DrawLine(penPOC, x1, yPOC, x2, yPOC);
                    DrawLabelOnPriceAxis(context, "POC-V", yPOC, _axisFont, System.Drawing.Color.Orange, _axisTextColor);

                    // HVN-Zonen (Rot)
                    if (mc.HVNZones != null && mc.HVNZones.Count > 0 && ShowZoneRects)
                    {
                        int hvnZoneWidth = (x2 - x1) / 2;
                        int hvnX1 = x2 - hvnZoneWidth;

                        foreach (var z in mc.HVNZones)
                        {
                            decimal pStart = z.Item1;
                            decimal pEnd = z.Item2;

                            int yStart = (int)ChartInfo.GetYByPrice(pStart, false);
                            int yEnd = (int)ChartInfo.GetYByPrice(pEnd, false);
                            int top = Math.Min(yStart, yEnd);
                            int bottom = Math.Max(yStart, yEnd);
                            if (bottom == top) bottom = top + 1;

                            var rect = new System.Drawing.Rectangle(hvnX1, top, x2 - hvnX1, bottom - top);
                            context.FillRectangle(System.Drawing.Color.FromArgb(50, System.Drawing.Color.IndianRed), rect);

                            context.DrawLine(penHVNEdge, hvnX1, yStart, x2, yStart);
                            context.DrawLine(penHVNEdge, hvnX1, yEnd, x2, yEnd);

                            DrawLabelOnPriceAxis(context, "HVN", top, _axisFont, System.Drawing.Color.IndianRed, _axisTextColor);

                            // Optional: genau eine Centerline je finaler Zone
                            if (ShowZoneCenters)
                            {
                                decimal centerPrice = (pStart + pEnd) / 2m;
                                int yCenter = (int)ChartInfo.GetYByPrice(centerPrice, false);
                                context.DrawLine(penHVNCenter, hvnX1, yCenter, x2, yCenter);
                            }
                        }
                    }

                    // LVN-Zonen (Gr?n)
                    if (mc.LVNZones != null && mc.LVNZones.Count > 0 && ShowZoneRects)
                    {
                        int lvnZoneWidth = (x2 - x1) / 3;
                        int lvnX1 = x2 - lvnZoneWidth;

                        foreach (var z in mc.LVNZones)
                        {
                            decimal pStart = z.Item1;
                            decimal pEnd = z.Item2;

                            int yStart = (int)ChartInfo.GetYByPrice(pStart, false);
                            int yEnd = (int)ChartInfo.GetYByPrice(pEnd, false);
                            int top = Math.Min(yStart, yEnd);
                            int bottom = Math.Max(yStart, yEnd);
                            if (bottom == top) bottom = top + 1;

                            var rect = new System.Drawing.Rectangle(lvnX1, top, x2 - lvnX1, bottom - top);
                            context.FillRectangle(System.Drawing.Color.FromArgb(45, System.Drawing.Color.ForestGreen), rect);

                            context.DrawLine(penLVNEdge, lvnX1, yStart, x2, yStart);
                            context.DrawLine(penLVNEdge, lvnX1, yEnd, x2, yEnd);

                            DrawLabelOnPriceAxis(context, "LVN", top, _axisFont, System.Drawing.Color.ForestGreen, _axisTextColor);

                            // Optional: genau eine Centerline je finaler Zone
                            if (ShowZoneCenters)
                            {
                                decimal centerPrice = (pStart + pEnd) / 2m;
                                int yCenter = (int)ChartInfo.GetYByPrice(centerPrice, false);
                                context.DrawLine(penLVNCenter, lvnX1, yCenter, x2, yCenter);
                            }
                        }
                    }

                    // WICHTIG: Kandidaten/Peaks nicht zeichnen
                    // Entfernt: Schleifen ?ber mc.HVNs / mc.LVNs (das waren Centerlines vieler Kandidaten)
                }
            }
        }



        // Ende OnRender Methode



        private Order CreateStopLimitOrder(OrderDirections direction, decimal price)
        {
            return new Order
            {
                Portfolio = Portfolio,
                Security = Security,
                Direction = direction,
                Type = OrderTypes.StopLimit,
                TriggerPrice = price,
                Price = price,
                QuantityToFill = HandelsMenge
            };
        }
        private Order CreateStopLimitOrder(OrderDirections direction, decimal triggerPrice, decimal limitPrice)
        {
            return new Order
            {
                Portfolio = Portfolio,
                Security = Security,
                Direction = direction,
                Type = OrderTypes.StopLimit,
                TriggerPrice = triggerPrice, // Stop-Trigger
                Price = limitPrice,          // Limit
                QuantityToFill = HandelsMenge
            };
        }

        private bool HasLiveEntryOrder()
        {
            try
            {
                bool pullActive = _pullbackOrder != null && _pullbackOrder.State == OrderStates.Active;
                bool entryActive = _entryOrder != null && _entryOrder.State == OrderStates.Active;
                bool marketActive = _marketOrder != null && _marketOrder.State == OrderStates.Active;
                return pullActive || entryActive || marketActive;
            }
            catch
            {
                // konservativ: wenn State nicht lesbar ist (Rebinding/Runtime-Tausch), behandle als aktiv
                return true;
            }
        }


        private Order GetActiveEntryOrder()
        {
            // bevorzugt die live-aktive
            if (_pullbackOrder != null && _pullbackOrder.State == OrderStates.Active) return _pullbackOrder;
            if (_entryOrder != null && _entryOrder.State == OrderStates.Active) return _entryOrder;
            // fallback: erste nicht-null
            return _pullbackOrder ?? _entryOrder;
        }
        private void PlacePullbackEntry(OrderDirections direction, decimal price, int bar)
        {

            if (HasLiveEntryOrder())
            {
                this.LogInfo("[PlacePullbackEntry] Es existiert bereits eine aktive Pullback-Order. Ignoriere.");
                return;
            }

            // Tick-Ausrichtung ohne Hilfsmethoden
            decimal alignedPrice = price;
            if (_tickSize > 0m)
            {
                var steps = price / _tickSize;
                alignedPrice = (direction == OrderDirections.Buy)
                    ? Math.Ceiling(steps) * _tickSize    // Long: nach oben
                    : Math.Floor(steps) * _tickSize;     // Short: nach unten
            }

            if (alignedPrice <= 0m)
            {
                this.LogWarn($"[PlacePullbackEntry] Abbruch: Ausgerichteter Preis <= 0 (aligned={alignedPrice}, raw={price}).");
                return;
            }

            // Trigger sauber initialisieren: immer = Limitpreis
            _pullbackTriggerPrice = alignedPrice;
            _isPullbackMode = false;            // Noch nicht im Trailing-Modus
            _pullbackBarIndex = bar;
            _entryBarIndex = bar;

            var pullback = new Order
            {
                Portfolio = Portfolio,
                Security = Security,
                Direction = direction,
                Type = OrderTypes.Limit,        // Initial als Limit-Order
                Price = alignedPrice,           // tick-ausgerichtet
                TriggerPrice = alignedPrice,    // Trigger explizit setzen, niemals 0 lassen
                QuantityToFill = HandelsMenge
            };

            _pullbackOrder = pullback;

            this.LogInfo($"[PlacePullbackEntry] Platziere Pullback Limit: Dir={direction}, Px={alignedPrice}, Qty={HandelsMenge}.");
            OpenOrder(pullback);
        }

        private void PlaceMarketEntry(OrderDirections direction, int bar)
        {
            if (HasLiveEntryOrder())
            {
                this.LogInfo("[PlaceMarketEntry] Es existiert bereits eine aktive Order. Ignoriere.");
                return;
            }

                     

            var market = new Order
            {
                Portfolio = Portfolio,
                Security = Security,
                Direction = direction, 
                Type = OrderTypes.Market,         // Zum aktuellen Marktpreis schlie?en
                QuantityToFill = HandelsMenge
            };

            _marketOrder = market;

            OpenOrder(market);
            this.LogInfo($"[PlaceMarketEntry] Platziere Market Order: Dir={direction}, Qty={HandelsMenge}."); 

            // Die Initialisierung der Manager (TP/SL) erfolgt, sobald die Order gef?llt ist,
            // da OnOrderChanged den _entryFillPrice aktualisiert und dann InitializeManagersAfterEntry aufruft.
        }



        // --- HILFSMETHODE ZUM PLATZIEREN EINER ENTRY ORDER -------------------- (Beibehalten)
        // Diese Methode wird von CheckEntrySignal aufgerufen, wenn ein Entry-Signal erkannt wird.
        private void PlaceEntry(OrderDirections direction, decimal price, int bar)
        {
            //this.LogInfo($"[DBG-ORDER] PlaceEntry called for bar={bar}, price={price}, time={DateTime.UtcNow:O}");
            // Logge den Versuch, eine Entry Order zu platzieren
            //this.LogInfo($"[PlaceEntry] Versuche, Entry Order zu platzieren: Direction={direction}, Price={price:F5}, SL/TP Ticks={ticks}, Bar={bar}.");

            // Guard gegen Mehrfach-Platzierung im selben Takt (sollte durch CheckEntrySignal verhindert werden, aber doppelte Pr?fung schadet nicht)
            if (HasLiveEntryOrder())
            {
                this.LogInfo("[PlaceEntry] Es existiert bereits eine aktive Entry-Order. Ignoriere.");
                return;
            }

            

            // ggf. alte Exit-Orders verwerfen (falls diese Methode aus irgendeinem Grund erneut aufgerufen w?rde,
            // obwohl noch Exit-Orders aus alten Trades ausstehen, was nicht passieren sollte)
            // Beachten Sie, dies cancelt keine aktiven Orders, setzt nur interne Referenzen auf null.
            _tpOrder = null; _slOrder = null;

            var entry = CreateStopLimitOrder(direction, price);
            _entryOrder = entry;
            _entryBarIndex = bar;
            this.LogInfo($"[PlaceEntry SET] entryBarIndex={_entryBarIndex} timeoutBars={_orderTimeoutBars} " +
                 $"timeoutBarIndex={_entryBarIndex + _orderTimeoutBars}");

            // Sende-Log inkl. tats?chlich verwendeter Menge
            this.LogInfo($"[PlaceEntry] Sende StopLimit: Dir={direction}, Trg={price:F5}, Qty={HandelsMenge}.");
            OpenOrder(entry);
            //this.LogInfo($"[DBG-ORDER] PlaceOrder returned:  time={DateTime.UtcNow:O}");

        } // Ende PlaceEntry Methode

        private void ArmIntrabarEntry(int bar,bool isLong,int bandIdx,decimal bandLevel,decimal signalBarHigh,decimal signalBarLow,decimal targetLevel,string marketSpeed,decimal volZ, decimal? longTriggerOverride = null, decimal? shortTriggerOverride = null,int? reclaimTicksOverride = null,int? nearTicksOverride = null)
        {
            // Nur aus Idle/Cancelled arming ? vermeidet Doppel-Arms
            if (_entryState != EntryState.Idle && _entryState != EntryState.Cancelled)
            {
                this.LogInfo($"[EntryArm] SKIP: State={_entryState} not idle/cancelled.");
                return;
            }

            _entryIsLong = isLong;
            _entryBandIdx = bandIdx;
            _entryBandLevel = bandLevel;
            _entryTargetLevel = targetLevel;

            _signalBarIndex = bar;
            _signalBarHigh = signalBarHigh;
            _signalBarLow = signalBarLow;

            _entryMarketSpeed = marketSpeed;
            _entryVolZ = volZ;

            // NEU: konsistente Offsets f?rs Setup
            EntryVersatzTicks = (_entryMarketSpeed == "Fast") ? 1 : 0;
            StopTriggerTicks = (_entryVolZ >= 1.0m) ? 2 : 1;

            // Overrides speichern
            _longTriggerOverride = longTriggerOverride;
            _shortTriggerOverride = shortTriggerOverride;
            _reclaimTicksOverride = reclaimTicksOverride;
            _nearTicksOverride = nearTicksOverride;

            _touchTsUtc = DateTime.UtcNow;
            _entryState = EntryState.TouchArmed;

            this.LogInfo($"[EntryArm] Armed: dir={(isLong ? "Long" : "Short")}, bandIdx={bandIdx}, band={bandLevel:F2}, target={targetLevel:F2}, sigH={signalBarHigh:F2}, sigL={signalBarLow:F2}, volZ={volZ:F2}, speed={marketSpeed}, bar={bar}, trigOvL={_longTriggerOverride?.ToString("F2") ?? "-"}, trigOvS={_shortTriggerOverride?.ToString("F2") ?? "-"}");
        }
        private decimal ComputeFinalLongTrigger(decimal tick)
        {
            if (_longTriggerOverride.HasValue) return _longTriggerOverride.Value;

            // Fallback: Extrem-Break wie bisher
            return _signalBarHigh + EntryVersatzTicks * tick;
        }

        private decimal ComputeFinalShortTrigger(decimal tick)
        {
            if (_shortTriggerOverride.HasValue) return _shortTriggerOverride.Value;

            // Fallback: Extrem-Break wie bisher
            return _signalBarLow - EntryVersatzTicks * tick;
        }
        // Continuation-StopLimit: nur platzieren, wenn positive Rejection und keine aktive Pullback/Continuation-Order und keine offene Position
        private void PlaceContinuationIfEligible(decimal volZ)
        {
            if (!_evalPositive) return;
            if (_positionOpen) return;
            if (HasLiveEntryOrder()) return;
            if (!IsOrderflowValidForContinuation(_entryIsLong)) return;

            decimal tick = InstrumentInfo?.TickSize ?? _tickSize;
            int off = (volZ >= 1.0m) ? 2 : StopLimitOffsetTicks;

            decimal stop = _entryIsLong
                ? _signalBarHigh + StopTriggerTicks * tick
                : _signalBarLow - StopTriggerTicks * tick;

            decimal limit = _entryIsLong
                ? stop + off * tick
                : stop - off * tick;

            if (_tickSize > 0m)
            {
                var sSteps = stop / _tickSize;
                stop = (_entryIsLong ? Math.Ceiling(sSteps) : Math.Floor(sSteps)) * _tickSize;

                var lSteps = limit / _tickSize;
                limit = (_entryIsLong ? Math.Ceiling(lSteps) : Math.Floor(lSteps)) * _tickSize;

                if (_entryIsLong && limit < stop) limit = stop;
                if (!_entryIsLong && limit > stop) limit = stop;
            }

            var dir = _entryIsLong ? OrderDirections.Buy : OrderDirections.Sell;

            try
            {
                var order = CreateStopLimitOrder(dir, stop, limit);
                _pullbackOrder = order;    // eine einheitliche Entry-Referenz
                _isPullbackMode = true;    // StopLimit-Modus
                _entryState = EntryState.ContinuationPlaced;
                _entryBarIndex = CurrentBar;
                OpenOrder(order);

                this.LogInfo($"[Continuation] Placed StopLimit: dir={dir}, stop={stop:F2}, limit={limit:F2}");
            }
            catch (Exception ex)
            {
                _pullbackOrder = null;
                this.LogWarn($"[Continuation] OpenOrder failed: {ex.Message}");
            }
        }

        private void CancelArmedEntry()
        {
            // interne Logs
            this.LogInfo("[EntryArm] Cancel armed entry");

            // Status zur?cksetzen
            _entryState = EntryState.Cancelled;  // oder Idle, je nach gew?nschtem Flow
            _armedBarIndex = -1;

            // Overrides l?schen
            _longTriggerOverride = null;
            _shortTriggerOverride = null;
            _reclaimTicksOverride = null;
            _nearTicksOverride = null;
        }


        private void CancelAllOpenOrdersIfAny()
        {
            foreach (var o in new[] { _pullbackOrder, _entryOrder, _tpOrder, _slOrder })
            {
                if (o == null) continue;
                try
                {
                    if (o.State == OrderStates.Active)
                        CancelOrder(o);
                }
                catch { /* ignore */ }
            }
            _pullbackOrder = null;
            _entryOrder = null;
            _tpOrder = null;
            _slOrder = null;
        }




        private void CleanupInactiveOrders()
        {
            if (_entryOrder != null && (_entryOrder.State == OrderStates.None || _entryOrder.State == OrderStates.Failed))
            {
                this.LogInfo($"[Cleanup] Entry Order: {_entryOrder.Direction} {_entryOrder.State}");
                _entryOrder = null;
                _activeTradeSetupParams = null;
                _entryBarIndex = -1;
                _armedBarIndex = -1;
            }

            // Pullback Order: ?hnlich
            if (_pullbackOrder != null && (_pullbackOrder.State == OrderStates.None || _pullbackOrder.State == OrderStates.Failed))
            {
                this.LogInfo($"[Cleanup] Pullback Order: {_pullbackOrder.Direction} {_pullbackOrder.State}");
                _pullbackOrder = null;
            }
            
        }
        private void Research_TryAddCandidateForSetup(
            int bar,
            string setupId,
            MarketRegimeDetails marketRegimeDetails,// KEIN NULLABLE, da es immer berechnet wird
            MyNamespace.Strategies.TradeManagement.ResearchDirection? forceDir = null,
            // ... bestehende S6-Parameter ...
            decimal? s6_MinSignalsRequired = null,
            decimal? s6_ThVolBurstZ = null,
            decimal? s6_ThCvdImpulseLong = null,
            decimal? s6_ThCvdImpulseShort = null,
            bool? s6_LongSetupValid = null,
            string s6_LongFailedReasons = null, // Kann nicht direkt in ExtraFeatures<decimal>
            bool? s6_ShortSetupValid = null,
            string s6_ShortFailedReasons = null, // Kann nicht direkt in ExtraFeatures<decimal>
            decimal? s6_GateMode = null,
            decimal? s6_Regime = null, // Das ist bereits das Enum als Dezimal
            int? s6_DistToPocTicks = null,
            bool? s6_PocDistanceOk = null,
            bool? s6_CanProceedLong = null,
            bool? s6_CanProceedShort = null
        )
        {
            this.LogInfo($"[TryAdd] TryAdd for {setupId}: researchEnabled={_researchEnabled}, snapshot null?={_currentLevelsSnapshot == null}");

            if (!_researchEnabled || _research == null || _currentLevelsSnapshot == null) return;

            var c = GetCandle(bar);
            decimal entry = (decimal)c.Close;

            bool isVwapSetup = setupId.StartsWith("S3-") || setupId.StartsWith("S5-");  // S3/S5: VWAP-Bounces
            bool isVahValSetup = setupId.StartsWith("S4-");  // S4: VAH/VAL-Pullback

            // Du hast diese Werte bereits:
            var atr = _currentAtrValue;
            var vwapSnapshot = _currentVwapSnapshot;
            decimal? vwap = _currentVwapSnapshot?.Current;
            decimal? vwapUpper1 = vwapSnapshot?.UpperBand1;
            decimal? vwapLower1 = vwapSnapshot?.LowerBand1;
            decimal? vwapUpper2 = vwapSnapshot?.UpperBand2;
            decimal? vwapLower2 = vwapSnapshot?.LowerBand2;
            decimal? vwapUpper3 = vwapSnapshot?.UpperBand3;
            decimal? vwapLower3 = vwapSnapshot?.LowerBand3;

            int? distToVwapTicks = (vwap.HasValue && vwap.Value != 0m)
               ? TicksBetween(entry, vwap.Value)
               : (int?)null;

            int? distToVwapUpper1Ticks = (vwapUpper1.HasValue && vwapUpper1.Value > 0m)
               ? TicksBetween(entry, vwapUpper1.Value)
               : (int?)null;

            int? distToVwapLower1Ticks = (vwapLower1.HasValue && vwapLower1.Value > 0m)
               ? TicksBetween(entry, vwapLower1.Value)
               : (int?)null;

            int? distToVwapUpper2Ticks = (vwapUpper2.HasValue && vwapUpper2.Value > 0m)
               ? TicksBetween(entry, vwapUpper2.Value)
               : (int?)null;

            int? distToVwapLower2Ticks = (vwapLower2.HasValue && vwapLower2.Value > 0m)
               ? TicksBetween(entry, vwapLower2.Value)
               : (int?)null;

            int? distToVwapUpper3Ticks = (vwapUpper3.HasValue && vwapUpper3.Value > 0m)
               ? TicksBetween(entry, vwapUpper3.Value)
               : (int?)null;

            int? distToVwapLower3Ticks = (vwapLower3.HasValue && vwapLower3.Value > 0m)
               ? TicksBetween(entry, vwapLower3.Value)
               : (int?)null;

            
            // Snapshot-Logik: CurrentVAH/VAL sind decimal (default 0m), g?ltig nur wenn > 0m gesetzt (aus BuildLevelsSnapshot)
            var snap = _currentLevelsSnapshot;  // F?r bessere Lesbarkeit
            bool isNearVAH = snap.CurrentVAH > 0m && Math.Abs(DistIfSet(entry, snap.CurrentVAH)) <= _researchProximityTicks;
            bool isNearVAL = snap.CurrentVAL > 0m && Math.Abs(DistIfSet(entry, snap.CurrentVAL)) <= _researchProximityTicks;

            // Optional: Debug-Log f?r Snapshot-Werte (entferne nach Test)
            this.LogInfo($"[TryAdd] Snapshot Debug: CurrentVAH={snap.CurrentVAH} (valid? {snap.CurrentVAH > 0m}), CurrentVAL={snap.CurrentVAL} (valid? {snap.CurrentVAL > 0m}), isNearVAH={isNearVAH}, isNearVAL={isNearVAL}");

            // Setup-spezifische Level-Bestimmung (ersetzt den einfachen FindNearestRelevantLevel-Aufruf)
            string lvlKey = "NA";
            decimal lvlPrice = 0m;
            int distTicks = int.MaxValue;

            if (isVwapSetup)
            {
                // Erweiterte VWAP-Band-Priorisierung (neu: Schleife f?r alle Bands)
                var vwapBands = new (decimal? price, string key, int? dist)[]
                {
                    (vwap, "VWAP", distToVwapTicks),
                    (vwapUpper1, "VWAP-Upper1", distToVwapUpper1Ticks),
                    (vwapLower1, "VWAP-Lower1", distToVwapLower1Ticks),
                    (vwapUpper2, "VWAP-Upper2", distToVwapUpper2Ticks),
                    (vwapLower2, "VWAP-Lower2", distToVwapLower2Ticks),
                    (vwapUpper3, "VWAP-Upper3", distToVwapUpper3Ticks),
                    (vwapLower3, "VWAP-Lower3", distToVwapLower3Ticks)
                };
                distTicks = int.MaxValue;
                foreach (var band in vwapBands)
                {
                    if (band.dist.HasValue && band.price.HasValue && band.price.Value > 0m && Math.Abs(band.dist.Value) < distTicks)
                    {
                        distTicks = Math.Abs(band.dist.Value);
                        lvlKey = band.key;
                        lvlPrice = band.price.Value;
                    }
                }
            }
            else if (isVahValSetup)
            {
                // F?r S4: Priorisiere VAH/VAL aus Snapshot (KORRIGIERT: > 0m statt HasValue und .Value)
                // Snapshot-Logik: Nur wenn > 0m (aus BuildLevelsSnapshot-Mapping, z. B. "VAH aktuell")
                if (isNearVAH && snap.CurrentVAH > 0m)
                {
                    lvlPrice = snap.CurrentVAH;  // Direkter Zugriff, kein .Value (da decimal)
                    distTicks = DistIfSet(entry, snap.CurrentVAH);
                    lvlKey = "CurrentVAH";
                }
                else if (isNearVAL && snap.CurrentVAL > 0m)
                {
                    lvlPrice = snap.CurrentVAL;  // Direkter Zugriff, kein .Value (da decimal)
                    distTicks = DistIfSet(entry, snap.CurrentVAL);
                    lvlKey = "CurrentVAL";
                }
                else
                {
                    // Fallback zu Snapshot (z. B. andere Levels wie PP, R1 etc.)
                    var (fallbackKey, fallbackPrice, fallbackDist) = FindNearestRelevantLevel(entry, _currentLevelsSnapshot);
                    if (fallbackDist < distTicks)
                    {
                        lvlKey = fallbackKey;
                        lvlPrice = fallbackPrice;
                        distTicks = fallbackDist;
                    }
                }
            }
            else
            {
                // F?r S1/S2: Standard Snapshot-Check (z. B. POC, Pivots aus BuildLevelsSnapshot)
                var (fallbackKey, fallbackPrice, fallbackDist) = FindNearestRelevantLevel(entry, _currentLevelsSnapshot);
                lvlKey = fallbackKey;
                lvlPrice = fallbackPrice;
                distTicks = fallbackDist;
            }

            // Setup-spezifischer Proximity-Check (locker f?r VWAP-Setups)
            // DistIfSet handhabt 0m-Werte korrekt (aus Snapshot-Logik)
            int maxProximity = isVwapSetup || isVahValSetup ? _researchProximityTicks * 2 : _researchProximityTicks;  // z. B. doppelt so weit f?r VWAP
            this.LogInfo($"[TryAdd] TryAdd {setupId}: Nearest Level={lvlKey} @ {lvlPrice}, distTicks={distTicks}, proximityThreshold={maxProximity}");
            if (distTicks < 0 || distTicks > maxProximity)
            {
                this.LogInfo($"[TryAdd] SKIPPED {setupId}: distTicks={distTicks} > {maxProximity}");  // Debug (angepasst zu LogInfo)
                return;
            }
            this.LogInfo($"[TryAdd] TryAdd {setupId} SUCCESS: Adding candidate with lvl={lvlKey}, dist={distTicks}");  // Erweitert um neue Infos

            // Richtung: forceDir priorisieren, dann Level-Crossover, Fallback VWAP (unver?ndert, kompatibel mit neuer lvlPrice)
            var dir = forceDir ?? (
                (lvlPrice != 0m && c.Low <= lvlPrice && c.Close > lvlPrice) ? MyNamespace.Strategies.TradeManagement.ResearchDirection.Long :
                (lvlPrice != 0m && c.High >= lvlPrice && c.Close < lvlPrice) ? MyNamespace.Strategies.TradeManagement.ResearchDirection.Short :
                (vwap != 0m && c.Close < vwap ? MyNamespace.Strategies.TradeManagement.ResearchDirection.Short : MyNamespace.Strategies.TradeManagement.ResearchDirection.Long)
            );
            this.LogInfo($"[TryAdd] TryAdd {setupId} dir={dir}");  // Optional: Extra Log f?r Richtung

            // OV direkt aus deinen Dictionaries (unver?ndert)
            var volBurstZ = SafeGet(_volBurstZ, bar);
            var cvdImpulse = SafeGet(_cvdImpulse, bar);
            var cvdCoherence = SafeGet(_cvdCoherence, bar);
            var aggPressure = SafeGet(_aggPressure, bar);
            var tradeRateZ = SafeGet(_tradeRateZ, bar);
            var efficiency = SafeGet(_efficiency, bar);
            var maxBull = SafeGet(_maxCounterShareBull, bar);
            var maxBear = SafeGet(_maxCounterShareBear, bar);

            // IttZ (Tempo) & Sweep (Richtung)
            var ittZ_raw = SafeGet(_ittZ_raw, bar);
            var ittZ_bull = SafeGet(_ittZ_bull, bar);
            var ittZ_bear = SafeGet(_ittZ_bear, bar);

            int sweepDir = SafeGet(_sweepDir, bar, 0);
            bool sweepUp = SafeGet(_sweepUp, bar, false);
            bool sweepDn = SafeGet(_sweepDn, bar, false);

            // Optional: Streak-L?nge der aktuellen Richtung
            int sweepStreak = SweepStreak(_sweepDir, bar);

            // MicroComposite: direkt aus _currentMC (unver?ndert)
            var mc = _currentMC;
            decimal? vpPOC = (mc != null && mc.POC > 0m) ? mc.POC : (decimal?)null;
            decimal? vpVAH = (mc != null && mc.VAH > 0m) ? mc.VAH : (decimal?)null;
            decimal? vpVAL = (mc != null && mc.VAL > 0m) ? mc.VAL : (decimal?)null;

            int dPOC = vpPOC.HasValue ? Math.Abs(TicksBetween(entry, vpPOC.Value)) : 0;
            int dVAH = vpVAH.HasValue ? Math.Abs(TicksBetween(entry, vpVAH.Value)) : 0;
            int dVAL = vpVAL.HasValue ? Math.Abs(TicksBetween(entry, vpVAL.Value)) : 0;

            decimal? hvn1 = (mc != null && mc.HVNs != null && mc.HVNs.Count > 0) ? mc.HVNs[0] : (decimal?)null;
            decimal? lvn1 = (mc != null && mc.LVNs != null && mc.LVNs.Count > 0) ? mc.LVNs[0] : (decimal?)null;
            int hvn1Dist = hvn1.HasValue ? Math.Abs(TicksBetween(entry, hvn1.Value)) : 0;
            int lvn1Dist = lvn1.HasValue ? Math.Abs(TicksBetween(entry, lvn1.Value)) : 0;

            // ?Weg frei? ? du hast bereits IsNoResistanceInPath / IsNoSupportInPath (unver?ndert)
            int isWegFrei = 1, firstObstacleTicks = 0; // falls du FirstObstacle nicht brauchst, 0 lassen
            if (mc != null)
            {
                int riskTicksResearch = GetRiskTicks();
                bool frei = (dir == MyNamespace.Strategies.TradeManagement.ResearchDirection.Long)
                    ? IsPathFreeLongEnhanced(entry, _researchPathCheckTicks, riskTicksResearch, mc, null, _tickSize, GetPathConfig(), out _)
                    : IsPathFreeShortEnhanced(entry, _researchPathCheckTicks, riskTicksResearch, mc, null, _tickSize, GetPathConfig(), out _);

                isWegFrei = frei ? 1 : 0;
            }


            // Distanzen zu Kern-Levels aus deinem Snapshot (unver?ndert)
            // Snapshot-Logik: DistIfSet pr?ft implizit > 0m f?r decimal-Felder
            int dPDH = DistIfSet(entry, snap.PreviousDayHigh);
            int dPDL = DistIfSet(entry, snap.PreviousDayLow);
            int dPDPOC = DistIfSet(entry, snap.PreviousDayPOC);

            int dCurPOC = DistIfSet(entry, snap.CurrentPOC);
            int dCurVAH = DistIfSet(entry, snap.CurrentVAH);
            int dCurVAL = DistIfSet(entry, snap.CurrentVAL);

            // RoundLevels sind decimal? (aus BuildLevelsSnapshot), daher HasValue/Value
            int dRoundBelow = snap.RoundLevelBelow.HasValue ? Math.Abs(TicksBetween(entry, snap.RoundLevelBelow.Value)) : 0;
            int dRoundAbove = snap.RoundLevelAbove.HasValue ? Math.Abs(TicksBetween(entry, snap.RoundLevelAbove.Value)) : 0;

            // BlockLevels sind wahrscheinlich decimal? (??= Operator), daher HasValue/Value
            int dBlockRes = snap.LastBlockResistance.HasValue ? Math.Abs(TicksBetween(entry, snap.LastBlockResistance.Value)) : 0;
            int dBlockSup = snap.LastBlockSupport.HasValue ? Math.Abs(TicksBetween(entry, snap.LastBlockSupport.Value)) : 0;

            // >>> NEU: Extraktion der tats?chlichen VAH/VAL/POC-Preise (aktuell und previous, >0m f?r G?ltigkeit)
            decimal? currentPOC = snap.CurrentPOC > 0m ? snap.CurrentPOC : (decimal?)null;
            decimal? currentVAH = snap.CurrentVAH > 0m ? snap.CurrentVAH : (decimal?)null;
            decimal? currentVAL = snap.CurrentVAL > 0m ? snap.CurrentVAL : (decimal?)null;
            decimal? previousPOC = snap.PreviousDayPOC > 0m ? snap.PreviousDayPOC : (decimal?)null;  // PreviousDayPOC als POC vom Vortag
            decimal? previousVAH = snap.PreviousDayVAH > 0m ? snap.PreviousDayVAH : (decimal?)null;  // Annahme: Existiert im Snapshot
            decimal? previousVAL = snap.PreviousDayVAL > 0m ? snap.PreviousDayVAL : (decimal?)null;  // Annahme: Existiert im Snapshot
                                                                                               // <<< NEU

            // Input erstellen (unver?ndert, aber mit neuen lvlKey/lvlPrice/distTicks)
            var input = new MyNamespace.Strategies.TradeManagement.ResearchCandidateInput
            {
                StartBarIndex = bar,
                Time = c.Time,
                Symbol = InstrumentInfo?.Instrument ?? InstrumentInfo?.ToString() ?? "",
                Timeframe = _researchTimeframeLabel,
                SetupId = setupId,
                Direction = dir,

                LevelKey = string.IsNullOrEmpty(lvlKey) ? "NA" : lvlKey,  // Setup-spezifisch
                LevelPrice = lvlPrice,  // Setup-spezifisch
                EntryPrice = entry,

                ATR = atr,
                VWAP = vwap,
                DistToVWAPTicks = distToVwapTicks,
                VWAP_UpperBand1 = vwapUpper1,
                DistToVWAPUpper1Ticks = distToVwapUpper1Ticks,
                VWAP_LowerBand1 = vwapLower1,
                DistToVWAPLower1Ticks = distToVwapLower1Ticks,

                VWAP_UpperBand2 = vwapUpper2,
                DistToVWAPUpper2Ticks = distToVwapUpper2Ticks,
                VWAP_LowerBand2 = vwapLower2,
                DistToVWAPLower2Ticks = distToVwapLower2Ticks,

                VWAP_UpperBand3 = vwapUpper3,
                DistToVWAPUpper3Ticks = distToVwapUpper3Ticks,
                VWAP_LowerBand3 = vwapLower3,
                DistToVWAPLower3Ticks = distToVwapLower3Ticks,

                DistToLevelTicks = (distTicks == int.MaxValue) ? -1 : distTicks,  // Oder -1, oder was auch immer "ung?ltig" bedeutet


                // OV (unver?ndert)
                VolBurstZ = volBurstZ,
                CvdImpulse = cvdImpulse,
                CvdCoherence = cvdCoherence,
                AggPressure = aggPressure,
                TradeRateZ = tradeRateZ,
                Efficiency = efficiency,
                MaxCounterDeltaShare = null, // nicht separat vorhanden
                MaxCounterShareBull = maxBull,
                MaxCounterShareBear = maxBear,
                IttZ_Raw = ittZ_raw,
                IttZ_Bull = ittZ_bull,
                IttZ_Bear = ittZ_bear,

                SweepDir = sweepDir,
                SweepUp = sweepUp ? 1 : 0,
                SweepDn = sweepDn ? 1 : 0,
                SweepStreak = sweepStreak,


                // VP30 / MicroComposite (unver?ndert)
                VP30_POC = vpPOC,
                VP30_VAH = vpVAH,
                VP30_VAL = vpVAL,
                VP30_DistToPOCTicks = dPOC,
                VP30_DistToVAHTicks = dVAH,
                VP30_DistToVALTicks = dVAL,
                VP30_IsWegFrei = isWegFrei,
                VP30_FirstObstacleTicks = firstObstacleTicks,
                VP30_HVN1Price = hvn1,
                VP30_HVN1DistTicks = hvn1Dist,
                VP30_LVN1Price = lvn1,
                VP30_LVN1DistTicks = lvn1Dist,

                // Day-/Pivot-/Block-/Round (unver?ndert)
                DistToPDHTicks = dPDH,
                DistToPDLTicks = dPDL,
                DistToPDPOCTicks = dPDPOC,
                DistToPOCTicks = dCurPOC,
                DistToVAHTicks = dCurVAH,
                DistToVALTicks = dCurVAL,
                DistToRoundBelowTicks = dRoundBelow,
                DistToRoundAboveTicks = dRoundAbove,
                DistToBlockResTicks = dBlockRes,
                DistToBlockSupTicks = dBlockSup,

                // >>> NEU: VAH/VAL/POC-Preise hinzuf?gen (nach Distanzen)
                CurrentPOC = currentPOC,
                CurrentVAH = currentVAH,
                CurrentVAL = currentVAL,
                PreviousPOC = previousPOC,
                PreviousVAH = previousVAH,
                PreviousVAL = previousVAL,
                // <<< NEU

                HorizonBars = _researchHorizonBars,
                ExtraFeatures = new Dictionary<string, decimal>()
            };

            // Zusatzflags aus Snapshot (optional) (unver?ndert)
            // Snapshot-Logik: IsBlockedLong/Short aus BuildLevelsSnapshot (Blocker-Erkennung)
            if (snap.IsBlockedLong) input.ExtraFeatures["IS_BLOCKED_LONG"] = 1m;
            if (snap.IsBlockedShort) input.ExtraFeatures["IS_BLOCKED_SHORT"] = 1m;
            if (snap.SessionHighs.Count > 0) input.ExtraFeatures["SESSION_HIGH_LAST"] = snap.SessionHighs[0].Value;
            if (snap.SessionLows.Count > 0) input.ExtraFeatures["SESSION_LOW_LAST"] = snap.SessionLows[0].Value;

            // Alle Extra-Level ?bernehmen (ohne vorhandene zu ?berschreiben) (unver?ndert)
            // Snapshot-Logik: Extra aus Fallbacks und PutExtraIfPositive (z. B. POC, VAH, R1 etc.)
            if (snap.Extra != null)
            {
                foreach (var kv in snap.Extra)
                {
                    if (kv.Value <= 0m) continue;
                    if (!input.ExtraFeatures.ContainsKey(kv.Key))
                        input.ExtraFeatures[kv.Key] = kv.Value;
                }
            }

            // >>> NEU: Kompakte Labels f?r einfache Klassifizierung
            // Tempo-Label: -1 = FAST, 0 = NEUTRAL, +1 = SLOW
            int tempoLabel = (ittZ_raw <= -1m) ? -1 : (ittZ_raw >= 1m ? 1 : 0);
            input.ExtraFeatures["ITTZ_TEMPO_LABEL"] = (decimal)tempoLabel;

            // Sweep-Labels (0m/1m)
            input.ExtraFeatures["SWEEP_ACTIVE"] = (sweepDir != 0 || sweepUp || sweepDn) ? 1m : 0m;
            input.ExtraFeatures["SWEEP_CONSISTENT_STREAK_GE2"] = (sweepStreak >= 2) ? 1m : 0m;
            // <<< NEU
            // --- NEU: GENERISCHE REGIME DETAILS ZU EXTRAFEATURES HINZUF?GEN ---
            // Pr?fen Sie auf null-Werte der Eigenschaften, falls diese nullable sind,
            // oder auf Standardwerte wie 0m, wenn sie nur >0m g?ltig sind.
            if (marketRegimeDetails.Regime != null) input.ExtraFeatures["REGIME_FINAL_ENUM"] = (decimal)marketRegimeDetails.Regime;
            input.ExtraFeatures["REGIME_FAST_VOTES"] = marketRegimeDetails.FastVotes;
            input.ExtraFeatures["REGIME_SLOW_VOTES"] = marketRegimeDetails.SlowVotes;
            input.ExtraFeatures["REGIME_IS_HIGH_VOL"] = marketRegimeDetails.IsHighVol ? 1m : 0m;
            input.ExtraFeatures["REGIME_VPS"] = marketRegimeDetails.Vps;
            input.ExtraFeatures["REGIME_VPS_EMA"] = marketRegimeDetails.VpsEma;
            input.ExtraFeatures["REGIME_VPS_STD"] = marketRegimeDetails.VpsStd;
            input.ExtraFeatures["REGIME_Z_SCORE"] = marketRegimeDetails.ZScore;
            input.ExtraFeatures["REGIME_TRADES_PER_SEC_Z"] = marketRegimeDetails.TradesPerSecZ;
            input.ExtraFeatures["REGIME_SECONDS_PER_BAR"] = marketRegimeDetails.SecondsPerBar;
            // --- ENDE NEU: GENERISCHE REGIME DETAILS ---
            // --- NEU: S6 Spezifische adaptive Schwellenwerte & Parameter hinzuf?gen ---
            if (setupId == "S6-VARejection") // Nur f?r S6 hinzuf?gen
            {
                if (s6_MinSignalsRequired.HasValue) input.ExtraFeatures["S6_MIN_SIGNALS_REQUIRED"] = s6_MinSignalsRequired.Value;
                if (s6_ThVolBurstZ.HasValue) input.ExtraFeatures["S6_TH_VOL_BURST_Z"] = s6_ThVolBurstZ.Value;
                if (s6_ThCvdImpulseLong.HasValue) input.ExtraFeatures["S6_TH_CVD_IMPULSE_LONG"] = s6_ThCvdImpulseLong.Value;
                if (s6_ThCvdImpulseShort.HasValue) input.ExtraFeatures["S6_TH_CVD_IMPULSE_SHORT"] = s6_ThCvdImpulseShort.Value;

                if (s6_LongSetupValid.HasValue) input.ExtraFeatures["S6_LONG_SETUP_VALID"] = s6_LongSetupValid.Value ? 1m : 0m;
                // Hinweis: s6_LongFailedReasons kann hier nicht direkt gespeichert werden, da ExtraFeatures decimal-Werte erwartet.
                // F?r die Speicherung von String-basierten Gr?nden m?sste ResearchCandidateInput um ein Dictionary<string, string>
                // oder dedizierte String-Felder erweitert werden.
                if (s6_ShortSetupValid.HasValue) input.ExtraFeatures["S6_SHORT_SETUP_VALID"] = s6_ShortSetupValid.Value ? 1m : 0m;
                // Hinweis: s6_ShortFailedReasons kann hier nicht direkt gespeichert werden, siehe oben.

                if (s6_GateMode.HasValue) input.ExtraFeatures["S6_GATE_MODE"] = s6_GateMode.Value;
                if (s6_Regime.HasValue) input.ExtraFeatures["S6_REGIME"] = s6_Regime.Value;
                if (s6_DistToPocTicks.HasValue) input.ExtraFeatures["S6_DIST_TO_POC_TICKS"] = s6_DistToPocTicks.Value;
                if (s6_PocDistanceOk.HasValue) input.ExtraFeatures["S6_POC_DISTANCE_OK"] = s6_PocDistanceOk.Value ? 1m : 0m;
                if (s6_CanProceedLong.HasValue) input.ExtraFeatures["S6_CAN_PROCEED_LONG"] = s6_CanProceedLong.Value ? 1m : 0m;
                if (s6_CanProceedShort.HasValue) input.ExtraFeatures["S6_CAN_PROCEED_SHORT"] = s6_CanProceedShort.Value ? 1m : 0m;
            }
            // --- ENDE NEU: S6 Spezifische adaptive Schwellenwerte & Parameter hinzuf?gen ---

            this.LogInfo($"[AddCandidate] ADDED {setupId}: lvl={lvlKey}, dist={distTicks}, dir={dir}");

            _research.AddCandidate(input);
        }

        private ResearchBarInput GetLastBarInput()
        {
            if (CurrentBar <= 0)
            {
                this.LogInfo("GetLastBarInput: Keine Bars verf?gbar (CurrentBar <= 0).");
                return null;  // Fallback: FinalizePending nutzt 0-Werte
            }

            int lastBarIndex = CurrentBar - 1;  // Letzter vollst?ndiger Bar-Index

            try
            {
                // ATAS: Hole Candle f?r den letzten Index (genau wie in OnCalculate)
                var lastCandle = GetCandle(lastBarIndex);
                if (lastCandle == null)
                {
                    this.LogInfo($"[GetLastBarInput] Fehler: GetCandle({lastBarIndex}) returned null.");
                    return null;
                }

                // Erstelle ResearchBarInput EXAKT wie in OnCalculate (kein Open/Volume, da nicht ben?tigt)
                var input = new ResearchBarInput
                {
                    BarIndex = lastBarIndex,  // F?ge den Index hinzu (wie in OnCalculate)
                    Time = lastCandle.Time,   // DateTime
                    High = (decimal)lastCandle.High,  // Cast zu decimal (f?r Research-Ticks)
                    Low = (decimal)lastCandle.Low,
                    Close = (decimal)lastCandle.Close
                    // Kein Open oder Volume ? passt zu deinem OnCalculate-Code
                };

                // Optional: Debug-Log (wie in OnCalculate, aber f?r letzten Bar)
                this.LogInfo($"[GetLastBarInput] LastBarInput erstellt: BarIndex={lastBarIndex}, Time={input.Time}, Close={input.Close}.");

                return input;
            }
            catch (Exception ex)
            {
                this.LogInfo($"[GetLastBarInput] Fehler beim Erstellen von LastBarInput (Index {lastBarIndex}): {ex.Message}.");
                return null;  // Fallback ? verhindert Crash
            }
        }
        // Wird aufgerufen, wenn die Strategie manuell oder durch die Plattform gestoppt wird (Beibehalten)
        protected override void OnStopping()
        {

            base.OnStopping();


            this.LogInfo($"[OnStopping] Strategie wird gestoppt. Bar={CurrentBar - 1}.");

            if (_research != null)
            {
                try
                {
                    // 1. Finalisiere unvollst?ndige Pending-Samples (mit letztem Bar f?r Genauigkeit)
                    ResearchBarInput lastBarInput = null;
                    if (CurrentBar > 0)  // Stelle sicher, dass Bars verarbeitet wurden
                    {
                        lastBarInput = GetLastBarInput();  // ATAS-spezifische Hilfsmethode (siehe unten)
                        this.LogInfo($"[OnStopping] Finalisiere pending Samples mit letztem Bar: {lastBarInput?.Time}.");
                    }
                    else
                    {
                        this.LogInfo("Keine Bars verarbeitet ? kein finaler Bar f?r Pending.");
                    }

                    _research.FinalizePending(lastBarInput);

                    // Optional: Status loggen (f?ge LogStatus-Methode in ResearchCollector hinzu, falls nicht da)
                    // _research.LogStatus("OnStopping: ");

                    // 2. Flush alle Buffers in CSVs
                    _research.Flush();

                    this.LogInfo("[OnStopping] Research finalisiert und CSVs geschrieben.");
                }
                catch (Exception ex)
                {
                    this.LogInfo($"[OnStopping] Fehler beim Finalisieren/Flush von Research: {ex.Message}. Stack: {ex.StackTrace}");
                }
                finally
                {
                    _research = null;  // Cleanup ? verhindert Memory-Leaks
                }
            }
           
            CancelAllOpenOrdersIfAny();

            if (_csvWriter != null)
            {
                try
                {
                    _csvWriter.Dispose();
                }
                catch { /* swallow exceptions during shutdown, oder logge */ }
                _csvWriter = null;
            }


            // Wir setzen unsere internen Felder auf null, da die Orders jetzt nicht mehr relevant sind.
            _entryOrder = null;
            _pullbackOrder = null;
            _marketOrder = null;
            _tpOrder = null;
            _slOrder = null;
            _entryBarIndex = -1;
            _armedBarIndex = -1;
            _pullbackBarIndex = -1;
            _lastTimeoutCheckBarIndex = -1;
            this.LogInfo("[OnStopping] Strategie OnStopping abgeschlossen. Offene Orders sollten gecancelt sein.");

            if (_marketStateEngine != null)
            {
                if (_marketStateUpdatedHandler != null)
                {
                    try
                    {
                        _marketStateEngine.MarketStateUpdated -= _marketStateUpdatedHandler;
                    }
                    catch (Exception ex)
                    {
                        this.LogWarn($"[OnStopping] Fehler beim Abmelden von MarketStateUpdated: {ex.Message}");
                    }
                    _marketStateUpdatedHandler = null;
                }
                _marketStateEngine = null;
            }

           
        } // Ende OnStopping Methode
        
    }

}























































































