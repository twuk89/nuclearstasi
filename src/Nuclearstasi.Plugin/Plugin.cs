using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Nuclearstasi.Core;
using Nuclearstasi.Plugin.Campaign;
using UnityEngine;

namespace Nuclearstasi.Plugin
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.nuclearstasi.mod";
        public const string Name = "Nuclearstasi";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        private Harmony harmony;

        private void Awake()
        {
            Log = Logger;
            Settings settings = PluginConfig.Load(Config);
            string saveDir = Path.Combine(Paths.ConfigPath, "Nuclearstasi");
            CampaignController.Instance = new CampaignController(settings, new CampaignStore(saveDir), Logger);
            ApplyPatches();
            Log.LogInfo($"{Name} {Version} loaded (mission prefix '{settings.Campaign.MissionPrefix}', saves in {saveDir})");
        }

        private void Update()
        {
            CampaignController.Instance?.Tick(Time.deltaTime);
        }

        private void ApplyPatches()
        {
            harmony = new Harmony(Guid);
            foreach (Type type in typeof(Plugin).Assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0)
                {
                    continue;
                }
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    Log.LogInfo("Patched " + type.Name);
                }
                catch (Exception e)
                {
                    Log.LogError($"Patch {type.Name} failed (game update?): {e.Message}");
                }
            }
        }
    }
}
