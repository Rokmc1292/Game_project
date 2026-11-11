using System.Collections.Generic;

namespace LiarsBatting.Progression
{
    // Everything about level, challenges and equipped cosmetics. Saved as ONE JSON string field
    // ("progress") on players/{nickname}; the level and the equipped title are also copied to
    // their own fields so other players can read them cheaply.
    // Unlocks aren't stored: a reward is unlocked exactly when Level >= its level.
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

        public bool IsUnlocked(RewardInfo reward) => reward != null && Level >= reward.Level;

        public string EquippedId(RewardKind kind)
        {
            switch (kind)
            {
                case RewardKind.Title: return Title;
                case RewardKind.Frame: return Frame;
                case RewardKind.Background: return Background;
                default: return CardSkin;
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
            }
        }

        public int Progress(Dictionary<string, int> map, string id) => map.TryGetValue(id, out int v) ? v : 0;
    }
}
