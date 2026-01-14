using System;
using System.Linq;
// using ATAS.DataFeedsCore; // Nicht direkt in der Engine benötigt, aber für Modelle
using MyNamespace.Strategies.Models;
using static MyNamespace.Strategies.Goldfluss3_3;
using Utils.Common.Logging; // LoggerHelper.Verwendung

namespace MyNamespace.Strategies.Orderflow
{
    public static class SqueezeUtils
    {
        public static decimal MapSqueezeToVolScore(SqueezeFeatures s)
        {
            if (s == null) return 0.5m;
            return s.SqueezeState switch
            {
                SqueezeStateEnum.On => 0.1m,
                SqueezeStateEnum.Off => 1.0m,
                SqueezeStateEnum.NoSqueeze => 0.5m,
                _ => 0.5m
            };
        }

       
        public static decimal GetDirScore(SqueezeFeatures s, decimal lambda = 0.02m)
        {
            if (s == null) return 0m;
            var x = (double)(s.MomentumVal / lambda);
            return (decimal)Math.Tanh(x);
        }

        public static bool SqueezeSupportsBreakout(SqueezeFeatures s, decimal momentumValThreshold = 0.0005m, int requiredDurationBars = 2)
        {
            if (s == null) return true;
            if (s.SqueezeState == SqueezeStateEnum.Off) return true;
            if (s.SqueezeState == SqueezeStateEnum.On)
            {
                return (Math.Abs(s.MomentumVal) >= momentumValThreshold) && (s.MomentumSlope > 0m) && (s.StateDurationBars >= requiredDurationBars);
            }
            return true;
        }

        public static bool IsSqueezeReady(SqueezeFeatures s)
        {
            if (s == null) return false;
            if (s.SqueezeState == SqueezeStateEnum.Unknown || s.SqueezeState == SqueezeStateEnum.NoSqueeze) return false;
            if (s.StateDurationBars <= 0 && s.MomentumVal == 0m) return false;
            return true;
        }
    }
}


