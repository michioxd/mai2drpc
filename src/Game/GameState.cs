namespace Mai2DRPC.Game
{
    public sealed class GameState
    {
        public GameScreen Screen { get; }
        public string? Title { get; }
        public string? Artist { get; }
        public string? Difficulty { get; }
        public string? Level { get; }
        public decimal? AchievementPlus { get; }
        public decimal? AchievementMinus { get; }
        public string GameName { get; }

        public GameState(
            GameScreen screen,
            string? title = null,
            string? artist = null,
            string? difficulty = null,
            string? level = null,
            decimal? achievementPlus = null,
            decimal? achievementMinus = null,
            string gameName = "maimai でらっくす"
        )
        {
            Screen = screen;
            Title = title;
            Artist = artist;
            Difficulty = difficulty;
            Level = level;
            AchievementPlus = achievementPlus;
            AchievementMinus = achievementMinus;
            GameName = gameName;
        }

        public override string ToString() =>
            $"{Screen}|{Artist}|{Title}|{Difficulty}|{Level}|{AchievementPlus}|{AchievementMinus}|{GameName}";
    }
}
