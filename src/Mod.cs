using System;
using HarmonyLib;
using Mai2DRPC.Configuration;
using Mai2DRPC.Discord;
using Mai2DRPC.Game;
using MelonLoader;

[assembly: MelonInfo(typeof(Mai2DRPC.Mod), "Mai2DRPC", "1.0.0", "michioxd", null)]
[assembly: MelonGame("sega-interactive", "Sinmai")]

namespace Mai2DRPC
{
    public sealed class Mod : MelonMod
    {
        private GameStateReader? stateReader;
        private DiscordPresence? presence;
        private DateTime nextUpdate;

        public override void OnInitializeMelon()
        {
            ModConfig config = ModConfig.Load(LoggerInstance);
            HarmonyInstance.PatchAll();
            stateReader = new GameStateReader();
            presence = new DiscordPresence(config, LoggerInstance);
            nextUpdate = DateTime.UtcNow;
            LoggerInstance.Msg("Initialized.");
        }

        public override void OnUpdate()
        {
            if (stateReader == null || presence == null || DateTime.UtcNow < nextUpdate)
                return;

            nextUpdate = DateTime.UtcNow.AddSeconds(presence.UpdateInterval);
            try
            {
                presence.Update(stateReader.GetCurrentState());
            }
            catch (Exception exception)
            {
                LoggerInstance.Warning($"Could not read game state: {exception.Message}");
            }
        }

        public override void OnDeinitializeMelon()
        {
            presence?.Dispose();
        }
    }
}
