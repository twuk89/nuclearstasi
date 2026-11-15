using HarmonyLib;
using Nuclearstasi.Plugin.Campaign;

namespace Nuclearstasi.Plugin.Patches
{
    [HarmonyPatch(typeof(MissionManager), nameof(MissionManager.StartMission))]
    internal static class StartMissionPatch
    {
        private static void Postfix()
        {
            try
            {
                CampaignController.Instance?.OnMissionStarted(MissionManager.CurrentMission);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("StartMissionPatch failed: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(MissionManager), "UnloadMission")]
    internal static class UnloadMissionPatch
    {
        private static void Prefix()
        {
            try
            {
                CampaignController.Instance?.OnMissionEnding();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("UnloadMissionPatch failed: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(FactionHQ), "OnDestroy")]
    internal static class FactionHQDestroyPatch
    {
        private static void Prefix(FactionHQ __instance)
        {
            try
            {
                CampaignController.Instance?.OnFactionDestroyed(__instance);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("FactionHQDestroyPatch failed: " + e);
            }
        }
    }
}
