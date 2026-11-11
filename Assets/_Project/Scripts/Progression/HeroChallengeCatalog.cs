using System;
using System.Collections.Generic;
using LiarsBatting.Core;

namespace LiarsBatting.Progression
{
    // Five mastery challenges per hero: two warm-ups (play, win) and three built around that
    // hero's ability. They only count in matches played AS that hero. "(누적)" ones add up across
    // matches; the others must happen within one match.
    public static class HeroChallengeCatalog
    {
        public const int PerHero = 5;

        private static readonly Dictionary<HeroId, List<ChallengeDef>> ByHero = Build();

        public static List<ChallengeDef> For(HeroId hero) => ByHero[hero];

        private static ChallengeDef C(HeroId hero, string key, string text, int target, Func<MatchStats, int> progress)
            => new ChallengeDef($"{hero}_{key}", text, target, progress);

        private static int B(bool value) => value ? 1 : 0;

        // "로" after a vowel or ㄹ, "으로" after any other final consonant (백회복으로, 허언수로).
        private static string Ro(string word)
        {
            char last = word[word.Length - 1];
            if (last < 0xAC00 || last > 0xD7A3) return "(으)로";
            int jong = (last - 0xAC00) % 28;
            return jong == 0 || jong == 8 ? "로" : "으로";
        }

        private static Dictionary<HeroId, List<ChallengeDef>> Build()
        {
            var map = new Dictionary<HeroId, List<ChallengeDef>>();
            foreach (var info in HeroCatalog.All)
            {
                var h = info.Id;
                var list = new List<ChallengeDef>
                {
                    C(h, "play3", $"{info.Name}{Ro(info.Name)} 3판 플레이 (누적)", 3, s => 1),
                    C(h, "win3", $"{info.Name}{Ro(info.Name)} 3승 (누적)", 3, s => B(s.Won)),
                };
                list.AddRange(AbilityChallenges(h));
                map[h] = list;
            }
            return map;
        }

        private static IEnumerable<ChallengeDef> AbilityChallenges(HeroId h)
        {
            switch (h)
            {
                case HeroId.Hunter:
                    yield return C(h, "hide", "LIE TOKEN으로 진짜 4스트라이크를 숨기고 그 판 승리", 1, s => B(s.Won && s.HidTrueWin));
                    yield return C(h, "believed10", "내 거짓말을 상대가 믿게 하기 (누적)", 10, s => s.LiesBelieved);
                    yield return C(h, "perfect", "LIE TOKEN 2개를 다 쓰고 한 번도 안 들킨 채 승리", 1,
                        s => B(s.Won && s.LiesTold >= 2 && s.LiesCaught == 0));
                    break;
                case HeroId.Paladin:
                    yield return C(h, "heal5", "신념의 회복 사용 (누적)", 5, s => s.AbilityUses);
                    yield return C(h, "afterheal", "회복한 뒤 한 거짓말을 상대가 믿게 하기", 1, s => B(s.LiesBelievedAfterHeal > 0));
                    yield return C(h, "lies3", "한 판에 거짓말 3번 하고 승리", 1, s => B(s.Won && s.LiesTold >= 3));
                    break;
                case HeroId.Rogue:
                    yield return C(h, "catch3", "진실 간파로 거짓말 잡아내기 (누적)", 3, s => s.RogueCatches);
                    yield return C(h, "doubt10", "의심으로 거짓말 잡아내기 (누적)", 10, s => s.ChallengesCorrect);
                    yield return C(h, "flawless", "의심을 한 번도 틀리지 않고 승리 (1번 이상 의심)", 1,
                        s => B(s.Won && s.Challenges >= 1 && s.ChallengesWrong == 0));
                    break;
                case HeroId.Priest:
                    yield return C(h, "query6", "한 자리 심문 사용 (누적)", 6, s => s.AbilityUses);
                    yield return C(h, "yes3", "심문에서 '맞습니다' 듣기 (누적)", 3, s => s.PriestYes);
                    yield return C(h, "twice", "한 판에 심문 2번 쓰고 승리", 1, s => B(s.Won && s.AbilityUses >= 2));
                    break;
                case HeroId.DemonHunter:
                    yield return C(h, "pure", "상대 숫자 공개 없이 추리만으로 승리", 1,
                        s => B(s.Won && s.WonByGuess && s.OpponentDigitsRevealed == 0));
                    yield return C(h, "fast", "내 추측 6번 이내로 맞혀서 승리", 1, s => B(s.Won && s.WonByGuess && s.Guesses <= 6));
                    yield return C(h, "sealed", "내 숫자를 하나도 공개하지 않고 승리", 1, s => B(s.Won && s.MyDigitsRevealed == 0));
                    break;
                case HeroId.Warrior:
                    yield return C(h, "rush5", "연속 돌격 사용 (누적)", 5, s => s.AbilityUses);
                    yield return C(h, "extrawin", "연속 돌격으로 얻은 추가 턴에 맞혀서 승리", 1, s => B(s.WonOnExtraTurn));
                    yield return C(h, "rushwin3", "연속 돌격을 쓴 판에서 승리 (누적)", 3, s => B(s.Won && s.AbilityUses > 0));
                    break;
                case HeroId.Wizard:
                    yield return C(h, "eye5", "천리안 사용 (누적)", 5, s => s.AbilityUses);
                    yield return C(h, "eyewin3", "천리안을 쓴 판에서 승리 (누적)", 3, s => B(s.Won && s.AbilityUses > 0));
                    yield return C(h, "quick", "천리안을 쓴 뒤 내 추측 3번 이내로 맞혀서 승리", 1,
                        s => B(s.Won && s.WonByGuess && s.GuessesAtWizard >= 0 && s.Guesses - s.GuessesAtWizard <= 3));
                    break;
            }
        }
    }
}
