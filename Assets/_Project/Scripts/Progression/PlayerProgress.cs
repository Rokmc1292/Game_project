using System.Collections.Generic;
using LiarsBatting.Core;

namespace LiarsBatting.Progression
{
    // One hero's mastery: level, challenge progress and the portrait look equipped for it.
    public class HeroProgress
    {
        public int Level = 1;
        public int Xp;
        public Dictionary<string, int> Challenges = new Dictionary<string, int>();
        public List<string> Done = new List<string>();
        public string Skin = "";      // reward id; "" = the default portrait
        public bool Effect;           // animated portrait on/off
    }

    // Everything about level, challenges, mastery and equipped cosmetics. Saved as ONE JSON string
    // field ("progress") on players/{nickname}; the level and the equipped title are also copied
    // to their own fields so other players can read them cheaply.
    // Unlocks aren't stored: a reward is unlocked exactly when the relevant level reaches it.
    public class PlayerProgress
    {
        public int Level = 1;
        public int Xp;   // XP inside the current level

        public int DailyPeriod = -1;
        public Dictionary<string, int> Daily = new Dictionary<string, int>();
        public List<string> DailyDone = new List<string>();

        public int WeeklyPeriod = -1;
        public Dictionary<string, int> Weekly = new Dictionary<string, int>();
        public List<string> WeeklyDone = new List<string>();
        public List<string> WeeklyWinHeroes = new List<string>();

        public string Title = RewardCatalog.NoTitle;
        public string Frame = RewardCatalog.NoFrame;
        public string Background = RewardCatalog.DefaultBackground;
        public string CardSkin = RewardCatalog.DefaultCards;

        public Dictionary<string, HeroProgress> Heroes = new Dictionary<string, HeroProgress>();

        public HeroProgress Hero(HeroId hero)
        {
            string key = hero.ToString();
            if (!Heroes.TryGetValue(key, out var progress))
            {
                progress = new HeroProgress();
                Heroes[key] = progress;
            }
            return progress;
        }

        public int HeroLevel(HeroId hero) => Heroes.TryGetValue(hero.ToString(), out var h) ? h.Level : 1;

        public string HeroSkinId(HeroId hero)
        {
            string skin = Heroes.TryGetValue(hero.ToString(), out var h) ? h.Skin : "";
            return string.IsNullOrEmpty(skin) ? RewardCatalog.SkinId(hero, 0) : skin;
        }

        public bool HeroEffectOn(HeroId hero) => Heroes.TryGetValue(hero.ToString(), out var h) && h.Effect;

        public bool IsUnlocked(RewardInfo reward)
        {
            if (reward == null) return false;
            return reward.Hero.HasValue ? HeroLevel(reward.Hero.Value) >= reward.Level : Level >= reward.Level;
        }

        public bool IsEquipped(RewardInfo reward)
        {
            switch (reward.Kind)
            {
                case RewardKind.Title: return Title == reward.Id;
                case RewardKind.Frame: return Frame == reward.Id;
                case RewardKind.Background: return Background == reward.Id;
                case RewardKind.CardSkin: return CardSkin == reward.Id;
                case RewardKind.HeroSkin: return HeroSkinId(reward.Hero.Value) == reward.Id;
                default: return HeroEffectOn(reward.Hero.Value) == (reward.Id == RewardCatalog.EffectId(reward.Hero.Value, true));
            }
        }

        // The reward currently occupying the same slot as `reward` (for undoing a failed save).
        public RewardInfo EquippedInSameSlot(RewardInfo reward)
        {
            switch (reward.Kind)
            {
                case RewardKind.Title: return RewardCatalog.Find(Title);
                case RewardKind.Frame: return RewardCatalog.Find(Frame);
                case RewardKind.Background: return RewardCatalog.Find(Background);
                case RewardKind.CardSkin: return RewardCatalog.Find(CardSkin);
                case RewardKind.HeroSkin: return RewardCatalog.Find(HeroSkinId(reward.Hero.Value));
                default: return RewardCatalog.Find(RewardCatalog.EffectId(reward.Hero.Value, HeroEffectOn(reward.Hero.Value)));
            }
        }

        public void Equip(RewardInfo reward)
        {
            switch (reward.Kind)
            {
                case RewardKind.Title: Title = reward.Id; break;
                case RewardKind.Frame: Frame = reward.Id; break;
                case RewardKind.Background: Background = reward.Id; break;
                case RewardKind.CardSkin: CardSkin = reward.Id; break;
                case RewardKind.HeroSkin: Hero(reward.Hero.Value).Skin = reward.Id; break;
                case RewardKind.HeroEffect:
                    Hero(reward.Hero.Value).Effect = reward.Id == RewardCatalog.EffectId(reward.Hero.Value, true);
                    break;
            }
        }

        public int Progress(Dictionary<string, int> map, string id) => map.TryGetValue(id, out int v) ? v : 0;
    }
}
