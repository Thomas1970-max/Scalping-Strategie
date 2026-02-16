namespace MyNamespace.Strategies.Models
{
    public sealed class MarketRegimeDetails
    {
        public MarketRegime Regime { get; set; } = MarketRegime.Normal;
        public int FastVotes { get; set; }
        public int SlowVotes { get; set; }
        public bool IsHighVol { get; set; }
        public decimal Vps { get; set; }
        public decimal VpsEma { get; set; }
        public decimal VpsStd { get; set; }
        public decimal ZScore { get; set; }
        public decimal TradesPerSecZ { get; set; }
        public decimal SecondsPerBar { get; set; }
    }
}
