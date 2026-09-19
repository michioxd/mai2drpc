using MAI2.Util;
using MAI2System;
using Manager;
using Manager.MaiStudio;
using Monitor.TakeOver;
using Process;

namespace Mai2DRPC.Game
{
    public sealed class GameStateReader
    {
        public GameState GetCurrentState()
        {
            ProcessBase? process = ProcessTracker.Current;
            GameScreen screen = GameManager.IsInGame
                ? GameScreen.Playing
                : ProcessTracker.ToScreen(process);

            if (screen == GameScreen.Playing)
                return readPlaying();
            if (screen == GameScreen.SongSelect && process is MusicSelectProcess musicSelect)
                return readSelection(musicSelect);
            return new GameState(screen, gameName: getGameName());
        }

        private static GameState readSelection(MusicSelectProcess process)
        {
            MusicData? music = process.GetMusic(0)?.MusicData;
            MusicDifficultyID difficulty =
                process.CurrentDifficulty != null && process.CurrentDifficulty.Length > 0
                    ? process.CurrentDifficulty[0]
                    : MusicDifficultyID.Invalid;
            return fromMusic(GameScreen.SongSelect, music, difficulty);
        }

        private static GameState readPlaying()
        {
            GameScoreList? score = Singleton<GamePlayManager>.Instance.GetGameScore(0);
            int musicId = score?.SessionInfo.musicId ?? GameManager.SelectMusicID[0];
            MusicDifficultyID difficulty = (MusicDifficultyID)(
                score?.SessionInfo.difficulty ?? GameManager.SelectDifficultyID[0]
            );
            MusicData? music = Singleton<DataManager>.Instance.GetMusic(musicId);
            return fromMusic(
                GameScreen.Playing,
                music,
                difficulty,
                score?.GetAchivement(),
                score?.GetDecTheoryAchivement()
            );
        }

        private static GameState fromMusic(
            GameScreen screen,
            MusicData? music,
            MusicDifficultyID difficulty,
            decimal? achievementPlus = null,
            decimal? achievementMinus = null
        )
        {
            string? difficultyName = difficulty.IsValid() ? difficulty.GetName() : null;
            int notesIndex = difficulty == MusicDifficultyID.Strong ? 0 : (int)difficulty;
            Notes? notes =
                music != null && notesIndex >= 0 && notesIndex < music.notesData.Count
                    ? music.notesData[notesIndex]
                    : null;
            string? level =
                notes == null ? null : $"{notes.level}{(notes.levelDecimal >= 7 ? "+" : "")}";
            return new GameState(
                screen,
                music?.name?.str,
                music?.artistName?.str,
                difficultyName,
                level,
                achievementPlus,
                achievementMinus,
                getGameName()
            );
        }

        private static string getGameName()
        {
            uint version = Singleton<SystemConfig>
                .Instance
                .config
                .romVersionInfo
                .versionNo
                .versionCode;
            TakeOverMonitor.MajorRomVersion brand = new TakeOverMajorVersion().GetMajorRomVersion(
                version
            );
            return brand <= TakeOverMonitor.MajorRomVersion.NONE
                ? "maimai でらっくす"
                : $"maimai でらっくす {(version >= 1070000 ? "MAGiCAL" : brand.ToString().Replace("_PLUS", " PLUS"))}";
        }
    }
}
