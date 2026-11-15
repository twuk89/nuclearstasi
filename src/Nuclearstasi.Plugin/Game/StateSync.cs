using System.Collections.Generic;
using Nuclearstasi.Core;
using NuclearOption.Networking;

namespace Nuclearstasi.Plugin.Game
{
    internal static class StateSync
    {
        public static void Capture(CampaignState state, FactionHQ rebels, FactionHQ occupiers)
        {
            state.AirbaseOwners.Clear();
            state.Warheads.Clear();
            state.Supplies.Clear();
            foreach (KeyValuePair<string, Airbase> entry in GameQueries.Airbases())
            {
                Airbase airbase = entry.Value;
                state.AirbaseOwners[entry.Key] = airbase.CurrentHQ != null ? airbase.CurrentHQ.faction.factionName : "";
                if (airbase.HasStorage())
                {
                    state.Warheads[entry.Key] = airbase.GetWarheads();
                }
            }
            state.Supplies[rebels.faction.factionName] = ReadSupplies(rebels);
            state.Supplies[occupiers.faction.factionName] = ReadSupplies(occupiers);

            float treasury = rebels.factionFunds;
            foreach (Player player in rebels.GetPlayers(false))
            {
                if (player != null)
                {
                    treasury += player.Allocation;
                }
            }
            state.RebelTreasury = treasury;
        }

        public static void Apply(CampaignState state, FactionHQ rebels, FactionHQ occupiers)
        {
            foreach (KeyValuePair<string, string> owner in state.AirbaseOwners)
            {
                if (!FactionRegistry.airbaseLookup.TryGetValue(owner.Key, out Airbase airbase) || airbase == null)
                {
                    Plugin.Log.LogWarning($"Saved airbase '{owner.Key}' not found in mission; skipped");
                    continue;
                }
                FactionHQ target = string.IsNullOrEmpty(owner.Value) ? null : FactionRegistry.HqFromName(owner.Value);
                if (airbase.CurrentHQ == target)
                {
                    continue;
                }
                if (target == null)
                {
                    ((ICapturable)airbase).OnCapture(null);
                }
                else
                {
                    airbase.capture.ForceCapture(target);
                }
                Plugin.Log.LogInfo($"Restored {GameQueries.DisplayName(airbase)} -> {(target == null ? "neutral" : owner.Value)}");
            }

            foreach (KeyValuePair<string, int> warheads in state.Warheads)
            {
                if (!FactionRegistry.airbaseLookup.TryGetValue(warheads.Key, out Airbase airbase) || airbase == null || !airbase.HasStorage())
                {
                    continue;
                }
                int diff = warheads.Value - airbase.GetWarheads();
                if (diff > 0)
                {
                    airbase.AddWarheads(diff);
                }
                else if (diff < 0)
                {
                    airbase.RemoveWarheads(-diff);
                }
            }

            ApplySupplies(state, rebels);
            ApplySupplies(state, occupiers);
            rebels.SetFunds(state.RebelTreasury);
        }

        public static Dictionary<string, int> ReadSupplies(FactionHQ hq)
        {
            var supplies = new Dictionary<string, int>();
            foreach (KeyValuePair<AircraftDefinition, FactionHQ.RuntimeSupply> entry in hq.AircraftSupply)
            {
                supplies[entry.Key.jsonKey] = entry.Value.Count;
            }
            foreach (KeyValuePair<VehicleDefinition, FactionHQ.RuntimeSupply> entry in hq.VehicleSupply)
            {
                supplies[entry.Key.jsonKey] = entry.Value.Count;
            }
            return supplies;
        }

        private static void ApplySupplies(CampaignState state, FactionHQ hq)
        {
            if (!state.Supplies.TryGetValue(hq.faction.factionName, out Dictionary<string, int> saved))
            {
                return;
            }
            Dictionary<string, int> current = ReadSupplies(hq);
            var keys = new HashSet<string>(saved.Keys);
            keys.UnionWith(current.Keys);
            foreach (string key in keys)
            {
                saved.TryGetValue(key, out int wanted);
                current.TryGetValue(key, out int have);
                if (wanted == have)
                {
                    continue;
                }
                if (!Encyclopedia.Lookup.TryGetValue(key, out UnitDefinition definition))
                {
                    Plugin.Log.LogWarning($"Unknown unit '{key}' in saved supplies; skipped");
                    continue;
                }
                hq.AddSupplyUnit(definition, wanted - have);
            }
        }
    }
}
