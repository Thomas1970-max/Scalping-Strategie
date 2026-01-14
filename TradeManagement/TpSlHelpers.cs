using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using ATAS.DataFeedsCore;
using static MyNamespace.Strategies.Goldfluss3_3;

namespace MyNamespace.Strategies.TradeManagement
{
    public static class TpSlHelpers
    {

        // Ticks umwandeln -> Preis (positive Richtung relativ zum Handel)
        public static decimal PriceFromTicks(TpSlContext ctx, decimal entryPrice, decimal ticks, OrderDirections direction)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            // defensiv: sicherstellen, dass der Tick positiv ist; falls nicht, Rückfall auf 1 (sollte bei echten Instrumenten nicht passieren)
            var tick = ctx.Tick;
            if (tick <= 0) tick = 1m;

            var sign = (direction == OrderDirections.Buy) ? 1m : -1m;
            return entryPrice + ticks * tick * sign;
        }

        // Ticks in absolute Entfernung (Preiseinheiten) umrechnen, unabhängig von der Richtung
        public static decimal TicksToPriceDistance(TpSlContext ctx, decimal ticks)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            var tick = ctx.Tick;
            if (tick <= 0) tick = 1m;
            return ticks * tick;
        }

        public static bool TryGetLevelPrice(TpSlContext ctx, string levelKey, out decimal price)
        {
            price = 0m;
            if (ctx?.Levels == null || string.IsNullOrWhiteSpace(levelKey))
                return false;

            var k = levelKey.Trim();
            var ku = k.ToUpperInvariant();

            // 1) typisierte Eigenschaften (preferiert, typsicher)
            switch (ku)
            {
                // Direkte deutsche "gestern"-Labels (explizit ergänzt)
                case "TAGESHOCH GESTERN":
                    if (ctx.Levels.PreviousDayHigh > 0m) { price = ctx.Levels.PreviousDayHigh; return true; }
                    return false;
                case "TAGESTIEF GESTERN":
                    if (ctx.Levels.PreviousDayLow > 0m) { price = ctx.Levels.PreviousDayLow; return true; }
                    return false;
                case "SCHLUSSKURS GESTERN":
                    if (ctx.Levels.PreviousDayClose > 0m) { price = ctx.Levels.PreviousDayClose; return true; }
                    return false;
                case "ERÖFFNUNGSKURS GESTERN":
                case "EROEFFNUNGSKURS GESTERN":
                    if (ctx.Levels.PreviousDayOpen > 0m) { price = ctx.Levels.PreviousDayOpen; return true; }
                    return false;

                case "POC":
                case "CURRENT_POC":
                case "CURRENTPOC":
                case "POC AKTUELL":
                    if (ctx.Levels.CurrentPOC > 0m) { price = ctx.Levels.CurrentPOC; return true; }
                    if (ctx.Levels.PreviousDayPOC > 0m) { price = ctx.Levels.PreviousDayPOC; return true; } // Fallback
                    return false;

                case "VAH":
                case "CURRENT_VAH":
                case "CURRENTVAH":
                case "VAH AKTUELL":
                    if (ctx.Levels.CurrentVAH > 0m) { price = ctx.Levels.CurrentVAH; return true; }
                    if (ctx.Levels.PreviousDayVAH > 0m) { price = ctx.Levels.PreviousDayVAH; return true; } // Fallback
                    return false;

                case "VAL":
                case "CURRENT_VAL":
                case "CURRENTVAL":
                case "VAL AKTUELL":
                    if (ctx.Levels.CurrentVAL > 0m) { price = ctx.Levels.CurrentVAL; return true; }
                    if (ctx.Levels.PreviousDayVAL > 0m) { price = ctx.Levels.PreviousDayVAL; return true; } // Fallback
                    return false;

                case "PP":
                    if (ctx.Levels.PP > 0m) { price = ctx.Levels.PP; return true; }
                    return false;

                case "R1":
                    if (ctx.Levels.R1 > 0m) { price = ctx.Levels.R1; return true; }
                    return false;
                case "R2":
                    if (ctx.Levels.R2 > 0m) { price = ctx.Levels.R2; return true; }
                    return false;
                case "R3":
                    if (ctx.Levels.R3 > 0m) { price = ctx.Levels.R3; return true; }
                    return false;

                case "S1":
                    if (ctx.Levels.S1 > 0m) { price = ctx.Levels.S1; return true; }
                    return false;
                case "S2":
                    if (ctx.Levels.S2 > 0m) { price = ctx.Levels.S2; return true; }
                    return false;
                case "S3":
                    if (ctx.Levels.S3 > 0m) { price = ctx.Levels.S3; return true; }
                    return false;

                // M-levels
                case "M1":
                    if (ctx.Levels.M1 > 0m) { price = ctx.Levels.M1; return true; }
                    return false;
                case "M2":
                    if (ctx.Levels.M2 > 0m) { price = ctx.Levels.M2; return true; }
                    return false;
                case "M3":
                    if (ctx.Levels.M3 > 0m) { price = ctx.Levels.M3; return true; }
                    return false;
                case "M4":
                    if (ctx.Levels.M4 > 0m) { price = ctx.Levels.M4; return true; }
                    return false;

                // VWAP-Section: Zentraler VWAP
                case "VWAP":
                case "CURRENT_VWAP":
                case "VWAP AKTUELL":
                case "VOLUME_WEIGHTED_AVG_PRICE":
                    if (ctx.Vwap?.Current > 0m) { price = ctx.Vwap.Current; return true; }
                    if (ctx.VwapPrevious?.Current > 0m) { price = ctx.VwapPrevious.Current; return true; }  // Fallback previous bar
                    if (ctx.Vwap?.PreviousDayCurrent > 0m) { price = ctx.Vwap.PreviousDayCurrent; return true; }  // Fallback previous day
                    return false;

                // VWAP Band 1 Upper
                case "VWAPBAND1_UPPER":
                case "VWAP_BAND_1_UPPER":
                case "VWAP SD1 UPPER":
                case "VWAP_BAND1_OBERE":
                case "UPPERBAND1":
                case "VWAP UPPER BAND 1":
                    if (ctx.Vwap?.UpperBand1 > 0m) { price = ctx.Vwap.UpperBand1; return true; }
                    if (ctx.VwapPrevious?.UpperBand1 > 0m) { price = ctx.VwapPrevious.UpperBand1; return true; }
                    if (ctx.Vwap?.PreviousDayUpperBand1 > 0m) { price = ctx.Vwap.PreviousDayUpperBand1; return true; }
                    return false;

                // VWAP Band 1 Lower
                case "VWAPBAND1_LOWER":
                case "VWAP_BAND_1_LOWER":
                case "VWAP SD1 LOWER":
                case "VWAP_BAND1_UNTERE":
                case "LOWERBAND1":
                case "VWAP LOWER BAND 1":
                    if (ctx.Vwap?.LowerBand1 > 0m) { price = ctx.Vwap.LowerBand1; return true; }
                    if (ctx.VwapPrevious?.LowerBand1 > 0m) { price = ctx.VwapPrevious.LowerBand1; return true; }
                    if (ctx.Vwap?.PreviousDayLowerBand1 > 0m) { price = ctx.Vwap.PreviousDayLowerBand1; return true; }
                    return false;

                // VWAP Band 2 Upper
                case "VWAPBAND2_UPPER":
                case "VWAP_BAND_2_UPPER":
                case "VWAP SD2 UPPER":
                case "VWAP_BAND2_OBERE":
                case "UPPERBAND2":
                    if (ctx.Vwap?.UpperBand2 > 0m) { price = ctx.Vwap.UpperBand2; return true; }
                    if (ctx.VwapPrevious?.UpperBand2 > 0m) { price = ctx.VwapPrevious.UpperBand2; return true; }
                    if (ctx.Vwap?.PreviousDayUpperBand2 > 0m) { price = ctx.Vwap.PreviousDayUpperBand2; return true; }
                    return false;

                // VWAP Band 2 Lower
                case "VWAPBAND2_LOWER":
                case "VWAP_BAND_2_LOWER":
                case "VWAP SD2 LOWER":
                case "VWAP_BAND2_UNTERE":
                case "LOWERBAND2":
                    if (ctx.Vwap?.LowerBand2 > 0m) { price = ctx.Vwap.LowerBand2; return true; }
                    if (ctx.VwapPrevious?.LowerBand2 > 0m) { price = ctx.VwapPrevious.LowerBand2; return true; }
                    if (ctx.Vwap?.PreviousDayLowerBand2 > 0m) { price = ctx.Vwap.PreviousDayLowerBand2; return true; }
                    return false;

                // VWAP Band 3 Upper
                case "VWAPBAND3_UPPER":
                case "VWAP_BAND_3_UPPER":
                case "VWAP SD3 UPPER":
                case "VWAP_BAND3_OBERE":
                case "UPPERBAND3":
                    if (ctx.Vwap?.UpperBand3 > 0m) { price = ctx.Vwap.UpperBand3; return true; }
                    if (ctx.VwapPrevious?.UpperBand3 > 0m) { price = ctx.VwapPrevious.UpperBand3; return true; }
                    if (ctx.Vwap?.PreviousDayUpperBand3 > 0m) { price = ctx.Vwap.PreviousDayUpperBand3; return true; }
                    return false;

                // VWAP Band 3 Lower
                case "VWAPBAND3_LOWER":
                case "VWAP_BAND_3_LOWER":
                case "VWAP SD3 LOWER":
                case "VWAP_BAND3_UNTERE":
                case "LOWERBAND3":
                    if (ctx.Vwap?.LowerBand3 > 0m) { price = ctx.Vwap.LowerBand3; return true; }
                    if (ctx.VwapPrevious?.LowerBand3 > 0m) { price = ctx.VwapPrevious.LowerBand3; return true; }
                    if (ctx.Vwap?.PreviousDayLowerBand3 > 0m) { price = ctx.Vwap.PreviousDayLowerBand3; return true; }
                    return false;

                

                case "RUNDE MARKE":
                case "RUNDE MARKE (BELOW)":
                    if (ctx.Levels.RoundLevelBelow.HasValue) { price = ctx.Levels.RoundLevelBelow.Value; return true; }
                    return false;
                case "RUNDE MARKE (ABOVE)":
                case "ROUNDLEVELABOVE":
                case "ROUND_LEVEL_ABOVE":
                    if (ctx.Levels.RoundLevelAbove.HasValue) { price = ctx.Levels.RoundLevelAbove.Value; return true; }
                    return false;
            }

            // 2) Levels.Extras + candidate variants (deine bestehende Kandidaten-Logik, case-insensitive Extra)
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                k,
                ku,
                ku.Replace(" ", "_"),
                ku.Replace(" ", ""),
                k + "_PRICE",
                ku + "_PRICE",
                k + "_LEVEL",
                ku + "_LEVEL",
                k + "_P",
                ku + " PRICE",
                ku + " _PRICE"
            };

            string RemoveUmlauts(string s) => s?.Replace("Ä", "AE").Replace("ä", "ae").Replace("Ö", "OE").Replace("ö", "oe").Replace("Ü", "UE").Replace("ü", "ue") .Replace("ß", "ss");
            var noU = RemoveUmlauts(k);
            if (!string.IsNullOrEmpty(noU))
            {
                candidates.Add(noU);
                candidates.Add(noU.ToUpperInvariant());
                candidates.Add(noU.Replace(" ", "_"));
                candidates.Add(noU + "_PRICE");
            }

            foreach (var cand in candidates)
            {
                try
                {
                    if (ctx.Levels.TryGetExtra(cand, out var p))
                    {
                        price = p;
                        return true;
                    }
                }
                catch
                {
                    // ignore und weiter versuchen
                }
            }

            // 3) Meta string fallback: falls jemand Zahlen als Meta-Strings gespeichert hat
            try
            {
                if (ctx.Levels.TryGetMeta(k, out var s) && !string.IsNullOrWhiteSpace(s)
                    && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                {
                    price = parsed;
                    return true;
                }

                if (ctx.Levels.TryGetMeta(ku, out s) && !string.IsNullOrWhiteSpace(s)
                    && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                {
                    price = parsed;
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

                
        // small helper for recording debug source
        public static void AddDebugSource(ManagementPlan plan, string msg)
        {
            if (plan == null || string.IsNullOrEmpty(msg)) return;
            if (plan.DebugSourceMessages == null)
                plan.DebugSourceMessages = new List<string>();
            plan.DebugSourceMessages.Add(msg);
        }
    }
}


