using System;
using System.Globalization;

namespace Nuclearstasi.Core
{
    public static class Messages
    {
        public static string TierName(Tier tier)
        {
            switch (tier)
            {
                case Tier.Alert: return "ALERT";
                case Tier.Hostile: return "HOSTILE";
                case Tier.TotalWar: return "TOTAL WAR";
                default: return "QUIET";
            }
        }

        public static string TierChanged(TierChange change)
        {
            if (!change.Changed)
            {
                return null;
            }
            if (!change.Rose)
            {
                return "Occupation forces have stood down to " + TierName(change.To);
            }
            switch (change.To)
            {
                case Tier.Alert: return "Occupation forces are now on ALERT";
                case Tier.Hostile: return "Occupation forces are now HOSTILE";
                default: return "The occupation has declared TOTAL WAR";
            }
        }

        public static string Status(double aggression, Tier tier, Territory t, float rebelFunds, double elapsedSeconds)
        {
            var time = TimeSpan.FromSeconds(Math.Max(0, elapsedSeconds));
            return string.Format(
                CultureInfo.InvariantCulture,
                "Aggression {0:0} ({1}) | Territory {2}/{3} sites ({4:0}%) | Rebel funds {5:0} | Campaign time {6}h {7:00}m",
                aggression,
                TierName(tier),
                t.Rebel.Total,
                t.TotalSites,
                t.RebelShare * 100,
                rebelFunds,
                (int)time.TotalHours,
                time.Minutes);
        }

        public static string CampaignStarted()
        {
            return "Nuclearstasi: a new campaign has begun. The occupation does not know you exist... yet.";
        }

        public static string CampaignResumed(Tier tier)
        {
            return "Nuclearstasi: campaign resumed. Occupation status: " + TierName(tier);
        }

        public static string CorruptSave()
        {
            return "Nuclearstasi: the campaign save could not be read and was set aside. Starting a new campaign.";
        }

        public static string AirbaseCaptured(string name)
        {
            return "Rebels have captured " + name;
        }
    }
}
