using System;
using Mai2DRPC.Configuration;
using Mai2DRPC.Game;
using MelonLoader;

namespace Mai2DRPC.Discord
{
    public sealed class DiscordPresence : IDisposable
    {
        private readonly DiscordIpcClient? client;
        private readonly MelonLogger.Instance logger;
        private readonly string achievementMode;
        private readonly bool showButton;
        private readonly bool verbose;
        private string? lastState;
        private string? lastGameName;

        public double UpdateInterval { get; }

        public DiscordPresence(ModConfig config, MelonLogger.Instance logger)
        {
            this.logger = logger;
            UpdateInterval = config.UpdateInterval;
            achievementMode = config.Achievement;
            showButton = config.ShowButton;
            verbose = config.Verbose;

            if (!config.Enabled)
            {
                logger.Msg("Discord Rich Presence is disabled.");
                return;
            }
            if (!long.TryParse(config.ClientId, out _))
            {
                logger.Warning("Set Mai2DRPC.ClientId in MelonPreferences.cfg.");
                return;
            }

            try
            {
                client = new DiscordIpcClient(config.ClientId, logger, verbose);
                client.OnReady += () =>
                {
                    lastState = null;
                    logger.Msg("Discord Ready.");
                };
                client.OnError += msg => logger.Error($"Discord error: {msg}");
                client.Start();
                logger.Msg($"Discord Rich Presence started (clientId={config.ClientId}).");
            }
            catch (Exception exception)
            {
                logger.Warning($"Discord Rich Presence could not initialize: {exception.Message}");
            }
        }

        public void Update(GameState state)
        {
            if (state.GameName != lastGameName)
            {
                if (verbose)
                    logger.Msg($"Game version: {state.GameName}");
                lastGameName = state.GameName;
            }

            if (
                client == null
                || !client.IsReady
                || state.Screen == GameScreen.Unknown
                || state.ToString() == lastState
            )
                return;

            try
            {
                (string details, string status) = build(state);
                string? difficulty =
                    state.Screen == GameScreen.Playing ? formatDifficulty(state.Difficulty) : null;
                client.SetActivity(
                    truncate(details),
                    truncate(status),
                    getVersionAsset(state.GameName),
                    state.GameName,
                    difficulty?.ToLowerInvariant().Replace(":", "").Replace("-", ""),
                    difficulty,
                    showButton ? "mai2drpc" : null,
                    showButton ? "https://github.com/michioxd/mai2drpc" : null
                );
                lastState = state.ToString();
                if (verbose)
                    logger.Msg($"Game state: {state.Screen}");
                if (verbose && !string.IsNullOrWhiteSpace(state.Title))
                    logger.Msg($"Song: {state.Artist} - {state.Title}");
                if (verbose && !string.IsNullOrWhiteSpace(state.Difficulty))
                    logger.Msg($"Difficulty: {state.Difficulty}");
            }
            catch (Exception exception)
            {
                logger.Warning($"Discord presence update failed: {exception.Message}");
            }
        }

        private (string details, string status) build(GameState state)
        {
            string details =
                state.Screen == GameScreen.Playing
                    ? joinSong(state.Artist, state.Title, state.GameName)
                    : state.GameName ?? "maimai でらっくす";
            string status;
            switch (state.Screen)
            {
                case GameScreen.Startup:
                    status = "Starting up...";
                    break;
                case GameScreen.Title:
                    status = "Title Screen";
                    break;
                case GameScreen.Entry:
                    status = "Entry";
                    break;
                case GameScreen.ModeSelect:
                    status = "Mode Select";
                    break;
                case GameScreen.SongSelect:
                    status = "Selecting Songs";
                    break;
                case GameScreen.DanCourseSelect:
                    status = "Dan Course";
                    break;
                case GameScreen.Playing:
                    status = formatDifficulty(state.Difficulty) ?? "Playing";
                    if (!string.IsNullOrWhiteSpace(state.Level))
                        status += $" {state.Level}";
                    decimal? achievement =
                        achievementMode == "+" ? state.AchievementPlus
                        : achievementMode == "-" ? state.AchievementMinus
                        : null;
                    if (achievement.HasValue)
                        status += $" {achievement.Value:F4}%";
                    break;
                case GameScreen.Result:
                    status = "Result";
                    break;
                case GameScreen.Memorial:
                    status = "Memorial";
                    break;
                case GameScreen.Loading:
                    status = "Loading...";
                    break;
                default:
                    status = "Idle";
                    break;
            }
            return (details, status);
        }

        private static string? formatDifficulty(string? difficulty)
        {
            if (string.IsNullOrWhiteSpace(difficulty))
                return null;
            string normalized = difficulty!.Replace(":", "").Replace("-", "").ToUpperInvariant();
            return normalized == "REMASTER" ? "Re:MASTER"
                : normalized == "UTAGE" ? "U-TA-GE"
                : normalized;
        }

        private static string getVersionAsset(string gameName)
        {
            if (gameName.EndsWith("MAGiCAL", StringComparison.Ordinal))
                return "70";
            if (gameName.EndsWith("CiRCLE PLUS", StringComparison.Ordinal))
                return "65";
            if (gameName.EndsWith("CiRCLE", StringComparison.Ordinal))
                return "60";
            if (gameName.EndsWith("PRiSM PLUS", StringComparison.Ordinal))
                return "55";
            if (gameName.EndsWith("PRiSM", StringComparison.Ordinal))
                return "50";
            if (gameName.EndsWith("BUDDiES PLUS", StringComparison.Ordinal))
                return "45";
            if (gameName.EndsWith("BUDDiES", StringComparison.Ordinal))
                return "40";
            return "default";
        }

        private static string joinSong(string? artist, string? title, string? gameName)
        {
            if (string.IsNullOrWhiteSpace(artist))
                return title ?? gameName ?? "";
            if (string.IsNullOrWhiteSpace(title))
                return artist!;
            return $"{artist} - {title}";
        }

        private static string truncate(string value) =>
            value.Length <= 128 ? value : value.Substring(0, 128);

        public void Dispose()
        {
            try
            {
                client?.Dispose();
            }
            catch (Exception ex)
            {
                logger.Warning($"Discord shutdown failed: {ex.Message}");
            }
        }
    }
}
