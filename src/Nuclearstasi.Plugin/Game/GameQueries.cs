using System;
using System.Collections.Generic;
using Mirage;
using Nuclearstasi.Core;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace Nuclearstasi.Plugin.Game
{
    internal static class GameQueries
    {
        public static bool IsServer()
        {
            try
            {
                NetworkManager manager = NetworkManagerNuclearOption.i;
                return manager != null && manager.Server != null && manager.Server.Active;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static IEnumerable<KeyValuePair<string, Airbase>> Airbases()
        {
            foreach (KeyValuePair<string, Airbase> entry in FactionRegistry.airbaseLookup)
            {
                Airbase airbase = entry.Value;
                if (airbase == null || airbase.AttachedAirbase)
                {
                    continue;
                }
                yield return entry;
            }
        }

        public static bool TryGetFactions(out FactionHQ rebels, out FactionHQ occupiers)
        {
            rebels = null;
            occupiers = null;
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (hq == null)
                {
                    continue;
                }
                if (hq.preventJoin)
                {
                    occupiers = hq;
                }
                else
                {
                    rebels = hq;
                }
            }
            return rebels != null && occupiers != null && rebels != occupiers;
        }

        public static bool IsLarge(Airbase airbase, float largeRunwayMetres)
        {
            if (airbase.runways == null)
            {
                return false;
            }
            foreach (Airbase.Runway runway in airbase.runways)
            {
                if (runway != null && runway.Length >= largeRunwayMetres)
                {
                    return true;
                }
            }
            return false;
        }

        public static void CountBuildings(Airbase airbase, out int factories, out int depots)
        {
            factories = 0;
            depots = 0;
            foreach (Building building in airbase.buildings)
            {
                if (building == null)
                {
                    continue;
                }
                if (building is VehicleDepot)
                {
                    depots++;
                }
                if (building.GetComponentInChildren<Factory>() != null)
                {
                    factories++;
                }
            }
        }

        public static Territory MeasureTerritory(FactionHQ rebels, FactionHQ occupiers, float largeRunwayMetres)
        {
            var territory = new Territory();
            foreach (KeyValuePair<string, Airbase> entry in Airbases())
            {
                Airbase airbase = entry.Value;
                if (airbase.disabled)
                {
                    continue;
                }
                bool large = IsLarge(airbase, largeRunwayMetres);
                CountBuildings(airbase, out int factories, out int depots);
                var counts = new SiteCounts
                {
                    SmallAirbases = large ? 0 : 1,
                    LargeAirbases = large ? 1 : 0,
                    Factories = factories,
                    Depots = depots,
                };
                if (airbase.CurrentHQ == rebels)
                {
                    territory.Rebel = territory.Rebel.Plus(counts);
                }
                else if (airbase.CurrentHQ == occupiers)
                {
                    territory.Occupier = territory.Occupier.Plus(counts);
                }
                else
                {
                    territory.Neutral = territory.Neutral.Plus(counts);
                }
            }
            return territory;
        }

        public static string DisplayName(Airbase airbase)
        {
            SavedAirbase saved = airbase.SavedAirbase;
            if (saved != null && !string.IsNullOrEmpty(saved.DisplayName))
            {
                return saved.DisplayName;
            }
            return airbase.NetworknetworkUniqueName;
        }

        public static Airbase FindByPrefix(string prefix)
        {
            foreach (KeyValuePair<string, Airbase> entry in Airbases())
            {
                string name = DisplayName(entry.Value) ?? "";
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Value;
                }
            }
            return null;
        }

        public static Dictionary<string, int> MissionSupplies(Mission mission, FactionHQ hq)
        {
            var supplies = new Dictionary<string, int>();
            mission.GetFactionFromHq(hq, out MissionFaction missionFaction);
            if (missionFaction?.supplies == null)
            {
                return supplies;
            }
            foreach (UnitCount count in missionFaction.supplies)
            {
                if (count == null || string.IsNullOrEmpty(count.UnitType))
                {
                    continue;
                }
                supplies.TryGetValue(count.UnitType, out int existing);
                supplies[count.UnitType] = existing + count.Count;
            }
            return supplies;
        }
    }
}
