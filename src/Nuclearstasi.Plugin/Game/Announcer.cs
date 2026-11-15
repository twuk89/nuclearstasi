namespace Nuclearstasi.Plugin.Game
{
    internal static class Announcer
    {
        public static void All(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }
            Plugin.Log.LogInfo("[Announce] " + message);
            MessageManager manager = NetworkSceneSingleton<MessageManager>.i;
            if (manager == null)
            {
                Plugin.Log.LogWarning("MessageManager not available; announcement not sent");
                return;
            }
            manager.RpcAllHQMessage(message);
        }
    }
}
