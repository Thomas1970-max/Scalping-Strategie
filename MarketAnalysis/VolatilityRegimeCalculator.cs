using System;
using System.Collections.Generic;
using MyNamespace.Strategies.Models;
using MyNamespace.Strategies.Orderflow;

namespace MyNamespace.Strategies.MarketAnalysis
{
    public sealed class VolatilityRegimeCalculator
    {
        public MarketRegimeDetails Evaluate(
            int bar,
            OvSnapshot currentSnapshot,
            MyNamespace.Strategies.MyClusterStatistic clusterStatistic,
            DateTime currentBarTime,
            DateTime prevBarTime,
            int volPhaseWindow,
            int minConsecutiveForSwitch,
            decimal lowEnterMult,
            decimal lowExitMult,
            decimal highEnterMult,
            decimal highExitMult,
            MarketRegime lastRegime,
            int regimeConsecutiveCount,
            out MarketRegime updatedLastRegime,
            out int updatedRegimeConsecutiveCount)
        {
            updatedLastRegime = lastRegime;
            updatedRegimeConsecutiveCount = regimeConsecutiveCount;

            decimal vps = clusterStatistic.VolPerSecond[bar];
            decimal tradesPerSec = currentSnapshot.TradeRateZ;

            decimal secondsPerBar;
            if (clusterStatistic.CandleDurations != null && clusterStatistic.CandleDurations.Count > bar)
                secondsPerBar = clusterStatistic.CandleDurations[bar];
            else if (currentBarTime > prevBarTime)
                secondsPerBar = (decimal)(currentBarTime - prevBarTime).TotalSeconds;
            else
                secondsPerBar = 0m;

            const decimal VOL_FAST_Z = 1.0m;
            const decimal VOL_SLOW_Z = -1.0m;
            const decimal TR_FAST_MIN = 1.0m;
            const decimal TR_SLOW_MAX = -1.0m;
            const decimal BARSEC_FAST_MULT = 0.6m;
            const decimal BARSEC_SLOW_MULT = 1.8m;

            int w = Math.Max(1, volPhaseWindow);
            int endIdx = bar > 0 ? bar - 1 : 0;
            int startIdx = Math.Max(0, endIdx - w + 1);
            int count = endIdx - startIdx + 1;
            if (count < 1)
                count = 1;

            var values = new List<decimal>(count);
            for (int i = startIdx; i <= endIdx; i++)
            {
                if (clusterStatistic.VolPerSecond.Count > i)
                    values.Add(clusterStatistic.VolPerSecond[i]);
                else
                    values.Add(0m);
            }

            var secValues = new List<decimal>(count);
            if (clusterStatistic.CandleDurations != null)
            {
                for (int i = startIdx; i <= endIdx; i++)
                {
                    if (clusterStatistic.CandleDurations.Count > i)
                        secValues.Add(clusterStatistic.CandleDurations[i]);
                    else
                        secValues.Add(0m);
                }
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
                phaseSigma = (decimal)Math.Sqrt((double)(varSum / values.Count));
            }

            decimal vpsCurrent = vps;
            decimal zPhase = (phaseSigma > 0m) ? (vpsCurrent - phaseMu) / phaseSigma : 0m;

            decimal lowBandPhase = phaseMu * lowEnterMult;
            decimal lowBandExit = phaseMu * lowExitMult;
            decimal highBandPhase = phaseMu * highEnterMult;
            decimal highBandExit = phaseMu * highExitMult;

            decimal avgSeconds = 0m;
            if (secValues.Count > 0)
            {
                decimal sumSec = 0m;
                int nSec = 0;
                foreach (var ss in secValues)
                {
                    if (ss > 0m)
                    {
                        sumSec += ss;
                        nSec++;
                    }
                }

                if (nSec > 0)
                    avgSeconds = sumSec / nSec;
            }

            int fastVotesLocal = 0, slowVotesLocal = 0;
            if (vpsCurrent >= highBandPhase) fastVotesLocal++; else if (vpsCurrent < lowBandPhase) slowVotesLocal++;
            if (phaseSigma > 0m && zPhase >= VOL_FAST_Z) fastVotesLocal++; else if (phaseSigma > 0m && zPhase <= VOL_SLOW_Z) slowVotesLocal++;
            if (tradesPerSec >= TR_FAST_MIN) fastVotesLocal++; else if (tradesPerSec <= TR_SLOW_MAX) slowVotesLocal++;
            if (avgSeconds > 0m && secondsPerBar > 0m && secondsPerBar <= avgSeconds * BARSEC_FAST_MULT) fastVotesLocal++;
            else if (avgSeconds > 0m && secondsPerBar > 0m && secondsPerBar >= avgSeconds * BARSEC_SLOW_MULT) slowVotesLocal++;

            string candidateSpeed = (fastVotesLocal >= 2 && slowVotesLocal < 2) ? "Fast"
                                 : (slowVotesLocal >= 2 && fastVotesLocal < 2) ? "Slow"
                                 : "Normal";

            MarketRegime candidateRegime = candidateSpeed switch
            {
                "Fast" => MarketRegime.Fast,
                "Slow" => MarketRegime.Slow,
                _ => MarketRegime.Normal
            };

            if (candidateRegime == lastRegime)
                updatedRegimeConsecutiveCount = regimeConsecutiveCount + 1;
            else
                updatedRegimeConsecutiveCount = 1;

            bool acceptSwitch;
            int minConsSlow = Math.Max(1, minConsecutiveForSwitch);
            int minConsFast = Math.Min(minConsSlow, 2);
            if (minConsFast < 1)
                minConsFast = 1;
            int minConsNormal = minConsSlow;
            const decimal FAST_SPIKE_MULT = 1.5m;
            switch (candidateRegime)
            {
                case MarketRegime.Fast:
                    if (lastRegime == MarketRegime.Fast)
                    {
                        acceptSwitch = (vpsCurrent >= highBandExit) || (updatedRegimeConsecutiveCount >= minConsFast);
                    }
                    else
                    {
                        acceptSwitch = (vpsCurrent >= highBandPhase && updatedRegimeConsecutiveCount >= minConsFast)
                                       || (vpsCurrent >= highBandPhase * FAST_SPIKE_MULT);
                    }
                    break;

                case MarketRegime.Slow:
                    if (lastRegime == MarketRegime.Slow)
                    {
                        acceptSwitch = (vpsCurrent <= lowBandExit) || (updatedRegimeConsecutiveCount >= minConsSlow);
                    }
                    else
                    {
                        acceptSwitch = (vpsCurrent <= lowBandPhase && updatedRegimeConsecutiveCount >= minConsSlow)
                                       || (vpsCurrent <= lowBandPhase * 0.8m);
                    }
                    break;

                default:
                    acceptSwitch = (updatedRegimeConsecutiveCount >= minConsNormal) || (lastRegime == MarketRegime.Normal);
                    break;
            }

            MarketRegime finalRegime = acceptSwitch ? candidateRegime : lastRegime;
            updatedLastRegime = acceptSwitch ? finalRegime : lastRegime;

            return new MarketRegimeDetails
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
        }
    }

