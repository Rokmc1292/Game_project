using System;
using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // The "reveal one of your digits" penalty, as my four secret cards laid out in their real
    // order. Hidden cards can be picked (hover lifts them, a click selects with a brass frame);
    // cards that are already public are dimmed and marked "공개됨". onSelected gets the index.
    public static class RevealCardPicker
    {
        private const float CardWidth = 76f;
        private const float CardHeight = 106f;
        private const float Frame = 4f;

        private static readonly Color FrameIdle = new Color(0, 0, 0, 0);
        private static readonly Color FrameHover = new Color32(214, 160, 70, 110);
        private static readonly Color FrameSelected = new Color32(255, 196, 84, 255);

        public static RectTransform Build(Transform parent, int[] secret, bool[] revealed, Action<int> onSelected)
        {
            var row = UiFactory.HorizontalGroup(parent, "RevealCards", spacing: 16, childAlign: TextAnchor.MiddleCenter);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            UiFactory.SetSize(row, 4 * (CardWidth + 2 * Frame) + 3 * 16, CardHeight + 2 * Frame + 12 + 22);

            var frames = new Image[secret.Length];
            var cards = new RectTransform[secret.Length];
            int selected = -1;

            void Repaint(int hovered)
            {
                for (int i = 0; i < frames.Length; i++)
                {
                    if (revealed[i]) continue;
                    frames[i].color = i == selected ? FrameSelected : i == hovered ? FrameHover : FrameIdle;
                    // Selected/hovered cards rise a little, like being pulled from the hand.
                    float lift = i == selected ? 10f : i == hovered ? 5f : 0f;
                    cards[i].anchoredPosition = new Vector2(0, lift);
                }
            }

            for (int i = 0; i < secret.Length; i++)
            {
                int index = i;
                var column = UiFactory.VerticalGroup(row, "Card" + i, spacing: 4, childAlign: TextAnchor.UpperCenter);
                column.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
                UiFactory.SetSize(column, CardWidth + 2 * Frame, CardHeight + 2 * Frame + 12 + 22);

                // Fixed-size slot; the card inside moves freely (no layout) so it can lift.
                var slot = UiFactory.Panel(column, "Slot", Color.clear);
                UiFactory.SetSize(slot, CardWidth + 2 * Frame, CardHeight + 2 * Frame + 12);

                var card = UiFactory.Panel(slot, "Frame", FrameIdle);
                card.anchorMin = card.anchorMax = new Vector2(0.5f, 0f);
                card.pivot = new Vector2(0.5f, 0f);
                card.sizeDelta = new Vector2(CardWidth + 2 * Frame, CardHeight + 2 * Frame);
                frames[i] = card.GetComponent<Image>();
                cards[i] = card;

                var face = UiFactory.Panel(card, "Face", CardArt.Tint);
                UiFactory.StretchToFillParent(face);
                face.offsetMin = new Vector2(Frame, Frame);
                face.offsetMax = new Vector2(-Frame, -Frame);
                var faceImage = face.GetComponent<Image>();
                faceImage.sprite = CardArt.Digit(secret[i]);
                faceImage.preserveAspect = true;
                faceImage.raycastTarget = false;

                string caption = $"{i + 1}번째";
                Color captionColor = UITheme.Muted;
                if (revealed[i])
                {
                    var tint = CardArt.Tint;
                    faceImage.color = new Color(tint.r * 0.45f, tint.g * 0.45f, tint.b * 0.45f, 0.6f);
                    frames[i].raycastTarget = false;
                    caption = "공개됨";
                    captionColor = new Color(UITheme.Muted.r, UITheme.Muted.g, UITheme.Muted.b, 0.6f);
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

                var label = UiFactory.Text(column, caption, 13, captionColor, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiFactory.SetSize(label, CardWidth + 2 * Frame, 22);
            }
            Repaint(-1);
            return row;
        }
    }
}
