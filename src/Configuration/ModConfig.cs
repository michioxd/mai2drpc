using System;
using MelonLoader;

namespace Mai2DRPC.Configuration
{
    public sealed class ModConfig
    {
        public string ClientId { get; private set; } = "1550789168869933066";
        public bool Enabled { get; private set; } = true;
        public double UpdateInterval { get; private set; } = 1.0;
        public string Achievement { get; private set; } = "-";

        public static ModConfig Load(MelonLogger.Instance logger)
        {
            try
            {
                MelonPreferences_Category category = MelonPreferences.CreateCategory("Mai2DRPC");
                var clientId = category.CreateEntry(
                    "ClientId",
                    "1550789168869933066",
                    "Discord application client ID"
                );
                var enabled = category.CreateEntry("Enabled", true, "Enable Discord presence");
                var updateInterval = category.CreateEntry(
                    "UpdateInterval",
                    1.0,
                    "Presence update interval in seconds"
                );
                var achievement = category.CreateEntry(
                    "Achievement",
                    "-",
                    "+ counts up, - counts down, false hides achievement"
                );
                MelonPreferences.Save();
                return new ModConfig
                {
                    ClientId = clientId.Value,
                    Enabled = enabled.Value,
                    UpdateInterval = Math.Max(0.25, updateInterval.Value),
                    Achievement = achievement.Value,
                };
            }
            catch (Exception exception)
            {
                logger.Error($"Could not load preferences; using defaults: {exception.Message}");
                return new ModConfig();
            }
        }
    }
}
