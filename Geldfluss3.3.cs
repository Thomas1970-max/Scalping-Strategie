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
using static MyNamespace.Strategies.Geldfluss3_3;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.ConstrainedExecution;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Xml.Linq;
using Utils.Common;
using DevExpress.Xpf.Core.Native;
using MyNamespace.Strategies.MarketAnalysis;
using MyNamespace.Strategies;
using System.Collections.Generic;
using System.Collections;
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



    [DisplayName("Geldfluss 3.3")] // Angepasster Name f?r ATAS
    [Category("Meine Strategien")] // Kategorie in ATAS
    public class Geldfluss3_3 : ChartStrategy, ICustomTypeDescriptor
    {
        public sealed class MicroCompositeSettingsWrapper
        {
            private readonly Geldfluss3_3 _s;

            public MicroCompositeSettingsWrapper(Geldfluss3_3 strategy)
            {
                _s = strategy;
            }

            [Display(Name = "MicroComposite ?ber N Bars",
                     Description = "Definiert die Gr??e des rollierenden Volumenprofils in Kerzen",
                     Order = 7)]
            public int M
            {
                get => _s.M;
                set => _s.M = value;
            }

            [Display(Name = "Value Area %", Order = 8)]
            public decimal ValueAreaPct
            {
                get => _s.ValueAreaPct;
                set => _s.ValueAreaPct = value;
            }

            [Display(Name = "Top HVN Zonen",
                     Description = "Maximale Anzahl an HVN-Zonen, die nach Scoring behalten werden. Weniger HVNs ausw?hlen; verringert ?berlagerungen und h?lt die wichtigsten, kompakten Zonen im Fokus.",
                     Order = 10)]
            public int TopNHVNs
            {
                get => _s.TopNHVNs;
                set => _s.TopNHVNs = value;
            }

            [Display(Name = "Top LVN Zonen",
                     Description = "Maximale Anzahl an LVN-Zonen, die nach Scoring behalten werden",
                     Order = 11)]
            public int TopNLVNs
            {
                get => _s.TopNLVNs;
                set => _s.TopNLVNs = value;
            }

            [Display(Name = "Mindestbreite Zone (Ticks)",
                     Description = "Lässt schmale, klare HVNs zu (nicht zu niedrig setzen, sonst Rauschen).",
                     Order = 12)]
            public int MinZoneTicks
            {
                get => _s.MinZoneTicks;
                set => _s.MinZoneTicks = value;
            }

            [Display(Name = "Minimale Prominenz",
                     Description = "Hebt die Qualität; indirekt oft schmalere Zonen, weil flache, breitgezogene 'Hügel' rausfallen. (0..1)",
                     Order = 13)]
            public decimal MinProminence
            {
                get => _s.MinProminence;
                set => _s.MinProminence = value;
            }

            [Display(Name = "Min. Volumenanteil",
                     Description = "Filtert Zonen mit sehr wenig Volumenanteil. (0..1)",
                     Order = 14)]
            public decimal MinVolShare
            {
                get => _s.MinVolShare;
                set => _s.MinVolShare = value;
            }

            [Display(Name = "Min. Breite relativ VA",
                     Description = "Mindestbreite einer Zone relativ zur Value-Area-Breite (0..1)",
                     Order = 15)]
            public decimal MinWidthPctVA
            {
                get => _s.MinWidthPctVA;
                set => _s.MinWidthPctVA = value;
            }

            [Display(Name = "Merge-Gap (Ticks)",
                     Description = "Zonen in diesem Tick-Abstand werden zusammengef?hrt. Klein halten, damit benachbarte Kandidaten/Zonen nicht zu einer sehr breiten Zone zusammengef?hrt werden.",
                     Order = 16)]
            public int GapTicks
            {
                get => _s.GapTicks;
                set => _s.GapTicks = value;
            }

            [Display(Name = "Max. Distanzgewicht (Ticks)",
                     Description = "Skalierung der Entfernung zum aktuellen Preis im Score",
                     Order = 20)]
            public int MaxDistTicks
            {
                get => _s.MaxDistTicks;
                set => _s.MaxDistTicks = value;
            }

            [Display(Name = "Smoothing (Ticks)",
                     Description = "Triangular Smoothing-Spanne f?r die Volumenreihe. Weniger Gl?ttung macht Peaks schmaler und Zonen k?rzer.",
                     Order = 21)]
            public int SmoothTicks
            {
                get => _s.SmoothTicks;
                set => _s.SmoothTicks = value;
            }

            [Display(Name = "Zonen an Value Area klemmen",
                     Description = "Schneidet alle HVN/LVN-Zonen an den Value-Area-Grenzen (VAL/VAH) zu. Dies verhindert, dass Zonen ?ber die wichtigsten Handelsbereiche hinausragen und sorgt f?r saubere, definierte Zonengrenzen.",
                     Order = 22)]
            public bool ClampZonesToVA
            {
                get => _s.ClampZonesToVA;
                set => _s.ClampZonesToVA = value;
            }

            [Display(Name = "Zonenbreite begrenzen aktiv",
                     Description = "Aktiviert eine harte Obergrenze f?r die maximale Breite von HVN/LVN-Zonen. N?tzlich um ?berbreite Zonen zu vermeiden, die durch Volumen-Schwankungen entstehen k?nnen.",
                     Order = 23)]
            public bool EnableCapZoneWidth
            {
                get => _s.EnableCapZoneWidth;
                set => _s.EnableCapZoneWidth = value;
            }

            [Display(Name = "Maximale Zonenbreite (Ticks)",
                     Description = "Die maximale Breite einer HVN/LVN-Zone in Ticks, symmetrisch um die Zonenmitte. Kleinere Werte erzeugen engere, pr?zisere Zonen; gr??ere Werte erlauben breitere Handelsbereiche.",
                     Order = 24)]
            public int CapZoneWidthTicks
            {
                get => _s.CapZoneWidthTicks;
                set => _s.CapZoneWidthTicks = value;
            }
        }

        public sealed class DailyProfileSettingsWrapper
        {
            private readonly Geldfluss3_3 _s;

            public DailyProfileSettingsWrapper(Geldfluss3_3 strategy)
            {
                _s = strategy;
            }

            [Display(Name = "Daily Histogramm Breite (px)",
                     Description = "Breite der Histogramm-Balken (maximale Ausdehnung) in Pixel.",
                     Order = 3)]
            [Range(40, 1000)]
            public int DailyHistogramWidthPx
            {
                get => _s.DailyHistogramWidthPx;
                set => _s.DailyHistogramWidthPx = value;
            }

            [Display(Name = "Daily Histogramm Opacity (0..255)",
                     Description = "Transparenz f?r die Histogramm-F?llung.",
                     Order = 4)]
            [Range(5, 255)]
            public int DailyHistogramOpacity
            {
                get => _s.DailyHistogramOpacity;
                set => _s.DailyHistogramOpacity = value;
            }

            [Display(Name = "Daily Profile: Volumenquelle",
                     Description = "Wenn aktiv, nutzt das Daily-Profil pvi.Volume (wie ATAS Market Profile bei Einstellung 'Volumen'). Wenn aus, nutzt Ask+Bid (Lots).",
                     Order = 10)]
            public bool DailyProfileUseAtasVolume
            {
                get => _s.DailyProfileUseAtasVolume;
                set => _s.DailyProfileUseAtasVolume = value;
            }
        }

        public sealed class DailyProfileWegFreiSettingsWrapper
        {
            private readonly Geldfluss3_3 _s;

            public DailyProfileWegFreiSettingsWrapper(Geldfluss3_3 strategy)
            {
                _s = strategy;
            }

            [Display(Name = "Daily WegFrei aktiv",
                     Description = "Aktiviert ein separates Daily-Volumenprofil (nur für WegFrei) und berücksichtigt Daily HVN/LVN zusätzlich zum MicroComposite (UND-Logik, sofern beide aktiv sind).",
                     Order = 1)]
            public bool EnableDailyProfilePathSystem
            {
                get => _s.EnableDailyProfilePathSystem;
                set => _s.EnableDailyProfilePathSystem = value;
            }

            [Display(Name = "Daily DMinTicks (Mindestabstand Blocker)",
                     Description = "Mindestabstand in Ticks: Blockt Entry, wenn ein Daily Blocker (POC/VA-Kante/HVN-Zonen-Kante) innerhalb dieser Distanz im Pfad liegt. Analog zu DMinTicks im MicroComposite.",
                     Order = 1)]
            [Range(1, 200)]
            public int DailyDMinTicks
            {
                get => _s.DailyDMinTicks;
                set => _s.DailyDMinTicks = value;
            }

            [Display(Name = "Daily Recalc alle N Bars",
                     Description = "Rechenintervall in Bars (Range-Bar kompatibel). 1 = jedes Bar neu berechnen.",
                     Order = 2)]
            [Range(1, 500)]
            public int DailyProfileRecalcEveryNBars
            {
                get => _s.DailyProfileRecalcEveryNBars;
                set => _s.DailyProfileRecalcEveryNBars = value;
            }

            [Display(Name = "Daily Top HVN Zonen",
                     Description = "Maximale Anzahl an HVN-Zonen (Daily Profil).",
                     Order = 2)]
            [Range(1, 50)]
            public int DailyTopNHVNs
            {
                get => _s.DailyTopNHVNs;
                set => _s.DailyTopNHVNs = value;
            }

            [Display(Name = "Daily Top LVN Zonen",
                     Description = "Maximale Anzahl an LVN-Zonen (Daily Profil).",
                     Order = 2)]
            [Range(1, 50)]
            public int DailyTopNLVNs
            {
                get => _s.DailyTopNLVNs;
                set => _s.DailyTopNLVNs = value;
            }

            [Display(Name = "Daily Mindestbreite Zone (Ticks)",
                     Description = "Mindestbreite einer Daily HVN/LVN-Zone in Ticks.",
                     Order = 2)]
            [Range(1, 100)]
            public int DailyMinZoneTicks
            {
                get => _s.DailyMinZoneTicks;
                set => _s.DailyMinZoneTicks = value;
            }

            [Display(Name = "Daily Min Prominenz",
                     Description = "Filtert flache Peaks (Daily Profil).",
                     Order = 2)]
            [Range(0.0, 1.0)]
            public decimal DailyMinProminence
            {
                get => _s.DailyMinProminence;
                set => _s.DailyMinProminence = value;
            }

            [Display(Name = "Daily Min Volumenanteil",
                     Description = "Filtert Zonen mit sehr kleinem Volumenanteil (Daily Profil).",
                     Order = 2)]
            [Range(0.0, 1.0)]
            public decimal DailyMinVolShare
            {
                get => _s.DailyMinVolShare;
                set => _s.DailyMinVolShare = value;
            }

            [Display(Name = "Daily Min Breite relativ VA",
                     Description = "Mindestbreite einer Zone relativ zur Value-Area-Breite (Daily Profil).",
                     Order = 2)]
            [Range(0.0, 1.0)]
            public decimal DailyMinWidthPctVA
            {
                get => _s.DailyMinWidthPctVA;
                set => _s.DailyMinWidthPctVA = value;
            }

            [Display(Name = "Daily Merge-Gap (Ticks)",
                     Description = "Zonen in diesem Tick-Abstand werden zusammengef?hrt (Daily Profil).",
                     Order = 2)]
            [Range(0, 20)]
            public int DailyGapTicks
            {
                get => _s.DailyGapTicks;
                set => _s.DailyGapTicks = value;
            }

            [Display(Name = "Daily Max. Distanzgewicht (Ticks)",
                     Description = "Skalierung der Entfernung zum aktuellen Preis im Score (Daily Profil).",
                     Order = 2)]
            [Range(1, 200)]
            public int DailyMaxDistTicks
            {
                get => _s.DailyMaxDistTicks;
                set => _s.DailyMaxDistTicks = value;
            }

            [Display(Name = "Daily Zonen an Value Area klemmen",
                     Description = "Schneidet alle Daily-HVN/LVN-Zonen an den Value-Area-Grenzen (VAL/VAH) zu.",
                     Order = 2)]
            public bool DailyClampZonesToVA
            {
                get => _s.DailyClampZonesToVA;
                set => _s.DailyClampZonesToVA = value;
            }

            [Display(Name = "Daily Zonen au?erhalb VA zulassen",
                     Description = "Wenn aktiv, d?rfen Daily HVN/LVN-Zonen auch au?erhalb der Value Area liegen (keine VA-Klemmung; Plateau-Scan ?ber komplette Profil-Achse).",
                     Order = 2)]
            public bool DailyAllowZonesOutsideVA
            {
                get => _s.DailyAllowZonesOutsideVA;
                set => _s.DailyAllowZonesOutsideVA = value;
            }

            [Display(Name = "Daily Zonenbreite begrenzen aktiv",
                     Description = "Aktiviert eine harte Obergrenze f?r die maximale Breite von Daily-HVN/LVN-Zonen.",
                     Order = 2)]
            public bool DailyEnableCapZoneWidth
            {
                get => _s.DailyEnableCapZoneWidth;
                set => _s.DailyEnableCapZoneWidth = value;
            }

            [Display(Name = "Daily Maximale Zonenbreite (Ticks)",
                     Description = "Maximale Breite einer Daily-HVN/LVN-Zone in Ticks.",
                     Order = 2)]
            [Range(1, 100)]
            public int DailyCapZoneWidthTicks
            {
                get => _s.DailyCapZoneWidthTicks;
                set => _s.DailyCapZoneWidthTicks = value;
            }

            [Display(Name = "Daily Plateau-Detektor (HVN/LVN)",
                     Description = "Erkennt HVN/LVN als zusammenh?ngende High/Low-Volume-Areas per Schwellwert (n?her an ATAS bei breiten Zonen).",
                     Order = 2)]
            public bool DailyUsePlateauDetector
            {
                get => _s.DailyUsePlateauDetector;
                set => _s.DailyUsePlateauDetector = value;
            }

            [Display(Name = "Daily HVN Plateau Schwelle (Anteil vom Max)",
                     Description = "Schwellwert f?r HVN-Areas: smoothVol >= Anteil * maxSmoothVol.",
                     Order = 2)]
            [Range(0.1, 0.95)]
            public decimal DailyHVNPlateauFrac
            {
                get => _s.DailyHVNPlateauFrac;
                set => _s.DailyHVNPlateauFrac = value;
            }

            [Display(Name = "Daily LVN Plateau Schwelle (Anteil vom Max)",
                     Description = "Schwellwert f?r LVN-Areas: smoothVol <= Anteil * maxSmoothVol.",
                     Order = 2)]
            [Range(0.01, 0.8)]
            public decimal DailyLVNPlateauFrac
            {
                get => _s.DailyLVNPlateauFrac;
                set => _s.DailyLVNPlateauFrac = value;
            }

            [Display(Name = "Daily Smoothing (Ticks)",
                     Description = "Triangular Smoothing-Spanne für Daily HVN/LVN.",
                     Order = 3)]
            [Range(1, 50)]
            public int DailySmoothTicks
            {
                get => _s.DailySmoothTicks;
                set => _s.DailySmoothTicks = value;
            }

            [Display(Name = "Daily Top Peaks",
                     Description = "Maximale Anzahl HVN-/LVN-Zentren für Daily Profil.",
                     Order = 4)]
            [Range(1, 50)]
            public int DailyTopNPeaks
            {
                get => _s.DailyTopNPeaks;
                set => _s.DailyTopNPeaks = value;
            }

            [Display(Name = "Daily Mindest-LVNs im Pfad",
                     Description = "Wie viele LVN-Korridore m?ssen im Daily-Profil im Pfad liegen.",
                     Order = 5)]
            [Range(0, 10)]
            public int DailyRequiredLVNsInPath
            {
                get => _s.DailyRequiredLVNsInPath;
                set => _s.DailyRequiredLVNsInPath = value;
            }

            [Display(Name = "Daily VA-Kanten au?erhalb Value lockern",
                     Description = "Wie MicroComposite: wenn Preis au?erhalb VA, VA-Kanten weniger streng behandeln.",
                     Order = 6)]
            public bool DailyRelaxVAEdgesWhenOutsideValue
            {
                get => _s.DailyRelaxVAEdgesWhenOutsideValue;
                set => _s.DailyRelaxVAEdgesWhenOutsideValue = value;
            }

            [Display(Name = "Daily HVN Strength (0..100)",
                     Description = "Ein einziger Stärkeregler für Daily-HVN-Blocking: 50 = neutral, höher = strenger (weniger blockt), niedriger = liberaler (mehr blockt). Intern werden Inside/Outside-VA Schwellen (POC/Median/Prominenz) angepasst.",
                     Order = 7)]
            [Range(0, 100)]
            public int DailyHVNStrength
            {
                get => _s.DailyHVNStrength;
                set => _s.DailyHVNStrength = value;
            }

            [Display(Name = "Daily HVN-St?rke vs. POC (%)",
                     Description = "HVN gilt als stark, wenn Volumen >= Anteil des POC-Volumens (Daily Profil).",
                     Order = 8)]
            [Range(0.1, 1.0)]
            public decimal DailyHVNStrengthVsPOC
            {
                get => _s.DailyHVNStrengthVsPOC;
                set => _s.DailyHVNStrengthVsPOC = value;
            }

            [Display(Name = "Daily HVN-St?rke vs. Median (Faktor)",
                     Description = "HVN gilt als stark, wenn Volumen >= Faktor * MedianVol (Daily Profil).",
                     Order = 9)]
            [Range(0.5, 3.0)]
            public decimal DailyHVNStrengthVsMedian
            {
                get => _s.DailyHVNStrengthVsMedian;
                set => _s.DailyHVNStrengthVsMedian = value;
            }

            [Display(Name = "Daily Mindest-Prominenz vs. Median (%)",
                     Description = "Mindest-Prominenz für Peaks im Daily Profil.",
                     Order = 10)]
            [Range(0.05, 0.5)]
            public decimal DailyMinProminenceVsMedian
            {
                get => _s.DailyMinProminenceVsMedian;
                set => _s.DailyMinProminenceVsMedian = value;
            }

            [Display(Name = "Daily Mindest-Abstand Blocker vs. Risk (Faktor)",
                     Description = "Blocker muss mind. RiskTicks * Faktor entfernt sein (Daily Profil).",
                     Order = 11)]
            [Range(0.5, 5.0)]
            public int DailyMinBlockerDistanceTicksVsRisk
            {
                get => _s.DailyMinBlockerDistanceTicksVsRisk;
                set => _s.DailyMinBlockerDistanceTicksVsRisk = value;
            }
        }

        public sealed class MicroCompositeWegFreiSettingsWrapper
        {
            private readonly Geldfluss3_3 _s;

            public MicroCompositeWegFreiSettingsWrapper(Geldfluss3_3 strategy)
            {
                _s = strategy;
            }

            [Display(Name = "Mindest-LVNs im Pfad",
                     Description = "Wie viele LVN-Korridore (Low Volume Nodes) m?ssen im Preispfad vorhanden sein, damit ein Handel als g?ltig gilt. LVNs sind 'd?nne' Stellen im Volumenprofil, die der Preis leicht durchqueren kann. H?here Werte machen die Strategie selektiver.",
                     Order = 1)]
            public int RequiredLVNsInPath
            {
                get => _s.RequiredLVNsInPath;
                set => _s.RequiredLVNsInPath = value;
            }

            [Display(Name = "MC HVN Zonen f?r WegFrei/TP nutzen",
                     Description = "Wenn deaktiviert, werden MicroComposite HVN-Zonen/Punkte NICHT für WegFrei-Blocking und Dynamic TP verwendet. MC POC/VAH/VAL bleiben weiterhin aktiv.",
                     Order = 2)]
            public bool UseMicroCompositeHVNsForWegFreiAndDynamicTP
            {
                get => _s.UseMicroCompositeHVNsForWegFreiAndDynamicTP;
                set => _s.UseMicroCompositeHVNsForWegFreiAndDynamicTP = value;
            }

            [Display(Name = "MC LVN Zonen f?r WegFrei/TP nutzen",
                     Description = "Wenn deaktiviert, wird die LVN-Pfad-Anforderung aus dem MicroComposite für WegFrei-Blocking nicht verwendet. MC POC/VAH/VAL bleiben weiterhin aktiv.",
                     Order = 3)]
            public bool UseMicroCompositeLVNsForWegFreiAndDynamicTP
            {
                get => _s.UseMicroCompositeLVNsForWegFreiAndDynamicTP;
                set => _s.UseMicroCompositeLVNsForWegFreiAndDynamicTP = value;
            }

            [Display(Name = "MicroComposite HVN Strength (0..100)",
                     Description = "Ein einziger Stärkeregler für MicroComposite-HVN-Blocking: 50 = neutral, höher = strenger (weniger blockt), niedriger = liberaler (mehr blockt). Intern werden Inside/Outside-VA Schwellen (POC/Median/Prominenz) angepasst.",
                     Order = 4)]
            public int MicroCompositeHVNStrength
            {
                get => _s.MicroCompositeHVNStrength;
                set => _s.MicroCompositeHVNStrength = value;
            }

            [Display(Name = "VA-Kanten au?erhalb Value lockern",
                     Description = "Wenn der aktuelle Preis au?erhalb der Value Area (VA) liegt, werden die VA-Kanten (VAL/VAH) als weniger strenge Blocker behandelt. Dies erm?glicht Trades, auch wenn der Preis kurz au?erhalb der wichtigsten Handelszone ist.",
                     Order = 5)]
            public bool RelaxVAEdgesWhenOutsideValue
            {
                get => _s.RelaxVAEdgesWhenOutsideValue;
                set => _s.RelaxVAEdgesWhenOutsideValue = value;
            }

            [Display(Name = "HVN-St?rke vs. POC (%)",
                     Description = "Ein HVN (High Volume Node) gilt als 'starker Blocker', wenn sein Volumen mindestens dieser Prozentsatz des POC-Volumens betr?gt. Der POC (Point of Control) ist das Preislevel mit dem h?chsten Volumen. Höhere Werte machen die Blocker-Bewertung strenger.",
                     Order = 6)]
            public decimal HVNStrengthVsPOC
            {
                get => _s.HVNStrengthVsPOC;
                set => _s.HVNStrengthVsPOC = value;
            }

            [Display(Name = "HVN-St?rke vs. Median (Faktor)",
                     Description = "Ein HVN gilt als 'stark', wenn sein Volumen mindestens dieser Faktor mal dem Durchschnittsvolumen aller Preislevel entspricht. Beispiel: 1.2 bedeutet, das HVN muss 20% mehr Volumen als der Durchschnitt haben. Dies hilft, wirklich signifikante Volumenpunkte zu identifizieren.",
                     Order = 7)]
            public decimal HVNStrengthVsMedian
            {
                get => _s.HVNStrengthVsMedian;
                set => _s.HVNStrengthVsMedian = value;
            }

            [Display(Name = "Mindest-Prominenz vs. Median (%)",
                     Description = "Die 'Prominenz' misst, wie deutlich sich ein Volumenpeak von seiner Umgebung abhebt. Dieser Wert bestimmt die minimale Prominenz im Verhältnis zum Medianvolumen. Höhere Werte filtern nur die deutlichsten Peaks heraus und ignorieren kleine Volumenvariationen.",
                     Order = 8)]
            public decimal MinProminenceVsMedian
            {
                get => _s.MinProminenceVsMedian;
                set => _s.MinProminenceVsMedian = value;
            }

            [Display(Name = "Mindest-Abstand Blocker vs. Risk (Faktor)",
                     Description = "Ein Blocker (HVN, POC, VA-Kante) muss mindestens diesen Faktor mal dem Risk-Ticks Abstand vom aktuellen Preis entfernt sein. Beispiel: 1 bedeutet der Blocker muss weiter entfernt sein als die Risk-Distanz. Höhere Werte erlauben Trades näher an Blockern.",
                     Order = 9)]
            public int MinBlockerDistanceTicksVsRisk
            {
                get => _s.MinBlockerDistanceTicksVsRisk;
                set => _s.MinBlockerDistanceTicksVsRisk = value;
            }

            [Display(Name = "D_min (Ticks bis HVN/POC)",
                     Description = "Die fundamentale Risikodistanz in Ticks. Dies ist die Basis f?r alle Weg-Frei-Berechnungen und definiert den Mindestabstand zu wichtigen Volumenleveln. H?here Werte machen die Strategie konservativer und verhindern Trades in volatilen Bereichen.",
                     Order = 10)]
            public int DMinTicks
            {
                get => _s.DMinTicks;
                set => _s.DMinTicks = value;
            }
        }

        private readonly SMA _sma = new SMA();
        private readonly VWAP _vwap = new VWAP();

        private readonly ValueDataSeries _vwapSeries = new ValueDataSeries("VWAP")
        {
            Color = System.Drawing.Color.Purple.Convert(), // Choose a distinct color
            VisualType = VisualMode.Line,
            Width = 2
        };

        private readonly MicroCompositeSettingsWrapper _microCompositeSettings;
        private readonly MicroCompositeWegFreiSettingsWrapper _microCompositeWegFreiSettings;
        private readonly DailyProfileSettingsWrapper _dailyProfileSettings;
        private readonly DailyProfileWegFreiSettingsWrapper _dailyProfileWegFreiSettings;

        AttributeCollection ICustomTypeDescriptor.GetAttributes()
            => TypeDescriptor.GetProvider(this).GetTypeDescriptor(this).GetAttributes();

        string? ICustomTypeDescriptor.GetClassName()
            => TypeDescriptor.GetProvider(this).GetTypeDescriptor(this).GetClassName();

        string? ICustomTypeDescriptor.GetComponentName()
            => TypeDescriptor.GetProvider(this).GetTypeDescriptor(this).GetComponentName();

        TypeConverter ICustomTypeDescriptor.GetConverter()
            => TypeDescriptor.GetProvider(this).GetTypeDescriptor(this).GetConverter();

        EventDescriptor? ICustomTypeDescriptor.GetDefaultEvent()
            => TypeDescriptor.GetProvider(this).GetTypeDescriptor(this).GetDefaultEvent();

        PropertyDescriptor? ICustomTypeDescriptor.GetDefaultProperty()
            => TypeDescriptor.GetProvider(this).GetTypeDescriptor(this).GetDefaultProperty();

        object? ICustomTypeDescriptor.GetEditor(Type editorBaseType)
            => TypeDescriptor.GetProvider(this).GetTypeDescriptor(this).GetEditor(editorBaseType);

        EventDescriptorCollection ICustomTypeDescriptor.GetEvents()
            => TypeDescriptor.GetProvider(this).GetTypeDescriptor(this).GetEvents();

        EventDescriptorCollection ICustomTypeDescriptor.GetEvents(Attribute[]? attributes)
            => TypeDescriptor.GetProvider(this).GetTypeDescriptor(this).GetEvents(attributes);

        PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties()
            => ((ICustomTypeDescriptor)this).GetProperties(null);

        PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties(Attribute[]? attributes)
        {
            var props = TypeDescriptor.GetProvider(this).GetTypeDescriptor(this).GetProperties(attributes);
            var filtered = props.Cast<PropertyDescriptor>()
                .Where(p => !string.Equals(p.Name, "IsActivated", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(p.Name, "Visible", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(p.Name, "Locked", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return new PropertyDescriptorCollection(filtered, readOnly: true);
        }

        object ICustomTypeDescriptor.GetPropertyOwner(PropertyDescriptor? pd)
            => this;

        [OFTParameter]
        [Category("Visualisierung")]
        [DisplayName("MarketStructure Zonen anzeigen")]
        public bool ShowMarketStructureZones { get; set; } = true;

        [OFTParameter]
        [Category("Visualisierung")]
        [DisplayName("MarketStateV2 Status oben anzeigen")]
        public bool ShowMarketStateV2Overlay { get; set; } = true;

        [OFTParameter]
        [Category("Visualisierung")]
        [DisplayName("MarketStructure Zonen-Labels")]
        public bool ShowMarketStructureZoneLabels { get; set; } = true;

        [OFTParameter]
        [Category("MarketStructure")]
        [DisplayName("Zonen-Sensitivität (0-10)")]
        [Description("Bündelt die ZigZag-/Zonen-Strenge. Höher = weniger, aber signifikantere Zonen (größere Mindestbewegung, mehr Left/Right-Bars, stärkere Konsolidierung).")]
        [DefaultValue(3)]
        [Range(0, 10)]
        public int MarketStructureZigZagSensitivity { get; set; } = 3;

        [OFTParameter]
        [Category("MarketStructure")]
        [DisplayName("Wick-Mindestlänge (Ticks)")]
        [Description("Mindestlänge des relevanten Wicks (in Ticks), damit aus einem Swing eine Zone gebaut wird. Größer = weniger Zonen, dafür stärker gefiltert.")]
        [DefaultValue(4)]
        [Range(1, 50)]
        public int MarketStructureWickMinTicks { get; set; } = 4;

        [OFTParameter]
        [Category("MarketStructure")]
        [DisplayName("Wick-Min. an Volatilität koppeln")]
        [Description("Wenn aktiv, wird die Wick-Mindestlänge automatisch je Volatilitäts-Regime skaliert (Slow/Normal/Fast).")]
        [DefaultValue(true)]
        public bool MarketStructureAdaptiveWickByRegime { get; set; } = false;

        [OFTParameter]
        [Category("MarketStructure")]
        [DisplayName("Wick-Multiplikator (Slow)")]
        [Description("Skalierung der Wick-Mindestlänge im Slow-Regime. Beispiel 0.8 = 20% weniger Wick-Anforderung in ruhigem Markt.")]
        [DefaultValue(0.8)]
        [Range(0.1, 5.0)]
        public decimal MarketStructureAdaptiveWickSlowMult { get; set; } = 0.8m;

        [OFTParameter]
        [Category("MarketStructure")]
        [DisplayName("Wick-Multiplikator (Fast)")]
        [Description("Skalierung der Wick-Mindestlänge im Fast-Regime. Beispiel 1.4 = 40% mehr Wick-Anforderung in volatilen Phasen.")]
        [DefaultValue(1.4)]
        [Range(0.1, 5.0)]
        public decimal MarketStructureAdaptiveWickFastMult { get; set; } = 1.4m;

        [OFTParameter]
        [Category("Visualisierung")]
        [DisplayName("Synthetic Tick900 Debug")]
        public bool ShowSyntheticTick900CandlesDebug { get; set; } = false;

        [OFTParameter]
        [Category("Reversal Settings")]
        [DisplayName("Compression Gap (Ticks)")]
        [Description("Blockiert Entry + Story, wenn bestätigte SUP/RES-Zonen zu nah beieinander liegen (Gap in Ticks).")]
        [DefaultValue(12)]
        [Range(0, 100)]
        public int ReversalCompressionGapTicks { get; set; } = 8;

        [OFTParameter]
        [Category("Continuation Settings")]
        [DisplayName("Compression Gap (Ticks)")]
        [Description("Blockiert Entry + Story, wenn bestätigte SUP/RES-Zonen zu nah beieinander liegen (Gap in Ticks). 0 = verwende Thresholds.")]
        [DefaultValue(12)]
        [Range(0, 100)]
        public int ContinuationCompressionGapTicks { get; set; } = 4;



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

        private int GetEffectiveMarketStructureWickMinTicks(MarketRegime regime)
        {
            int baseTicks = Math.Max(1, MarketStructureWickMinTicks);
            if (!MarketStructureAdaptiveWickByRegime)
                return baseTicks;

            decimal mult = 1.0m;
            if (regime == MarketRegime.Slow)
                mult = MarketStructureAdaptiveWickSlowMult;
            else if (regime == MarketRegime.Fast)
                mult = MarketStructureAdaptiveWickFastMult;

            if (mult <= 0m)
                mult = 1.0m;

            int eff = (int)Math.Round(baseTicks * mult, MidpointRounding.AwayFromZero);
            return Math.Max(1, eff);
        }

        private void EnsurePreviousDayLevelZonesInMarketStructure(
            MyNamespace.Strategies.MarketAnalysis.MarketStructureContext ctx,
            OvSnapshot currentSnapshot,
            int createdBar,
            DateTime currentDay)
        {
            if (ctx == null || currentSnapshot == null)
                return;

            if (_tickSize <= 0m)
                return;

            if (_pdZoneDay != currentDay)
            {
                _pdOpenZoneId = 0;
                _pdCloseZoneId = 0;
                _pdZoneDay = currentDay;

                _pdHighZonePendingTypeCloses = 0;
                _pdLowZonePendingTypeCloses = 0;
            }

            const int ZoneTicks = 4;
            decimal pad = ZoneTicks * _tickSize;

            void Upsert(ref int zoneId, ref int pendingTypeCloses, decimal levelValue)
            {
                if (levelValue <= 0m)
                    return;

                decimal low = levelValue - pad;
                decimal high = levelValue + pad;
                decimal mid = (low + high) * 0.5m;

                var type = MyNamespace.Strategies.MarketAnalysis.MarketStructureContext.ZoneType.Support;
                bool existsInCtx = false;
                if (zoneId > 0)
                {
                    try
                    {
                        MyNamespace.Strategies.MarketAnalysis.MarketStructureContext.Zone? existing = null;
                        var zones = ctx.ActiveZones;
                        for (int zi = 0; zi < zones.Count; zi++)
                        {
                            var z = zones[zi];
                            if (z != null && z.Id == zoneId)
                            {
                                existing = z;
                                existsInCtx = true;
                                break;
                            }
                        }
                        if (existing != null)
                            type = existing.Type;
                    }
                    catch { }
                }

                if (zoneId > 0 && !existsInCtx)
                {
                    zoneId = 0;
                    pendingTypeCloses = 0;
                }

                var desiredType = (MyNamespace.Strategies.MarketAnalysis.MarketStructureContext.ZoneType?)null;
                decimal px = currentSnapshot.Close;
                if (px > high)
                    desiredType = MyNamespace.Strategies.MarketAnalysis.MarketStructureContext.ZoneType.Support;
                else if (px < low)
                    desiredType = MyNamespace.Strategies.MarketAnalysis.MarketStructureContext.ZoneType.Resistance;
                else if (zoneId <= 0)
                    desiredType = px >= mid
                        ? MyNamespace.Strategies.MarketAnalysis.MarketStructureContext.ZoneType.Support
                        : MyNamespace.Strategies.MarketAnalysis.MarketStructureContext.ZoneType.Resistance;

                bool updateType = false;
                if (desiredType.HasValue)
                {
                    if (zoneId <= 0)
                    {
                        type = desiredType.Value;
                        pendingTypeCloses = 0;
                    }
                    else if (desiredType.Value == type)
                    {
                        pendingTypeCloses = 0;
                    }
                    else
                    {
                        pendingTypeCloses = Math.Max(0, pendingTypeCloses) + 1;
                        if (pendingTypeCloses >= 2)
                        {
                            type = desiredType.Value;
                            updateType = true;
                            pendingTypeCloses = 0;
                        }
                    }
                }

                zoneId = ctx.UpsertExternalZone(
                    existingZoneId: zoneId,
                    initialType: type,
                    low: low,
                    high: high,
                    createdBar: createdBar,
                    initialStatus: MyNamespace.Strategies.MarketAnalysis.MarketStructureContext.ZoneStatus.Ready,
                    confirmed: true,
                    updateTypeIfExists: updateType);
            }

            Upsert(ref _pdOpenZoneId, ref _pdHighZonePendingTypeCloses, _previousDayHigh);
            Upsert(ref _pdCloseZoneId, ref _pdLowZonePendingTypeCloses, _previousDayLow);
        }

        private void EnsureTick900BackfillRequested(int bar)
        {
            void LogGateOnce(string reason)
            {
                if (_msBackfillGateDiagLogged)
                    return;
                _msBackfillGateDiagLogged = true;
                try
                {
                    int curBar = -1;
                    try { curBar = CurrentBar; } catch { }

                    DateTime t0 = default;
                    DateTime tLast = default;
                    DateTime tLastLast = default;
                    try
                    {
                        var c0 = GetCandle(0);
                        if (c0 != null)
                            t0 = c0.Time;
                    }
                    catch { }
                    try
                    {
                        var idx = Math.Max(0, curBar - 1);
                        var cl = GetCandle(idx);
                        if (cl != null)
                        {
                            tLast = cl.Time;
                            tLastLast = cl.LastTime;
                        }
                    }
                    catch { }

                    //this.LogInfo($"[Tick900Backfill:{_msInstanceId}] Backfill gate blocked: reason='{reason}' bar={bar} CurrentBar={curBar} firstTime={t0:O} lastTime={tLast:O} lastLastTime={tLastLast:O} requested={_msTick900BackfillRequested} completed={_msTick900BackfillCompleted} tickBars={_msTick900Bar}");
                }
                catch { }
            }

            if (!IsMarketStructureLeader())
            {
                LogGateOnce("not-leader");
                return;
            }
            if (_msTick900BackfillRequested)
            {
                LogGateOnce("already-requested");
                return;
            }
            if (_msTick900Aggregator == null || _marketStructureContext == null)
            {
                LogGateOnce("null-aggregator-or-context");
                return;
            }

            // Wenn ATAS beim Reload/Template-Apply kurzzeitig mehrere Instanzen gleichzeitig laufen lässt,
            // darf nur die neueste Instanz Backfill/MarketStructure erzeugen.
            if (_msGeneration != Volatile.Read(ref _msGlobalGeneration))
            {
                LogGateOnce("not-latest-generation");
                return;
            }

            // Backfill nur am rechten Rand auslösen. Während der historischen Berechnung ruft ATAS OnCalculate
            // für viele Bars auf, wobei CurrentBar/LastTime noch im Aufbau ist. Ein zu früher Request führt zu
            // Teilbereichen (z.B. nur bis 00:13) und verschobenen Zonen.
            if (bar < Math.Max(0, CurrentBar - 1))
            {
                LogGateOnce("not-at-right-edge");
                return;
            }

            try
            {
                if (CurrentBar <= 0)
                {
                    LogGateOnce("CurrentBar<=0");
                    return;
                }

                var lastIdx = Math.Max(0, CurrentBar - 1);
                var lastCandle = GetCandle(lastIdx);
                if (lastCandle == null)
                {
                    LogGateOnce("lastCandle-null");
                    return;
                }

                var firstCandle = GetCandle(0);
                if (firstCandle == null)
                {
                    LogGateOnce("firstCandle-null");
                    return;
                }

                var endTime = lastCandle.LastTime;
                if (endTime == default)
                {
                    LogGateOnce("endTime-default");
                    return;
                }

                // Backtest/Replay Reload kann den Chart-Zeitraum zurücksetzen (EndTime springt rückwärts).
                // In diesem Fall darf die globale Watermark den neuen Request NICHT blockieren.
                // Wir resetten die Watermark und lokalen Tick900-Zustand, sodass ein neuer Backfill möglich ist.
                try
                {
                    var globalEndTicks0 = Volatile.Read(ref _msGlobalBackfillRequestedEndTimeTicks);
                    // Only treat this as a true reset if the time jump is meaningful.
                    // During replay/load, ATAS can temporarily report slightly earlier endTime while building the chart.
                    var backJump = globalEndTicks0 != 0 ? (globalEndTicks0 - endTime.Ticks) : 0;
                    if (globalEndTicks0 != 0 && backJump > TimeSpan.FromMinutes(5).Ticks)
                    {
                        Interlocked.Exchange(ref _msGlobalBackfillRequestedEndTimeTicks, 0);
                        _msTick900BackfillRequestedEndTime = default;
                        _msTick900BackfillRequested = false;
                        _msTick900BackfillCompleted = false;
                        _msTick900ResetOnNextBackfillResponse = true;
                        ClearSyntheticTick900Debug();
                        _msBackfillGateDiagLogged = false;
                        //this.LogWarn($"[Tick900Backfill:{_msInstanceId}] Detected backward chart time jump -> reset global watermark and Tick900 state. endTime={endTime:O} prevGlobalTicks={globalEndTicks0}");
                    }
                }
                catch { }

                // Wenn wir bereits einen Backfill abgeschlossen haben, aber der Chart inzwischen weiter nach rechts
                // nachgeladen wurde, müssen wir nachziehen. Sonst bleiben Tick900-Candles/Zonen auf dem frühen Stand.
                // Wir rebuilden bewusst komplett, weil MarketStructureContext auf Sequenz-Logik basiert.
                if (_msTick900BackfillCompleted && _msTick900BackfillRequestedEndTime != default)
                {
                    if (!IsNearRealTime(endTime))
                        return;

                    var minAdvance = TimeSpan.FromSeconds(30);
                    if (endTime <= _msTick900BackfillRequestedEndTime.Add(minAdvance))
                        return;

                    // Avoid constant rebuilds while replay/live trades are streaming.
                    // In that case, we should rely on OnNewTrade aggregation rather than re-backfilling every few seconds.
                    try
                    {
                        var nowUtc = DateTime.UtcNow;
                        var liveLag = nowUtc - _msLastLiveTradeWallClockUtc;
                        if (_msLastLiveTradeWallClockUtc != default && liveLag > TimeSpan.FromSeconds(15))
                        {
                            _msTick900RebuildAfterResumeNeeded = true;
                            return;
                        }
                        if (!_msTick900RebuildAfterResumeNeeded && _msLastLiveTradeWallClockUtc != default && liveLag < TimeSpan.FromSeconds(2))
                            return;
                    }
                    catch { }

                    // Cooldown on refresh requests.
                    try
                    {
                        var nowUtc = DateTime.UtcNow;
                        if (_msLastBackfillRequestWallClockUtc != default && nowUtc - _msLastBackfillRequestWallClockUtc < TimeSpan.FromSeconds(10))
                            return;
                    }
                    catch { }

                    // Wichtig: nicht sofort resetten, sonst "blitzen" Zonen (Render sieht ActiveZones=0)
                    // während der neue Backfill noch in-flight ist.
                    // Reset erfolgt atomar beim Eintreffen der nächsten Backfill-Response.
                    _msTick900ResetOnNextBackfillResponse = true;
                    _msTick900BackfillCompleted = false;
                    _msTick900RebuildAfterResumeNeeded = false;

					_msTick900BackfillRequested = false;
					_msTick900BackfillRequestDiagLogged = false;
                }

                // Prozessweiter Guard: über alle Strategie-Instanzen hinweg nie ein älteres/gleiches EndTime anfordern.
                // Sonst kann eine spätere zweite Instanz den Zustand mit einem Backfill für einen früheren Tag überschreiben.
                var globalEndTicks = Volatile.Read(ref _msGlobalBackfillRequestedEndTimeTicks);
                if (globalEndTicks != 0 && endTime.Ticks <= globalEndTicks)
                {
                    try
                    {
                        bool localEmpty = !_msTick900BackfillCompleted && _msTick900Bar == 0 && (_msTick900ClosedCandles == null || _msTick900ClosedCandles.Count == 0);
                        if (localEmpty)
                        {
                            Interlocked.Exchange(ref _msGlobalBackfillRequestedEndTimeTicks, 0);
                            _msBackfillGateDiagLogged = false;
                            globalEndTicks = 0;
                        }
                    }
                    catch { }

                    if (globalEndTicks != 0)
                    {
                        LogGateOnce("global-endtime-watermark-block");
                        return;
                    }
                }

                var firstTime = firstCandle.Time;
                if (firstTime == default)
                {
                    LogGateOnce("firstTime-default");
                    return;
                }

                // Replay/History kann den Chart-Zeitraum anfangs in Blöcken nachladen.
                // Ein "warte auf mehrere stabile Calls"-Gate kann dazu führen, dass bei pausiertem Replay
                // gar kein Backfill ausgelöst wird (nur 1 Calculate-Call auf dem letzten Bar).
                // Daher tracken wir die Kandidaten weiterhin, blockieren den Request aber nicht mehr.
                if (firstTime != _msTick900BackfillCandidateFirstTime || endTime != _msTick900BackfillCandidateEndTime)
                {
                    _msTick900BackfillCandidateFirstTime = firstTime;
                    _msTick900BackfillCandidateEndTime = endTime;
                    _msTick900BackfillCandidateStableCount = 0;
                }
                else
                {
                    _msTick900BackfillCandidateStableCount++;
                }

                // Guard: Wenn wir bereits ein neueres/gleiches EndTime angefordert hatten, nie "rückwärts" neu anfordern.
                if (_msTick900BackfillRequestedEndTime != default && endTime <= _msTick900BackfillRequestedEndTime)
                {
                    LogGateOnce("instance-endtime-not-advancing");
                    return;
                }

                DateTime startTime;
                try
                {
                    // Anchor Tick900 backfill to the current trading session start.
                    // Using the full chart history start can create very large requests in live mode,
                    // delaying/aborting response processing and preventing debug candle rendering.
                    startTime = NormalizeToChartTime(GetSessionStartTimeForSwingSeed(endTime));
                }
                catch
                {
                    startTime = endTime;
                }

                if (startTime == default)
                    startTime = endTime;

                if (startTime == default)
                    startTime = endTime;

                // Atomic in-flight gate: prevent duplicate parallel requests (same instance/thread races).
                // Without this, OnCalculate can dispatch multiple identical CT requests before the bool flags settle.
                if (Interlocked.CompareExchange(ref _msTick900BackfillInFlight, 1, 0) != 0)
                {
                    LogGateOnce("backfill-in-flight-atomic");
                    return;
                }

                // Process-wide in-flight gate: prevent duplicate requests from parallel instances/appdomains.
                if (Interlocked.CompareExchange(ref _msGlobalTick900BackfillInFlight, 1, 0) != 0)
                {
                    LogGateOnce("global-backfill-in-flight-atomic");
                    Interlocked.Exchange(ref _msTick900BackfillInFlight, 0);
                    return;
                }

                // OS-wide gate (named semaphore): protects against duplicate requests across parallel AppDomains
                // where static fields are not shared reliably. Semaphore is intentionally non-reentrant.
                if (_msBackfillRequestSemaphore != null)
                {
                    bool gotGate = false;
                    try { gotGate = _msBackfillRequestSemaphore.WaitOne(0); } catch { gotGate = false; }
                    if (!gotGate)
                    {
                        LogGateOnce("global-backfill-semaphore-busy");
                        Interlocked.Exchange(ref _msTick900BackfillInFlight, 0);
                        Interlocked.Exchange(ref _msGlobalTick900BackfillInFlight, 0);
                        return;
                    }

                    _msBackfillRequestSemaphoreOwned = true;
                }

                var request = new CumulativeTradesRequest(startTime, endTime, 0, 0);
                _msTick900BackfillRequested = true;
                _msTick900BackfillRequestedEndTime = endTime;
                try { _msLastBackfillRequestWallClockUtc = DateTime.UtcNow; } catch { }

                if (!_msTick900BackfillRequestDiagLogged)
                {
                    _msTick900BackfillRequestDiagLogged = true;
                    this.LogInfo($"[Tick900Backfill:{_msInstanceId}] Request cumulative trades: {startTime:yyyy-MM-dd HH:mm:ss}..{endTime:yyyy-MM-dd HH:mm:ss} (end={endTime:O}) CurrentBar={CurrentBar}");
                }

                // Prozessweite Watermark setzen (monoton steigend). Falls parallel ein anderer Thread/Instanz ebenfalls
                // etwas setzen will, gewinnt das höhere EndTime.
                long newTicks = endTime.Ticks;
                while (true)
                {
                    var cur = Volatile.Read(ref _msGlobalBackfillRequestedEndTimeTicks);
                    if (cur >= newTicks)
                        break;
                    if (Interlocked.CompareExchange(ref _msGlobalBackfillRequestedEndTimeTicks, newTicks, cur) == cur)
                        break;
                }

                RequestForCumulativeTrades(request);
                //this.LogInfo($"[Tick900Backfill:{_msInstanceId}] Requesting cumulative trades: {startTime:O}..{endTime:O} firstTime={firstTime:O} endTime={endTime:O} CurrentBar={CurrentBar}");
            }
            catch (Exception ex)
            {
                //this.LogWarn($"[Tick900Backfill:{_msInstanceId}] Request failed: {ex.GetType().Name}: {ex.Message}");
                _msTick900BackfillRequested = false;
                Interlocked.Exchange(ref _msTick900BackfillInFlight, 0);
                Interlocked.Exchange(ref _msGlobalTick900BackfillInFlight, 0);
                if (_msBackfillRequestSemaphoreOwned)
                {
                    try { _msBackfillRequestSemaphore?.Release(); } catch { }
                    _msBackfillRequestSemaphoreOwned = false;
                }
            }

        }

        private void TrackBestBidAskFreeze()
        {
            try
            {
                var sec = Security;
                if (sec == null)
                    return;

                decimal ask = sec.BestAskPrice;
                decimal bid = sec.BestBidPrice;
                decimal last = sec.LastTradePrice ?? 0m;

                var now = DateTime.UtcNow;

                bool changed = false;
                if (ask != _bbbaLastAsk || bid != _bbbaLastBid)
                {
                    changed = true;
                    _bbbaLastAsk = ask;
                    _bbbaLastBid = bid;
                    _bbbaLastChangeUtc = now;
                }

                // Track last trade changes separately (for freeze detection)
                bool lastChanged = last > 0m && last != _bbbaLastTrade;
                if (lastChanged)
                    _bbbaLastTrade = last;

                if (_bbbaLastChangeUtc == DateTime.MinValue)
                {
                    // initialize change timestamp on first run
                    if (ask > 0m || bid > 0m)
                        _bbbaLastChangeUtc = now;
                    else
                        return;
                }

                // Freeze definition: Bid/Ask unchanged >= 5s while LastTrade is changing
                if (!changed && lastChanged)
                {
                    var staleFor = now - _bbbaLastChangeUtc;
                    if (staleFor.TotalSeconds >= 5)
                    {
                        if (_bbbaLastWarnUtc == DateTime.MinValue || (now - _bbbaLastWarnUtc).TotalSeconds >= 5)
                        {
                            _bbbaLastWarnUtc = now;
                            this.LogWarn($"[QuoteFreeze] BestBid/Ask unchanged for {staleFor.TotalSeconds:F1}s while LastTrade moves. Last={last:F2}, BestAsk={ask:F2}, BestBid={bid:F2}, CurrentBar={CurrentBar}.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.LogWarn($"[QuoteFreeze] Tracker error: {ex.Message}");
            }
        }

        protected override void OnCumulativeTradesResponse(CumulativeTradesRequest request, IEnumerable<CumulativeTrade> cumulativeTrades)
        {
            if (!IsMarketStructureLeader())
                return;
            if (!UseTick900ForMarketStructure)
                return;
            if (_msTick900Aggregator == null || _marketStructureContext == null)
                return;

            if (_msGeneration != Volatile.Read(ref _msGlobalGeneration))
                return;

            // Prozessweiter Guard: nur die Antwort zum neuesten angeforderten EndTime verarbeiten.
            // Damit kann eine zweite Instanz (oder ein späteres, älteres Request) den Zustand nicht mehr "zurücksetzen".
            try
            {
                var globalEndTicks = Volatile.Read(ref _msGlobalBackfillRequestedEndTimeTicks);
                if (globalEndTicks != 0 && request != null && request.EndTime.Ticks < globalEndTicks)
                    return;

                // Duplicate responses for the same end-time can arrive (cache/server pipeline).
                // Reprocessing them causes long freezes and can desync Tick900 state.
                if (request != null && _msTick900LastProcessedBackfillEndTime != default && request.EndTime <= _msTick900LastProcessedBackfillEndTime)
                    return;
            }
            catch { }

            if (_msTick900ResetOnNextBackfillResponse)
            {
                _msTick900ResetOnNextBackfillResponse = false;
                this.LogInfo($"[Tick900Backfill:{_msInstanceId}] Reset Tick900 state on backfill response. endTime={request?.EndTime:O}");
                try
                {
                    _marketStructureContext.Reset();
                }
                catch { }

                try
                {
                    _msTick900Aggregator.Reset();
                    _msTick900BucketSessionStart = DateTime.MinValue;
                    _msTick900Bar = 0;
                    _msTick900LiveTradeBuffer.Clear();
                    _msTick900ClosedCandles.Clear();
                    _msChartTimeKind = null;
                }
                catch { }
            }

            try
            {
                _msTick900Aggregator.Reset();
                _msTick900BucketSessionStart = DateTime.MinValue;
                _msTick900Bar = 0;
                _msTick900BackfillSawTicks = false;
                _msTick900ClosedCandles.Clear();
                _msChartTimeKind = null;

                int backfillTickCount = 0;
                DateTime backfillStartTime = default;
                var backfillEndTime = request != null ? NormalizeToChartTime(request.EndTime) : default;

                var list = cumulativeTrades?.ToList() ?? new List<CumulativeTrade>();
                if (list.Count > 1)
                    list.Sort((a, b) => a.Time.CompareTo(b.Time));
                if (list.Count > 0)
                    backfillStartTime = NormalizeToChartTime(list[0].Time);

                bool processedAllTicks = false;
                try
                {
                    var allTicks = new List<MarketDataArg>(Math.Max(1024, list.Count * 4));
                    for (int i = 0; i < list.Count; i++)
                    {
                        var tks = list[i]?.Ticks;
                        if (tks == null)
                            continue;

                        try
                        {
                            allTicks.AddRange(tks);
                        }
                        catch
                        {
                            foreach (var tk in tks)
                                allTicks.Add(tk);
                        }
                    }

                    if (allTicks.Count > 0)
                    {
                        backfillTickCount = allTicks.Count;
                        _msTick900BackfillSawTicks = true;

                        List<(DateTime T, MarketDataArg A)> allTicksByChartTime;
                        DateTime maxTickChartTime = default;
                        try
                        {
                            allTicksByChartTime = new List<(DateTime, MarketDataArg)>(allTicks.Count);
                            for (int i = 0; i < allTicks.Count; i++)
                            {
                                var a = allTicks[i];
                                if (a == null) continue;
                                var tt = NormalizeToChartTime(a.Time);
                                if (maxTickChartTime == default || tt > maxTickChartTime)
                                    maxTickChartTime = tt;
                                allTicksByChartTime.Add((tt, a));
                            }
                            if (allTicksByChartTime.Count > 1)
                                allTicksByChartTime.Sort((x, y) => x.T.CompareTo(y.T));
                        }
                        catch
                        {
                            allTicksByChartTime = null;
                        }

                        var src = allTicksByChartTime != null
                            ? allTicksByChartTime.Select(x => x.A)
                            : allTicks;

                        foreach (var tick in src)
                        {
                            var tt = NormalizeToChartTime(tick.Time);

                            var sessStart = GetSessionStartTimeForSwingSeed(tt);
                            if (_msTick900BucketSessionStart == DateTime.MinValue)
                                _msTick900BucketSessionStart = sessStart;
                            else if (sessStart != _msTick900BucketSessionStart)
                            {
                                _msTick900Aggregator.Reset();
                                _msTick900BucketSessionStart = sessStart;
                            }

                            decimal incNetDeltaTick = 0m;
                            try
                            {
                                var dir = tick.Direction.ToString();
                                if (dir == "Buy")
                                    incNetDeltaTick = tick.Volume;
                                else if (dir == "Sell")
                                    incNetDeltaTick = -tick.Volume;
                            }
                            catch { }

                            if (_msTick900Aggregator.AddTrade(tt, tick.Price, tick.Volume, incNetDeltaTick, out var closedFromTick) && closedFromTick != null)
                            {
                                _msTick900Bar++;
                                RecordSyntheticTick900ClosedCandle(closedFromTick);
                                _marketStructureContext.Update(
                                    _msTick900Bar,
                                    closedFromTick,
                                    snapshot: null,
                                    tickSize: _tickSize,
                                    vwap: 0m,
                                    recentOf: null,
                                    allowZoneCreation: true,
                                    allowZoneLifecycle: false);

                                try
                                {
                                    int maxId = -1;
                                    var zonesNow = _marketStructureContext.ActiveZones;
                                    if (zonesNow != null)
                                    {
                                        for (int zi = 0; zi < zonesNow.Count; zi++)
                                        {
                                            var z = zonesNow[zi];
                                            if (z == null) continue;
                                            if (z.Status == MyNamespace.Strategies.MarketAnalysis.MarketStructureContext.ZoneStatus.Used) continue;
                                            if (z.Id > maxId) maxId = z.Id;
                                        }
                                    }
                                    if (maxId > _msTick900LastSeenMaxZoneId)
                                    {
                                        _msTick900LastSeenMaxZoneId = maxId;
                                        System.Threading.Interlocked.Exchange(ref _msTick900ZonesDirtyFlag, 1);
                                    }
                                }
                                catch { }
                            }
                        }

                        processedAllTicks = true;

                        try
                        {
                            this.LogInfo($"[Tick900Backfill:{_msInstanceId}] Backfill tick stats: ticks={allTicks.Count} maxTickChartTime={maxTickChartTime:O} endTime={request?.EndTime:O}");
                        }
                        catch { }
                    }
                }
                catch
                {
                    processedAllTicks = false;
                }

                CumulativeTrade prev = null;

                foreach (var ct in list)
                {
                    if (processedAllTicks)
                        break;
                    // Prefer true trade-by-trade ticks if available (align with ATAS indicators like TapePattern).
                    // This yields correct 900-tick candles, instead of counting each CumulativeTrade as one tick.
                    bool processedTicks = false;
                    try
                    {
                        var ticks = ct.Ticks;
                        if (ticks != null)
                        {
                            _msTick900BackfillSawTicks = true;
                            List<MarketDataArg> tickEnum;
                            try
                            {
                                tickEnum = ticks.ToList();
                                if (tickEnum.Count > 1)
                                    tickEnum.Sort((a, b) => a.Time.CompareTo(b.Time));
                            }
                            catch
                            {
                                tickEnum = null;
                            }

                            var sourceEnum = (IEnumerable<MarketDataArg>)(tickEnum ?? ticks);
                            foreach (var tick in sourceEnum)
                            {
                                // Normalize tick time to chart/local time (ATAS indicators often apply InstrumentInfo.TimeZone).
                                // For our bucketing/zone alignment, we must be consistent with GetCandle(...) times.
                                var tt = NormalizeToChartTime(tick.Time);

                                var sessStart = GetSessionStartTimeForSwingSeed(tt);
                                if (_msTick900BucketSessionStart == DateTime.MinValue)
                                    _msTick900BucketSessionStart = sessStart;
                                else if (sessStart != _msTick900BucketSessionStart)
                                {
                                    _msTick900Aggregator.Reset();
                                    _msTick900BucketSessionStart = sessStart;
                                }

                                decimal incNetDeltaTick = 0m;
                                try
                                {
                                    var dir = tick.Direction.ToString();
                                    if (dir == "Buy")
                                        incNetDeltaTick = tick.Volume;
                                    else if (dir == "Sell")
                                        incNetDeltaTick = -tick.Volume;
                                }
                                catch { }

                                if (_msTick900Aggregator.AddTrade(tt, tick.Price, tick.Volume, incNetDeltaTick, out var closedFromTick) && closedFromTick != null)
                                {
                                    _msTick900Bar++;
                                    RecordSyntheticTick900ClosedCandle(closedFromTick);
                                    _marketStructureContext.Update(
                                        _msTick900Bar,
                                        closedFromTick,
                                        snapshot: null,
                                        tickSize: _tickSize,
                                        vwap: 0m,
                                        recentOf: null,
                                        allowZoneCreation: true,
                                        allowZoneLifecycle: false);

                                    try
                                    {
                                        int maxId = -1;
                                        var zonesNow = _marketStructureContext.ActiveZones;
                                        if (zonesNow != null)
                                        {
                                            for (int zi = 0; zi < zonesNow.Count; zi++)
                                            {
                                                var z = zonesNow[zi];
                                                if (z == null) continue;
                                                if (z.Status == MyNamespace.Strategies.MarketAnalysis.MarketStructureContext.ZoneStatus.Used) continue;
                                                if (z.Id > maxId) maxId = z.Id;
                                            }
                                        }
                                        if (maxId > _msTick900LastSeenMaxZoneId)
                                        {
                                            _msTick900LastSeenMaxZoneId = maxId;
                                            System.Threading.Interlocked.Exchange(ref _msTick900ZonesDirtyFlag, 1);
                                        }
                                    }
                                    catch { }
                                }
                            }

                            processedTicks = true;
                        }
                    }
                    catch
                    {
                        processedTicks = false;
                    }

                    if (processedTicks)
                    {
                        prev = ct;
                        continue;
                    }

                    if (!_msTick900BackfillSawTicks && !_msTick900BackfillTicksDiagLogged)
                    {
                        _msTick900BackfillTicksDiagLogged = true;
                        //this.LogWarn($"[Tick900Backfill:{_msInstanceId}] Backfill response has no per-trade tick list (ct.Ticks==null). 900-tick candles are approximate and may desync vs ATAS Tick(900) chart.");
                    }

                    // Fallback: bucket by cumulative-trade timestamp
                    var t = NormalizeToChartTime(ct.Time);
                    var sessStart2 = GetSessionStartTimeForSwingSeed(t);
                    if (_msTick900BucketSessionStart == DateTime.MinValue)
                        _msTick900BucketSessionStart = sessStart2;
                    else if (sessStart2 != _msTick900BucketSessionStart)
                    {
                        _msTick900Aggregator.Reset();
                        _msTick900BucketSessionStart = sessStart2;
                    }

                    // CumulativeTrade kann als "Update" des letzten Trades gesendet werden.
                    // In diesem Fall darf nur das Inkrement in Volume/Delta verarbeitet werden.
                    bool isUpdateOfPrev = false;
                    if (prev != null)
                    {
                        try
                        {
                            // ATAS liefert i.d.R. IsEqual für CumulativeTrade (siehe z.B. TapePattern/PublicActiveVolume).
                            // Falls das aus irgendeinem Grund nicht verfügbar ist, fallback auf Heuristik.
                            isUpdateOfPrev = prev.IsEqual(ct);
                        }
                        catch
                        {
                            try
                            {
                                isUpdateOfPrev = prev.Time == ct.Time && prev.FirstPrice == ct.FirstPrice;
                            }
                            catch { }
                        }
                    }

                    decimal incVol;
                    decimal incNetDelta;
                    try
                    {
                        incVol = isUpdateOfPrev ? (ct.Volume - prev.Volume) : ct.Volume;
                    }
                    catch
                    {
                        incVol = ct.Volume;
                    }

                    try
                    {
                        var ask = ct.NewAsk.Volume;
                        var bid = ct.NewBid.Volume;

                        if (isUpdateOfPrev)
                        {
                            var prevAsk = prev.NewAsk.Volume;
                            var prevBid = prev.NewBid.Volume;
                            incNetDelta = (ask - prevAsk) - (bid - prevBid);
                        }
                        else
                        {
                            incNetDelta = ask - bid;
                        }
                    }
                    catch
                    {
                        incNetDelta = 0m;
                    }

                    prev = ct;

                    if (incVol == 0m && incNetDelta == 0m)
                        continue;

                    if (_msTick900Aggregator.AddTrade(t, ct.FirstPrice, incVol, incNetDelta, out var closedTickCandle) && closedTickCandle != null)
                    {
                        _msTick900Bar++;
                        RecordSyntheticTick900ClosedCandle(closedTickCandle);
                        _marketStructureContext.Update(
                            _msTick900Bar,
                            closedTickCandle,
                            snapshot: null,
                            tickSize: _tickSize,
                            vwap: 0m,
                            recentOf: null,
                            allowZoneCreation: true,
                            allowZoneLifecycle: false);
                    }
                }

                _msTick900BackfillCompleted = true;
                if (_msTick900LiveTradeBuffer.Count > 0)
                {
                    int liveBufApplied = 0;
                    int liveBufSkipped = 0;
                    foreach (var a in _msTick900LiveTradeBuffer)
                    {
                        if (a == null) continue;
                        // Verhindere Doppelzählung: Trades, die zeitlich im Backfill-Fenster liegen, dürfen nicht
                        // nochmal in den Aggregator (sonst verschieben sich 900-Tick-Candle-Grenzen und Zonen driften).
                        try
                        {
                            if (_msTick900BackfillRequestedEndTime != default)
                            {
                                var at = NormalizeToChartTime(a.Time);
                                var et = NormalizeToChartTime(_msTick900BackfillRequestedEndTime);
                                if (at <= et)
                                {
                                    liveBufSkipped++;
                                    continue;
                                }
                            }
                        }
                        catch { }
                        liveBufApplied++;
                        var localTime = NormalizeToChartTime(a.Time);
                        var sessStart3 = GetSessionStartTimeForSwingSeed(localTime);
                        if (_msTick900BucketSessionStart == DateTime.MinValue)
                            _msTick900BucketSessionStart = sessStart3;
                        else if (sessStart3 != _msTick900BucketSessionStart)
                        {
                            _msTick900Aggregator.Reset();
                            _msTick900BucketSessionStart = sessStart3;
                        }

                        decimal incNetDeltaTick = 0m;
                        try
                        {
                            var dir = a.Direction.ToString();
                            if (dir == "Buy")
                                incNetDeltaTick = a.Volume;
                            else if (dir == "Sell")
                                incNetDeltaTick = -a.Volume;
                        }
                        catch { }

                        if (_msTick900Aggregator.AddTrade(localTime, a.Price, a.Volume, incNetDeltaTick, out var closedTickCandle2) && closedTickCandle2 != null)
                        {
                            _msTick900Bar++;
                            RecordSyntheticTick900ClosedCandle(closedTickCandle2);
                            var vwapNow = _currentVwapSnapshot?.Current ?? 0m;
                            _marketStructureContext.Update(
                                _msTick900Bar,
                                closedTickCandle2,
                                snapshot: null,
                                tickSize: _tickSize,
                                vwap: vwapNow,
                                recentOf: null,
                                allowZoneCreation: true,
                                allowZoneLifecycle: false);
                        }
                    }
                    _msTick900LiveTradeBuffer.Clear();

					try
					{
						_msTick900LiveBufAppliedLastBackfill = liveBufApplied;
						_msTick900LiveBufSkippedLastBackfill = liveBufSkipped;
					}
					catch { }
                }
                else
                {
					try
					{
						_msTick900LiveBufAppliedLastBackfill = 0;
						_msTick900LiveBufSkippedLastBackfill = 0;
					}
					catch { }
                }

                NormalizeZonesAfterBackfill();

                try
                {
                    var zc = _marketStructureContext?.ActiveZones?.Count ?? 0;
                    //this.LogInfo($"[Tick900Backfill:{_msInstanceId}] Zones after backfill: active={zc}");
                }
                catch { }

                if (IsMarketStructureLeader())
                    WriteZonesSnapshot();

                

                try
                {
                    //this.LogInfo($"[Tick900Backfill:{_msInstanceId}] Backfill tick-list availability: ctTicksPresent={_msTick900BackfillSawTicks}");
                }
                catch { }

                try
                {
                    var lastClosed = _msTick900ClosedCandles != null && _msTick900ClosedCandles.Count > 0
                        ? _msTick900ClosedCandles[_msTick900ClosedCandles.Count - 1]
                        : null;
                    var lastTime = lastClosed != null ? lastClosed.Time : default;
                    var lastLastTime = lastClosed != null ? lastClosed.LastTime : default;
                    this.LogInfo(
                        $"[Tick900Backfill:{_msInstanceId}] Completed backfill: endTime={request?.EndTime:O} ctItems={list.Count} ctTicksPresent={_msTick900BackfillSawTicks} " +
                        $"tickBars={_msTick900Bar} closedCandles={(_msTick900ClosedCandles != null ? _msTick900ClosedCandles.Count : 0)} " +
                        $"lastCandleTime={lastTime:O} lastCandleLastTime={lastLastTime:O} " +
                        $"liveBufApplied={_msTick900LiveBufAppliedLastBackfill} liveBufSkipped={_msTick900LiveBufSkippedLastBackfill} " +
                        $"liveBufRemaining={(_msTick900LiveTradeBuffer != null ? _msTick900LiveTradeBuffer.Count : 0)} requestedEnd={_msTick900BackfillRequestedEndTime:O}");
                }
                catch { }

                try
                {
                    var forming = _msTick900Aggregator.GetCurrentFormingCandle();
                    if (forming != null)
                    {
                        this.LogInfo(
                            $"[Tick900Backfill:{_msInstanceId}] Forming Tick900: tradeCount={forming.TradeCount} " +
                            $"time={forming.Time:O} lastTime={forming.LastTime:O} " +
                            $"OHLC=({forming.Open:F2},{forming.High:F2},{forming.Low:F2},{forming.Close:F2}) vol={forming.Volume} netD={forming.NetDelta}");
                    }
                    else
                    {
                        this.LogInfo($"[Tick900Backfill:{_msInstanceId}] Forming Tick900: none (count={_msTick900Aggregator.CurrentCount})");
                    }
                }
                catch { }

                //this.LogInfo($"[Tick900Backfill:{_msInstanceId}] Completed. Trades={list.Count} tickBars={_msTick900Bar}");

                try
                {
                    if (backfillTickCount > 0 && backfillStartTime != default && backfillEndTime != default)
                    {
                        int chartClosedBarsInRange = 0;
                        for (int i = 0; i < CurrentBar; i++)
                        {
                            var c = GetCandle(i);
                            if (c == null)
                                continue;

                            var lt = NormalizeToChartTime(c.LastTime != default ? c.LastTime : c.Time);
                            if (lt > backfillStartTime && lt <= backfillEndTime)
                                chartClosedBarsInRange++;
                        }

                        int formingRemainder = backfillTickCount % 900;
                        int lower = (900 * chartClosedBarsInRange) - backfillTickCount;
                        int upper = (900 * (chartClosedBarsInRange + 1) - 1) - backfillTickCount;
                        bool carryRangeValid = upper >= 0 && lower <= 899;
                        if (carryRangeValid)
                        {
                            if (lower < 0) lower = 0;
                            if (upper > 899) upper = 899;
                            this.LogInfo($"[Tick900Backfill:{_msInstanceId}] Tick phase diag: range={backfillStartTime:O}..{backfillEndTime:O} ticks={backfillTickCount} chartClosedBars={chartClosedBarsInRange} formingRemainder={formingRemainder} estimatedInitialCarryTicks={lower}..{upper}");
                        }
                        else
                        {
                            this.LogInfo($"[Tick900Backfill:{_msInstanceId}] Tick phase diag: range={backfillStartTime:O}..{backfillEndTime:O} ticks={backfillTickCount} chartClosedBars={chartClosedBarsInRange} formingRemainder={formingRemainder} estimatedInitialCarryTicks=unknown (insufficient chart range overlap)");
                        }
                    }
                }
                catch { }

                // Backfill fertig: In-Flight Flag zurücksetzen, damit ein späteres "Nachziehen" möglich ist.
                _msTick900BackfillRequested = false;
                _msTick900BackfillCompleted = true;
                if (request != null)
                    _msTick900LastProcessedBackfillEndTime = request.EndTime;
                Interlocked.Exchange(ref _msTick900BackfillInFlight, 0);
                Interlocked.Exchange(ref _msGlobalTick900BackfillInFlight, 0);
                if (_msBackfillRequestSemaphoreOwned)
                {
                    try { _msBackfillRequestSemaphore?.Release(); } catch { }
                    _msBackfillRequestSemaphoreOwned = false;
                }
            }
            catch (Exception ex)
            {
                //this.LogWarn($"[Tick900Backfill:{_msInstanceId}] Response processing failed: {ex.GetType().Name}: {ex.Message}");
                _msTick900BackfillRequested = false;
                Interlocked.Exchange(ref _msTick900BackfillInFlight, 0);
                Interlocked.Exchange(ref _msGlobalTick900BackfillInFlight, 0);
                if (_msBackfillRequestSemaphoreOwned)
                {
                    try { _msBackfillRequestSemaphore?.Release(); } catch { }
                    _msBackfillRequestSemaphoreOwned = false;
                }
            }
        }

        private DateTime GetSessionStartTimeForSwingSeed(DateTime referenceTime)
        {
            if (TryGetSessionTimesForSwingSeed(out var start, out _))
            {
                var sessionStart = referenceTime.Date.Add(start);
                if (referenceTime.TimeOfDay < start)
                    sessionStart = sessionStart.AddDays(-1);
                return sessionStart;
            }

            if (CurrentBar < 0)
                return referenceTime;

            int startBar;
            try
            {
                startBar = FindCurrentSessionStartBarForSwingSeed();
            }
            catch (ArgumentOutOfRangeException)
            {
                return referenceTime;
            }
            if (startBar < 0)
                return referenceTime;
            if (startBar > CurrentBar)
                startBar = Math.Max(0, CurrentBar);

            ATAS.Indicators.IndicatorCandle candle;
            try
            {
                candle = GetCandle(startBar);
            }
            catch (ArgumentOutOfRangeException)
            {
                return referenceTime;
            }

            return candle != null ? candle.Time : referenceTime;
        }

        private int FindCurrentSessionStartBarForSwingSeed()
        {
            var last = Math.Max(0, CurrentBar - 1);
            for (int i = last; i >= 0; i--)
            {
                bool isNew;
                try
                {
                    isNew = IsNewSession(i);
                }
                catch (ArgumentOutOfRangeException)
                {
                    continue;
                }

                if (isNew)
                    return i;
            }

            return 0;
        }

        private int FindSessionStartBarByTime(int endBar, DateTime sessionStartTime)
        {
            int startBar = 0;
            for (int i = Math.Max(0, endBar); i >= 0; i--)
            {
                var candle = GetCandle(i);
                if (candle == null)
                    continue;

                if (candle.Time < sessionStartTime)
                {
                    startBar = Math.Min(endBar, i + 1);
                    break;
                }

                startBar = 0;
            }

            return startBar;
        }

        private bool TryGetSessionTimesForSwingSeed(out TimeSpan start, out TimeSpan end)
        {
            start = default;
            end = default;

            if (TryGetWorkingTimeForSwingSeed(out start, out end))
                return true;

            if (TryGetTradingOptionsTimesForSwingSeed(out start, out end))
                return true;

            return false;
        }

        private void TryAcquireMarketStructureLeadership()
        {
            if (_msLeaderElectionAttempted)
                return;
            _msLeaderElectionAttempted = true;

            if (_msLeaderMutex == null)
            {
                _msIsLeaderInstance = true;
                //this.LogWarn($"[Tick900Backfill:{_msInstanceId}] LeaderMutex not initialized -> fallback leader=true.");
                return;
            }

            try
            {
                _msIsLeaderInstance = _msLeaderMutex.WaitOne(0);
                if (!_msIsLeaderInstance)
                {
                    // If a stale instance keeps the mutex, allow the newest generation to proceed.
                    // This prevents the visible/current chart instance from becoming permanently passive.
                    var latest = _msGeneration == Volatile.Read(ref _msGlobalGeneration);
                    if (latest)
                        _msIsLeaderInstance = true;
                }

                if (_msIsLeaderInstance && !_msZonesSnapshotClearedByLeader)
                {
                    _msZonesSnapshotClearedByLeader = true;
                    try
                    {
                        ClearZonesSnapshot();
                    }
                    catch { }
                }

                //this.LogInfo($"[Tick900Backfill:{_msInstanceId}] LeaderMutex acquire in OnCalculate: isLeader={_msIsLeaderInstance}");
            }
            catch (Exception ex)
            {
                _msIsLeaderInstance = true;
                //this.LogWarn($"[Tick900Backfill:{_msInstanceId}] LeaderMutex WaitOne failed -> fallback leader=true. {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void WriteDecimal(BinaryWriter bw, decimal v)
        {
            var bits = decimal.GetBits(v);
            bw.Write(bits[0]);
            bw.Write(bits[1]);
            bw.Write(bits[2]);
            bw.Write(bits[3]);
        }

        private static decimal ReadDecimal(BinaryReader br)
        {
            int lo = br.ReadInt32();
            int mid = br.ReadInt32();
            int hi = br.ReadInt32();
            int flags = br.ReadInt32();
            return new decimal(new int[] { lo, mid, hi, flags });
        }

        private void RecordSyntheticTick900ClosedCandle(TickCandle c)
        {
            if (c == null)
                return;
            try
            {
                // OHLC invariants: Open/Close must be within [Low,High] and Low<=High.
                // If violated, the aggregation logic is wrong or the input price stream is inconsistent.
                try
                {
                    if (!_msTick900OhlcInvariantDiagLogged)
                    {
                        bool bad = false;
                        if (c.High < c.Low) bad = true;
                        if (c.Open < c.Low || c.Open > c.High) bad = true;
                        if (c.Close < c.Low || c.Close > c.High) bad = true;

                        if (bad)
                        {
                            _msTick900OhlcInvariantDiagLogged = true;
                            //this.LogWarn($"[Tick900Backfill:{_msInstanceId}] Synthetic Tick900 OHLC invariant violation: T={c.Time:O} LT={c.LastTime:O} O={c.Open} H={c.High} L={c.Low} C={c.Close} TC={c.TradeCount}");
                        }
                    }
                }
                catch { }

                _msTick900ClosedCandles.Add(c);
            }
            catch { }
        }

        private DateTime NormalizeToChartTime(DateTime t)
        {
            try
            {
                if (_msChartTimeKind == null)
                {
                    var c0 = GetCandle(0);
                    if (c0 != null)
                        _msChartTimeKind = c0.Time.Kind;
                    else
                        _msChartTimeKind = DateTimeKind.Unspecified;
                }

                var chartKind = _msChartTimeKind.Value;

                if (chartKind == DateTimeKind.Local)
                {
                    if (t.Kind == DateTimeKind.Utc)
                        return t.ToLocalTime();
                    if (t.Kind == DateTimeKind.Unspecified)
                        return DateTime.SpecifyKind(t, DateTimeKind.Local);
                    return t;
                }

                if (chartKind == DateTimeKind.Utc)
                {
                    if (t.Kind == DateTimeKind.Local)
                        return t.ToUniversalTime();
                    if (t.Kind == DateTimeKind.Unspecified)
                        return DateTime.SpecifyKind(t, DateTimeKind.Utc);
                    return t;
                }

                // ChartKind == Unspecified: keep values as-is, but strip kind for consistent comparisons
                if (t.Kind == DateTimeKind.Unspecified)
                    return t;
                return DateTime.SpecifyKind(t, DateTimeKind.Unspecified);
            }
            catch
            {
                return t;
            }
        }

        private int GetXByBarSafe(int bar)
        {
            try
            {
                if (_msGetXByBar == null && _msGetXByBarMi == null)
                {
                    var t = ChartInfo?.GetType();
                    if (t != null)
                    {
                        // Try multiple likely signatures.
                        var mi = t.GetMethod("GetXByBar", new[] { typeof(int) })
                                 ?? t.GetMethod("GetXByBar", new[] { typeof(int), typeof(bool) })
                                 ?? t.GetMethod("GetXByCandle", new[] { typeof(int) })
                                 ?? t.GetMethod("GetXByCandle", new[] { typeof(int), typeof(bool) })
                                 ?? t.GetMethod("GetX", new[] { typeof(int) });

                        if (mi != null)
                        {
                            _msGetXByBarMi = mi;
                            _msGetXByBarMiParamCount = mi.GetParameters()?.Length ?? 0;

                            if (_msGetXByBarMiParamCount == 1)
                            {
                                try { _msGetXByBar = (Func<int, int>)Delegate.CreateDelegate(typeof(Func<int, int>), ChartInfo, mi); }
                                catch { _msGetXByBar = null; }
                            }
                        }
                    }
                }

                if (_msGetXByBar != null)
                    return _msGetXByBar(bar);

                if (_msGetXByBarMi != null)
                {
                    object? res = null;
                    if (_msGetXByBarMiParamCount == 1)
                        res = _msGetXByBarMi.Invoke(ChartInfo, new object[] { bar });
                    else if (_msGetXByBarMiParamCount == 2)
                        res = _msGetXByBarMi.Invoke(ChartInfo, new object[] { bar, false });

                    if (res is int xi)
                        return xi;
                    if (res != null)
                    {
                        try { return Convert.ToInt32(res); } catch { }
                    }
                }
            }
            catch { }

            // Fallback: approximate using bar distance to the right edge and an estimated bar width.
            try
            {
                int w = ChartInfo?.PriceChartContainer?.Region.Width ?? 0;
                int last = CurrentBar;
                int barsFromRight = Math.Max(0, last - bar);

                int step = 6;
                try
                {
                    var pc = ChartInfo?.PriceChartContainer;
                    var pt = pc?.GetType();
                    if (pt != null)
                    {
                        var pBarWidth = pt.GetProperty("BarWidth") ?? pt.GetProperty("CandleWidth") ?? pt.GetProperty("Step");
                        var v = pBarWidth?.GetValue(pc);
                        if (v != null)
                            step = Math.Max(2, Convert.ToInt32(v));
                    }
                }
                catch { }

                if (!_msXMapDiagLogged)
                {
                    _msXMapDiagLogged = true;
                    try { this.LogWarn($"[Tick900Backfill:{_msInstanceId}] GetXByBar not available -> using fallback X mapping (step={step})."); } catch { }
                }

                int xRight = Math.Max(0, w - 10);
                return Math.Max(0, xRight - (barsFromRight * step));
            }
            catch
            {
                return 0;
            }
        }

        private void ClearSyntheticTick900Debug()
        {
            try { _msTick900ClosedCandles.Clear(); } catch { }
        }

        private bool IsNearRealTime(DateTime chartEndTime)
        {
            try
            {
                if (chartEndTime == default)
                    return false;

                var endUtc = chartEndTime.Kind == DateTimeKind.Utc
                    ? chartEndTime
                    : DateTime.SpecifyKind(chartEndTime, DateTimeKind.Local).ToUniversalTime();

                var nowUtc = DateTime.UtcNow;
                var diff = nowUtc - endUtc;
                if (diff < TimeSpan.Zero)
                    diff = -diff;
                return diff < TimeSpan.FromMinutes(2);
            }
            catch
            {
                return false;
            }
        }

        private int FindChartBarByTime(DateTime time)
        {
            try
            {
                time = NormalizeToChartTime(time);
                int last = CurrentBar;
                if (last < 0)
                    return -1;

                // 1) Binary search by candle start time to get close to the right area.
                int lo = 0;
                int hi = last;
                while (lo <= hi)
                {
                    int mid = lo + ((hi - lo) / 2);
                    var c = GetCandle(mid);
                    if (c == null)
                    {
                        hi = mid - 1;
                        continue;
                    }

                    var t0 = NormalizeToChartTime(c.Time);
                    var t1 = NormalizeToChartTime(c.LastTime != default ? c.LastTime : c.Time);
                    var start = t0 <= t1 ? t0 : t1;
                    if (start < time)
                        lo = mid + 1;
                    else if (start > time)
                        hi = mid - 1;
                    else
                    {
                        // exact start match
                        return mid;
                    }
                }

                // Candidate is hi (last candle with start <= time)
                int idx = Math.Max(0, Math.Min(last, hi));

                // 2) Try to find a candle whose interval contains 'time'.
                // We scan a small neighborhood because LastTime can overlap slightly depending on feed/build.
                int from = Math.Max(0, idx - 20);
                int to = Math.Min(last, idx + 20);

                // 2a) Prefer an interval match if available.
                for (int i = from; i <= to; i++)
                {
                    var c = GetCandle(i);
                    if (c == null)
                        continue;
                    var t0 = NormalizeToChartTime(c.Time);
                    var t1 = NormalizeToChartTime(c.LastTime != default ? c.LastTime : c.Time);
                    var start = t0 <= t1 ? t0 : t1;
                    var end = t0 <= t1 ? t1 : t0;
                    if (start <= time && time <= end)
                        return i;
                }

                // 2b) If no interval match, choose the nearest candle by start time.
                int best = idx;
                long bestDist = long.MaxValue;
                for (int i = from; i <= to; i++)
                {
                    var c = GetCandle(i);
                    if (c == null)
                        continue;
                    var start = NormalizeToChartTime(c.Time);
                    long d = Math.Abs((start - time).Ticks);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = i;
                        if (bestDist == 0)
                            break;
                    }
                }

                return best;
            }
            catch
            {
                return -1;
            }
        }

        private int FindBestChartBarMatchForSyntheticTickCandle(int initialBar, TickCandle syn)
        {
            try
            {
                if (syn == null)
                    return initialBar;
                int last = CurrentBar;
                if (initialBar < 0 || last < 0)
                    return initialBar;

                int from = Math.Max(0, initialBar - 10);
                int to = Math.Min(last, initialBar + 10);

                int bestBar = initialBar;
                decimal bestScore = decimal.MaxValue;
                decimal ts = _tickSize > 0m ? _tickSize : 0.25m;

                for (int i = from; i <= to; i++)
                {
                    var c = GetCandle(i);
                    if (c == null)
                        continue;

                    decimal score = ScoreChartCandleVsSyntheticOHLC(c, syn, ts);

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestBar = i;
                    }
                }

                return bestBar;
            }
            catch
            {
                return initialBar;
            }
        }

        private decimal ScoreChartCandleVsSyntheticOHLC(IndicatorCandle c, TickCandle syn, decimal ts)
        {
            try
            {
                if (c == null || syn == null)
                    return decimal.MaxValue;

                decimal score = 0m;
                try { score += Math.Abs((c.Open - syn.Open) / ts); } catch { }
                try { score += Math.Abs((c.Close - syn.Close) / ts); } catch { }
                try { score += Math.Abs((c.High - syn.High) / ts); } catch { }
                try { score += Math.Abs((c.Low - syn.Low) / ts); } catch { }
                return score;
            }
            catch
            {
                return decimal.MaxValue;
            }
        }

        private int FindBestChartBarMatchForSyntheticTickCandleWide(int initialBar, TickCandle syn, int window)
        {
            try
            {
                if (syn == null)
                    return initialBar;

                int last = CurrentBar;
                if (last < 0)
                    return initialBar;

                int baseBar = initialBar;
                if (baseBar < 0)
                    baseBar = last;

                int w = window > 0 ? window : 50;
                int from = Math.Max(0, baseBar - w);
                int to = Math.Min(last, baseBar + w);

                int bestBar = baseBar;
                decimal bestScore = decimal.MaxValue;
                decimal ts = _tickSize > 0m ? _tickSize : 0.25m;

                for (int i = from; i <= to; i++)
                {
                    var c = GetCandle(i);
                    if (c == null)
                        continue;
                    var score = ScoreChartCandleVsSyntheticOHLC(c, syn, ts);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestBar = i;
                        if (bestScore == 0m)
                            break;
                    }
                }

                return bestBar;
            }
            catch
            {
                return initialBar;
            }
        }

        private void WriteZonesSnapshot()
        {
            try
            {
                if (_msZonesMmf == null || _marketStructureContext == null)
                    return;

                var zones = _marketStructureContext.GetActiveZonesSnapshot();
                using var stream = _msZonesMmf.CreateViewStream(0, 0, MemoryMappedFileAccess.Write);
                using var bw = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
                bw.Write(DateTime.UtcNow.Ticks);
                int count = zones?.Count ?? 0;
                bw.Write(count);
                if (count <= 0)
                    return;

                for (int i = 0; i < zones.Count; i++)
                {
                    var z = zones[i];
                    if (z == null)
                    {
                        bw.Write(0);
                        bw.Write((byte)0);
                        bw.Write((byte)0);
                        WriteDecimal(bw, 0m);
                        WriteDecimal(bw, 0m);
                        bw.Write((byte)0);
                        bw.Write(0);
                        continue;
                    }

                    bw.Write(z.Id);
                    bw.Write((byte)z.Type);
                    bw.Write((byte)z.Status);
                    WriteDecimal(bw, z.Low);
                    WriteDecimal(bw, z.High);
                    bw.Write((byte)(z.IsMultiTouch ? 1 : 0));
                    bw.Write(z.MultiTouchScore);
                    bw.Write((byte)(z.IsConfirmed ? 1 : 0));
                }
            }
            catch { }
        }

        private void NormalizeZonesAfterBackfill()
        {
            try
            {
                var ctx = _marketStructureContext;
                if (ctx == null)
                    return;

                var zones = ctx.ActiveZones;
                if (zones == null || zones.Count == 0)
                    return;

                // Backfill soll Marktstruktur aufbauen, aber keine Zonen "verbrauchen".
                // Wenn während des historischen Builds Trigger-Bedingungen erfüllt werden, würden Zonen sonst
                // als Triggered/Used enden und wegen Render-Filter nicht sichtbar sein.
                foreach (var z in zones)
                {
                    if (z == null)
                        continue;
                    if (z.Status == MarketStructureContext.ZoneStatus.Triggered || z.Status == MarketStructureContext.ZoneStatus.Used)
                    {
                        z.Status = MarketStructureContext.ZoneStatus.Ready;
                        z.TouchCount = 0;
                        z.LastTouchedBar = 0;
                    }
                }
            }
            catch { }
        }

        private void ClearZonesSnapshot()
        {
            try
            {
                if (_msZonesMmf == null)
                    return;

                using var stream = _msZonesMmf.CreateViewStream(0, 0, MemoryMappedFileAccess.Write);
                using var bw = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
                bw.Write(DateTime.UtcNow.Ticks);
                bw.Write(0);
            }
            catch { }
        }

        private List<(int Id, MarketStructureContext.ZoneType Type, MarketStructureContext.ZoneStatus Status, decimal Low, decimal High, bool IsMultiTouch, int MultiTouchScore, bool IsConfirmed)> ReadZonesSnapshot()
        {
            var res = new List<(int, MarketStructureContext.ZoneType, MarketStructureContext.ZoneStatus, decimal, decimal, bool, int, bool)>(16);
            try
            {
                if (_msZonesMmf == null)
                    return res;

                using var stream = _msZonesMmf.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
                using var br = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
                _ = br.ReadInt64();
                int count = br.ReadInt32();
                if (count <= 0)
                    return res;

                for (int i = 0; i < count; i++)
                {
                    int id = br.ReadInt32();
                    var type = (MarketStructureContext.ZoneType)br.ReadByte();
                    var status = (MarketStructureContext.ZoneStatus)br.ReadByte();
                    var low = ReadDecimal(br);
                    var high = ReadDecimal(br);
                    bool isMultiTouch = false;
                    int multiTouchScore = 0;
                    bool isConfirmed = true;
                    try
                    {
                        isMultiTouch = br.ReadByte() != 0;
                        multiTouchScore = br.ReadInt32();
                        isConfirmed = br.ReadByte() != 0;
                    }
                    catch
                    {
                        isMultiTouch = false;
                        multiTouchScore = 0;
                        isConfirmed = true;
                    }
                    if (id <= 0)
                        continue;
                    res.Add((id, type, status, low, high, isMultiTouch, multiTouchScore, isConfirmed));
                }
            }
            catch { }
            return res;
        }

        private bool TryGetWorkingTimeForSwingSeed(out TimeSpan start, out TimeSpan end)
        {
            start = default;
            end = default;

            var security = GetPropertyValueForSwingSeed(this, "Security");
            if (security is null)
                return false;

            var workingTime = GetPropertyValueForSwingSeed(security, "WorkingTime");
            if (workingTime is null)
                return false;

            if (!TryReadTimeSpanForSwingSeed(workingTime, "StartTime", out start))
                return false;
            if (!TryReadTimeSpanForSwingSeed(workingTime, "EndTime", out end))
                end = default;

            return true;
        }

        private bool TryGetTradingOptionsTimesForSwingSeed(out TimeSpan start, out TimeSpan end)
        {
            start = default;
            end = default;

            var tradingOptions = GetPropertyValueForSwingSeed(this, "TradingOptions")
                ?? GetPropertyValueForSwingSeed(GetPropertyValueForSwingSeed(this, "ChartInfo"), "TradingOptions");

            if (tradingOptions is null)
                return false;

            if (!TryReadTimeSpanForSwingSeed(tradingOptions, "SessionBeginTime", out start))
                return false;
            if (!TryReadTimeSpanForSwingSeed(tradingOptions, "SessionEndTime", out end))
                end = default;

            return true;
        }

        private static object? GetPropertyValueForSwingSeed(object? obj, string propertyName)
        {
            if (obj is null)
                return null;

            var t = obj.GetType();
            var pi = t.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return pi?.GetValue(obj);
        }

        private static bool TryReadTimeSpanForSwingSeed(object obj, string propertyName, out TimeSpan value)
        {
            value = default;
            if (obj is null)
                return false;

            var raw = GetPropertyValueForSwingSeed(obj, propertyName);
            if (raw is null)
                return false;

            if (raw is TimeSpan ts)
            {
                value = ts;
                return true;
            }

            if (raw is DateTime dt)
            {
                value = dt.TimeOfDay;
                return true;
            }

            return false;
        }

        private void SeedDailyProfileFromDayStart(int currentBar)
        {
            _dailyProfileClosedHist.Clear();
            _dailyProfileDevHist.Clear();
            _dailyProfileCombinedHist.Clear();

            if (currentBar <= 0)
            {
                _dailyProfileDevBar = currentBar;
                RebuildDailyProfileBarHist(currentBar, _dailyProfileDevHist);
                CombineDailyProfileHists();
                return;
            }

            // Session-Start (nicht nur Date), damit es zum MarketProfile-Sessiontemplate passt
            int startBar = 0;
            for (int i = currentBar; i >= 0; i--)
            {
                if (IsNewSession(i))
                {
                    startBar = i;
                    break;
                }
            }

            for (int i = startBar; i < currentBar; i++)
            {
                foreach (var lvl in EnumerateClusterLevels(i))
                {
                    if (lvl.TotalVol <= 0m) continue;
                    if (_dailyProfileClosedHist.TryGetValue(lvl.Price, out var v)) _dailyProfileClosedHist[lvl.Price] = v + lvl.TotalVol;
                    else _dailyProfileClosedHist[lvl.Price] = lvl.TotalVol;
                }
            }

            _dailyProfileDevBar = currentBar;
            RebuildDailyProfileBarHist(currentBar, _dailyProfileDevHist);
            CombineDailyProfileHists();
        }


        private readonly List<TrackedLevel> _untouchedLevels = new List<TrackedLevel>();

        // NEU: Ein Dictionary zum Verwalten der gezeichneten Linien f?r signifikante Levels
        private readonly Dictionary<string, HorizontalLine> _significantLines = new Dictionary<string, HorizontalLine>();


        private DateTime _lastProcessedDay = DateTime.MinValue;




        // Diese Instanz liefert uns die statischen Werte des Vortages.
        private readonly DynamicLevels _dailyLevels = new DynamicLevels();
        private readonly DailyLines _dailyLines = new DailyLines();
        private readonly MarketAnalysis.PublicActiveVolume _publicActiveVolume = new MarketAnalysis.PublicActiveVolume();


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

        private int _pdOpenZoneId;
        private int _pdCloseZoneId;
        private DateTime _pdZoneDay = DateTime.MinValue;

        private int _pdHighZonePendingTypeCloses;
        private int _pdLowZonePendingTypeCloses;

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
            public decimal HVNStrengthVsPOCInside { get; set; } = 0.50m;
            public decimal HVNStrengthVsPOCOutside { get; set; } = 0.50m;
            public decimal HVNStrengthVsMedianInside { get; set; } = 1.20m;
            public decimal HVNStrengthVsMedianOutside { get; set; } = 1.20m;
            public decimal MinProminenceVsMedianInside { get; set; } = 0.15m;
            public decimal MinProminenceVsMedianOutside { get; set; } = 0.15m;
            public bool ForceAllHVNsStrong { get; set; } = false;
            public bool ForceAllHVNsWeak { get; set; } = false;
            public int MinBlockerDistanceTicksVsRisk { get; set; } = 1; // Blocker muss weiter entfernt sein als RiskTicks * Faktor (1 = gleich Risk)
            public bool UseHvnZonesForBlocking { get; set; } = true;
            public bool UseLvnPathForBlocking { get; set; } = true;
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

        private sealed class TrackedZone
        {
            public int Id;
            public decimal Start;
            public decimal End;
            public decimal Score;
            public int Missing;
            public int HitCount;
            public bool Active;
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

        private int[] _smoothWeights;
        private int _lastSmoothSpan = -1;
        private int _smoothRadius = 0;

        int SmoothingTicks = 1;     // Gl?ttung
        int TopNPeaks = 4;          // Anzahl HVNs/LVNs

        private MicroComposite _currentMC;
        private MicroComposite _prevMC;

        // =====================
        // Daily Profile (nur WegFrei, unabhängig vom MicroComposite)
        // =====================
        private DateTime _dailyProfileDate = DateTime.MinValue;
        private readonly SortedDictionary<decimal, decimal> _dailyProfileClosedHist = new();
        private readonly SortedDictionary<decimal, decimal> _dailyProfileDevHist = new();
        private readonly SortedDictionary<decimal, decimal> _dailyProfileCombinedHist = new();
        private int _dailyProfileDevBar = -1;
        private int _lastDailyProfileRecalcBar = -1;
        private decimal _lastDailyProfileRecalcPOC = 0m;
        private decimal _lastDailyProfileRecalcVAH = 0m;
        private decimal _lastDailyProfileRecalcVAL = 0m;
        private long _lastDailyProfileActiveVolSignature = 0;
        private DateTime _lastDailyProfileActiveVolRecalcTime = DateTime.MinValue;
        private int _lastDailyProfileDebugBar = -1;
        private bool _dailyProfileSeededForDate = false;
        private bool _prevDailyProfileEnabledFlag = false;

        private int _dailyHvnNextId = 1;
        private readonly List<TrackedZone> _dailyHvnTracked = new();
        private List<int> _dailyHvnLastOutputIds = new();

        private decimal _dailyGridMinPrice = 0m;
        private decimal _dailyGridMaxPrice = 0m;
        private decimal[]? _dailySmoothEma;
        private bool[]? _dailyHvnLevelState;

        private DateTime _dailyHvnLastStatLogUtc = DateTime.MinValue;

        private MicroComposite? _dailyProfileForPath;
        private MicroComposite? _prevDailyProfileForPath;
        private MicroComposite? _dailyProfileForVisual;

        private void EnsureSmoothWeights(int span)
        {
            if (span <= 1)
            {
                _lastSmoothSpan = span;
                _smoothWeights = null;
                _smoothRadius = 0;
                return;
            }

            if (span == _lastSmoothSpan && _smoothWeights != null)
                return;

            _lastSmoothSpan = span;
            var w = new List<int>();
            for (int i = 1; i <= span; i++) w.Add(i);
            for (int i = span - 1; i >= 1; i--) w.Add(i);
            _smoothWeights = w.ToArray();
            _smoothRadius = _smoothWeights.Length / 2;
        }

        private decimal[] SmoothTriangularCached(decimal[] y, int span)
        {
            if (span <= 1 || y == null || y.Length == 0)
                return (decimal[])y.Clone();

            EnsureSmoothWeights(span);
            var weights = _smoothWeights;
            int radius = _smoothRadius;
            if (weights == null || weights.Length == 0 || radius <= 0)
                return (decimal[])y.Clone();

            var ys = new decimal[y.Length];
            for (int i = 0; i < y.Length; i++)
            {
                decimal s = 0m;
                int wsum = 0;
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

        private List<decimal> BuildPriceAxis(decimal minP, decimal maxP, decimal tick)
        {
            var result = new List<decimal>();
            for (decimal p = minP; p <= maxP; p += tick) result.Add(p);
            return result;
        }

        private void RebuildDailyProfileBarHist(int barIndex, SortedDictionary<decimal, decimal> target)
        {
            target.Clear();
            foreach (var lvl in EnumerateClusterLevels(barIndex))
            {
                if (lvl.TotalVol <= 0m) continue;
                if (target.TryGetValue(lvl.Price, out var v)) target[lvl.Price] = v + lvl.TotalVol;
                else target[lvl.Price] = lvl.TotalVol;
            }
        }

        private void CombineDailyProfileHists()
        {
            _dailyProfileCombinedHist.Clear();
            foreach (var kv in _dailyProfileClosedHist) _dailyProfileCombinedHist[kv.Key] = kv.Value;
            foreach (var kv in _dailyProfileDevHist)
            {
                if (_dailyProfileCombinedHist.TryGetValue(kv.Key, out var v)) _dailyProfileCombinedHist[kv.Key] = v + kv.Value;
                else _dailyProfileCombinedHist[kv.Key] = kv.Value;
            }
        }

        private static List<(int L, int R)> BuildSegments(decimal[] smooth, int lo, int hi, Func<decimal, bool> predicate)
        {
            var segs = new List<(int L, int R)>();
            int i = Math.Max(0, lo);
            int end = Math.Min(smooth.Length - 1, hi);
            while (i <= end)
            {
                while (i <= end && !predicate(smooth[i])) i++;
                if (i > end) break;
                int L = i;
                while (i <= end && predicate(smooth[i])) i++;
                int R = i - 1;
                segs.Add((L, R));
            }
            return segs;
        }

        private List<(decimal Start, decimal End)> MergeSegmentsToZones(List<(int L, int R)> segs, List<decimal> prices, int gapTicks)
        {
            if (segs == null || segs.Count == 0)
                return new List<(decimal Start, decimal End)>();

            segs.Sort((a, b) => a.L.CompareTo(b.L));
            var merged = new List<(int L, int R)>();
            var cur = segs[0];
            for (int i = 1; i < segs.Count; i++)
            {
                var nxt = segs[i];
                if (nxt.L <= cur.R + Math.Max(0, gapTicks) + 1)
                    cur = (cur.L, Math.Max(cur.R, nxt.R));
                else
                {
                    merged.Add(cur);
                    cur = nxt;
                }
            }
            merged.Add(cur);

            var zones = new List<(decimal Start, decimal End)>(merged.Count);
            foreach (var m in merged)
            {
                if (m.L < 0 || m.R >= prices.Count || m.L > m.R)
                    continue;
                zones.Add((prices[m.L], prices[m.R]));
            }
            return zones;
        }

        private static List<(int L, int R)> MergeSegments(List<(int L, int R)> segs, int gapTicks)
        {
            if (segs == null || segs.Count == 0)
                return new List<(int L, int R)>();

            segs.Sort((a, b) => a.L.CompareTo(b.L));
            var merged = new List<(int L, int R)>();
            var cur = segs[0];
            for (int i = 1; i < segs.Count; i++)
            {
                var nxt = segs[i];
                if (nxt.L <= cur.R + Math.Max(0, gapTicks) + 1)
                    cur = (cur.L, Math.Max(cur.R, nxt.R));
                else
                {
                    merged.Add(cur);
                    cur = nxt;
                }
            }
            merged.Add(cur);
            return merged;
        }

        private static List<(int L, int R)> CapSegmentsAroundExtremum(List<(int L, int R)> segs, decimal[] smooth, int capTicks, bool useMax)
        {
            if (segs == null || segs.Count == 0)
                return segs ?? new List<(int L, int R)>();
            if (smooth == null || smooth.Length == 0)
                return segs;
            if (capTicks <= 0)
                return segs;

            var res = new List<(int L, int R)>(segs.Count);
            foreach (var s in segs)
            {
                int L = Math.Max(0, s.L);
                int R = Math.Min(smooth.Length - 1, s.R);
                if (L > R) continue;

                int w = R - L + 1;
                if (w <= capTicks)
                {
                    res.Add((L, R));
                    continue;
                }

                int extIdx = L;
                decimal extVal = smooth[L];
                for (int i = L + 1; i <= R; i++)
                {
                    var v = smooth[i];
                    if (useMax)
                    {
                        if (v > extVal) { extVal = v; extIdx = i; }
                    }
                    else
                    {
                        if (v < extVal) { extVal = v; extIdx = i; }
                    }
                }

                int left = extIdx - (capTicks - 1) / 2;
                int right = left + capTicks - 1;
                if (left < L) { left = L; right = left + capTicks - 1; }
                if (right > R) { right = R; left = right - capTicks + 1; }
                if (left < L) left = L;
                if (right > R) right = R;

                res.Add((left, right));
            }
            return res;
        }

        private static List<(decimal Start, decimal End)> ClampZonesToRange(List<(decimal Start, decimal End)> zones, decimal lo, decimal hi)
        {
            if (zones == null || zones.Count == 0)
                return zones ?? new List<(decimal Start, decimal End)>();
            if (lo <= 0m || hi <= 0m)
                return zones;
            if (hi < lo) { var t = lo; lo = hi; hi = t; }

            var res = new List<(decimal Start, decimal End)>(zones.Count);
            foreach (var z in zones)
            {
                var a = z.Start;
                var b = z.End;
                if (a > b) { var tmp = a; a = b; b = tmp; }
                var s = Math.Max(a, lo);
                var e = Math.Min(b, hi);
                if (e >= s)
                    res.Add((s, e));
            }
            return res;
        }

        private static List<(decimal Start, decimal End)> CapZoneWidths(List<(decimal Start, decimal End)> zones, decimal maxWidth)
        {
            if (zones == null || zones.Count == 0)
                return zones ?? new List<(decimal Start, decimal End)>();
            if (maxWidth <= 0m)
                return zones;

            var res = new List<(decimal Start, decimal End)>(zones.Count);
            foreach (var z in zones)
            {
                var a = z.Start;
                var b = z.End;
                if (a > b) { var t = a; a = b; b = t; }
                var w = b - a;
                if (w <= maxWidth)
                {
                    res.Add((a, b));
                    continue;
                }
                var mid = (a + b) / 2m;
                res.Add((mid - maxWidth / 2m, mid + maxWidth / 2m));
            }
            return res;
        }

        private void RecalcDailyProfileForPath(decimal currentPOC, decimal currentVAH, decimal currentVAL)
        {
            if (_tickSize <= 0m || _dailyProfileCombinedHist.Count == 0)
            {
                // Histogramm kann kurzfristig leer sein (z.B. Snapshot-Latenz). Letztes gültiges Profil behalten.
                return;
            }

            _prevDailyProfileForPath = _dailyProfileForPath;

            // Daily Profile (Plateau/Histogramm-basiert): Zonen sind kontinuierliche dicke Bereiche
            // und werden sowohl für WegFrei/Entry/TP als auch für die Visualisierung genutzt.
            var minP = _dailyProfileCombinedHist.Keys.First();
            var maxP = _dailyProfileCombinedHist.Keys.Last();
            var prices = BuildPriceAxis(minP, maxP, _tickSize);
            if (prices.Count < 3)
            {
                _dailyProfileForPath = null;
                return;
            }

            var vols = new decimal[prices.Count];
            decimal totalVol = 0m;
            for (int i = 0; i < prices.Count; i++)
            {
                vols[i] = _dailyProfileCombinedHist.TryGetValue(prices[i], out var v) ? v : 0m;
                totalVol += vols[i];
            }

            var smooth = SmoothTriangularCached(vols, Math.Max(1, DailySmoothTicks));
            decimal maxS = 0m;
            for (int i = 0; i < smooth.Length; i++)
                if (smooth[i] > maxS) maxS = smooth[i];

            // -------- HVN Engine (stabiler Tick-Grid + EMA + Hysterese + Persistenz) --------
            // Tick-Grid stabilisieren: EMA/State-Arrays müssen bei Range-Erweiterung verschoben werden.
            void EnsureDailyGrid(decimal newMin, decimal newMax)
            {
                newMin = RoundToTick(newMin);
                newMax = RoundToTick(newMax);
                if (_dailyGridMinPrice == 0m && _dailyGridMaxPrice == 0m)
                {
                    _dailyGridMinPrice = newMin;
                    _dailyGridMaxPrice = newMax;
                    return;
                }

                if (newMin > _dailyGridMinPrice) newMin = _dailyGridMinPrice;
                if (newMax < _dailyGridMaxPrice) newMax = _dailyGridMaxPrice;

                if (newMin == _dailyGridMinPrice && newMax == _dailyGridMaxPrice)
                    return;

                int oldLen = (int)Math.Round((_dailyGridMaxPrice - _dailyGridMinPrice) / _tickSize) + 1;
                int newLen = (int)Math.Round((newMax - newMin) / _tickSize) + 1;
                int offsetTicks = (int)Math.Round((_dailyGridMinPrice - newMin) / _tickSize);

                var newEma = new decimal[newLen];
                var newState = new bool[newLen];

                if (_dailySmoothEma != null && _dailySmoothEma.Length == oldLen)
                {
                    for (int i = 0; i < oldLen; i++)
                    {
                        int ni = i + offsetTicks;
                        if (ni < 0 || ni >= newLen) continue;
                        newEma[ni] = _dailySmoothEma[i];
                    }
                }

                if (_dailyHvnLevelState != null && _dailyHvnLevelState.Length == oldLen)
                {
                    for (int i = 0; i < oldLen; i++)
                    {
                        int ni = i + offsetTicks;
                        if (ni < 0 || ni >= newLen) continue;
                        newState[ni] = _dailyHvnLevelState[i];
                    }
                }

                _dailySmoothEma = newEma;
                _dailyHvnLevelState = newState;
                _dailyGridMinPrice = newMin;
                _dailyGridMaxPrice = newMax;
            }

            EnsureDailyGrid(prices.First(), prices.Last());
            int gridLen = prices.Count;
            if (_dailySmoothEma == null || _dailySmoothEma.Length != gridLen)
                _dailySmoothEma = new decimal[gridLen];
            if (_dailyHvnLevelState == null || _dailyHvnLevelState.Length != gridLen)
                _dailyHvnLevelState = new bool[gridLen];

            // EMA Update (recalc-basiert)
            decimal emaAlpha = 0.35m;
            for (int i = 0; i < gridLen; i++)
            {
                var prev = _dailySmoothEma[i];
                var cur = smooth[i];
                _dailySmoothEma[i] = (emaAlpha * cur) + ((1m - emaAlpha) * prev);
            }

            var mc = new MicroComposite
            {
                POC = currentPOC,
                VAH = currentVAH,
                VAL = currentVAL,
                TotalVol = totalVol,
                LevelVols = new SortedDictionary<decimal, decimal>(_dailyProfileCombinedHist)
            };

            // POCVol aus Histogramm ableiten (für HVN-Stärke-Filter)
            if (mc.LevelVols != null)
            {
                if (mc.LevelVols.TryGetValue(currentPOC, out var pv))
                    mc.POCVol = pv;
                else
                {
                    // Fallback: auf Tick-Grid runden
                    var pocRounded = RoundToTick(currentPOC);
                    if (mc.LevelVols.TryGetValue(pocRounded, out var pv2))
                        mc.POCVol = pv2;
                }
            }

            if (maxS <= 0m)
            {
                _dailyProfileForPath = mc;
                return;
            }

            List<(decimal Start, decimal End)> hvScored = new();

            int scanLo = 0;
            int scanHi = prices.Count - 1;
            int idxVALGlobal = -1;
            int idxVAHGlobal = -1;
            if (currentVAL > 0m && currentVAH > 0m)
            {
                idxVALGlobal = prices.FindIndex(p => p == currentVAL);
                idxVAHGlobal = prices.FindIndex(p => p == currentVAH);
                if (idxVALGlobal >= 0 && idxVAHGlobal >= 0)
                {
                    if (!DailyAllowZonesOutsideVA)
                    {
                        scanLo = Math.Min(idxVALGlobal, idxVAHGlobal);
                        scanHi = Math.Max(idxVALGlobal, idxVAHGlobal);
                    }
                }
            }

            // Kontextfenster (nur für Output/Ranking, NICHT für die Detektion):
            // Entry/TP soll HVNs in der Nähe des aktuellen Preises priorisieren,
            // aber die Plateau-Erkennung soll weiterhin global laufen (wie vorher).
            int hvScanLo = scanLo;
            int hvScanHi = scanHi;
            int hvLocalWindowTicks = 30;
            decimal hvCurrP = 0m;
            if (_tickSize > 0m && prices.Count > 0 && CurrentBar >= 0)
            {
                hvCurrP = RoundToTick(GetLastPrice());
                int idxCur = prices.FindIndex(p => p == hvCurrP);
                if (idxCur < 0)
                {
                    // Fallback: nächster Tick
                    decimal bestDist = decimal.MaxValue;
                    int best = -1;
                    for (int i = 0; i < prices.Count; i++)
                    {
                        var d = Math.Abs(prices[i] - hvCurrP);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            best = i;
                        }
                    }
                    idxCur = best;
                }

                if (idxCur >= 0)
                {
                    hvScanLo = Math.Max(scanLo, idxCur - hvLocalWindowTicks);
                    hvScanHi = Math.Min(scanHi, idxCur + hvLocalWindowTicks);
                }
            }

            int minTicks = Math.Max(1, DailyMinZoneTicks);
            int gap = Math.Max(0, DailyGapTicks);

            // Schwellen (Anteil vom Max): erzeugt dicke Plateaus
            decimal lvThr = Math.Max(0.01m, Math.Min(0.95m, DailyLVNPlateauFrac)) * maxS;

            // HVN: adaptiv senken, wenn zu wenige Segmente gefunden werden.
            // WICHTIG: Wenn Zonen außerhalb VA erlaubt sind, darf die HVN-Schwelle nicht am globalen Max hängen,
            // sonst werden Tails fast nie als HVN erkannt. Daher: regionales Max pro Range.
            decimal hvFracStart = Math.Max(0.10m, Math.Min(0.99m, DailyHVNPlateauFrac));
            decimal hvFracMin = DailyAllowZonesOutsideVA ? 0.30m : hvFracStart;

            int hvLocalWindowHalf = 2;
            decimal hvSetFactorOutVA = 1.55m;
            decimal hvSetFactorInVA = 1.25m;
            decimal hvClearFactorOutVA = 1.25m;
            decimal hvClearFactorInVA = 1.10m;
            decimal minLevelSharePrev = 0.00015m;
            decimal minLevelShareOff = 0.00008m;
            decimal minZoneShareSet = 0.0020m;
            decimal minZoneShareOff = 0.0010m;
            decimal minZoneShareSetOutVA = minZoneShareSet * 0.70m;
            decimal minZoneShareOffOutVA = minZoneShareOff * 0.60m;
            int minPlateauWidthTicks = Math.Max(1, DailyMinZoneTicks);
            int persistSetRecalcs = 2;
            int persistClearRecalcs = 4;
            int maxTrackedZones = 20;

            int lvLocalWindowHalf = 2;
            decimal lvLocalFactor = 2.0m;

            decimal MaxInRange(int lo, int hi)
            {
                lo = Math.Max(0, lo);
                hi = Math.Min(prices.Count - 1, hi);
                if (lo > hi) return 0m;
                decimal m = 0m;
                for (int i = lo; i <= hi; i++) if (_dailySmoothEma![i] > m) m = _dailySmoothEma[i];
                return m;
            }

            decimal LocalMedian(decimal[] arr, int i, int halfWindow)
            {
                int n = arr.Length;
                int lo = Math.Max(0, i - Math.Max(0, halfWindow));
                int hi = Math.Min(n - 1, i + Math.Max(0, halfWindow));
                int cnt = hi - lo + 1;
                if (cnt <= 0) return 0m;

                var tmp = new List<decimal>(cnt);
                for (int k = lo; k <= hi; k++) tmp.Add(arr[k]);
                tmp.Sort();
                int m = tmp.Count / 2;
                if (tmp.Count % 2 == 1) return tmp[m];
                return (tmp[m - 1] + tmp[m]) / 2m;
            }

            List<(int L, int R)> BuildSegmentsFromMask(bool[] mask, int lo, int hi)
            {
                var segs = new List<(int L, int R)>();
                int i = Math.Max(0, lo);
                int end = Math.Min(mask.Length - 1, hi);
                while (i <= end)
                {
                    while (i <= end && !mask[i]) i++;
                    if (i > end) break;
                    int L = i;
                    while (i <= end && mask[i]) i++;
                    int R = i - 1;
                    segs.Add((L, R));
                }
                return segs;
            }

            decimal ZoneCenter((decimal Start, decimal End) z) => (z.Start + z.End) / 2m;
            decimal ZS((decimal Start, decimal End) z) => Math.Min(z.Start, z.End);
            decimal ZE((decimal Start, decimal End) z) => Math.Max(z.Start, z.End);
            decimal OverlapLen((decimal Start, decimal End) a, (decimal Start, decimal End) b)
            {
                var s = Math.Max(ZS(a), ZS(b));
                var e = Math.Min(ZE(a), ZE(b));
                return Math.Max(0m, e - s);
            }
            decimal Len((decimal Start, decimal End) z) => Math.Max(_tickSize, ZE(z) - ZS(z) + _tickSize);
            decimal OverlapRatio((decimal Start, decimal End) a, (decimal Start, decimal End) b)
            {
                var o = OverlapLen(a, b);
                var denom = Math.Max(_tickSize, Math.Min(Len(a), Len(b)));
                return denom > 0m ? (o / denom) : 0m;
            }
            int TicksDist(decimal a, decimal b)
            {
                if (_tickSize <= 0m) return int.MaxValue;
                return (int)Math.Round(Math.Abs(a - b) / _tickSize);
            }

            decimal LocalMedianEma(int i, int halfWindow) => LocalMedian(_dailySmoothEma!, i, halfWindow);

            // ---------- HVN Engine: Level-State (Set/Clear) ----------
            for (int i = 0; i < prices.Count; i++)
            {
                var med = LocalMedianEma(i, hvLocalWindowHalf);
                bool inVA = false;
                if (idxVALGlobal >= 0 && idxVAHGlobal >= 0)
                {
                    int vaLo = Math.Min(idxVALGlobal, idxVAHGlobal);
                    int vaHi = Math.Max(idxVALGlobal, idxVAHGlobal);
                    inVA = (i >= vaLo && i <= vaHi);
                }

                decimal setFactor = inVA ? hvSetFactorInVA : hvSetFactorOutVA;
                decimal clearFactor = inVA ? hvClearFactorInVA : hvClearFactorOutVA;
                decimal levelShare = (totalVol > 0m) ? (vols[i] / totalVol) : 0m;

                // Set: EMA deutlich über lokalem Median + kleiner Share-Vorfilter
                bool shouldSet = (med > 0m && _dailySmoothEma![i] >= med * setFactor) && (levelShare >= minLevelSharePrev);

                // Clear: EMA fällt unter Clear-Faktor oder Share fällt ab
                bool shouldClear = (med > 0m && _dailySmoothEma![i] < med * clearFactor) || (levelShare < minLevelShareOff);

                if (!_dailyHvnLevelState![i])
                {
                    if (shouldSet) _dailyHvnLevelState[i] = true;
                }
                else
                {
                    if (shouldClear) _dailyHvnLevelState[i] = false;
                }
            }

            // ---------- HVN Engine: Segmente + Segment-VolShare ----------
            var hvSegs = BuildSegmentsFromMask(_dailyHvnLevelState!, scanLo, scanHi);
            hvSegs = MergeSegments(hvSegs, gap);
            hvSegs = hvSegs.Where(s => (s.R - s.L + 1) >= minPlateauWidthTicks).ToList();

            // EMA-Threshold-Plateau-Detektion (robust), pro Subrange (bottom tail / VA / top tail),
            // damit VA-Maxima die Tails nicht „überstimmen“.
            List<(int L, int R)> BuildThrSegsForRange(int rLo, int rHi)
            {
                rLo = Math.Max(0, rLo);
                rHi = Math.Min(prices.Count - 1, rHi);
                if (rLo > rHi) return new List<(int L, int R)>();

                decimal maxE = 0m;
                for (int i = rLo; i <= rHi; i++)
                    if (_dailySmoothEma![i] > maxE) maxE = _dailySmoothEma[i];
                if (maxE <= 0m) return new List<(int L, int R)>();

                int want = Math.Max(1, DailyTopNHVNs);
                var best = new List<(int L, int R)>();
                decimal thrFrac = 0.55m;
                while (thrFrac >= 0.35m)
                {
                    decimal thr = maxE * thrFrac;
                    var fb = BuildSegments(_dailySmoothEma!, rLo, rHi, v => v >= thr);
                    fb = MergeSegments(fb, gap);
                    fb = fb.Where(s => (s.R - s.L + 1) >= minPlateauWidthTicks).ToList();
                    if (fb.Count > best.Count) best = fb;
                    if (fb.Count >= want || (fb.Count > 0 && thrFrac <= 0.40m)) break;
                    thrFrac -= 0.05m;
                }
                return best;
            }

            var hvSegsFromThr = new List<(int L, int R)>();
            if (DailyAllowZonesOutsideVA && idxVALGlobal >= 0 && idxVAHGlobal >= 0)
            {
                int vaLo = Math.Min(idxVALGlobal, idxVAHGlobal);
                int vaHi = Math.Max(idxVALGlobal, idxVAHGlobal);
                if (vaLo > scanLo)
                    hvSegsFromThr.AddRange(BuildThrSegsForRange(scanLo, vaLo - 1));
                hvSegsFromThr.AddRange(BuildThrSegsForRange(Math.Max(scanLo, vaLo), Math.Min(scanHi, vaHi)));
                if (vaHi < scanHi)
                    hvSegsFromThr.AddRange(BuildThrSegsForRange(vaHi + 1, scanHi));
            }
            else
            {
                hvSegsFromThr = BuildThrSegsForRange(scanLo, scanHi);
            }

            hvSegsFromThr = MergeSegments(hvSegsFromThr, gap);
            hvSegsFromThr = hvSegsFromThr.Where(s => (s.R - s.L + 1) >= minPlateauWidthTicks).ToList();

            // Auswahl: wenn Threshold-Segmente mehr Struktur liefern als die Masken-Segmente, bevorzuge sie.
            if (hvSegsFromThr.Count > hvSegs.Count)
                hvSegs = hvSegsFromThr;

            // Dip/Saddle-Splitting: wenn mehrere Plateaus durch flache Brücken zu einem Segment verschmelzen,
            // splitten wir an deutlichen Dips in der EMA-Kurve.
            decimal dipFrac = 0.85m;
            int dipRunMin = 1;
            if (hvSegs.Count > 0)
            {
                var splitSegs = new List<(int L, int R)>();
                foreach (var s in hvSegs)
                {
                    int L0 = Math.Max(0, s.L);
                    int R0 = Math.Min(prices.Count - 1, s.R);
                    if (L0 > R0) continue;

                    int width = (R0 - L0 + 1);
                    if (width < (minPlateauWidthTicks * 2 + dipRunMin))
                    {
                        splitSegs.Add((L0, R0));
                        continue;
                    }

                    decimal peak = 0m;
                    for (int i = L0; i <= R0; i++)
                        if (_dailySmoothEma![i] > peak) peak = _dailySmoothEma[i];

                    if (peak <= 0m)
                    {
                        splitSegs.Add((L0, R0));
                        continue;
                    }

                    decimal dipThr = peak * dipFrac;
                    int curL = L0;
                    int run = 0;
                    int dipStart = -1;
                    for (int i = L0; i <= R0; i++)
                    {
                        if (_dailySmoothEma![i] < dipThr)
                        {
                            if (run == 0) dipStart = i;
                            run++;
                            if (run >= dipRunMin)
                            {
                                int leftR = dipStart - 1;
                                if (leftR - curL + 1 >= minPlateauWidthTicks)
                                    splitSegs.Add((curL, leftR));

                                // überspringe Dip-Run komplett, beginne danach neu
                                curL = i + 1;
                                run = 0;
                                dipStart = -1;
                            }
                        }
                        else
                        {
                            run = 0;
                            dipStart = -1;
                        }
                    }

                    if (curL <= R0 && (R0 - curL + 1) >= minPlateauWidthTicks)
                        splitSegs.Add((curL, R0));
                }

                hvSegs = splitSegs;
            }

            if (DailyEnableCapZoneWidth)
            {
                int capTicks = Math.Max(1, DailyCapZoneWidthTicks);
                hvSegs = CapSegmentsAroundExtremum(hvSegs, _dailySmoothEma!, capTicks, useMax: true);
            }

            decimal SegVol(int L, int R)
            {
                L = Math.Max(0, L);
                R = Math.Min(prices.Count - 1, R);
                decimal zVol = 0m;
                for (int k = L; k <= R; k++) zVol += vols[k];
                return zVol;
            }

            var hvCandidates = new List<(decimal Start, decimal End, decimal Score, decimal Share, bool InVA)>();
            foreach (var s in hvSegs)
            {
                decimal zVol = SegVol(s.L, s.R);
                decimal share = (totalVol > 0m ? (zVol / totalVol) : 0m);

                bool segInVA = false;
                if (idxVALGlobal >= 0 && idxVAHGlobal >= 0)
                {
                    int vaLo = Math.Min(idxVALGlobal, idxVAHGlobal);
                    int vaHi = Math.Max(idxVALGlobal, idxVAHGlobal);
                    int c = (s.L + s.R) / 2;
                    segInVA = (c >= vaLo && c <= vaHi);
                }

                decimal offThr = segInVA ? minZoneShareOff : minZoneShareOffOutVA;
                if (share < offThr) continue;

                if (!segInVA && mc.POCVol > 0m)
                {
                    int cIdx = (s.L + s.R) / 2;
                    bool inWin = (cIdx >= hvScanLo && cIdx <= hvScanHi);
                    decimal pocFactor = GetDailyPathConfig().HVNStrengthVsPOCOutside;
                    if (inWin) pocFactor = Math.Max(0.10m, pocFactor * 0.75m);

                    decimal peak = 0m;
                    for (int k = Math.Max(0, s.L); k <= Math.Min(prices.Count - 1, s.R); k++)
                        if (vols[k] > peak) peak = vols[k];
                    if (peak < (mc.POCVol * pocFactor))
                        continue;
                }

                decimal sum = 0m; int n = 0;
                for (int k = s.L; k <= s.R; k++) { sum += _dailySmoothEma![k]; n++; }
                decimal mean = (n > 0 ? sum / n : 0m);
                decimal score = mean * (1m + (share * 2m));
                hvCandidates.Add((prices[s.L], prices[s.R], score, share, segInVA));
            }

            // Spike-Rescue: schmale lokale Peaks als HVN-Kandidaten ergänzen (z.B. 6791),
            // auch wenn sie durch Segmentierung/MinZoneTicks nicht als Plateau-Segment auftauchen.
            bool CandidateOverlaps(decimal aS, decimal aE, decimal bS, decimal bE)
            {
                decimal a0 = Math.Min(aS, aE);
                decimal a1 = Math.Max(aS, aE);
                decimal b0 = Math.Min(bS, bE);
                decimal b1 = Math.Max(bS, bE);
                return !(a1 < b0 || b1 < a0);
            }

            if (prices.Count > 2 && totalVol > 0m)
            {
                int spikeHalf = 1;          // => 1..3 Ticks breit
                int regHalf = 6;            // regionale Max-Referenz
                int winHalf = hvLocalWindowHalf;
                decimal spikeMedianFactor = 1.30m;
                decimal spikeRegFactor = 0.70m;

                decimal LocalMedianEmaAt(int idx)
                {
                    int lo = Math.Max(0, idx - winHalf);
                    int hi = Math.Min(prices.Count - 1, idx + winHalf);
                    int len = hi - lo + 1;
                    if (len <= 0) return 0m;
                    var tmp = new decimal[len];
                    int t = 0;
                    for (int k = lo; k <= hi; k++) tmp[t++] = _dailySmoothEma![k];
                    Array.Sort(tmp);
                    return tmp[len / 2];
                }

                decimal RegionalMaxEmaAt(int idx)
                {
                    int lo = Math.Max(0, idx - regHalf);
                    int hi = Math.Min(prices.Count - 1, idx + regHalf);
                    decimal m = 0m;
                    for (int k = lo; k <= hi; k++)
                        if (_dailySmoothEma![k] > m) m = _dailySmoothEma[k];
                    return m;
                }

                for (int i = Math.Max(1, scanLo + 1); i <= Math.Min(prices.Count - 2, scanHi - 1); i++)
                {
                    decimal e = _dailySmoothEma![i];
                    if (e <= 0m) continue;

                    // lokales Maximum
                    if (!(e >= _dailySmoothEma[i - 1] && e >= _dailySmoothEma[i + 1]))
                        continue;

                    decimal med = LocalMedianEmaAt(i);
                    decimal reg = RegionalMaxEmaAt(i);
                    bool strongVsMedian = (med > 0m && e >= med * spikeMedianFactor);
                    bool strongVsReg = (reg > 0m && e >= reg * spikeRegFactor);
                    if (!strongVsMedian && !strongVsReg) continue;

                    int L = Math.Max(scanLo, i - spikeHalf);
                    int R = Math.Min(scanHi, i + spikeHalf);
                    if (L > R) continue;

                    decimal zVol = SegVol(L, R);
                    decimal share = zVol / totalVol;
                    if (share < minLevelShareOff) continue;

                    bool segInVA = false;
                    if (idxVALGlobal >= 0 && idxVAHGlobal >= 0)
                    {
                        int vaLo = Math.Min(idxVALGlobal, idxVAHGlobal);
                        int vaHi = Math.Max(idxVALGlobal, idxVAHGlobal);
                        segInVA = (i >= vaLo && i <= vaHi);
                    }

                    if (!segInVA && mc.POCVol > 0m)
                    {
                        bool inWin = (i >= hvScanLo && i <= hvScanHi);
                        decimal pocFactor = GetDailyPathConfig().HVNStrengthVsPOCOutside;
                        if (inWin) pocFactor = Math.Max(0.10m, pocFactor * 0.75m);

                        decimal peak = vols[i];
                        if (peak < (mc.POCVol * pocFactor))
                            continue;
                    }

                    decimal startP = prices[L];
                    decimal endP = prices[R];

                    bool overlapsExisting = hvCandidates.Any(c => CandidateOverlaps(startP, endP, c.Start, c.End));
                    if (overlapsExisting) continue;

                    // Score so, dass echte Spikes nicht von breiten aber flachen Segmenten verdrängt werden.
                    decimal score = e * (1m + (share * 4m));
                    hvCandidates.Add((startP, endP, score, share, segInVA));
                }
            }

            // Set-Share bevorzugen, aber nicht destruktiv (sonst würden HVNs nie auftauchen)
            var hvCandStrong = hvCandidates
                .Where(x => x.Share >= (x.InVA ? minZoneShareSet : minZoneShareSetOutVA))
                .OrderByDescending(x => x.Score)
                .ToList();

            if (hvCandStrong.Count == 0)
            {
                hvCandStrong = hvCandidates
                    .OrderByDescending(x => x.Score)
                    .Take(Math.Max(3, DailyTopNHVNs))
                    .ToList();
            }

            // Wenn weiterhin keine Kandidaten existieren, können keine HVNs ausgegeben werden.
            // Das darf praktisch nicht passieren, aber zur Sicherheit früh raus.
            int hvCandidateCount = hvCandStrong.Count;

            // ---------- HVN Engine: Tracking + Persistenz ----------
            foreach (var tz in _dailyHvnTracked)
                tz.Missing++;

            var used = new HashSet<int>();
            foreach (var cand in hvCandStrong)
            {
                var candZone = (Start: cand.Start, End: cand.End);
                TrackedZone? best = null;
                decimal bestOvr = 0m;
                int bestDist = int.MaxValue;

                foreach (var tz in _dailyHvnTracked)
                {
                    if (used.Contains(tz.Id)) continue;
                    if (tz.Missing >= persistClearRecalcs) continue;

                    var tzZone = (Start: tz.Start, End: tz.End);
                    var ovr = OverlapRatio(tzZone, candZone);
                    var dist = TicksDist(ZoneCenter(tzZone), ZoneCenter(candZone));
                    bool ok = (ovr >= 0.25m) || (dist <= 4);
                    if (!ok) continue;

                    if (ovr > bestOvr || (ovr == bestOvr && dist < bestDist))
                    {
                        best = tz;
                        bestOvr = ovr;
                        bestDist = dist;
                    }
                }

                if (best == null)
                {
                    if (_dailyHvnTracked.Count < maxTrackedZones)
                    {
                        _dailyHvnTracked.Add(new TrackedZone
                        {
                            Id = _dailyHvnNextId++,
                            Start = cand.Start,
                            End = cand.End,
                            Score = cand.Score,
                            Missing = 0,
                            HitCount = 1,
                            Active = (persistSetRecalcs <= 1)
                        });
                    }
                    continue;
                }

                used.Add(best.Id);
                best.Missing = 0;
                best.Score = cand.Score;
                best.HitCount++;
                if (!best.Active && best.HitCount >= persistSetRecalcs)
                    best.Active = true;

                // Kanten langsam bewegen (LERP + MaxShift)
                decimal curS = Math.Min(best.Start, best.End);
                decimal curE = Math.Max(best.Start, best.End);
                decimal tarS = Math.Min(cand.Start, cand.End);
                decimal tarE = Math.Max(cand.Start, cand.End);

                int widthTicks = Math.Max(1, (int)Math.Round((curE - curS) / _tickSize) + 1);
                int maxShiftTicks = Math.Max(1, (int)Math.Round(widthTicks * 0.2m));
                decimal maxShift = maxShiftTicks * _tickSize;
                decimal lerpAlpha = 0.30m;

                decimal newS = (curS + (tarS - curS) * lerpAlpha);
                decimal newE = (curE + (tarE - curE) * lerpAlpha);
                newS = Math.Max(curS - maxShift, Math.Min(curS + maxShift, newS));
                newE = Math.Max(curE - maxShift, Math.Min(curE + maxShift, newE));

                best.Start = newS;
                best.End = newE;
            }

            foreach (var tz in _dailyHvnTracked)
            {
                if (tz.Missing >= persistClearRecalcs)
                    tz.Active = false;
                if (tz.Missing > 0)
                    tz.HitCount = Math.Max(0, tz.HitCount - 1);
            }

            _dailyHvnTracked.RemoveAll(z => z.Missing >= persistClearRecalcs);

            // ---------- HVN Output ----------
            int hvTopN = Math.Max(0, DailyTopNHVNs);
            var rankedTracked = _dailyHvnTracked
                .OrderByDescending(z => z.Score)
                .ToList();

            bool InLocalWindow(decimal start, decimal end)
            {
                if (hvCurrP <= 0m || _tickSize <= 0m) return true;
                decimal c = (start + end) / 2m;
                int d = (int)Math.Round(Math.Abs(c - hvCurrP) / _tickSize);
                return d <= hvLocalWindowTicks;
            }

            // Output priorisieren: erst aktive Zonen im lokalen Preisfenster, dann Rest nach Score auffüllen.
            hvScored = rankedTracked
                .Where(z => z.Active && InLocalWindow(z.Start, z.End))
                .Take(hvTopN)
                .Select(z => (z.Start, z.End))
                .ToList();

            if (hvScored.Count < hvTopN)
            {
                foreach (var z in rankedTracked)
                {
                    if (hvScored.Count >= hvTopN) break;
                    if (!z.Active) continue;
                    if (hvScored.Any(h => h.Start == z.Start && h.End == z.End)) continue;
                    hvScored.Add((z.Start, z.End));
                }
            }

            if (hvScored.Count < hvTopN)
            {
                foreach (var z in rankedTracked)
                {
                    if (hvScored.Count >= hvTopN) break;
                    if (z.Active) continue;
                    if (z.Missing >= persistClearRecalcs) continue;
                    hvScored.Add((z.Start, z.End));
                }
            }

            List<(int L, int R)> FilterSegmentsByZoneVolShare(List<(int L, int R)> segs, decimal minShare)
            {
                if (segs == null || segs.Count == 0) return segs ?? new List<(int L, int R)>();
                if (totalVol <= 0m || minShare <= 0m) return segs;

                var res = new List<(int L, int R)>(segs.Count);
                foreach (var s in segs)
                {
                    int L = Math.Max(0, s.L);
                    int R = Math.Min(prices.Count - 1, s.R);
                    if (L > R) continue;
                    decimal zVol = 0m;
                    for (int i = L; i <= R; i++) zVol += vols[i];
                    if (zVol / totalVol >= minShare)
                        res.Add((L, R));
                }
                return res;
            }

            var lvSegs = new List<(int L, int R)>();

            // Ranges definieren
            var ranges = new List<(int Lo, int Hi)>();
            if (DailyAllowZonesOutsideVA && idxVALGlobal >= 0 && idxVAHGlobal >= 0)
            {
                int vaLo = Math.Min(idxVALGlobal, idxVAHGlobal);
                int vaHi = Math.Max(idxVALGlobal, idxVAHGlobal);

                // bottom tail, VA, top tail
                if (vaLo > 0) ranges.Add((0, vaLo - 1));
                ranges.Add((vaLo, vaHi));
                if (vaHi < prices.Count - 1) ranges.Add((vaHi + 1, prices.Count - 1));
            }
            else
            {
                ranges.Add((scanLo, scanHi));
            }

            foreach (var rg in ranges)
            {
                // LVN detection disabled for Daily profile (not used for entry or dynamic TP)
                // LVN Segmente would be processed here if needed
            }
            lvSegs = MergeSegments(lvSegs, gap);

            if (DailyEnableCapZoneWidth)
            {
                int capTicks = Math.Max(1, DailyCapZoneWidthTicks);
                lvSegs = CapSegmentsAroundExtremum(lvSegs, smooth, capTicks, useMax: false);
            }
            var lvZones = MergeSegmentsToZones(lvSegs, prices, 0);

            decimal minWidth = minTicks * _tickSize;
            lvZones = lvZones.Where(z => Math.Abs(z.End - z.Start) + _tickSize >= minWidth).ToList();

            if (DailyClampZonesToVA && !DailyAllowZonesOutsideVA)
            {
                hvScored = ClampZonesToRange(hvScored, currentVAL, currentVAH);
                lvZones = ClampZonesToRange(lvZones, currentVAL, currentVAH);
            }

            // LVNs disjunkt zu HVNs halten (LVN ist sekundär, aber soll nicht in HVN-Flächen liegen)
            if (hvScored.Count > 0 && lvZones.Count > 0)
            {
                var filtered = new List<(decimal Start, decimal End)>();
                foreach (var z in lvZones)
                {
                    decimal curS = Math.Min(z.Start, z.End);
                    decimal curE = Math.Max(z.Start, z.End);
                    foreach (var h in hvScored)
                    {
                        decimal hs = Math.Min(h.Start, h.End);
                        decimal he = Math.Max(h.Start, h.End);
                        if (curE < hs || curS > he) continue;
                        if (hs <= curS && he >= curE) { curS = curE + _tickSize; break; }
                        if (hs > curS && he < curE)
                        {
                            filtered.Add((curS, hs - _tickSize));
                            curS = he + _tickSize;
                        }
                        else if (hs <= curS) curS = he + _tickSize;
                        else if (he >= curE) curE = hs - _tickSize;
                        if (curS > curE) break;
                    }
                    if (curS <= curE) filtered.Add((curS, curE));
                }
                lvZones = filtered.Where(z => Math.Abs(z.End - z.Start) + _tickSize >= minWidth).ToList();
            }

            // Debug: LVN detection pipeline
            //this.LogInfo($"[DailyLVN-DBG] lvSegs={lvSegs?.Count ?? 0} lvZonesRaw={lvZones?.Count ?? 0} minWidth={minWidth:F2} hvScored={hvScored?.Count ?? 0} afterHVNCut={lvZones?.Count ?? 0}");

            // HVN zones already computed via EMA/Hysterese engine above

            // HVNs nahe VAH/VAL/POC vermeiden (Doppel-Blocker), aber NICHT destruktiv:
            // Wenn sich VAH/VAL/POC im Lauf der Session verschieben, darf dadurch nicht alles verschwinden.
            int keyLevelPadTicks = 2;
            if (keyLevelPadTicks > 0 && _tickSize > 0m && hvScored.Count > 1)
            {
                decimal pad = keyLevelPadTicks * _tickSize;
                decimal vah = currentVAH;
                decimal val = currentVAL;
                decimal poc = currentPOC;

                bool OverlapsLevel((decimal Start, decimal End) z, decimal level)
                {
                    if (level <= 0m) return false;
                    decimal s = Math.Min(z.Start, z.End);
                    decimal e = Math.Max(z.Start, z.End);
                    return level >= (s - pad) && level <= (e + pad);
                }

                var filtered = hvScored
                    .Where(z => !OverlapsLevel(z, vah) && !OverlapsLevel(z, val) && !OverlapsLevel(z, poc))
                    .ToList();

                // NICHT destruktiv: Filter nur anwenden, wenn ausreichend HVNs übrig bleiben.
                // Ziel: Key-Level-Duplikate reduzieren ohne die HVN-Liste zusammenbrechen zu lassen.
                int topN = Math.Max(0, DailyTopNHVNs);
                int minAfterFilter = Math.Min(hvScored.Count, Math.Max(2, topN - 1));
                if (filtered.Count >= minAfterFilter)
                    hvScored = filtered;
            }

            if (ShowDailyProfileLevels)
            {
                var now = DateTime.UtcNow;
                // Zeitbasiert, da Range-Bars -> Barzählung taugt nicht als Kadenz.
                if ((now - _dailyHvnLastStatLogUtc).TotalSeconds >= 5)
                {
                    _dailyHvnLastStatLogUtc = now;
                    int hvTopNForStat = Math.Max(0, DailyTopNHVNs);
                    decimal gLoP = (prices.Count > 0 ? prices[Math.Max(0, Math.Min(prices.Count - 1, scanLo))] : 0m);
                    decimal gHiP = (prices.Count > 0 ? prices[Math.Max(0, Math.Min(prices.Count - 1, scanHi))] : 0m);
                    decimal wLoP = (prices.Count > 0 ? prices[Math.Max(0, Math.Min(prices.Count - 1, hvScanLo))] : 0m);
                    decimal wHiP = (prices.Count > 0 ? prices[Math.Max(0, Math.Min(prices.Count - 1, hvScanHi))] : 0m);
                    decimal currP = (_tickSize > 0m && CurrentBar >= 0 ? RoundToTick(GetLastPrice()) : 0m);
                    this.LogInfo($"[DailyHVN-STAT] hvTopN={hvTopNForStat} hvOutNow={hvScored.Count} hvSegs={hvSegs.Count} hvCand={hvCandidateCount} tracked={_dailyHvnTracked.Count} allowOutVA={DailyAllowZonesOutsideVA} global=[{gLoP:F2},{gHiP:F2}] win=[{wLoP:F2},{wHiP:F2}] curr={currP:F2} winTicks={hvLocalWindowTicks} totalVol={totalVol:F0} POC={currentPOC:F2} VAH={currentVAH:F2} VAL={currentVAL:F2}");
                }
            }

            _dailyHvnLastOutputIds = _dailyHvnTracked
                .Where(z => hvScored.Any(h => (h.Start == z.Start && h.End == z.End)))
                .Select(z => z.Id)
                .ToList();

            List<(decimal Start, decimal End)> lvScored = lvZones
                .Select(z =>
                {
                    int l = prices.FindIndex(p => p == z.Start);
                    int r = prices.FindIndex(p => p == z.End);
                    if (l < 0 || r < 0) return (z, score: 0m);
                    if (l > r) { var t = l; l = r; r = t; }
                    decimal s = 0m; int n = 0;
                    for (int i = l; i <= r; i++) { s += smooth[i]; n++; }
                    decimal mean = (n > 0 ? s / n : 0m);
                    return (z, score: (mean > 0m ? (1m / (1m + mean)) : 1m));
                })
                .OrderByDescending(x => x.score)
                .Take(Math.Max(0, DailyTopNLVNs))
                .Select(x => x.z)
                .ToList();

            mc.HVNZones = hvScored;
            mc.LVNZones = lvScored;
            mc.HVNs = mc.HVNZones.Select(z => RoundToTick((z.Start + z.End) / 2m)).ToList();
            mc.LVNs = mc.LVNZones.Select(z => RoundToTick((z.Start + z.End) / 2m)).ToList();

            //this.LogInfo($"[DailyLVN-DBG] final LVNZones={lvScored?.Count ?? 0} topN={DailyTopNLVNs} ShowZoneRects={ShowZoneRects}");

            if (ShowDailyProfileLevels && mc.HVNZones.Count == 0)
            {
                this.LogInfo($"[DailyHVN-DBG] HVNs=0 levels={prices.Count} totalVol={totalVol:F0} POC={currentPOC:F2} VAH={currentVAH:F2} VAL={currentVAL:F2} hvSegs={hvSegs.Count} hvCand={hvCandidateCount} minTicks={minTicks} cap={DailyEnableCapZoneWidth}/{DailyCapZoneWidthTicks} topN={DailyTopNHVNs}");
            }

            _dailyProfileForPath = mc;
        }

        private void RecalcDailyProfileForVisual(decimal currentPOC, decimal currentVAH, decimal currentVAL)
        {
            _dailyProfileForVisual = _dailyProfileForPath;
        }

        private void RecalcDailyProfileForPath_Legacy(decimal currentPOC, decimal currentVAH, decimal currentVAL)
        {
            if (_tickSize <= 0m || _dailyProfileCombinedHist.Count == 0)
            {
                _prevDailyProfileForPath = _dailyProfileForPath;
                _dailyProfileForPath = null;
                return;
            }

            _prevDailyProfileForPath = _dailyProfileForPath;

            var mc = BuildMicroCompositeFromHist(
                _dailyProfileCombinedHist,
                DailySmoothTicks,
                DailyTopNPeaks,
                developingHist: null,
                valueAreaFraction: 0.70m,
                minZoneTicksOverride: DailyMinZoneTicks,
                minProminenceOverride: DailyMinProminence,
                minVolShareOverride: DailyMinVolShare,
                minWidthPctVAOverride: DailyMinWidthPctVA,
                gapTicksOverride: DailyGapTicks,
                topNHVNsOverride: DailyTopNHVNs,
                topNLVNsOverride: DailyTopNLVNs,
                maxDistTicksOverride: DailyMaxDistTicks,
                clampToVAOverride: DailyClampZonesToVA,
                enableCapOverride: DailyEnableCapZoneWidth,
                capTicksOverride: DailyCapZoneWidthTicks);

            // Aktuelle VA/POC aus _dailyLevels übernehmen (ATAS), nicht aus der Histogramm-VA.
            mc.POC = currentPOC;
            mc.VAH = currentVAH;
            mc.VAL = currentVAL;

            if (DailyClampZonesToVA && !DailyAllowZonesOutsideVA)
            {
                static List<(decimal Start, decimal End)> ClampZones(List<(decimal Start, decimal End)> zones, decimal lo, decimal hi)
                {
                    if (zones == null || zones.Count == 0) return zones ?? new List<(decimal Start, decimal End)>();
                    if (hi <= 0m || lo <= 0m) return zones;
                    if (hi < lo) { var t = lo; lo = hi; hi = t; }
                    var res = new List<(decimal Start, decimal End)>(zones.Count);
                    foreach (var z in zones)
                    {
                        var a = z.Start;
                        var b = z.End;
                        if (a > b) { var tmp = a; a = b; b = tmp; }
                        var s = Math.Max(a, lo);
                        var e = Math.Min(b, hi);
                        if (e >= s && s > 0m && e > 0m)
                            res.Add((s, e));
                    }
                    return res;
                }

                mc.HVNZones = ClampZones(mc.HVNZones, mc.VAL, mc.VAH);
                mc.LVNZones = ClampZones(mc.LVNZones, mc.VAL, mc.VAH);
            }

            if (DailyUsePlateauDetector && mc.LevelVols != null && mc.LevelVols.Count > 0)
            {
                var tick = _tickSize;
                var prices = mc.LevelVols.Keys.ToList();
                if (prices.Count > 2)
                {
                    var vols = prices.Select(p => mc.LevelVols.TryGetValue(p, out var v) ? v : 0m).ToArray();
                    var smooth = SmoothTriangularCached(vols, Math.Max(1, DailySmoothTicks));
                    decimal maxS = 0m;
                    for (int i = 0; i < smooth.Length; i++) if (smooth[i] > maxS) maxS = smooth[i];

                    if (maxS > 0m)
                    {
                        int scanLo = 0;
                        int scanHi = prices.Count - 1;
                        if (!DailyAllowZonesOutsideVA && mc.VAL > 0m && mc.VAH > 0m)
                        {
                            int idxVAL = prices.FindIndex(p => p == mc.VAL);
                            int idxVAH = prices.FindIndex(p => p == mc.VAH);
                            if (idxVAL >= 0 && idxVAH >= 0)
                            {
                                scanLo = Math.Min(idxVAL, idxVAH);
                                scanHi = Math.Max(idxVAL, idxVAH);
                            }
                        }

                        {
                            int vaLo = scanLo;
                            int vaHi = scanHi;

                            int minTicks = Math.Max(1, DailyMinZoneTicks);
                            int gap = Math.Max(0, DailyGapTicks);
                            int capTicks = Math.Max(1, DailyCapZoneWidthTicks);
                            bool capOn = DailyEnableCapZoneWidth;

                            int MaxW = capTicks;
                            (int L, int R) Cap((int L, int R) z)
                            {
                                if (!capOn) return z;
                                int w = z.R - z.L + 1;
                                if (w <= MaxW) return z;
                                int c = (z.L + z.R) / 2;
                                int half = (MaxW - 1) / 2;
                                int Lx = Math.Max(vaLo, c - half);
                                int Rx = Lx + MaxW - 1;
                                if (Rx > vaHi) { Rx = vaHi; Lx = Math.Max(vaLo, Rx - MaxW + 1); }
                                return (Lx, Rx);
                            }

                            List<(int L, int R)> MergeWithGap(List<(int L, int R)> zs)
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

                            decimal totalVol = mc.TotalVol > 0m ? mc.TotalVol : vols.Sum();
                            decimal ZoneVol(int L, int R)
                            {
                                decimal s = 0m;
                                for (int i = L; i <= R; i++) s += vols[i];
                                return s;
                            }

                            List<(int L, int R)> ExtractSegments(Func<decimal, bool> predicate)
                            {
                                var segs = new List<(int L, int R)>();
                                int i = vaLo;
                                while (i <= vaHi)
                                {
                                    while (i <= vaHi && !predicate(smooth[i])) i++;
                                    if (i > vaHi) break;
                                    int L = i;
                                    while (i <= vaHi && predicate(smooth[i])) i++;
                                    int R = i - 1;
                                    if (R - L + 1 < minTicks) continue;
                                    segs.Add(Cap((L, R)));
                                }
                                return MergeWithGap(segs);
                            }

                            decimal lvThr = Math.Max(0.01m, Math.Min(0.80m, DailyLVNPlateauFrac)) * maxS;

                            // HVN: adaptiv senken, wenn zu wenige Segmente gefunden werden (v.a. au?erhalb VA)
                            decimal hvFracStart = Math.Max(0.10m, Math.Min(0.95m, DailyHVNPlateauFrac));
                            decimal hvFracMin = DailyAllowZonesOutsideVA ? 0.35m : hvFracStart;
                            decimal hvFrac = hvFracStart;
                            List<(int L, int R)> hvSegs = new();
                            while (true)
                            {
                                decimal hvThr = hvFrac * maxS;
                                hvSegs = ExtractSegments(v => v >= hvThr);
                                if (hvSegs.Count >= Math.Max(1, DailyTopNHVNs) || hvFrac <= hvFracMin)
                                    break;
                                hvFrac = Math.Max(hvFracMin, hvFrac - 0.05m);
                            }

                            var lvSegs = ExtractSegments(v => v <= lvThr);

                            // LVNs disjunkt zu HVNs halten
                            if (hvSegs.Count > 0 && lvSegs.Count > 0)
                            {
                                var cut = hvSegs;
                                var kept = new List<(int L, int R)>();
                                foreach (var s in lvSegs)
                                {
                                    int curL = s.L, curR = s.R;
                                    foreach (var c in cut)
                                    {
                                        if (curR < c.L || curL > c.R) continue;
                                        if (c.L <= curL && c.R >= curR) { curL = curR + 1; break; }
                                        if (c.L > curL && c.R < curR)
                                        {
                                            kept.Add((curL, c.L - 1));
                                            curL = c.R + 1;
                                        }
                                        else if (c.L <= curL) curL = c.R + 1;
                                        else if (c.R >= curR) curR = c.L - 1;
                                        if (curL > curR) break;
                                    }
                                    if (curL <= curR) kept.Add((curL, curR));
                                }
                                lvSegs = MergeWithGap(kept.Where(z => z.R - z.L + 1 >= minTicks).ToList());
                            }

                            // Scoring: HVN nach VolShare/Breite; LVN nach (1-VolShare) und Tiefe
                            var hvScored = hvSegs
                                .Select(z => new
                                {
                                    z.L,
                                    z.R,
                                    score = (totalVol > 0m ? (ZoneVol(z.L, z.R) / totalVol) : 0m) + 0.25m * ((decimal)(z.R - z.L + 1) / Math.Max(1, (vaHi - vaLo + 1)))
                                })
                                .OrderByDescending(x => x.score)
                                .Take(Math.Max(1, DailyTopNHVNs))
                                .OrderBy(x => x.L)
                                .ToList();

                            var lvScored = lvSegs
                                .Select(z => new
                                {
                                    z.L,
                                    z.R,
                                    score = 1m - (totalVol > 0m ? (ZoneVol(z.L, z.R) / totalVol) : 0m)
                                })
                                .OrderByDescending(x => x.score)
                                .Take(Math.Max(1, DailyTopNLVNs))
                                .OrderBy(x => x.L)
                                .ToList();

                            mc.HVNZones = hvScored.Select(x => (prices[x.L], prices[x.R])).ToList();
                            mc.LVNZones = lvScored.Select(x => (prices[x.L], prices[x.R])).ToList();
                        }
                    }
                }
            }

            // POCVol aus Histogramm ableiten (für HVN-Stärke-Filter)
            if (mc.LevelVols != null && mc.LevelVols.TryGetValue(currentPOC, out var pv))
                mc.POCVol = pv;

            _dailyProfileForPath = mc;
        }
        private PathConfig GetPathConfig()
        {
            bool forceAllStrong = (MicroCompositeHVNStrength <= 0);
            bool forceAllWeak = (MicroCompositeHVNStrength >= 100);
            decimal factor = ComputeStrengthFactorFromSlider(MicroCompositeHVNStrength);

            decimal basePocInside = HVNStrengthVsPOC;
            decimal baseMedInside = HVNStrengthVsMedian;
            decimal basePromInside = MinProminenceVsMedian;
            decimal basePocOutside = HVNStrengthVsPOC;
            decimal baseMedOutside = HVNStrengthVsMedian;
            decimal basePromOutside = MinProminenceVsMedian;

            decimal pocInside = Math.Max(0.10m, Math.Min(0.90m, basePocInside * factor));
            decimal pocOutside = Math.Max(0.10m, Math.Min(0.90m, basePocOutside * factor));
            decimal medInside = Math.Max(0.50m, Math.Min(3.00m, 1.0m + ((baseMedInside - 1.0m) * factor)));
            decimal medOutside = Math.Max(0.50m, Math.Min(3.00m, 1.0m + ((baseMedOutside - 1.0m) * factor)));
            decimal promInside = Math.Max(0.05m, Math.Min(0.50m, basePromInside * factor));
            decimal promOutside = Math.Max(0.05m, Math.Min(0.50m, basePromOutside * factor));

            return new PathConfig
            {
                RequiredLVNsInPath = (UseMicroCompositeLVNsForWegFreiAndDynamicTP ? RequiredLVNsInPath : 0),
                RelaxVAEdgesWhenOutsideValue = RelaxVAEdgesWhenOutsideValue,
                HVNStrengthVsPOC = pocInside,
                HVNStrengthVsMedian = medInside,
                MinProminenceVsMedian = promInside,
                HVNStrengthVsPOCInside = pocInside,
                HVNStrengthVsPOCOutside = pocOutside,
                HVNStrengthVsMedianInside = medInside,
                HVNStrengthVsMedianOutside = medOutside,
                MinProminenceVsMedianInside = promInside,
                MinProminenceVsMedianOutside = promOutside,
                ForceAllHVNsStrong = forceAllStrong,
                ForceAllHVNsWeak = forceAllWeak,
                MinBlockerDistanceTicksVsRisk = MinBlockerDistanceTicksVsRisk,
                UseHvnZonesForBlocking = UseMicroCompositeHVNsForWegFreiAndDynamicTP,
                UseLvnPathForBlocking = UseMicroCompositeLVNsForWegFreiAndDynamicTP
            };
        }

        private static decimal ComputeStrengthFactorFromSlider(int strength0to100)
        {
            int s = Math.Max(0, Math.Min(100, strength0to100));
            decimal delta = (s - 50) / 50m;
            decimal factor = 1m + (delta * 0.25m);
            if (factor < 0.70m) factor = 0.70m;
            if (factor > 1.30m) factor = 1.30m;
            return factor;
        }

        private PathConfig GetDailyPathConfig()
        {
            bool forceAllStrong = (DailyHVNStrength <= 0);
            bool forceAllWeak = (DailyHVNStrength >= 100);

            decimal factor = ComputeStrengthFactorFromSlider(DailyHVNStrength);

            decimal basePocInside = 0.45m;
            decimal baseMedInside = 1.30m;
            decimal basePromInside = 0.15m;
            decimal basePocOutside = 0.35m;
            decimal baseMedOutside = 1.10m;
            decimal basePromOutside = 0.10m;

            decimal pocInside = Math.Max(0.10m, Math.Min(0.90m, basePocInside * factor));
            decimal pocOutside = Math.Max(0.10m, Math.Min(0.90m, basePocOutside * factor));
            decimal medInside = Math.Max(0.50m, Math.Min(3.00m, 1.0m + ((baseMedInside - 1.0m) * factor)));
            decimal medOutside = Math.Max(0.50m, Math.Min(3.00m, 1.0m + ((baseMedOutside - 1.0m) * factor)));
            decimal promInside = Math.Max(0.05m, Math.Min(0.50m, basePromInside * factor));
            decimal promOutside = Math.Max(0.05m, Math.Min(0.50m, basePromOutside * factor));

            return new PathConfig
            {
                RequiredLVNsInPath = 0,
                RelaxVAEdgesWhenOutsideValue = DailyRelaxVAEdgesWhenOutsideValue,
                HVNStrengthVsPOC = pocInside,
                HVNStrengthVsMedian = medInside,
                MinProminenceVsMedian = promInside,
                HVNStrengthVsPOCInside = pocInside,
                HVNStrengthVsPOCOutside = pocOutside,
                HVNStrengthVsMedianInside = medInside,
                HVNStrengthVsMedianOutside = medOutside,
                MinProminenceVsMedianInside = promInside,
                MinProminenceVsMedianOutside = promOutside,
                ForceAllHVNsStrong = forceAllStrong,
                ForceAllHVNsWeak = forceAllWeak,
                MinBlockerDistanceTicksVsRisk = DailyMinBlockerDistanceTicksVsRisk,
                UseLvnPathForBlocking = false
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
        private int _lastImbalanceLogBar = -1;

        private bool _isBreakEvenCompleted = false;
        private int _breakEvenLevelReached = 0;

        // Live/History-Gates
        private int _liveStartBar = -1;
        private bool _entryLogicLockedUntilLive = true;
        private bool _onlyFromLive = false; // falls du eine Option daf?r hast
        private int _lastEvalBar = -1;    // Entprellen: nur 1x pro Bar
        private int _lastSeenBar = -1;    // h?chster bisher gesehener Bar-Index
        private int _lastProcessedBar = -1;
        private DateTime _lastDailyWegFreiDebugHeartbeatTime = DateTime.MinValue;


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

        private readonly VolatilityRegimeCalculator _volatilityRegimeCalculator = new VolatilityRegimeCalculator();

        private readonly MyNamespace.Strategies.MarketAnalysis.MarketRegimeEvaluator _marketRegimeEvaluator = new MyNamespace.Strategies.MarketAnalysis.MarketRegimeEvaluator();

        private bool _historicalAnalysisPerformed = false; // Stellt sicher, dass die Analyse nur einmal l?uft

        private bool _swingCandleSeedPerformed = false;


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
        private readonly Dictionary<int, decimal> _stackedBuyImbCount = new(); // gesamt
        private readonly Dictionary<int, decimal> _stackedSellImbCount = new(); // gesamt
        private readonly Dictionary<int, decimal> _stackedBuyImbTopCount = new(); // direkt unter High (anchored)
        private readonly Dictionary<int, decimal> _stackedSellImbBottomCount = new(); // direkt ?ber Low (anchored)
        private readonly Dictionary<int, decimal> _imbalanceScoreSeries = new();
        private readonly Dictionary<int, string> _imbalanceScoreLabelSeries = new();

        // --- Stacked-Imbalance Parameter (Defaults analog ATAS) ---
        private decimal _imbalanceRatioPct = 150; // 150% => Faktor 1.5 (realistischer als 300%)
        private int _imbalanceRangeMin = 2;   // Mindestl?nge eines zusammenh?ngenden Stacks
        private int _imbalanceVolumeMin = 10;  // Mindestvolumen pro Level (angepasst an reale Daten)
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

        [Category("Reversal Settings")]
        [DisplayName("Reversal Thresholds")]
        [Description("Reversal-spezifische Orderflow-Schwellenwerte f?r ReversalBounce Pattern")]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public OrderflowThresholds ReversalThresholds { get; set; } = new OrderflowThresholds();

        [Category("Continuation Settings")]
        [DisplayName("Continuation: Phase Lookback Bars")]
        [Description("Wie viele Bars rückwirkend Healthy_Pullback oder Momentum_Refuel gewesen sein darf, damit ContinuationPullback-Einstiege weiterhin erlaubt sind.")]
        [DefaultValue(6)]
        public int ContinuationPhaseLookbackBars
        {
            get => ReversalThresholds?.ContinuationPhaseLookbackBars_UI ?? 6;
            set
            {
                if (ReversalThresholds != null)
                    ReversalThresholds.ContinuationPhaseLookbackBars_UI = Math.Max(0, value);
            }
        }


        [OFTParameter]
        [Category("Reversal Settings")]
        [DisplayName("Reversal Evaluator aktiv")]
        [Description("Aktiviert/deaktiviert den ReversalBouncePatternEvaluator.")]
        [DefaultValue(true)]
        public bool EnableReversalEvaluator { get; set; } = true;

        [OFTParameter]
        [Category("Reversal Settings")]
        [DisplayName("Break-Even Stages (Trigger:Offset)")]
        [Description("BE-Stufen für Reversal-Patterns. Format: trigger:offset;trigger:offset (Ticks). Leer = Setup/Default.")]
        public string ReversalBreakEvenStagesConfig { get; set; } = "5:1;8:5";

        [OFTParameter]
        [Category("Continuation Settings")]
        [DisplayName("Pullback Evaluator aktiv")]
        [Description("Aktiviert/deaktiviert den ContinuationPullbackEvaluator.")]
        [DefaultValue(false)]
        public bool EnablePullbackEvaluator { get; set; } = false;

        [OFTParameter]
        [Category("Continuation Settings")]
        [DisplayName("Break-Even Stages (Trigger:Offset)")]
        [Description("BE-Stufen für Continuation-Patterns. Format: trigger:offset;trigger:offset (Ticks). Leer = Setup/Default.")]
        public string ContinuationBreakEvenStagesConfig { get; set; } = "";


        [Display(Name = "Noisy Orderflow-Logs unterdrücken",
                 GroupName = "Orderflow - Logging",
                 Description = "Unterdrückt sehr ausführliche Debug/Info-Logs aus Orderflow-Modulen (z.B. FeatureCalculator/Thresholds/AddBar/ImbalanceScore), damit Backtests übersichtlicher werden.",
                 Order = 1)]
        public bool SuppressNoisyOrderflowLogs { get; set; } = true;


        [Display(Name = "Enable Order Timeout", GroupName = "Order Timeout", Order = 100)]
        [Description("Aktiviert das automatische L?schen von Entry-Orders nach einer bestimmten Anzahl von Bars.")] // F?ge Beschreibung hinzu
        public bool EnableOrderTimeout { get; set; } = true;

        [Display(Name = "Timeout (Bars)", GroupName = "Order Timeout", Order = 110)]
        [Range(1, 100, ErrorMessage = "Please enter a value between 1 and 100.")]
        [Description("Anzahl der Bars, nach denen eine nicht gef?llte Entry-Order gel?scht wird.")] // F?ge Beschreibung hinzu
        public int OrderTimeoutBars { get; set; } = 1;

        [OFTParameter]
        [Category("CSV Export")]
        [DisplayName("CSV Export aktivieren")]
        [Description("Aktiviert/deaktiviert den CSV-Export der OvSnapshots")]
        public bool EnableCsvExport { get; set; } = false;

        [OFTParameter]
        [Category("CSV Export")]
        [DisplayName("T?gliche CSV-Dateien")]
        [Description("Erstellt f?r jeden Handelstag eine separate CSV-Datei")]
        public bool UseDailyCsvFiles { get; set; } = false;

        [OFTParameter]
        [Category("CSV Export")]
        [DisplayName("CSV-Dateien ?berschreiben")]
        [Description("??berschreibt existierende CSV-Dateien statt anzuh?ngen (n?tzlich f?r Backtests)")]
        public bool OverwriteExistingCsv { get; set; } = false;

        [OFTParameter]
        [Category("CSV Export")]
        [DisplayName("ImbalanceScore CSV Export")]
        [Description("Schreibt eine separate CSV nur mit Imbalance-Score Werten")]
        public bool EnableImbalanceScoreCsvExport { get; set; } = false;

        // ... F?GE HIER WEITERE GLOBALE [OFTParameter]-PROPERTIES F?R ALLE COMMON CONDITIONS HINZU,
        // DIE DU ?BER DAS ATAS-UI STEUERN M?CHTEST (z.B. VolBurst, CvdImpulseRisingSequence etc.)
        // Stelle sicher, dass f?r jede "UseXyz"-Flag in SetupConditionConfig ein
        // entsprechender "Parameter_UseXyz" hier definiert ist, wenn es global einstellbar sein soll.
        // =========================================================================


        // =========================================================================
        // ALLES, WAS BLEIBT | Anfang
        // =========================================================================
        private SqueezeMomentumCalculator _squeezeCalc;

        private decimal _pdPOC, _pdVAH, _pdVAL;

        private BackgroundCsvWriter _csvWriter;
        private string _csvPath;
        private string _currentCsvDate;
        private string _storedBacktestDate;

        private BackgroundCsvWriter _imbScoreCsvWriter;
        private string _imbScoreCsvPath;

        private const string CsvHeader =
        "BarIndex,Time_ISO,Instrument,Open,High,Low,Close,Volume,Delta,Ask,Bid,MaxCounterShareBull,MaxCounterShareBear,VolBurstZ,CvdImpulse,CvdCoherence,AggPressure,TradeRateZ," +
        "Efficiency,BuyTrades,SellTrades,TotalTrades,IttZ,SweepUp,SweepDn,StackedBuyImbCount,StackedSellImbCount,StackedBuyImbTopCount,StackedSellImbBottomCount,StackedImbRatioPct,StackedImbMinVolPerLevel," +
        "StackedImbRangeMin,StackedImbMaxDepthTicks,CandleDuration,VolPerSecond,EmaVolPerSecond,EmaVolPerSecondStd,CumulativeDelta,CumulativeVolume,MarketRegime,BarDeltaPerVolume," +
        // neu: History/CreatedAtUtc
        "HistoryVersion,ThresholdsCreatedAtUtc,ThresholdsSnapshot,PrunerNullifiedFields,FinalDecision,MetCriteriaList,MetHardList,MetRelList,MetCriteriaCount,PossibleCriteriaCount,DetectReason,CompositeScore,CvdMean30,CvdStd30,VolBurstMax3,VolBurstAge,InflectionCount3,SoftVolBurstFlag,SoftNegativeFlag," +
        // neu: Pattern- und OfFeatures-Felder (Text/Num)
        "PatternType,PatternDirection,PatternCategory,PatternLevel,PatternConfidence,PatternScore,PatternCombinedConf,VolBurstClass,VolBurstCooldownLeft,SlopeCvd,SlopePressure,SlopeEff,SlopeTradeRate,PersistBull,PersistBear,InflectionCvd,InflectionPressure," +
        "BacktestRunId,CommitHash,FeatureFlags";

        private const string ImbalanceScoreCsvHeader =
        "BarIndex,Time_ISO,ImbalanceScore,Label,TotalPairs,BuyMax,SellMax,BuyPairs,SellPairs,AvgBuyVol,AvgSellVol,BaseVol,coverageBuy,coverageSell,anchoredBuy,anchoredSell,volBuyNorm,volSellNorm,weightedBuy,weightedSell";



        // =========================================================================
        // ALLES, WAS BLEIBT | Ende
        // =========================================================================
        // ========================================================
        // PRIVATE MEMBER-VARIABLEN (Zustand der Strategie)
        // ========================================================    
        private readonly ILoggerSource _loggerSource;
        private SetupConfiguration _strategySetup;

        internal static volatile bool SuppressNoisyOrderflowLogsGlobal;

        private static readonly string[] _noisyLogPrefixes =
        {
            "[OrderflowFeatureCalculator]",
            "[AddFeatureAndSync]",
            "[ImbalanceScore]",
            "[RangeStructureDetector]",
            "[AddBar]",
        };

        internal static bool ShouldSuppressNoisyLogStatic(string message)
        {
            if (!SuppressNoisyOrderflowLogsGlobal)
                return false;
            if (string.IsNullOrEmpty(message))
                return false;

            for (int i = 0; i < _noisyLogPrefixes.Length; i++)
            {
                var p = _noisyLogPrefixes[i];
                if (message.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private bool ShouldSuppressNoisyLog(string message) => ShouldSuppressNoisyLogStatic(message);

        private MarketAnalysis.MarketStructureContext _marketStructureContext;
        private const bool UseTick900ForMarketStructure = true;
        private static int _msGlobalGeneration;
        private static long _msGlobalBackfillRequestedEndTimeTicks;
        private static int _msGlobalTick900BackfillInFlight;
        private readonly int _msGeneration;
        private readonly string _msInstanceId = Guid.NewGuid().ToString("N").Substring(0, 6);
        private Mutex? _msLeaderMutex;
        private Semaphore? _msBackfillRequestSemaphore;
        private bool _msBackfillRequestSemaphoreOwned;
        private bool _msIsLeaderInstance;
        private bool _msLeaderElectionAttempted;
        private bool _msLeaderLogOnce;
        private bool _msZonesDiagLogged;
        private bool _msZonesSnapshotDiagLogged;
        private bool _msBackfillGateDiagLogged;
        private bool _msZonesSnapshotClearedByLeader;
        private bool _msZone11RenderDiagLogged;

        private bool _msZone12RenderDiagLogged;
        private bool _msZone13RenderDiagLogged;
        private bool _msTick900BackfillRequestDiagLogged;
        private MemoryMappedFile? _msZonesMmf;
        private string? _msZonesMmfName;
        private Tick900Aggregator _msTick900Aggregator;
        private int _msTick900Bar;
        private DateTime _msTick900BucketSessionStart;
        private bool _msTick900BackfillRequested;
        private int _msTick900BackfillInFlight;
        private bool _msTick900BackfillCompleted;
        private DateTime _msTick900BackfillRequestedEndTime;
        private DateTime _msTick900LastProcessedBackfillEndTime;
        private DateTime _msTick900BackfillCandidateFirstTime;
        private DateTime _msTick900BackfillCandidateEndTime;
        private int _msTick900BackfillCandidateStableCount;
        private bool _msTick900ResetOnNextBackfillResponse;
        private bool _msTick900RebuildAfterResumeNeeded;
        private int _msTick900LiveBufAppliedLastBackfill;
        private int _msTick900LiveBufSkippedLastBackfill;
        private const int MsTick900MaxLiveBuffer = 200000;
        private readonly List<MarketDataArg> _msTick900LiveTradeBuffer = new List<MarketDataArg>(4096);
        private readonly List<TickCandle> _msTick900ClosedCandles = new List<TickCandle>(4096);

        private int _msTick900ZonesDirtyFlag = 0;
        private int _msTick900LastSeenMaxZoneId = -1;
        
        private DateTimeKind? _msChartTimeKind;
        private DateTime _msLastLiveTradeWallClockUtc;
        private DateTime _msLastBackfillRequestWallClockUtc;
        private Func<int, int>? _msGetXByBar;
        private System.Reflection.MethodInfo? _msGetXByBarMi;
        private int _msGetXByBarMiParamCount;
        private bool _msXMapDiagLogged;
        private bool _msTick900BackfillSawTicks;
        private bool _msTick900BackfillTicksDiagLogged;
        private bool _msTick900OhlcInvariantDiagLogged;
        private PatternRunner _patternRunner;
        private OvSnapshotHistory _ovSnapshotHistory;
        private OfFeaturesHistory _ofFeaturesHistory;
        private Dictionary<int, OfFeatures> _ofFeaturesByBar;

        private int _lastSeenMarketStructureMaxReadyZoneId = -1;
        private int _lastIntrabarZoneTriggerClosed = -1;

        private int _lastIntrabarZoneTriggerMaxPendingId = -1;

        private int _lastPendingZonesLogClosed = -1;

        private int _deferredIntrabarPendingEvalClosed = -1;
        private int _deferredIntrabarPendingEvalMaxPendingId = -1;
        private bool _deferredIntrabarPendingEvalTick900Dirty;

        private int _lastOnCalculateBar = -1;
        private decimal _lastOnCalculateValue;
        readonly object _ofFeaturesSync = new object();
        private readonly List<int> _ofFeaturesBarList;
        private readonly Dictionary<int, int> _ofFeaturesBarMap = new Dictionary<int, int>(); // fungiert als Set (Wert wird nicht verwendet)

        private OrderflowFeatureCalculator _featureCalculator;
 
        private MyNamespace.Strategies.MarketAnalysis.MarketStateEngineV2 _marketStateEngineV2;
        private MyNamespace.Strategies.Models.MarketStateV2 _currentMarketStateV2;
        private string _marketStateV2OverlayText;
        private MarketRegimeDetails _marketRegimeDetails;
 
        //Volumenprofil Vortag
        private VolumeProfileGenerator _volumeProfileGenerator;
        // Volumenprofil

        private void AddFeatureAndSync(OfFeatures feature)
        {
            if (feature == null)
            {
                var m = "[AddFeatureAndSync] feature ist null -> Abbruch.";
                if (!ShouldSuppressNoisyLog(m))
                    this.LogWarn(m);
                return;
            }

            // History wird ben?tigt; kann nicht hier neu zugewiesen werden wenn readonly.
            if (_ofFeaturesHistory == null)
            {
                var m = "[AddFeatureAndSync] _ofFeaturesHistory ist null -> Abbruch (initialisiere im Konstruktor).";
                if (!ShouldSuppressNoisyLog(m))
                    this.LogWarn(m);
                return;
            }

            // Pr?fe die readonly-Collections; falls null -> Abbruch (sollte durch Konstruktor initialisiert sein)
            if (_ofFeaturesByBar == null || _ofFeaturesBarList == null || _ofFeaturesBarMap == null)
            {
                var m = "[AddFeatureAndSync] One of required collections is null (_ofFeaturesByBar/_ofFeaturesBarList/_ofFeaturesBarMap). Abbruch (initialisieren im Konstruktor).";
                if (!ShouldSuppressNoisyLog(m))
                    this.LogWarn(m);
                return;
            }

            // _ofFeaturesSync ist readonly und muss ebenfalls im Konstruktor gesetzt worden sein.
            if (_ofFeaturesSync == null)
            {
                // Falls das Lock-Objekt doch null ist, kann man nicht weitermachen (readonly sollte verhindern)
                var m = "[AddFeatureAndSync] _ofFeaturesSync ist null -> Abbruch (sollte readonly im Deklarator oder Konstruktor gesetzt werden).";
                if (!ShouldSuppressNoisyLog(m))
                    this.LogWarn(m);
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
                        var m = $"[AddFeatureAndSync] Fehler beim Hinzuf?gen zu _ofFeaturesHistory: {exAdd.GetType().Name}: {exAdd.Message}";
                        if (!ShouldSuppressNoisyLog(m))
                            this.LogWarn(m);
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






        private decimal? _longTriggerOverride;
        private decimal? _shortTriggerOverride;
        private int? _reclaimTicksOverride;
        private int? _nearTicksOverride;

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
        private bool EnableIntrabarEntry = false;
        private bool EnableEntryQuoteSanityGuard = true;
        private int EntryQuoteMaxDeviationTicks = 8;

        private DateTime _bbbaLastChangeUtc = DateTime.MinValue;
        private DateTime _bbbaLastWarnUtc = DateTime.MinValue;
        private decimal _bbbaLastAsk = 0m;
        private decimal _bbbaLastBid = 0m;
        private decimal _bbbaLastTrade = 0m;

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
        private bool _intrabarImmediatePlaceMode = false;
        private int _intrabarImmediatePlaceBar = -1;

        // Entry-Kontext
        private bool _entryIsLong = false;           // true = Long, false = Short
        private int _signalBarIndex = -1;
        private decimal _signalBarHigh = 0m;
        private decimal _signalBarLow = 0m;

        private int _lastEntryAttemptSetupBar = -1;

        private int _lastPendingSignalBarPlaced = -1;

        private int _entryTimeoutStartBarIndex = -1;

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
        [Browsable(false)]
        [Range(1, 500, ErrorMessage = "Die SMA Periode muss zwischen 1 und 500 liegen.")]
        [Description("Periode f?r den Simple Moving Average (SMA), der als Trendfilter f?r das Signal dient.")]
        public int SmaPeriod { get; set; } = 50; // Standardwert ist 10

        [Display(Name = "VWAP Periode", // Wenn VWAP einen Zeitraum unterst?tzt, andernfalls entfernen oder anpassen.
          GroupName = "VWAP",
          Order = 1360)]
        public int VWAPPeriod { get; set; } = 1; // Standardzeitraum; geeigneten Wert in den ATAS-Dokumenten ?berpr?fen



        [Display(Name = "Bänder anzeigen",
            GroupName = "VWAP",
            Order = 1)]
        public bool ShowVWAPBands { get; set; } = false; // Standardm??ig auf true setzen, um B?nder anzuzeigen

        [Display(Name = "VWAP-Nähe Blocker aktivieren",
                 GroupName = "VWAP",
                 Description = "Blockiert Einstiege bei Annäherung an VWAP aus Richtung",
                 Order = 2)]
        public bool EnableVwapProximityBlocker { get; set; } = true;

        [Display(Name = "VWAP Abstand (Ticks)",
                 GroupName = "VWAP",
                 Description = "Abstand in Ticks vom VWAP für Blockade (empfohlen: 6 für ES)",
                 Order = 3)]
        [Range(1, 50, ErrorMessage = "Wert zwischen 1 und 50")]
        public int VwapProximityTicks { get; set; } = 8;

        [Display(Name = "Round Numbers Stufen",
         GroupName = "Level-Parameter",
         Order = 100)]
        public decimal PointStep { get; set; } = 50;


        // =========================================================================
        // Level-Parameter Einstellungen
        // =========================================================================

        // Entry-Blocker Parameter
        [Display(Name = "Level-System berechnen/rendern",
                 GroupName = "Level-Parameter",
                 Description = "Aktiviert die Berechnung und Darstellung der Level (ohne automatisch Entries zu blockieren).",
                 Order = 0)]
        public bool EnableLevelSystem { get; set; } = true;

        [Display(Name = "Level-System aktivieren",
                 GroupName = "Level-Parameter",
                 Description = "Aktiviert das komplette Level-System: Berechnet signifikante Preislevel, ?berwacht Ber?hrungen und blockiert Einstiege bei Level-N?he. Dies ist der Master-Schalter f?r alle Level-bezogenen Funktionen.",
                 Order = 1)]
        public bool EnableIsBlocked { get; set; } = false;

        [Display(Name = "Mindestabstand f?r Entry (Ticks)",
                 GroupName = "Level-Parameter",
                 Description = "Verhindert den Einstieg, wenn der Preis zu nah an signifikanten Zonen ist. Dies betrifft die 'IsTooCloseForEntry' Logik. H?here Werte machen die Strategie selektiver.",
                 Order = 3)]
        [Range(0.1, 1000)]
        public decimal ProximityTicksForEntry { get; set; } = 8;

        // Signifikante Level Parameter
        [Display(Name = "Level visualisieren",
                 GroupName = "Level-Parameter",
                 Description = "Zeigt signifikante Preislevel im Chart an. Ben?tigt 'Level-System aktivieren'. Wenn Visualisierung ohne Berechnung gew?nscht, wird die Level-Berechnung automatisch aktiviert.",
                 Order = 2)]
        public bool EnableSignificantPreviousLevels { get; set; } = true;

        [Display(Name = "Anzahl Tage f?r signifikante Levels",
                 GroupName = "Level-Parameter",
                 Description = "Definiert ?ber wie viele Tage zur?ck signifikante Preislevel (Tageshoch/-tief, Vortages-POC) f?r die Handelsentscheidung ber?cksichtigt werden. Mehr Tage geben mehr Referenzpunkte.",
                 Order = 4)]
        [Range(1, 100)]
        public int MaxDaysForSignificantLevels { get; set; } = 5;

        [Display(Name = "Mindestabstand Kerzen-Ber?hrungen",
                 GroupName = "Level-Parameter",
                 Description = "Definiert den Mindestabstand in Kerzen, bevor eine neue Ber?hrung desselben Levels gez?hlt wird. Verhindert, dass schnelle Preisfluktuationen um ein Level als multiple Ber?hrungen gewertet werden.",
                 Order = 5)]
        [Range(1, 50)]
        public int MinCandleSeparationForTouches { get; set; } = 5;



        [Browsable(false)]
        [Display(Name = "MicroComposite ?ber N Bars", GroupName = "MicroComposite Einstellungen",
        Description = "Definiert die Gr??e des rollierenden Volumenprofils in Kerzen",
        Order = 7)]
        public int M { get; set; } = 100; // Standardwert anpassen

        [Browsable(false)]
        [Display(Name = "Value Area %", GroupName = "MicroComposite Einstellungen", Order = 7)]
        public decimal ValueAreaPct { get; set; } = 0.70m; // auf 0.682m setzen, wenn ATAS-so


        // =========================================================================
        // MicroComposite Einstellungen
        // =========================================================================

        // MicroComposite ? System Einstellungen
        [Display(Name = "MicroComposite-System aktivieren",
                 GroupName = "MicroComposite",
                 Description = "Aktiviert das komplette MicroComposite-System: Berechnet Volumenprofile, pr?ft Weg-Frei-Blocker und zeigt HVN/LVN-Zonen an. Dies ist der Master-Schalter f?r alle MicroComposite-bezogenen Funktionen.",
                 Order = 1)]
        public bool EnableMicroCompositeSystem { get; set; } = false;

        [Display(Name = "MicroComposite visualisieren",
                 GroupName = "MicroComposite",
                 Description = "Zeigt MicroComposite-Levels (HVN/LVN-Zonen, POC, VAH/VAL) im Chart an. Ben?tigt 'MicroComposite-System aktivieren'. Wenn Visualisierung ohne Berechnung gew?nscht, wird das MicroComposite-System automatisch aktiviert.",
                 Order = 2)]
        public bool ShowMicroCompositeLevels { get; set; } = false;

        [Category("MicroComposite")]
        [DisplayName("MicroComposite Einstellungen")]
        [Description("MicroComposite Parameter (zugeklappt, bei Bedarf aufklappen).")]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public MicroCompositeSettingsWrapper MicroCompositeSettings
        {
            get => _microCompositeSettings;
        }

        [Category("MicroComposite")]
        [DisplayName("MicroComposite Weg-Frei-Einstellungen")]
        [Description("MicroComposite Weg-Frei/Path/Blocking Parameter (zugeklappt, bei Bedarf aufklappen).")]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public MicroCompositeWegFreiSettingsWrapper MicroCompositeWegFreiSettings
        {
            get => _microCompositeWegFreiSettings;
        }

        [Display(Name = "Daily Profile anzeigen",
                 GroupName = "Daily Profile",
                 Description = "Zeigt Daily-HVN/LVN-Zonen (aus dem Daily-Volumenprofil) im Chart an. Unabhängig vom MicroComposite.",
                 Order = 1)]
        public bool ShowDailyProfileLevels { get; set; } = false;

        [Display(Name = "Daily Histogramm anzeigen",
                 GroupName = "Daily Profile",
                 Description = "Zeigt das interne Daily-Volumenhistogramm (PublicActiveVolume) als Market-Profile-Balken pro Preislevel.",
                 Order = 2)]
        public bool ShowDailyHistogram { get; set; } = false;

        [Category("Daily Profile")]
        [DisplayName("Daily Profile Einstellungen")]
        [Description("Daily Profile Parameter (zugeklappt, bei Bedarf aufklappen).")]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public DailyProfileSettingsWrapper DailyProfileSettings
        {
            get => _dailyProfileSettings;
        }

        [Category("Daily Profile")]
        [DisplayName("Daily Profile Weg-Frei-Einstellungen")]
        [Description("Daily Profile Weg-Frei/Path/Blocking Parameter (zugeklappt, bei Bedarf aufklappen).")]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public DailyProfileWegFreiSettingsWrapper DailyProfileWegFreiSettings
        {
            get => _dailyProfileWegFreiSettings;
        }

        [Browsable(false)]
        [Display(Name = "Daily Histogramm Breite (px)",
                 GroupName = "Daily Profile Einstellungen",
                 Description = "Breite der Histogramm-Balken (maximale Ausdehnung) in Pixel.",
                 Order = 3)]
        [Range(40, 1000)]
        public int DailyHistogramWidthPx { get; set; } = 600;

        [Browsable(false)]
        [Display(Name = "Daily Histogramm Opacity (0..255)",
                 GroupName = "Daily Profile Einstellungen",
                 Description = "Transparenz f?r die Histogramm-F?llung.",
                 Order = 4)]
        [Range(5, 255)]
        public int DailyHistogramOpacity { get; set; } = 60;

        [Browsable(false)]
        [Display(Name = "Daily Profile: Volumenquelle",
                 GroupName = "Daily Profile Einstellungen",
                 Description = "Wenn aktiv, nutzt das Daily-Profil pvi.Volume (wie ATAS Market Profile bei Einstellung 'Volumen'). Wenn aus, nutzt Ask+Bid (Lots).",
                 Order = 10)]
        public bool DailyProfileUseAtasVolume { get; set; } = true;

        // MicroComposite ? Einstellungen (Datenqualit?t)
        [Browsable(false)]
        [Display(Name = "Top HVN Zonen",
                 GroupName = "MicroComposite Einstellungen",
                 Description = "Maximale Anzahl an HVN-Zonen, die nach Scoring behalten werden. Weniger HVNs ausw?hlen; verringert ?berlagerungen und h?lt die wichtigsten, kompakten Zonen im Fokus.",
                 Order = 10)]
        [Range(1, 50)]
        public int TopNHVNs { get; set; } = 4;

        [Browsable(false)]
        [Display(Name = "Top LVN Zonen",
                 GroupName = "MicroComposite Einstellungen",
                 Description = "Maximale Anzahl an LVN-Zonen, die nach Scoring behalten werden",
                 Order = 11)]
        [Range(1, 50)]
        public int TopNLVNs { get; set; } = 4;

        [Browsable(false)]
        [Display(Name = "Mindestbreite Zone (Ticks)",
                 GroupName = "MicroComposite Einstellungen",
                 Description = "Lässt schmale, klare HVNs zu (nicht zu niedrig setzen, sonst Rauschen).",
                 Order = 12)]
        [Range(1, 100)]
        public int MinZoneTicks { get; set; } = 3;

        [Browsable(false)]
        [Display(Name = "Minimale Prominenz",
                 GroupName = "MicroComposite Einstellungen",
                 Description = "Hebt die Qualität; indirekt oft schmalere Zonen, weil flache, breitgezogene 'Hügel' rausfallen. (0..1)",
                 Order = 13)]
        [Range(0.0, 1.0)]
        public decimal MinProminence { get; set; } = 0.14m;

        [Browsable(false)]
        [Display(Name = "Min. Volumenanteil",
                 GroupName = "MicroComposite Einstellungen",
                 Description = "Filtert Zonen mit sehr wenig Volumenanteil. (0..1)",
                 Order = 14)]
        [Range(0.0, 1.0)]
        public decimal MinVolShare { get; set; } = 0.005m;

        [Browsable(false)]
        [Display(Name = "Min. Breite relativ VA",
                 GroupName = "MicroComposite Einstellungen",
                 Description = "Mindestbreite einer Zone relativ zur Value-Area-Breite (0..1)",
                 Order = 15)]
        [Range(0.0, 1.0)]
        public decimal MinWidthPctVA { get; set; } = 0.02m;

        [Browsable(false)]
        [Display(Name = "Merge-Gap (Ticks)",
                 GroupName = "MicroComposite Einstellungen",
                 Description = "Zonen in diesem Tick-Abstand werden zusammengef?hrt. Klein halten, damit benachbarte Kandidaten/Zonen nicht zu einer sehr breiten Zone zusammengef?hrt werden.",
                 Order = 16)]
        [Range(0, 20)]
        public int GapTicks { get; set; } = 3;

        [Browsable(false)]
        [Display(Name = "Max. Distanzgewicht (Ticks)",
                 GroupName = "MicroComposite Einstellungen",
                 Description = "Skalierung der Entfernung zum aktuellen Preis im Score",
                 Order = 20)]
        [Range(1, 100)]
        public int MaxDistTicks { get; set; } = 20;

        [Browsable(false)]
        [Display(Name = "Smoothing (Ticks)",
                 GroupName = "MicroComposite Einstellungen",
                 Description = "Triangular Smoothing-Spanne f?r die Volumenreihe. Weniger Gl?ttung macht Peaks schmaler und Zonen k?rzer.",
                 Order = 21)]
        [Range(1, 50)]
        public int SmoothTicks { get; set; } = 3;

        // Zonen-Begrenzungs-Parameter
        [Browsable(false)]
        [Display(Name = "Zonen an Value Area klemmen",
                 GroupName = "MicroComposite Einstellungen",
                 Description = "Schneidet alle HVN/LVN-Zonen an den Value-Area-Grenzen (VAL/VAH) zu. Dies verhindert, dass Zonen ?ber die wichtigsten Handelsbereiche hinausragen und sorgt f?r saubere, definierte Zonengrenzen.",
                 Order = 22)]
        public bool ClampZonesToVA { get; set; } = false;

        [Browsable(false)]
        [Display(Name = "Zonenbreite begrenzen aktiv",
                 GroupName = "MicroComposite Einstellungen",
                 Description = "Aktiviert eine harte Obergrenze f?r die maximale Breite von HVN/LVN-Zonen. N?tzlich um ?berbreite Zonen zu vermeiden, die durch Volumen-Schwankungen entstehen k?nnen.",
                 Order = 23)]
        public bool EnableCapZoneWidth { get; set; } = false;

        [Browsable(false)]
        [Display(Name = "Maximale Zonenbreite (Ticks)",
                 GroupName = "MicroComposite Einstellungen",
                 Description = "Die maximale Breite einer HVN/LVN-Zone in Ticks, symmetrisch um die Zonenmitte. Kleinere Werte erzeugen engere, pr?zisere Zonen; gr??ere Werte erlauben breitere Handelsbereiche.",
                 Order = 24)]
        [Range(1, 50)]
        public int CapZoneWidthTicks { get; set; } = 6;

        // MicroComposite ? Weg-Frei-Einstellungen (Handelslogik)
        [Browsable(false)]
        [Display(Name = "Mindest-LVNs im Pfad",
                 GroupName = "MicroComposite Weg-Frei-Einstellungen",
                 Description = "Wie viele LVN-Korridore (Low Volume Nodes) m?ssen im Preispfad vorhanden sein, damit ein Handel als g?ltig gilt. LVNs sind 'd?nne' Stellen im Volumenprofil, die der Preis leicht durchqueren kann. H?here Werte machen die Strategie selektiver.",
                 Order = 1)]
        [Range(0, 10)]
        public int RequiredLVNsInPath { get; set; } = 1;

        [Browsable(false)]
        [Display(Name = "MC HVN Zonen f?r WegFrei/TP nutzen",
                 GroupName = "MicroComposite Weg-Frei-Einstellungen",
                 Description = "Wenn deaktiviert, werden MicroComposite HVN-Zonen/Punkte NICHT für WegFrei-Blocking und Dynamic TP verwendet. MC POC/VAH/VAL bleiben weiterhin aktiv.",
                 Order = 2)]
        public bool UseMicroCompositeHVNsForWegFreiAndDynamicTP { get; set; } = true;

        [Browsable(false)]
        [Display(Name = "MC LVN Zonen f?r WegFrei/TP nutzen",
                 GroupName = "MicroComposite Weg-Frei-Einstellungen",
                 Description = "Wenn deaktiviert, wird die LVN-Pfad-Anforderung aus dem MicroComposite für WegFrei-Blocking nicht verwendet. MC POC/VAH/VAL bleiben weiterhin aktiv.",
                 Order = 3)]
        public bool UseMicroCompositeLVNsForWegFreiAndDynamicTP { get; set; } = false;

        [Browsable(false)]
        [Display(Name = "Daily WegFrei aktiv",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Aktiviert ein separates Daily-Volumenprofil (nur für WegFrei) und berücksichtigt Daily HVN/LVN zusätzlich zum MicroComposite (UND-Logik, sofern beide aktiv sind).",
                 Order = 1)]
        public bool EnableDailyProfilePathSystem { get; set; } = false;

        [Browsable(false)]
        [Display(Name = "Daily DMinTicks (Mindestabstand Blocker)",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Mindestabstand in Ticks: Blockt Entry, wenn ein Daily Blocker (POC/VA-Kante/HVN-Zonen-Kante) innerhalb dieser Distanz im Pfad liegt. Analog zu DMinTicks im MicroComposite.",
                 Order = 1)]
        [Range(1, 200)]
        public int DailyDMinTicks { get; set; } = 8;

        [Browsable(false)]
        [Display(Name = "Daily Recalc alle N Bars",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Rechenintervall in Bars (Range-Bar kompatibel). 1 = jedes Bar neu berechnen.",
                 Order = 2)]
        [Range(1, 500)]
        public int DailyProfileRecalcEveryNBars { get; set; } = 1;

        [Browsable(false)]
        [Display(Name = "Daily Top HVN Zonen",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Maximale Anzahl an HVN-Zonen (Daily Profil).",
                 Order = 2)]
        [Range(1, 50)]
        public int DailyTopNHVNs { get; set; } = 8;

        [Browsable(false)]
        [Display(Name = "Daily Top LVN Zonen",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Maximale Anzahl an LVN-Zonen (Daily Profil).",
                 Order = 2)]
        [Range(1, 50)]
        public int DailyTopNLVNs { get; set; } = 4;

        [Browsable(false)]
        [Display(Name = "Daily Mindestbreite Zone (Ticks)",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Mindestbreite einer Daily HVN/LVN-Zone in Ticks.",
                 Order = 2)]
        [Range(1, 100)]
        public int DailyMinZoneTicks { get; set; } = 2;

        [Browsable(false)]
        [Display(Name = "Daily Min Prominenz",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Filtert flache Peaks (Daily Profil).",
                 Order = 2)]
        [Range(0.0, 1.0)]
        public decimal DailyMinProminence { get; set; } = 0.12m;

        [Browsable(false)]
        [Display(Name = "Daily Min Volumenanteil",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Filtert Zonen mit sehr kleinem Volumenanteil (Daily Profil).",
                 Order = 2)]
        [Range(0.0, 1.0)]
        public decimal DailyMinVolShare { get; set; } = 0.004m;

        [Browsable(false)]
        [Display(Name = "Daily Min Breite relativ VA",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Mindestbreite einer Zone relativ zur Value-Area-Breite (Daily Profil).",
                 Order = 2)]
        [Range(0.0, 1.0)]
        public decimal DailyMinWidthPctVA { get; set; } = 0.02m;

        [Browsable(false)]
        [Display(Name = "Daily Merge-Gap (Ticks)",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Zonen in diesem Tick-Abstand werden zusammengef?hrt (Daily Profil).",
                 Order = 2)]
        [Range(0, 20)]
        public int DailyGapTicks { get; set; } = 1;

        [Browsable(false)]
        [Display(Name = "Daily Max. Distanzgewicht (Ticks)",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Skalierung der Entfernung zum aktuellen Preis im Score (Daily Profil).",
                 Order = 2)]
        [Range(1, 200)]
        public int DailyMaxDistTicks { get; set; } = 40;

        [Browsable(false)]
        [Display(Name = "Daily Zonen an Value Area klemmen",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Schneidet alle Daily-HVN/LVN-Zonen an den Value-Area-Grenzen (VAL/VAH) zu.",
                 Order = 2)]
        public bool DailyClampZonesToVA { get; set; } = true;

        [Browsable(false)]
        [Display(Name = "Daily Zonen au?erhalb VA zulassen",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Wenn aktiv, d?rfen Daily HVN/LVN-Zonen auch au?erhalb der Value Area liegen (keine VA-Klemmung; Plateau-Scan ?ber komplette Profil-Achse).",
                 Order = 2)]
        public bool DailyAllowZonesOutsideVA { get; set; } = true;

        [Browsable(false)]
        [Display(Name = "Daily Zonenbreite begrenzen aktiv",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Aktiviert eine harte Obergrenze f?r die maximale Breite von Daily-HVN/LVN-Zonen.",
                 Order = 2)]
        public bool DailyEnableCapZoneWidth { get; set; } = true;

        [Browsable(false)]
        [Display(Name = "Daily Maximale Zonenbreite (Ticks)",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Maximale Breite einer Daily-HVN/LVN-Zone in Ticks.",
                 Order = 2)]
        [Range(1, 100)]
        public int DailyCapZoneWidthTicks { get; set; } = 10;

        [Browsable(false)]
        [Display(Name = "Daily Plateau-Detektor (HVN/LVN)",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Erkennt HVN/LVN als zusammenh?ngende High/Low-Volume-Areas per Schwellwert (n?her an ATAS bei breiten Zonen).",
                 Order = 2)]
        public bool DailyUsePlateauDetector { get; set; } = true;

        [Browsable(false)]
        [Display(Name = "Daily HVN Plateau Schwelle (Anteil vom Max)",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Schwellwert f?r HVN-Areas: smoothVol >= Anteil * maxSmoothVol.",
                 Order = 2)]
        [Range(0.1, 0.95)]
        public decimal DailyHVNPlateauFrac { get; set; } = 0.65m;

        [Browsable(false)]
        [Display(Name = "Daily LVN Plateau Schwelle (Anteil vom Max)",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Schwellwert f?r LVN-Areas: smoothVol <= Anteil * maxSmoothVol.",
                 Order = 2)]
        [Range(0.01, 0.8)]
        public decimal DailyLVNPlateauFrac { get; set; } = 0.25m;

        [Browsable(false)]
        [Display(Name = "Daily Smoothing (Ticks)",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Triangular Smoothing-Spanne für Daily HVN/LVN.",
                 Order = 3)]
        [Range(1, 50)]
        public int DailySmoothTicks { get; set; } = 3;

        [Browsable(false)]
        [Display(Name = "Daily Top Peaks",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Maximale Anzahl HVN-/LVN-Zentren für Daily Profil.",
                 Order = 4)]
        [Range(1, 50)]
        public int DailyTopNPeaks { get; set; } = 6;

        [Browsable(false)]
        [Display(Name = "Daily Mindest-LVNs im Pfad",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Wie viele LVN-Korridore m?ssen im Daily-Profil im Pfad liegen.",
                 Order = 5)]
        [Range(0, 10)]
        public int DailyRequiredLVNsInPath { get; set; } = 1;

        [Browsable(false)]
        [Display(Name = "Daily VA-Kanten au?erhalb Value lockern",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Wie MicroComposite: wenn Preis au?erhalb VA, VA-Kanten weniger streng behandeln.",
                 Order = 6)]
        public bool DailyRelaxVAEdgesWhenOutsideValue { get; set; } = true;

        [Browsable(false)]
        [Display(Name = "Daily HVN Strength (0..100)",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Ein einziger Stärkeregler für Daily-HVN-Blocking: 50 = neutral, höher = strenger (weniger blockt), niedriger = liberaler (mehr blockt). Intern werden Inside/Outside-VA Schwellen (POC/Median/Prominenz) angepasst.",
                 Order = 7)]
        [Range(0, 100)]
        public int DailyHVNStrength { get; set; } = 50;

        [Browsable(false)]
        [Display(Name = "Daily HVN-St?rke vs. POC (%)",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "HVN gilt als stark, wenn Volumen >= Anteil des POC-Volumens (Daily Profil).",
                 Order = 8)]
        [Range(0.1, 1.0)]
        public decimal DailyHVNStrengthVsPOC { get; set; } = 0.40m;

        [Browsable(false)]
        [Display(Name = "Daily HVN-St?rke vs. Median (Faktor)",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "HVN gilt als stark, wenn Volumen >= Faktor * MedianVol (Daily Profil).",
                 Order = 9)]
        [Range(0.5, 3.0)]
        public decimal DailyHVNStrengthVsMedian { get; set; } = 1.20m;

        [Browsable(false)]
        [Display(Name = "Daily Mindest-Prominenz vs. Median (%)",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Mindest-Prominenz für Peaks im Daily Profil.",
                 Order = 10)]
        [Range(0.05, 0.5)]
        public decimal DailyMinProminenceVsMedian { get; set; } = 0.15m;

        [Browsable(false)]
        [Display(Name = "Daily Mindest-Abstand Blocker vs. Risk (Faktor)",
                 GroupName = "Daily Profile Weg-Frei-Einstellungen",
                 Description = "Blocker muss mind. RiskTicks * Faktor entfernt sein (Daily Profil).",
                 Order = 11)]
        [Range(0.5, 5.0)]
        public int DailyMinBlockerDistanceTicksVsRisk { get; set; } = 1;

        [Browsable(false)]
        [Display(Name = "MicroComposite HVN Strength (0..100)",
                 GroupName = "MicroComposite Weg-Frei-Einstellungen",
                 Description = "Ein einziger Stärkeregler für MicroComposite-HVN-Blocking: 50 = neutral, höher = strenger (weniger blockt), niedriger = liberaler (mehr blockt). Intern werden Inside/Outside-VA Schwellen (POC/Median/Prominenz) angepasst.",
                 Order = 1)]
        [Range(0, 100)]
        public int MicroCompositeHVNStrength { get; set; } = 50;

        [Browsable(false)]
        [Display(Name = "VA-Kanten au?erhalb Value lockern",
                 GroupName = "MicroComposite Weg-Frei-Einstellungen",
                 Description = "Wenn der aktuelle Preis au?erhalb der Value Area (VA) liegt, werden die VA-Kanten (VAL/VAH) als weniger strenge Blocker behandelt. Dies erm?glicht Trades, auch wenn der Preis kurz au?erhalb der wichtigsten Handelszone ist.",
                 Order = 2)]
        public bool RelaxVAEdgesWhenOutsideValue { get; set; } = true;

        [Browsable(false)]
        [Display(Name = "HVN-St?rke vs. POC (%)",
                 GroupName = "MicroComposite Weg-Frei-Einstellungen",
                 Description = "Ein HVN (High Volume Node) gilt als 'starker Blocker', wenn sein Volumen mindestens dieser Prozentsatz des POC-Volumens betr?gt. Der POC (Point of Control) ist das Preislevel mit dem h?chsten Volumen. Höhere Werte machen die Blocker-Bewertung strenger.",
                 Order = 3)]
        [Range(0.1, 1.0)]
        public decimal HVNStrengthVsPOC { get; set; } = 0.40m;

        [Browsable(false)]
        [Display(Name = "HVN-St?rke vs. Median (Faktor)",
                 GroupName = "MicroComposite Weg-Frei-Einstellungen",
                 Description = "Ein HVN gilt als 'stark', wenn sein Volumen mindestens dieser Faktor mal dem Durchschnittsvolumen aller Preislevel entspricht. Beispiel: 1.2 bedeutet, das HVN muss 20% mehr Volumen als der Durchschnitt haben. Dies hilft, wirklich signifikante Volumenpunkte zu identifizieren.",
                 Order = 4)]
        [Range(0.5, 3.0)]
        public decimal HVNStrengthVsMedian { get; set; } = 1.20m;

        [Browsable(false)]
        [Display(Name = "Mindest-Prominenz vs. Median (%)",
                 GroupName = "MicroComposite Weg-Frei-Einstellungen",
                 Description = "Die 'Prominenz' misst, wie deutlich sich ein Volumenpeak von seiner Umgebung abhebt. Dieser Wert bestimmt die minimale Prominenz im Verhältnis zum Medianvolumen. Höhere Werte filtern nur die deutlichsten Peaks heraus und ignorieren kleine Volumenvariationen.",
                 Order = 5)]
        [Range(0.05, 0.5)]
        public decimal MinProminenceVsMedian { get; set; } = 0.15m;

        [Browsable(false)]
        [Display(Name = "Mindest-Abstand Blocker vs. Risk (Faktor)",
                 GroupName = "MicroComposite Weg-Frei-Einstellungen",
                 Description = "Ein Blocker (HVN, POC, VA-Kante) muss mindestens diesen Faktor mal dem Risk-Ticks Abstand vom aktuellen Preis entfernt sein. Beispiel: 1 bedeutet der Blocker muss weiter entfernt sein als die Risk-Distanz. Höhere Werte erlauben Trades näher an Blockern.",
                 Order = 6)]
        [Range(0.5, 5.0)]
        public int MinBlockerDistanceTicksVsRisk { get; set; } = 1;

        [Browsable(false)]
        [Display(Name = "D_min (Ticks bis HVN/POC)",
                 GroupName = "MicroComposite Weg-Frei-Einstellungen",
                 Description = "Die fundamentale Risikodistanz in Ticks. Dies ist die Basis f?r alle Weg-Frei-Berechnungen und definiert den Mindestabstand zu wichtigen Volumenleveln. H?here Werte machen die Strategie konservativer und verhindern Trades in volatilen Bereichen.",
                 Order = 7)]
        [Range(1, 100)]
        public int DMinTicks { get; set; } = 8;

        [Display(Name = "Min Dynamic TP Distance (Ticks)",
                 GroupName = "TP/SL ? Dynamic TP",
                 Description = "Wenn TpType='Vorgeschlagen' aktiv ist und das vorgeschlagene Ziel n?her als diese Tick-Distanz am Entry liegt, wird auf einen Tick-basierten TP zur?ckgefallen.",
                 Order = 1)]
        [Range(0, 100)]
        public int MinDynamicTpDistanceTicks { get; set; } = 6;

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


        public Geldfluss3_3()
        {
            _msGeneration = Interlocked.Increment(ref _msGlobalGeneration);
            _loggerSource = this as ILoggerSource ?? throw new InvalidOperationException("Strategy must implement ILoggerSource or provide a logger source.");

            _microCompositeSettings = new MicroCompositeSettingsWrapper(this);
            _microCompositeWegFreiSettings = new MicroCompositeWegFreiSettingsWrapper(this);
            _dailyProfileSettings = new DailyProfileSettingsWrapper(this);
            _dailyProfileWegFreiSettings = new DailyProfileWegFreiSettingsWrapper(this);
            // Lock - Objekt: kann inline beim Feld deklariert werden; hier optional nochmal setzen
            // _ofFeaturesSync = new object(); // nur erlaubt, wenn nicht inline initialisiert
            _ofFeaturesByBar = new Dictionary<int, OfFeatures>();
            // _ofFeaturesHistory: falls readonly und sinnvoll, initialisieren; ansonsten lass es null und handle im Code
            _ofFeaturesHistory = new OfFeaturesHistory();

        }

        private bool IsMarketStructureLeader()
        {
            if (!UseTick900ForMarketStructure)
                return false;

            // Lazy leader election: only attempt to acquire the mutex when we actually run calculation.
            // This avoids render-only strategy instances becoming the leader and blocking backfill.
            if (!_msLeaderElectionAttempted)
                TryAcquireMarketStructureLeadership();

            if (_msIsLeaderInstance)
                return true;

            if (!_msLeaderLogOnce)
            {
                _msLeaderLogOnce = true;
                //this.LogInfo($"[Tick900Backfill:{_msInstanceId}] Non-leader instance: MarketStructure Tick900 pipeline disabled for this instance.");
            }

            return false;
        }


        // Wird einmalig beim Laden der Strategie aufgerufen (Beibehalten)
        protected override void OnInitialize()
        {
            base.OnInitialize();

            SuppressNoisyOrderflowLogsGlobal = SuppressNoisyOrderflowLogs;

            this.LogInfo("[OnInitialize] Geldfluss 3.3 initialisiert.");

            // OS-weiter Leader-Mutex: ATAS kann mehrere Instanzen/AppDomains parallel laden.
            // Statische Felder sind dann nicht ausreichend. Wir erlauben Tick900/MS nur der Leader-Instanz.
            string instrumentName = (InstrumentInfo?.Instrument ?? InstrumentInfo?.ToString() ?? "Unknown");
            foreach (var ch in System.IO.Path.GetInvalidFileNameChars())
                instrumentName = instrumentName.Replace(ch, '_');

            try
            {
                var pid = 0;
                try { pid = Process.GetCurrentProcess().Id; } catch { pid = 0; }
                var mutexName = $"Local\\Geldfluss3_3_Tick900MS_{instrumentName}_P{pid}";
                _msLeaderMutex = new Mutex(false, mutexName);

                var backfillSemaphoreName = $"Local\\Geldfluss3_3_Tick900BackfillReq_{instrumentName}";
                _msBackfillRequestSemaphore = new Semaphore(1, 1, backfillSemaphoreName);
                _msBackfillRequestSemaphoreOwned = false;
                _msIsLeaderInstance = false;
                _msLeaderElectionAttempted = false;
                //this.LogInfo($"[Tick900Backfill:{_msInstanceId}] LeaderMutex created='{mutexName}' (acquire deferred to OnCalculate)");
            }
            catch (Exception ex)
            {
                // Wenn Mutex nicht geht, fallen wir auf die bisherige Generation-Guard Logik zurück.
                _msIsLeaderInstance = true;
                _msLeaderElectionAttempted = true;
                //this.LogWarn($"[Tick900Backfill:{_msInstanceId}] LeaderMutex acquire failed -> fallback leader=true. {ex.GetType().Name}: {ex.Message}");
            }

            // Zonen-Snapshot-Sharing ist optional. Falls MMF nicht möglich ist, dürfen wir NICHT die Strategie destabilisieren.
            try
            {
                _msZonesMmfName = $"Local\\Geldfluss3_3_Tick900Zones_{instrumentName}";
                _msZonesMmf = MemoryMappedFile.CreateOrOpen(_msZonesMmfName, 64 * 1024, MemoryMappedFileAccess.ReadWrite);
            }
            catch (Exception ex)
            {
                _msZonesMmf = null;
                //this.LogWarn($"[Tick900Backfill:{_msInstanceId}] Zones MMF disabled: {ex.GetType().Name}: {ex.Message}");
            }

            _marketStructureContext = new MarketStructureContext();
            try
            {
                _marketStructureContext.LoggerSource = this;
                _marketStructureContext.ZigZagSensitivity = MarketStructureZigZagSensitivity;
                var r = _marketRegimeDetails != null ? _marketRegimeDetails.Regime : MarketRegime.Normal;
                _marketStructureContext.WickZoneMinTicks = GetEffectiveMarketStructureWickMinTicks(r);
            }
            catch { }
            _msTick900Aggregator = new Tick900Aggregator(900);
            _msTick900Bar = 0;
            _msTick900BucketSessionStart = DateTime.MinValue;
            _msTick900BackfillRequested = false;
            _msTick900BackfillCompleted = false;
            _msTick900BackfillRequestedEndTime = default;



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


            _strategySetup = new SetupConfiguration();
            _strategySetup.UiThresholds = ReversalThresholds;

            // Erst die histories/collections anlegen, die andere Komponenten ben?tigen:
            _ovSnapshotHistory = new OvSnapshotHistory(256, _loggerSource);
            _ofFeaturesHistory = new OfFeaturesHistory(capacity: 500, _loggerSource);

            // Jetzt den Feature-Calculator anlegen, weil er die OvSnapshot-Historie und StrategyConfig braucht
            _featureCalculator = new OrderflowFeatureCalculator(_ovSnapshotHistory, _strategySetup, ofFeaturesHistory: _ofFeaturesHistory, loggerSource: _loggerSource);

            // Cluster-Statistik kann unabh?ngig sein
            _myClusterStatistic = new MyClusterStatistic();

            if (_strategySetup?.PatternDefaultThresholds != null && _strategySetup.PatternDefaultThresholds.Count == 0)
            {
                _strategySetup.PatternDefaultThresholds[OrderflowPatternType.PotentialLongReversalBounce] = new OrderflowThresholds();
                _strategySetup.PatternDefaultThresholds[OrderflowPatternType.PotentialShortReversalBounce] = new OrderflowThresholds();
            }

            try
            {
                _strategySetup?.PatternDefaultThresholds?.Remove(OrderflowPatternType.PotentialLongTrendContinuation);
                _strategySetup?.PatternDefaultThresholds?.Remove(OrderflowPatternType.PotentialShortTrendContinuation);
            }
            catch { }

            ReversalBouncePatternEvaluatorV2? reversalEvaluatorLong = null;
            ReversalBouncePatternEvaluatorV2? reversalEvaluatorShort = null;
            ContinuationPullbackEvaluatorV2? continuationEvaluatorLong = null;
            ContinuationPullbackEvaluatorV2? continuationEvaluatorShort = null;

            if (EnableReversalEvaluator)
            {
                reversalEvaluatorLong = new ReversalBouncePatternEvaluatorV2(OrderDirections.Buy, this, ReversalCompressionGapTicks);
                reversalEvaluatorShort = new ReversalBouncePatternEvaluatorV2(OrderDirections.Sell, this, ReversalCompressionGapTicks);
            }

            if (EnablePullbackEvaluator)
            {
                continuationEvaluatorLong = new ContinuationPullbackEvaluatorV2(OrderDirections.Buy, this, ContinuationCompressionGapTicks);
                continuationEvaluatorShort = new ContinuationPullbackEvaluatorV2(OrderDirections.Sell, this, ContinuationCompressionGapTicks);
            }
            _patternRunner = new PatternRunner(
                reversalEvaluatorLong,
                reversalEvaluatorShort,
                continuationEvaluatorLong,
                continuationEvaluatorShort,
                ReversalThresholds,
                _loggerSource,
                _tickSize);

            try
            {
                var keys = _strategySetup?.PatternDefaultThresholds?.Keys;
                var keysJoined = keys != null && keys.Any() ? string.Join(",", keys) : "(none)";
                this.LogInfo($"[INIT] PatternDefaultThresholds keys: {keysJoined}");
            _marketStateEngineV2 = new MyNamespace.Strategies.MarketAnalysis.MarketStateEngineV2(_tickSize, slopeLookbackK: 5, zWindowN: 100);

            // Now prepare per-bar containers (gr??en sinnvoll initialisieren)
            _ofFeaturesByBar = new Dictionary<int, OfFeatures>(_ofFeaturesHistory != null ? Math.Max(16, _ofFeaturesHistory.Count) : 512);

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

            Add(_publicActiveVolume);
            _publicActiveVolume.SetLoggerSource(_loggerSource);
            _publicActiveVolume.Filter = 0;

            _dailyLevels.PeriodFrame = DynamicLevels.Period.Daily;
            _dailyLevels.Days = 1;
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

            if (EnableLevelSystem)
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
            _lastImbalanceLogBar = -1;

            // Reset day data
            _previousDayOpen = _previousDayHigh = _previousDayLow = _previousDayClose = 0;
            _currentDayOpen = _currentDayHigh = _currentDayLow = _currentDayClose = 0;

            _tpSlCalculator = new TpSlCalculator();

            _tickSize = InstrumentInfo?.TickSize ?? 0.25m;  // Fallback-Wert, z.B. f?r Gold-Futures (anpassen an dein Instrument!)
            if (_tickSize == 0m)
            {
                this.LogInfo("[OnInitialize] TickSize konnte nicht initialisiert werden ? Fallback auf 0.25 verwendet.");
            }

            // CSV-Export vorbereiten, aber erst im OnCalculate() wirklich initialisieren
            if (EnableCsvExport)
            {
                // Instrumentnamen sicher für Dateiname machen
                string csvInstrumentName = (InstrumentInfo?.Instrument ?? InstrumentInfo?.ToString() ?? "Unknown");
                foreach (var ch in System.IO.Path.GetInvalidFileNameChars())
                    csvInstrumentName = csvInstrumentName.Replace(ch, '_');

                // Ausgabeordner
                string outDir = @"C:\Users\User\Documents\Strategieauswertung";
                System.IO.Directory.CreateDirectory(outDir);

                // Dateiname generieren basierend auf Einstellungen
                string timeframeLabel = "TF";

                if (UseDailyCsvFiles)
                {
                    // NOCH NICHTS erstellen - warten auf erste gültige Bar im OnCalculate()
                    this.LogInfo("[OnInitialize] CSV Export enabled - will be initialized on first valid bar");
                }
                else
                {
                    // Ursprüngliches Verhalten - statischer Dateiname
                    _csvPath = System.IO.Path.Combine(outDir, $"{csvInstrumentName}_{timeframeLabel}_ovsnapshots.csv");

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
            catch
            {
            }



        }


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
            if (UseTick900ForMarketStructure && IsMarketStructureLeader() && _msTick900Aggregator != null && _marketStructureContext != null && args != null)
            {
                if (_msGeneration != Volatile.Read(ref _msGlobalGeneration))
                    return;
                try
                {
                    try { _msLastLiveTradeWallClockUtc = DateTime.UtcNow; } catch { }
                    // Wenn Backfill noch nicht fertig ist, puffern wir die Live-Trades.
                    if (!_msTick900BackfillCompleted)
                    {
                        if (_msTick900LiveTradeBuffer.Count < MsTick900MaxLiveBuffer)
                        {
                            _msTick900LiveTradeBuffer.Add(args);
                        }
                        else
                        {
                            _msTick900BackfillCompleted = true;
                            //this.LogWarn($"[Tick900Backfill:{_msInstanceId}] Live trade buffer overflow ({MsTick900MaxLiveBuffer}). Switching to live Tick900 aggregation without backfill.");
                        }
                    }
                    else
                    {
                        var localTime = NormalizeToChartTime(args.Time);
                        DateTime sessStart;
                        try
                        {
                            sessStart = GetSessionStartTimeForSwingSeed(localTime);
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            sessStart = localTime;
                        }
                        if (_msTick900BucketSessionStart == DateTime.MinValue)
                            _msTick900BucketSessionStart = sessStart;
                        else if (sessStart != _msTick900BucketSessionStart)
                        {
                            // Sessionwechsel: nur die 900-Tick-Buckets neu starten.
                            // MarketStructureContext bleibt bestehen, und _msTick900Bar bleibt monoton steigend.
                            _msTick900Aggregator.Reset();
                            _msTick900BucketSessionStart = sessStart;
                        }

                        decimal incNetDeltaTick = 0m;
                        try
                        {
                            var dir = args.Direction.ToString();
                            if (dir == "Buy")
                                incNetDeltaTick = args.Volume;
                            else if (dir == "Sell")
                                incNetDeltaTick = -args.Volume;
                        }
                        catch { }

                        if (_msTick900Aggregator.AddTrade(localTime, args.Price, args.Volume, incNetDeltaTick, out var closedTickCandle) && closedTickCandle != null)
                        {
                            _msTick900Bar++;
                            RecordSyntheticTick900ClosedCandle(closedTickCandle);
                            var vwapNow = _currentVwapSnapshot?.Current ?? 0m;
                            int maxReadyZoneIdBefore = -1;
                            try
                            {
                                var zonesBefore = _marketStructureContext.ActiveZones;
                                if (zonesBefore != null && zonesBefore.Count > 0)
                                {
                                    for (int zi = 0; zi < zonesBefore.Count; zi++)
                                    {
                                        var z = zonesBefore[zi];
                                        if (z == null)
                                            continue;
                                        if (z.Status != MyNamespace.Strategies.MarketAnalysis.MarketStructureContext.ZoneStatus.Ready)
                                            continue;
                                        if (z.Id > maxReadyZoneIdBefore)
                                            maxReadyZoneIdBefore = z.Id;
                                    }
                                }
                            }
                            catch { maxReadyZoneIdBefore = -1; }
                            try
                            {
                                _marketStructureContext.ZigZagSensitivity = MarketStructureZigZagSensitivity;
                                var r = _marketRegimeDetails != null ? _marketRegimeDetails.Regime : MarketRegime.Normal;
                                _marketStructureContext.WickZoneMinTicks = GetEffectiveMarketStructureWickMinTicks(r);
                            }
                            catch { }
                            _marketStructureContext.Update(
                                _msTick900Bar,
                                closedTickCandle,
                                snapshot: null,
                                tickSize: _tickSize,
                                vwap: vwapNow,
                                recentOf: null,
                                allowZoneCreation: true,
                                allowZoneLifecycle: false);
                            try
                            {
                                int maxReadyZoneIdAfter = -1;
                                var zonesAfter = _marketStructureContext.ActiveZones;
                                if (zonesAfter != null && zonesAfter.Count > 0)
                                {
                                    for (int zi = 0; zi < zonesAfter.Count; zi++)
                                    {
                                        var z = zonesAfter[zi];
                                        if (z == null)
                                            continue;
                                        if (z.Status != MyNamespace.Strategies.MarketAnalysis.MarketStructureContext.ZoneStatus.Ready)
                                            continue;
                                        if (z.Id > maxReadyZoneIdAfter)
                                            maxReadyZoneIdAfter = z.Id;
                                    }
                                }

                                if (maxReadyZoneIdAfter >= 0 && maxReadyZoneIdAfter > maxReadyZoneIdBefore)
                                {
                                    this.LogInfo($"[Tick900-INTRABAR-ZONE-CREATED] Ready zone created (maxReadyId {maxReadyZoneIdAfter} > {maxReadyZoneIdBefore}) msTick900Bar={_msTick900Bar}, chartBar={CurrentBar}");
                                    _lastSeenMarketStructureMaxReadyZoneId = Math.Max(_lastSeenMarketStructureMaxReadyZoneId, maxReadyZoneIdAfter);
                                    
                                    // Setze Dirty-Flag, damit der Intrabar-Pfad in OnCalculate SOFORT anspringt
                                    System.Threading.Interlocked.Exchange(ref _msTick900ZonesDirtyFlag, 1);
                                }
                            }
                            catch { }

                            WriteZonesSnapshot();
                            
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (ex is ArgumentOutOfRangeException)
                        return;
                    this.LogWarn($"[Tick900->MarketStructure] Update failed: {ex}");
                }
            }

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

                if (DailyProfileUseAtasVolume)
                {
                    if (pvi.Volume > 0)
                        vol = pvi.Volume;
                    else if (pvi.Ask > 0 || pvi.Bid > 0)
                        vol = pvi.Ask + pvi.Bid;
                }
                else
                {
                    if (pvi.Ask > 0 || pvi.Bid > 0)
                        vol = pvi.Ask + pvi.Bid;
                    else if (pvi.Volume > 0)
                        vol = pvi.Volume;
                }

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
            decimal valueAreaFraction = 0.70m,
            int minZoneTicksOverride = -1,
            decimal minProminenceOverride = -1m,
            decimal minVolShareOverride = -1m,
            decimal minWidthPctVAOverride = -1m,
            int gapTicksOverride = -1,
            int topNHVNsOverride = -1,
            int topNLVNsOverride = -1,
            int maxDistTicksOverride = -1,
            bool? clampToVAOverride = null,
            bool? enableCapOverride = null,
            int capTicksOverride = -1)
        {
            var mc = new MicroComposite();

            // Guards
            if (hist == null || hist.Count == 0) { this.LogInfo("[BuildMicroCompositeFromHist] MC: hist leer -> return"); return mc; }

            decimal tick = (_tickSize > 0m) ? _tickSize : 0m;
            if (tick <= 0m) { this.LogInfo("[BuildMicroCompositeFromHist] MC: TickSize noch 0/unbekannt -> Abbruch"); return mc; }

            // Men?-Parameter einlesen (Chart-Menu steuert Verhalten)
            int minZoneTicks = Math.Max(1, (minZoneTicksOverride > 0) ? minZoneTicksOverride : this.MinZoneTicks);
            decimal minProminence = Math.Max(0m, (minProminenceOverride >= 0m) ? minProminenceOverride : this.MinProminence);
            decimal minVolShare = Math.Max(0m, (minVolShareOverride >= 0m) ? minVolShareOverride : this.MinVolShare);
            decimal minWidthPctVA = Math.Max(0m, (minWidthPctVAOverride >= 0m) ? minWidthPctVAOverride : this.MinWidthPctVA);
            int gapTicks = Math.Max(0, (gapTicksOverride >= 0) ? gapTicksOverride : this.GapTicks);
            int topNHVNs = Math.Max(1, (topNHVNsOverride > 0) ? topNHVNsOverride : this.TopNHVNs);
            int topNLVNs = Math.Max(1, (topNLVNsOverride > 0) ? topNLVNsOverride : this.TopNLVNs);
            int maxDistTicks = Math.Max(1, (maxDistTicksOverride > 0) ? maxDistTicksOverride : this.MaxDistTicks);
            int menuSmooth = Math.Max(1, (smoothTicks > 0) ? smoothTicks : this.SmoothTicks);

            // NEU: Men?schalter f?r Cap/Klemmung
            bool clampToVA = clampToVAOverride ?? this.ClampZonesToVA;
            bool enableCap = enableCapOverride ?? this.EnableCapZoneWidth;
            int capTicks = Math.Max(1, (capTicksOverride > 0) ? capTicksOverride : this.CapZoneWidthTicks);

            // AwayFromZero wie ATAS
            decimal RoundToTick(decimal p)
            {
                if (tick <= 0m) return p;
                var q = p / tick;
                var r = Math.Round(q, 0, MidpointRounding.AwayFromZero);
                return r * tick;
            }

            SortedDictionary<decimal, decimal> NormalizeHistToTicks(SortedDictionary<decimal, decimal> h, Func<decimal, decimal> roundFunc)
            {
                var n = new SortedDictionary<decimal, decimal>();
                if (h == null) return n;
                foreach (var kv in h)
                {
                    var p = roundFunc(kv.Key);
                    if (!n.ContainsKey(p)) n[p] = kv.Value; else n[p] += kv.Value;
                }
                return n;
            }

            var normAll = NormalizeHistToTicks(hist, RoundToTick);
            var normDev = NormalizeHistToTicks(developingHist, RoundToTick);

            decimal sumAll = normAll.Values.Sum();
            decimal sumDev = normDev.Values.Sum();
            //this.LogInfo($"MC: Start - tick={tick}, Levels(All)={normAll.Count}, Levels(Dev)={normDev.Count}, SumAll={sumAll}, SumDev={sumDev}");
            if (normAll.Count == 0) { this.LogInfo("[BuildMicroCompositeFromHist] MC: normAll leer -> return"); return mc; }

            // Achse min..max
            var minP = normAll.Keys.Min();
            var maxP = normAll.Keys.Max();
            var prices = BuildPriceAxis(minP, maxP, tick);
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
                    if (after < 0m)
                    {
                        this.LogInfo($"⚠ Completed negative at {kv.Key:F2}: was {normCompleted[kv.Key]:F0}, dev={kv.Value:F0}, result clamped to 0");
                        negClamped++;
                        negSum += (-after);
                        after = 0m;
                    }
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

            var raw = prices.Select(p => mc.LevelVols[p]).ToArray();

            var smooth = SmoothTriangularCached(raw, menuSmooth);
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
            if (valleys.Count == 0)
            {
#if DEBUG
                throw new InvalidOperationException("[BuildMC] valleys array is empty - cannot compute prominence");
#else
                    this.LogWarn("[BuildMC] valleys array is empty - skipping HVN scoring");
#endif
                mc.HVNZones = new();
                mc.LVNZones = new();
                mc.HVNs = new();
                mc.LVNs = new();
                return mc;
            }
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
                bool strongPlateau = (volShare >= (minVolShare * 1.8m)) && (widthPctVA >= (minWidthPctVA * 1.8m));
                if (prom < minProminence && !strongPlateau) { hvnRejectedProm++; continue; }
                if (volShare < minVolShare && !strongPlateau) { hvnRejectedVol++; continue; }

                decimal centerPrice = prices[(Lh + Rh) / 2];
                decimal sc = 0.25m * prom + 0.45m * volShare + 0.20m * widthPctVA + 0.10m * DistanceWeight(centerPrice, curPrice);
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
            decimal minWidthPctVA_LVN = Math.Max(0m, minWidthPctVA * 0.25m);
            foreach (var l in lvnIntervals)
            {
                int Ll = l.L, Rl = l.R;
                int width = Rl - Ll + 1;
                decimal widthPctVA = VAWidthTicks > 0 ? (decimal)width / VAWidthTicks : 0m;
                if (width < minZoneTicks) { lvnRejectedWidth++; continue; }
                if (widthPctVA < minWidthPctVA_LVN) { lvnRejectedWidthPct++; continue; }

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

            // HVN/LVN-Zonen füllen
            mc.HVNZones ??= new();
            mc.HVNZones.Clear();
            foreach (var z in hvnIntervals)
            {
                if (z.L < 0 || z.R >= prices.Count || z.L > z.R)
                {
                    var errMsg = $"[BuildMC] HVN interval out of bounds: L={z.L}, R={z.R}, prices.Count={prices.Count}";
#if DEBUG
                    throw new InvalidOperationException(errMsg);
#else
                        this.LogWarn(errMsg);
                        continue;
#endif
                }
                mc.HVNZones.Add((Start: prices[z.L], End: prices[z.R]));
            }

            mc.LVNZones ??= new();
            mc.LVNZones.Clear();
            foreach (var z in lvnFiltered)
            {
                if (z.L < 0 || z.R >= prices.Count || z.L > z.R)
                {
                    var errMsg = $"[BuildMC] LVN interval out of bounds: L={z.L}, R={z.R}, prices.Count={prices.Count}";
#if DEBUG
                    throw new InvalidOperationException(errMsg);
#else
                        this.LogWarn(errMsg);
                        continue;
#endif
                }
                mc.LVNZones.Add((Start: prices[z.L], End: prices[z.R]));
            }

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

            var prices = BuildPriceAxis(minP, maxP, tick);

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
            decimal bestPrice = 0m;
            decimal bestVol = -1m;
            foreach (var kv in hist)
            {
                if (kv.Value > bestVol)
                {
                    bestVol = kv.Value;
                    bestPrice = kv.Key;
                }
            }
            return bestPrice;
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
        // =========================================================================
        // Stacked-Imbalance | Korrigierte Version (V2.1)
        // =========================================================================

        public class StackedImbalanceResult
        {
            // längster Stack irgendwo im Bar
            public int BuyCountMax;
            public int SellCountMax;

            // anchored am Bar-Extrem (direkt unter dem High bzw. über dem Low)
            public int BuyCountTopAnchored;
            public int SellCountBottomAnchored;

            // optional: Preise der anchored Stacks
            public decimal[] BuyTopAnchoredPrices;
            public decimal[] SellBottomAnchoredPrices;

            public int TotalPairs;
            public int BuyPairsCount;
            public int SellPairsCount;

            // 🔴 KORRIGIERT: AvgImbVol basiert jetzt auf qualifizierten Imbalances
            public decimal AvgBuyImbVolQualified;
            public decimal AvgSellImbVolQualified;

            // 🔴 KORRIGIERT: Separate Mediane für Buy- und Sell-Basisvolumen
            public decimal BuyBaseVolMedian;      // Median der Bid-Volumen für Buy-Imbalances
            public decimal SellBaseVolMedian;     // Median der Ask-Volumen für Sell-Imbalances

            // NEU: Qualitäts-Metriken (bereits vorhanden, werden jetzt konsistenter genutzt)
            public decimal BuyVolMedian;      // Median der Buy-Imbalance-Volumes (qualifiziert)
            public decimal SellVolMedian;     // Median der Sell-Imbalance-Volumes (qualifiziert)
            public int QualifiedBuyCount;     // Anzahl Imbalances über MinVol (bereits BuyPairsCount)
            public int QualifiedSellCount;    // Anzahl Imbalances über MinVol (bereits SellPairsCount)

            // 🔴 NEU: Volume-Scores für konsistente Berechnung (wird in ComputeImbalanceScoreV2 gesetzt)
            public decimal VolBuyScore01;     // Volume-Score für Buy-Seite (0-1)
            public decimal VolSellScore01;    // Volume-Score für Sell-Seite (0-1)

            // 🔴 NEU: Weighted-Scores für Debugging und Konsistenz-Prüfung
            public decimal WeightedBuy;       // Weighted Score für Buy-Seite
            public decimal WeightedSell;      // Weighted Score für Sell-Seite

            public decimal ImbalanceScore;
            public string ImbalanceScoreLabel;

            public bool InsufficientLevels;

            // 🟢 NEU: SPATIAL-DELTA-PROPERTIES (MINIMAL!)
            // ════════════════════════════════════════════════════════════════

            /// <summary>
            /// Delta-Ratio OBEN (Top-Zone): 0=100% rot, 0.5=neutral, 1=100% grün
            /// </summary>
            public decimal TopDeltaRatio { get; set; }

            /// <summary>
            /// Delta-Ratio UNTEN (Bottom-Zone): 0=100% rot, 0.5=neutral, 1=100% grün
            /// </summary>
            public decimal BottomDeltaRatio { get; set; }

            /// <summary>
            /// Dominanz oben: "GREEN" (>60%), "RED" (<40%), oder "NEUTRAL"
            /// </summary>
            public string TopDominance { get; set; } = "NEUTRAL";

            /// <summary>
            /// Dominanz unten: "GREEN" (>60%), "RED" (<40%), oder "NEUTRAL"
            /// </summary>
            public string BottomDominance { get; set; } = "NEUTRAL";

            /// <summary>
            /// Gesamtes Netto-Delta der Kerze (Ask-gesamt - Bid-gesamt)
            /// </summary>
            public decimal NetDeltaTotal { get; set; }

            /// <summary>
            /// Ist der Trade ein "Perfect Setup"? (automatisch erkannt)
            /// </summary>
            public bool IsPerfectLongSetup { get; set; }
            public bool IsPerfectShortSetup { get; set; }
            public string PerfectSetupReason { get; set; } = "";

            public decimal UpperWickDeltaRatio { get; set; }
            public decimal LowerWickDeltaRatio { get; set; }
            public string UpperWickDominance { get; set; } = "NEUTRAL";
            public string LowerWickDominance { get; set; } = "NEUTRAL";
            public decimal UpperWickAbsDeltaTotal { get; set; }
            public decimal LowerWickAbsDeltaTotal { get; set; }
        }

        private List<decimal[]> BuildVolumesArrayForBar(int bar)
        {
            var res = new List<decimal[]>();

            var cndl = GetCandle(bar);
            if (cndl == null)
            {
                this.LogWarn($"[BuildVolumesArrayForBar] Bar {bar}: GetCandle returned null");
                return res;
            }

            // TickSize sicher bestimmen (kein InstrumentInfo!)
            decimal ts = InstrumentInfo?.TickSize ?? _tickSize;
            if (ts <= 0m)
            {
                this.LogWarn($"[BuildVolumesArrayForBar] Bar {bar}: Invalid TickSize={ts}, InstrumentInfo.TickSize={InstrumentInfo?.TickSize}, _tickSize={_tickSize}");
                return res;
            }

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

            decimal ratioFactor = 1.0m + (p.ImbalanceRatioPct / 100m);
            res.TotalPairs = n - 1;

            var buyImb = new bool[n];
            var sellImb = new bool[n];

            var allBidVolumesForBuyReference = new List<decimal>();
            var allAskVolumesForSellReference = new List<decimal>();
            var buyImbVolumesQualified = new List<decimal>();
            var sellImbVolumesQualified = new List<decimal>();

            // 🟢 NEU: Delta-Tracking nach Zonen
            decimal topGreenDelta = 0m;
            decimal topRedDelta = 0m;
            decimal bottomGreenDelta = 0m;
            decimal bottomRedDelta = 0m;
            decimal totalGreenDelta = 0m;
            decimal totalRedDelta = 0m;

            decimal upperWickGreenDelta = 0m;
            decimal upperWickRedDelta = 0m;
            decimal lowerWickGreenDelta = 0m;
            decimal lowerWickRedDelta = 0m;

            var cndl = GetCandle(bar);
            decimal low = cndl.Low;
            decimal high = cndl.High;
            decimal range = high - low;
            decimal threshold33 = low + (range / 3m);
            decimal threshold66 = low + (2m * range / 3m);

            decimal bodyLow = Math.Min(cndl.Open, cndl.Close);
            decimal bodyHigh = Math.Max(cndl.Open, cndl.Close);

            // ═══════════════════════════════════════════════════════════════
            // DIAGONALE ITERATION (wie zuvor)
            // ═══════════════════════════════════════════════════════════════

            for (int i = 0; i < n - 1; i++)
            {
                decimal price = vols[i][0];
                decimal bidUnten = vols[i][1];
                decimal askUnten = vols[i][2];
                decimal bidOben = vols[i + 1][1];
                decimal askOben = vols[i + 1][2];

                allBidVolumesForBuyReference.Add(bidUnten);
                allAskVolumesForSellReference.Add(askUnten);

                // 🟢 NEU: Berechne Delta pro Level und akkumuliere nach Zone
                decimal delta = askUnten - bidUnten;  // Ask (aggressive Käufe) - Bid (aggressive Verkäufe)

                // Bestimme Zone basierend auf UNTEREN Level-Preis
                bool isTopZone = price > threshold66;
                bool isBottomZone = price < threshold33;

                bool isUpperWick = price > bodyHigh;
                bool isLowerWick = price < bodyLow;

                if (delta > 0)
                {
                    totalGreenDelta += delta;
                    if (isTopZone) topGreenDelta += delta;
                    if (isBottomZone) bottomGreenDelta += delta;
                    if (isUpperWick) upperWickGreenDelta += delta;
                    if (isLowerWick) lowerWickGreenDelta += delta;
                }
                else if (delta < 0)
                {
                    totalRedDelta += Math.Abs(delta);
                    if (isTopZone) topRedDelta += Math.Abs(delta);
                    if (isBottomZone) bottomRedDelta += Math.Abs(delta);
                    if (isUpperWick) upperWickRedDelta += Math.Abs(delta);
                    if (isLowerWick) lowerWickRedDelta += Math.Abs(delta);
                }

                // Imbalance-Erkennung (wie zuvor)
                if (askOben > (bidUnten * ratioFactor))
                {
                    if (askOben >= p.ImbalanceVolumeMin)
                    {
                        buyImb[i] = true;
                        buyImbVolumesQualified.Add(askOben);
                    }
                }

                if (bidUnten > (askOben * ratioFactor))
                {
                    if (bidUnten >= p.ImbalanceVolumeMin)
                    {
                        sellImb[i] = true;
                        sellImbVolumesQualified.Add(bidUnten);
                    }
                }
            }

            // ═══════════════════════════════════════════════════════════════
            // SPATIAL-DELTA-RATIOS BERECHNEN
            // ═══════════════════════════════════════════════════════════════

            decimal topTotal = topGreenDelta + topRedDelta;
            res.TopDeltaRatio = topTotal > 0 ? topGreenDelta / topTotal : 0.5m;
            res.TopDominance = res.TopDeltaRatio > 0.6m ? "GREEN"
                         : res.TopDeltaRatio < 0.4m ? "RED"
                         : "NEUTRAL";

            decimal bottomTotal = bottomGreenDelta + bottomRedDelta;
            res.BottomDeltaRatio = bottomTotal > 0 ? bottomGreenDelta / bottomTotal : 0.5m;
            res.BottomDominance = res.BottomDeltaRatio > 0.6m ? "GREEN"
                            : res.BottomDeltaRatio < 0.4m ? "RED"
                            : "NEUTRAL";

            // Netto-Delta
            res.NetDeltaTotal = totalGreenDelta - totalRedDelta;

            decimal upperWickTotal = upperWickGreenDelta + upperWickRedDelta;
            res.UpperWickAbsDeltaTotal = upperWickTotal;
            res.UpperWickDeltaRatio = upperWickTotal > 0 ? upperWickGreenDelta / upperWickTotal : 0.5m;
            res.UpperWickDominance = res.UpperWickDeltaRatio > 0.6m ? "GREEN"
                                  : res.UpperWickDeltaRatio < 0.4m ? "RED"
                                  : "NEUTRAL";

            decimal lowerWickTotal = lowerWickGreenDelta + lowerWickRedDelta;
            res.LowerWickAbsDeltaTotal = lowerWickTotal;
            res.LowerWickDeltaRatio = lowerWickTotal > 0 ? lowerWickGreenDelta / lowerWickTotal : 0.5m;
            res.LowerWickDominance = res.LowerWickDeltaRatio > 0.6m ? "GREEN"
                                  : res.LowerWickDeltaRatio < 0.4m ? "RED"
                                  : "NEUTRAL";

            // ═══════════════════════════════════════════════════════════════
            // PERFECT SETUP ERKENNUNG (MINIMAL!)
            // ═══════════════════════════════════════════════════════════════

            // 🟢 PERFECT LONG: Red-Imbalances-Bottom + Green-Delta-Top
            if (res.SellCountBottomAnchored >= 2 &&
                res.BottomDominance == "RED" &&
                res.TopDominance == "GREEN")
            {
                res.IsPerfectLongSetup = true;
                res.PerfectSetupReason = $"Sell-Imb({res.SellCountBottomAnchored}) Bottom-Red + Top-Green";
            }

            // 🔴 PERFECT SHORT: Green-Imbalances-Top + Red-Delta-Bottom
            if (res.BuyCountTopAnchored >= 2 &&
                res.TopDominance == "GREEN" &&
                res.BottomDominance == "RED")
            {
                res.IsPerfectShortSetup = true;
                res.PerfectSetupReason = $"Buy-Imb({res.BuyCountTopAnchored}) Top-Green + Bottom-Red";
            }

            // Rest wie zuvor (Anchored, Scores, etc.)
            res.BuyCountMax = LongestConsecutiveTrue(buyImb);
            res.SellCountMax = LongestConsecutiveTrue(sellImb);

            res.BuyPairsCount = buyImbVolumesQualified.Count;
            res.SellPairsCount = sellImbVolumesQualified.Count;
            res.QualifiedBuyCount = buyImbVolumesQualified.Count;
            res.QualifiedSellCount = sellImbVolumesQualified.Count;

            res.AvgBuyImbVolQualified = buyImbVolumesQualified.Count > 0
                ? buyImbVolumesQualified.Average()
                : 0m;
            res.AvgSellImbVolQualified = sellImbVolumesQualified.Count > 0
                ? sellImbVolumesQualified.Average()
                : 0m;

            res.BuyVolMedian = ComputeMedian(buyImbVolumesQualified);
            res.SellVolMedian = ComputeMedian(sellImbVolumesQualified);

            res.BuyBaseVolMedian = ComputeMedian(allBidVolumesForBuyReference);
            res.SellBaseVolMedian = ComputeMedian(allAskVolumesForSellReference);

            // Anchored Calculation
            res.BuyCountTopAnchored = 0;
            var topBuyPrices = new List<decimal>();
            int maxPairsTop = Math.Max(0, Math.Min(p.MaxDepthTicksAnchored, n - 1));
            for (int k = 0; k < maxPairsTop; k++)
            {
                int i = (n - 2) - k;
                if (i < 0) break;
                if (buyImb[i])
                {
                    res.BuyCountTopAnchored++;
                    topBuyPrices.Add(vols[i + 1][0]);
                }
                else break;
            }
            res.BuyTopAnchoredPrices = topBuyPrices.ToArray();

            res.SellCountBottomAnchored = 0;
            var bottomSellPrices = new List<decimal>();
            int maxPairsBottom = Math.Max(0, Math.Min(p.MaxDepthTicksAnchored, n - 1));
            for (int i = 0; i < maxPairsBottom; i++)
            {
                if (i >= n - 1) break;
                if (sellImb[i])
                {
                    res.SellCountBottomAnchored++;
                    bottomSellPrices.Add(vols[i][0]);
                }
                else break;
            }
            res.SellBottomAnchoredPrices = bottomSellPrices.ToArray();

            // Score berechnen
            res.ImbalanceScore = ComputeImbalanceScoreV2(res, p, out var scoreLabel);
            res.ImbalanceScoreLabel = scoreLabel;

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

        /// <summary>
        /// Verbesserte Score-Berechnung mit nicht-linearer Volume-Skalierung
        /// </summary>
        private decimal ComputeImbalanceScoreV2(StackedImbalanceResult result, StackedImbParams p, out string label)
        {
            // Gewichtungen (konfigurierbar machen falls gewünscht)
            const decimal wCoverage = 0.40m;   // Coverage-Anteil
            const decimal wAnchored = 0.35m;   // Anchored wichtiger für Reversals
            const decimal wVolume = 0.25m;     // Volume als Bestätigung
            const decimal eps = 1e-9m;

            if (result.TotalPairs <= 1)
            {
                label = "insufficient";
                return 0m;
            }

            // =====================================================================
            // 1. COVERAGE SCORE: Anteil der Imbalance-Paare am Gesamtbar
            // =====================================================================
            decimal coverageBuy = Clamp01Local((decimal)result.BuyCountMax / result.TotalPairs);
            decimal coverageSell = Clamp01Local((decimal)result.SellCountMax / result.TotalPairs);

            // =====================================================================
            // 2. ANCHORED SCORE: Wie tief ist der Stack am Extrem verankert
            // =====================================================================
            decimal anchoredBuy = 0m;
            decimal anchoredSell = 0m;

            if (p.MaxDepthTicksAnchored > 0)
            {
                // KORRIGIERT: Nicht-lineare Skalierung - erste Levels wichtiger
                anchoredBuy = ComputeAnchoredScore(result.BuyCountTopAnchored, p.MaxDepthTicksAnchored);
                anchoredSell = ComputeAnchoredScore(result.SellCountBottomAnchored, p.MaxDepthTicksAnchored);
            }

            // 🔴 CRITICAL DEBUG - Anchored Werte SOFORT nach Berechnung
            // string fmtDec6(decimal v) => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            // this.LogWarn($"[ComputeImbalanceScoreV2-ANCHORED-RAW] " +
            //              $"anchoredBuy={fmtDec6(anchoredBuy)}, anchoredSell={fmtDec6(anchoredSell)} (BEFORE any modifications)");

            // =====================================================================
            // 3. VOLUME SCORE: Wie stark sind die Imbalances im Vergleich zum Basis-Volumen
            // =====================================================================
            decimal volBuyScore01 = ComputeVolumeScore(result.AvgBuyImbVolQualified, result.BuyBaseVolMedian, p);
            decimal volSellScore01 = ComputeVolumeScore(result.AvgSellImbVolQualified, result.SellBaseVolMedian, p);

            // =====================================================================
            // 4. FINALE SCORE-BERECHNUNG
            // =====================================================================
            decimal weightedBuy = (wCoverage * coverageBuy)
                                + (wAnchored * anchoredBuy)
                                + (wVolume * volBuyScore01);

            decimal weightedSell = (wCoverage * coverageSell)
                                + (wAnchored * anchoredSell)
                                + (wVolume * volSellScore01);

            // 🔴 NEU: Speichere die Volume-Scores und Weighted-Scores im Result
            result.VolBuyScore01 = volBuyScore01;
            result.VolSellScore01 = volSellScore01;
            result.WeightedBuy = weightedBuy;
            result.WeightedSell = weightedSell;

            // 🔴 CRITICAL DEBUG
            // fmtDec6 ist bereits oben definiert (Zeile 4990) - keine erneute Definition nötig

            // this.LogWarn($"[ComputeImbalanceScoreV2-WEIGHTS] " +
            //              $"coverageBuy={fmtDec6(coverageBuy)}, coverageSell={fmtDec6(coverageSell)} | " +
            //              $"anchoredBuy={fmtDec6(anchoredBuy)}, anchoredSell={fmtDec6(anchoredSell)} | " +
            //              $"volBuyScore01={fmtDec6(volBuyScore01)}, volSellScore01={fmtDec6(volSellScore01)}");

            // this.LogWarn($"[ComputeImbalanceScoreV2-CALC] " +
            //              $"wCoverage({fmtDec6(wCoverage)} * {fmtDec6(coverageBuy)}) + " +
            //              $"wAnchored({fmtDec6(wAnchored)} * {fmtDec6(anchoredBuy)}) + " +
            //              $"wVolume({fmtDec6(wVolume)} * {fmtDec6(volBuyScore01)}) = {fmtDec6(weightedBuy)}");

            // this.LogWarn($"[ComputeImbalanceScoreV2-CALC] " +
            //              $"wCoverage({fmtDec6(wCoverage)} * {fmtDec6(coverageSell)}) + " +
            //              $"wAnchored({fmtDec6(wAnchored)} * {fmtDec6(anchoredSell)}) + " +
            //              $"wVolume({fmtDec6(wVolume)} * {fmtDec6(volSellScore01)}) = {fmtDec6(weightedSell)}");

            // this.LogWarn($"[ComputeImbalanceScoreV2-FINAL] " +
            //              $"anchoredBuy={fmtDec6(anchoredBuy)} (used in calc), " +
            //              $"anchoredSell={fmtDec6(anchoredSell)} (used in calc)");

            decimal totalWeight = wCoverage + wAnchored + wVolume;
            decimal raw = weightedBuy - weightedSell;
            decimal score = raw / totalWeight;
            score = ClampLocal(score, -1m, 1m);

            // =====================================================================
            // 5. LABEL BESTIMMUNG mit Hysterese
            // =====================================================================
            label = DetermineScoreLabel(score, result);

            return score;
        }

        /// <summary>
        /// Nicht-lineare Anchored-Score-Berechnung
        /// Erste Levels am Extrem sind wichtiger als tiefere
        /// </summary>
        private decimal ComputeAnchoredScore(int anchoredCount, int maxDepth)
        {
            if (maxDepth <= 0 || anchoredCount <= 0) return 0m;

            // Logarithmische Skalierung: Erste Levels zählen mehr
            // 1 Level  → ~0.43
            // 2 Levels → ~0.63
            // 3 Levels → ~0.76
            // 4 Levels → ~0.86
            // 5 Levels → ~0.93
            decimal ratio = (decimal)anchoredCount / maxDepth;
            decimal logScore = (decimal)Math.Log(1.0 + (double)ratio * (Math.E - 1.0));

            return Clamp01Local(logScore);
        }

        /// <summary>
        /// KORRIGIERTE Volume-Score-Berechnung mit Threshold und nicht-linearer Skalierung
        /// </summary>
        private decimal ComputeVolumeScore(decimal avgImbVol, decimal baseMedian, StackedImbParams p)
        {
            const decimal eps = 1e-9m;

            // Kein Imbalance-Volumen → Score 0
            if (avgImbVol <= 0m) return 0m;

            // Basis-Median zu niedrig → verwende MinVol als Referenz
            // 🔴 KORRIGIERT: Nutze p.ImbalanceVolumeMin nur, wenn BaseMedian <= eps.
            // Ansonsten sollte der Median die Referenz sein.
            decimal reference = baseMedian;
            if (reference <= eps) reference = p.ImbalanceVolumeMin;
            if (reference <= eps) return 0m; // Falls beides 0 ist

            decimal ratio = avgImbVol / reference;

            // 🔴 DEBUG
            // string fmtDec6(decimal v) => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            // this.LogWarn($"[ComputeVolumeScore] avgImbVol={fmtDec6(avgImbVol)}, " +
            //              $"baseMedian={fmtDec6(baseMedian)}, reference={fmtDec6(reference)}, " +
            //              $"ratio={fmtDec6(ratio)}, ImbalanceVolumeMin={fmtDec6(p.ImbalanceVolumeMin)}");

            // =====================================================================
            // NEUE LOGIK: Threshold-basierte Skalierung
            // =====================================================================
            //
            // ratio < 1.0  → Score = 0 (unter Referenz, nicht aussagekräftig)
            // ratio = 1.0  → Score = 0 (genau Referenz, neutral)
            // ratio = 1.5  → Score ≈ 0.25
            // ratio = 2.0  → Score ≈ 0.50
            // ratio = 3.0  → Score ≈ 0.75
            // ratio >= 4.0 → Score = 1.0 (Maximum)
            //
            // Formel: score = (ratio - 1) / 3, clamped to [0, 1]
            // Das bedeutet: 4x das Basisvolumen = maximaler Score
            // =====================================================================

            if (ratio <= 1.0m)
            {
                return 0m;  // Unter oder gleich Referenz → kein positiver Beitrag
            }

            // Lineare Skalierung ab Threshold 1.0, Maximum bei 4.0
            decimal score = (ratio - 1.0m) / 3.0m;

            // this.LogWarn($"[ComputeVolumeScore] score(pre-clamp)={fmtDec6(score)}, " +
            //              $"score(post-clamp)={fmtDec6(Clamp01Local(score))}");

            return Clamp01Local(score);
        }

        /// <summary>
        /// Label-Bestimmung mit Kontext-Bewertung
        /// </summary>
        private string DetermineScoreLabel(decimal score, StackedImbalanceResult result)
        {
            // Mindestanforderungen für starke Signale
            bool hasBuyStack = result.BuyCountMax >= 2 || result.BuyCountTopAnchored >= 2;
            bool hasSellStack = result.SellCountMax >= 2 || result.SellCountBottomAnchored >= 2;

            if (score >= 0.5m && hasBuyStack)
                return "strong_buy";
            if (score >= 0.25m && hasBuyStack)
                return "buy";
            if (score >= 0.15m)
                return "weak_buy";

            if (score <= -0.5m && hasSellStack)
                return "strong_sell";
            if (score <= -0.25m && hasSellStack)
                return "sell";
            if (score <= -0.15m)
                return "weak_sell";

            return "neutral";
        }

        private static decimal Clamp01Local(decimal value) => ClampLocal(value, 0m, 1m);

        private static decimal ClampLocal(decimal value, decimal min, decimal max)
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
            {
                if (!EnableImbalanceScoreCsvExport || !UseDailyCsvFiles)
                    return;
            }

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
            }

            if (EnableImbalanceScoreCsvExport && _imbScoreCsvWriter == null)
            {
                if (CurrentBar >= 0)
                {
                    this.LogInfo("[CheckAndRecreateCsvWriterIfNeeded] ImbalanceScore CSV Writer will be created on first valid bar");
                    InitializeDailyImbalanceScoreCsvWriter();
                }
                else
                {
                    return;
                }
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

                string timeframeLabel = "TF";
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

        private void InitializeDailyImbalanceScoreCsvWriter()
        {
            try
            {
                string backtestDate = GetBacktestDateFromFirstBar();
                string instrumentName = (InstrumentInfo?.Instrument ?? InstrumentInfo?.ToString() ?? "Unknown");
                foreach (var ch in System.IO.Path.GetInvalidFileNameChars())
                    instrumentName = instrumentName.Replace(ch, '_');

                string timeframeLabel = "TF";
                string outDir = @"C:\Users\User\Documents\Strategieauswertung";
                System.IO.Directory.CreateDirectory(outDir);

                _imbScoreCsvPath = System.IO.Path.Combine(outDir, $"{instrumentName}_{timeframeLabel}_imbalance_score_{backtestDate}.csv");

                if (OverwriteExistingCsv && System.IO.File.Exists(_imbScoreCsvPath))
                {
                    try
                    {
                        System.IO.File.Delete(_imbScoreCsvPath);
                        this.LogInfo($"[InitializeDailyImbalanceScoreCsvWriter] Existing CSV file deleted: {_imbScoreCsvPath}");
                    }
                    catch (Exception ex)
                    {
                        this.LogWarn($"[InitializeDailyImbalanceScoreCsvWriter] Could not delete existing CSV file {_imbScoreCsvPath}: {ex.Message}");
                    }
                }

                _imbScoreCsvWriter = new BackgroundCsvWriter(_imbScoreCsvPath, ImbalanceScoreCsvHeader);
                this.LogInfo($"[InitializeDailyImbalanceScoreCsvWriter] CSV Writer created -> path={_imbScoreCsvPath}");
            }
            catch (Exception ex)
            {
                this.LogError($"[InitializeDailyImbalanceScoreCsvWriter] Failed to initialize CSV writer: {ex.Message}");
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

            // WICHTIG: speichere die Zeit BEIM ERSTEN AUFRUF und verwende sie für die gesamte Session
            try
            {
                this.LogInfo("[GetStoredBacktestDate] INFO: No stored date available, capturing current bar time");

                // Versuche, die Zeit von der aktuellen Bar zu bekommen
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
            return fallback;
        }

        // Hauptlogik pro Bar und pro Tick auf dem letzten Bar
        protected override void OnCalculate(int bar, decimal value)
        {
            try
            {
                _lastOnCalculateBar = bar;
                _lastOnCalculateValue = value;
                var c = GetCandle(bar);
                if (c == null) // Explizite Null-Pr?fung f?r aktuelle Kerze
                {
                    this.LogWarn($"OnCalculate: Aktuelle Kerze f?r Bar {bar} ist null. ?berspringe weitere Verarbeitung.");
                    return;
                }

                if (UseTick900ForMarketStructure && !_msLeaderElectionAttempted)
                    TryAcquireMarketStructureLeadership();

                

                if (UseTick900ForMarketStructure)
                    EnsureTick900BackfillRequested(bar);

                int maxIdx = CurrentBar;

                if (bar < 0 || maxIdx < 0 || bar > maxIdx || InstrumentInfo == null || InstrumentInfo.Instrument == null || _tickSize == 0m)
                {
                    this.LogInfo($"OnCalculate: Ungültiger Zustand (bar={bar}, maxIdx={maxIdx}, InstrumentInfo={InstrumentInfo != null}, Instrument={InstrumentInfo?.Instrument != null}, _tickSize={_tickSize}) -> überspringe.");
                    return;
                }

                // Tageswechsel prüfen und ggf. neuen Writer erstellen
                CheckAndRecreateCsvWriterIfNeeded();

                int b = bar;

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

                var mcRolling = GetRollingMicroComposite();
                bool sameRef = object.ReferenceEquals(_currentMC, mcRolling);
                int hvnZonesCur = _currentMC?.HVNZones?.Count ?? 0;
                int lvnZonesCur = _currentMC?.LVNZones?.Count ?? 0;
                int hvnZonesRoll = mcRolling?.HVNZones?.Count ?? 0;
                int lvnZonesRoll = mcRolling?.LVNZones?.Count ?? 0;
                decimal tickDbg = InstrumentInfo?.TickSize ?? _tickSize;

                string ZonesToString(List<(decimal Start, decimal End)> zs)
                {
                    if (zs == null || zs.Count == 0) return "-";
                    int take = Math.Min(zs.Count, 6);
                    var parts = new List<string>(take);
                    for (int i = 0; i < take; i++)
                        parts.Add($"[{zs[i].Start:F2}-{zs[i].End:F2}]");
                    return string.Join(" ", parts);
                }

                string hvnCurStr = ZonesToString(_currentMC?.HVNZones);
                string hvnRollStr = ZonesToString(mcRolling?.HVNZones);
                //this.LogInfo($"[MC-SNAP] barClosed={mcBaseBar} tick={tickDbg:F2} sameRef={sameRef} | cur: POC={_currentMC?.POC:F2} VAH={_currentMC?.VAH:F2} VAL={_currentMC?.VAL:F2} HVNZones={hvnZonesCur} {hvnCurStr} LVNZones={lvnZonesCur} | roll: POC={mcRolling?.POC:F2} VAH={mcRolling?.VAH:F2} VAL={mcRolling?.VAL:F2} HVNZones={hvnZonesRoll} {hvnRollStr} LVNZones={lvnZonesRoll}");

                // Feature auf abgeschlossener Kerze ausf?hren
                // Z?hler f?r die NEUE Kerze zur?cksetzen
                _currentBarBuyTrades = 0;
                _currentBarSellTrades = 0;

                // WICHTIG: lastCalculatedBar fortschreiben
                _lastCalculatedBar = bar;
            }
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


            // 11) Stacked Imbalance auf zuletzt geschlossener Kerze (bar-1)
            int imbBar = Math.Max(0, b - 1);

            var pImb = new StackedImbParams
            {
                ImbalanceRatioPct = _imbalanceRatioPct,
                ImbalanceVolumeMin = _imbalanceVolumeMin,
                IgnoreZeroValues = _imbIgnoreZeroValues,
                MaxDepthTicksAnchored = _imbMaxDepthTicksAnchored
            };
            var r = ComputeStackedImbalanceForBar(imbBar, pImb);

            // Schreibe Ergebnisse
            _stackedBuyImbCount[imbBar] = (decimal)r.BuyCountMax;
            _stackedSellImbCount[imbBar] = (decimal)r.SellCountMax;
            _stackedBuyImbTopCount[imbBar] = (decimal)r.BuyCountTopAnchored;
            _stackedSellImbBottomCount[imbBar] = (decimal)r.SellCountBottomAnchored;
            _imbalanceScoreSeries[imbBar] = r.ImbalanceScore;
            _imbalanceScoreLabelSeries[imbBar] = r.ImbalanceScoreLabel ?? string.Empty;

            // -------------------------------------------------------------------------
            // Structured Imbalance-Score Log (für Debugging und Verifizierung)
            // -------------------------------------------------------------------------

            // ✅ KORREKT: Verwende exakt die gleichen Konstanten wie in ComputeImbalanceScoreV2()
            const decimal wCoverage = 0.40m;
            const decimal wAnchored = 0.35m;
            const decimal wVolume = 0.25m;
            const decimal totalWeight = wCoverage + wAnchored + wVolume; // = 1.0m

            // 🔴 KORRIGIERT: Berechne Coverage basierend auf den bereits qualifizierten Max-Counts
            decimal totalPairs = Math.Max(1, r.TotalPairs);
            decimal coverageBuy = ClampLocal((decimal)r.BuyCountMax / totalPairs, 0m, 1m);
            decimal coverageSell = ClampLocal((decimal)r.SellCountMax / totalPairs, 0m, 1m);

            // Anchored-Scores (diese MÜSSEN mit ComputeImbalanceScoreV2 identisch sein!)
            decimal anchoredBuy = ComputeAnchoredScore(r.BuyCountTopAnchored, _imbMaxDepthTicksAnchored);
            decimal anchoredSell = ComputeAnchoredScore(r.SellCountBottomAnchored, _imbMaxDepthTicksAnchored);

            // 🔴 WICHTIG: Verwende die bereits berechneten Volume-Scores und Weighted-Scores aus dem Result!
            // Diese wurden in ComputeImbalanceScoreV2 berechnet und gespeichert.
            decimal volBuyScore01 = r.VolBuyScore01;
            decimal volSellScore01 = r.VolSellScore01;
            decimal weightedBuy = r.WeightedBuy;
            decimal weightedSell = r.WeightedSell;

            // ✅ KORREKT: Verwende die gespeicherten Weighted-Scores direkt
            // Keine Neuberechnung mehr nötig!

            // ✅ KORREKT: Finale Score-Berechnung (EXAKT wie in ComputeImbalanceScoreV2)
            decimal calculatedScore = (weightedBuy - weightedSell) / totalWeight;
            calculatedScore = ClampLocal(calculatedScore, -1m, 1m);

            // 🔴 KRITISCH: Überprüfe auf Mismatch
            decimal tolerance = 0.000001m;
            if (Math.Abs(calculatedScore - r.ImbalanceScore) > tolerance)
            {
                this.LogWarn($"[ImbalanceScore Mismatch] Calculated={calculatedScore:F6}, " +
                             $"Actual={r.ImbalanceScore:F6}, Diff={Math.Abs(calculatedScore - r.ImbalanceScore):F6}, " +
                             $"volBuyScore01={volBuyScore01:F6}, volSellScore01={volSellScore01:F6}, " +
                             $"coverageBuy={coverageBuy:F6}, coverageSell={coverageSell:F6}, " +
                             $"anchoredBuy={anchoredBuy:F6}, anchoredSell={anchoredSell:F6}");
            }

            // Für volBuyNorm/volSellNorm (nur für Logging)
            decimal volBuyNorm = r.BuyBaseVolMedian > 0m ? r.AvgBuyImbVolQualified / (r.BuyBaseVolMedian + 1e-6m) : 0m;
            decimal volSellNorm = r.SellBaseVolMedian > 0m ? r.AvgSellImbVolQualified / (r.SellBaseVolMedian + 1e-6m) : 0m;

            string fmtDec6Local(decimal v) => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

            var imbalanceLog =
                $"[ImbalanceScore] {{" +
                $"\"BarIndex\":{imbBar},\"TotalPairs\":{r.TotalPairs}," +
                $"\"BuyMax\":{r.BuyCountMax},\"SellMax\":{r.SellCountMax}," +
                $"\"BuyAnch\":{r.BuyCountTopAnchored},\"SellAnch\":{r.SellCountBottomAnchored}," +
                $"\"BuyPairs\":{r.BuyPairsCount},\"SellPairs\":{r.SellPairsCount}," +
                $"\"AvgBuyVol\":{fmtDec6Local(r.AvgBuyImbVolQualified)},\"AvgSellVol\":{fmtDec6Local(r.AvgSellImbVolQualified)}," +
                $"\"BuyBaseVol\":{fmtDec6Local(r.BuyBaseVolMedian)},\"SellBaseVol\":{fmtDec6Local(r.SellBaseVolMedian)}," +
                $"\"coverageBuy\":{fmtDec6Local(coverageBuy)},\"coverageSell\":{fmtDec6Local(coverageSell)}," +
                $"\"anchoredBuy\":{fmtDec6Local(anchoredBuy)},\"anchoredSell\":{fmtDec6Local(anchoredSell)}," +
                $"\"volBuyNorm\":{fmtDec6Local(volBuyNorm)},\"volSellNorm\":{fmtDec6Local(volSellNorm)}," +
                $"\"volBuyScore01\":{fmtDec6Local(volBuyScore01)},\"volSellScore01\":{fmtDec6Local(volSellScore01)}," +
                $"\"weightedBuy\":{fmtDec6Local(weightedBuy)},\"weightedSell\":{fmtDec6Local(weightedSell)}," +
                $"\"calculatedScore\":{fmtDec6Local(calculatedScore)}," +
                $"\"score\":{fmtDec6Local(r.ImbalanceScore)},\"label\":\"{r.ImbalanceScoreLabel}\"" +
                $"}}";

            if (imbBar != _lastImbalanceLogBar)
            {
                // 🔴 NUR IM ENTRY-FENSTER LOGGEN: Prüfe ob wir uns in der Pattern-Prüfung befinden
                // Wir verwenden einen einfachen Check: Nur loggen wenn der aktuelle Bar innerhalb der letzten 10 Bars ist
                // und wir nicht in der historischen Datensammlung sind (history mode)
                bool isInEntryWindow = (CurrentBar - imbBar) <= 10 && CurrentBar > 0;

                if (isInEntryWindow)
                {
                    var msg = imbalanceLog.Replace("{", "{{").Replace("}", "}}");
                    if (!ShouldSuppressNoisyLog(msg))
                        this.LogInfo(msg);
                }
                _lastImbalanceLogBar = imbBar;
            }

            if (EnableImbalanceScoreCsvExport && _imbScoreCsvWriter != null && imbBar >= 0)
            {
                try
                {
                    var imbCandle = GetCandle(imbBar);
                    string timeIso = imbCandle != null
                        ? imbCandle.Time.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                        : string.Empty;
                    string fmtDec3Local(decimal v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

                    string line = string.Join(",",
                        imbBar.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        timeIso,
                        fmtDec6Local(r.ImbalanceScore),
                        r.ImbalanceScoreLabel ?? string.Empty,
                        r.TotalPairs.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        r.BuyCountMax.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        r.SellCountMax.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        r.BuyPairsCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        r.SellPairsCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        fmtDec6Local(r.AvgBuyImbVolQualified),
                        fmtDec6Local(r.AvgSellImbVolQualified),
                        fmtDec6Local(r.BuyBaseVolMedian),
                        fmtDec6Local(r.SellBaseVolMedian),
                        fmtDec3Local(coverageBuy),
                        fmtDec3Local(coverageSell),
                        fmtDec3Local(anchoredBuy),
                        fmtDec3Local(anchoredSell),
                        fmtDec3Local(volBuyNorm),
                        fmtDec3Local(volSellNorm),
                        fmtDec3Local(weightedBuy),
                        fmtDec3Local(weightedSell)
                    );

                    _imbScoreCsvWriter.EnqueueLine(line);
                }
                catch (Exception ex)
                {
                    this.LogWarn($"[ImbalanceScore CSV] Failed to enqueue line for bar={imbBar}: {ex.Message}");
                }
            }



            _currentCandleData = c;


            bool stopTick = false; // statt fr?her returns

            // === Tickgenaue Armed-Entry-Ausl?sung (VOR dem Guard) ===
            if (EnableIntrabarEntry && _entryState == EntryState.TouchArmed && !HasLiveEntryOrder())
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
                    decimal lastTrade = Security?.LastTradePrice ?? 0m;
                    decimal triggerCheckPriceLong = lastTrade > 0m ? lastTrade : (Security?.BestAskPrice ?? 0m);
                    decimal triggerCheckPriceShort = lastTrade > 0m ? lastTrade : (Security?.BestBidPrice ?? 0m);

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


            // === Tick-leichte Trade-Management-Updates (m?ssen auch laufen, wenn heavy-work f?r diesen Bar bereits gelaufen ist) ===
            TrackBestBidAskFreeze();

            // 1) BestSinceEntry aktualisieren (pro Tick, intrabar)
            if (_positionOpen)
            {
                var last = Security?.LastTradePrice ?? 0m;
                if (last > 0m)
                {
                    if (_isLongTrade) _bestSinceEntry = Math.Max(_bestSinceEntry, last);
                    else _bestSinceEntry = Math.Min(_bestSinceEntry, last);
                    //this.LogInfo($"[OnCalculate] Tick-Update: Last={last}, BestSinceEntry={_bestSinceEntry} (Long={_isLongTrade})");
                }

                // 2) Manager-Init intrabar erlauben (nicht an Heavy-Work binden)
                // Hintergrund: wenn der Entry intrabar erfolgt und der Heavy-Work Teil f?r diesen Bar bereits gelaufen ist,
                // w?rde _managersInitialized sonst erst beim n?chsten Bar gesetzt und BreakEven kann nicht intrabar arbeiten.
                if (!_managersInitialized && !_isExitPlacementPending && _slOrder != null && _tpOrder != null)
                {
                    try
                    {
                        InitializeManagersAfterEntry(_lastTpSlResult);
                        this.LogInfo("[OnCalculate] Managers initialized in tick-light section.");
                    }
                    catch (Exception ex)
                    {
                        this.LogWarn($"[OnCalculate] Tick-light InitializeManagersAfterEntry failed: {ex.Message}");
                        _managersInitialized = false;
                    }
                }

                // 3) BreakEven pro Tick ausf?hren, sobald Manager initialisiert sind
                // (Manager-Init bleibt weiter unten im heavy-work Teil)
                if (_managersInitialized && !_isExitPlacementPending && EnableBreakEven)
                {
                    //this.LogInfo($"[OnCalculate] BreakEven-Conditions: PosOpen={_positionOpen}, ManagersInit={_managersInitialized}, ExitPending={_isExitPlacementPending}, BE={EnableBreakEven}");
                    //this.LogInfo("[OnCalculate] Calling ProcessBreakEvenTick() per tick.");
                    try { ProcessBreakEvenTick(); }
                    catch (Exception ex) { this.LogWarn($"[OnCalculate] ProcessBreakEvenTick Exception: {ex.Message}"); }
                }
            }

            // Früher Guard: schweren Teil ggf. ?berspringen, aber NICHT returnen
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
                                    //this.LogInfo("[OnCalculate] Calling ProcessBreakEvenTick() after pending clear.");
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

                // 2) Manager-Init (Fallback) und 3) BreakEven-Tick
                //this.LogInfo($"[OnCalculate] BreakEven-Conditions: PosOpen={_positionOpen}, ManagersInit={_managersInitialized}, ExitPending={_isExitPlacementPending}, BE={EnableBreakEven}");
                if (_slOrder != null && _tpOrder != null && _positionOpen && !_managersInitialized && !_isExitPlacementPending)
                {
                    InitializeManagersAfterEntry(_lastTpSlResult);
                    this.LogInfo("[OnCalculate] Managers initialized in fallback.");
                    if (EnableBreakEven)
                    {
                        //this.LogInfo("[OnCalculate] Calling ProcessBreakEvenTick() post-init.");
                        try { ProcessBreakEvenTick(); }
                        catch (Exception ex) { this.LogWarn($"[OnCalculate] ProcessBreakEvenTick Exception: {ex.Message}"); }
                    }
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
                _marketStructureContext?.Reset();

            if (isNewSession)
            {
                _msTick900Aggregator?.Reset();
                _msTick900BucketSessionStart = DateTime.MinValue;
            }

            if (isNewSession)
            {
                // Eine neue Session hat begonnen
                // Die Werte des vorherigen Tages sind nun die abgeschlossenen Werte des _currentDay...
                if (_lastSessionStartBar != -1) // Nur wenn bereits eine Session abgeschlossen wurde
                {
                    _previousDayOpen = _currentDayOpen;
                    _previousDayHigh = _currentDayHigh;
                    _previousDayLow = _currentDayLow;
                    var lastSessionCandle = GetCandle(_lastSessionStartBar);
                    _previousDayClose = lastSessionCandle != null ? lastSessionCandle.Close : 0m; // Close der letzten Kerze der vorherigen Session

                    // Korrektur: Das Close des Vortages ist das Close des letzten Balkens des Vortages.
                    // ATAS-Indikatoren arbeiten oft mit dem Close des letzten Balkens VOR dem NewSession-Balken.
                    // Wenn der letzte Balken des Vortages der Balken (bar-1) war, dann ist dessen Close der korrekte Wert.
                    if (bar > 0)
                    {
                        var prevCandle = GetCandle(bar - 1);
                        if (prevCandle != null)
                            _previousDayClose = prevCandle.Close;
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

            bool hasDailyLevels = _dailyLevels != null
                && _dailyLevels.DataSeries != null
                && _dailyLevels.DataSeries.Count >= 4
                && _dailyLevels.DataSeries[0] != null
                && _dailyLevels.DataSeries[2] != null
                && _dailyLevels.DataSeries[3] != null;

            // --- R?ckw?rtssuche f?r POC (DataSeries[0]) ---
            if (hasDailyLevels)
            {
                for (int i = bar; i >= 0; i--)
                {
                    // ?berpr?fen, ob der Index f?r diese DataSeries g?ltig ist
                    // (WICHTIG, da DataSeries.Count durch _targetBar begrenzt sein kann)
                    if (_dailyLevels.DataSeries[0].Count > i)
                    {
                        object raw = _dailyLevels.DataSeries[0][i];
                        decimal val = raw != null ? Convert.ToDecimal(raw) : 0m;
                        if (val != 0m)
                        {
                            currentPOC = val;
                            break; // Letzten Nicht-Null-POC gefunden
                        }
                    }
                }
            }
            //this.LogInfo($"[DEBUG] Bar {bar}: R?ckw?rtssuche POC ergibt: {currentPOC}");
            // --- R?ckw?rtssuche f?r VAH (DataSeries[2]) ---
            if (hasDailyLevels)
            {
                for (int i = bar; i >= 0; i--)
                {
                    if (_dailyLevels.DataSeries[2].Count > i)
                    {
                        object raw = _dailyLevels.DataSeries[2][i];
                        decimal val = raw != null ? Convert.ToDecimal(raw) : 0m;
                        if (val != 0m)
                        {
                            currentVAH = val;
                            break; // Letzten Nicht-Null-VAH gefunden
                        }
                    }
                }
            }
            //this.LogInfo($"[DEBUG] Bar {bar}: R?ckw?rtssuche VAH ergibt: {currentVAH}");
            // --- R?ckw?rtssuche f?r VAL (DataSeries[3]) ---
            if (hasDailyLevels)
            {
                for (int i = bar; i >= 0; i--)
                {
                    if (_dailyLevels.DataSeries[3].Count > i)
                    {
                        object raw = _dailyLevels.DataSeries[3][i];
                        decimal val = raw != null ? Convert.ToDecimal(raw) : 0m;
                        if (val != 0m)
                        {
                            currentVAL = val;
                            break; // Letzten Nicht-Null-VAL gefunden
                        }
                    }
                }
            }
            //this.LogInfo($"[DEBUG] Bar {bar}: R?ckw?rtssuche VAL ergibt: {currentVAL}");

            // =====================
            // Daily Profile für WegFrei (Range-Bar kompatibel: bar-count getriggert)
            // =====================
            bool dailyEnabled = (EnableDailyProfilePathSystem || ShowDailyProfileLevels || ShowDailyHistogram);
            if (dailyEnabled)
            {
                // Reset am Session-Start (ATAS-konform wie DynamicLevels: IsNewSession/DataProvider.IsNewSession)
                if (isNewSession)
                {
                    _dailyProfileDate = c.Time.Date;
                    _dailyProfileClosedHist.Clear();
                    _dailyProfileDevHist.Clear();
                    _dailyProfileCombinedHist.Clear();
                    _dailyProfileDevBar = -1;
                    _lastDailyProfileRecalcBar = -1;
                    _lastDailyProfileRecalcPOC = 0m;
                    _lastDailyProfileRecalcVAH = 0m;
                    _lastDailyProfileRecalcVAL = 0m;
                    _lastDailyProfileActiveVolSignature = 0;
                    _lastDailyProfileActiveVolRecalcTime = DateTime.MinValue;
                    _dailyProfileForPath = null;
                    _prevDailyProfileForPath = null;
                    _dailyProfileForVisual = null;
                    _dailyProfileSeededForDate = false;

                    _dailyHvnNextId = 1;
                    _dailyHvnTracked.Clear();
                    _dailyHvnLastOutputIds = new();
                }

                bool justEnabled = !_prevDailyProfileEnabledFlag && dailyEnabled;
                _prevDailyProfileEnabledFlag = dailyEnabled;

                // Build Daily histogram directly from tick-based cumulative trades (PublicActiveVolume)
                var snapSig = _publicActiveVolume != null ? _publicActiveVolume.Signature : 0;
                bool histChanged = snapSig != 0 && snapSig != _lastDailyProfileActiveVolSignature;
                if (histChanged)
                {
                    var tmp = new SortedDictionary<decimal, decimal>();
                    foreach (var kv in _publicActiveVolume.GetTotalSnapshot())
                    {
                        if (kv.Value <= 0m) continue;
                        var px = RoundToTick(kv.Key);
                        if (tmp.TryGetValue(px, out var v)) tmp[px] = v + kv.Value;
                        else tmp[px] = kv.Value;
                    }

                    // Wichtig: nur übernehmen, wenn Snapshot verwertbar ist.
                    // Sonst kann _dailyProfileCombinedHist kurzzeitig leer werden und die Zonen verschwinden.
                    if (tmp.Count > 0)
                    {
                        _dailyProfileCombinedHist.Clear();
                        foreach (var kv in tmp) _dailyProfileCombinedHist[kv.Key] = kv.Value;
                        _lastDailyProfileActiveVolSignature = snapSig;
                    }
                }

                    // Wir verlassen uns auf ATAS DynamicLevels für POC/VAH/VAL. Wenn diese noch nicht bereit sind, überspringen Sie die tägliche Neuberechnung der Zone.

                    bool levelsChanged = currentPOC != 0m && currentVAH != 0m && currentVAL != 0m &&
                                     (currentPOC != _lastDailyProfileRecalcPOC || currentVAH != _lastDailyProfileRecalcVAH || currentVAL != _lastDailyProfileRecalcVAL);

                var now = c.LastTime;
                if (now == default)
                    now = c.Time;
                const int IntrabarMinMs = 250;
                bool intrabarReady = _lastDailyProfileActiveVolRecalcTime == DateTime.MinValue || (now - _lastDailyProfileActiveVolRecalcTime).TotalMilliseconds >= IntrabarMinMs;

                bool shouldRecalc = _lastDailyProfileRecalcBar < 0
                    || levelsChanged
                    || (histChanged && intrabarReady)
                    || (bar - _lastDailyProfileRecalcBar) >= Math.Max(1, DailyProfileRecalcEveryNBars);
                if (shouldRecalc)
                {
                    if (currentPOC != 0m && currentVAH != 0m && currentVAL != 0m)
                    {
                        RecalcDailyProfileForPath(currentPOC, currentVAH, currentVAL);
                        RecalcDailyProfileForVisual(currentPOC, currentVAH, currentVAL);
                        _lastDailyProfileActiveVolRecalcTime = now;
                    }
                    _lastDailyProfileRecalcBar = bar;
                    if (currentPOC != 0m && currentVAH != 0m && currentVAL != 0m)
                    {
                        _lastDailyProfileRecalcPOC = currentPOC;
                        _lastDailyProfileRecalcVAH = currentVAH;
                        _lastDailyProfileRecalcVAL = currentVAL;
                    }
                }

                // Debug-Log unabhängig vom Recalc-Trigger, damit Logs auch bei großen Recalc-Intervallen weiterlaufen.
                if (ShowDailyProfileLevels)
                {
                    const int DailyDbgEveryNBars = 50;
                    bool shouldDbg = _lastDailyProfileDebugBar < 0 || (bar - _lastDailyProfileDebugBar) >= DailyDbgEveryNBars || isNewSession;
                    if (shouldDbg)
                    {
                        _lastDailyProfileDebugBar = bar;
                        decimal totalVol = 0m;
                        foreach (var kv in _dailyProfileCombinedHist) totalVol += kv.Value;
                        int hvnZones = _dailyProfileForPath?.HVNZones?.Count ?? 0;
                        int lvnZones = _dailyProfileForPath?.LVNZones?.Count ?? 0;
                        int levels = _dailyProfileCombinedHist.Count;
                      //this.LogInfo($"[DailyProfile-DBG] bar={bar} levels={levels} totalVol={totalVol:F0} sig={snapSig} histChanged={histChanged} shouldRecalc={shouldRecalc} POC={currentPOC:F2} VAH={currentVAH:F2} VAL={currentVAL:F2} HVNZones={hvnZones} LVNZones={lvnZones} pathNull={(_dailyProfileForPath == null)} visualNull={(_dailyProfileForVisual == null)}");
                    }
                }
            }

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
                    if (_volumeProfileGenerator != null)
                        (_pdPOC, _pdVAH, _pdVAL) = _volumeProfileGenerator.GetPreviousDayVolumeProfile(bar - 1);
                    else
                    {
                        _pdPOC = 0m;
                        _pdVAH = 0m;
                        _pdVAL = 0m;
                    }
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

                if (EnableLevelSystem)
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

                _pdOpenZoneId = 0;
                _pdCloseZoneId = 0;
                _pdZoneDay = c.Time.Date;

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
            decimal pp = 0m, s1 = 0m, s2 = 0m, s3 = 0m, r1 = 0m, r2 = 0m, r3 = 0m, m1 = 0m, m2 = 0m, m3 = 0m, m4 = 0m;
            if (_pivots != null && _pivots.DataSeries != null && _pivots.DataSeries.Count >= 11)
            {
                bool ok = true;
                for (int si = 0; si <= 10; si++)
                {
                    if (_pivots.DataSeries[si] == null || _pivots.DataSeries[si].Count <= bar)
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                {
                    pp = Convert.ToDecimal(_pivots.DataSeries[0][bar]);
                    s1 = Convert.ToDecimal(_pivots.DataSeries[1][bar]);
                    s2 = Convert.ToDecimal(_pivots.DataSeries[2][bar]);
                    s3 = Convert.ToDecimal(_pivots.DataSeries[3][bar]);
                    r1 = Convert.ToDecimal(_pivots.DataSeries[4][bar]);
                    r2 = Convert.ToDecimal(_pivots.DataSeries[5][bar]);
                    r3 = Convert.ToDecimal(_pivots.DataSeries[6][bar]);
                    m1 = Convert.ToDecimal(_pivots.DataSeries[7][bar]);
                    m2 = Convert.ToDecimal(_pivots.DataSeries[8][bar]);
                    m3 = Convert.ToDecimal(_pivots.DataSeries[9][bar]);
                    m4 = Convert.ToDecimal(_pivots.DataSeries[10][bar]);
                }
            }

            // Berechnen Sie die runden Zahlen direkt mit dem Punkt-Schritt ===
            var currentPrice = c.Close;
            decimal levelBelow = Math.Floor(currentPrice / PointStep) * PointStep;
            decimal levelAbove = levelBelow + PointStep;

            // *** HINZUGEF?GT: Dynamische Level (Current POC, Runde Zahlen) zum Zeichnen hinzuf?gen/aktualisieren ***
            if (EnableLevelSystem)
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







            if (EnableLevelSystem)
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

            if (stopTick)
            {
                bool forceIntrabarPendingPass = false;
                try
                {
                    forceIntrabarPendingPass = System.Threading.Interlocked.CompareExchange(ref _msTick900ZonesDirtyFlag, 0, 0) == 1;
                }
                catch { forceIntrabarPendingPass = false; }

                if (!forceIntrabarPendingPass)
                    return;

                this.LogDebug("[OnCalculate] stopTick bypassed because msTick900ZonesDirtyFlag=1 -> continue to intrabar pending evaluation.");
            }

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

            try
            {
                if (_marketStructureContext != null && bar > 0 && _currentLevelsSnapshot != null && _hasOvLastClosed && ovSnapshot != null)
                {
                                        bool tick900Dirty = System.Threading.Interlocked.Exchange(ref _msTick900ZonesDirtyFlag, 0) == 1;
                                        int maxReadyZoneId = -1;
                    int pendingCount = 0;
                    int activeCount = 0;
                    try
                    {
                        var zonesNow = _marketStructureContext.ActiveZones;
                        activeCount = zonesNow != null ? zonesNow.Count : 0;
                        if (zonesNow != null && zonesNow.Count > 0)
                        {
                            for (int zi = 0; zi < zonesNow.Count; zi++)
                            {
                                var z = zonesNow[zi];
                                if (z == null)
                                    continue;
                                if (z.Status == MyNamespace.Strategies.MarketAnalysis.MarketStructureContext.ZoneStatus.Used)
                                    continue;
                                if (z.IsConfirmed)
                                    continue;
                                pendingCount++;
                                if (z.Id > maxReadyZoneId)
                                    maxReadyZoneId = z.Id;
                            }
                        }
                    }
                    catch { maxReadyZoneId = -1; }

                    int currentBarNow = bar;
                    int evalBar = currentBarNow - 1; // Evaluieren der GESCHLOSSENEN Kerze retroaktiv
                    int closedNow = currentBarNow - 1;
                    if (closedNow >= 0 && closedNow != _lastPendingZonesLogClosed)
                    {
                        this.LogInfo("[OnCalculate-PENDING-ZONES] bar=" + bar + " closed=" + closedNow + " activeZones=" + activeCount + " pendingUnconfirmed=" + pendingCount + " maxPendingId=" + maxReadyZoneId);
                        _lastPendingZonesLogClosed = closedNow;
                    }

                    bool notAlreadyTriggeredForEvalBar = evalBar != _lastIntrabarZoneTriggerClosed;
                    bool newPendingZoneAppearedSameEvalBar = (evalBar == _lastIntrabarZoneTriggerClosed) && (maxReadyZoneId > _lastIntrabarZoneTriggerMaxPendingId);
                    if (pendingCount > 0 && evalBar >= 0 && (tick900Dirty || notAlreadyTriggeredForEvalBar || newPendingZoneAppearedSameEvalBar))
                    {
                        this.LogInfo("[OnCalculate-INTRABAR-PENDING] Pending zones present (pending=" + pendingCount + ", maxPendingId=" + maxReadyZoneId + ") at bar=" + bar + " CurrentBar=" + CurrentBar + " -> EvaluateSignalsAndOrders(closed=" + evalBar + ")");

                        OvSnapshot ovForEvalBar = null;
                        try
                        {
                            if (ovSnapshot != null && ovSnapshot.Bar == evalBar)
                                ovForEvalBar = ovSnapshot;
                            else if (_ovSnapshotHistory != null && !_ovSnapshotHistory.IsEmpty)
                            {
                                int maxScan = 80;
                                int count = 0;
                                for (int off = 0; off >= -maxScan; off--)
                                {
                                    if (!_ovSnapshotHistory.TryGetRelative(off, out var s) || s == null)
                                        break;
                                    if (s.Bar == evalBar)
                                    {
                                        ovForEvalBar = s;
                                        break;
                                    }
                                    count++;
                                    if (count > maxScan)
                                        break;
                                }
                            }
                        }
                        catch { ovForEvalBar = null; }

                        if (ovForEvalBar == null)
                        {
                            try
                            {
                                var cc = GetCandle(evalBar);
                                if (cc != null)
                                {
                                    decimal candlePocPrice = cc.Close;
                                    decimal pocDelta = 0m;
                                    try
                                    {
                                        var pocPvi = cc.MaxVolumePriceInfo;
                                        if (pocPvi != null)
                                        {
                                            candlePocPrice = (decimal)pocPvi.Price;
                                            pocDelta = (decimal)pocPvi.Ask - (decimal)pocPvi.Bid;
                                        }
                                    }
                                    catch { }

                                    var lowPvi = cc.GetPriceVolumeInfo(cc.Low);
                                    var highPvi = cc.GetPriceVolumeInfo(cc.High);
                                    decimal bidAtLow = lowPvi != null ? (decimal)lowPvi.Bid : 0m;
                                    decimal askAtLow = lowPvi != null ? (decimal)lowPvi.Ask : 0m;
                                    decimal bidAtHigh = highPvi != null ? (decimal)highPvi.Bid : 0m;
                                    decimal askAtHigh = highPvi != null ? (decimal)highPvi.Ask : 0m;

                                    ovForEvalBar = new OvSnapshot
                                    {
                                        Bar = evalBar,
                                        ChartBarNumber = evalBar + 1,
                                        SessionBarNumber = evalBar + 1,
                                        Open = cc.Open,
                                        High = cc.High,
                                        Low = cc.Low,
                                        Time = cc.Time,
                                        Close = cc.Close,
                                        Volume = cc.Volume,
                                        Delta = cc.Delta,
                                        Ask = cc.Ask,
                                        Bid = cc.Bid,
                                        BestAskPrice = Security?.BestAskPrice ?? 0m,
                                        BestBidPrice = Security?.BestBidPrice ?? 0m,
                                        CandlePocPrice = candlePocPrice,
                                        PocDelta = pocDelta,
                                        BidAtLow = bidAtLow,
                                        AskAtLow = askAtLow,
                                        BidAtHigh = bidAtHigh,
                                        AskAtHigh = askAtHigh,
                                    };
                                }
                            }
                            catch { ovForEvalBar = null; }
                        }

                        MyNamespace.Strategies.Models.DetectedOrderflowPattern detectedPatternIntrabar = new MyNamespace.Strategies.Models.DetectedOrderflowPattern();

                        // Berechne MarketRegime für evalBar (forming bar)
                        MarketRegime localRegimeForEvalBar = MarketRegime.None;
                        MarketRegimeDetails localRegimeDetailsForEvalBar = null;
                        try
                        {
                            var cRegIntra = GetCandle(evalBar);
                            var pRegIntra = GetCandle(evalBar - 1);
                            if (cRegIntra != null && pRegIntra != null && ovForEvalBar != null)
                            {
                                var cndlIntra = new MyNamespace.Strategies.MarketAnalysis.IndicatorCandleAdapter(cRegIntra, ovForEvalBar?.NetDeltaTotal ?? 0m);
                                var prevCndlIntra = new MyNamespace.Strategies.MarketAnalysis.IndicatorCandleAdapter(pRegIntra, ovForEvalBar?.NetDeltaTotal ?? 0m);
                                localRegimeDetailsForEvalBar = _marketRegimeEvaluator.GetCurrentMarketRegime(evalBar, ovForEvalBar, _myClusterStatistic, cndlIntra, prevCndlIntra);
                                if (localRegimeDetailsForEvalBar != null)
                                    localRegimeForEvalBar = localRegimeDetailsForEvalBar.Regime;
                            }
                        }
                        catch (Exception exRegime)
                        {
                            this.LogWarn($"[OnCalculate-INTRABAR-PENDING] MarketRegime compute failed: {exRegime.Message}");
                        }

                        // Berechne MarketStateV2 für evalBar (forming bar)
                        MyNamespace.Strategies.Models.MarketStateV2 localMarketStateForEvalBar = null;
                        try
                        {
                            if (_marketStateEngineV2 != null && ovForEvalBar != null && _currentVwapSnapshot != null)
                            {
                                var htfSwingHigh = _marketStructureContext?.LastConfirmedSwingHigh;
                                var htfSwingLow = _marketStructureContext?.LastConfirmedSwingLow;
                                bool isInHtfZone = _marketStructureContext != null && _marketStructureContext.IsInAnyActiveZone(ovForEvalBar.Close, _tickSize, bufferTicks: 2);
                                var htfZoneType = _marketStructureContext != null
                                    ? _marketStructureContext.GetHtfZoneType(ovForEvalBar.Close, _tickSize, bufferTicks: 2)
                                    : MyNamespace.Strategies.Models.HtfZoneType.None;

                                var v2InputIntra = new MyNamespace.Strategies.Models.MarketStateInputV2(
                                    bar: evalBar,
                                    high: ovForEvalBar.High,
                                    low: ovForEvalBar.Low,
                                    close: ovForEvalBar.Close,
                                    vwap: _currentVwapSnapshot.Current,
                                    upperBand1: _currentVwapSnapshot.UpperBand1,
                                    lowerBand1: _currentVwapSnapshot.LowerBand1,
                                    upperBand2: _currentVwapSnapshot.UpperBand2,
                                    lowerBand2: _currentVwapSnapshot.LowerBand2,
                                    upperBand3: _currentVwapSnapshot.UpperBand3,
                                    lowerBand3: _currentVwapSnapshot.LowerBand3,
                                    currentVah: _currentLevelsSnapshot?.CurrentVAH ?? 0m,
                                    currentVal: _currentLevelsSnapshot?.CurrentVAL ?? 0m,
                                    candlePocPrice: ovForEvalBar.CandlePocPrice,
                                    regime: localRegimeForEvalBar,
                                    htfSwingHigh: htfSwingHigh,
                                    htfSwingLow: htfSwingLow,
                                    isInHtfZone: isInHtfZone,
                                    htfZoneType: htfZoneType);

                                localMarketStateForEvalBar = _marketStateEngineV2.Update(v2InputIntra);
                                this.LogInfo($"[OnCalculate-INTRABAR-PENDING] Computed MarketState for evalBar={evalBar}: Bias={localMarketStateForEvalBar?.Bias} Phase={localMarketStateForEvalBar?.Phase}");
                            }
                        }
                        catch (Exception exState)
                        {
                            this.LogWarn($"[OnCalculate-INTRABAR-PENDING] MarketStateV2 compute failed: {exState.Message}");
                        }

                        // Fallback auf globale Werte wenn lokale Berechnung fehlschlägt
                        if (localMarketStateForEvalBar == null)
                            localMarketStateForEvalBar = _currentMarketStateV2 ?? new MyNamespace.Strategies.Models.MarketStateV2 { Dynamic = localRegimeForEvalBar };

                        try
                        {
                            if (_patternRunner == null)
                            {
                                // skip detection - patternRunner not initialized
                            }
                            else if (_ofFeaturesHistory == null)
                            {
                                // skip detection - ofFeaturesHistory not initialized
                            }
                            else
                            {
                                OfFeatures featIntrabar = null;
                                try
                                {
                                    if (_ofFeaturesByBar != null)
                                        _ofFeaturesByBar.TryGetValue(evalBar, out featIntrabar);
                                }
                                catch { featIntrabar = null; }

                                if (featIntrabar == null)
                                {
                                    try { _ofFeaturesHistory.TryGetByBar(evalBar, out featIntrabar); }
                                    catch { featIntrabar = null; }
                                }

                                if (featIntrabar == null)
                                {
                                    try
                                    {
                                        OvSnapshot snapForEval = null;
                                        try
                                        {
                                            snapForEval = ovForEvalBar;
                                        }
                                        catch { snapForEval = null; }

                                        if (_featureCalculator != null && snapForEval != null)
                                        {
                                            var forced = _featureCalculator.ComputeFeaturesForLastBar(snapForEval);
                                            if (forced != null)
                                            {
                                                AddFeatureAndSync(forced);
                                                featIntrabar = forced;
                                            }
                                        }
                                        else if (snapForEval == null)
                                        {
                                            // No OvSnapshot available - cannot force OfFeatures
                                        }
                                    }
                                    catch (Exception)
                                    {
                                        // Forced OfFeatures compute failed - ignore
                                    }
                                }

                                if (featIntrabar != null)
                                {
                                    if (featIntrabar != null && featIntrabar.Snapshot != null && !_ofFeaturesByBar.ContainsKey(evalBar))
                                    {
                                        _ofFeaturesByBar[evalBar] = featIntrabar;
                                    }

                                    var computedBias = localMarketStateForEvalBar.Bias;

                                    var detectedPattern = _patternRunner.DetectDominantOrderflowPattern(
                                        bar: evalBar,
                                        history: _ofFeaturesHistory,
                                        ofFeaturesByBar: _ofFeaturesByBar,
                                        currentRegime: localRegimeForEvalBar,
                                        currentDirectionalBias: computedBias,
                                        currentMarketState: localMarketStateForEvalBar,
                                        currentMarketStructureContext: _marketStructureContext,
                                        currentBar: evalBar);

                                    if (detectedPattern != null)
                                        detectedPatternIntrabar = detectedPattern;
                                }
                            }
                        }
                        catch (Exception)
                        {
                            _intrabarImmediatePlaceMode = false;
                            _intrabarImmediatePlaceBar = -1;
                        }

                        _intrabarImmediatePlaceMode = true;
                        _intrabarImmediatePlaceBar = bar;
                        try
                        {
                            EvaluateSignalsAndOrders(
                                caller: "IntrabarPending",
                                closed: evalBar,
                                ovLastClosed: ovForEvalBar ?? ovSnapshot,
                                ofFeaturesHistory: _ofFeaturesHistory,
                                isBlockedLong: _currentLevelsSnapshot.IsBlockedLong,
                                isBlockedShort: _currentLevelsSnapshot.IsBlockedShort,
                                levelsSnapshot: _currentLevelsSnapshot,
                                currentPOC_Explicit: _currentLevelsSnapshot.CurrentPOC,
                                currentVAH_Explicit: _currentLevelsSnapshot.CurrentVAH,
                                currentVAL_Explicit: _currentLevelsSnapshot.CurrentVAL,
                                currentMarketRegime: localRegimeForEvalBar,
                                detectedPattern: detectedPatternIntrabar,
                                currentMarketState: localMarketStateForEvalBar,
                                currentVwap: _currentVwapSnapshot?.Current ?? 0m);
                        }
                        finally
                        {
                            _intrabarImmediatePlaceMode = false;
                            _intrabarImmediatePlaceBar = -1;
                        }
                        _lastIntrabarZoneTriggerClosed = evalBar;

                        _lastIntrabarZoneTriggerMaxPendingId = maxReadyZoneId;
                    }

                    _lastSeenMarketStructureMaxReadyZoneId = maxReadyZoneId;
                }
            }
            catch { }

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
                        this.LogInfo($"[OnCalculate] MyClusterStatistic series not ready for closed={closed} (VolPerSecond.Count={_myClusterStatistic.VolPerSecond.Count}) -> continue with defaults (no postpone)");
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
                    this.LogInfo($"[OnCalculate] ? newBar: current={bar}, closed={closed}, lastEval={_lastEvalBar}, latestZBar={latestZBar}");

                    if (_lastEvalBar == closed)
                    {
                        this.LogInfo($"[OnCalculate] debounce: already evaluated closed={closed}");
                        return;
                    }

                    if (!HasKey(_volBurstZ, closed))
                    {
                        this.LogInfo($"[OnCalculate] VolZ for closed={closed} not ready (latestZBar={latestZBar}) -> continue with defaults (no postpone)");
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
                    decimal imbalanceScore = GetOr0(_imbalanceScoreSeries, closed);
                    string imbalanceScoreLabel = _imbalanceScoreLabelSeries.TryGetValue(closed, out var scoreLabel)
                        ? scoreLabel
                        : string.Empty;

                    // HINZUGEF?GT: Werte aus MyClusterStatistic (index-sicher; wenn Series noch nicht bereit, nutze 0)
                    decimal candleDuration = 0m;
                    decimal volPerSecond = 0m;
                    decimal emaVolPerSecond = 0m;
                    decimal emaVolPerSecondStd = 0m;
                    decimal cumulativeDelta = 0m;
                    decimal cumulativeVolume = 0m;
                    decimal barDeltaPerVolume = 0m;
                    try { if (_myClusterStatistic.CandleDurations != null && _myClusterStatistic.CandleDurations.Count > closed) candleDuration = _myClusterStatistic.CandleDurations[closed]; } catch { }
                    try { if (_myClusterStatistic.VolPerSecond != null && _myClusterStatistic.VolPerSecond.Count > closed) volPerSecond = _myClusterStatistic.VolPerSecond[closed]; } catch { }
                    try { if (_myClusterStatistic.EmaVolPerSecond != null && _myClusterStatistic.EmaVolPerSecond.Count > closed) emaVolPerSecond = _myClusterStatistic.EmaVolPerSecond[closed]; } catch { }
                    try { if (_myClusterStatistic.EmaVolPerSecondStd != null && _myClusterStatistic.EmaVolPerSecondStd.Count > closed) emaVolPerSecondStd = _myClusterStatistic.EmaVolPerSecondStd[closed]; } catch { }
                    try { if (_myClusterStatistic.CumulativeDelta != null && _myClusterStatistic.CumulativeDelta.Count > closed) cumulativeDelta = _myClusterStatistic.CumulativeDelta[closed]; } catch { }
                    try { if (_myClusterStatistic.CumulativeVolume != null && _myClusterStatistic.CumulativeVolume.Count > closed) cumulativeVolume = _myClusterStatistic.CumulativeVolume[closed]; } catch { }
                    try { if (_myClusterStatistic.BarDeltaPerVolume != null && _myClusterStatistic.BarDeltaPerVolume.Count > closed) barDeltaPerVolume = _myClusterStatistic.BarDeltaPerVolume[closed]; } catch { }
                    // Stacked imbalance Zusatzfelder: stelle sicher, dass _imbalanceRatioPct decimal ist
                    decimal stackedImbRatioPct = _imbalanceRatioPct; // bereits decimal, wie gew?nscht


                    this.LogDebug($"[OnCalculate SNAP] Bar={closed} Time={closedCandle.Time:O} High={closedCandle.High:F2} Low={closedCandle.Low:F2} Close={closedCandle.Close:F2} " +
                        $"Volume={closedCandle.Volume:F2} Delta={closedCandle.Delta:F2} VolBurstZ={Volumenburst:F4} CvdImpulse={CVDImpuls:F4} AggPressure={DruckAnteil:F4} TradeRateZ={TradeRate:F4}");

                    // Cluster-/PriceInfo Felder für Orderflow-Regeln (FinishedAuction, POC-Delta)
                    var lowPvi = closedCandle.GetPriceVolumeInfo(closedCandle.Low);
                    var highPvi = closedCandle.GetPriceVolumeInfo(closedCandle.High);
                    decimal bidAtLow = lowPvi != null ? (decimal)lowPvi.Bid : 0m;
                    decimal askAtLow = lowPvi != null ? (decimal)lowPvi.Ask : 0m;
                    decimal bidAtHigh = highPvi != null ? (decimal)highPvi.Bid : 0m;
                    decimal askAtHigh = highPvi != null ? (decimal)highPvi.Ask : 0m;

                    // POC über MaxVolumePriceInfo (Fallback: Close). POC-Delta = Ask - Bid am POC-Level.
                    decimal candlePocPrice = closedCandle.Close;
                    decimal pocDelta = 0m;
                    decimal pocVolume = 0m;
                    decimal pocBid = 0m;
                    decimal pocAsk = 0m;

                    decimal maxBidLevelPrice = 0m;
                    decimal maxBidLevelBid = 0m;
                    decimal maxBidLevelAsk = 0m;
                    decimal maxAskLevelPrice = 0m;
                    decimal maxAskLevelAsk = 0m;
                    decimal maxAskLevelBid = 0m;
                    try
                    {
                        var pocPvi = closedCandle.MaxVolumePriceInfo;
                        if (pocPvi != null)
                        {
                            candlePocPrice = (decimal)pocPvi.Price;
                            pocDelta = (decimal)pocPvi.Ask - (decimal)pocPvi.Bid;
                            pocVolume = (decimal)pocPvi.Volume;
                            pocBid = (decimal)pocPvi.Bid;
                            pocAsk = (decimal)pocPvi.Ask;
                        }
                    }
                    catch
                    {
                        // ignore, use fallback
                    }

                    try
                    {
                        var maxBidPvi = closedCandle.MaxBidPriceInfo;
                        if (maxBidPvi != null)
                        {
                            maxBidLevelPrice = (decimal)maxBidPvi.Price;
                            maxBidLevelBid = (decimal)maxBidPvi.Bid;
                            maxBidLevelAsk = (decimal)maxBidPvi.Ask;
                        }
                    }
                    catch
                    {
                    }

                    try
                    {
                        var maxAskPvi = closedCandle.MaxAskPriceInfo;
                        if (maxAskPvi != null)
                        {
                            maxAskLevelPrice = (decimal)maxAskPvi.Price;
                            maxAskLevelAsk = (decimal)maxAskPvi.Ask;
                            maxAskLevelBid = (decimal)maxAskPvi.Bid;
                        }
                    }
                    catch
                    {
                    }

                    int sessionStartBarForNumbering = 0;
                    for (int i = Math.Max(0, closed); i >= 0; i--)
                    {
                        if (IsNewSession(i))
                        {
                            sessionStartBarForNumbering = i;
                            break;
                        }
                    }

                    int chartBarNumber = closed + 1;
                    int sessionBarNumber = (closed - sessionStartBarForNumbering) + 1;

                    // Snapshot aktualisieren 
                    ovSnapshot = new OvSnapshot
                    {
                        Bar = closed,
                        ChartBarNumber = chartBarNumber,
                        SessionBarNumber = sessionBarNumber,
                        Open = closedCandle.Open,
                        High = closedCandle.High,
                        Low = closedCandle.Low,
                        Time = closedCandle.Time,
                        Close = closedCandle.Close,
                        Volume = closedCandle.Volume,
                        Delta = closedCandle.Delta,
                        Ask = closedCandle.Ask,
                        Bid = closedCandle.Bid,
                        BestAskPrice = Security?.BestAskPrice ?? 0m,
                        BestBidPrice = Security?.BestBidPrice ?? 0m,
                        CandlePocPrice = candlePocPrice,
                        PocDelta = pocDelta,
                        PocVolume = pocVolume,
                        PocBid = pocBid,
                        PocAsk = pocAsk,
                        MaxBidLevelPrice = maxBidLevelPrice,
                        MaxBidLevelBid = maxBidLevelBid,
                        MaxBidLevelAsk = maxBidLevelAsk,
                        MaxAskLevelPrice = maxAskLevelPrice,
                        MaxAskLevelAsk = maxAskLevelAsk,
                        MaxAskLevelBid = maxAskLevelBid,
                        BidAtLow = bidAtLow,
                        AskAtLow = askAtLow,
                        BidAtHigh = bidAtHigh,
                        AskAtHigh = askAtHigh,
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
                        ImbalanceScore = imbalanceScore,
                        ImbalanceScoreLabel = imbalanceScoreLabel,
                        StackedImbMinVolPerLevel = _imbalanceVolumeMin,
                        StackedImbRatioPct = _imbalanceRatioPct,
                        StackedImbRangeMin = _imbalanceRangeMin,
                        StackedImbMaxDepthTicks = _imbMaxDepthTicksAnchored,
                        // ? NEU: Spatial-Delta Properties
                        TopDeltaRatio = r.TopDeltaRatio,
                        BottomDeltaRatio = r.BottomDeltaRatio,
                        TopDominance = r.TopDominance,
                        BottomDominance = r.BottomDominance,
                        NetDeltaTotal = r.NetDeltaTotal,
                        IsPerfectLongSetup = r.IsPerfectLongSetup,
                        IsPerfectShortSetup = r.IsPerfectShortSetup,
                        PerfectSetupReason = r.PerfectSetupReason,
                        UpperWickDeltaRatio = r.UpperWickDeltaRatio,
                        LowerWickDeltaRatio = r.LowerWickDeltaRatio,
                        UpperWickDominance = r.UpperWickDominance,
                        LowerWickDominance = r.LowerWickDominance,
                        UpperWickAbsDeltaTotal = r.UpperWickAbsDeltaTotal,
                        LowerWickAbsDeltaTotal = r.LowerWickAbsDeltaTotal
                    };




                    this.LogInfo(string.Format(CultureInfo.InvariantCulture,
                        "[OnCalculate_SNAP] ? Bar={0} ChartBarNumber={1} SessionBarNumber={2} Time={3:O} MarketRegime={4} " +
                        "Open={5:F2} High={6:F2} Low={7:F2} Close={8:F2} Volume={9:F2} Delta={10:F2} Ask={11:F2} Bid={12:F2} " +
                        "BestAskPrice={13:F2} BestBidPrice={14:F2} " +
                        "CandlePocPrice={15:F2} PocDelta={16:F4} PocVolume={17:F2} BidAtLow={18:F2} AskAtLow={19:F2} BidAtHigh={20:F2} AskAtHigh={21:F2} " +
                        "MaxBull={22:F4} MaxBear={23:F4} VolBurstZ={24:F4} CvdImpulse={25:F4} CvdCoherence={26:F4} AggPressure={27:F4} TradeRateZ={28:F4} Efficiency={29:F4} " +
                        "BuyTrades={30} SellTrades={31} TotalTrades={32} IttZ={33:F4} SweepUp={34} SweepDn={35} " +
                        "ImbalanceScore={36:F4} ImbalanceScoreLabel={37} " +
                        "StackedBuyCount={38} StackedSellCount={39} StackedBuyTop={40} StackedSellBottom={41} StackedImbRatioPct={42:F4} StackedImbMinVolPerLevel={43} StackedImbRangeMin={44} StackedImbMaxDepthTicks={45} " +
                        "CandleDuration={46:F4} VolPerSecond={47:F4} EmaVolPerSecond={48:F4} EmaVolPerSecondStd={49:F4} CumulativeDelta={50:F4} CumulativeVolume={51:F4} BarDeltaPerVolume={52:F6} " +
                        "TopDeltaRatio={53:F4} BottomDeltaRatio={54:F4} TopDominance={55} BottomDominance={56} NetDeltaTotal={57:F4} " +
                        "IsPerfectLongSetup={58} IsPerfectShortSetup={59} PerfectSetupReason={60} " +
                        "UpperWickDeltaRatio={61:F4} LowerWickDeltaRatio={62:F4} UpperWickDominance={63} LowerWickDominance={64} UpperWickAbsDeltaTotal={65:F4} LowerWickAbsDeltaTotal={66:F4}",
                        ovSnapshot.Bar, ovSnapshot.ChartBarNumber, ovSnapshot.SessionBarNumber, ovSnapshot.Time, ovSnapshot.MarketRegime,
                        ovSnapshot.Open, ovSnapshot.High, ovSnapshot.Low, ovSnapshot.Close, ovSnapshot.Volume, ovSnapshot.Delta, ovSnapshot.Ask, ovSnapshot.Bid,
                        ovSnapshot.BestAskPrice, ovSnapshot.BestBidPrice,
                        ovSnapshot.CandlePocPrice, ovSnapshot.PocDelta, ovSnapshot.PocVolume, ovSnapshot.BidAtLow, ovSnapshot.AskAtLow, ovSnapshot.BidAtHigh, ovSnapshot.AskAtHigh,
                        ovSnapshot.MaxCounterShareBull, ovSnapshot.MaxCounterShareBear, ovSnapshot.VolBurstZ, ovSnapshot.CvdImpulse, ovSnapshot.CvdCoherence, ovSnapshot.AggPressure, ovSnapshot.TradeRateZ, ovSnapshot.Efficiency,
                        ovSnapshot.BuyTrades, ovSnapshot.SellTrades, ovSnapshot.TotalTrades, ovSnapshot.IttZ, ovSnapshot.SweepUpClosed, ovSnapshot.SweepDnClosed,
                        ovSnapshot.ImbalanceScore, ovSnapshot.ImbalanceScoreLabel,
                        ovSnapshot.StackedBuyImbCount, ovSnapshot.StackedSellImbCount, ovSnapshot.StackedBuyImbTopCount, ovSnapshot.StackedSellImbBottomCount, ovSnapshot.StackedImbRatioPct, ovSnapshot.StackedImbMinVolPerLevel, ovSnapshot.StackedImbRangeMin, ovSnapshot.StackedImbMaxDepthTicks,
                        ovSnapshot.CandleDuration, ovSnapshot.VolPerSecond, ovSnapshot.EmaVolPerSecond, ovSnapshot.EmaVolPerSecondStd, ovSnapshot.CumulativeDelta, ovSnapshot.CumulativeVolume, ovSnapshot.BarDeltaPerVolume,
                        ovSnapshot.TopDeltaRatio, ovSnapshot.BottomDeltaRatio, ovSnapshot.TopDominance, ovSnapshot.BottomDominance, ovSnapshot.NetDeltaTotal,
                        ovSnapshot.IsPerfectLongSetup, ovSnapshot.IsPerfectShortSetup, ovSnapshot.PerfectSetupReason,
                        ovSnapshot.UpperWickDeltaRatio, ovSnapshot.LowerWickDeltaRatio, ovSnapshot.UpperWickDominance, ovSnapshot.LowerWickDominance, ovSnapshot.UpperWickAbsDeltaTotal, ovSnapshot.LowerWickAbsDeltaTotal
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

                    // Deferred intrabar pending-zone evaluation: now the snapshot/history should be aligned to 'closed'.
                    try
                    {
                        if (_deferredIntrabarPendingEvalClosed == closed && _marketStructureContext != null && _currentLevelsSnapshot != null && _hasOvLastClosed && ovSnapshot != null)
                        {
                            int maxPendingIdNow = -1;
                            int pendingCountNow = 0;
                            try
                            {
                                var zonesNow = _marketStructureContext.ActiveZones;
                                if (zonesNow != null && zonesNow.Count > 0)
                                {
                                    for (int zi = 0; zi < zonesNow.Count; zi++)
                                    {
                                        var z = zonesNow[zi];
                                        if (z == null) continue;
                                        if (z.Status == MyNamespace.Strategies.MarketAnalysis.MarketStructureContext.ZoneStatus.Used) continue;
                                        if (z.IsConfirmed) continue;
                                        pendingCountNow++;
                                        if (z.Id > maxPendingIdNow) maxPendingIdNow = z.Id;
                                    }
                                }
                            }
                            catch { maxPendingIdNow = -1; pendingCountNow = 0; }

                            if (pendingCountNow > 0)
                            {
                                this.LogInfo($"[OnCalculate-INTRABAR-PENDING-DEFERRED] Running deferred pending-zone eval. bar={bar} closed={closed} pending={pendingCountNow} maxPendingId={maxPendingIdNow} tick900Dirty={_deferredIntrabarPendingEvalTick900Dirty}");

                                MyNamespace.Strategies.Models.DetectedOrderflowPattern detectedPatternDeferred = new MyNamespace.Strategies.Models.DetectedOrderflowPattern();
                                try
                                {
                                    if (_patternRunner != null && _ofFeaturesHistory != null)
                                    {
                                        var safeOfFeaturesByBar = _ofFeaturesByBar ?? new Dictionary<int, OfFeatures>();
                                        var localRegime = _marketRegimeDetails != null ? _marketRegimeDetails.Regime : MarketRegime.None;
                                        var localMarketState = _currentMarketStateV2 ?? new MyNamespace.Strategies.Models.MarketStateV2 { Dynamic = localRegime };
                                        var localBias = localMarketState.Bias;
                                        detectedPatternDeferred = _patternRunner.DetectDominantOrderflowPattern(
                                            bar: closed,
                                            history: _ofFeaturesHistory,
                                            ofFeaturesByBar: safeOfFeaturesByBar,
                                            currentRegime: localRegime,
                                            currentDirectionalBias: localBias,
                                            currentMarketState: localMarketState,
                                            currentMarketStructureContext: _marketStructureContext,
                                            currentBar: closed);
                                        this.LogInfo($"[OnCalculate-INTRABAR-PENDING-DEFERRED-PATTERN] Detected: bar={bar} closed={closed} type={detectedPatternDeferred?.Type} dir={detectedPatternDeferred?.Direction} conf={detectedPatternDeferred?.ConfidenceScore:0.00}");
                                    }
                                    else
                                    {
                                        this.LogWarn($"[OnCalculate-INTRABAR-PENDING-DEFERRED-PATTERN] Pattern detection skipped: runner={( _patternRunner==null ? "null" : "ok")} history={( _ofFeaturesHistory==null ? "null" : "ok")} (bar={bar}, closed={closed})");
                                    }
                                }
                                catch (Exception exDet)
                                {
                                    this.LogWarn($"[OnCalculate-INTRABAR-PENDING-DEFERRED-PATTERN] Detection failed: {exDet.GetType().Name}: {exDet.Message} (bar={bar}, closed={closed})");
                                }

                                EvaluateSignalsAndOrders(
                                    caller: "IntrabarPendingDeferred",
                                    closed: closed,
                                    ovLastClosed: ovSnapshot,
                                    ofFeaturesHistory: _ofFeaturesHistory,
                                    isBlockedLong: _currentLevelsSnapshot.IsBlockedLong,
                                    isBlockedShort: _currentLevelsSnapshot.IsBlockedShort,
                                    levelsSnapshot: _currentLevelsSnapshot,
                                    currentPOC_Explicit: _currentLevelsSnapshot.CurrentPOC,
                                    currentVAH_Explicit: _currentLevelsSnapshot.CurrentVAH,
                                    currentVAL_Explicit: _currentLevelsSnapshot.CurrentVAL,
                                    currentMarketRegime: _marketRegimeDetails != null ? _marketRegimeDetails.Regime : MarketRegime.None,
                                    detectedPattern: detectedPatternDeferred,
                                    currentMarketState: _currentMarketStateV2,
                                    currentVwap: _currentVwapSnapshot?.Current ?? 0m);

                                _lastIntrabarZoneTriggerClosed = closed;
                                _lastIntrabarZoneTriggerMaxPendingId = maxPendingIdNow;
                            }

                            _deferredIntrabarPendingEvalClosed = -1;
                            _deferredIntrabarPendingEvalMaxPendingId = -1;
                            _deferredIntrabarPendingEvalTick900Dirty = false;
                        }
                    }
                    catch { }

                    // ========== PHASE 3: MARKET REGIME ==========

                    // **********************************************************************************
                    // HIER AUFRUFEN: Berechnung des Marktregimes f?r den geschlossenen Balken
                    // **********************************************************************************
                    MarketRegime currentMarketRegime = MarketRegime.None;

                    var cReg = GetCandle(closed);
                    var pReg = GetCandle(closed - 1);
                    if (cReg == null || pReg == null)
                    {
                        this.LogWarn($"[OnCalculate] Market Regime cannot be determined: missing candle(s) for bar {closed}");
                        _marketRegimeDetails = new MarketRegimeDetails { Regime = MarketRegime.Normal };
                        currentMarketRegime = _marketRegimeDetails.Regime;
                    }
                    else
                    {
                        var cndl = new MyNamespace.Strategies.MarketAnalysis.IndicatorCandleAdapter(cReg, ovSnapshot?.NetDeltaTotal ?? 0m);
                        var prevCndl = new MyNamespace.Strategies.MarketAnalysis.IndicatorCandleAdapter(pReg, ovSnapshot?.NetDeltaTotal ?? 0m);

                        _marketRegimeDetails = _marketRegimeEvaluator.GetCurrentMarketRegime(closed, ovSnapshot, _myClusterStatistic, cndl, prevCndl);
                        if (_marketRegimeDetails == null)
                        {
                            this.LogWarn($"[OnCalculate] MarketRegimeEvaluator returned null for bar {closed}");
                            _marketRegimeDetails = new MarketRegimeDetails { Regime = MarketRegime.Normal };
                        }

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

                        try
                        {
                            if (_marketStateEngineV2 != null && ovSnapshot != null && _currentVwapSnapshot != null)
                            {
                                var htfSwingHigh = _marketStructureContext?.LastConfirmedSwingHigh;
                                var htfSwingLow = _marketStructureContext?.LastConfirmedSwingLow;
                                bool isInHtfZone = _marketStructureContext != null && _marketStructureContext.IsInAnyActiveZone(ovSnapshot.Close, _tickSize, bufferTicks: 2);
                                var htfZoneType = _marketStructureContext != null
                                    ? _marketStructureContext.GetHtfZoneType(ovSnapshot.Close, _tickSize, bufferTicks: 2)
                                    : MyNamespace.Strategies.Models.HtfZoneType.None;

                                var v2Input = new MyNamespace.Strategies.Models.MarketStateInputV2(
                                    bar: feat?.Bar ?? ovSnapshot.Bar,
                                    high: ovSnapshot.High,
                                    low: ovSnapshot.Low,
                                    close: ovSnapshot.Close,
                                    vwap: _currentVwapSnapshot.Current,
                                    upperBand1: _currentVwapSnapshot.UpperBand1,
                                    lowerBand1: _currentVwapSnapshot.LowerBand1,
                                    upperBand2: _currentVwapSnapshot.UpperBand2,
                                    lowerBand2: _currentVwapSnapshot.LowerBand2,
                                    upperBand3: _currentVwapSnapshot.UpperBand3,
                                    lowerBand3: _currentVwapSnapshot.LowerBand3,
                                    currentVah: currentVAH,
                                    currentVal: currentVAL,
                                    candlePocPrice: ovSnapshot.CandlePocPrice,
                                    regime: localCurrentRegime,
                                    htfSwingHigh: htfSwingHigh,
                                    htfSwingLow: htfSwingLow,
                                    isInHtfZone: isInHtfZone,
                                    htfZoneType: htfZoneType);

                                _currentMarketStateV2 = _marketStateEngineV2.Update(v2Input);
                            }
                        }
                        catch (Exception exV2Pre)
                        {
                            this.LogWarn($"[OnCalculate MarketStateV2-pre] failed: {exV2Pre.GetType().Name}: {exV2Pre.Message}");
                        }

                        if (_currentMarketStateV2 == null)
                            _currentMarketStateV2 = new MyNamespace.Strategies.Models.MarketStateV2 { Dynamic = localCurrentRegime };

                        var localCurrentDirectionalBias = _currentMarketStateV2.Bias;
                        var localCurrentMarketStateForDetector = _currentMarketStateV2;

                        if (_marketStructureContext != null && closed >= 0)
                        {
                            try
                            {
                                var cndl = GetCandle(closed);
                                if (cndl != null)
                                {
                                    var mcndl = new IndicatorCandleAdapter(cndl, ovSnapshot?.NetDeltaTotal ?? 0m);
                                    var vwapNow = _currentVwapSnapshot?.Current ?? 0m;
                                    int maxReadyZoneIdBefore = -1;
                                    try
                                    {
                                        var zonesBefore = _marketStructureContext.ActiveZones;
                                        if (zonesBefore != null && zonesBefore.Count > 0)
                                        {
                                            for (int zi = 0; zi < zonesBefore.Count; zi++)
                                            {
                                                var z = zonesBefore[zi];
                                                if (z == null)
                                                    continue;
                                                if (z.Status != MyNamespace.Strategies.MarketAnalysis.MarketStructureContext.ZoneStatus.Ready)
                                                    continue;
                                                if (z.Id > maxReadyZoneIdBefore)
                                                    maxReadyZoneIdBefore = z.Id;
                                            }
                                        }
                                    }
                                    catch { maxReadyZoneIdBefore = -1; }
                                    try
                                    {
                                        _marketStructureContext.ZigZagSensitivity = MarketStructureZigZagSensitivity;
                                        _marketStructureContext.WickZoneMinTicks = GetEffectiveMarketStructureWickMinTicks(currentMarketRegime);
                                    }
                                    catch { }
                                    _marketStructureContext.Update(
                                        closed,
                                        mcndl,
                                        ovSnapshot,
                                        tickSize: _tickSize,
                                        vwap: vwapNow,
                                        recentOf: null,
                                        allowZoneCreation: !UseTick900ForMarketStructure,
                                        allowZoneLifecycle: true);

                                    try
                                    {
                                        int maxReadyZoneIdAfter = -1;
                                        var zonesAfter = _marketStructureContext.ActiveZones;
                                        if (zonesAfter != null && zonesAfter.Count > 0)
                                        {
                                            for (int zi = 0; zi < zonesAfter.Count; zi++)
                                            {
                                                var z = zonesAfter[zi];
                                                if (z == null)
                                                    continue;
                                                if (z.Status != MyNamespace.Strategies.MarketAnalysis.MarketStructureContext.ZoneStatus.Ready)
                                                    continue;
                                                if (z.Id > maxReadyZoneIdAfter)
                                                    maxReadyZoneIdAfter = z.Id;
                                            }
                                        }

                                        bool notAlreadyTriggeredForClosed = closed != _lastIntrabarZoneTriggerClosed;
                                        bool newPendingZoneAppearedSameClosed = (closed == _lastIntrabarZoneTriggerClosed) && (maxReadyZoneIdAfter > _lastIntrabarZoneTriggerMaxPendingId);
                                        if (maxReadyZoneIdAfter >= 0 && maxReadyZoneIdAfter > maxReadyZoneIdBefore && (notAlreadyTriggeredForClosed || newPendingZoneAppearedSameClosed))
                                        {
                                            this.LogInfo($"[OnCalculate-INTRABAR-ZONE-AFTER-UPDATE] Ready zone created during MarketStructure.Update (maxReadyId {maxReadyZoneIdAfter} > {maxReadyZoneIdBefore}) at bar={bar}, closed={closed} -> EvaluateSignalsAndOrders NOW");
                                            EvaluateSignalsAndOrders(
                                                caller: "IntrabarZoneAfterUpdate",
                                                closed: closed,
                                                ovLastClosed: ovSnapshot,
                                                ofFeaturesHistory: _ofFeaturesHistory,
                                                isBlockedLong: _currentLevelsSnapshot.IsBlockedLong,
                                                isBlockedShort: _currentLevelsSnapshot.IsBlockedShort,
                                                levelsSnapshot: _currentLevelsSnapshot,
                                                currentPOC_Explicit: _currentLevelsSnapshot.CurrentPOC,
                                                currentVAH_Explicit: _currentLevelsSnapshot.CurrentVAH,
                                                currentVAL_Explicit: _currentLevelsSnapshot.CurrentVAL,
                                                currentMarketRegime: _marketRegimeDetails != null ? _marketRegimeDetails.Regime : MarketRegime.None,
                                                detectedPattern: new MyNamespace.Strategies.Models.DetectedOrderflowPattern(),
                                                currentMarketState: _currentMarketStateV2,
                                                currentVwap: _currentVwapSnapshot?.Current ?? 0m);
                                            _lastIntrabarZoneTriggerClosed = closed;

                                            _lastIntrabarZoneTriggerMaxPendingId = maxReadyZoneIdAfter;
                                            _lastSeenMarketStructureMaxReadyZoneId = Math.Max(_lastSeenMarketStructureMaxReadyZoneId, maxReadyZoneIdAfter);
                                        }
                                    }
                                    catch { }

                                    try
                                    {
                                        if (_marketStructureContext != null && ovSnapshot != null)
                                            EnsurePreviousDayLevelZonesInMarketStructure(_marketStructureContext, ovSnapshot, createdBar: closed, currentDay: c.Time.Date);
                                    }
                                    catch { }
                                }
                            }
                            catch (Exception exMs)
                            {
                                this.LogWarn($"[MarketStructureContext] Update failed: {exMs.GetType().Name}: {exMs.Message}");
                            }
                        }

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

                        // Konfiguration / Anzahl der Evaluatoren
                        var configuredPatternCount = _strategySetup?.PatternDefaultThresholds?.Count ?? 0;
                        int evaluatorCount = 0;
                        string evaluatorPreview = string.Empty;
                        try
                        {
                            var evals = _patternRunner?.Evaluators;
                            evaluatorCount = evals?.Count ?? 0;
                            if (evals != null)
                            {
                                evaluatorPreview = string.Join(",", evals.Take(6).Select(e => e == null ? "(null)" : $"{e.GetType().Name}|{e.Type}|{e.Direction}"));
                            }
                            else
                            {
                                evaluatorPreview = "n/a";
                            }
                        }
                        catch (Exception ex)
                        {
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


                        if (_patternRunner == null)
                        {
                            this.LogWarn("[OnCalculate NEW LOG] _patternRunner ist null; DetectDominantOrderflowPattern wird nicht aufgerufen.");
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

                            var result = _patternRunner.DetectDominantOrderflowPattern(
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

                        // --- MarketStateEngineV2 (Player 1) ---
                        try
                        {
                            if (_marketStateEngineV2 != null && ovSnapshot != null && _currentVwapSnapshot != null)
                            {
                                var htfSwingHigh = _marketStructureContext?.LastConfirmedSwingHigh;
                                var htfSwingLow = _marketStructureContext?.LastConfirmedSwingLow;
                                bool isInHtfZone = _marketStructureContext != null && _marketStructureContext.IsInAnyActiveZone(ovSnapshot.Close, _tickSize, bufferTicks: 2);

                                var htfZoneType = _marketStructureContext != null
                                    ? _marketStructureContext.GetHtfZoneType(ovSnapshot.Close, _tickSize, bufferTicks: 2)
                                    : MyNamespace.Strategies.Models.HtfZoneType.None;

                                var v2Input = new MyNamespace.Strategies.Models.MarketStateInputV2(
                                    bar: feat?.Bar ?? ovSnapshot.Bar,
                                    high: ovSnapshot.High,
                                    low: ovSnapshot.Low,
                                    close: ovSnapshot.Close,
                                    vwap: _currentVwapSnapshot.Current,
                                    upperBand1: _currentVwapSnapshot.UpperBand1,
                                    lowerBand1: _currentVwapSnapshot.LowerBand1,
                                    upperBand2: _currentVwapSnapshot.UpperBand2,
                                    lowerBand2: _currentVwapSnapshot.LowerBand2,
                                    upperBand3: _currentVwapSnapshot.UpperBand3,
                                    lowerBand3: _currentVwapSnapshot.LowerBand3,
                                    currentVah: currentVAH,
                                    currentVal: currentVAL,
                                    candlePocPrice: ovSnapshot.CandlePocPrice,
                                    regime: localCurrentRegime,
                                    htfSwingHigh: htfSwingHigh,
                                    htfSwingLow: htfSwingLow,
                                    isInHtfZone: isInHtfZone,
                                    htfZoneType: htfZoneType);

                                _currentMarketStateV2 = _marketStateEngineV2.Update(v2Input);

                                decimal sd1 = _currentVwapSnapshot.UpperBand1 > 0m
                                    ? (_currentVwapSnapshot.UpperBand1 - _currentVwapSnapshot.Current)
                                    : 0m;
                                bool inVa = currentVAH > 0m && currentVAL > 0m && ovSnapshot.Close >= currentVAL && ovSnapshot.Close <= currentVAH;

                                string PhaseDe(MyNamespace.Strategies.Models.MarketPhaseV2? p)
                                {
                                    if (!p.HasValue) return "n/a";
                                    switch (p.Value)
                                    {
                                        case MyNamespace.Strategies.Models.MarketPhaseV2.Trend_Impulse: return "Starker Trend-Schub";
                                        case MyNamespace.Strategies.Models.MarketPhaseV2.Healthy_Pullback: return "Gesunder Rücksetzer";
                                        case MyNamespace.Strategies.Models.MarketPhaseV2.Momentum_Refuel: return "Trend-Luftholen";
                                        case MyNamespace.Strategies.Models.MarketPhaseV2.Range_Balanced: return "Seitwärts-Gleichgewicht";
                                        case MyNamespace.Strategies.Models.MarketPhaseV2.Exhaustion: return "Markt-Erschöpfung";
                                        case MyNamespace.Strategies.Models.MarketPhaseV2.Volatile_Breakout: return "Dynamischer Ausbruch";
                                        case MyNamespace.Strategies.Models.MarketPhaseV2.Maturing_Trend: return "Ermüdender Trend";
                                        default: return p.Value.ToString();
                                    }
                                }

                                string BiasDe(MyNamespace.Strategies.Models.MarketBiasV2? b)
                                {
                                    if (!b.HasValue) return "n/a";
                                    switch (b.Value)
                                    {
                                        case MyNamespace.Strategies.Models.MarketBiasV2.Long: return "Long";
                                        case MyNamespace.Strategies.Models.MarketBiasV2.Short: return "Short";
                                        case MyNamespace.Strategies.Models.MarketBiasV2.Neutral: return "Neutral";
                                        default: return b.Value.ToString();
                                    }
                                }

                                string VwapTrendstaerkeDe(decimal? zSlope)
                                {
                                    if (!zSlope.HasValue) return "n/a";
                                    var a = Math.Abs(zSlope.Value);
                                    if (a < 0.8m) return "niedrig";
                                    if (a < 1.5m) return "mittel";
                                    return "hoch";
                                }

                                string PocTreppenstrukturDe(int? staircase)
                                {
                                    if (!staircase.HasValue) return "n/a";
                                    if (staircase.Value >= 3) return "eher aufwärts-stufig";
                                    if (staircase.Value <= -3) return "eher abwärts-stufig";
                                    return "eher unstrukturiert / Range";
                                }

                                string VolatilitaetDe(decimal? volMult, decimal sd1)
                                {
                                    if (!volMult.HasValue)
                                        return sd1 > 0m ? "mittel" : "n/a";

                                    if (volMult.Value < 0.9m) return "niedrig";
                                    if (volMult.Value > 1.1m) return "hoch";
                                    return "mittel";
                                }

                                string VolaKurzDe(string vola)
                                {
                                    if (string.IsNullOrWhiteSpace(vola))
                                        return "n/a";
                                    if (string.Equals(vola, "hoch", StringComparison.OrdinalIgnoreCase))
                                        return "Hoch";
                                    if (string.Equals(vola, "niedrig", StringComparison.OrdinalIgnoreCase))
                                        return "Niedrig";
                                    if (string.Equals(vola, "mittel", StringComparison.OrdinalIgnoreCase))
                                        return "Normal";
                                    return vola;
                                }

                                string StrukturbruchDe(MyNamespace.Strategies.MarketAnalysis.SwingStructureBias? d)
                                {
                                    if (!d.HasValue) return "n/a";
                                    switch (d.Value)
                                    {
                                        case MyNamespace.Strategies.MarketAnalysis.SwingStructureBias.Bullish: return "Aufwärts";
                                        case MyNamespace.Strategies.MarketAnalysis.SwingStructureBias.Bearish: return "Abwärts";
                                        case MyNamespace.Strategies.MarketAnalysis.SwingStructureBias.None: return "—";
                                        default: return d.Value.ToString();
                                    }
                                }

                                var volaKurz = VolaKurzDe(VolatilitaetDe(_currentMarketStateV2?.VolatilityMultiplier, sd1));
                                _marketStateV2OverlayText = $"{PhaseDe(_currentMarketStateV2?.Phase)} | Bias={BiasDe(_currentMarketStateV2?.Bias)} | Vola={volaKurz} | Trend-Fortsetzung ok={(_currentMarketStateV2?.IsTrendContinuing == true ? "Ja" : "Nein")} | Letzter Strukturbruch={StrukturbruchDe(_currentMarketStateV2?.SwingLastBreakDirection)}";

                                var v2Sig = SmartLogger.ComposeSignature(
                                    ("bar", (feat?.Bar ?? ovSnapshot.Bar).ToString()),
                                    ("phase", (_currentMarketStateV2?.Phase.ToString() ?? "n/a")),
                                    ("bias", (_currentMarketStateV2?.Bias.ToString() ?? "n/a")));
                                SmartLogger.Instance.LogIfChanged(
                                    category: "MarketStateV2",
                                    sourceId: "MarketStateEngineV2",
                                    barIndex: feat?.Bar ?? ovSnapshot.Bar,
                                    message: $"Bar {feat?.Bar ?? ovSnapshot.Bar}: Phase={PhaseDe(_currentMarketStateV2?.Phase)} [{_currentMarketStateV2?.Phase}] | Bias={BiasDe(_currentMarketStateV2?.Bias)} | " +
                                             $"Volatilität={VolatilitaetDe(_currentMarketStateV2?.VolatilityMultiplier, sd1)} (Multiplikator={_currentMarketStateV2?.VolatilityMultiplier:F2}, 1σ={sd1:F2}) | " +
                                             $"TrendContinuing={(_currentMarketStateV2?.IsTrendContinuing == true ? "Ja" : "Nein")} | SwingBias={_currentMarketStateV2?.SwingBias} | SwingConf={_currentMarketStateV2?.SwingConfidence:F2} | SwingBarsSinceBreak={_currentMarketStateV2?.SwingBarsSinceBreak} | SwingLastBreakDir={_currentMarketStateV2?.SwingLastBreakDirection} | " +
                                             $"VWAP-Trendstärke={VwapTrendstaerkeDe(_currentMarketStateV2?.ZSlope)} | " +
                                             $"POC-Treppenstruktur={PocTreppenstrukturDe(_currentMarketStateV2?.StaircaseIndex)} (Index={_currentMarketStateV2?.StaircaseIndex}) | " +
                                             $"VA={(inVa ? "✅ drin" : "❌ draußen")} (VAL={currentVAL:F2}, VAH={currentVAH:F2}) | " +
                                             $"Kerzen-POC={ovSnapshot.CandlePocPrice:F2} | Tradeable={(_currentMarketStateV2?.IsTradeable == true ? "Ja" : "Nein")}",
                                    signature: v2Sig,
                                    backendLogAction: s => this.LogInfo($"[MarketStateV2] {s}")
                                );
                            }
                        }
                        catch (Exception exV2)
                        {
                            this.LogWarn($"[OnCalculate MarketStateV2] failed: {exV2.GetType().Name}: {exV2.Message}");
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

                    string thresholdsSnapshotJson = string.Empty;
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

                        string prunerNullifiedFieldsLocal = string.Empty;

                        // Compose and enqueue CSV line (if writer available)
                        string csvLine = null;

                        // Zus?tzliche Null-Pr?fungen vor der CSV-Verarbeitung
                        if (ovSnapshot == null)
                        {
                            //this.LogError("[OnCalculate-CSV_ERROR] ovSnapshot is null - skipping CSV processing");
                        }
                        else if (_csvWriter == null)
                        {
                            //this.LogError("[OnCalculate-CSV_ERROR] _csvWriter is null - skipping CSV processing");
                        }
                        else
                        {
                            try
                            {
                                // extract historyVersion + createdAt from thresholds result (defensive)
                                int hv = -1;
                                DateTime? createdAt = null;

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
                                //this.LogError($"[OnCalculate-CSV_ERROR] ToCsvLine formatting failed for Bar={ovSnapshot.Bar}. Exception: {exFmt}");
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
                                    //this.LogError($"[OnCalculate-CSV_ERROR] EnqueueLine failed for Bar={ovSnapshot.Bar}. Exception: {exEnq}");
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

                    // Wenn wir für diesen closed-Bar bereits einen Intrabar-Pending-Trigger hatten,
                    // aktivieren wir den Intrabar-Immediate-Place-Modus, damit placeBar=bar (forming) statt closed verwendet wird.
                    bool useIntrabarPlaceMode = false;

                    // WICHTIG: IsNewBar-Pfad darf niemals mit Intrabar-Immediate-Place arbeiten.
                    // Falls der Flag aus einem vorherigen Intrabar/Pending-Zweig "leakt" (z.B. Exception-Pfad),
                    // erzwingen wir hier scoped: off.
                    bool prevIntrabarImmediatePlaceMode = _intrabarImmediatePlaceMode;
                    int prevIntrabarImmediatePlaceBar = _intrabarImmediatePlaceBar;
                    _intrabarImmediatePlaceMode = false;
                    _intrabarImmediatePlaceBar = -1;

                    //this.LogInfo($"[OnCalculate] Calling EvaluateSignalsAndOrders for closed={closed}, lastEvalBar(before)={_lastEvalBar}");
                    //this.LogInfo($"[DEBUG] Vor EvaluateSignalsAndOrders: Bar={bar}, POC={currentPOC}, VAH={currentVAH}, VAL={currentVAL}");
                    //this.LogInfo($"[DBG] Enter isNewBar: bar={bar}, closed={closed}, now={DateTime.UtcNow:O}, CurrentBar={CurrentBar}, _lastEvalBar={_lastEvalBar}");
                    try
                    {
                        EvaluateSignalsAndOrders(
                            caller: "IsNewBar",
                            closed: closed,
                            ovLastClosed: ovSnapshot,
                            ofFeaturesHistory: _ofFeaturesHistory,
                            isBlockedLong: isBlockedLong,
                            isBlockedShort: isBlockedShort,
                            levelsSnapshot: _currentLevelsSnapshot,
                            currentPOC_Explicit: currentPOC,
                            currentVAH_Explicit: currentVAH,
                            currentVAL_Explicit: currentVAL,
                            currentMarketRegime: currentMarketRegime,
                            detectedPattern: detectedPattern,
                            currentMarketState: _currentMarketStateV2,
                            currentVwap: _currentVwapSnapshot?.Current ?? 0m);
                    }
                    finally
                    {
                        if (useIntrabarPlaceMode)
                        {
                            _intrabarImmediatePlaceMode = false;
                            _intrabarImmediatePlaceBar = -1;
                        }

                        // Restore previous intrabar immediate place state (defensive).
                        _intrabarImmediatePlaceMode = prevIntrabarImmediatePlaceMode;
                        _intrabarImmediatePlaceBar = prevIntrabarImmediatePlaceBar;
                    }
                    _lastEvalBar = closed;

                    //this.LogInfo($"[OnCalculate] EvaluateSignalsAndOrders completed for closed={closed}; setting _lastEvalBar={closed}");
                }
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
                        int timeoutBaseBarIndex = _entryTimeoutStartBarIndex >= 0 ? _entryTimeoutStartBarIndex : _entryBarIndex;
                        int timeoutBarIndex = timeoutBaseBarIndex + OrderTimeoutBars; // $$timeoutBarIndex = _entryBarIndex + OrderTimeoutBars$$

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
                                    _entryTimeoutStartBarIndex = -1;
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



                decimal currentMarketPrice = Security?.LastTradePrice ?? 0m;
                if (currentMarketPrice <= 0m)  // FIX: Ung?ltiger Preis ? Abbruch
                {
                    this.LogWarn("[OnCalculate] Pullback-Trailing: Ung?ltiger Market-Price, skip.");
                    return;
                }
                bool shouldModifyOrder = false;
                decimal newTriggerPrice = 0m;
                decimal newLimitPrice = 0m;

                decimal tick = InstrumentInfo?.TickSize ?? _tickSize;
                if (tick <= 0m) tick = 0.25m;

                // Preisquelle: für Long eher Ask, für Short eher Bid; fallback auf Last.
                var bestAsk = Security?.BestAskPrice;
                var bestBid = Security?.BestBidPrice;
                decimal refPriceLong = (bestAsk.HasValue && bestAsk.Value > 0m) ? bestAsk.Value : currentMarketPrice;
                decimal refPriceShort = (bestBid.HasValue && bestBid.Value > 0m) ? bestBid.Value : currentMarketPrice;

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
                    if (!_isPullbackMode && refPriceLong <= _pullbackTriggerPrice + tick)
                    {
                        this.LogInfo($"[OnCalculate] Long Pullback ber?hrt (Px={refPriceLong} <= Trigger={_pullbackTriggerPrice}). Wechsle zu trailing StopLimit.");
                        CancelOrder(po);

                        // Long: StopLimit X Ticks ÜBER dem aktuellen Preis; Fill erst beim Reversal nach oben.
                        decimal trg = refPriceLong + (PullbackTicksTrail * tick);
                        decimal lim = trg + tick; // kleiner Puffer, um "triggered-but-not-filled" zu reduzieren
                        _pullbackOrder = CreateStopLimitOrder(OrderDirections.Buy, trg, lim);
                        _isPullbackMode = true;
                        OpenOrder(_pullbackOrder);
                        return; // wichtig: diesen Tick hier beenden
                    }
                    else if (_isPullbackMode && EnableContinuousTrailing)
                    {
                        // Wenn der Preis weiter fällt, soll der Stop-Trigger mit nach unten wandern,
                        // aber immer X Ticks ÜBER dem Preis bleiben.
                        decimal potentialTrigger = refPriceLong + (PullbackTicksTrail * tick);
                        if (po.TriggerPrice > 0m && potentialTrigger < po.TriggerPrice)
                        {
                            newTriggerPrice = potentialTrigger;
                            newLimitPrice = potentialTrigger + tick;
                            shouldModifyOrder = true;
                        }
                    }
                }
                else if (po.Direction == OrderDirections.Sell)
                {
                    if (!_isPullbackMode && refPriceShort >= _pullbackTriggerPrice - tick)
                    {
                        this.LogInfo($"[OnCalculate] Short Pullback ber?hrt (Px={refPriceShort} >= Trigger={_pullbackTriggerPrice}). Wechsle zu trailing StopLimit.");
                        CancelOrder(po);

                        // Short: StopLimit X Ticks UNTER dem aktuellen Preis; Fill erst beim Reversal nach unten.
                        decimal trg = refPriceShort - (PullbackTicksTrail * tick);
                        decimal lim = trg - tick; // kleiner Puffer
                        _pullbackOrder = CreateStopLimitOrder(OrderDirections.Sell, trg, lim);
                        _isPullbackMode = true;
                        OpenOrder(_pullbackOrder);
                        return; // wichtig
                    }
                    else if (_isPullbackMode && EnableContinuousTrailing)
                    {
                        // Wenn der Preis weiter steigt, soll der Stop-Trigger mit nach oben wandern,
                        // aber immer X Ticks UNTER dem Preis bleiben.
                        decimal potentialTrigger = refPriceShort - (PullbackTicksTrail * tick);
                        if (po.TriggerPrice > 0m && potentialTrigger > po.TriggerPrice)
                        {
                            newTriggerPrice = potentialTrigger;
                            newLimitPrice = potentialTrigger - tick;
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
                        modifiedOrder.TriggerPrice = newTriggerPrice;
                        modifiedOrder.Price = newLimitPrice;
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
            catch (Exception ex)
            {
                this.LogError($"[OnCalculate-FATAL] bar={bar} CurrentBar={CurrentBar} ex={ex}");
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
            var mcForLevels = EnableMicroCompositeSystem ? (_currentMC ?? GetRollingMicroComposite()) : null;
            if (mcForLevels != null)
            {
                // Dynamischer POC, VAH, VAL aus MicroComposite
                if (mcForLevels.POC > 0)
                    significantLevels.Add((mcForLevels.POC, "MC.POC"));
                if (mcForLevels.VAH > 0)
                    significantLevels.Add((mcForLevels.VAH, "MC.VAH"));
                if (mcForLevels.VAL > 0)
                    significantLevels.Add((mcForLevels.VAL, "MC.VAL"));

                // HVN Zonen -> Kanten nutzen (entspricht gerenderten Rechtecken)
                if (UseMicroCompositeHVNsForWegFreiAndDynamicTP)
                {
                    foreach (var hvnZone in mcForLevels.HVNZones)
                    {
                        if (hvnZone.Start > 0) significantLevels.Add((hvnZone.Start, "MC.HVNZone.Start"));
                        if (hvnZone.End > 0) significantLevels.Add((hvnZone.End, "MC.HVNZone.End"));
                    }
                }

                // LVN (Low Volume Nodes) werden entfernt - nur HVN f?r TP-Berechnung verwenden

                // LVN-Zonen werden entfernt

                this.LogDebug($"[LEVEL] MicroComposite hinzugef?gt: POC={_currentMC.POC:F2}, VAH={_currentMC.VAH:F2}, VAL={_currentMC.VAL:F2}, " +
                             $"HVNs={_currentMC.HVNs.Count}, LVNs={_currentMC.LVNs.Count}");
            }

            // Daily HVN Zonen als TP-Kandidaten (nur Kanten) - unabhängig vom MicroComposite.
            if (_dailyProfileForPath != null && _dailyProfileForPath.HVNZones != null)
            {
                foreach (var z in _dailyProfileForPath.HVNZones)
                {
                    if (z.Start > 0) significantLevels.Add((z.Start, "D.HVNZone.Start"));
                    if (z.End > 0) significantLevels.Add((z.End, "D.HVNZone.End"));
                }
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

        private void EvaluateSignalsAndOrders(string caller, int closed, OvSnapshot ovLastClosed, Orderflow.OfFeaturesHistory ofFeaturesHistory, bool isBlockedLong, bool isBlockedShort, LevelsSnapshot levelsSnapshot, decimal currentPOC_Explicit, decimal currentVAH_Explicit, decimal currentVAL_Explicit, MarketRegime currentMarketRegime, DetectedOrderflowPattern detectedPattern, MyNamespace.Strategies.Models.MarketStateV2 currentMarketState, decimal currentVwap)
        {
            //this.LogInfo($"[DBG-EVAL] Start EvaluateSignalsAndOrders(closed={closed}) at {DateTime.UtcNow:O}");

            if (closed < 0 || closed > CurrentBar || (closed == CurrentBar && !_intrabarImmediatePlaceMode))
            {
                if (_intrabarImmediatePlaceMode)
                    this.LogInfo($"[INTRABAR-IMMEDIATE-SKIP] reason=closed_out_of_range closed={closed} CurrentBar={CurrentBar}");
                return;
            }
            //this.LogInfo($"[DBG] Pre-Eval bar={bar}, hasZ=true, Z={Volumenburst:F6}, CurrentBar={CurrentBar}");

            int? entryClosedOverride = null;
            int? entryChartBarOverride = null;
            try
            {
                if (detectedPattern?.MatchedCriteriaValues != null && detectedPattern.MatchedCriteriaValues.Count > 0)
                {
                    if (detectedPattern.MatchedCriteriaValues.TryGetValue("EntryClosedOverride", out var eIdxObj) && eIdxObj != null)
                    {
                        if (eIdxObj is int ei)
                            entryClosedOverride = ei;
                        else if (eIdxObj is long el)
                            entryClosedOverride = (int)el;
                        else if (int.TryParse(eIdxObj.ToString(), out var eParsed))
                            entryClosedOverride = eParsed;
                    }

                    if (detectedPattern.MatchedCriteriaValues.TryGetValue("EntryChartBarOverride", out var eChartObj) && eChartObj != null)
                    {
                        if (eChartObj is int eci)
                            entryChartBarOverride = eci;
                        else if (eChartObj is long ecl)
                            entryChartBarOverride = (int)ecl;
                        else if (int.TryParse(eChartObj.ToString(), out var ecParsed))
                            entryChartBarOverride = ecParsed;
                    }
                }
            }
            catch { }

            // Guard: Retro-Entry in Pending-Zone nur zulassen, wenn das Signalbar direkt vor der aktuell entstehenden Forming-Bar liegt.
            // (Sonst ist Entry/Preis-Kontext veraltet, wenn der Markt bereits mehrere Bars weiter ist.)
            int? pendingSignalBarUsedForDedupe = null;
            try
            {
                if (caller == "IntrabarPending")
                {
                    int placeBarForPending = (_intrabarImmediatePlaceMode && _intrabarImmediatePlaceBar >= 0)
                        ? _intrabarImmediatePlaceBar
                        : closed;
                    int requiredSignalBar = placeBarForPending - 1;

                    int signalBarUsed = entryClosedOverride ?? closed;
                    pendingSignalBarUsedForDedupe = signalBarUsed;
                    if (signalBarUsed != requiredSignalBar)
                    {
                        this.LogInfo(
                            $"[SETUP-BLOCKED-RETRO-TIMING] caller={caller} pattern={detectedPattern?.Type} dir={detectedPattern?.Direction} " +
                            $"CurrentBar={CurrentBar} requiredSignalBar={requiredSignalBar} signalBarUsed={signalBarUsed} " +
                            $"EntryClosedOverride={(entryClosedOverride.HasValue ? entryClosedOverride.Value.ToString() : "-")} closed={closed} " +
                            $"intrabarMode={_intrabarImmediatePlaceMode} intrabarPlaceBar={_intrabarImmediatePlaceBar}");
                        return;
                    }

                    if (_lastPendingSignalBarPlaced == signalBarUsed)
                    {
                        this.LogInfo($"[PENDING-DEDUPE] Skip: signalBar={signalBarUsed} already used for pending entry.");
                        return;
                    }
                }
            }
            catch { }

            int placeBar = closed;
            if (_intrabarImmediatePlaceMode && _intrabarImmediatePlaceBar >= 0)
            {
                placeBar = _intrabarImmediatePlaceBar;
                this.LogInfo($"[EvaluateSignalsAndOrders] Intrabar immediate place mode: caller={caller} placeBar={placeBar} (was closed={closed})");
            }
            int placeChart = placeBar + 1;

            int setupBar = closed;
            if (caller == "IntrabarPending")
            {
                setupBar = CurrentBar - 1;
            }
            else if (entryClosedOverride.HasValue && entryClosedOverride.Value >= 0 && entryClosedOverride.Value < CurrentBar)
            {
                setupBar = entryClosedOverride.Value;
            }

            if (detectedPattern != null && detectedPattern.IsDetected && setupBar >= 0)
            {
                if (_lastEntryAttemptSetupBar == setupBar)
                {
                    if (_intrabarImmediatePlaceMode)
                        this.LogInfo($"[INTRABAR-IMMEDIATE-SKIP] reason=entry_dedupe_same_setup_bar caller={caller} setupBar={setupBar} closed={closed} placeBar={( _intrabarImmediatePlaceMode && _intrabarImmediatePlaceBar >= 0 ? _intrabarImmediatePlaceBar : closed)} pattern={detectedPattern.Type} dir={detectedPattern.Direction}");
                    this.LogInfo($"[ENTRY-DEDUPE] Skip duplicate entry evaluation: setupBar={setupBar} closed={closed} caller={caller} pattern={detectedPattern.Type} dir={detectedPattern.Direction}");
                    return;
                }
                _lastEntryAttemptSetupBar = setupBar;
            }

            var c = GetCandle(setupBar);
            var p = setupBar > 0 ? GetCandle(setupBar - 1) : c;
            double seconds = (c.Time - p.Time).TotalSeconds;
            if (seconds <= 0) seconds = 1;
            //this.LogInfo($"[Eval] bar={bar}, time={c.Time:O}");
            bool inSession = IsWithinAnyTradingSession(setupBar);
            var tickSize = InstrumentInfo?.TickSize ?? _tickSize;
            int riskTicks = GetRiskTicks();
            bool wegFreiLong = true;
            bool wegFreiShort = true;
            string wegFreiLongBlocker = string.Empty;
            string wegFreiShortBlocker = string.Empty;

            bool wegFreiLongMC = true;
            bool wegFreiShortMC = true;
            string wegFreiLongBlockerMC = string.Empty;
            string wegFreiShortBlockerMC = string.Empty;

            bool wegFreiLongD = true;
            bool wegFreiShortD = true;
            string wegFreiLongBlockerD = string.Empty;
            string wegFreiShortBlockerD = string.Empty;

            if ((c.Time - _lastDailyWegFreiDebugHeartbeatTime).TotalSeconds >= 5)
            {
                _lastDailyWegFreiDebugHeartbeatTime = c.Time;
                //this.LogInfo($"[DailyWegFreiDebug] PreCheck closed={closed} EnableDailyProfilePathSystem={EnableDailyProfilePathSystem} dailyProfileForPathNull={(_dailyProfileForPath == null)} dailyProfileForVisualNull={(_dailyProfileForVisual == null)} DailyHVNStrength={DailyHVNStrength} DailyDMinTicks={DailyDMinTicks} close={c.Close:F2}");
            }

            if (EnableMicroCompositeSystem)
            {
                var mcForPath = _currentMC ?? GetRollingMicroComposite();
                wegFreiLongMC = IsPathFreeLongEnhanced(c.Close, DMinTicks, riskTicks, mcForPath, null, tickSize, GetPathConfig(), out wegFreiLongBlockerMC);
                wegFreiShortMC = IsPathFreeShortEnhanced(c.Close, DMinTicks, riskTicks, mcForPath, null, tickSize, GetPathConfig(), out wegFreiShortBlockerMC);
            }

            if (EnableDailyProfilePathSystem && _dailyProfileForPath != null)
            {
                var dailyCfg = GetDailyPathConfig();

                if (DailyHVNStrength <= 0 && !dailyCfg.ForceAllHVNsStrong)
                    //this.LogInfo($"[DailyWegFreiDebug] DailyHVNStrength={DailyHVNStrength} but dailyCfg.ForceAllHVNsStrong={dailyCfg.ForceAllHVNsStrong} ForceAllHVNsWeak={dailyCfg.ForceAllHVNsWeak}");

                wegFreiLongD = IsPathFreeLongEnhanced(c.Close, DailyDMinTicks, riskTicks, _dailyProfileForPath, _prevDailyProfileForPath, tickSize, dailyCfg, out wegFreiLongBlockerD);
                wegFreiShortD = IsPathFreeShortEnhanced(c.Close, DailyDMinTicks, riskTicks, _dailyProfileForPath, _prevDailyProfileForPath, tickSize, dailyCfg, out wegFreiShortBlockerD);

                if (DailyHVNStrength <= 0 && wegFreiLongD)
                {
                    int hvnPts = _dailyProfileForPath.HVNs?.Count ?? 0;
                    int hvnZones = _dailyProfileForPath.HVNZones?.Count ?? 0;
                    int? nearestTicks = null;
                    decimal? nearestPrice = null;
                    string nearestKind = string.Empty;

                    if (_dailyProfileForPath.HVNZones != null)
                    {
                        foreach (var z in _dailyProfileForPath.HVNZones)
                        {
                            decimal start = z.Start;
                            decimal end = z.End;
                            int dS = TicksBetweenAbs(start, c.Close);
                            int dE = TicksBetweenAbs(end, c.Close);
                            int d = Math.Min(dS, dE);
                            if (!nearestTicks.HasValue || d < nearestTicks.Value)
                            {
                                nearestTicks = d;
                                nearestPrice = (dS <= dE) ? start : end;
                                nearestKind = "HVNZoneEdge";
                            }
                        }
                    }

                    if (_dailyProfileForPath.HVNs != null)
                    {
                        foreach (var hvn in _dailyProfileForPath.HVNs)
                        {
                            int d = TicksBetweenAbs(hvn, c.Close);
                            if (!nearestTicks.HasValue || d < nearestTicks.Value)
                            {
                                nearestTicks = d;
                                nearestPrice = hvn;
                                nearestKind = "HVN";
                            }
                        }
                    }

                    if (nearestTicks.HasValue && nearestTicks.Value <= DailyDMinTicks)
                    {
                        //this.LogInfo($"[DailyWegFreiDebug] Allowed LONG despite DailyHVNStrength={DailyHVNStrength} ForceStrong={dailyCfg.ForceAllHVNsStrong} hvnPts={hvnPts} hvnZones={hvnZones} nearest={nearestKind}@{nearestPrice:F2} distTicks={nearestTicks} DailyDMinTicks={DailyDMinTicks} close={c.Close:F2}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(wegFreiLongBlockerD))
                    wegFreiLongBlockerD = wegFreiLongBlockerD.StartsWith("MC.", StringComparison.OrdinalIgnoreCase)
                        ? "D." + wegFreiLongBlockerD.Substring(3)
                        : (wegFreiLongBlockerD.StartsWith("LVN_PATH", StringComparison.OrdinalIgnoreCase) ? "D." + wegFreiLongBlockerD : "D." + wegFreiLongBlockerD);

                if (!string.IsNullOrWhiteSpace(wegFreiShortBlockerD))
                    wegFreiShortBlockerD = wegFreiShortBlockerD.StartsWith("MC.", StringComparison.OrdinalIgnoreCase)
                        ? "D." + wegFreiShortBlockerD.Substring(3)
                        : (wegFreiShortBlockerD.StartsWith("LVN_PATH", StringComparison.OrdinalIgnoreCase) ? "D." + wegFreiShortBlockerD : "D." + wegFreiShortBlockerD);
            }

            wegFreiLong = (!EnableMicroCompositeSystem || wegFreiLongMC) && (!EnableDailyProfilePathSystem || wegFreiLongD);
            wegFreiShort = (!EnableMicroCompositeSystem || wegFreiShortMC) && (!EnableDailyProfilePathSystem || wegFreiShortD);

            if (!wegFreiLong)
            {
                if (EnableMicroCompositeSystem && !wegFreiLongMC && !string.IsNullOrWhiteSpace(wegFreiLongBlockerMC))
                    wegFreiLongBlocker = wegFreiLongBlockerMC;
                if (EnableDailyProfilePathSystem && !wegFreiLongD && !string.IsNullOrWhiteSpace(wegFreiLongBlockerD))
                    wegFreiLongBlocker = string.IsNullOrWhiteSpace(wegFreiLongBlocker) ? wegFreiLongBlockerD : (wegFreiLongBlocker + " | " + wegFreiLongBlockerD);
            }

            if (!wegFreiShort)
            {
                if (EnableMicroCompositeSystem && !wegFreiShortMC && !string.IsNullOrWhiteSpace(wegFreiShortBlockerMC))
                    wegFreiShortBlocker = wegFreiShortBlockerMC;
                if (EnableDailyProfilePathSystem && !wegFreiShortD && !string.IsNullOrWhiteSpace(wegFreiShortBlockerD))
                    wegFreiShortBlocker = string.IsNullOrWhiteSpace(wegFreiShortBlocker) ? wegFreiShortBlockerD : (wegFreiShortBlocker + " | " + wegFreiShortBlockerD);
            }

            // VWAP-Blocker wird an der tatsächlichen Entry-Preislogik geprüft (siehe Order-Platzierung)



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
                if (_intrabarImmediatePlaceMode)
                    this.LogInfo($"[INTRABAR-IMMEDIATE-SKIP] reason=outside_session setupBar={setupBar} time={c.Time:O}");
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
                if (_intrabarImmediatePlaceMode)
                    this.LogInfo($"[INTRABAR-IMMEDIATE-SKIP] reason=trading_manager_null closed={closed} setupBar={setupBar}");
                this.LogInfo($"[EvaluateSignalAndOrders] TradingManager ist null!");
                return;
            }

            // 3. Pr?fen auf ausreichend historische Kerzen
            if (closed < 2)
            {
                if (_intrabarImmediatePlaceMode)
                    this.LogInfo($"[INTRABAR-IMMEDIATE-SKIP] reason=insufficient_history closed={closed}");
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
                if (_intrabarImmediatePlaceMode)
                    this.LogInfo($"[INTRABAR-IMMEDIATE-SKIP] reason=guards_open_pos_or_orders closed={closed} setupBar={setupBar} Pos={CurrentPosition} TMActive={hasActiveTradingManagerOrders} EntryPending={hasPendingEntryOrders} ExitPending={hasPendingExitOrders}");
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

            bool isLongSetupValid = false;
            bool isShortSetupValid = false;

            if (detectedPattern.IsDetected && detectedPattern.ConfidenceScore >= 0.30m)
            {
                const int pocKlebeZoneTicks = 6;

                if (levelsSnapshot != null)
                {
                    if (levelsSnapshot.PreviousDayPOC > 0m)
                    {
                        int pdPocDistTicks = Math.Abs(TicksBetween(c.Close, levelsSnapshot.PreviousDayPOC));
                        if (pdPocDistTicks <= pocKlebeZoneTicks)
                        {
                            if (_intrabarImmediatePlaceMode)
                                this.LogInfo($"[INTRABAR-IMMEDIATE-SKIP] reason=poc_cling_prev_day setupBar={setupBar} PdPOC={levelsSnapshot.PreviousDayPOC:F2} close={c.Close:F2} distTicks={pdPocDistTicks}");
                            this.LogInfo($"[SETUP-BLOCKED] Setup blockiert: Preis klebt am POC vom Vortag (PdPOC={levelsSnapshot.PreviousDayPOC:F2}, Close={c.Close:F2}, dist={pdPocDistTicks} ticks, zone=±{pocKlebeZoneTicks}).");
                            return;
                        }
                    }

                    if (levelsSnapshot.CurrentPOC > 0m)
                    {
                        int curPocDistTicks = Math.Abs(TicksBetween(c.Close, levelsSnapshot.CurrentPOC));
                        if (curPocDistTicks <= pocKlebeZoneTicks)
                        {
                            if (_intrabarImmediatePlaceMode)
                                this.LogInfo($"[INTRABAR-IMMEDIATE-SKIP] reason=poc_cling_current setupBar={setupBar} POC={levelsSnapshot.CurrentPOC:F2} close={c.Close:F2} distTicks={curPocDistTicks}");
                            this.LogInfo($"[SETUP-BLOCKED] Setup blockiert: Preis klebt am aktuellen POC (POC={levelsSnapshot.CurrentPOC:F2}, Close={c.Close:F2}, dist={curPocDistTicks} ticks, zone=±{pocKlebeZoneTicks}).");
                            return;
                        }
                    }
                }

                if ((detectedPattern.Type == OrderflowPatternType.PotentialLongReversalBounce
                        || detectedPattern.Type == OrderflowPatternType.PotentialLongTrendContinuation)
                    && detectedPattern.Direction == OrderDirections.Buy)
                {
                    if (c.Close >= c.Open)
                    {
                        isLongSetupValid = true;
                        this.LogInfo($"[SETUP-LONG] ✅ Long Setup VALID (V2): Pattern={detectedPattern.Type}, Confidence={detectedPattern.ConfidenceScore:F2}");
                    }
                    else
                    {
                        if (_intrabarImmediatePlaceMode)
                            this.LogInfo($"[INTRABAR-IMMEDIATE-SKIP] reason=signal_candle_not_bullish setupBar={setupBar} open={c.Open:F2} close={c.Close:F2} pattern={detectedPattern.Type} conf={detectedPattern.ConfidenceScore:0.00}");
                        this.LogInfo($"[SETUP-LONG-BLOCKED] Long Setup blockiert: Signalkerze nicht bullisch (Open={c.Open:F2}, Close={c.Close:F2}, Bar={closed}, ChartBar={closed + 1}, Time={c.Time:O}, Pattern={detectedPattern.Type}, Dir={detectedPattern.Direction}).");
                    }
                }
                else if ((detectedPattern.Type == OrderflowPatternType.PotentialShortReversalBounce
                            || detectedPattern.Type == OrderflowPatternType.PotentialShortTrendContinuation)
                         && detectedPattern.Direction == OrderDirections.Sell)
                {
                    if (c.Close <= c.Open)
                    {
                        isShortSetupValid = true;
                        this.LogInfo($"[SETUP-SHORT] ✅ Short Setup VALID (V2): Pattern={detectedPattern.Type}, Confidence={detectedPattern.ConfidenceScore:F2}");
                    }
                    else
                    {
                        if (_intrabarImmediatePlaceMode)
                            this.LogInfo($"[INTRABAR-IMMEDIATE-SKIP] reason=signal_candle_not_bearish setupBar={setupBar} open={c.Open:F2} close={c.Close:F2} pattern={detectedPattern.Type} conf={detectedPattern.ConfidenceScore:0.00}");
                        this.LogInfo($"[SETUP-SHORT-BLOCKED] Short Setup blockiert: Signalkerze nicht bärisch (Open={c.Open:F2}, Close={c.Close:F2}, Bar={closed}, ChartBar={closed + 1}, Time={c.Time:O}, Pattern={detectedPattern.Type}, Dir={detectedPattern.Direction}).");
                    }
                }
                else
                {
                    this.LogDebug($"[SETUP-VALID] Kein V2-Setup (Type={detectedPattern.Type}, Dir={detectedPattern.Direction}).");
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
                if (_intrabarImmediatePlaceMode && detectedPattern != null && detectedPattern.IsDetected)
                {
                    this.LogInfo($"[SETUP-NO-ORDER] caller={caller} Pattern detected but no valid setup -> no order. Pattern={detectedPattern.Type} Dir={detectedPattern.Direction} conf={detectedPattern.ConfidenceScore:0.00} setupBar={setupBar} open={c.Open:F2} close={c.Close:F2} isBlockedLong={isBlockedLong} isBlockedShort={isBlockedShort} wegFreiLong={wegFreiLong} wegFreiShort={wegFreiShort} vwap={currentVwap:F2}");
                }
                this.LogDebug("[SETUP-VALID] Derzeit kein g?ltiges Long- oder Short-Setup nach Pattern/Bias-Kriterien.");
            }



            // ========== SCHRITT 5: ORDERPLATZIERUNG ==========
            if (isLongSetupValid)
            {
                decimal longEntryPrice = 0;
                var setup = new SetupParams(); // Standard-Setup-Parameter
                string entryTriggerLevelName = string.Empty;
                decimal entryTriggerLevel = 0;

                int? signalBarIdxOverride = null;
                int? signalChartBarOverride = null;
                try
                {
                    if (detectedPattern?.MatchedCriteriaValues != null && detectedPattern.MatchedCriteriaValues.Count > 0)
                    {
                        if (detectedPattern.MatchedCriteriaValues.TryGetValue("SignalBarIndex", out var sIdxObj) && sIdxObj != null)
                        {
                            if (sIdxObj is int si)
                                signalBarIdxOverride = si;
                            else if (sIdxObj is long sl)
                                signalBarIdxOverride = (int)sl;
                            else if (int.TryParse(sIdxObj.ToString(), out var sParsed))
                                signalBarIdxOverride = sParsed;
                        }
                        if (detectedPattern.MatchedCriteriaValues.TryGetValue("SignalChartBarNumber", out var sChartObj) && sChartObj != null)
                        {
                            if (sChartObj is int sci)
                                signalChartBarOverride = sci;
                            else if (sChartObj is long scl)
                                signalChartBarOverride = (int)scl;
                            else if (int.TryParse(sChartObj.ToString(), out var scParsed))
                                signalChartBarOverride = scParsed;
                        }
                    }
                }
                catch { }

                decimal signalPoc = 0m;
                bool signalPocFromOverride = false;
                if (signalBarIdxOverride.HasValue)
                {
                    try
                    {
                        if (ofFeaturesHistory != null)
                        {
                            for (int i = 0; i < ofFeaturesHistory.Count; i++)
                            {
                                var s = ofFeaturesHistory.GetOfFeatures(i)?.Snapshot;
                                if (s != null && s.Bar == signalBarIdxOverride.Value)
                                {
                                    signalPoc = s.CandlePocPrice;
                                    signalPocFromOverride = true;
                                    if (!signalChartBarOverride.HasValue && s.ChartBarNumber > 0)
                                        signalChartBarOverride = s.ChartBarNumber;
                                    break;
                                }
                            }
                        }

                        if (!signalPocFromOverride)
                        {
                            var last = _ovSnapshotHistory?.GetLast(4096);
                            if (last != null)
                            {
                                for (int i = last.Count - 1; i >= 0; i--)
                                {
                                    var s = last[i];
                                    if (s != null && s.Bar == signalBarIdxOverride.Value)
                                    {
                                        signalPoc = s.CandlePocPrice;
                                        signalPocFromOverride = true;
                                        if (!signalChartBarOverride.HasValue && s.ChartBarNumber > 0)
                                            signalChartBarOverride = s.ChartBarNumber;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (!signalPocFromOverride)
                    signalPoc = ovLastClosed?.CandlePocPrice ?? 0m;
                if (signalPoc <= 0m)
                {
                    this.LogWarn($"[ORDER-LONG] Candle POC nicht verfügbar (ovLastClosed.CandlePocPrice={signalPoc:F2}). Fallback auf Close={c.Close:F2} (Bar={closed}).");
                    signalPoc = c.Close;
                }
                signalPoc = RoundToTick(signalPoc);

                // Sicherstellen, dass levelsSnapshot nicht null ist, bevor darauf zugegriffen wird
                if (levelsSnapshot == null)
                {
                    this.LogWarn("[ORDER-LONG] levelsSnapshot ist null. Kann keinen Einstiegspreis bestimmen.");
                    return;
                }

                switch (detectedPattern.Type)
                {
                    case OrderflowPatternType.PotentialLongReversalBounce:
                        longEntryPrice = RoundToTick(signalPoc + (2m * tickSize));
                        entryTriggerLevelName = "CANDLE_POC";
                        entryTriggerLevel = signalPoc;

                        decimal suggestedSlLong = c.Low - tickSize;
                        int slTicksLong = (int)Math.Ceiling((longEntryPrice - suggestedSlLong) / tickSize);
                        if (slTicksLong < 1) slTicksLong = 1;

                        setup = new SetupParams
                        {
                            TpTicks = 10,
                            SlTicks = slTicksLong,
                            BreakEvenLevelsTrendConfig = string.IsNullOrWhiteSpace(ReversalBreakEvenStagesConfig) ? "5:1;8:5" : ReversalBreakEvenStagesConfig,
                            SuggestedStopLossPrice = suggestedSlLong,
                            TrailType = "CANDLE_HL",
                            TrailActivateAfterTicks = 4,
                        };
                        break;

                    case OrderflowPatternType.PotentialLongTrendContinuation:
                        longEntryPrice = RoundToTick(signalPoc + (2m * tickSize));
                        entryTriggerLevelName = "CANDLE_POC";
                        entryTriggerLevel = signalPoc;

                        decimal suggestedSlLongCont = c.Low - tickSize;
                        int slTicksLongCont = (int)Math.Ceiling((longEntryPrice - suggestedSlLongCont) / tickSize);
                        if (slTicksLongCont < 1) slTicksLongCont = 1;

                        setup = new SetupParams
                        {
                            TpTicks = 10,
                            SlTicks = slTicksLongCont,
                            BreakEvenLevelsTrendConfig = string.IsNullOrWhiteSpace(ContinuationBreakEvenStagesConfig) ? null : ContinuationBreakEvenStagesConfig,
                            SuggestedStopLossPrice = suggestedSlLongCont,
                            TrailType = "CANDLE_HL",
                            TrailActivateAfterTicks = 4,
                        };
                        break;

                    default:
                        this.LogWarn($"[ORDER-LONG] Unbekannter OrderflowPatternType f?r Long-Setup: {detectedPattern.Type}. Kein Einstieg.");
                        return;
                }

                _activeTradeSetupParams = setup;
                _entryBarIndex = placeBar;
                _entryTimeoutStartBarIndex = _intrabarImmediatePlaceMode ? (placeBar - 1) : (CurrentBar - 1);

                bool entryWegFreiLong = true;
                string entryWegFreiLongBlocker = string.Empty;
                if (EnableMicroCompositeSystem)
                {
                    var mcForPath = _currentMC ?? GetRollingMicroComposite();
                    entryWegFreiLong = IsPathFreeLongEnhanced(longEntryPrice, DMinTicks, riskTicks, mcForPath, null, tickSize, GetPathConfig(), out entryWegFreiLongBlocker);
                }
                if (EnableDailyProfilePathSystem && _dailyProfileForPath != null)
                {
                    var dailyCfgForEntry = GetDailyPathConfig();
                    bool okDaily = IsPathFreeLongEnhanced(longEntryPrice, DailyDMinTicks, riskTicks, _dailyProfileForPath, _prevDailyProfileForPath, tickSize, dailyCfgForEntry, out var dailyBlocker);
                    if (!okDaily)
                    {
                        entryWegFreiLong = false;
                        var prefixed = dailyBlocker.StartsWith("MC.", StringComparison.OrdinalIgnoreCase)
                            ? ("D." + dailyBlocker.Substring(3))
                            : ("D." + dailyBlocker);
                        entryWegFreiLongBlocker = string.IsNullOrWhiteSpace(entryWegFreiLongBlocker)
                            ? prefixed
                            : (entryWegFreiLongBlocker + " | " + prefixed);
                    }
                }

                if (!entryWegFreiLong)
                {
                    this.LogInfo($"[SETUP-LONG-BLOCKED] Long Setup blockiert, da Weg nicht frei am EntryPrice={longEntryPrice:F2}. Blocker={entryWegFreiLongBlocker}");
                    return;
                }

                if (EnableVwapProximityBlocker && tickSize > 0m && VwapProximityTicks > 0 && currentVwap > 0m)
                {
                    decimal proximityDistance = VwapProximityTicks * tickSize;
                    decimal entryDist = Math.Abs(longEntryPrice - currentVwap);
                    bool crossedUp = (c.Open < currentVwap && c.Close > currentVwap) || (c.High > currentVwap && c.Low < currentVwap && c.Close >= currentVwap);

                    if ((longEntryPrice < currentVwap && entryDist <= proximityDistance) || crossedUp)
                    {
                        this.LogInfo($"[ORDER-LONG-BLOCKED] VWAP-Blocker: Long blockiert. Entry={longEntryPrice:F4}, VWAP={currentVwap:F4}, distTicks={(entryDist / tickSize):F1}, limit={VwapProximityTicks}, crossedUp={crossedUp} (Bar={closed}).");
                        return;
                    }
                }
                this.LogInfo($"[ORDER-LONG] Platzierung: Pattern={detectedPattern.Type}, Bias={currentMarketState.Bias}, " +
                             $"TriggerLevel={entryTriggerLevelName}={entryTriggerLevel:F2}, EntryPrice={longEntryPrice:F2}.");
                if (caller == "IntrabarPending")
                    _lastPendingSignalBarPlaced = pendingSignalBarUsedForDedupe ?? setupBar;
                if (EnablePullback && PullbackTicksInitial > 0)
                {
                    // Pullback-Arming-Level nahe am aktuellen Signalbar-Close, um Fill-Chance zu erhöhen.
                    // Long: X Ticks UNTER Close -> bei Touch switch zu trailing StopLimit (Reversal Entry)
                    decimal armPx = RoundToTick(c.Close - (PullbackTicksInitial * tickSize));
                    this.LogInfo($"[ORDER-LONG] Pullback aktiv: armPx={armPx:F2} (close={c.Close:F2}, initTicks={PullbackTicksInitial}).");
                    PlacePullbackEntry(OrderDirections.Buy, armPx, placeBar);
                    _entryState = EntryState.PullbackPlaced;
                }
                else
                {
                    PlaceEntry(OrderDirections.Buy, longEntryPrice, placeBar);
                }
                return; // Beende die Methode nach Orderplatzierung
            }
            else if (isShortSetupValid)
            {
                decimal shortEntryPrice = 0;
                var setup = new SetupParams(); // Standard-Setup-Parameter
                string entryTriggerLevelName = string.Empty;
                decimal entryTriggerLevel = 0;

                int? signalBarIdxOverride = null;
                int? signalChartBarOverride = null;
                try
                {
                    if (detectedPattern?.MatchedCriteriaValues != null && detectedPattern.MatchedCriteriaValues.Count > 0)
                    {
                        if (detectedPattern.MatchedCriteriaValues.TryGetValue("SignalBarIndex", out var sIdxObj) && sIdxObj != null)
                        {
                            if (sIdxObj is int si)
                                signalBarIdxOverride = si;
                            else if (sIdxObj is long sl)
                                signalBarIdxOverride = (int)sl;
                            else if (int.TryParse(sIdxObj.ToString(), out var sParsed))
                                signalBarIdxOverride = sParsed;
                        }
                        if (detectedPattern.MatchedCriteriaValues.TryGetValue("SignalChartBarNumber", out var sChartObj) && sChartObj != null)
                        {
                            if (sChartObj is int sci)
                                signalChartBarOverride = sci;
                            else if (sChartObj is long scl)
                                signalChartBarOverride = (int)scl;
                            else if (int.TryParse(sChartObj.ToString(), out var scParsed))
                                signalChartBarOverride = scParsed;
                        }
                    }
                }
                catch { }

                decimal signalPoc = 0m;
                bool signalPocFromOverride = false;
                if (signalBarIdxOverride.HasValue)
                {
                    try
                    {
                        if (ofFeaturesHistory != null)
                        {
                            for (int i = 0; i < ofFeaturesHistory.Count; i++)
                            {
                                var s = ofFeaturesHistory.GetOfFeatures(i)?.Snapshot;
                                if (s != null && s.Bar == signalBarIdxOverride.Value)
                                {
                                    signalPoc = s.CandlePocPrice;
                                    signalPocFromOverride = true;
                                    if (!signalChartBarOverride.HasValue && s.ChartBarNumber > 0)
                                        signalChartBarOverride = s.ChartBarNumber;
                                    break;
                                }
                            }
                        }

                        if (!signalPocFromOverride)
                        {
                            var last = _ovSnapshotHistory?.GetLast(4096);
                            if (last != null)
                            {
                                for (int i = last.Count - 1; i >= 0; i--)
                                {
                                    var s = last[i];
                                    if (s != null && s.Bar == signalBarIdxOverride.Value)
                                    {
                                        signalPoc = s.CandlePocPrice;
                                        signalPocFromOverride = true;
                                        if (!signalChartBarOverride.HasValue && s.ChartBarNumber > 0)
                                            signalChartBarOverride = s.ChartBarNumber;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (!signalPocFromOverride)
                    signalPoc = ovLastClosed?.CandlePocPrice ?? 0m;
                if (signalPoc <= 0m)
                {
                    this.LogWarn($"[ORDER-SHORT] Candle POC nicht verfügbar (ovLastClosed.CandlePocPrice={signalPoc:F2}). Fallback auf Close={c.Close:F2} (Bar={closed}).");
                    signalPoc = c.Close;
                }
                signalPoc = RoundToTick(signalPoc);

                // Sicherstellen, dass levelsSnapshot nicht null ist, bevor darauf zugegriffen wird
                if (levelsSnapshot == null)
                {
                    this.LogWarn("[ORDER-SHORT] levelsSnapshot ist null. Kann keinen Einstiegspreis bestimmen.");
                    return;
                }

                switch (detectedPattern.Type)
                {
                    case OrderflowPatternType.PotentialShortReversalBounce:
                        shortEntryPrice = RoundToTick(signalPoc - (2m * tickSize));
                        entryTriggerLevelName = "CANDLE_POC";
                        entryTriggerLevel = signalPoc;

                        decimal suggestedSlShort = c.High + tickSize;
                        int slTicksShort = (int)Math.Ceiling((suggestedSlShort - shortEntryPrice) / tickSize);
                        if (slTicksShort < 1) slTicksShort = 1;

                        setup = new SetupParams
                        {
                            TpTicks = 10,
                            SlTicks = slTicksShort,
                            BreakEvenLevelsTrendConfig = string.IsNullOrWhiteSpace(ReversalBreakEvenStagesConfig) ? "5:1;8:5" : ReversalBreakEvenStagesConfig,
                            SuggestedStopLossPrice = suggestedSlShort,
                            TrailType = "CANDLE_HL",
                            TrailActivateAfterTicks = 4,
                        };
                        break;

                    case OrderflowPatternType.PotentialShortTrendContinuation:
                        shortEntryPrice = RoundToTick(signalPoc - (2m * tickSize));
                        entryTriggerLevelName = "CANDLE_POC";
                        entryTriggerLevel = signalPoc;

                        decimal suggestedSlShortCont = c.High + tickSize;
                        int slTicksShortCont = (int)Math.Ceiling((suggestedSlShortCont - shortEntryPrice) / tickSize);
                        if (slTicksShortCont < 1) slTicksShortCont = 1;

                        setup = new SetupParams
                        {
                            TpTicks = 10,
                            SlTicks = slTicksShortCont,
                            BreakEvenLevelsTrendConfig = string.IsNullOrWhiteSpace(ContinuationBreakEvenStagesConfig) ? null : ContinuationBreakEvenStagesConfig,
                            SuggestedStopLossPrice = suggestedSlShortCont,
                            TrailType = "CANDLE_HL",
                            TrailActivateAfterTicks = 4,
                        };
                        break;

                    default:
                        this.LogWarn($"[ORDER-SHORT] Unbekannter OrderflowPatternType f?r Short-Setup: {detectedPattern.Type}. Kein Einstieg.");
                        return;
                }

                _activeTradeSetupParams = setup;
                _entryBarIndex = placeBar;
                _entryTimeoutStartBarIndex = _intrabarImmediatePlaceMode ? (placeBar - 1) : (CurrentBar - 1);

                bool entryWegFreiShort = true;
                string entryWegFreiShortBlocker = string.Empty;
                if (EnableMicroCompositeSystem)
                {
                    var mcForPath = _currentMC ?? GetRollingMicroComposite();
                    entryWegFreiShort = IsPathFreeShortEnhanced(shortEntryPrice, DMinTicks, riskTicks, mcForPath, null, tickSize, GetPathConfig(), out entryWegFreiShortBlocker);
                }
                if (EnableDailyProfilePathSystem && _dailyProfileForPath != null)
                {
                    var dailyCfgForEntry = GetDailyPathConfig();
                    bool okDaily = IsPathFreeShortEnhanced(shortEntryPrice, DailyDMinTicks, riskTicks, _dailyProfileForPath, _prevDailyProfileForPath, tickSize, dailyCfgForEntry, out var dailyBlocker);
                    if (!okDaily)
                    {
                        entryWegFreiShort = false;
                        var prefixed = dailyBlocker.StartsWith("MC.", StringComparison.OrdinalIgnoreCase)
                            ? ("D." + dailyBlocker.Substring(3))
                            : ("D." + dailyBlocker);
                        entryWegFreiShortBlocker = string.IsNullOrWhiteSpace(entryWegFreiShortBlocker)
                            ? prefixed
                            : (entryWegFreiShortBlocker + " | " + prefixed);
                    }
                }

                if (!entryWegFreiShort)
                {
                    this.LogInfo($"[SETUP-SHORT-BLOCKED] Short Setup blockiert, da Weg nicht frei am EntryPrice={shortEntryPrice:F2}. Blocker={entryWegFreiShortBlocker}");
                    return;
                }

                if (EnableVwapProximityBlocker && tickSize > 0m && VwapProximityTicks > 0 && currentVwap > 0m)
                {
                    decimal proximityDistance = VwapProximityTicks * tickSize;
                    decimal entryDist = Math.Abs(shortEntryPrice - currentVwap);
                    bool crossedDown = (c.Open > currentVwap && c.Close < currentVwap) || (c.High > currentVwap && c.Low < currentVwap && c.Close <= currentVwap);

                    if ((shortEntryPrice > currentVwap && entryDist <= proximityDistance) || crossedDown)
                    {
                        this.LogInfo($"[ORDER-SHORT-BLOCKED] VWAP-Blocker: Short blockiert. Entry={shortEntryPrice:F4}, VWAP={currentVwap:F4}, distTicks={(entryDist / tickSize):F1}, limit={VwapProximityTicks}, crossedDown={crossedDown} (Bar={closed}).");
                        return;
                    }
                }

                this.LogInfo($"[DBG-EVAL] About to PlaceEntry: caller={caller}, intrabarMode={_intrabarImmediatePlaceMode}, intrabarPlaceBar={_intrabarImmediatePlaceBar}, CurrentBar={CurrentBar}, lastOnCalcBar={_lastOnCalculateBar}, dir={(isLongSetupValid ? "Buy" : "Sell")}, price={shortEntryPrice}, closed={closed}, closedChart={closed + 1}, placeBar={placeBar}, placeChart={placeChart}, signalBar={(signalBarIdxOverride?.ToString() ?? "-")}, signalChart={(signalChartBarOverride?.ToString() ?? "-")}, pocSrc={(signalPocFromOverride ? "SignalBarOverride" : "ClosedBar")}");
                this.LogInfo($"[ORDER-SHORT] Platzierung: Pattern={detectedPattern.Type}, Bias={currentMarketState.Bias}, " +
                             $"TriggerLevel={entryTriggerLevelName}={entryTriggerLevel:F2}, EntryPrice={shortEntryPrice:F2}.");
                if (caller == "IntrabarPending")
                    _lastPendingSignalBarPlaced = pendingSignalBarUsedForDedupe ?? setupBar;
                if (EnablePullback && PullbackTicksInitial > 0)
                {
                    // Short: X Ticks ÜBER Close -> bei Touch switch zu trailing StopLimit (Reversal Entry)
                    decimal armPx = RoundToTick(c.Close + (PullbackTicksInitial * tickSize));
                    this.LogInfo($"[ORDER-SHORT] Pullback aktiv: armPx={armPx:F2} (close={c.Close:F2}, initTicks={PullbackTicksInitial}).");
                    PlacePullbackEntry(OrderDirections.Sell, armPx, placeBar);
                    _entryState = EntryState.PullbackPlaced;
                }
                else
                {
                    PlaceEntry(OrderDirections.Sell, shortEntryPrice, placeBar);
                }
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
                // Widerstand ist über dem aktuellen Preis (oder Touch/kleiner Overshoot), und der Abstand ist innerhalb des Schwellenwerts
                // Zusätzlich: 2-Tick Overshoot-Toleranz, damit ein Close genau auf dem Level oder leicht darüber trotzdem blockt.
                var touchBuffer = 2m * _tickSize;
                var dist = level.Value - currentPrice;
                if (dist >= -touchBuffer && dist <= threshold)
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
                // Unterstützung ist unter dem aktuellen Preis (oder Touch/kleiner Overshoot), und der Abstand ist innerhalb des Schwellenwerts
                // Zusätzlich: 2-Tick Overshoot-Toleranz, damit ein Close genau auf dem Level oder leicht darunter trotzdem blockt.
                var touchBuffer = 2m * _tickSize;
                var dist = currentPrice - level.Value;
                if (dist >= -touchBuffer && dist <= threshold)
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
            decimal tickSize,
            bool insideValue
        )
        {
            if (cfg.ForceAllHVNsStrong) return true;
            if (cfg.ForceAllHVNsWeak) return false;

            // Ohne LevelVols: konservativ -> behandle HVN als stark (POCVol vorhanden)
            if (mc.LevelVols == null || mc.LevelVols.Count == 0)
                return true;

            // HVN-Preis auf Tick-Grid / nächstes vorhandenes Level bringen
            decimal key = hvnPrice;
            if (!mc.LevelVols.ContainsKey(key))
            {
                key = Math.Round(hvnPrice / tickSize, 0, MidpointRounding.AwayFromZero) * tickSize;
                if (!mc.LevelVols.ContainsKey(key))
                {
                    decimal best = 0m;
                    int bestDist = int.MaxValue;
                    foreach (var p in mc.LevelVols.Keys)
                    {
                        int d = (int)Math.Round((double)(Math.Abs(p - hvnPrice) / tickSize));
                        if (d < bestDist)
                        {
                            bestDist = d;
                            best = p;
                            if (bestDist == 0) break;
                        }
                    }
                    if (bestDist <= 2) key = best; // nur in unmittelbarer Nähe snapen
                }
            }

            if (!mc.LevelVols.TryGetValue(key, out var hvnVol))
                return true;

            var medianVol = TryComputeMedianVol(mc.LevelVols);
            decimal? prominence = null;

            // Nachbarpreise (?1 Tick)
            decimal left = key - tickSize;
            decimal right = key + tickSize;
            var vL = mc.LevelVols.TryGetValue(left, out var vl) ? vl : hvnVol;
            var vR = mc.LevelVols.TryGetValue(right, out var vr) ? vr : hvnVol;
            prominence = hvnVol - Math.Max(vL, vR);

            decimal pocFactor = insideValue ? cfg.HVNStrengthVsPOCInside : cfg.HVNStrengthVsPOCOutside;
            decimal medFactor = insideValue ? cfg.HVNStrengthVsMedianInside : cfg.HVNStrengthVsMedianOutside;
            decimal promFactor = insideValue ? cfg.MinProminenceVsMedianInside : cfg.MinProminenceVsMedianOutside;

            bool strongVsPOC = mc.POCVol > 0m && hvnVol >= pocFactor * mc.POCVol;
            bool strongVsMedian = medianVol.HasValue && hvnVol >= medFactor * medianVol.Value;
            bool prominent = (medianVol.HasValue && prominence.HasValue) ? prominence.Value >= promFactor * medianVol.Value : true;

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
            decimal touchBuffer = 2m * tickSize;
            decimal lowerBoundTouch = currentPrice - touchBuffer;
            bool insideValue = IsInsideValue(mcCurr, currentPrice);

            // STRICT: Wenn innerhalb DMinTicks ein MC-Level/HVN (inkl. HVN-Zonen-Kanten) liegt, MUSS Entry geblockt werden.
            // Damit k?nnen keine Trades mit TP "1 Tick vor HVN" entstehen.
            if (mcCurr.POC >= lowerBoundTouch && mcCurr.POC <= upperThreshold)
            {
                blocker = $"MC.POC@{mcCurr.POC:F2}";
                return false;
            }

            if (mcCurr.VAH >= lowerBoundTouch && mcCurr.VAH <= upperThreshold)
            {
                blocker = $"MC.VAH@{mcCurr.VAH:F2}";
                return false;
            }

            if (cfg.UseHvnZonesForBlocking && mcCurr.HVNZones != null && mcCurr.HVNZones.Count > 0)
            {
                foreach (var z in mcCurr.HVNZones)
                {
                    bool intersects = !(z.End < lowerBoundTouch || z.Start > upperThreshold);
                    if (!intersects) continue;

                    decimal center = (z.Start + z.End) / 2m;
                    bool insideValueForStrength = IsInsideValue(mcCurr, center);
                    bool strong = IsStrongHVN(center, mcCurr, cfg, tickSize, insideValueForStrength);
                    if (!strong)
                    {
                        if (this.DailyHVNStrength <= 0 && object.ReferenceEquals(mcCurr, _dailyProfileForPath))
                            //this.LogInfo($"[DailyWegFreiDebug] Filtered HVNZone as WEAK despite DailyHVNStrength={DailyHVNStrength} ForceStrong={cfg.ForceAllHVNsStrong} center={center:F2} zone=[{z.Start:F2}-{z.End:F2}] insideValue={insideValueForStrength} upperTh={upperThreshold:F2}");
                        continue;
                    }

                    bool startIn = (z.Start >= lowerBoundTouch && z.Start <= upperThreshold);
                    bool endIn = (z.End >= lowerBoundTouch && z.End <= upperThreshold);
                    decimal edge = currentPrice <= z.Start ? z.Start : z.End;

                    blocker = $"MC.HVNZone@{edge:F2}";
                    return false;
                }
            }

            if (cfg.UseHvnZonesForBlocking && mcCurr.HVNs != null)
            {
                foreach (var hvn in mcCurr.HVNs)
                {
                    if (hvn < lowerBoundTouch || hvn > upperThreshold) continue;

                    bool insideValueForStrength = IsInsideValue(mcCurr, hvn);
                    bool strong = IsStrongHVN(hvn, mcCurr, cfg, tickSize, insideValueForStrength);
                    if (!strong)
                    {
                        if (this.DailyHVNStrength <= 0 && object.ReferenceEquals(mcCurr, _dailyProfileForPath))
                            //this.LogInfo($"[DailyWegFreiDebug] Filtered HVN as WEAK despite DailyHVNStrength={DailyHVNStrength} ForceStrong={cfg.ForceAllHVNsStrong} hvn={hvn:F2} insideValue={insideValueForStrength} upperTh={upperThreshold:F2}");
                        continue;
                    }
                    blocker = $"MC.HVN@{hvn:F2}";
                    return false;
                }
            }

            // LVN-Count: kombiniere Punkte + Zonen (als Tupel konvertiert)
            int lvnCountPts = 0;
            int lvnCountZones = 0;
            int lvnCount = 0;
            if (cfg.UseLvnPathForBlocking && cfg.RequiredLVNsInPath > 0)
            {
                lvnCountPts = CountLVNsInPathUp(mcCurr.LVNs, currentPrice, upperThreshold);
                lvnCountZones = mcCurr.LVNZones != null
                    ? CountLVNZoneEdgesInPathUp(mcCurr.LVNZones, currentPrice, upperThreshold)
                    : 0;
                lvnCount = Math.Max(lvnCountPts, lvnCountZones);
            }

            int migDir = ValueMigrationDirection(mcPrev, mcCurr, tickSize);

            //this.LogInfo($"PathLong: price={currentPrice:F2} upperTh={upperThreshold:F2} dMinTicks={dMinTicks} riskTicks={riskTicks} insideValue={insideValue} lvnCount={lvnCount} migDir={migDir} lvnPts={lvnCountPts} lvnZones={lvnCountZones}");

            if (cfg.UseLvnPathForBlocking && cfg.RequiredLVNsInPath > 0 && lvnCount < cfg.RequiredLVNsInPath)
            {
                //this.LogInfo($"PathLong: lvnCount {lvnCount} < erforderlich {cfg.RequiredLVNsInPath} ? false");
                blocker = $"LVN_PATH<{cfg.RequiredLVNsInPath}";
                return false;
            }

            // POC blockt
            if (mcCurr.POC >= lowerBoundTouch && mcCurr.POC <= upperThreshold)
            {
                //this.LogInfo($"PathLong: POC {mcCurr.POC:F2} blocks in range ? false");
                blocker = $"MC.POC@{mcCurr.POC:F2}";
                return false;
            }

            // HVN-Blocker: zuerst Zonen (als Tupel), dann Punkt-HVNs
            decimal? closestStrongBlocker = null;

            if (cfg.UseHvnZonesForBlocking && mcCurr.HVNZones != null && mcCurr.HVNZones.Count > 0)
            {
                foreach (var z in mcCurr.HVNZones)
                {
                    bool intersects = !(z.End < lowerBoundTouch || z.Start > upperThreshold);
                    if (!intersects) continue;

                    decimal center = (z.Start + z.End) / 2m;
                    bool insideValueForStrength = IsInsideValue(mcCurr, center);
                    if (!IsStrongHVN(center, mcCurr, cfg, tickSize, insideValueForStrength)) continue;

                    bool startIn = (z.Start >= lowerBoundTouch && z.Start <= upperThreshold);
                    bool endIn = (z.End >= lowerBoundTouch && z.End <= upperThreshold);
                    decimal edge = startIn ? z.Start
                                  : endIn ? z.End
                                  : (TicksBetweenAbs(z.Start, currentPrice) <= TicksBetweenAbs(z.End, currentPrice) ? z.Start : z.End);

                    int dist = TicksBetweenAbs(edge, currentPrice);
                    //this.LogInfo($"PathLong: candidate HVN-Zone [{z.Start:F2}-{z.End:F2}] edge={edge:F2} distTicks={dist}");

                    if (closestStrongBlocker == null || dist < TicksBetweenAbs(closestStrongBlocker.Value, currentPrice))
                        closestStrongBlocker = edge;
                }
            }

            if (cfg.UseHvnZonesForBlocking && closestStrongBlocker == null && mcCurr.HVNs != null)
            {
                foreach (var hvn in mcCurr.HVNs)
                {
                    if (hvn < lowerBoundTouch || hvn > upperThreshold) continue;

                    bool insideValueForStrength = IsInsideValue(mcCurr, hvn);
                    if (!IsStrongHVN(hvn, mcCurr, cfg, tickSize, insideValueForStrength)) continue;

                    int dist = TicksBetweenAbs(hvn, currentPrice);
                    //this.LogInfo($"PathLong: candidate HVN {hvn:F2} distTicks={dist}");
                    if (closestStrongBlocker == null || dist < TicksBetweenAbs(closestStrongBlocker.Value, currentPrice))
                        closestStrongBlocker = hvn;
                }
            }

            // VAH-Block
            bool vahBlocks = (mcCurr.VAH >= lowerBoundTouch && mcCurr.VAH <= upperThreshold);
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

            if (cfg.UseHvnZonesForBlocking && closestStrongBlocker.HasValue)
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
            decimal touchBuffer = 2m * tickSize;
            decimal upperBoundTouch = currentPrice + touchBuffer;
            bool insideValue = IsInsideValue(mcCurr, currentPrice);

            // STRICT: Wenn innerhalb DMinTicks ein MC-Level/HVN (inkl. HVN-Zonen-Kanten) liegt, MUSS Entry geblockt werden.
            if (mcCurr.POC <= upperBoundTouch && mcCurr.POC >= lowerThreshold)
            {
                blocker = $"MC.POC@{mcCurr.POC:F2}";
                return false;
            }

            if (mcCurr.VAL <= upperBoundTouch && mcCurr.VAL >= lowerThreshold)
            {
                blocker = $"MC.VAL@{mcCurr.VAL:F2}";
                return false;
            }

            if (cfg.UseHvnZonesForBlocking && mcCurr.HVNZones != null && mcCurr.HVNZones.Count > 0)
            {
                foreach (var z in mcCurr.HVNZones)
                {
                    bool intersects = !(z.Start > upperBoundTouch || z.End < lowerThreshold);
                    if (!intersects) continue;

                    decimal center = (z.Start + z.End) / 2m;
                    bool insideValueForStrength = IsInsideValue(mcCurr, center);
                    if (!IsStrongHVN(center, mcCurr, cfg, tickSize, insideValueForStrength)) continue;

                    bool startIn = (z.Start <= upperBoundTouch && z.Start >= lowerThreshold);
                    bool endIn = (z.End <= upperBoundTouch && z.End >= lowerThreshold);
                    decimal edge = currentPrice >= z.End ? z.End : z.Start;

                    blocker = $"MC.HVNZone@{edge:F2}";
                    return false;
                }
            }

            if (cfg.UseHvnZonesForBlocking && mcCurr.HVNs != null)
            {
                foreach (var hvn in mcCurr.HVNs)
                {
                    if (hvn > upperBoundTouch || hvn < lowerThreshold) continue;

                    bool insideValueForStrength = IsInsideValue(mcCurr, hvn);
                    if (!IsStrongHVN(hvn, mcCurr, cfg, tickSize, insideValueForStrength)) continue;
                    blocker = $"MC.HVN@{hvn:F2}";
                    return false;
                }
            }

            int lvnCountPts = 0;
            int lvnCountZones = 0;
            int lvnCount = 0;
            if (cfg.UseLvnPathForBlocking && cfg.RequiredLVNsInPath > 0)
            {
                lvnCountPts = CountLVNsInPathDown(mcCurr.LVNs, currentPrice, lowerThreshold);
                lvnCountZones = mcCurr.LVNZones != null
                    ? CountLVNZoneEdgesInPathDown(mcCurr.LVNZones, currentPrice, lowerThreshold)
                    : 0;
                lvnCount = Math.Max(lvnCountPts, lvnCountZones);
            }


            int migDir = ValueMigrationDirection(mcPrev, mcCurr, tickSize);

            //this.LogInfo($"PathShort: price={currentPrice:F2} lowerTh={lowerThreshold:F2} dMinTicks={dMinTicks} riskTicks={riskTicks} insideValue={insideValue} lvnCount={lvnCount} migDir={migDir} lvnPts={lvnCountPts} lvnZones={lvnCountZones}");

            if (cfg.UseLvnPathForBlocking && cfg.RequiredLVNsInPath > 0 && lvnCount < cfg.RequiredLVNsInPath)
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
                    bool intersects = !(z.Start > upperBoundTouch || z.End < lowerThreshold);
                    if (!intersects) continue;

                    decimal center = (z.Start + z.End) / 2m;
                    bool insideValueForStrength = IsInsideValue(mcCurr, center);
                    if (!IsStrongHVN(center, mcCurr, cfg, tickSize, insideValueForStrength)) continue;

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
                    if (hvn > upperBoundTouch || hvn < lowerThreshold) continue;
                    bool insideValueForStrength = IsInsideValue(mcCurr, hvn);
                    if (!IsStrongHVN(hvn, mcCurr, cfg, tickSize, insideValueForStrength)) continue;

                    int dist = TicksBetweenAbs(hvn, currentPrice);
                    //this.LogInfo($"PathShort: candidate HVN {hvn:F2} distTicks={dist}");
                    if (closestStrongBlocker == null ||
                        TicksBetweenAbs(hvn, currentPrice) < TicksBetweenAbs(closestStrongBlocker.Value, currentPrice))
                    {
                        closestStrongBlocker = hvn;
                    }
                }
            }

            bool valBlocks = (mcCurr.VAL <= upperBoundTouch && mcCurr.VAL >= lowerThreshold);
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

            if (cfg.UseHvnZonesForBlocking && closestStrongBlocker.HasValue)
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
                // WICHTIG: Cancel/Failed einer Exit-Order kann auch durch Modify/ReRegister entstehen (z.B. BreakEven/Trailing).
                // In diesem Fall ist die Position noch offen und darf NICHT als Trade-Ende interpretiert werden.
                if (CurrentPosition != 0)
                {
                    this.LogInfo($"[OnOrderChanged] EXIT ORDER Id={order.Id} (Typ: {order.Type}) CANCELED/FAILED while position still open (Pos={CurrentPosition}). Likely Modify/ReRegister - skip trade reset.");
                    return;
                }

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
                    _currentBar = TryGetCandleAtOrBefore(CurrentBar) ?? GetCandle(CurrentBar);
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    this.LogWarn($"[PlaceTpSlOrders-Initial] GetCandle(CurrentBar={CurrentBar}) fehlgeschlagen: {ex.Message} - Fallback zu currentCandle.");
                    _currentBar = currentCandle;
                }
            }


            _entryFillPrice = entryPrice;
            _currentTradeDirection = tradeDirection;

            this.LogInfo($"[PlaceTpSlOrders{methodSuffix}] Entry final: {_entryFillPrice:F5}, Dir: {_currentTradeDirection}");

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

                _fillBarIndex = -1;
                _lastPendingSignalBarPlaced = -1;

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
            if (EnableSignificantPreviousLevels && !EnableLevelSystem)
            {
                EnableLevelSystem = true;
                this.LogInfo("[LEVEL-AUTO] Level-System automatisch aktiviert, da Visualisierung gew?nscht.");
            }

            if (!EnableSignificantPreviousLevels && !ShowMicroCompositeLevels && !ShowDailyProfileLevels && !ShowDailyHistogram && !ShowMarketStructureZones && !ShowSyntheticTick900CandlesDebug && !ShowMarketStateV2Overlay)
                return;

            if (ShowMarketStateV2Overlay)
            {
                try
                {
                    var text = _marketStateV2OverlayText;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var font = _axisFont;
                        var size = context.MeasureString(text, font);
                        int y = 10;
                        int rectW = size.Width + 10;
                        int rectH = size.Height + 6;
                        int chartW = 0;
                        try { chartW = ChartInfo.PriceChartContainer.Region.Width; } catch { chartW = 0; }
                        int x = (chartW > 0) ? Math.Max(0, (chartW - rectW) / 2) : 10;
                        if (chartW > 0)
                            x = Math.Min(x, Math.Max(0, chartW - rectW));
                        var rect = new System.Drawing.Rectangle(x, y, rectW, rectH);
                        context.FillRectangle(System.Drawing.Color.FromArgb(160, System.Drawing.Color.Black), rect);
                        context.DrawString(text, font, System.Drawing.Color.White, rect.X + 5, rect.Y + 3);
                    }
                }
                catch
                {
                }
            }

            int x1 = ChartInfo.PriceChartContainer.Region.Width - Length;
            int x2 = ChartInfo.PriceChartContainer.Region.Width;

            // 1) Vortages/Vorwochen-Levels
            if (EnableSignificantPreviousLevels && EnableLevelSystem)
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
                    if (mc.HVNZones != null && mc.HVNZones.Count > 0)
                    {
                        var pathCfg = GetPathConfig();
                        int hvnZoneWidth = (x2 - x1) / 2;
                        int hvnX1 = x2 - hvnZoneWidth;

                        foreach (var z in mc.HVNZones)
                        {
                            decimal pStart = z.Item1;
                            decimal pEnd = z.Item2;

                            decimal centerPrice = (pStart + pEnd) / 2m;
                            bool isStrong = IsStrongHVN(centerPrice, mc, pathCfg, _tickSize, IsInsideValue(mc, centerPrice));

                            var penEdge = isStrong
                                ? new RenderPen(System.Drawing.Color.DarkRed, 2)
                                : penHVNEdge;
                            var fillColor = isStrong
                                ? System.Drawing.Color.FromArgb(95, System.Drawing.Color.Red)
                                : System.Drawing.Color.FromArgb(50, System.Drawing.Color.IndianRed);

                            int yStart = (int)ChartInfo.GetYByPrice(pStart, false);
                            int yEnd = (int)ChartInfo.GetYByPrice(pEnd, false);
                            int top = Math.Min(yStart, yEnd);
                            int bottom = Math.Max(yStart, yEnd);
                            if (bottom == top) bottom = top + 1;

                            var rect = new System.Drawing.Rectangle(hvnX1, top, x2 - hvnX1, bottom - top);
                            context.FillRectangle(fillColor, rect);

                            context.DrawLine(penEdge, hvnX1, yStart, x2, yStart);
                            context.DrawLine(penEdge, hvnX1, yEnd, x2, yEnd);

                            DrawLabelOnPriceAxis(context, "HVN", top, _axisFont, isStrong ? System.Drawing.Color.DarkRed : System.Drawing.Color.IndianRed, _axisTextColor);

                            // Optional: genau eine Centerline je finaler Zone
                            int yCenter = (int)ChartInfo.GetYByPrice(centerPrice, false);
                            context.DrawLine(isStrong ? new RenderPen(System.Drawing.Color.DarkRed, 2) : penHVNCenter, hvnX1, yCenter, x2, yCenter);
                        }
                    }

                    // LVN-Zonen (Gr?n)
                    if (mc.LVNZones != null && mc.LVNZones.Count > 0)
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
                            decimal centerPrice = (pStart + pEnd) / 2m;
                            int yCenter = (int)ChartInfo.GetYByPrice(centerPrice, false);
                            context.DrawLine(penLVNCenter, lvnX1, yCenter, x2, yCenter);
                        }
                    }

                    // WICHTIG: Kandidaten/Peaks nicht zeichnen
                    // Entfernt: Schleifen ?ber mc.HVNs / mc.LVNs (das waren Centerlines vieler Kandidaten)
                }
            }

            // 3) Daily Profile (nur Zonen, getrennt von MicroComposite)
            if (ShowDailyProfileLevels)
            {
                var d = _dailyProfileForVisual ?? _dailyProfileForPath;
                if (d != null)
                {
                    var penHVNEdge = new RenderPen(System.Drawing.Color.FromArgb(180, System.Drawing.Color.RoyalBlue), 1);
                    penHVNEdge.DashPattern = new float[] { 4f, 3f };
                    var penLVNEdge = new RenderPen(System.Drawing.Color.FromArgb(180, System.Drawing.Color.MediumPurple), 1);
                    penLVNEdge.DashPattern = new float[] { 4f, 3f };

                    int hvnX1 = x1;
                    int lvnX1 = x1;

                    // HVN-Zonen (Daily)
                    if (d.HVNZones != null && d.HVNZones.Count > 0)
                    {
                        var pathCfg = GetDailyPathConfig();
                        foreach (var z in d.HVNZones)
                        {
                            decimal pStart = z.Item1;
                            decimal pEnd = z.Item2;

                            decimal centerPrice = (pStart + pEnd) / 2m;
                            bool isStrong = IsStrongHVN(centerPrice, d, pathCfg, _tickSize, IsInsideValue(d, centerPrice));

                            var fillColor = isStrong
                                ? System.Drawing.Color.FromArgb(85, System.Drawing.Color.RoyalBlue)
                                : System.Drawing.Color.FromArgb(45, System.Drawing.Color.RoyalBlue);

                            int yStart = (int)ChartInfo.GetYByPrice(pStart, false);
                            int yEnd = (int)ChartInfo.GetYByPrice(pEnd, false);
                            int top = Math.Min(yStart, yEnd);
                            int bottom = Math.Max(yStart, yEnd);
                            if (bottom == top) bottom = top + 1;

                            var rect = new System.Drawing.Rectangle(hvnX1, top, x2 - hvnX1, bottom - top);
                            context.FillRectangle(fillColor, rect);
                            context.DrawLine(isStrong ? new RenderPen(System.Drawing.Color.RoyalBlue, 2) : penHVNEdge, hvnX1, yStart, x2, yStart);
                            context.DrawLine(isStrong ? new RenderPen(System.Drawing.Color.RoyalBlue, 2) : penHVNEdge, hvnX1, yEnd, x2, yEnd);

                            DrawLabelOnPriceAxis(context, "HVN-D", top, _axisFont, System.Drawing.Color.RoyalBlue, _axisTextColor);
                        }
                    }

                    // LVN-Zonen (Daily)
                    if (d.LVNZones != null && d.LVNZones.Count > 0)
                    {
                        foreach (var z in d.LVNZones)
                        {
                            decimal pStart = z.Item1;
                            decimal pEnd = z.Item2;

                            int yStart = (int)ChartInfo.GetYByPrice(pStart, false);
                            int yEnd = (int)ChartInfo.GetYByPrice(pEnd, false);
                            int top = Math.Min(yStart, yEnd);
                            int bottom = Math.Max(yStart, yEnd);
                            if (bottom == top) bottom = top + 1;

                            var rect = new System.Drawing.Rectangle(lvnX1, top, x2 - lvnX1, bottom - top);
                            context.FillRectangle(System.Drawing.Color.FromArgb(35, System.Drawing.Color.MediumPurple), rect);
                            context.DrawLine(penLVNEdge, lvnX1, yStart, x2, yStart);
                            context.DrawLine(penLVNEdge, lvnX1, yEnd, x2, yEnd);

                            DrawLabelOnPriceAxis(context, "LVN-D", top, _axisFont, System.Drawing.Color.MediumPurple, _axisTextColor);
                        }
                    }
                }
            }

            // 4) Daily Histogramm (Market-Profile Style)
            if (ShowDailyHistogram)
            {
                var d = _dailyProfileForPath;
                var hist = d?.LevelVols;
                if (hist != null && hist.Count > 0 && _tickSize > 0m)
                {
                    decimal maxVol = 0m;
                    foreach (var kv in hist)
                        if (kv.Value > maxVol) maxVol = kv.Value;

                    if (maxVol > 0m)
                    {
                        int right = ChartInfo.PriceChartContainer.Region.Width;
                        int barWidthPx = Math.Max(20, DailyHistogramWidthPx);
                        int alpha = Math.Max(5, Math.Min(255, DailyHistogramOpacity));
                        var fillColor = System.Drawing.Color.FromArgb(alpha, System.Drawing.Color.SteelBlue);

                        foreach (var kv in hist)
                        {
                            var price = kv.Key;
                            var vol = kv.Value;
                            if (vol <= 0m) continue;

                            int y = (int)ChartInfo.GetYByPrice(price, false);
                            int y2 = (int)ChartInfo.GetYByPrice(price + _tickSize, false);
                            int h = Math.Max(1, Math.Abs(y2 - y));

                            int w = (int)Math.Round((double)(vol / maxVol) * barWidthPx);
                            if (w <= 0) continue;
                        }
                    }
                }
            }

            // 4b) MarketStructure (Tick900) Zones
            if (ShowMarketStructureZones)
            {
                MarketStructureContext.Zone[] zones = null;
                try
                {
                    zones = _marketStructureContext?.ActiveZones?.ToArray();
                }
                catch { }

                if (!_msZonesSnapshotDiagLogged)
                {
                    _msZonesSnapshotDiagLogged = true;
                    int localCount = 0;
                    try { localCount = zones?.Length ?? 0; } catch { }
                    int snapCount = 0;
                    try { snapCount = ReadZonesSnapshot()?.Count ?? 0; } catch { }
                    this.LogInfo($"[MarketStructure:{_msInstanceId}] Render diag: isLeader={IsMarketStructureLeader()} localZones={localCount} snapZones={snapCount} mmf='{_msZonesMmfName ?? "-"}'");
                }

                if (!IsMarketStructureLeader())
                {
                    var snap = ReadZonesSnapshot();
                    if (snap.Count > 0)
                    {
                        zones = snap
                            .Select(s => new MarketStructureContext.Zone
                            {
                                Id = s.Id,
                                Type = s.Type,
                                Status = s.Status,
                                Low = s.Low,
                                High = s.High,
                                IsMultiTouch = s.IsMultiTouch,
                                MultiTouchScore = s.MultiTouchScore,
                                IsConfirmed = s.IsConfirmed,
                                CreatedBar = 0,
                                LastTouchedBar = 0
                            })
                            .ToArray();
                    }
                }

                if ((zones == null || zones.Length == 0) && !_msZonesDiagLogged)
                {
                    _msZonesDiagLogged = true;
                    int ctxCount = 0;
                    try { ctxCount = _marketStructureContext?.ActiveZones?.Count ?? 0; } catch { }
                    this.LogInfo($"[MarketStructure:{_msInstanceId}] ShowMarketStructureZones enabled but no zones to draw. active={ctxCount} tickBars={_msTick900Bar} backfillCompleted={_msTick900BackfillCompleted}");
                }

                if (zones != null && zones.Length > 0)
                {
                    int xLeft = 0;
                    int xRight = ChartInfo.PriceChartContainer.Region.Width;
                    var penSupport = new RenderPen(System.Drawing.Color.FromArgb(200, System.Drawing.Color.ForestGreen), 1);
                    var penResistance = new RenderPen(System.Drawing.Color.FromArgb(200, System.Drawing.Color.IndianRed), 1);
                    var fillSupport = System.Drawing.Color.FromArgb(28, System.Drawing.Color.ForestGreen);
                    var fillResistance = System.Drawing.Color.FromArgb(28, System.Drawing.Color.IndianRed);
                    var fillTriggered = System.Drawing.Color.FromArgb(45, System.Drawing.Color.Goldenrod);

                    foreach (var z in zones)
                    {
                        if (z == null)
                            continue;
                        // Nur frische/aktive Zonen zeichnen. Getriggerte/benutzte Zonen sollen verschwinden.
                        if (z.Status != MarketStructureContext.ZoneStatus.New && z.Status != MarketStructureContext.ZoneStatus.Ready)
                            continue;

                        bool doLog = false;
                        if (z.Id == 11 && !_msZone11RenderDiagLogged) { _msZone11RenderDiagLogged = true; doLog = true; }
                        else if (z.Id == 12 && !_msZone12RenderDiagLogged) { _msZone12RenderDiagLogged = true; doLog = true; }
                        else if (z.Id == 13 && !_msZone13RenderDiagLogged) { _msZone13RenderDiagLogged = true; doLog = true; }
                        if (doLog)
                            this.LogInfo($"[MarketStructure:{_msInstanceId}] Render ZoneDiag id={z.Id} source={(IsMarketStructureLeader() ? "local" : "snapshot")} type={z.Type} status={z.Status} confirmed={z.IsConfirmed} bounds=[{z.Low:F2}..{z.High:F2}]");

                        int yLow = (int)ChartInfo.GetYByPrice(z.Low, false);
                        int yHigh = (int)ChartInfo.GetYByPrice(z.High, false);
                        int top = Math.Min(yLow, yHigh);
                        int bottom = Math.Max(yLow, yHigh);
                        if (bottom == top)
                            bottom = top + 1;

                        var rect = new System.Drawing.Rectangle(xLeft, top, xRight - xLeft, bottom - top);
                        var baseFill = (z.Type == MarketStructureContext.ZoneType.Support ? fillSupport : fillResistance);
                        var basePen = z.Type == MarketStructureContext.ZoneType.Support ? penSupport : penResistance;

                        int strength = 0;
                        try { strength = z.IsMultiTouch ? Math.Max(1, z.MultiTouchScore) : 0; } catch { strength = 0; }

                        // Normal zones: lighter (more transparent). MultiTouch zones: darker (less transparent)
                        // strength 1 => strong, strength>=2 => very strong
                        int fillAlpha = strength <= 0 ? 28 : (strength == 1 ? 55 : 80);
                        int penAlpha = strength <= 0 ? 200 : (strength == 1 ? 230 : 255);

                        // Pending zones: intentionally drawn much lighter and in gray so you can
                        // see they are tradable for Immediate (A) but not yet zigzag-confirmed.
                        bool pending = false;
                        try { pending = !z.IsConfirmed; } catch { pending = false; }
                        if (pending)
                        {
                            fillAlpha = Math.Max(6, fillAlpha / 4);
                            penAlpha = Math.Max(80, penAlpha / 2);
                        }

                        var fillColor = pending
                            ? System.Drawing.Color.FromArgb(fillAlpha, System.Drawing.Color.Gray)
                            : System.Drawing.Color.FromArgb(fillAlpha, baseFill);
                        var penColor = pending
                            ? System.Drawing.Color.FromArgb(penAlpha, System.Drawing.Color.LightGray)
                            : System.Drawing.Color.FromArgb(penAlpha, basePen.Color);
                        var pen = new RenderPen(penColor, basePen.Width);

                        context.FillRectangle(fillColor, rect);
                        context.DrawLine(pen, xLeft, yLow, xRight, yLow);
                        context.DrawLine(pen, xLeft, yHigh, xRight, yHigh);

                        if (ShowMarketStructureZoneLabels)
                        {
                            int yMid = (int)ChartInfo.GetYByPrice(z.Mid, false);
                            string label = z.Type == MarketStructureContext.ZoneType.Support
                                ? $"SUP Z{z.Id}"
                                : $"RES Z{z.Id}";

                            if (z.IsMultiTouch)
                                label += $" MT{Math.Max(1, z.MultiTouchScore)}";

                            if (pending)
                                label += " P";

                            DrawLabelOnPriceAxis(
                                context,
                                label,
                                yMid,
                                _axisFont,
                                z.Type == MarketStructureContext.ZoneType.Support ? penColor : penColor,
                                _axisTextColor);
                        }
                    }
                }
            }

            // 5) Synthetic Tick900 debug candles (OHLC overlay)
            if (ShowSyntheticTick900CandlesDebug)
            {
                try
                {
                    // Draw only a limited recent window for performance.
                    int max = 80;
                    var formingCandle = _msTick900Aggregator != null ? _msTick900Aggregator.GetCurrentFormingCandle() : null;
                    int closedMax = Math.Max(0, max - (formingCandle != null ? 1 : 0));
                    int startIdx = Math.Max(0, _msTick900ClosedCandles.Count - closedMax);
                    var col = System.Drawing.Color.FromArgb(170, System.Drawing.Color.Magenta);
                    var penWick = new RenderPen(col, 1);
                    var fillUp = System.Drawing.Color.FromArgb(70, System.Drawing.Color.Magenta);
                    var fillDown = System.Drawing.Color.FromArgb(35, System.Drawing.Color.Magenta);

                    // Thin rendering: only one synthetic candle per chart bar.
                    // Iterate newest->oldest so we keep the *latest* synthetic candle for each chart bar.
                    var seenBars = new HashSet<int>();

                    if (formingCandle != null)
                    {
                        DateTime ptA0 = NormalizeToChartTime(formingCandle.Time);
                        DateTime ptB0 = NormalizeToChartTime(formingCandle.LastTime != default ? formingCandle.LastTime : formingCandle.Time);
                        int barA0 = FindChartBarByTime(ptA0);
                        int barB0 = FindChartBarByTime(ptB0);

                        int candA0 = barA0 >= 0 ? FindBestChartBarMatchForSyntheticTickCandle(barA0, formingCandle) : -1;
                        int candB0 = barB0 >= 0 ? FindBestChartBarMatchForSyntheticTickCandle(barB0, formingCandle) : -1;

                        int bar0 = candA0 >= 0 ? candA0 : candB0;
                        if (candA0 >= 0 && candB0 >= 0)
                        {
                            var ca = GetCandle(candA0);
                            var cb = GetCandle(candB0);
                            decimal ts0 = _tickSize > 0m ? _tickSize : 0.25m;
                            var sa = ScoreChartCandleVsSyntheticOHLC(ca, formingCandle, ts0);
                            var sb = ScoreChartCandleVsSyntheticOHLC(cb, formingCandle, ts0);
                            bar0 = sa <= sb ? candA0 : candB0;
                        }

                        bar0 = FindBestChartBarMatchForSyntheticTickCandleWide(bar0, formingCandle, 60);

                        if (bar0 >= 0 && seenBars.Add(bar0))
                        {
                            int x0 = GetXByBarSafe(bar0);
                            int yHigh0 = (int)ChartInfo.GetYByPrice(formingCandle.High, false);
                            int yLow0 = (int)ChartInfo.GetYByPrice(formingCandle.Low, false);
                            int yOpen0 = (int)ChartInfo.GetYByPrice(formingCandle.Open, false);
                            int yClose0 = (int)ChartInfo.GetYByPrice(formingCandle.Close, false);

                            context.DrawLine(penWick, x0, yHigh0, x0, yLow0);

                            int top0 = Math.Min(yOpen0, yClose0);
                            int bottom0 = Math.Max(yOpen0, yClose0);
                            if (bottom0 == top0) bottom0 = top0 + 1;
                            int bodyW0 = 4;
                            var rect0 = new System.Drawing.Rectangle(x0 - bodyW0 / 2, top0, bodyW0, bottom0 - top0);
                            context.FillRectangle(formingCandle.Close >= formingCandle.Open ? fillUp : fillDown, rect0);
                            context.DrawRectangle(penWick, rect0);
                        }
                    }

                    for (int i = _msTick900ClosedCandles.Count - 1; i >= startIdx; i--)
                    {
                        var c = _msTick900ClosedCandles[i];
                        if (c == null)
                            continue;

                        // Map by candle start time to align with ATAS Tick(900) bar timestamping.
                        DateTime ptA = NormalizeToChartTime(c.Time);
                        DateTime ptB = NormalizeToChartTime(c.LastTime != default ? c.LastTime : c.Time);
                        int barA = FindChartBarByTime(ptA);
                        int barB = FindChartBarByTime(ptB);

                        int candA = barA >= 0 ? FindBestChartBarMatchForSyntheticTickCandle(barA, c) : -1;
                        int candB = barB >= 0 ? FindBestChartBarMatchForSyntheticTickCandle(barB, c) : -1;

                        int bar = candA >= 0 ? candA : candB;
                        if (candA >= 0 && candB >= 0)
                        {
                            var ca = GetCandle(candA);
                            var cb = GetCandle(candB);
                            decimal ts = _tickSize > 0m ? _tickSize : 0.25m;
                            var sa = ScoreChartCandleVsSyntheticOHLC(ca, c, ts);
                            var sb = ScoreChartCandleVsSyntheticOHLC(cb, c, ts);
                            bar = sa <= sb ? candA : candB;
                        }

                        bar = FindBestChartBarMatchForSyntheticTickCandleWide(bar, c, 60);
                        if (bar < 0)
                            continue;

                        if (seenBars.Contains(bar))
                            continue;
                        seenBars.Add(bar);

                        int x = GetXByBarSafe(bar);
                        int yHigh = (int)ChartInfo.GetYByPrice(c.High, false);
                        int yLow = (int)ChartInfo.GetYByPrice(c.Low, false);
                        int yOpen = (int)ChartInfo.GetYByPrice(c.Open, false);
                        int yClose = (int)ChartInfo.GetYByPrice(c.Close, false);

                        // Wick
                        context.DrawLine(penWick, x, yHigh, x, yLow);

                        // Body as small rectangle (much easier to read than a vertical line)
                        int top = Math.Min(yOpen, yClose);
                        int bottom = Math.Max(yOpen, yClose);
                        if (bottom == top) bottom = top + 1;
                        int bodyW = 4;
                        var rect = new System.Drawing.Rectangle(x - bodyW / 2, top, bodyW, bottom - top);
                        context.FillRectangle(c.Close >= c.Open ? fillUp : fillDown, rect);
                        context.DrawRectangle(penWick, rect);
                    }
                }
                catch { }
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
                // Limit-Orders: Buy sollte NICHT nach oben gerundet werden (schlechterer Fill), Sell nicht nach unten.
                alignedPrice = (direction == OrderDirections.Buy)
                    ? Math.Floor(steps) * _tickSize      // Buy Limit: nach unten
                    : Math.Ceiling(steps) * _tickSize;   // Sell Limit: nach oben
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

            bool quoteSanityFailed = false;

            if (EnableEntryQuoteSanityGuard)
            {
                decimal tick = InstrumentInfo?.TickSize ?? _tickSize;
                if (tick <= 0m) tick = 0.25m;

                decimal lastTrade = Security?.LastTradePrice ?? 0m;
                decimal bestAsk = Security?.BestAskPrice ?? 0m;
                decimal bestBid = Security?.BestBidPrice ?? 0m;

                if (lastTrade > 0m && (bestAsk > 0m || bestBid > 0m) && EntryQuoteMaxDeviationTicks > 0)
                {
                    decimal maxDevPx = EntryQuoteMaxDeviationTicks * tick;
                    bool askBad = bestAsk > 0m && Math.Abs(bestAsk - lastTrade) > maxDevPx;
                    bool bidBad = bestBid > 0m && Math.Abs(bestBid - lastTrade) > maxDevPx;

                    if (askBad || bidBad)
                    {
                        quoteSanityFailed = true;
                        this.LogWarn($"[PlaceEntry] QuoteSanity FAIL: Dir={direction}, IntendedPx={price:F5}, Last={lastTrade:F5}, BestAsk={bestAsk:F5}, BestBid={bestBid:F5}, maxDevTicks={EntryQuoteMaxDeviationTicks}. Fallback -> Stop entry.");
                    }
                }
            }

            Order entry;
            if (quoteSanityFailed)
            {
                entry = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Direction = direction,
                    Type = OrderTypes.Stop,
                    TriggerPrice = price,
                    Price = price,
                    QuantityToFill = HandelsMenge
                };
            }
            else
            {
                entry = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Direction = direction,
                    Type = OrderTypes.Limit,
                    Price = price,
                    QuantityToFill = HandelsMenge
                };
            }
            _entryOrder = entry;
            _entryBarIndex = bar;
            this.LogInfo($"[PlaceEntry SET] entryBarIndex={_entryBarIndex} timeoutBars={_orderTimeoutBars} " +
                 $"timeoutBarIndex={_entryBarIndex + _orderTimeoutBars}");
            try
            {
                var cb = CurrentBar;
                int lastCalcBar = _lastOnCalculateBar;
                int calcClosed = lastCalcBar - 1;

                DateTime? tClosed = null;
                DateTime? tForming = null;
                try
                {
                    if (TryGetCandleSafe(cb - 1, out var icClosed) && icClosed != null)
                        tClosed = icClosed.Time;
                }
                catch { }
                try
                {
                    if (TryGetCandleSafe(cb, out var icForming) && icForming != null)
                        tForming = icForming.Time;
                }
                catch { }

                this.LogInfo($"[PlaceEntry-TIMING] CurrentBar={cb} CurrentClosed={(cb - 1)} lastOnCalcBar={lastCalcBar} lastOnCalcClosed={calcClosed} argBar={bar} nowUtc={DateTime.UtcNow:O} candleTime(CurrentClosed)={(tClosed.HasValue ? tClosed.Value.ToString("O") : "n/a")} candleTime(CurrentForming)={(tForming.HasValue ? tForming.Value.ToString("O") : "n/a")}");
            }
            catch { }

            // Sende-Log inkl. tats?chlich verwendeter Menge
            if (entry.Type == OrderTypes.Stop)
                this.LogInfo($"[PlaceEntry] Sende Stop: Dir={direction}, Trg={entry.TriggerPrice:F5}, Px={entry.Price:F5}, Qty={HandelsMenge}.");
            else
                this.LogInfo($"[PlaceEntry] Sende Limit: Dir={direction}, Px={entry.Price:F5}, Qty={HandelsMenge}.");
            OpenOrder(entry);
            try
            {
                this.LogInfo($"[PlaceEntry-AFTER-OPEN] EntryOrder Id={entry.Id} Status={entry.Status()} State={entry.State} Dir={entry.Direction} Type={entry.Type} Px={entry.Price:F5} Trg={entry.TriggerPrice:F5}");
            }
            catch { }
            //this.LogInfo($"[DBG-ORDER] PlaceOrder returned:  time={DateTime.UtcNow:O}");

        } // Ende PlaceEntry Methode

        private void ArmIntrabarEntry(int bar, bool isLong, int bandIdx, decimal bandLevel, decimal signalBarHigh, decimal signalBarLow, decimal targetLevel, string marketSpeed, decimal volZ, decimal? longTriggerOverride = null, decimal? shortTriggerOverride = null, int? reclaimTicksOverride = null, int? nearTicksOverride = null)
        {
            if (!EnableIntrabarEntry)
                return;

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
                if (o == null)
                    continue;

                try
                {
                    if (o.State == OrderStates.Active)
                        CancelOrder(o);
                }
                catch (Exception ex)
                {
                    this.LogWarn($"[CancelAllOpenOrdersIfAny] CancelOrder failed: {ex.Message}");
                }
            }
        }

        private void CleanupInactiveOrders()
        {
            if (_entryOrder != null && _entryOrder.State == OrderStates.Failed)
            {
                this.LogInfo($"[Cleanup] Entry Order: {_entryOrder.Direction} {_entryOrder.State}");
                _entryOrder = null;
                _activeTradeSetupParams = null;
                _entryBarIndex = -1;
                _armedBarIndex = -1;
            }

            // Pullback Order: ?hnlich
            if (_pullbackOrder != null && _pullbackOrder.State == OrderStates.Failed)
            {
                this.LogInfo($"[Cleanup] Pullback Order: {_pullbackOrder.Direction} {_pullbackOrder.State}");
                _pullbackOrder = null;
            }

        }
        // Wird aufgerufen, wenn die Strategie manuell oder durch die Plattform gestoppt wird (Beibehalten)
        protected override void OnStopping()
        {

            base.OnStopping();


            this.LogInfo($"[OnStopping] Strategie wird gestoppt. Bar={CurrentBar - 1}.");

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

            _marketStateEngineV2 = null;
            _currentMarketStateV2 = null;


        } // Ende OnStopping Methode

    }

}























































































