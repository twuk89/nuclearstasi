using System.Collections.Generic;

namespace Nuclearstasi.Core
{
    public enum CampaignStatus
    {
        Active,
        Won,
        Lost,
    }

    public sealed class CampaignState
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        public string MissionName = "";
        public CampaignStatus Status = CampaignStatus.Active;
        public double ElapsedSeconds;
        public double Aggression;
        public bool CapitalUnlocked;
        public float RebelTreasury;

        /// <summary>Airbase key → owning faction name ("" = neutral).</summary>
        public Dictionary<string, string> AirbaseOwners = new Dictionary<string, string>();

        /// <summary>Airbase key → stored warheads.</summary>
        public Dictionary<string, int> Warheads = new Dictionary<string, int>();

        /// <summary>Faction name → unit jsonKey → stockpiled count.</summary>
        public Dictionary<string, Dictionary<string, int>> Supplies = new Dictionary<string, Dictionary<string, int>>();

        public void EnsureCollections()
        {
            if (MissionName == null) MissionName = "";
            if (AirbaseOwners == null) AirbaseOwners = new Dictionary<string, string>();
            if (Warheads == null) Warheads = new Dictionary<string, int>();
            if (Supplies == null) Supplies = new Dictionary<string, Dictionary<string, int>>();
            foreach (string key in new List<string>(Supplies.Keys))
            {
                if (Supplies[key] == null) Supplies[key] = new Dictionary<string, int>();
            }
            foreach (string key in new List<string>(AirbaseOwners.Keys))
            {
                if (AirbaseOwners[key] == null) AirbaseOwners[key] = "";
            }
        }
    }
}
