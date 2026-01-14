using System;
using System.Collections.Generic;
using ATAS.DataFeedsCore;
using MyNamespace.Strategies.Models;
using Utils.Common.Logging;
using static MyNamespace.Strategies.Goldfluss3_3;


namespace MyNamespace.Strategies.Orderflow
{
    public class RangeStructureDetector
    {
        private readonly int _window;
        private readonly Queue<(int dir, bool isTrend, int ticks)> _buf;


        public RangeStructureDetector(int window)
        {
            _window = window;
            _buf = new Queue<(int, bool, int)>();
        }

        // Neuer Eintrag pro abgeschlossener Rangebar: dir = +1 / -1, isTrend = true/false, ticks = gerundete Tick-Größe
        public void AddBar(int dir, bool isTrend, int ticks)
        {
            _buf.Enqueue((dir, isTrend, ticks));
            if (_buf.Count > _window) _buf.Dequeue();
        }

        // Falls irgendwo noch der alte Aufruf mit 2 Parametern existiert, kannst du zusätzlich eine Overload bereitstellen:
        public void AddBar(int dir, bool isTrend)
        {
            // Fallback: falls keine ticks übergeben werden, nehme 0 (oder  (isTrend ? defaultTrendTicks : defaultRevTicks) )
            _buf.Enqueue((dir, isTrend, 0));
            if (_buf.Count > _window) _buf.Dequeue();
        }


        // Ergebnisse über Fenster
        public RangeStructureFeatures ComputeFeatures()
        {
            var items = _buf.ToArray();
            int N = items.Length;
            if (N == 0) return new RangeStructureFeatures();

            // flipRate
            int flips = 0;
            for (int i = 1; i < N; i++) if (items[i].dir != items[i - 1].dir) flips++;
            decimal flipRate = N > 1 ? (decimal)flips / (decimal)(N - 1) : 0m;

            // revShare (Reversal bars proportion): wenn isTrend==false zählt als reversal
            int revCount = items.Count(x => !x.isTrend);
            decimal revShare = (decimal)revCount / Math.Max(1, N);

            // Aktuelle Lauflänge der Trendbalken in derselben Richtung am Pufferende
            int runT = 0;
            if (N > 0)
            {
                int lastDir = items[N - 1].dir;
                for (int i = N - 1; i >= 0; i--)
                {
                    if (items[i].dir == lastDir && items[i].isTrend) runT++;
                    else break;
                }
            }

            // Netto/Grob-Ticks mit tatsächlichen, gerundeten ticks
            int netTicks = items.Sum(x => x.dir * x.ticks);
            int grossTicks = items.Sum(x => Math.Abs(x.ticks));
            decimal eff = grossTicks > 0 ? Math.Abs((decimal)netTicks) / (decimal)grossTicks : 0m;

            return new RangeStructureFeatures
            {
                FlipRate = flipRate,
                RevShare = revShare,
                RunTrend = runT,
                Efficiency = eff,
                Window = N
            };
        }
    }

    public class RangeStructureFeatures
    {
        public decimal FlipRate { get; set; } = 0m;
        public decimal RevShare { get; set; } = 0m;
        public int RunTrend { get; set; } = 0;
        public decimal Efficiency { get; set; } = 0m;
        public int Window { get; set; } = 0;
    }
}


