using System;
using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // Victory / defeat popup: a heavy black-leather ledger panel with a blind-embossed double border,
    // a muted foil title ("승 리" in aged brass, "패 배" in dull red), a one-line verdict, the revealed
    // secret as cards, and a single button back to the main menu. No glow, no particles on purpose.
    // Art lives in Resources/Result: result_panel (9-sliced), result_title_win, result_title_lose.
    public class ResultPopupView
    {
        public readonly RectTransform Root;

        private readonly Image _title;
        private readonly Text _message;
        private readonly RevealedDigitsRow _digits;
        private readonly Sprite _winTitle;
        private readonly Sprite _loseTitle;

        public ResultPopupView(Transform parent, Action onMainMenu)
        {
            _winTitle = Resources.Load<Sprite>("Result/result_title_win");
            _loseTitle = Resources.Load<Sprite>("Result/result_title_lose");

            // full-screen dim that also blocks clicks behind the popup
            var rootGo = new GameObject("ResultPopup", typeof(RectTransform), typeof(Image));
            Root = (RectTransform)rootGo.transform;
            Root.SetParent(parent, false);
            UiFactory.StretchToFillParent(Root);
            rootGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);

            var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var panel = (RectTransform)panelGo.transform;
            panel.SetParent(Root, false);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(640f, 400f);
            var panelImage = panelGo.GetComponent<Image>();
            var panelSprite = Resources.Load<Sprite>("Result/result_panel");
            if (panelSprite != null)
            {
                panelImage.sprite = panelSprite;
                panelImage.type = Image.Type.Sliced;
                panelImage.color = Color.white;
            }
            else
            {
                Debug.LogWarning("ResultPopupView: Resources/Result/result_panel 을 찾을 수 없음");
                panelImage.color = UITheme.Bg;
            }

            var content = UiFactory.VerticalGroup(panel, "Content", spacing: 8,
                padding: new RectOffset(70, 70, 44, 62), childAlign: TextAnchor.UpperCenter);
            UiFactory.StretchToFillParent(content);

            var titleGo = new GameObject("Title", typeof(RectTransform), typeof(Image));
            titleGo.transform.SetParent(content, false);
            UiFactory.SetSize(titleGo.transform, 210, 90);
            _title = titleGo.GetComponent<Image>();
            _title.preserveAspect = true;
            _title.raycastTarget = false;

            _message = UiFactory.Text(content, "", 22, new Color32(190, 182, 166, 255), TextAnchor.MiddleCenter, FontStyle.Bold);
            _message.raycastTarget = false;
            UiFactory.SetHeight(_message, 30);
            var shadow = _message.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(1.5f, -2f);

            _digits = new RevealedDigitsRow(content, 58f, 81f, 12f);
            UiFactory.SetFlexible(_digits.Root, 1, 0);

            var buttonRow = UiFactory.HorizontalGroup(content, "ButtonRow", spacing: 0, childAlign: TextAnchor.MiddleCenter);
            UiFactory.SetHeight(buttonRow, 52);
            UiFactory.SetFlexible(buttonRow, 1, 0);
            var button = UiFactory.Button(buttonRow, "메인 메뉴로", UITheme.Accent, Color.white,
                () => onMainMenu?.Invoke(), 17);
            UiFactory.SetSize(button, 220, 48);
            MenuButtonStyle.ApplyChoice(button, UITheme.Accent);

            Root.gameObject.SetActive(false);
        }

        // digits: the secret to show as cards (null = no cards, e.g. the opponent's secret isn't known online).
        public void Show(bool won, string message, int[] digits)
        {
            _title.sprite = won ? _winTitle : _loseTitle;
            _title.color = _title.sprite != null ? Color.white : Color.clear;
            _message.text = message;

            bool hasDigits = digits != null && digits.Length == 4;
            _digits.Root.gameObject.SetActive(hasDigits);
            if (hasDigits) _digits.ShowDigits(digits);

            Root.gameObject.SetActive(true);
            Root.SetAsLastSibling();
        }
    }
}
