using System;
using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // A generic modal: a message plus a row of buttons. Used for every "stop and
    // decide" moment that sits between the two main panels -- trusting or
    // challenging a reported result, picking which of your own digits to open,
    // or just acknowledging what just happened.
    public class ChoiceOverlayView
    {
        private readonly GameObject _root;
        private readonly Text _message;
        private readonly RectTransform _buttonRow;
        private readonly RectTransform _box;
        private readonly Text _countdown;
        private readonly RectTransform _content;
        private readonly Button _backButton;     // top-left "뒤로가기", only for popups that ask for it
        private RectTransform _historyRow;       // the 나의 기록 / 상대 기록 buttons, top-right
        private bool _hideHistoryNext;
        private Action _pendingBack;
        private Action _backAction;
        private bool _playNextClickSound = true;

        public ChoiceOverlayView(Transform parent)
        {
            _root = new GameObject("ChoiceOverlay", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)_root.transform;
            rt.SetParent(parent, false);
            UiFactory.StretchToFillParent(rt);
            _root.GetComponent<Image>().color = new Color(0, 0, 0, 0.75f);

            var box = UiFactory.VerticalGroup(rt, "Box", spacing: 18,
                padding: new RectOffset(36, 36, 32, 32), childAlign: TextAnchor.MiddleCenter);
            box.anchorMin = new Vector2(0.12f, 0.3f);
            box.anchorMax = new Vector2(0.88f, 0.7f);
            box.offsetMin = Vector2.zero;
            box.offsetMax = Vector2.zero;
            var boxImage = box.gameObject.AddComponent<Image>();
            boxImage.color = UITheme.Bg;
            MenuButtonStyle.ApplyPanel(boxImage);

            _box = box;
            _message = UiFactory.Text(box, "", 16, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            // Optional visuals between the message and the buttons (result lamps, the lamp picker).
            _content = UiFactory.VerticalGroup(box, "Content", spacing: 8, childAlign: TextAnchor.MiddleCenter);
            _content.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
            UiFactory.SetFlexible(_content, 1, 0);
            _buttonRow = UiFactory.HorizontalGroup(box, "Buttons", spacing: 10, childAlign: TextAnchor.MiddleCenter);
            UiFactory.SetHeight(_buttonRow, 52);
            // Without this the row (a HorizontalGroup, flexible by default) soaks up all the spare
            // height of the tall message box and stretches every button into a tall block.
            UiFactory.SetFlexible(_buttonRow, 1, 0);

            // The HUD hourglass sits behind this popup's dim backdrop, so a timed decision shows its
            // remaining seconds here (top-left of the box; the history buttons use the top-right).
            _countdown = UiFactory.Text(box, "", 24, UITheme.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            _countdown.raycastTarget = false;
            var countdownLayout = _countdown.GetComponent<LayoutElement>();
            if (countdownLayout == null) countdownLayout = _countdown.gameObject.AddComponent<LayoutElement>();
            countdownLayout.ignoreLayout = true;   // not part of the box's vertical layout
            var countdownRt = (RectTransform)_countdown.transform;
            countdownRt.anchorMin = new Vector2(0, 1);
            countdownRt.anchorMax = new Vector2(0, 1);
            countdownRt.pivot = new Vector2(0, 1);
            countdownRt.anchoredPosition = new Vector2(20, -12);
            countdownRt.sizeDelta = new Vector2(220, 34);
            _countdown.gameObject.SetActive(false);

            _backButton = UiFactory.Button(box, "◀ 뒤로가기", UITheme.Accent, Color.white, OnBackClicked, 14);
            _backButton.GetComponent<LayoutElement>().ignoreLayout = true;   // Button() already added a LayoutElement   // not part of the box's vertical layout
            var backRt = (RectTransform)_backButton.transform;
            backRt.anchorMin = new Vector2(0, 1);
            backRt.anchorMax = new Vector2(0, 1);
            backRt.pivot = new Vector2(0, 1);
            backRt.anchoredPosition = new Vector2(16, -12);
            backRt.sizeDelta = new Vector2(130, 34);
            MenuButtonStyle.ApplyChoice(_backButton, UITheme.Accent);
            _backButton.gameObject.SetActive(false);

            _root.SetActive(false);
        }

        // Applies to the NEXT Show only: hide the history buttons (a popup that has nothing to do with
        // the match so far, like the AI difficulty choice) and/or put a 뒤로가기 button in the top-left.
        public void ConfigureNextShow(bool hideHistory, Action onBack = null)
        {
            _hideHistoryNext = hideHistory;
            _pendingBack = onBack;
        }

        public void SuppressNextClickSound() => _playNextClickSound = false;

        private void OnBackClicked()
        {
            _root.SetActive(false);
            var back = _backAction;
            _backAction = null;
            back?.Invoke();
        }

        // Called every frame by the shared countdown while a timed decision is running.
        public void SetCountdown(float remaining)
        {
            _countdown.gameObject.SetActive(true);
            _countdown.text = $"남은 시간 {Mathf.CeilToInt(remaining)}초";
            _countdown.color = remaining <= 5f ? UITheme.Clay : new Color32(214, 160, 70, 255);
        }

        public void HideCountdown() => _countdown.gameObject.SetActive(false);

        // Buttons close the overlay themselves, but a timed decision that runs out
        // resolves without any click -- its timeout fallback must close it explicitly,
        // or the stale prompt stays on screen (still clickable) after the turn moves on.
        public void Hide() => _root.SetActive(false);

        // Two small buttons in the box's top-right corner: hovering one shows the matching guess history
        // while the player decides (the history buttons on the HUD sit behind this popup's backdrop).
        public void SetHistorySources(HistoryHoverView mine, HistoryHoverView opponent, MonoBehaviour host)
        {
            var row = UiFactory.HorizontalGroup(_box, "HistoryPeeks", spacing: 8, childAlign: TextAnchor.MiddleRight);
            row.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;   // not part of the box's vertical layout
            row.anchorMin = new Vector2(1, 1);
            row.anchorMax = new Vector2(1, 1);
            row.pivot = new Vector2(1, 1);
            row.anchoredPosition = new Vector2(-16, -12);
            row.sizeDelta = new Vector2(220, 32);
            _historyRow = row;

            var mineButton = new HistoryPeekButton(row, "나의 기록", "나의 추측 기록 (상대 응답)", mine, host);
            UiFactory.SetSize(mineButton.Root, 104, 32);
            var opponentButton = new HistoryPeekButton(row, "상대 기록", "상대의 추측 기록 (내 판정)", opponent, host);
            UiFactory.SetSize(opponentButton.Root, 104, 32);
        }

        public void Show(string message, params (string label, Action onClick)[] options)
            => Show(message, UITheme.Accent, options);

        // buttonColor lets a step tint every option the same way (e.g. strike-
        // yellow for a "how many strikes" step) so the choice reads at a glance.
        public void Show(string message, Color buttonColor, params (string label, Action onClick)[] options)
        {
            var mixed = new (string, Color, Action)[options.Length];
            for (int i = 0; i < options.Length; i++)
                mixed[i] = (options[i].label, buttonColor, options[i].onClick);
            ShowMixed(message, mixed);
        }

        // For the rarer case where options need DIFFERENT colors in one prompt
        // (e.g. a neutral "trust" button next to a clay-colored "challenge" one).
        public Button[] ShowMixed(string message, params (string label, Color color, Action onClick)[] options)
            => ShowWithContent(message, null, options);

        // buildContent fills a slot between the message and the buttons (e.g. result lamps).
        // Returns the buttons in option order, so a caller can enable/disable them later.
        public Button[] ShowWithContent(string message, Action<RectTransform> buildContent,
            params (string label, Color color, Action onClick)[] options)
        {
            bool playClickSound = _playNextClickSound;
            _playNextClickSound = true;
            if (_historyRow != null) _historyRow.gameObject.SetActive(!_hideHistoryNext);
            _hideHistoryNext = false;
            _backAction = _pendingBack;
            _pendingBack = null;
            _backButton.gameObject.SetActive(_backAction != null);

            _message.text = message;
            _message.gameObject.SetActive(!string.IsNullOrEmpty(message));
            for (int i = _buttonRow.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_buttonRow.GetChild(i).gameObject);
            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                var child = _content.GetChild(i).gameObject;
                child.SetActive(false);   // Destroy is end-of-frame; hide it so it can't take layout space now
                UnityEngine.Object.Destroy(child);
            }
            buildContent?.Invoke(_content);
            _content.gameObject.SetActive(buildContent != null);
            // Lamps need more room than a message + buttons, so the box grows taller while they're shown.
            _box.anchorMin = new Vector2(0.12f, buildContent != null ? 0.2f : 0.3f);
            _box.anchorMax = new Vector2(0.88f, buildContent != null ? 0.8f : 0.7f);

            var buttons = new Button[options.Length];
            for (int o = 0; o < options.Length; o++)
            {
                var option = options[o];
                var onClick = option.onClick;
                var button = UiFactory.Button(_buttonRow, option.label, option.color, Color.white, () =>
                {
                    _root.SetActive(false);
                    onClick?.Invoke();
                }, 15, playClickSound);
                UiFactory.SetSize(button, Mathf.Max(130f, option.label.Length * 11f + 64f), 48f);
                MenuButtonStyle.ApplyChoice(button, option.color);
                buttons[o] = button;
            }
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            return buttons;
        }
    }
}
