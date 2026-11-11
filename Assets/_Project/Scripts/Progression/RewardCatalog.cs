using System;
using System.Collections.Generic;
using LiarsBatting.Core;

namespace LiarsBatting.Progression
{
    public enum RewardKind { Title, Frame, Background, CardSkin, HeroSkin, HeroEffect }

    // One unlockable cosmetic. Level 1 = available from the start (the defaults).
    // Hero == null: unlocked by the player level. Hero set: unlocked by that hero's mastery level.
    // Tint is a hex colour ("RRGGBB") used by the placeholder look until real art exists:
    // a background/card/portrait tint, or a portrait-frame colour. A sprite at
    // Resources/Cosmetics/<Id> replaces the placeholder where the game supports it (portrait skins).
    public class RewardInfo
    {
        public string Id;
        public RewardKind Kind;
        public string Name;
        public int Level;
        public string Tint;
        public HeroId? Hero;

        public RewardInfo(string id, RewardKind kind, string name, int level, string tint = null, HeroId? hero = null)
        {
            Id = id; Kind = kind; Name = name; Level = level; Tint = tint; Hero = hero;
        }
    }

    public static class RewardCatalog
    {
        public const string NoTitle = "title_none";
        public const string NoFrame = "frame_none";
        public const string DefaultBackground = "bg_default";
        public const string DefaultCards = "card_default";

        // Player-level rewards, in level order. Edit names/levels/tints here.
        public static readonly RewardInfo[] Player =
        {
            new RewardInfo(NoTitle, RewardKind.Title, "칭호 없음", 1),
            new RewardInfo(NoFrame, RewardKind.Frame, "기본 테두리", 1, "D6A046"),
            new RewardInfo(DefaultBackground, RewardKind.Background, "기본 지하실", 1, "FFFFFF"),
            new RewardInfo(DefaultCards, RewardKind.CardSkin, "기본 카드", 1, "FFFFFF"),

            new RewardInfo("title_rookie", RewardKind.Title, "초보 거짓말쟁이", 2),
            new RewardInfo("frame_bronze", RewardKind.Frame, "청동 테두리", 3, "B07A45"),
            new RewardInfo("bg_crimson", RewardKind.Background, "진홍 도박장", 5, "FFB4AA"),
            new RewardInfo("card_ebony", RewardKind.CardSkin, "흑단 카드", 7, "B9B2C4"),
            new RewardInfo("title_pokerface", RewardKind.Title, "포커페이스", 10),
            new RewardInfo("frame_silver", RewardKind.Frame, "은 테두리", 10, "D3D7DE"),
            new RewardInfo("bg_teal", RewardKind.Background, "심야 청록", 15, "A6E0DC"),
            new RewardInfo("card_gold", RewardKind.CardSkin, "황금 카드", 20, "FFE08A"),
            new RewardInfo("title_tazza", RewardKind.Title, "타짜", 20),
            new RewardInfo("frame_gold", RewardKind.Frame, "금 테두리", 25, "F2C94C"),
            new RewardInfo("title_legend", RewardKind.Title, "전설의 허풍선이", 30),
            new RewardInfo("bg_vip", RewardKind.Background, "황금 VIP룸", 30, "FFE2A8"),
        };

        // ---- hero mastery rewards (same shape for every hero) ----
        public const int HeroTitleLevel = 3;
        public const int HeroSkin1Level = 5;
        public const int HeroEffectLevel = 7;
        public const int HeroMasterLevel = 10;

        // (Lv.3 title, Lv.10 master title) per hero. Edit names here.
        private static readonly Dictionary<HeroId, (string first, string master)> HeroTitles =
            new Dictionary<HeroId, (string, string)>
            {
                { HeroId.Hunter, ("사냥꾼의 혀", "거짓말 사냥의 대가") },
                { HeroId.Paladin, ("꺾이지 않는 신념", "불굴의 성기사") },
                { HeroId.Rogue, ("진실을 보는 눈", "거짓의 천적") },
                { HeroId.Priest, ("집요한 심문관", "한 자리의 달인") },
                { HeroId.DemonHunter, ("봉인자", "사슬의 지배자") },
                { HeroId.Warrior, ("멈추지 않는 돌격", "연속 돌격의 화신") },
                { HeroId.Wizard, ("꿰뚫는 시선", "천리안의 현자") },
            };

        public static string SkinId(HeroId hero, int n) => $"{hero}_skin{n}";
        public static string EffectId(HeroId hero, bool on) => $"{hero}_fx{(on ? 1 : 0)}";

        public static readonly RewardInfo[] Hero = BuildHeroRewards();

        private static RewardInfo[] BuildHeroRewards()
        {
            var list = new List<RewardInfo>();
            foreach (var info in HeroCatalog.All)
            {
                var h = info.Id;
                var titles = HeroTitles[h];
                list.Add(new RewardInfo(SkinId(h, 0), RewardKind.HeroSkin, "기본 초상화", 1, "FFFFFF", h));
                list.Add(new RewardInfo(EffectId(h, false), RewardKind.HeroEffect, "효과 없음", 1, null, h));
                list.Add(new RewardInfo($"{h}_title1", RewardKind.Title, titles.first, HeroTitleLevel, null, h));
                list.Add(new RewardInfo(SkinId(h, 1), RewardKind.HeroSkin, "세피아 초상화", HeroSkin1Level, "E8C9A0", h));
                list.Add(new RewardInfo(EffectId(h, true), RewardKind.HeroEffect, "움직이는 초상화", HeroEffectLevel, null, h));
                list.Add(new RewardInfo($"{h}_title2", RewardKind.Title, titles.master, HeroMasterLevel, null, h));
                list.Add(new RewardInfo(SkinId(h, 2), RewardKind.HeroSkin, "황금 초상화", HeroMasterLevel, "FFE27A", h));
            }
            return list.ToArray();
        }

        public static RewardInfo Find(string id) =>
            Array.Find(Player, r => r.Id == id) ?? Array.Find(Hero, r => r.Id == id);

        // Player-level rewards of one kind (the 꾸미기 tab).
        public static List<RewardInfo> OfKind(RewardKind kind) => new List<RewardInfo>(Array.FindAll(Player, r => r.Kind == kind));

        // One hero's rewards of one kind (the 영웅 숙련도 tab).
        public static List<RewardInfo> ForHero(HeroId hero, RewardKind kind) =>
            new List<RewardInfo>(Array.FindAll(Hero, r => r.Hero == hero && r.Kind == kind));

        public static List<RewardInfo> ForHero(HeroId hero) => new List<RewardInfo>(Array.FindAll(Hero, r => r.Hero == hero));

        // Player rewards reached by going from levelBefore to levelAfter.
        public static List<RewardInfo> UnlockedBetween(int levelBefore, int levelAfter) =>
            new List<RewardInfo>(Array.FindAll(Player, r => r.Level > levelBefore && r.Level <= levelAfter));

        public static List<RewardInfo> HeroUnlockedBetween(HeroId hero, int levelBefore, int levelAfter) =>
            new List<RewardInfo>(Array.FindAll(Hero, r => r.Hero == hero && r.Level > levelBefore && r.Level <= levelAfter));

        // The equipped title's text, or "" when none is equipped.
        public static string TitleText(string titleId)
        {
            var r = Find(titleId);
            return r == null || r.Id == NoTitle ? "" : r.Name;
        }
    }
}
