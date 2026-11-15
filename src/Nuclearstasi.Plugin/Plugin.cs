using BepInEx;
using BepInEx.Logging;
using Nuclearstasi.Core;

namespace Nuclearstasi.Plugin
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.nuclearstasi.mod";
        public const string Name = "Nuclearstasi";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            Settings settings = PluginConfig.Load(Config);
            Log.LogInfo($"{Name} {Version} loaded (mission prefix '{settings.Campaign.MissionPrefix}')");
        }
    }
}
