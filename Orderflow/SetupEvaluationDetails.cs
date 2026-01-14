using System.Collections.Generic;
using System.Linq;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using static MyNamespace.Strategies.Goldfluss3_3;

// Diese Klasse hält die zusammengestellten Hard- und Relevant-Conditions.

namespace MyNamespace.Strategies.Orderflow
{
    // Speichert Bewertungsergebnisse für harte und relevante Bedingungen
    public class SetupEvaluationDetails
    {
        public ConditionTemplate HardConditionsTemplate { get; } = new ConditionTemplate();
        public ConditionTemplate RelevantConditionsTemplate { get; } = new ConditionTemplate();
        public int MinSignalsRequired { get; set; } = 1;
    }
}



