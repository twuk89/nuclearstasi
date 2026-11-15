using System;
using System.Collections.Generic;

namespace Nuclearstasi.Core
{
    public sealed class ReinforcementPlanner
    {
        private readonly Dictionary<string, int> baseline;
        private readonly Dictionary<string, double> carry = new Dictionary<string, double>();
        private readonly ReinforcementSettings settings;

        public ReinforcementPlanner(IDictionary<string, int> baseline, ReinforcementSettings settings)
        {
            this.baseline = new Dictionary<string, int>(baseline);
            this.settings = settings;
        }

        public static double Multiplier(double occupierShare, double exponent)
        {
            double share = Math.Max(0, Math.Min(1, occupierShare));
            return Math.Pow(share, exponent);
        }

        public Dictionary<string, int> NextWave(double occupierShare)
        {
            var wave = new Dictionary<string, int>();
            double multiplier = Multiplier(occupierShare, settings.ShareExponent);
            foreach (KeyValuePair<string, int> entry in baseline)
            {
                if (entry.Value <= 0)
                {
                    continue;
                }
                carry.TryGetValue(entry.Key, out double owed);
                owed += entry.Value * settings.WaveFraction * multiplier;
                int whole = (int)Math.Floor(owed + 1e-9);
                carry[entry.Key] = owed - whole;
                if (whole > 0)
                {
                    wave[entry.Key] = whole;
                }
            }
            return wave;
        }
    }
}
