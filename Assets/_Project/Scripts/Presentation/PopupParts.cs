using UnityEngine;
using UnityEngine.UI;
using LiarsBatting.Core;

namespace LiarsBatting.Presentation
{
    // Shared pieces for the content slot of the choice popups, so every popup (normal turns and
    // hero abilities alike) speaks the same visual language: a brass ability heading, cards for
    // digits, result lamps (ResultLamps), and verdicts as a big coloured word.
    public static class PopupParts
    {
        public static readonly Vector2 BigCard = new Vector2(76, 106);
        public static readonly Vector2 MediumCard = new Vector2(60, 84);
        public static readonly Vector2 SmallCard = new Vector2(44, 62);

        private static readonly Color Brass = new Color32(214, 160, 70, 255);

        // "◆ 한 자리 심문" -- names the hero ability a popup belongs to.
        public static Text AbilityHeading(Transform slot, HeroId hero)
        {
            var info = HeroCatalog.Get(hero);
            string name = info != null ? $"{info.Name} · {info.AbilityName}" : hero.ToString();
            var text = UiFactory.Text(slot, name, 18, Brass, TextAnchor.MiddleCenter, FontStyle.Bold);
            text.raycastTarget = false;
            UiFactory.SetSize(text, 420, 26);
            Shade(text);
            return text;
        }

        public static Text Description(Transform slot, string body, float width = 460f)
        {
            var text = UiFactory.Text(slot, body, 14, UITheme.Muted, TextAnchor.MiddleCenter);
            text.raycastTarget = false;
            int lines = body.Split('\n').Length;
            UiFactory.SetSize(text, width, 20f * lines + 4f);
            return text;
        }

        // A verdict ("진실" / "거짓" / "맞습니다" / "아닙니다") in the history colours.
        public static Text Verdict(Transform slot, string word, bool positive, int size = 30)
        {
            var text = UiFactory.Text(slot, word, size, positive ? UITheme.TruthInk : UITheme.LieInk,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            text.raycastTarget = false;
            UiFactory.SetSize(text, 320, size + 10);
            Shade(text);
            return text;
        }

        // Non-clickable cards side by side, each with an optional caption underneath.
        public static RectTransform Cards(Transform slot, Sprite[] faces, string[] captions, Vector2 size)
        {
            var row = UiFactory.HorizontalGroup(slot, "Cards", spacing: 18, childAlign: TextAnchor.MiddleCenter);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            float captionH = captions != null ? 20f : 0f;
            UiFactory.SetSize(row, faces.Length * (size.x + 40f) + (faces.Length - 1) * 18f, size.y + captionH + 4f);

            for (int i = 0; i < faces.Length; i++)
            {
                var column = UiFactory.VerticalGroup(row, "Card" + i, spacing: 4, childAlign: TextAnchor.UpperCenter);
                column.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
                UiFactory.SetSize(column, size.x + 40f, size.y + captionH + 4f);

                var holder = UiFactory.Panel(column, "Holder", Color.clear);
                holder.GetComponent<Image>().raycastTarget = false;
                UiFactory.SetSize(holder, size.x, size.y);
                CardFace(holder, faces[i]);

                if (captions != null)
                {
                    var label = UiFactory.Text(column, captions[i], 13, UITheme.Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
                    label.raycastTarget = false;
                    UiFactory.SetSize(label, size.x + 40f, captionH);
                }
            }
            return row;
        }

        // A card image filling its parent, tinted with the equipped card skin.
        public static RectTransform CardFace(Transform parent, Sprite sprite)
        {
            var face = UiFactory.Panel(parent, "Face", CardArt.Tint);
            UiFactory.StretchToFillParent(face);
            var image = face.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return face;
        }

        // The one-line status under a picker ("공개할 카드를 고르세요." ...).
        public static Text Status(Transform slot, string text)
        {
            var status = UiFactory.Text(slot, text, 13, UITheme.Muted, TextAnchor.MiddleCenter);
            status.raycastTarget = false;
            UiFactory.SetSize(status, 420, 20);
            return status;
        }

        private static void Shade(Text text)
        {
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
        }
    }
}
