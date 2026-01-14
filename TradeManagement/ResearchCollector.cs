using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.Text.Json.Nodes;

namespace MyNamespace.Strategies.TradeManagement
{
    public enum ResearchDirection
    {
        Neutral = 0,  // Neu: Keine klare Richtung (z.B. POC ohne Bias, oder No-Signal)
        Long = 1,     // Bullish: Long-Setup (z.B. Bounce von Support)
        Short = -1    // Bearish: Short-Setup (z.B. Bounce von Resistance)
    }

    public sealed class ResearchCandidateInput
    {
        // Identifikation
        public int StartBarIndex { get; set; }
        public DateTime Time { get; set; }
        public string Symbol { get; set; }
        public string Timeframe { get; set; }
        public string SetupId { get; set; }
        public ResearchDirection Direction { get; set; }

        // Entry & Level
        public string LevelKey { get; set; }          // z.B. "POC","VAH","PDH","RoundBelow",...
        public decimal LevelPrice { get; set; }
        public decimal EntryPrice { get; set; }

        // Price/Context
        public decimal? ATR { get; set; }
        public decimal? VWAP { get; set; }
        public int? DistToVWAPTicks { get; set; }
        public int DistToLevelTicks { get; set; }
        // Neue VWAP-Bänder
        public decimal? VWAP_UpperBand1 { get; set; }
        public int? DistToVWAPUpper1Ticks { get; set; }
        public decimal? VWAP_LowerBand1 { get; set; }
        public int? DistToVWAPLower1Ticks { get; set; }
        public decimal? VWAP_UpperBand2 { get; set; }
        public int? DistToVWAPUpper2Ticks { get; set; }
        public decimal? VWAP_LowerBand2 { get; set; }
        public int? DistToVWAPLower2Ticks { get; set; }
        public decimal? VWAP_UpperBand3 { get; set; }
        public int? DistToVWAPUpper3Ticks { get; set; }
        public decimal? VWAP_LowerBand3 { get; set; }
        public int? DistToVWAPLower3Ticks { get; set; }



        // Orderflow (OV)
        public decimal? VolBurstZ { get; set; }
        public decimal? CvdImpulse { get; set; }
        public decimal? CvdCoherence { get; set; }
        public decimal? AggPressure { get; set; }
        public decimal? TradeRateZ { get; set; }
        public decimal? Efficiency { get; set; }
        public decimal? MaxCounterDeltaShare { get; set; }
        public decimal? MaxCounterShareBull { get; set; }
        public decimal? MaxCounterShareBear { get; set; }
        public decimal? CounterDelta { get; set; }
        public decimal? IttZ_Raw { get; set; }
        public decimal? IttZ_Bull { get; set; }
        public decimal? IttZ_Bear { get; set; }

        public int SweepDir { get; set; }    // -1/0/+1
        public int SweepUp { get; set; }     // 0/1
        public int SweepDn { get; set; }     // 0/1
        public int SweepStreak { get; set; } // Länge der aktuellen Richtung (Bars)

        // Rolling Volume Profile (30 Bars)
        public decimal? VP30_POC { get; set; }
        public decimal? VP30_VAH { get; set; }
        public decimal? VP30_VAL { get; set; }
        public int VP30_DistToPOCTicks { get; set; }
        public int VP30_DistToVAHTicks { get; set; }
        public int VP30_DistToVALTicks { get; set; }
        public int VP30_IsWegFrei { get; set; }               // 0/1
        public int VP30_FirstObstacleTicks { get; set; }      // +Ticks
        public decimal? VP30_HVN1Price { get; set; }
        public int VP30_HVN1DistTicks { get; set; }
        public decimal? VP30_LVN1Price { get; set; }
        public int VP30_LVN1DistTicks { get; set; }

        // >>> NEU: VAH/VAL/POC-Preise (aktuell und previous)
        public decimal? CurrentPOC { get; set; }
        public decimal? CurrentVAH { get; set; }
        public decimal? CurrentVAL { get; set; }
        public decimal? PreviousPOC { get; set; }
        public decimal? PreviousVAH { get; set; }
        public decimal? PreviousVAL { get; set; }
        // <<< NEU

        // Globale Level-Distanzen
        public int DistToPDHTicks { get; set; }
        public int DistToPDLTicks { get; set; }
        public int DistToPOCTicks { get; set; }
        public int DistToVAHTicks { get; set; }
        public int DistToVALTicks { get; set; }
        public int DistToPDPOCTicks { get; set; }
        public int DistToRoundBelowTicks { get; set; }
        public int DistToRoundAboveTicks { get; set; }
        public int DistToBlockResTicks { get; set; }
        public int DistToBlockSupTicks { get; set; }

        // Fenster
        public int HorizonBars { get; set; }

