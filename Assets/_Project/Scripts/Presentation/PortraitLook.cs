using LiarsBatting.Core;
using LiarsBatting.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // How a hero's portrait looks for a given player: the equipped mastery skin and whether the
    // "moving portrait" effect is on. Placeholder until real art exists: a skin is a colour tint
    // of the normal portrait, unless a sprite is placed at Resources/Cosmetics/<skin id>
    // (e.g. Cosmetics/Wizard_skin1), which is then used as-is.
    public static class PortraitLook
    {
        public static Sprite SpriteFor(HeroId hero, PlayerProgress progress)
        {
            var art = SkinArt(hero, progress);
            return art != null ? art : Resources.Load<Sprite>($"Heroes/Hero_{hero}_Portrait");
        }

        public static Color TintFor(HeroId hero, PlayerProgress progress)
        {
            if (progress == null || SkinArt(hero, progress) != null) return Color.white;
            var skin = RewardCatalog.Find(progress.HeroSkinId(hero));
            return skin != null && !string.IsNullOrEmpty(skin.Tint) ? UITheme.Hex(skin.Tint) : Color.white;
        }

        public static bool AnimatedFor(HeroId hero, PlayerProgress progress) => progress != null && progress.HeroEffectOn(hero);

        private static Sprite SkinArt(HeroId hero, PlayerProgress progress)
        {
            if (progress == null) return null;
            string skin = progress.HeroSkinId(hero);
            if (skin == RewardCatalog.SkinId(hero, 0)) return null;
            return Resources.Load<Sprite>($"Cosmetics/{skin}");
        }

        // Applies the full look to an Image, adding the animator the first time it's needed.
        public static void Apply(Image image, HeroId hero, PlayerProgress progress)
        {
            image.sprite = SpriteFor(hero, progress);
            var tint = TintFor(hero, progress);
            var animator = image.GetComponent<PortraitAnimator>();
            bool animated = AnimatedFor(hero, progress);
            if (animator == null && animated) animator = image.gameObject.AddComponent<PortraitAnimator>();
            if (animator != null) animator.Set(image, tint, animated);
            else image.color = tint;
        }
    }

    // The placeholder "moving portrait": a slow breathing scale plus a soft brightness shimmer.
    public class PortraitAnimator : MonoBehaviour
    {
        private Image _image;
        private Color _base = Color.white;
        private bool _on;

        public void Set(Image image, Color baseColor, bool on)
        {
            _image = image;
            _base = baseColor;
            _on = on;
            if (!on)
            {
                transform.localScale = Vector3.one;
                _image.color = _base;
            }
        }

        private void Update()
        {
            if (!_on || _image == null) return;
            float t = Time.unscaledTime;
            transform.localScale = Vector3.one * (1f + 0.035f * Mathf.Sin(t * 2.2f));
            float glow = 0.5f + 0.5f * Mathf.Sin(t * 3.1f);
            _image.color = Color.Lerp(_base, Color.white, 0.35f * glow) * new Color(1f + 0.12f * glow, 1f + 0.1f * glow, 1f + 0.05f * glow, 1f);
        }
    }
}