    public sealed class MarketRegimeEvaluator
    {
        private readonly VolatilityRegimeCalculator _calculator = new VolatilityRegimeCalculator();

        private readonly int _volPhaseWindow;
        private readonly int _minConsecutiveForSwitch;
        private readonly decimal _highEnterMult;
        private readonly decimal _highExitMult;
        private readonly decimal _lowEnterMult;
        private readonly decimal _lowExitMult;

        private MarketRegime _lastRegime = MarketRegime.Normal;
        private int _regimeConsecutiveCount = 0;

        public MarketRegimeEvaluator(
            int volPhaseWindow = 30,
            int minConsecutiveForSwitch = 2,
            decimal highEnterMult = 1.15m,
            decimal highExitMult = 1.05m,
            decimal lowEnterMult = 0.85m,
            decimal lowExitMult = 0.95m)
        {
            _volPhaseWindow = Math.Max(1, volPhaseWindow);
            _minConsecutiveForSwitch = Math.Max(1, minConsecutiveForSwitch);
            _highEnterMult = highEnterMult;
            _highExitMult = highExitMult;
            _lowEnterMult = lowEnterMult;
            _lowExitMult = lowExitMult;
        }

        public MarketRegimeDetails GetCurrentMarketRegime(
            int bar,
            OvSnapshot currentSnapshot,
            MyNamespace.Strategies.MyClusterStatistic clusterStatistic,
            IMarketCandle currentCandle,
            IMarketCandle prevCandle)
        {
            if (currentCandle == null || prevCandle == null)
                return new MarketRegimeDetails { Regime = MarketRegime.Normal };

            if (clusterStatistic == null)
                return new MarketRegimeDetails { Regime = MarketRegime.Normal };

            if (clusterStatistic.VolPerSecond == null || clusterStatistic.EmaVolPerSecond == null)
                return new MarketRegimeDetails { Regime = MarketRegime.Normal };

            if (bar < 0 || clusterStatistic.VolPerSecond.Count <= bar || clusterStatistic.EmaVolPerSecond.Count <= bar)
                return new MarketRegimeDetails { Regime = MarketRegime.Normal };

            var details = _calculator.Evaluate(
                bar,
                currentSnapshot,
                clusterStatistic,
                currentCandle.Time,
                prevCandle.Time,
                _volPhaseWindow,
                _minConsecutiveForSwitch,
                _lowEnterMult,
                _lowExitMult,
                _highEnterMult,
                _highExitMult,
                _lastRegime,
                _regimeConsecutiveCount,
                out var updatedLastRegime,
                out var updatedConsecutive);

            _lastRegime = updatedLastRegime;
            _regimeConsecutiveCount = updatedConsecutive;

            return details;
        }
    }
}
