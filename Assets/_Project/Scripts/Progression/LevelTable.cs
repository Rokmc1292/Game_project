namespace LiarsBatting.Progression
{
    // Every experience number in one place, so balancing is a matter of editing this file.
    public static class LevelTable
    {
        public const int MaxPlayerLevel = 30;

        // XP needed to go from `level` to `level + 1`: 200, 250, 300, ...
        public static int PlayerXpToNext(int level) => 200 + 50 * (level - 1);

        // A finished match. AI matches give half of what a match against a person gives.
        public static int MatchXp(bool online, bool won)
        {
            if (online) return won ? 120 : 60;
            return won ? 60 : 30;
        }

        public const int DailyChallengeXp = 100;
        public const int WeeklyChallengeXp = 300;
    }
}
