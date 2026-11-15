using System;
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
                try
                {
                    RestoreOwner(owner.Key, owner.Value);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Restoring owner of '{owner.Key}' failed: {e.Message}");
                }
            }

            foreach (KeyValuePair<string, int> warheads in state.Warheads)
            {
                try
                {
                    RestoreWarheads(warheads.Key, warheads.Value);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Restoring warheads of '{warheads.Key}' failed: {e.Message}");
                }
            }

            ApplySupplies(state, rebels);
            ApplySupplies(state, occupiers);
            try
            {
                RestoreFunds(state, rebels);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Restoring funds '{rebels.faction.factionName}' failed: {e.Message}");
            }
        }

        private static void RestoreOwner(string key, string ownerName)
        {
            if (!FactionRegistry.airbaseLookup.TryGetValue(key, out Airbase airbase) || airbase == null)
            {
                Plugin.Log.LogWarning($"Saved airbase '{key}' not found in mission; skipped");
                return;
            }
            FactionHQ target = null;
            if (!string.IsNullOrEmpty(ownerName))
            {
                target = FactionRegistry.HqFromName(ownerName);
                if (target == null)
                {
                    Plugin.Log.LogWarning($"Owner '{ownerName}' of airbase '{key}' not resolved; skipped");
                    return;
                }
            }
            if (airbase.CurrentHQ == target)
            {
                return;
            }
            if (airbase.capture == null)
            {
                Plugin.Log.LogWarning($"Airbase '{key}' has no capture component; skipped");
                return;
            }
            if (target == null)
            {
                ((ICapturable)airbase).OnCapture(null);
                // OnCapture(null) leaves the capture balance stale; reset it to the mission's setting.
                airbase.capture.SetCapturable(airbase.SavedAirbase == null || airbase.SavedAirbase.Capturable);
            }
            else
            {
                airbase.capture.ForceCapture(target);
            }
            Plugin.Log.LogInfo($"Restored {GameQueries.DisplayName(airbase)} -> {(target == null ? "neutral" : ownerName)}");
        }

        private static void RestoreWarheads(string key, int wanted)
        {
            if (!FactionRegistry.airbaseLookup.TryGetValue(key, out Airbase airbase) || airbase == null || !airbase.HasStorage())
            {
                return;
            }
            int diff = wanted - airbase.GetWarheads();
            if (diff > 0)
            {
                airbase.AddWarheads(diff);
            }
            else if (diff < 0)
            {
                airbase.RemoveWarheads(-diff);
            }
        }

        private static void RestoreFunds(CampaignState state, FactionHQ rebels)
        {
            // Players already present hold their personal funds, so exclude them from the pool to avoid double-counting.
            float allocated = 0f;
            foreach (Player player in rebels.GetPlayers(false))
            {
                if (player != null)
                {
                    allocated += player.Allocation;
                }
            }
            rebels.SetFunds(Math.Max(0f, state.RebelTreasury - allocated));
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
                try
                {
                    RestoreSupply(hq, key, saved, current);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Restoring supply '{key}' failed: {e.Message}");
                }
            }
        }

        private static void RestoreSupply(FactionHQ hq, string key, Dictionary<string, int> saved, Dictionary<string, int> current)
        {
            saved.TryGetValue(key, out int wanted);
            current.TryGetValue(key, out int have);
            if (wanted == have)
            {
                return;
            }
            if (!Encyclopedia.Lookup.TryGetValue(key, out UnitDefinition definition))
            {
                Plugin.Log.LogWarning($"Unknown unit '{key}' in saved supplies; skipped");
                return;
            }
            hq.ModifyUnitSupply(definition, wanted - have);
        }
    }
}
