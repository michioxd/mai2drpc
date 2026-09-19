using HarmonyLib;
using Manager;
using Process;

namespace Mai2DRPC.Game
{
    internal static class ProcessTracker
    {
        public static ProcessBase? Current { get; private set; }
        public static bool Verbose { get; set; }

        public static void Added(ProcessBase process)
        {
            if (ToScreen(process) != GameScreen.Unknown)
                Current = process;
        }

        public static void Released(ProcessBase process)
        {
            if (ReferenceEquals(Current, process))
                Current = null;
        }

        public static GameScreen ToScreen(ProcessBase? process)
        {
            if (process is GameProcess)
                return GameScreen.Playing;
            if (process is ResultProcess)
                return GameScreen.Result;
            if (process is PhotoEditProcess)
                return GameScreen.Memorial;
            if (process is Process.CourseSelect.CourseSelectProcess)
                return GameScreen.DanCourseSelect;
            if (process is MusicSelectProcess)
                return GameScreen.SongSelect;
            if (process is Process.ModeSelect.ModeSelectProcess)
                return GameScreen.ModeSelect;
            if (process is EntryProcess)
                return GameScreen.Entry;
            if (process is AdvertiseProcess)
                return GameScreen.Title;
            if (process is FadeProcess || process is BlackoutFadeProcess)
                return GameScreen.Loading;
            if (
                process is PowerOnProcess
                || process is WarningProcess
                || process is StartupProcess
                || process is PleaseWaitProcess
                || process is DataSaveProcess
                || process is Process.LoginBonus.LoginBonusProcess
            )
                return GameScreen.Startup;
            return GameScreen.Unknown;
        }
    }

    [HarmonyPatch(typeof(ProcessManager), nameof(ProcessManager.AddProcess))]
    internal static class AddProcessPatch
    {
        [HarmonyPostfix]
        private static void postfix(ProcessBase? process, uint __result)
        {
            if (ProcessTracker.Verbose)
                MelonLoader.MelonLogger.Msg(
                    $"[ProcessTracker] Added process: {process?.GetType().Name} (Result={__result})"
                );
            if (__result != 0 && process != null)
                ProcessTracker.Added(process);
        }
    }

    [HarmonyPatch(typeof(ProcessManager), nameof(ProcessManager.ReleaseProcess))]
    internal static class ReleaseProcessPatch
    {
        [HarmonyPostfix]
        private static void postfix(ProcessBase? process)
        {
            if (ProcessTracker.Verbose)
                MelonLoader.MelonLogger.Msg(
                    $"[ProcessTracker] Released process: {process?.GetType().Name}"
                );
            if (process != null)
                ProcessTracker.Released(process);
        }
    }
}
