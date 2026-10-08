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

            _message = UiFactory.Text(box, "", 16, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            _buttonRow = UiFactory.HorizontalGroup(box, "Buttons", spacing: 10, childAlign: TextAnchor.MiddleCenter);
            UiFactory.SetHeight(_buttonRow, 52);
            // Without this the row (a HorizontalGroup, flexible by default) soaks up all the spare
            // height of the tall message box and stretches every button into a tall block.
            UiFactory.SetFlexible(_buttonRow, 1, 0);

            _root.SetActive(false);
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
        public void ShowMixed(string message, params (string label, Color color, Action onClick)[] options)
        {
            _message.text = message;
            for (int i = _buttonRow.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_buttonRow.GetChild(i).gameObject);

            foreach (var option in options)
            {
                var onClick = option.onClick;
                var button = UiFactory.Button(_buttonRow, option.label, option.color, Color.white, () =>
                {
                    _root.SetActive(false);
                    onClick?.Invoke();
                }, 15);
                UiFactory.SetSize(button, Mathf.Max(130f, option.label.Length * 11f + 64f), 48f);
                MenuButtonStyle.ApplyChoice(button, option.color);
            }
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
        }
    }
}
