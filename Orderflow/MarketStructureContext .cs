using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using static MyNamespace.Strategies.Goldfluss3_3;

namespace MyNamespace.Strategies.Orderflow
{
    /// <summary>
    /// Platzhalter für den Marktstrukturkontext.
    /// Diese Klasse wird später mit detaillierten Informationen über
    /// signifikante Preislevel, Volumenprofile (HVNs, LVNs), VWAP, VAH/VAL/POC etc. gefüllt.
    /// Für die aktuelle Phase der Orderflow-Mustererkennung wird sie vorerst als leerer Kontext übergeben.
    /// </summary>
    public class MarketStructureContext
    {
        // Hier werden später die Eigenschaften für Support/Resistance-Level,
        // Volumenprofile (HVN, LVN), VWAP, VAH/VAL/POC, etc. hinzugefügt.
        // Beispiel:
        // public List<PriceLevel> SupportLevels { get; } = new List<PriceLevel>();
        // public List<PriceLevel> ResistanceLevels { get; } = new List<PriceLevel>();
        // public VolumeProfile CurrentVolumeProfile { get; set; }
        // public decimal CurrentVWAP { get; set; }
        // public decimal CurrentVAH { get; set; }
        // public decimal CurrentVAL { get; set; }
        // public decimal CurrentPOC { get; set; }
    }

    // Beispiel für eine zukünftige Hilfsklasse (muss nicht jetzt definiert werden)
    /*
    public class PriceLevel
    {
        public decimal Price { get; set; }
        public LevelType Type { get; set; } // Enum { Support, Resistance, HVN, LVN, POC }
        public int Touches { get; set; }
        public DateTime LastTouched { get; set; }
    }
    */
}


