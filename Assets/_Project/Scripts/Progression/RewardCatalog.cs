using System;
using System.Collections.Generic;

namespace LiarsBatting.Progression
{
    public enum RewardKind { Title, Frame, Background, CardSkin }

    // One unlockable cosmetic. Level 1 = available from the start (the defaults).
    // Tint is a hex colour ("RRGGBB") used by the placeholder look until real art exists:
    // a background/card tint, or a portrait-frame colour. Art dropped into
    // Resources/Cosmetics/<Id> later can replace the placeholder.
    public class RewardInfo
    {
        public string Id;
        public RewardKind Kind;
        public string Name;
        public int Level;
        public string Tint;

        public RewardInfo(string id, RewardKind kind, string name, int level, string tint = null)
        {
            Id = id; Kind = kind; Name = name; Level = level; Tint = tint;
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

        public static RewardInfo Find(string id) => Array.Find(Player, r => r.Id == id);

        public static List<RewardInfo> OfKind(RewardKind kind) => new List<RewardInfo>(Array.FindAll(Player, r => r.Kind == kind));

        // Rewards reached by going from levelBefore to levelAfter (both inclusive on the new side).
        public static List<RewardInfo> UnlockedBetween(int levelBefore, int levelAfter) =>
            new List<RewardInfo>(Array.FindAll(Player, r => r.Level > levelBefore && r.Level <= levelAfter));

        // The equipped title's text, or "" when none is equipped.
        public static string TitleText(string titleId)
        {
            var r = Find(titleId);
            return r == null || r.Id == NoTitle ? "" : r.Name;
        }
    }
}
