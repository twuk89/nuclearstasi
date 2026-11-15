using HarmonyLib;
using Nuclearstasi.Plugin.Campaign;

namespace Nuclearstasi.Plugin.Patches
{
    [HarmonyPatch(typeof(MissionManager), nameof(MissionManager.StartMission))]
    internal static class StartMissionPatch
    {
        private static void Postfix()
        {
            CampaignController.Instance?.OnMissionStarted(MissionManager.CurrentMission);
        }
    }

    [HarmonyPatch(typeof(MissionManager), "UnloadMission")]
    internal static class UnloadMissionPatch
    {
        private static void Prefix()
        {
            CampaignController.Instance?.OnMissionEnding();
        }
    }

    [HarmonyPatch(typeof(FactionHQ), "OnDestroy")]
    internal static class FactionHQDestroyPatch
    {
        private static void Prefix(FactionHQ __instance)
        {
            CampaignController.Instance?.OnFactionDestroyed(__instance);
        }
    }
}
