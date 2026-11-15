using Nuclearstasi.Plugin.Campaign;
using NuclearOption.ModdingApi;

namespace Nuclearstasi.Plugin.Commands
{
    internal static class NsCommand
    {
        private static readonly string[] Actions = { "status", "save", "new", "aggro" };

        public static void Register()
        {
            bool registered = ChatCommands.RegisterServerCommand(
                "ns",
                "Nuclearstasi campaign: status | save | new | aggro <0-100>",
                Handle,
                new[]
                {
                    ChatCommandArgument.FromChoices("action", Actions, optional: true),
                    new ChatCommandArgument("value", ChatArgumentType.Int, optional: true),
                });
            Plugin.Log.LogInfo(registered ? "Registered /ns command" : "/ns command already registered");
        }

        private static ChatCommandResult Handle(ChatCommandRequest request)
        {
            try
            {
                return HandleCore(request);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("/ns command failed: " + e);
                return ChatCommandResult.Error(0, "Nuclearstasi error: " + e.Message);
            }
        }

        private static ChatCommandResult HandleCore(ChatCommandRequest request)
        {
            CampaignController controller = CampaignController.Instance;
            string action = request.Args.Count > 0 ? request.Args[0].ToLowerInvariant() : "status";

            if (action == "new")
            {
                if (!request.Sender.IsHost)
                {
                    return ChatCommandResult.Error(0, "Only the host can use /ns new");
                }
                controller?.ResetCampaign();
                return ChatCommandResult.Ok("Campaign save wiped. Restart the mission to begin a new campaign.");
            }

            if (controller == null || !controller.Active)
            {
                return ChatCommandResult.Ok("Nuclearstasi is not active in this mission.");
            }

            switch (action)
            {
                case "status":
                    return ChatCommandResult.Ok(controller.StatusText());
                case "save":
                    if (!request.Sender.IsHost) return ChatCommandResult.Error(0, "Only the host can use /ns save");
                    controller.SaveNow();
                    return ChatCommandResult.Ok("Campaign saved.");
                case "aggro":
                    if (!request.Sender.IsHost) return ChatCommandResult.Error(0, "Only the host can use /ns aggro");
                    if (!request.TryGetInt(1, out int value, out string error))
                    {
                        return ChatCommandResult.Error(0, error ?? "Usage: /ns aggro <0-100>");
                    }
                    controller.SetAggression(value);
                    return ChatCommandResult.Ok("Aggression set. " + controller.StatusText());
                default:
                    return ChatCommandResult.Error(0, "Usage: /ns [status|save|new|aggro <0-100>]");
            }
        }
    }
}