        // Erweiterbar
        public Dictionary<string, decimal> ExtraFeatures { get; set; } = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ResearchBarInput
    {
        public int BarIndex { get; set; }
        public DateTime Time { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
    }

    internal sealed class ResearchSample
    {
        public int StartBarIndex;
        public DateTime Time;
        public string Symbol;
        public string Timeframe;
        public string SetupId;
        public ResearchDirection Direction;
        public string LevelKey;
        public decimal LevelPrice;
        public decimal EntryPrice;

        public decimal? ATR;
        public decimal? VWAP;
        public int? DistToVWAPTicks;
        // Neue VWAP-Bänder
        public decimal? VWAP_UpperBand1;
        public int? DistToVWAPUpper1Ticks;
        public decimal? VWAP_LowerBand1;
        public int? DistToVWAPLower1Ticks;
        public decimal? VWAP_UpperBand2;
        public int? DistToVWAPUpper2Ticks;
        public decimal? VWAP_LowerBand2;
        public int? DistToVWAPLower2Ticks;
        public decimal? VWAP_UpperBand3;
        public int? DistToVWAPUpper3Ticks;
        public decimal? VWAP_LowerBand3;
        public int? DistToVWAPLower3Ticks;
        public int DistToLevelTicks;

        public decimal? VolBurstZ;
        public decimal? CvdImpulse;
        public decimal? CvdCoherence;
        public decimal? AggPressure;
        public decimal? TradeRateZ;
        public decimal? Efficiency;
        public decimal? MaxCounterDeltaShare;
        public decimal? MaxCounterShareBull;
        public decimal? MaxCounterShareBear;
        public decimal? CounterDelta;
        public decimal? IttZ_Raw;
        public decimal? IttZ_Bull;
        public decimal? IttZ_Bear;

        public int SweepDir;
        public int SweepUp;
        public int SweepDn;
        public int SweepStreak;

        public decimal? VP30_POC;
        public decimal? VP30_VAH;
        public decimal? VP30_VAL;
        public int VP30_DistToPOCTicks;
        public int VP30_DistToVAHTicks;
        public int VP30_DistToVALTicks;
        public int VP30_IsWegFrei;
        public int VP30_FirstObstacleTicks;
        public decimal? VP30_HVN1Price;
        public int VP30_HVN1DistTicks;
        public decimal? VP30_LVN1Price;
        public int VP30_LVN1DistTicks;

        // >>> NEU: VAH/VAL/POC-Preise
        public decimal? CurrentPOC;
        public decimal? CurrentVAH;
        public decimal? CurrentVAL;
        public decimal? PreviousPOC;
        public decimal? PreviousVAH;
        public decimal? PreviousVAL;
        // <<< NEU

        public int DistToPDHTicks;
        public int DistToPDLTicks;
        public int DistToPOCTicks;
        public int DistToVAHTicks;
        public int DistToVALTicks;

        public int DistToPDPOCTicks;
        public int DistToRoundBelowTicks;
        public int DistToRoundAboveTicks;
        public int DistToBlockResTicks;
        public int DistToBlockSupTicks;

        public int HorizonBars;
        public int BarsLeft;

        public decimal MFE;       // in Ticks
        public decimal MAE;       // in Ticks
        public int TimeToMFE;     // Bars (0-basiert)
        public int TimeToMAE;     // Bars (0-basiert)

        public string ExtraFeaturesJson;

        // Pfad relativ zum Entry (richtungsnormalisiert: Long=+1, Short=-1)
        public List<decimal> HiTicks = new List<decimal>(64);
        public List<decimal> LoTicks = new List<decimal>(64);
        public List<decimal> CloseTicks = new List<decimal>(64);
    }

    public sealed class ResearchCollector
    {
        private readonly decimal _tickSize;
        private readonly int _outcomeTicks;  // TP benchmark in Ticks
        private readonly int _stopTicks;     // SL benchmark in Ticks

        private readonly string _runId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

        private string _baseDir = @"C:\Users\User\Documents\Strategieauswertung";
        private readonly Dictionary<string, List<string>> _buffers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _headerWrittenFor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private const int AutoFlushThreshold = 4000;

        private readonly List<ResearchSample> _pending = new List<ResearchSample>(2048);
        // >>> NEU: Public Property für Pending-Count (read-only, sicher)
        public int PendingCount => _pending.Count;
        // <<< NEU
        public ResearchCollector(decimal tickSize, int outcomeTicks, int stopTicks, string baseDir)
        {
            if (tickSize <= 0) throw new ArgumentOutOfRangeException(nameof(tickSize));
            if (outcomeTicks <= 0) throw new ArgumentOutOfRangeException(nameof(outcomeTicks));
            if (stopTicks <= 0) throw new ArgumentOutOfRangeException(nameof(stopTicks));
            if (string.IsNullOrWhiteSpace(baseDir)) throw new ArgumentNullException(nameof(baseDir));

            _tickSize = tickSize;
            _outcomeTicks = outcomeTicks;
            _stopTicks = stopTicks;
            _baseDir = baseDir;
        }

        private static string SanitizeFileNamePart(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "UnknownSetup";
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s;
        }

        private string GetFilePath(DateTime time, string setupId)
        {
            var day = time.ToUniversalTime().ToString("yyyy-MM-dd", CsvCulture);
            var setup = SanitizeFileNamePart(setupId);
            var fileName = $"{day}_{setup}.csv";
            var dir = _baseDir;
            return Path.Combine(dir, fileName);
        }

        private List<string> GetBuffer(string filePath)
        {
            if (!_buffers.TryGetValue(filePath, out var buf))
            {
                buf = new List<string>(8192);
                _buffers[filePath] = buf;
            }
            return buf;
        }

        private void EnsureHeader(string filePath)
        {
            if (_headerWrittenFor.Contains(filePath)) return;

            if (File.Exists(filePath))
            {
                using var sr = new StreamReader(filePath);
                var first = sr.ReadLine();
                if (!string.IsNullOrEmpty(first) && first.StartsWith("Time,Symbol,Timeframe,SetupId,Direction", StringComparison.Ordinal))
                {
                    _headerWrittenFor.Add(filePath);
                    return;
                }
            }
            var buf = GetBuffer(filePath);
            buf.Add(string.Join(",",
                // Identifikation
                "Time", "Symbol", "Timeframe", "SetupId", "Direction",
                // Entry & Level
                "LevelKey", "LevelPrice", "EntryPrice", "DistToLevelTicks",
                // Price/Context
                "ATR", "VWAP", "DistToVWAPTicks",
                // Neue VWAP-Bänder
                "VWAP_UpperBand1", "DistToVWAPUpper1Ticks", "VWAP_LowerBand1", "DistToVWAPLower1Ticks",
                "VWAP_UpperBand2", "DistToVWAPUpper2Ticks", "VWAP_LowerBand2", "DistToVWAPLower2Ticks",
                "VWAP_UpperBand3", "DistToVWAPUpper3Ticks", "VWAP_LowerBand3", "DistToVWAPLower3Ticks",
                // OV
                "VolBurstZ", "CvdImpulse", "CvdCoherence", "AggPressure", "TradeRateZ", "Efficiency",
                "MaxCounterDeltaShare", "MaxCounterShareBull", "MaxCounterShareBear",
                // >>> NEU: IttZ & Sweep
                "IttZ_Raw", "IttZ_Bull", "IttZ_Bear",
                "SweepDir", "SweepUp", "SweepDn", "SweepStreak",
                // VP30
                "VP30_POC", "VP30_VAH", "VP30_VAL",
                "VP30_DistToPOCTicks", "VP30_DistToVAHTicks", "VP30_DistToVALTicks",
                "VP30_IsWegFrei", "VP30_FirstObstacleTicks",
                "VP30_HVN1Price", "VP30_HVN1DistTicks", "VP30_LVN1Price", "VP30_LVN1DistTicks",
                // >>> NEU: VAH/VAL/POC-Preise (nach VP30, vor Distanzen)
                "CurrentPOC", "CurrentVAH", "CurrentVAL",
                "PreviousPOC", "PreviousVAH", "PreviousVAL",
                // <<< NEU
                // Globale Level-Distanzen
                "DistToPDHTicks", "DistToPDLTicks", "DistToPOCTicks", "DistToVAHTicks", "DistToVALTicks",
                "DistToPDPOCTicks", "DistToRoundBelowTicks", "DistToRoundAboveTicks", "DistToBlockResTicks", "DistToBlockSupTicks",
                // Fenster/Outcomes
                "HorizonBars", "MFE", "MAE", "TimeToMFE", "TimeToMAE",
                "Outcome_MFE_GE_TP", "Outcome_CloseRet_GE_TP", "CloseRetTicks",
                "Outcome_TP_before_SL_within_H",
                // Pfade
                "Path_HiTicks", "Path_LoTicks", "Path_CloseTicks",
                // Erweiterbar
                "ExtraFeaturesJson"
            ));
            _headerWrittenFor.Add(filePath);
        }

        // >>> ADDED: Session-Handling für Europe/Berlin (inkl. Sommerzeit)
        private static readonly TimeZoneInfo BerlinTz =
            TryGetTz("Europe/Berlin") ?? TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");

        private static TimeZoneInfo TryGetTz(string id)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch { return null; }
        }

        // 09:00 inklusive, 21:00 exklusiv (lokal Berlin)
        private static bool IsInBerlinSession(DateTime time)
        {
            var utc = time.Kind == DateTimeKind.Utc ? time : time.ToUniversalTime();
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, BerlinTz);
            var tod = local.TimeOfDay;
            return tod >= new TimeSpan(9, 0, 0) && tod < new TimeSpan(21, 0, 0);
        }
        // <<< ADDED

