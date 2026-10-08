using System;
using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // The 0-9 number-card tray used both for picking your secret at setup and for building
    // a guess during your attack turn. Clicking a tray card COPIES its digit into the first
    // empty slot (the source card just greys out while in use); clicking a filled slot
    // DELETES it and frees the tray card again -- it can never produce a duplicate digit
    // because a used tray card can't be clicked.
    //
    // Two layouts: the classic stacked one (setup screen) and a split one for the match
    // HUD where the 4 slots sit in the middle of the screen and the tray + submit button sit
    // in the right column.
    public class CardPickerView
    {
        public readonly RectTransform Root;

        private readonly Button[] _trayButtons = new Button[10];
        private readonly Image[] _trayImages = new Image[10];
        private readonly bool[] _used = new bool[10];
        private readonly bool[] _digitDisabled = new bool[10]; // DemonHunter's 0~8 restriction

        private readonly RectTransform[] _slots = new RectTransform[4];
        private readonly Image[] _slotImages = new Image[4];
        private readonly int[] _slotValues = { -1, -1, -1, -1 };

        private readonly Button _submitButton;
        private readonly Action<int[]> _onSubmit;

        private static readonly Color TrayUsedTint = new Color(0.30f, 0.30f, 0.30f, 1f);
        private static readonly Color TrayDisabledTint = new Color(0.20f, 0.20f, 0.20f, 1f);

        // Classic stacked layout (secret-pick screen): centred, bigger 5:7 cards.
        public CardPickerView(Transform parent, string title, string submitLabel, Action<int[]> onSubmit)
        {
            _onSubmit = onSubmit;
            var slotCard = new Vector2(64f, 90f);
            var trayCard = new Vector2(56f, 78f);
            Root = UiFactory.VerticalGroup(parent, "CardPicker", spacing: 14, childAlign: TextAnchor.UpperCenter);

            UiFactory.Text(Root, title, 15, UITheme.Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
            var slotsRow = BuildSlots(Root, slotCard, 14f);
            UiFactory.SetHeight(slotsRow, slotCard.y);
            UiFactory.SetFlexible(slotsRow, 1, 0);   // otherwise the row soaks up spare height and the slots turn into tall bars

            UiFactory.Text(Root, "카드를 클릭해 복사, 채워진 슬롯을 클릭해 삭제합니다.", 12, UITheme.Muted, TextAnchor.MiddleCenter);
            BuildTray(Root, trayCard, 10f);

            var submitRow = UiFactory.HorizontalGroup(Root, "SubmitRow", spacing: 0, childAlign: TextAnchor.MiddleCenter);
            UiFactory.SetHeight(submitRow, 52);
            UiFactory.SetFlexible(submitRow, 1, 0);
            _submitButton = UiFactory.Button(submitRow, submitLabel, UITheme.Accent, Color.white, Submit, 17);
            UiFactory.SetSize(_submitButton, 300, 52);
            MenuButtonStyle.ApplyChoice(_submitButton, UITheme.Accent);
            RefreshSubmitInteractable();
        }

        // Split layout for the match HUD: slots go into slotsParent, tray + submit into trayParent.
        public CardPickerView(Transform slotsParent, Transform trayParent, string submitLabel,
            Action<int[]> onSubmit, Vector2 slotCard, Vector2 trayCard)
        {
            _onSubmit = onSubmit;

            var slotsRow = BuildSlots(slotsParent, slotCard, 16f);
            UiFactory.StretchToFillParent(slotsRow);

            Root = UiFactory.VerticalGroup(trayParent, "TrayColumn", spacing: 8, childAlign: TextAnchor.UpperCenter);
            UiFactory.StretchToFillParent(Root);
            var hint = UiFactory.Text(Root, "카드를 클릭해 복사, 채워진 슬롯을 클릭해 삭제", 12, UITheme.Muted, TextAnchor.MiddleLeft);
            hint.raycastTarget = false;
            UiFactory.SetHeight(hint, 16);
            BuildTray(Root, trayCard, 8f);

            _submitButton = UiFactory.Button(Root, submitLabel, UITheme.Accent, Color.white, Submit, 16);
            UiFactory.SetHeight(_submitButton, 40);
            MenuButtonStyle.ApplyChoice(_submitButton, UITheme.Accent);
            RefreshSubmitInteractable();
        }

        private RectTransform BuildSlots(Transform parent, Vector2 card, float spacing)
        {
            var row = UiFactory.HorizontalGroup(parent, "Slots", spacing: spacing, childAlign: TextAnchor.MiddleCenter);
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var slotGo = new GameObject($"Slot{i}", typeof(RectTransform), typeof(Image), typeof(Button));
                var slotRt = (RectTransform)slotGo.transform;
                slotRt.SetParent(row, false);
                UiFactory.SetSize(slotRt, card.x, card.y);
                var img = slotGo.GetComponent<Image>();
                img.color = UITheme.Surface2;
                img.preserveAspect = true;
                var btn = slotGo.GetComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => ClearSlot(index));

                _slots[i] = slotRt;
                _slotImages[i] = img;
            }
            return row;
        }

        private RectTransform BuildTray(Transform parent, Vector2 card, float spacing)
        {
            var tray = UiFactory.Grid(parent, "Tray", columns: 5, cellSize: card.x, spacing: spacing);
            var grid = tray.GetComponent<GridLayoutGroup>();
            grid.cellSize = card;
            grid.childAlignment = TextAnchor.MiddleCenter;
            UiFactory.SetHeight(tray, card.y * 2 + spacing);

            for (int digit = 0; digit < 10; digit++)
            {
                int d = digit;
                var cardGo = new GameObject($"Card{d}", typeof(RectTransform), typeof(Image), typeof(Button));
                cardGo.transform.SetParent(tray, false);
                var img = cardGo.GetComponent<Image>();
                img.sprite = CardArt.Digit(d);
                img.color = Color.white;
                img.preserveAspect = true;
                var btn = cardGo.GetComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => PlaceDigit(d));

                _trayButtons[d] = btn;
                _trayImages[d] = img;
            }
            return tray;
        }

        private void PlaceDigit(int digit)
        {
            if (_used[digit]) return;
            int emptyIndex = Array.IndexOf(_slotValues, -1);
            if (emptyIndex < 0) return;

            _slotValues[emptyIndex] = digit;
            _slotImages[emptyIndex].sprite = CardArt.Digit(digit);
            _slotImages[emptyIndex].color = Color.white;
            _used[digit] = true;
            _trayImages[digit].color = TrayUsedTint;
            RefreshSubmitInteractable();
        }

        private void ClearSlot(int slotIndex)
        {
            int digit = _slotValues[slotIndex];
            if (digit < 0) return;

            _slotValues[slotIndex] = -1;
            _slotImages[slotIndex].sprite = null;
            _slotImages[slotIndex].color = UITheme.Surface2;
            _used[digit] = false;
            _trayImages[digit].color = _digitDisabled[digit] ? TrayDisabledTint : Color.white;
            RefreshSubmitInteractable();
        }

        private void RefreshSubmitInteractable()
        {
            bool full = Array.IndexOf(_slotValues, -1) < 0;
            _submitButton.interactable = full;
        }

        private void Submit()
        {
            if (Array.IndexOf(_slotValues, -1) >= 0) return;
            var result = new[] { _slotValues[0], _slotValues[1], _slotValues[2], _slotValues[3] };
            ResetAll();
            _onSubmit?.Invoke(result);
        }

        public void ResetAll()
        {
            for (int i = 0; i < 4; i++) ClearSlot(i);
        }

        public void SetInteractable(bool interactable, string disabledHint = null)
        {
            for (int d = 0; d < 10; d++)
                _trayButtons[d].interactable = interactable && !_digitDisabled[d];
            for (int i = 0; i < 4; i++) _slots[i].GetComponent<Button>().interactable = interactable;
            RefreshSubmitInteractable();
            if (!interactable) _submitButton.interactable = false;
        }

        // A permanently (for this game) disabled digit stays disabled even through later
        // SetInteractable(true) calls -- used for DemonHunter's "opponent's pool is 0~8 only"
        // restriction. Pass enabled:true to lift it again.
        public void SetDigitEnabled(int digit, bool enabled)
        {
            _digitDisabled[digit] = !enabled;
            _trayButtons[digit].interactable = enabled && !_used[digit];
            _trayImages[digit].color = enabled ? Color.white : TrayDisabledTint;
        }
    }
}
