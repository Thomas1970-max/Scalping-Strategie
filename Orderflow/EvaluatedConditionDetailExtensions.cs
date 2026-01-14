using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using static MyNamespace.Strategies.Goldfluss3_3;

namespace MyNamespace.Strategies.Orderflow
{
    public static class EvaluatedConditionDetailExtensions
    {
        /// <summary>
        /// Liefert eine kompakte, log-freundliche Darstellung einer EvaluatedConditionDetail-Instanz.
        /// Beispiel: "CvdImpulse=1234 (Rule:>1000)"
        /// </summary>
        /// <param name="d">Die zu formatierende Bedingung</param>
        /// <param name="maxValueLength">Maximale Anzahl Zeichen für MetValue (Trunking)</param>
        /// <returns>Kurze String-Repräsentation, nullsicher</returns>
        public static string ToShortString(this EvaluatedConditionDetail d, int maxValueLength = 40)
        {
            if (d == null) return "(null)";

            try
            {
                var name = string.IsNullOrWhiteSpace(d.ConditionName) ? "(unnamed)" : d.ConditionName.Trim();

                var met = string.IsNullOrEmpty(d.MetValue) ? "n/a" : d.MetValue.Trim();
                if (met.Length > maxValueLength)
                    met = met.Substring(0, maxValueLength - 3) + "...";

                var rule = string.IsNullOrEmpty(d.RuleApplied) ? "n/a" : d.RuleApplied.Trim();

                // Kompakte Ausgabe: Name=Met (Rule:Rule)
                return $"{name}={met} (Rule:{rule})";
            }
            catch
            {
                // Fallback: benutze ToString(), falls etwas Unerwartetes passiert
                try { return d.ToString(); } catch { return "(unformattable-condition)"; }
            }
        }
    }
}


