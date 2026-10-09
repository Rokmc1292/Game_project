using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // A small button for the corner of the choice popups (truth-or-lie, trust-or-challenge ...).
    // Hovering it shows the full guess history of the given HistoryHoverView, so the log can be
    // read while deciding. It only reads the source's records; it never changes them.
    public class HistoryPeekButton
    {
        public readonly RectTransform Root;

        private readonly HistoryHoverView _source;
        private readonly ScrollingHistoryColumn _full;
        private readonly GameObject _popup;
        private bool _hovered;

        public HistoryPeekButton(Transform parent, string label, string popupTitle, HistoryHoverView source,
            MonoBehaviour host)
        {
            _source = source;

            var go = new GameObject("HistoryPeek", typeof(RectTransform), typeof(Image), typeof(Button));
            Root = (RectTransform)go.transform;
            Root.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = UITheme.Surface;                  // fallback if the plate sprites are missing
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;

            var text = UiFactory.Text(Root, label, 12, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            text.raycastTarget = false;
            var textRt = (RectTransform)text.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(8, 2);
            textRt.offsetMax = new Vector2(-8, -2);
            MenuButtonStyle.ApplyChoice(button, UITheme.Accent);

            // Popup below the button, right edges aligned; own sorting so it sits above the dark backdrop.
            _popup = new GameObject("HistoryPopup", typeof(RectTransform), typeof(Image));
            var popupRt = (RectTransform)_popup.transform;
            popupRt.SetParent(Root, false);
            popupRt.anchorMin = new Vector2(1, 0);
            popupRt.anchorMax = new Vector2(1, 0);
            popupRt.pivot = new Vector2(1, 1);
            popupRt.anchoredPosition = new Vector2(0, -6);
            popupRt.sizeDelta = new Vector2(360, 240);
            _popup.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.045f, 0.98f);
            var canvas = _popup.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 60;
            _popup.AddComponent<GraphicRaycaster>();

            _full = new ScrollingHistoryColumn(popupRt, popupTitle, host);
            UiFactory.StretchToFillParent(_full.Root);
            _full.Root.offsetMin = new Vector2(8, 8);
            _full.Root.offsetMax = new Vector2(-8, -8);
            _full.HoverOverride = () => _hovered;

            var hover = go.AddComponent<HoverTrigger>();
            hover.OnEnter = () =>
            {
                _hovered = true;
                Rebuild();
                _popup.SetActive(true);
                _full.ScrollToBottom();
            };
            hover.OnExit = () =>
            {
                _hovered = false;
                _popup.SetActive(false);
            };
            _popup.SetActive(false);
        }

        private void Rebuild()
        {
            _full.Clear();
            foreach (var r in _source.Records)
                _full.AddRow(r.Guess, r.Reported, r.WasLie, r.RevealLie, r.TruthConfirmed);
        }
    }
}
