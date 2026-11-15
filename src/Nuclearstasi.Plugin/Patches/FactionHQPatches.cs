using HarmonyLib;
using Nuclearstasi.Plugin.Campaign;

namespace Nuclearstasi.Plugin.Patches
{
    [HarmonyPatch(typeof(FactionHQ), "DistributeFunds")]
    internal static class DistributeFundsPatch
    {
        private static void Prefix(FactionHQ __instance)
        {
            try
            {
                CampaignController.Instance?.BeforeDistributeFunds(__instance);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("DistributeFundsPatch failed: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(FactionHQ), nameof(FactionHQ.ReportKillAction))]
    internal static class ReportKillActionPatch
    {
        private static void Postfix(FactionHQ __instance, Unit target)
        {
            try
            {
                CampaignController.Instance?.OnKillReported(__instance, target);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("ReportKillActionPatch failed: " + e);
            }
        }
    }

    /// <summary>Vanilla nuclear escalation is disabled while a campaign is active.</summary>
    [HarmonyPatch(typeof(FactionHQ), "CheckEscalation")]
    internal static class CheckEscalationPatch
    {
        private static bool Prefix()
        {
            try
            {
                CampaignController controller = CampaignController.Instance;
                return controller == null || !controller.Active;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("CheckEscalationPatch failed: " + e);
                return true;
            }
        }
    }
}
