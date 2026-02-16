using System;
using System.Collections.Generic;
using System.Linq;
using MyNamespace.Strategies.Models;

namespace MyNamespace.Strategies.Orderflow
{
    public sealed class MarketStateEngineV2
    {
        private readonly decimal _tickSize;
        private readonly int _slopeLookbackK;
        private readonly int _zWindowN;
        private readonly decimal _overextendedMultiplier;

        private readonly int _rangeConfirmBars;

        private decimal _lastSlopeTicksPerBar;

        private readonly Queue<decimal> _slopeWindow = new Queue<decimal>();
        private readonly Queue<decimal> _vwapWindow = new Queue<decimal>();

        private readonly Queue<(decimal poc, decimal vah, decimal val)> _pocVaWindow = new Queue<(decimal poc, decimal vah, decimal val)>();
        private readonly Queue<(decimal high, decimal close, decimal vwap, decimal upper1, decimal lower1, decimal upper2, decimal lower2)> _priceWindow = new Queue<(decimal high, decimal close, decimal vwap, decimal upper1, decimal lower1, decimal upper2, decimal lower2)>();

        private MarketPhaseV2? _prevPhase;
        private MarketRegime? _prevRegime;
        private int _rangeCandidateCount;

        public MarketStateEngineV2(decimal tickSize, int slopeLookbackK = 5, int zWindowN = 100, decimal overextendedMultiplier = 2.5m, int rangeConfirmBars = 3)
        {
            _tickSize = tickSize > 0m ? tickSize : 0.25m;
            _slopeLookbackK = Math.Max(1, slopeLookbackK);
            _zWindowN = Math.Max(10, zWindowN);
            _overextendedMultiplier = overextendedMultiplier > 0m ? overextendedMultiplier : 2.5m;
            _rangeConfirmBars = Math.Max(1, rangeConfirmBars);
        }

