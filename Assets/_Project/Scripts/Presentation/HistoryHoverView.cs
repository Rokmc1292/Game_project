using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LiarsBatting.Core;

namespace LiarsBatting.Presentation
{
    // A compact guess log: shows only the most recent few rows in a small box. Hovering
    // the box pops up the complete, scrollable history next to it. Exposes the same
    // AddRow / Clear calls the old always-open ScrollingHistoryColumn had.
    public class HistoryHoverView
    {
        public readonly RectTransform Root;

        private struct Record
        {
            public int[] Guess;
            public JudgeResult Reported;
            public bool WasLie;
            public bool RevealLie;
        }

        private readonly List<Record> _records = new List<Record>();
        private readonly RectTransform _recent;
        private readonly ScrollingHistoryColumn _full;
        private readonly GameObject _popup;
        private readonly int _recentCount;
        private bool _hovered;

        public HistoryHoverView(Transform parent, string label, MonoBehaviour host, int recentCount = 2)
        {
            _recentCount = recentCount;

            Root = UiFactory.Panel(parent, "HistoryBox", UITheme.Surface);
            var col = UiFactory.VerticalGroup(Root, "Col", spacing: 4, padding: new RectOffset(10, 10, 6, 6));
            UiFactory.StretchToFillParent(col);
            UiFactory.Text(col, label, 12, UITheme.Muted, TextAnchor.UpperLeft, FontStyle.Bold);
            _recent = UiFactory.VerticalGroup(col, "Recent", spacing: 2);

            // Popup: a child of the box so hovering the popup itself keeps it open. It gets its
            // own sorting override (and raycaster) so it always draws above the rest of the HUD.
            _popup = new GameObject("FullHistoryPopup", typeof(RectTransform), typeof(Image));
            var popupRt = (RectTransform)_popup.transform;
            popupRt.SetParent(Root, false);
            popupRt.anchorMin = new Vector2(0, 1);
            popupRt.anchorMax = new Vector2(0, 1);
            popupRt.pivot = new Vector2(1, 1);
            popupRt.anchoredPosition = new Vector2(-8, 0);
            popupRt.sizeDelta = new Vector2(330, 280);
            _popup.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.045f, 0.97f);
            var canvas = _popup.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 30;
            _popup.AddComponent<GraphicRaycaster>();

            _full = new ScrollingHistoryColumn(popupRt, "전체 기록 — " + label, host);
            UiFactory.StretchToFillParent(_full.Root);
            _full.Root.offsetMin = new Vector2(8, 8);
            _full.Root.offsetMax = new Vector2(-8, -8);
            _full.HoverOverride = () => _hovered;

            var hover = Root.gameObject.AddComponent<HoverTrigger>();
            hover.OnEnter = () =>
            {
                _hovered = true;
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

        // revealLie: false hides whether this row was a lie (used for what the OPPONENT told me).
        public void AddRow(int[] guess, JudgeResult reported, bool wasLie, bool revealLie = true)
        {
            _full.AddRow(guess, reported, wasLie, revealLie);
            _records.Add(new Record { Guess = guess, Reported = reported, WasLie = wasLie, RevealLie = revealLie });
            RefreshRecent();
        }

        public void Clear()
        {
            _full.Clear();
            _records.Clear();
            RefreshRecent();
        }

        private void RefreshRecent()
        {
            for (int i = _recent.childCount - 1; i >= 0; i--)
            {
                var child = _recent.GetChild(i).gameObject;
                child.SetActive(false);          // leave the layout immediately; Destroy is end-of-frame
                Object.Destroy(child);
            }
            int start = Mathf.Max(0, _records.Count - _recentCount);
            for (int i = start; i < _records.Count; i++)
            {
                var r = _records[i];
                ScrollingHistoryColumn.BuildRow(_recent, r.Guess, r.Reported, r.WasLie, r.RevealLie);
            }
        }
    }
}
