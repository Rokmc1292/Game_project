using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // Keeps the fixed-size HUD stage as large as fits inside its parent (letterboxed).
    internal class StageScaler : MonoBehaviour
    {
        public float Width = 1280f;
        public float Height = 664f;

        private void LateUpdate()
        {
            var parent = transform.parent as RectTransform;
            if (parent == null) return;
            float s = Mathf.Min(parent.rect.width / Width, parent.rect.height / Height);
            if (s > 0f) transform.localScale = new Vector3(s, s, 1f);
        }
    }

    // The match screen HUD. Everything is placed on a fixed 1280x664 stage (top-left origin,
    // y grows downward):
    //   top centre  : opponent's revealed digits          left   : hourglass + whose turn
    //   middle      : the digits I'm about to submit      lower-left: my LIE TOKEN coins + hero
    //   bottom      : my secret (small) + my revealed     right  : guess logs (hover = full
    //                                                              history), 0-9 tray, submit
    // Keeps the member names the rest of the game already uses (MyRevealed, OpponentRevealed,
    // MyAttackHistory, OpponentAttackHistory, ShowMySecret).
    public class MatchHudView
    {
        public const float StageW = 1280f;
        public const float StageH = 664f;

        public readonly RectTransform Stage;
        public readonly RectTransform GuessSlotsHolder;
        public readonly RectTransform TrayHolder;
        public readonly RevealedDigitsRow OpponentRevealed;
        public readonly RevealedDigitsRow MyRevealed;
        public readonly RevealedDigitsRow MySecret;
        public readonly HistoryHoverView MyAttackHistory;
        public readonly HistoryHoverView OpponentAttackHistory;
        public readonly HourglassTimerView Hourglass;
        public readonly LieTokenView LieTokens;

        private readonly Text _turnLabel;

        public MatchHudView(Transform matchRoot, MonoBehaviour host)
        {
            var stageGo = new GameObject("Stage", typeof(RectTransform), typeof(StageScaler));
            Stage = (RectTransform)stageGo.transform;
            Stage.SetParent(matchRoot, false);
            Stage.anchorMin = new Vector2(0.5f, 0.5f);
            Stage.anchorMax = new Vector2(0.5f, 0.5f);
            Stage.pivot = new Vector2(0.5f, 0.5f);
            Stage.sizeDelta = new Vector2(StageW, StageH);
            Stage.anchoredPosition = Vector2.zero;

            // right column backdrop (guess logs + tray + submit)
            var rightPanel = UiFactory.Panel(Stage, "RightPanel", UITheme.Bg);
            Place(rightPanel, 876, 136, 388, 520);

            OpponentRevealed = new RevealedDigitsRow(Stage, 76, 106, 16);
            Place(OpponentRevealed.Root, 464, 16, 352, 106);
            Label("상대의 밝혀진 숫자", 464, 124, 352, 18, 12, UITheme.Muted, TextAnchor.MiddleCenter);

            GuessSlotsHolder = NewRect("GuessSlots");
            Place(GuessSlotsHolder, 464, 256, 352, 106);
            Label("제출할 숫자", 464, 366, 352, 18, 12, UITheme.Muted, TextAnchor.MiddleCenter);

            Hourglass = new HourglassTimerView(Stage);
            Place(Hourglass.Root, 70, 96, 144, 256);
            _turnLabel = Label("", 50, 356, 184, 30, 20, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);

            LieTokens = new LieTokenView(Stage);
            Place(LieTokens.Root, 262, 430, 170, 76);

            Label("내 비밀번호", 440, 486, 116, 18, 12, UITheme.Muted, TextAnchor.MiddleRight);
            MySecret = new RevealedDigitsRow(Stage, 30, 42, 8);
            Place(MySecret.Root, 562, 474, 156, 42);
            MyRevealed = new RevealedDigitsRow(Stage, 76, 106, 16);
            Place(MyRevealed.Root, 464, 524, 352, 106);
            Label("공개된 내 숫자", 464, 632, 352, 18, 12, UITheme.Muted, TextAnchor.MiddleCenter);

            MyAttackHistory = new HistoryHoverView(Stage, "나의 추측 기록 (상대 응답)", host);
            Place(MyAttackHistory.Root, 888, 148, 364, 98);
            OpponentAttackHistory = new HistoryHoverView(Stage, "상대의 추측 기록 (내 판정)", host);
            Place(OpponentAttackHistory.Root, 888, 254, 364, 98);

            TrayHolder = NewRect("Tray");
            Place(TrayHolder, 888, 362, 364, 258);
        }

        public void PlaceOpponentHero(RectTransform hero) => Place(hero, 340, 14, 90, 142);
        public void PlaceMyHero(RectTransform hero) => Place(hero, 302, 512, 90, 142);
        public void PlacePriestButton(RectTransform button) => Place(button, 888, 624, 364, 32);

        public void ShowMySecret(int[] secret) => MySecret.ShowDigits(secret);

        // Derives "내 턴" / "상대 턴" for the label under the hourglass from the header text.
        public void SetTurnLabel(string headerText)
        {
            string t = headerText ?? "";
            string label = t.StartsWith("내 턴") ? "내 턴"
                         : (t.StartsWith("상대 턴") || t.Contains("님의 턴")) ? "상대 턴"
                         : "";
            if (_turnLabel.text != label) _turnLabel.text = label;
            _turnLabel.color = label == "내 턴" ? UITheme.Ink : UITheme.Muted;
        }

        // Top-left based placement on the stage.
        public static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private RectTransform NewRect(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(Stage, false);
            return rt;
        }

        private Text Label(string text, float x, float y, float w, float h, int size, Color color,
            TextAnchor anchor, FontStyle style = FontStyle.Normal)
        {
            var t = UiFactory.Text(Stage, text, size, color, anchor, style);
            t.raycastTarget = false;
            Place((RectTransform)t.transform, x, y, w, h);
            return t;
        }
    }
}