        public void AddCandidate(ResearchCandidateInput input)
        {
            if (input == null) return;
            // Optional: Falls HorizonBars <= 0, Candidate nicht anlegen (ansonsten würde 0 faktisch 1 Bar bedeuten)
            // if (input.HorizonBars <= 0) return;

            // >>> ADDED: Session-Filter (nur 09:00–21:00 Europe/Berlin)
            if (!IsInBerlinSession(input.Time))
                return;
            // <<< ADDED

            // >>> KORRIGIERT: VWAP 0.0000 -> null normalisieren + DistToVWAPTicks leeren, wenn kein VWAP
            var vwapNorm = (input.VWAP.HasValue && input.VWAP.Value > 0m) ? input.VWAP : null;
            int? distToVwapTicksNorm = vwapNorm.HasValue ? input.DistToVWAPTicks : null;

            var vwapUpper1Norm = (input.VWAP_UpperBand1.HasValue && input.VWAP_UpperBand1.Value > 0m) ? input.VWAP_UpperBand1 : null;
            int? distToVwapUpper1TicksNorm = vwapUpper1Norm.HasValue ? input.DistToVWAPUpper1Ticks : null;

            var vwapLower1Norm = (input.VWAP_LowerBand1.HasValue && input.VWAP_LowerBand1.Value > 0m) ? input.VWAP_LowerBand1 : null;
            int? distToVwapLower1TicksNorm = vwapLower1Norm.HasValue ? input.DistToVWAPLower1Ticks : null;

            var vwapUpper2Norm = (input.VWAP_UpperBand2.HasValue && input.VWAP_UpperBand2.Value > 0m) ? input.VWAP_UpperBand2 : null;
            int? distToVwapUpper2TicksNorm = vwapUpper2Norm.HasValue ? input.DistToVWAPUpper2Ticks : null;

            // KORRIGIERT: Falsche HasValue-Überprüfung für Lower2 (war Upper2)
            var vwapLower2Norm = (input.VWAP_LowerBand2.HasValue && input.VWAP_LowerBand2.Value > 0m) ? input.VWAP_LowerBand2 : null;
            // KORRIGIERT: Falsche Dist-Variable (war DistToVWAPUpper2Ticks)
            int? distToVwapLower2TicksNorm = vwapLower2Norm.HasValue ? input.DistToVWAPLower2Ticks : null;

            var vwapUpper3Norm = (input.VWAP_UpperBand3.HasValue && input.VWAP_UpperBand3.Value > 0m) ? input.VWAP_UpperBand3 : null;
            int? distToVwapUpper3TicksNorm = vwapUpper3Norm.HasValue ? input.DistToVWAPUpper3Ticks : null;

            // KORRIGIERT: Für Lower3 bereits ok, aber explizit bestätigt
            var vwapLower3Norm = (input.VWAP_LowerBand3.HasValue && input.VWAP_LowerBand3.Value > 0m) ? input.VWAP_LowerBand3 : null;
            int? distToVwapLower3TicksNorm = vwapLower3Norm.HasValue ? input.DistToVWAPLower3Ticks : null;
            // <<< KORRIGIERT

            var s = new ResearchSample
            {
                StartBarIndex = input.StartBarIndex,
                Time = input.Time,
                Symbol = input.Symbol ?? "",
                Timeframe = input.Timeframe ?? "",
                SetupId = input.SetupId ?? "",
                Direction = input.Direction,
                LevelKey = input.LevelKey ?? "",
                LevelPrice = input.LevelPrice,
                EntryPrice = input.EntryPrice,

                ATR = input.ATR,
                VWAP = vwapNorm,
                DistToVWAPTicks = distToVwapTicksNorm,
                VWAP_UpperBand1 = vwapUpper1Norm,
                DistToVWAPUpper1Ticks = distToVwapUpper1TicksNorm,
                VWAP_LowerBand1 = vwapLower1Norm,
                DistToVWAPLower1Ticks = distToVwapLower1TicksNorm,
                VWAP_UpperBand2 = vwapUpper2Norm,
                DistToVWAPUpper2Ticks = distToVwapUpper2TicksNorm,
                VWAP_LowerBand2 = vwapLower2Norm,
                DistToVWAPLower2Ticks = distToVwapLower2TicksNorm,
                VWAP_UpperBand3 = vwapUpper3Norm,
                DistToVWAPUpper3Ticks = distToVwapUpper3TicksNorm,
                VWAP_LowerBand3 = vwapLower3Norm,
                DistToVWAPLower3Ticks = distToVwapLower3TicksNorm,

                DistToLevelTicks = input.DistToLevelTicks,


                VolBurstZ = input.VolBurstZ,
                CvdImpulse = input.CvdImpulse,
                CvdCoherence = input.CvdCoherence,
                AggPressure = input.AggPressure,
                TradeRateZ = input.TradeRateZ,
                Efficiency = input.Efficiency,
                MaxCounterDeltaShare = input.MaxCounterDeltaShare,
                MaxCounterShareBull = input.MaxCounterShareBull,
                MaxCounterShareBear = input.MaxCounterShareBear,
                CounterDelta = input.CounterDelta,
                // >>> NEU: IttZ/Sweep übernehmen (mit leichter Normalisierung)
                IttZ_Raw = (input.IttZ_Raw.HasValue ? input.IttZ_Raw : null),
                IttZ_Bull = (input.IttZ_Bull.HasValue ? input.IttZ_Bull : null),
                IttZ_Bear = (input.IttZ_Bear.HasValue ? input.IttZ_Bear : null),

                // SweepDir auf -1/0/+1 begrenzen (falls extern abweicht)
                SweepDir = Math.Sign(input.SweepDir),
                SweepUp = input.SweepUp != 0 ? 1 : 0,
                SweepDn = input.SweepDn != 0 ? 1 : 0,
                // Streak nur sinnvoll, wenn Richtung != 0
                SweepStreak = (Math.Sign(input.SweepDir) == 0) ? 0 : input.SweepStreak,

                VP30_POC = (input.VP30_POC.HasValue && input.VP30_POC.Value > 0m) ? input.VP30_POC : null,
                VP30_VAH = (input.VP30_VAH.HasValue && input.VP30_VAH.Value > 0m) ? input.VP30_VAH : null,
                VP30_VAL = (input.VP30_VAL.HasValue && input.VP30_VAL.Value > 0m) ? input.VP30_VAL : null,
                VP30_DistToPOCTicks = input.VP30_DistToPOCTicks,
                VP30_DistToVAHTicks = input.VP30_DistToVAHTicks,
                VP30_DistToVALTicks = input.VP30_DistToVALTicks,
                VP30_IsWegFrei = input.VP30_IsWegFrei,
                VP30_FirstObstacleTicks = input.VP30_FirstObstacleTicks,
                VP30_HVN1Price = (input.VP30_HVN1Price.HasValue && input.VP30_HVN1Price.Value > 0m) ? input.VP30_HVN1Price : null,
                VP30_HVN1DistTicks = input.VP30_HVN1DistTicks,
                VP30_LVN1Price = (input.VP30_LVN1Price.HasValue && input.VP30_LVN1Price.Value > 0m) ? input.VP30_LVN1Price : null,
                VP30_LVN1DistTicks = input.VP30_LVN1DistTicks,

                // >>> NEU: VAH/VAL/POC-Preise übernehmen (mit Normalisierung zu null bei <=0m)
                CurrentPOC = (input.CurrentPOC.HasValue && input.CurrentPOC.Value > 0m) ? input.CurrentPOC : null,
                CurrentVAH = (input.CurrentVAH.HasValue && input.CurrentVAH.Value > 0m) ? input.CurrentVAH : null,
                CurrentVAL = (input.CurrentVAL.HasValue && input.CurrentVAL.Value > 0m) ? input.CurrentVAL : null,
                PreviousPOC = (input.PreviousPOC.HasValue && input.PreviousPOC.Value > 0m) ? input.PreviousPOC : null,
                PreviousVAH = (input.PreviousVAH.HasValue && input.PreviousVAH.Value > 0m) ? input.PreviousVAH : null,
                PreviousVAL = (input.PreviousVAL.HasValue && input.PreviousVAL.Value > 0m) ? input.PreviousVAL : null,
                // <<< NEU

                DistToPDHTicks = input.DistToPDHTicks,
                DistToPDLTicks = input.DistToPDLTicks,
                DistToPOCTicks = input.DistToPOCTicks,
                DistToVAHTicks = input.DistToVAHTicks,
                DistToVALTicks = input.DistToVALTicks,

                DistToPDPOCTicks = input.DistToPDPOCTicks,
                DistToRoundBelowTicks = input.DistToRoundBelowTicks,
                DistToRoundAboveTicks = input.DistToRoundAboveTicks,
                DistToBlockResTicks = input.DistToBlockResTicks,
                DistToBlockSupTicks = input.DistToBlockSupTicks,

                HorizonBars = input.HorizonBars,
                BarsLeft = input.HorizonBars,

                // Pfade
                HiTicks = new List<decimal>(Math.Max(8, input.HorizonBars)),
                LoTicks = new List<decimal>(Math.Max(8, input.HorizonBars)),
                CloseTicks = new List<decimal>(Math.Max(8, input.HorizonBars)),

                MFE = 0m,
                MAE = 0m,
                TimeToMFE = -1,
                TimeToMAE = -1,

                ExtraFeaturesJson = (input.ExtraFeatures != null && input.ExtraFeatures.Count > 0)
                    ? JsonSerializer.Serialize(input.ExtraFeatures)
                    : ""
            };
            _pending.Add(s);
        }