        public MarketStateV2 Update(MarketStateInputV2 input)
        {
            var state = new MarketStateV2
            {
                Dynamic = input.Regime
            };

            const decimal RangeSigmaAbsThreshold = 0.5m;
            const decimal ImpulseSigmaThreshold = 1.1m;
            const decimal BreakoutSigmaAbsThreshold = 1.7m;
            const decimal MaturingSigmaThreshold = 0.9m;
            const bool UseHardRegimeCooldownGateForHealthyPullback = false;

            // ---- VWAP slope + z-score (k=5, N=100) ----
            decimal zSlope = UpdateZSlope(input.Vwap);
            state.ZSlope = zSlope;

            // ---- SD1 / Sigma positioning ----
            decimal sd1 = input.UpperBand1 > 0m ? (input.UpperBand1 - input.Vwap) : 0m;
            state.Sd1 = sd1;
            decimal sigma = sd1 > 0m ? (input.Close - input.Vwap) / sd1 : 0m;
            state.SigmaFromVwap = sigma;

            // ---- VWAP anchor confirmation + Bias ----
            // Anchor = Preis auf richtiger Seite + VWAP-Slope in gleicher Richtung.
            // Dadurch wird verhindert, dass kurze "Wackler" um den VWAP als Trend gewertet werden.
            bool slopeUp = _lastSlopeTicksPerBar > 0m;
            bool slopeDown = _lastSlopeTicksPerBar < 0m;

            bool anchorBull = input.Close > input.Vwap && slopeUp;
            bool anchorBear = input.Close < input.Vwap && slopeDown;
            state.AnchorTrendConfirmed = anchorBull || anchorBear;

            if (anchorBull) state.Bias = MarketBiasV2.Long;
            else if (anchorBear) state.Bias = MarketBiasV2.Short;
            else state.Bias = MarketBiasV2.Neutral;

            // ---- POC staircase index (6 bars => 5 steps) ----
            int staircase = UpdateStaircase(input.CandlePocPrice, input.CurrentVAH, input.CurrentVAL);
            state.StaircaseIndex = staircase;

            // ---- SD2.5 levels (explicit via SD1 distance) ----
            // Hack: UpperBand1 = VWAP + 1*SD => SD = UpperBand1 - VWAP
            decimal upperSdX = (sd1 > 0m) ? (input.Vwap + sd1 * _overextendedMultiplier) : 0m;
            decimal lowerSdX = (sd1 > 0m) ? (input.Vwap - sd1 * _overextendedMultiplier) : 0m;

            bool isOverextended = IsOverextended(input.Close, state.Bias, upperSdX, lowerSdX);

            bool regimeCooling = IsRegimeCooling(_prevRegime, input.Regime);

            // ---- Core hierarchical rules (per spec) ----
            if (isOverextended)
            {
                state.Phase = MarketPhaseV2.Exhaustion;
                state.VolatilityMultiplier = 1.0m;
                state.IsTradeable = true;
                _prevPhase = state.Phase;
                _prevRegime = input.Regime;
                return state;
            }

            // ---- Pullback zone context (VWAP bis 1σ in Trendrichtung) ----
            // Bull: 0..+1σ (oberhalb VWAP, bis SD1)
            // Bear: -1σ..0 (unterhalb VWAP, bis SD1)
            state.InPullbackZone = state.AnchorTrendConfirmed &&
                                   ((state.Bias == MarketBiasV2.Long && sigma >= 0m && sigma <= 1.0m) ||
                                    (state.Bias == MarketBiasV2.Short && sigma <= 0m && sigma >= -1.0m));

            decimal signedSigma = (state.Bias == MarketBiasV2.Long) ? sigma : (state.Bias == MarketBiasV2.Short ? -sigma : 0m);

            // Trend_Impulse:
            // StaircaseIndex == 5 AND ZSlope > 2.0 AND price < SD2.5
            if (state.AnchorTrendConfirmed &&
                input.Regime != MarketRegime.Slow &&
                Math.Abs(staircase) >= 4 &&
                SignedZ(zSlope, state.Bias) > 1.5m &&
                signedSigma >= ImpulseSigmaThreshold)
            {
                state.Phase = MarketPhaseV2.Trend_Impulse;
                state.VolatilityMultiplier = (input.Regime == MarketRegime.Fast) ? 1.15m : 1.0m;
                state.IsTradeable = true;
                _prevPhase = state.Phase;
                _prevRegime = input.Regime;
                return state;
            }

            // Healthy_Pullback:
            // 2-bar criterion (per spec):
            // - Context: prevPhase ideally Trend_Impulse/Volatile_Breakout (optional, recommended)
            // - Structure break: staircase not at +/-5 (practically: abs <=2 or drop below 3)
            // - Vector check:
            //   prevHigh near/over SD2
            //   currClose below SD2 but still above VWAP (or SD1)
            //   currClose < prevClose
            //   staircase break, optional vorherige Phase trendig).
            bool healthyPullback = state.AnchorTrendConfirmed &&
                                  state.InPullbackZone &&
                                  (!UseHardRegimeCooldownGateForHealthyPullback
                                      ? input.Regime != MarketRegime.Fast
                                      : (input.Regime != MarketRegime.Fast && regimeCooling)) &&
                                  IsHealthyPullback2Bar(input, state.Bias, staircase);
            if (healthyPullback)
            {
                state.Phase = MarketPhaseV2.Healthy_Pullback;
                state.VolatilityMultiplier = 1.0m;
                state.IsTradeable = true;
                _prevPhase = state.Phase;
                _prevRegime = input.Regime;
                return state;
            }

            // Range_Balanced default:
            // Flat slope + price in VA + clustering
            bool slopeFlat = Math.Abs(zSlope) <= 0.5m;
            bool inValueArea = IsInValueArea(input.Close, input.CurrentVAH, input.CurrentVAL);
            bool pocClustered = IsLastPocClustered();

            bool rangeCandidate = slopeFlat &&
                                  input.Regime != MarketRegime.Fast &&
                                  Math.Abs(sigma) <= RangeSigmaAbsThreshold &&
                                  inValueArea &&
                                  pocClustered;
            if (rangeCandidate) _rangeCandidateCount++;
            else _rangeCandidateCount = 0;

            if (rangeCandidate && _rangeCandidateCount >= _rangeConfirmBars)
            {
                state.Phase = MarketPhaseV2.Range_Balanced;
                state.VolatilityMultiplier = 1.0m;
                state.IsTradeable = true;
                _prevPhase = state.Phase;
                _prevRegime = input.Regime;
                return state;
            }

            // Volatile_Breakout heuristic:
            // Regime FAST + leaving VA
            if (input.Regime == MarketRegime.Fast &&
                !inValueArea &&
                Math.Abs(sigma) >= BreakoutSigmaAbsThreshold)
            {
                state.Phase = MarketPhaseV2.Volatile_Breakout;
                state.VolatilityMultiplier = 1.5m;
                state.IsTradeable = true;
                _prevPhase = state.Phase;
                _prevRegime = input.Regime;
                return state;
            }

            // Maturing_Trend heuristic:
            // Trend bias but slope not significant anymore
            if (state.AnchorTrendConfirmed &&
                state.Bias != MarketBiasV2.Neutral &&
                input.Regime != MarketRegime.Fast &&
                Math.Abs(sigma) > RangeSigmaAbsThreshold &&
                Math.Abs(zSlope) <= 1.0m &&
                signedSigma >= 0m && signedSigma < MaturingSigmaThreshold)
            {
                state.Phase = MarketPhaseV2.Maturing_Trend;
                state.VolatilityMultiplier = 0.9m;
                state.IsTradeable = true;
                _prevPhase = state.Phase;
                _prevRegime = input.Regime;
                return state;
            }

            // fallback
            state.Phase = state.Bias == MarketBiasV2.Neutral
                ? MarketPhaseV2.Range_Balanced
                : (Math.Abs(sigma) <= RangeSigmaAbsThreshold ? MarketPhaseV2.Range_Balanced : MarketPhaseV2.Maturing_Trend);
            state.VolatilityMultiplier = 1.0m;
            state.IsTradeable = true;
            _prevPhase = state.Phase;
            _prevRegime = input.Regime;
            return state;
        }

