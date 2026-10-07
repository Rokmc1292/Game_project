using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // A row of 4 cards showing the hidden "?" card for a still-hidden digit and the actual
    // digit card once it's been forced open (or, with ShowDigits, always showing the digits).
    public class RevealedDigitsRow
    {
        public readonly RectTransform Root;
        private readonly Image[] _slots = new Image[4];

        public RevealedDigitsRow(Transform parent, float cardWidth = 28f, float cardHeight = 39f, float spacing = 8f)
        {
            Root = UiFactory.HorizontalGroup(parent, "RevealedRow", spacing: spacing, childAlign: TextAnchor.MiddleCenter);
            UiFactory.SetHeight(Root, cardHeight);
            for (int i = 0; i < 4; i++)
            {
                var slot = UiFactory.Panel(Root, $"Rev{i}", Color.white);
                UiFactory.SetSize(slot, cardWidth, cardHeight);
                var img = slot.GetComponent<Image>();
                img.sprite = CardArt.Unknown();
                img.preserveAspect = true;
                img.raycastTarget = false;
                _slots[i] = img;
            }
        }

        public void Refresh(int[] secret, bool[] revealed)
        {
            for (int i = 0; i < 4; i++)
                _slots[i].sprite = revealed[i] ? CardArt.Digit(secret[i]) : CardArt.Unknown();
        }

        // Always show every digit (used for my own secret).
        public void ShowDigits(int[] digits)
        {
            for (int i = 0; i < 4; i++)
                _slots[i].sprite = CardArt.Digit(digits[i]);
        }
    }
}
