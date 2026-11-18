using System;
using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // Clickable cards for every "pick one" step in a popup (which of my digits to reveal, which
    // position / digit the Priest asks about). Hover lifts a card, a click selects it with a
    // brass frame; disabled cards are dimmed and can't be picked. onSelected gets the index.
    public static class CardChoice
    {
        private const float Frame = 4f;

        private static readonly Color FrameIdle = new Color(0, 0, 0, 0);
        private static readonly Color FrameHover = new Color32(214, 160, 70, 110);
        private static readonly Color FrameSelected = new Color32(255, 196, 84, 255);

        // columns: cards per row (the rest wrap to further rows). captions may be null.
        public static RectTransform Build(Transform parent, Sprite[] faces, bool[] disabled, string[] captions,
            Vector2 cardSize, int columns, Action<int> onSelected)
        {
            int count = faces.Length;
            float gap = cardSize.x >= 60f ? 16f : 10f;
            float lift = Mathf.Round(cardSize.y * 0.1f);
            float captionH = captions != null ? 22f : 0f;
            float cellW = cardSize.x + 2 * Frame;
            float slotH = cardSize.y + 2 * Frame + lift;
            float cellH = slotH + (captions != null ? 4f + captionH : 0f);
            int rows = Mathf.CeilToInt(count / (float)columns);

            var grid = UiFactory.VerticalGroup(parent, "CardChoice", spacing: 4, childAlign: TextAnchor.MiddleCenter);
            grid.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
            UiFactory.SetSize(grid, columns * cellW + (columns - 1) * gap, rows * cellH + (rows - 1) * 4);

            var frames = new Image[count];
            var cards = new RectTransform[count];
            int selected = -1;

            void Repaint(int hovered)
            {
                for (int i = 0; i < count; i++)
                {
                    if (disabled != null && disabled[i]) continue;
                    frames[i].color = i == selected ? FrameSelected : i == hovered ? FrameHover : FrameIdle;
                    // Selected/hovered cards rise a little, like being pulled from the hand.
                    cards[i].anchoredPosition = new Vector2(0, i == selected ? lift : i == hovered ? lift / 2f : 0f);
                }
            }

            RectTransform row = null;
            for (int i = 0; i < count; i++)
            {
                if (i % columns == 0)
                {
                    row = UiFactory.HorizontalGroup(grid, "Row", spacing: gap, childAlign: TextAnchor.MiddleCenter);
                    row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
                    UiFactory.SetHeight(row, cellH);
                }

                int index = i;
                bool off = disabled != null && disabled[i];
                var column = UiFactory.VerticalGroup(row, "Card" + i, spacing: 4, childAlign: TextAnchor.UpperCenter);
                column.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
                UiFactory.SetSize(column, cellW, cellH);

                // Fixed-size slot; the card inside moves freely (no layout) so it can lift.
                var slot = UiFactory.Panel(column, "Slot", Color.clear);
                slot.GetComponent<Image>().raycastTarget = false;
                UiFactory.SetSize(slot, cellW, slotH);

                var card = UiFactory.Panel(slot, "Frame", FrameIdle);
                card.anchorMin = card.anchorMax = new Vector2(0.5f, 0f);
                card.pivot = new Vector2(0.5f, 0f);
                card.sizeDelta = new Vector2(cellW, cardSize.y + 2 * Frame);
                frames[i] = card.GetComponent<Image>();
                cards[i] = card;

                var face = PopupParts.CardFace(card, faces[i]);
                face.offsetMin = new Vector2(Frame, Frame);
                face.offsetMax = new Vector2(-Frame, -Frame);
                var faceImage = face.GetComponent<Image>();

                if (off)
                {
                    var tint = CardArt.Tint;
                    faceImage.color = new Color(tint.r * 0.45f, tint.g * 0.45f, tint.b * 0.45f, 0.6f);
                    frames[i].raycastTarget = false;
                }
                else
                {
                    var button = card.gameObject.AddComponent<Button>();
                    button.targetGraphic = frames[i];
                    button.transition = Selectable.Transition.None;   // frame + lift are the feedback
                    button.onClick.AddListener(() =>
                    {
                        selected = index;
                        Repaint(-1);
                        GameAudio.Play("SFX_UI_Click", volume: 0.6f);
                        onSelected?.Invoke(index);
                    });
                    var hover = card.gameObject.AddComponent<HoverTrigger>();
                    hover.OnEnter = () => Repaint(index);
                    hover.OnExit = () => Repaint(-1);
                }

                if (captions != null)
                {
                    var c = UITheme.Muted;
                    var label = UiFactory.Text(column, captions[i], 13, off ? new Color(c.r, c.g, c.b, 0.6f) : c,
                        TextAnchor.MiddleCenter, FontStyle.Bold);
                    UiFactory.SetSize(label, cellW, captionH);
                }
            }
            Repaint(-1);
            return grid;
        }
    }

    // The "reveal one of your digits" penalty: my four secret cards in their real order, the
    // already-public ones dimmed and marked "공개됨".
    public static class RevealCardPicker
    {
        public static RectTransform Build(Transform parent, int[] secret, bool[] revealed, Action<int> onSelected)
        {
            var faces = new Sprite[secret.Length];
            var captions = new string[secret.Length];
            for (int i = 0; i < secret.Length; i++)
            {
                faces[i] = CardArt.Digit(secret[i]);
                captions[i] = revealed[i] ? "공개됨" : $"{i + 1}번째";
            }
            return CardChoice.Build(parent, faces, revealed, captions, PopupParts.BigCard, 4, onSelected);
        }
    }
}