        private bool IsHealthyPullback2Bar(MarketStateInputV2 input, MarketBiasV2 bias, int staircase)
        {
            _priceWindow.Enqueue((input.High, input.Close, input.Vwap, input.UpperBand1, input.LowerBand1, input.UpperBand2, input.LowerBand2));
            while (_priceWindow.Count > 2)
                _priceWindow.Dequeue();

            if (_priceWindow.Count < 2)
                return false;

            // Optional context gate: previous phase was trend-supporting
            bool contextOk = _prevPhase == MarketPhaseV2.Trend_Impulse || _prevPhase == MarketPhaseV2.Volatile_Breakout || _prevPhase == null;
            if (!contextOk)
                return false;

            // Structure break: not at full staircase anymore
            bool structureBreak = Math.Abs(staircase) <= 2;
            if (!structureBreak)
                return false;

            var arr = _priceWindow.ToArray();
            var prev = arr[0];
            var cur = arr[1];

            if (bias == MarketBiasV2.Long)
            {
                decimal prevSd1 = prev.upper1 > 0m ? (prev.upper1 - prev.vwap) : 0m;
                decimal prevUpperSd15 = prevSd1 > 0m ? (prev.vwap + prevSd1 * 1.5m) : 0m;
                bool prevExtended = (prev.upper2 > 0m && prev.high >= prev.upper2) || (prevUpperSd15 > 0m && prev.high >= prevUpperSd15);
                bool curBelowSd2 = cur.close < cur.upper2 && cur.upper2 > 0m;
                bool curAboveVwap = cur.close > cur.vwap;
                bool curAboveSd1 = cur.close > cur.upper1 && cur.upper1 > 0m;
                bool falling = cur.close < prev.close;
                return prevExtended && curBelowSd2 && (curAboveVwap || curAboveSd1) && falling;
            }

            if (bias == MarketBiasV2.Short)
            {
                decimal prevSd1 = prev.lower1 > 0m ? (prev.vwap - prev.lower1) : 0m;
                decimal prevLowerSd15 = prevSd1 > 0m ? (prev.vwap - prevSd1 * 1.5m) : 0m;
                bool prevExtended = (prev.lower2 > 0m && prev.high <= prev.lower2) || (prevLowerSd15 > 0m && prev.high <= prevLowerSd15);
                bool curAboveSd2 = cur.close > cur.lower2 && cur.lower2 > 0m;
                bool curBelowVwap = cur.close < cur.vwap;
                bool curBelowSd1 = cur.close < cur.lower1 && cur.lower1 > 0m;
                bool rising = cur.close > prev.close;
                return prevExtended && curAboveSd2 && (curBelowVwap || curBelowSd1) && rising;
            }

            return false;
        }

        private decimal UpdateZSlope(decimal vwap)
        {
            _vwapWindow.Enqueue(vwap);
            while (_vwapWindow.Count > _slopeLookbackK + _zWindowN + 5)
                _vwapWindow.Dequeue();

            if (_vwapWindow.Count <= _slopeLookbackK)
                return 0m;

            var arr = _vwapWindow.ToArray();
            decimal vwapNow = arr[arr.Length - 1];
            decimal vwapPrev = arr[arr.Length - 1 - _slopeLookbackK];

            decimal slopeTicksPerBar = (vwapNow - vwapPrev) / (_slopeLookbackK * _tickSize);
            _lastSlopeTicksPerBar = slopeTicksPerBar;

            _slopeWindow.Enqueue(slopeTicksPerBar);
            while (_slopeWindow.Count > _zWindowN)
                _slopeWindow.Dequeue();

            if (_slopeWindow.Count < Math.Min(_zWindowN, 20))
                return 0m;

            var slopes = _slopeWindow.ToArray();
            decimal mean = slopes.Average();
            decimal var = 0m;
            for (int i = 0; i < slopes.Length; i++)
            {
                var d = slopes[i] - mean;
                var += d * d;
            }
            var /= slopes.Length;
            decimal std = (decimal)Math.Sqrt((double)var);
            if (std <= 0.0000001m)
                return 0m;

            return (slopeTicksPerBar - mean) / std;
        }

