namespace LiarsBatting.Progression
{
    // Every experience number in one place, so balancing is a matter of editing this file.
    public static class LevelTable
    {
        // ---- player level ----
        public const int MaxPlayerLevel = 30;

        // XP needed to go from `level` to `level + 1`: 200, 250, 300, ...
        public static int PlayerXpToNext(int level) => 200 + 50 * (level - 1);

        // A finished match. AI matches give half of what a match against a person gives.
        // A draw sits between a win and a loss.
        // Against the AI a WIN pays more the harder the AI (aiTier 0 = 초급, 1 = 중수, 2 = 고수).
        public static int MatchXp(bool online, bool won, bool draw = false, int aiTier = 0)
        {
            if (draw) return online ? 90 : 45;
            if (online) return won ? 120 : 60;
            if (!won) return 30;
            return AiWinXp[System.Math.Max(0, System.Math.Min(aiTier, AiWinXp.Length - 1))];
        }

        private static readonly int[] AiWinXp = { 60, 90, 120 };
        private static readonly int[] AiWinHeroXp = { 50, 75, 100 };

        // Surrendering before this many seconds of match time earns nothing (no XP, no challenge
        // progress) -- otherwise quitting instantly would be the fastest way to farm.
        public const int MinSurrenderSecondsForXp = 180;

        public const int DailyChallengeXp = 100;
        public const int WeeklyChallengeXp = 300;

        // ---- hero mastery ----
        public const int MaxHeroLevel = 10;

        // 100, 200, 300, ... (4,500 in total to reach Lv.10)
        public static int HeroXpToNext(int level) => 100 * level;

        // A finished match played as that hero (AI matches give half).
        public static int HeroMatchXp(bool online, bool won, bool draw = false, int aiTier = 0)
        {
            if (draw) return online ? 70 : 35;
            if (online) return won ? 100 : 40;
            if (!won) return 20;
            return AiWinHeroXp[System.Math.Max(0, System.Math.Min(aiTier, AiWinHeroXp.Length - 1))];
        }

        public const int HeroChallengeXp = 150;
    }
}
