using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // Applies the rusted-iron-plate look to a button that UiFactory.Button already
    // built. The plate sprites are 9-sliced (rivets stay put, the middle stretches),
    // and hover / pressed / disabled are separate sprites (SpriteSwap).
    // primary = the highlighted plate with the amber border (e.g. "AI 매칭").
    public static class MenuButtonStyle
    {
        private static readonly Color PrimaryText = new Color32(255, 226, 170, 255);

        public static void Apply(Button button, bool primary)
        {
            string prefix = primary ? "btn_primary" : "btn";
            var normal   = Load(primary ? "btn_primary" : "btn_normal");
            var hover    = Load(primary ? "btn_primary_hover" : "btn_hover");
            var pressed  = Load(primary ? "btn_primary_pressed" : "btn_pressed");
            var disabled = Load("btn_disabled");
            if (normal == null) return; // sprites missing: leave the default flat style

            var image = button.targetGraphic as Image ?? button.GetComponent<Image>();
            image.sprite = normal;
            image.type = Image.Type.Sliced;
            image.color = Color.white;

            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = hover,
                pressedSprite = pressed,
                selectedSprite = normal,   // don't stay "hover-looking" after a click
                disabledSprite = disabled
            };

            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.color = primary ? PrimaryText : UITheme.Ink;
                var shadow = label.GetComponent<Shadow>() ?? label.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
                shadow.effectDistance = new Vector2(1.5f, -2f);
            }
        }

        private static Sprite Load(string name)
        {
            var s = Resources.Load<Sprite>("Buttons/" + name);
            if (s == null) Debug.LogWarning("MenuButtonStyle: 스프라이트를 찾을 수 없음 Resources/Buttons/" + name);
            return s;
        }
    }
}