        private static bool IsRegimeCooling(MarketRegime? prev, MarketRegime current)
        {
            if (!prev.HasValue)
                return false;

            int prevRank = RegimeRank(prev.Value);
            int curRank = RegimeRank(current);
            return curRank < prevRank;
        }

        private static int RegimeRank(MarketRegime r)
        {
            return r switch
            {
                MarketRegime.Fast => 3,
                MarketRegime.Normal => 2,
                MarketRegime.Slow => 1,
                _ => 0
            };
        }

        private int UpdateStaircase(decimal poc, decimal vah, decimal val)
        {
            _pocVaWindow.Enqueue((poc, vah, val));
            while (_pocVaWindow.Count > 6)
                _pocVaWindow.Dequeue();

            if (_pocVaWindow.Count < 6)
                return 0;

            var arr = _pocVaWindow.ToArray();
            int sum = 0;
            for (int i = 1; i < arr.Length; i++)
            {
                var prev = arr[i - 1];
                var cur = arr[i];

                // 0 if clustered: POC within previous bar's VA
                if (prev.vah > 0m && prev.val > 0m && cur.poc >= prev.val && cur.poc <= prev.vah)
                {
                    sum += 0;
                    continue;
                }

                if (cur.poc > prev.poc) sum += 1;
                else if (cur.poc < prev.poc) sum -= 1;
            }

            return sum;
        }

        private bool HasStaircaseBreak()
        {
            if (_pocVaWindow.Count < 6)
                return false;

            // break heuristic: previously strong (abs>=4), now weaker (abs<=2)
            var arr = _pocVaWindow.ToArray();
            int prevSum = 0;
            for (int i = 1; i < arr.Length - 1; i++)
            {
                var prev = arr[i - 1];
                var cur = arr[i];
                if (prev.vah > 0m && prev.val > 0m && cur.poc >= prev.val && cur.poc <= prev.vah)
                    continue;
                if (cur.poc > prev.poc) prevSum += 1;
                else if (cur.poc < prev.poc) prevSum -= 1;
            }

            int lastSum = 0;
            for (int i = 1; i < arr.Length; i++)
            {
                var prev = arr[i - 1];
                var cur = arr[i];
                if (prev.vah > 0m && prev.val > 0m && cur.poc >= prev.val && cur.poc <= prev.vah)
                    continue;
                if (cur.poc > prev.poc) lastSum += 1;
                else if (cur.poc < prev.poc) lastSum -= 1;
            }

            return Math.Abs(prevSum) >= 4 && Math.Abs(lastSum) <= 2;
        }

        private bool IsLastPocClustered()
        {
            if (_pocVaWindow.Count < 2)
                return false;

            var arr = _pocVaWindow.ToArray();
            var prev = arr[arr.Length - 2];
            var cur = arr[arr.Length - 1];
            if (prev.vah <= 0m || prev.val <= 0m)
                return false;
            return cur.poc >= prev.val && cur.poc <= prev.vah;
        }

        private static bool IsInValueArea(decimal price, decimal vah, decimal val)
        {
            if (vah <= 0m || val <= 0m)
                return false;
            return price >= val && price <= vah;
        }

        private static bool IsOverextended(decimal price, MarketBiasV2 bias, decimal upperSd25, decimal lowerSd25)
        {
            if (bias == MarketBiasV2.Long)
                return upperSd25 > 0m && price > upperSd25;
            if (bias == MarketBiasV2.Short)
                return lowerSd25 > 0m && price < lowerSd25;
            return false;
        }

        private static decimal SignedZ(decimal z, MarketBiasV2 bias)
        {
            if (bias == MarketBiasV2.Long) return z;
            if (bias == MarketBiasV2.Short) return -z;
            return 0m;
        }

        private static decimal Lerp(decimal a, decimal b, decimal t)
        {
            return a + (b - a) * t;
        }
    }
}
