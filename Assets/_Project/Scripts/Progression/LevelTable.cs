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
        public static int MatchXp(bool online, bool won)
        {
            if (online) return won ? 120 : 60;
            return won ? 60 : 30;
        }

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
        public static int HeroMatchXp(bool online, bool won)
        {
            if (online) return won ? 100 : 40;
            return won ? 50 : 20;
        }

        public const int HeroChallengeXp = 150;
    }
}
