using System;
using System.Collections.Generic;

namespace LiarsBatting.Progression
{
    // One challenge: a description, how much counts as done, and how much one finished match
    // adds to it. Progress == null means the service tracks it specially (distinct heroes).
    public class ChallengeDef
    {
        public string Id;
        public string Text;
        public int Target;
        public Func<MatchStats, int> Progress;

        public ChallengeDef(string id, string text, int target, Func<MatchStats, int> progress)
        {
            Id = id; Text = text; Target = target; Progress = progress;
        }
    }

    // The daily and weekly pools, and which ones are active in a given period. Everyone gets the
    // same picks on the same day: the pick is seeded by the period number, not by the player.
    // Days roll over at 00:00 Korea time, weeks on Monday 00:00 Korea time.
    public static class ChallengeCatalog
    {
        public const int DailyCount = 3;
        public const int WeeklyCount = 3;
        public const string DistinctHeroWinsId = "w_heroes3";

        // Shift "today" by this many days to test resets without touching the system clock.
        public static int DebugDayOffset = 0;

        public static readonly ChallengeDef[] DailyPool =
        {
            new ChallengeDef("d_play1", "게임 1판 완료하기", 1, s => 1),
            new ChallengeDef("d_win1", "1승 하기", 1, s => s.Won ? 1 : 0),
            new ChallengeDef("d_play2", "게임 2판 완료하기", 2, s => 1),
            new ChallengeDef("d_lie3", "거짓말 3번 하기", 3, s => s.LiesTold),
            new ChallengeDef("d_catch2", "의심으로 거짓말 2번 잡아내기", 2, s => s.ChallengesCorrect),
            new ChallengeDef("d_human1", "사람과 1판 대전하기", 1, s => s.Online ? 1 : 0),
            new ChallengeDef("d_ability3", "영웅 능력 3번 사용하기", 3, s => s.AbilityUses),
            new ChallengeDef("d_believed2", "내 거짓말을 상대가 믿게 하기 2번", 2, s => s.LiesBelieved),
        };

        public static readonly ChallengeDef[] WeeklyPool =
        {
            new ChallengeDef("w_play10", "게임 10판 완료하기", 10, s => 1),
            new ChallengeDef("w_win5", "5승 하기", 5, s => s.Won ? 1 : 0),
            new ChallengeDef("w_humanwin3", "사람 상대로 3승 하기", 3, s => s.Online && s.Won ? 1 : 0),
            new ChallengeDef(DistinctHeroWinsId, "서로 다른 영웅 3명으로 승리하기", 3, null),
            new ChallengeDef("w_catch10", "의심으로 거짓말 10번 잡아내기", 10, s => s.ChallengesCorrect),
            new ChallengeDef("w_clean1", "내 숫자를 하나도 공개하지 않고 승리하기", 1, s => s.Won && s.MyDigitsRevealed == 0 ? 1 : 0),
        };

        private static readonly TimeSpan Kst = TimeSpan.FromHours(9);
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1);

        private static DateTime KstNow(DateTime utcNow) => utcNow + Kst + TimeSpan.FromDays(DebugDayOffset);

        public static int DayNumber(DateTime utcNow) => (int)(KstNow(utcNow).Date - Epoch).TotalDays;

        // 1970-01-01 was a Thursday; +3 makes weeks start on Monday.
        public static int WeekNumber(DateTime utcNow) => (DayNumber(utcNow) + 3) / 7;

        public static TimeSpan UntilDailyReset(DateTime utcNow)
        {
            var kst = KstNow(utcNow);
            return kst.Date.AddDays(1) - kst;
        }

        public static TimeSpan UntilWeeklyReset(DateTime utcNow)
        {
            var kst = KstNow(utcNow);
            int daysSinceMonday = ((int)kst.DayOfWeek + 6) % 7;
            return kst.Date.AddDays(7 - daysSinceMonday) - kst;
        }

        public static List<ChallengeDef> DailyFor(int dayNumber) => Pick(DailyPool, DailyCount, dayNumber * 7919 + 17);

        public static List<ChallengeDef> WeeklyFor(int weekNumber) => Pick(WeeklyPool, WeeklyCount, weekNumber * 104729 + 31);

        private static List<ChallengeDef> Pick(ChallengeDef[] pool, int count, int seed)
        {
            var list = new List<ChallengeDef>(pool);
            var rng = new Random(seed);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list.GetRange(0, Math.Min(count, list.Count));
        }
    }
}