        public void UpdateBar(ResearchBarInput bar)
        {
            if (bar == null || _pending.Count == 0) return;

            // >>> ADDED: Off-Hours-Bars ignorieren (nur Session 09:00–21:00 Europe/Berlin)
            if (!IsInBerlinSession(bar.Time))
                return;
            // <<< ADDED

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var s = _pending[i];
                if (s.Direction == ResearchDirection.Neutral)
                {
                    // Entweder: skippen
                    // _pending.RemoveAt(i); // falls du sie gar nicht auswerten willst
                    // oder: weiterhin laufen lassen aber ohne Pfad/MFE/MAE-Update
                    continue;
                }
                // Richtungsnormalisierung: Long = +1, Short = -1
                var dir = (s.Direction == ResearchDirection.Long) ? 1m : -1m;

                // Pfad relativ zum Entry, in Ticks
                var highTicks = dir * ((bar.High - s.EntryPrice) / _tickSize);
                var lowTicks = dir * ((bar.Low - s.EntryPrice) / _tickSize);
                var clTicks = dir * ((bar.Close - s.EntryPrice) / _tickSize);

                var favTicks = Math.Max(highTicks, lowTicks);   // Richtung-normalisiert: größte günstige Bewegung
                var advTicks = Math.Min(highTicks, lowTicks);   // Richtung-normalisiert: größte ungünstige Bewegung

                var hiTicks = Math.Max(0m, favTicks);           // nur positiv
                var loTicks = Math.Min(0m, advTicks);           // nur negativ

                s.HiTicks.Add(hiTicks);
                s.LoTicks.Add(loTicks);
                s.CloseTicks.Add(clTicks);

                // Fortschritt (0-basiert)
                var barsElapsed = s.HorizonBars - s.BarsLeft;

                // MFE/MAE aus Pfad-Definition ableiten
                // MFE-Kandidat: größter günstiger Move dieses Bars oder günstiger Close
                var mfeCandidate = Math.Max(hiTicks, Math.Max(0m, clTicks));
                // MAE-Kandidat: größter ungünstiger Move dieses Bars oder ungünstiger Close (Betrag)
                var maeCandidate = Math.Max(Math.Abs(loTicks), Math.Abs(Math.Min(0m, clTicks)));

                if (mfeCandidate > s.MFE) { s.MFE = mfeCandidate; s.TimeToMFE = barsElapsed; }
                if (maeCandidate > s.MAE) { s.MAE = maeCandidate; s.TimeToMAE = barsElapsed; }

                s.BarsLeft--;

                if (s.BarsLeft <= 0)
                {
                    // CloseRetTicks ist das richtungsnormalisierte clTicks
                    var closeRetTicks = clTicks;

                    AppendRow(s, closeRetTicks);
                    _pending.RemoveAt(i);
                }
            }

