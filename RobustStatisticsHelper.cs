using System;
using System.Collections.Generic;
using System.Linq;

namespace MyNamespace.Strategies
{
    ///


    /// Robuste Statistik-Hilfsfunktionen:
    /// - Median
    /// - MAD (Median Absolute Deviation)
    /// - Skalierte MAD (auf StdDev-Äquivalent)
    /// - Robuste Z-Score-Berechnung
    ///

    public static class RobustStatisticsHelper
    {
        private const double MadToStdDev = 1.4826;
       
        public static double ComputeMedian(IEnumerable<double> samples)
        {
            if (samples == null) return double.NaN;
            var arr = samples as double[] ?? samples.ToArray();
            if (arr.Length == 0) return double.NaN;
            Array.Sort(arr);
            int n = arr.Length;
            if ((n & 1) == 1) return arr[n / 2];
            return (arr[n / 2 - 1] + arr[n / 2]) / 2.0;
        }

        public static double ComputeMAD(IEnumerable<double> samples)
        {
            if (samples == null) return double.NaN;
            var arr = samples as double[] ?? samples.ToArray();
            if (arr.Length == 0) return double.NaN;
            double med = ComputeMedian(arr);
            var dev = new double[arr.Length];
            for (int i = 0; i < arr.Length; i++) dev[i] = Math.Abs(arr[i] - med);
            Array.Sort(dev);
            int n = dev.Length;
            if ((n & 1) == 1) return dev[n / 2];
            return (dev[n / 2 - 1] + dev[n / 2]) / 2.0;
        }

        public static double ComputeScaledMAD(IEnumerable<double> samples)
        {
            var mad = ComputeMAD(samples);
            if (double.IsNaN(mad)) return double.NaN;
            return mad * MadToStdDev;
        }

        public static double ComputeRobustZ(double value, IEnumerable<double> window, double clamp = double.PositiveInfinity)
        {
            if (window == null) return 0.0;
            var arr = window as double[] ?? window.ToArray();
            if (arr.Length == 0) return 0.0;
            double median = ComputeMedian(arr);
            double scaledMad = ComputeScaledMAD(arr);
            if (double.IsNaN(median) || double.IsNaN(scaledMad) || scaledMad <= double.Epsilon) return 0.0;
            double z = (value - median) / scaledMad;
            if (!double.IsFinite(z)) return 0.0;
            if (!double.IsInfinity(clamp) && Math.Abs(z) > clamp) z = Math.Sign(z) * clamp;
            return z;
        }

        public static bool TryComputeRobustZ(double value, IEnumerable<double> window, out double z, double clamp = double.PositiveInfinity)
        {
            z = 0.0;
            if (window == null) return false;
            var arr = window as double[] ?? window.ToArray();
            if (arr.Length == 0) return false;
            double median = ComputeMedian(arr);
            double scaledMad = ComputeScaledMAD(arr);
            if (double.IsNaN(median) || double.IsNaN(scaledMad) || scaledMad <= double.Epsilon)
            {
                z = 0.0;
                return true;
            }
            z = (value - median) / scaledMad;
            if (!double.IsFinite(z)) { z = 0.0; return true; }
            if (!double.IsInfinity(clamp) && Math.Abs(z) > clamp) z = Math.Sign(z) * clamp;
            return true;
        }
    }
}


