using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using static MyNamespace.Strategies.Goldfluss3_3;

namespace MyNamespace.Strategies.Orderflow
{
    /// <summary>
    /// Repräsentiert eine spezifische Bedingung, die von einem Muster-Evaluator geprüft wurde und als erfüllt galt.
    /// Wird verwendet, um detaillierte Informationen über die erfüllten Bedingungen zu speichern.
    /// </summary>
    public class EvaluatedConditionDetail
    {
        /// <summary>
        /// Der Bezeichner der Bedingung (z.B. "CvdImpulseThreshold", "EfficiencySlopePositive").
        /// </summary>
        public string ConditionName { get; }

        /// <summary>
        /// Der Wert, der die Bedingung erfüllt hat (z.B. der aktuelle CvdImpulse-Wert "1234.56").
        /// </summary>
        public string MetValue { get; }

        /// <summary>
        /// Die konkrete Regel oder der Schwellenwert, die/der angewendet wurde (z.B. "> 1000", "< 0.5").
        /// </summary>
        public string RuleApplied { get; }

        public EvaluatedConditionDetail(string conditionName, string metValue, string ruleApplied)
        {
            ConditionName = conditionName ?? throw new ArgumentNullException(nameof(conditionName));
            MetValue = metValue ?? "N/A"; // Kann leer sein, wenn nur das Vorhandensein relevant ist
            RuleApplied = ruleApplied ?? "N/A"; // Kann leer sein, wenn keine spezifische Regel angewendet wurde
        }

        public override string ToString()
        {
            return $"{ConditionName}: Met '{MetValue}' (Rule: '{RuleApplied}')";
        }
    }
}