            MaybeAutoFlush();
        }

        private void MaybeAutoFlush()
        {
            foreach (var kv in _buffers.ToList())
            {
                if (kv.Value.Count > AutoFlushThreshold)
                {
                    Flush();
                    break;
                }
            }
        }

        private static string JoinTicks(IEnumerable<decimal> xs)
            => xs == null ? "" : string.Join(";", xs.Select(v => v.ToString("0.0000", CultureInfo.InvariantCulture)));

        private string WithRunId(string json)
        {
            var dict = ParseJsonToDict(json);
            dict["RunId"] = _runId;
            return JsonSerializer.Serialize(dict);
        }

        private Dictionary<string, object> ParseJsonToDict(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object>>(json)
                       ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                // Falls das JSON mal korrupt ist, sichere es trotzdem
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["_ExtraFeaturesRaw"] = json
                };
            }
        }

        private string MergeIntoJson(string existingJson, Dictionary<string, object> additions)
        {
            var dict = ParseJsonToDict(existingJson);
            foreach (var kv in additions)
                dict[kv.Key] = kv.Value ?? JsonValue.Create((string)null);
            return JsonSerializer.Serialize(dict);
        }

        private void AppendRow(ResearchSample s, decimal closeRetTicks)
        {
            // Konsistenzprüfung: Pfad vs. MFE/MAE (gleiche Tick-Skalierung erwartet)
            decimal pathMfe = s.HiTicks.Count > 0 ? s.HiTicks.Max() : 0m;
            decimal pathMae = s.LoTicks.Count > 0 ? Math.Abs(s.LoTicks.Min()) : 0m;

            const decimal tol = 0.0001m;
            bool mfeMismatch = Math.Abs(s.MFE - pathMfe) > tol;
            bool maeMismatch = Math.Abs(s.MAE - pathMae) > tol;

            if (mfeMismatch || maeMismatch)
            {
                var diag = new Dictionary<string, object>
                {
                    ["TickSize"] = _tickSize,
                    ["Entry"] = s.EntryPrice,
                    ["LastBarClose"] = s.CloseTicks.Count > 0 ? (object)s.CloseTicks.Last() : null,
                    ["PathMFE"] = pathMfe,
                    ["PathMAE"] = pathMae,
                    ["MFE"] = s.MFE,
                    ["MAE"] = s.MAE,
                    ["Time"] = s.Time.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                    ["Symbol"] = s.Symbol,
                    ["Setup"] = s.SetupId
                };

                s.ExtraFeaturesJson = MergeIntoJson(s.ExtraFeaturesJson, diag);
            }

            // Outcomes
            bool tpHit = s.MFE >= _outcomeTicks;
            bool slHit = s.MAE >= _stopTicks;
            bool tpBeforeSl = (s.TimeToMFE >= 0 && (!slHit || s.TimeToMFE <= s.TimeToMAE)) && tpHit;

            var filePath = GetFilePath(s.Time, s.SetupId);
            EnsureHeader(filePath);
            var buf = GetBuffer(filePath);

            buf.Add(string.Join(",",
                // Identifikation
                T(s.Time),
                s.Symbol,
                s.Timeframe,
                s.SetupId,
                s.Direction.ToString(),

                // Entry & Level
                s.LevelKey,
                F4(s.LevelPrice),
                F4(s.EntryPrice),
                (s.DistToLevelTicks < 0 ? "" : I(s.DistToLevelTicks)),

                // Price/Context
                F4(s.ATR),
                F4(s.VWAP),
                I(s.DistToVWAPTicks),
                // Neue VWAP-Bänder
                F4(s.VWAP_UpperBand1),
                I(s.DistToVWAPUpper1Ticks),
                F4(s.VWAP_LowerBand1),
                I(s.DistToVWAPLower1Ticks),
                
                F4(s.VWAP_UpperBand2),
                I(s.DistToVWAPUpper2Ticks),
                F4(s.VWAP_LowerBand2),
                I(s.DistToVWAPLower2Ticks),
              
                F4(s.VWAP_UpperBand3),
                I(s.DistToVWAPUpper3Ticks),
                F4(s.VWAP_LowerBand3),
                I(s.DistToVWAPLower3Ticks),

                // OV
                F4(s.VolBurstZ),
                F4(s.CvdImpulse),
                F4(s.CvdCoherence),
                F4(s.AggPressure),
                F4(s.TradeRateZ),
                F4(s.Efficiency),
                F4(s.MaxCounterDeltaShare),
                F4(s.MaxCounterShareBull),
                F4(s.MaxCounterShareBear),
                // >>> NEU: IttZ & Sweep
                F4(s.IttZ_Raw),
                F4(s.IttZ_Bull),
                F4(s.IttZ_Bear),
                I(s.SweepDir),
                I(s.SweepUp),
                I(s.SweepDn),
                I(s.SweepStreak),

                // VP30
                F4(s.VP30_POC),
                F4(s.VP30_VAH),
                F4(s.VP30_VAL),
                I(s.VP30_DistToPOCTicks),
                I(s.VP30_DistToVAHTicks),
                I(s.VP30_DistToVALTicks),
                I(s.VP30_IsWegFrei),
                I(s.VP30_FirstObstacleTicks),
                F4(s.VP30_HVN1Price),
                I(s.VP30_HVN1DistTicks),
                F4(s.VP30_LVN1Price),
                I(s.VP30_LVN1DistTicks),

                // >>> NEU: VAH/VAL/POC-Preise (F4 für decimal?, leer bei null)
                F4(s.CurrentPOC), F4(s.CurrentVAH), F4(s.CurrentVAL),
                F4(s.PreviousPOC), F4(s.PreviousVAH), F4(s.PreviousVAL),
                // <<< NEU

                // Globale Distanzen
                I(s.DistToPDHTicks),
                I(s.DistToPDLTicks),
                I(s.DistToPOCTicks),
                I(s.DistToVAHTicks),
                I(s.DistToVALTicks),

                I(s.DistToPDPOCTicks),
                I(s.DistToRoundBelowTicks),
                I(s.DistToRoundAboveTicks),
                I(s.DistToBlockResTicks),
                I(s.DistToBlockSupTicks),

                // Fenster/Outcomes
                I(s.HorizonBars),
                F4(s.MFE),
                F4(s.MAE),
                I(s.TimeToMFE),
                I(s.TimeToMAE),
                B(tpHit),
                B(closeRetTicks >= _outcomeTicks),
                F4(closeRetTicks),
                B(tpBeforeSl),

                // Pfade (jeweils als Semikolon-String, sauber gequotet)
                EscapeCsv(JoinTicks(s.HiTicks)),
                EscapeCsv(JoinTicks(s.LoTicks)),
                EscapeCsv(JoinTicks(s.CloseTicks)),

                // Extra (nur EIN JSON-Feld, mit RunId injiziert)
                EscapeCsv(WithRunId(s.ExtraFeaturesJson))
            ));

            MaybeAutoFlush();
        }

        private static string EscapeCsv(string s)
        {
            if (s == null) return "";
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\t') || s.Contains(';'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static readonly CultureInfo CsvCulture = CultureInfo.InvariantCulture;

        // Feste 4 Nachkommastellen
        private static string F4(decimal v) => v.ToString("F4", CsvCulture);
        private static string F4(decimal? v) => v.HasValue ? v.Value.ToString("F4", CsvCulture) : "";

        private static string I(int v) => v.ToString(CsvCulture);
        private static string I(int? v) => v.HasValue ? v.Value.ToString(CsvCulture) : "";
        private static string L(long v) => v.ToString(CsvCulture);
        private static string B(bool v) => v ? "1" : "0";
        private static string T(DateTime t) => t.ToUniversalTime().ToString("o", CsvCulture); // ISO-8601

        public void Flush()
        {
            if (_buffers.Count == 0) return;
            try
            {
                if (!Directory.Exists(_baseDir))
                    Directory.CreateDirectory(_baseDir);

                foreach (var kv in _buffers.ToList())
                {
                    var path = kv.Key;
                    var lines = kv.Value;
                    if (lines.Count == 0) continue;

                    // ... (CreateDir etc.)

                    try
                    {
                        File.AppendAllLines(path, lines);
                        lines.Clear();
                        _buffers.Remove(path);  // Entferne leere
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Flush partial ERROR for {path}: {ex.Message}");
                    }
                }
            }
            catch
            {
                // Optional: Fehler-Logging
            }
        }
        public void FinalizePending(ResearchBarInput finalBar = null)
        {
            if (_pending.Count == 0) return;

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var s = _pending[i];
                s.BarsLeft = 0;

                decimal closeRetTicks = 0m;

                if (finalBar != null)
                {
                    // Optional: konsistenter Session-Check
                    // if (!IsInBerlinSession(finalBar.Time)) { /* entweder ignorieren oder trotzdem verarbeiten */ }

                    if (s.Direction != ResearchDirection.Neutral)
                    {
                        var dir = (s.Direction == ResearchDirection.Long) ? 1m : -1m;
                        var highTicks = dir * ((finalBar.High - s.EntryPrice) / _tickSize);
                        var lowTicks = dir * ((finalBar.Low - s.EntryPrice) / _tickSize);
                        var clTicks = dir * ((finalBar.Close - s.EntryPrice) / _tickSize);

                        var favTicks = Math.Max(highTicks, lowTicks);
                        var advTicks = Math.Min(highTicks, lowTicks);

                        var hiTicks = Math.Max(0m, favTicks);
                        var loTicks = Math.Min(0m, advTicks);

                        s.HiTicks.Add(hiTicks);
                        s.LoTicks.Add(loTicks);
                        s.CloseTicks.Add(clTicks);

                        // gleiche MFE/MAE-Logik wie in UpdateBar
                        var barsElapsed = s.HorizonBars; // letzter Bar-Index

                        var mfeCandidate = Math.Max(hiTicks, Math.Max(0m, clTicks));
                        var maeCandidate = Math.Max(Math.Abs(loTicks), Math.Abs(Math.Min(0m, clTicks)));

                        if (mfeCandidate > s.MFE) { s.MFE = mfeCandidate; s.TimeToMFE = barsElapsed; }
                        if (maeCandidate > s.MAE) { s.MAE = maeCandidate; s.TimeToMAE = barsElapsed; }

                        closeRetTicks = clTicks;
                    }
                    // else: Neutral bleibt bei closeRetTicks = 0m und ohne Pfad-/MFE-Update
                }

                AppendRow(s, closeRetTicks);
                _pending.RemoveAt(i);
            }
        }

    }
}


