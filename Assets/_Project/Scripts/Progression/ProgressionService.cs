using System;
using System.Collections.Generic;
using System.Text;

namespace LiarsBatting.Progression
{
    // What one finished match earned, for the result popup.
    public class MatchSettlement
    {
        public int XpGained;
        public int LevelBefore;
        public int LevelAfter;
        public readonly List<string> Completed = new List<string>();
        public readonly List<RewardInfo> NewRewards = new List<RewardInfo>();

        public string ToSummary()
        {
            var sb = new StringBuilder($"+{XpGained} XP");
            if (LevelAfter > LevelBefore) sb.Append($"  ·  레벨 업! Lv.{LevelAfter}");
            if (Completed.Count > 0) sb.Append("\n완료: ").Append(string.Join(", ", Completed));
            if (NewRewards.Count > 0)
            {
                var names = new List<string>();
                foreach (var r in NewRewards) names.Add(r.Name);
                sb.Append("\n새 보상: ").Append(string.Join(", ", names));
            }
            return sb.ToString();
        }
    }

    // The rules that turn a finished match into XP, levels and challenge progress. Pure logic on
    // PlayerProgress; ProfileStore does the loading and saving around it.
    public static class ProgressionService
    {
        // Starts a fresh daily/weekly set when the period has changed since the last save.
        public static void RollPeriods(PlayerProgress p, DateTime utcNow)
        {
            int day = ChallengeCatalog.DayNumber(utcNow);
            if (p.DailyPeriod != day)
            {
                p.DailyPeriod = day;
                p.Daily.Clear();
                p.DailyDone.Clear();
            }

            int week = ChallengeCatalog.WeekNumber(utcNow);
            if (p.WeeklyPeriod != week)
            {
                p.WeeklyPeriod = week;
                p.Weekly.Clear();
                p.WeeklyDone.Clear();
                p.WeeklyWinHeroes.Clear();
            }
        }

        public static MatchSettlement ApplyMatch(PlayerProgress p, MatchStats s, DateTime utcNow)
        {
            RollPeriods(p, utcNow);
            var result = new MatchSettlement { LevelBefore = p.Level };
            int xp = LevelTable.MatchXp(s.Online, s.Won);

            foreach (var def in ChallengeCatalog.DailyFor(p.DailyPeriod))
                if (Advance(p.Daily, p.DailyDone, def, def.Progress(s)))
                {
                    xp += LevelTable.DailyChallengeXp;
                    result.Completed.Add(def.Text);
                }

            if (s.Won)
            {
                string hero = s.Hero.ToString();
                if (!p.WeeklyWinHeroes.Contains(hero)) p.WeeklyWinHeroes.Add(hero);
            }
            foreach (var def in ChallengeCatalog.WeeklyFor(p.WeeklyPeriod))
            {
                bool done;
                if (def.Id == ChallengeCatalog.DistinctHeroWinsId)
                {
                    int had = p.Progress(p.Weekly, def.Id);
                    done = Advance(p.Weekly, p.WeeklyDone, def, p.WeeklyWinHeroes.Count - had);
                }
                else
                {
                    done = Advance(p.Weekly, p.WeeklyDone, def, def.Progress(s));
                }
                if (done)
                {
                    xp += LevelTable.WeeklyChallengeXp;
                    result.Completed.Add(def.Text);
                }
            }

            AddXp(p, xp);
            result.XpGained = xp;
            result.LevelAfter = p.Level;
            result.NewRewards.AddRange(RewardCatalog.UnlockedBetween(result.LevelBefore, result.LevelAfter));
            return result;
        }

        // Adds to a challenge; true only on the match that completes it.
        private static bool Advance(Dictionary<string, int> map, List<string> done, ChallengeDef def, int amount)
        {
            if (done.Contains(def.Id) || amount <= 0) return false;
            map.TryGetValue(def.Id, out int current);
            current = Math.Min(def.Target, current + amount);
            map[def.Id] = current;
            if (current < def.Target) return false;
            done.Add(def.Id);
            return true;
        }

        public static void AddXp(PlayerProgress p, int amount)
        {
            p.Xp += amount;
            while (p.Level < LevelTable.MaxPlayerLevel && p.Xp >= LevelTable.PlayerXpToNext(p.Level))
            {
                p.Xp -= LevelTable.PlayerXpToNext(p.Level);
                p.Level++;
            }
            if (p.Level >= LevelTable.MaxPlayerLevel) p.Xp = 0;
        }
    }
}
