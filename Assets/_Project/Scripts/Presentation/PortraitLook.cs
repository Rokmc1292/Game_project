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

        // Closed-eyelid overlay for the blink (Resources/Cosmetics/Blink/<Hero>_skin<0|1|2>), drawn
        // for the skin that is showing so the lids match its colours. Null for heroes whose eyes
        // can't blink (DemonHunter's sunglasses).
        public static Sprite BlinkFor(HeroId hero, PlayerProgress progress)
        {
            string skin = progress != null ? progress.HeroSkinId(hero) : RewardCatalog.SkinId(hero, 0);
            string n = skin.Substring(skin.Length - 1);   // "..._skinN"
            return Resources.Load<Sprite>($"Cosmetics/Blink/{hero}_skin{n}")
                   ?? Resources.Load<Sprite>($"Cosmetics/Blink/{hero}_skin0");
        }

        // Applies the full look to an Image, adding the animator the first time it's needed.
        public static void Apply(Image image, HeroId hero, PlayerProgress progress)
        {
            image.sprite = SpriteFor(hero, progress);
            var tint = TintFor(hero, progress);
            var animator = image.GetComponent<PortraitAnimator>();
            bool animated = AnimatedFor(hero, progress);
            if (animator == null && animated) animator = image.gameObject.AddComponent<PortraitAnimator>();
            if (animator != null) animator.Set(image, tint, animated, BlinkFor(hero, progress));
            else image.color = tint;
        }
    }

    // Portrait frames (player-level reward): an arch-shaped metal frame drawn over the portrait.
    // The art (Resources/Cosmetics/frame_*) is the 512x620 portrait canvas plus 30px on every
    // side, so the overlay sits on the portrait's rect stretched outwards by that margin.
    public static class PortraitFrame
    {
        private const string OverlayName = "FrameOverlay";
        private const float PadX = 30f / 512f;
        private const float PadY = 30f / 620f;

        public static Sprite ArtFor(string frameId) =>
            string.IsNullOrEmpty(frameId) || frameId == RewardCatalog.NoFrame
                ? null
                : Resources.Load<Sprite>($"Cosmetics/{frameId}");

        // Shows the frame's art over `portrait` (or hides it). True when art was applied.
        public static bool Apply(Image portrait, string frameId)
        {
            var existing = portrait.transform.Find(OverlayName);
            var overlay = existing != null ? existing.GetComponent<Image>() : null;
            var art = ArtFor(frameId);
            if (art == null)
            {
                if (overlay != null) overlay.gameObject.SetActive(false);
                return false;
            }
            if (overlay == null)
            {
                var go = new GameObject(OverlayName, typeof(RectTransform), typeof(Image));
                var rt = (RectTransform)go.transform;
                rt.SetParent(portrait.transform, false);
                rt.anchorMin = new Vector2(-PadX, -PadY);
                rt.anchorMax = new Vector2(1 + PadX, 1 + PadY);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                overlay = go.GetComponent<Image>();
                overlay.raycastTarget = false;
                // The portrait keeps its aspect inside a box that may be wider or taller than the
                // art; keeping the frame's aspect too makes both scale the same way and line up.
                overlay.preserveAspect = true;
            }
            overlay.sprite = art;
            overlay.gameObject.SetActive(true);
            return true;
        }
    }

    // The placeholder "moving portrait": a slow breathing scale plus a soft brightness shimmer.
    // The "moving portrait" (hero mastery Lv.7). Layers drawn over the portrait image:
    //   - a band of light sweeping across every few seconds (clipped to the portrait's shape)
    //   - small gold specks drifting upwards, kept off the hero's face and body
    //   - a rotating star flare on the gem at the top of the arch
    //   - an occasional blink (a closed-eyelid overlay made for each hero and skin)
    //   - a very faint overall shimmer
    // Everything sits in one child placed first, so a frame overlay or tooltip still draws on top.
    public class PortraitAnimator : MonoBehaviour
    {
        private const int SpeckCount = 10;
        private const float SweepPeriod = 4.5f;      // seconds between sweeps
        private const float SweepDuration = 1.2f;
        private const float SpeckRise = 3.6f;         // seconds for a speck to rise

        private Image _image;
        private Color _base = Color.white;
        private bool _on;

        private RectTransform _fx;
        private Image _maskImage;
        private RectTransform _sweep;
        private Image _sweepImage;
        private readonly Image[] _specks = new Image[SpeckCount];
        private readonly Vector3[] _speckSeeds = new Vector3[SpeckCount];   // x, phase, size
        private RectTransform _star;
        private Image _starImage;
        private Image _blink;
        private float _phase;
        private float _nextBlink;

        private static Sprite _bandSprite, _dotSprite, _starSprite;

        public void Set(Image image, Color baseColor, bool on, Sprite blinkSprite)
        {
            _image = image;
            _base = baseColor;
            _on = on;
            transform.localScale = Vector3.one;
            _image.color = _base;
            if (on) Build();
            if (_fx != null)
            {
                _fx.gameObject.SetActive(on);
                _maskImage.sprite = image.sprite;
                _blink.sprite = blinkSprite;
                _blink.enabled = false;
            }
        }

        private void Build()
        {
            if (_fx != null) return;
            _phase = Random.value * 10f;
            _nextBlink = Time.unscaledTime + Random.Range(1.5f, 4f);

            _fx = NewRect("PortraitFx", transform);
            _fx.SetAsFirstSibling();

            // clip layer: an invisible copy of the portrait used as a mask
            var maskRt = NewRect("FxMask", _fx);
            _maskImage = maskRt.gameObject.AddComponent<Image>();
            _maskImage.preserveAspect = true;
            _maskImage.raycastTarget = false;
            maskRt.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            _sweep = new GameObject("Sweep", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            _sweep.SetParent(maskRt, false);
            _sweep.anchorMin = _sweep.anchorMax = new Vector2(0.5f, 0.5f);
            _sweep.localRotation = Quaternion.Euler(0, 0, -28f);
            _sweepImage = _sweep.GetComponent<Image>();
            _sweepImage.sprite = BandSprite();
            _sweepImage.raycastTarget = false;

            for (int i = 0; i < SpeckCount; i++)
            {
                var speck = new GameObject($"Speck{i}", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                speck.transform.SetParent(maskRt, false);
                speck.sprite = DotSprite();
                speck.raycastTarget = false;
                var rt = speck.rectTransform;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                _specks[i] = speck;
                // keep specks to the sides or above the head, away from the hero's face and body
                float x = Random.value < 0.5f ? Random.Range(0.08f, 0.28f) : Random.Range(0.72f, 0.92f);
                _speckSeeds[i] = new Vector3(x, Random.value, Random.Range(0.04f, 0.075f));
            }

            _blink = NewRect("Blink", _fx).gameObject.AddComponent<Image>();
            _blink.preserveAspect = true;
            _blink.raycastTarget = false;

            _star = new GameObject("GemStar", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            _star.SetParent(_fx, false);
            _star.anchorMin = _star.anchorMax = new Vector2(0.5f, 1f - 38f / 620f);   // the gem on the arch
            _starImage = _star.GetComponent<Image>();
            _starImage.sprite = StarSprite();
            _starImage.raycastTarget = false;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        private void Update()
        {
            if (!_on || _image == null || _fx == null) return;
            float now = Time.unscaledTime;
            float t = now + _phase;
            var size = ((RectTransform)transform).rect.size;

            // faint shimmer
            float glow = 0.5f + 0.5f * Mathf.Sin(t * 2.6f);
            _image.color = Color.Lerp(_base, Color.white, 0.10f * glow);

            // light sweep: crosses during the first SweepDuration of every SweepPeriod
            float s = (t % SweepPeriod) / SweepDuration;
            _sweepImage.enabled = s < 1f;
            if (s < 1f)
            {
                _sweep.sizeDelta = new Vector2(size.x * 0.32f, size.y * 1.8f);
                _sweep.anchoredPosition = new Vector2(Mathf.Lerp(-0.9f, 0.9f, s) * size.x, 0f);
                _sweepImage.color = new Color(1f, 0.97f, 0.88f, 0.55f * Mathf.Sin(s * Mathf.PI));
            }

            // gold specks drifting up
            for (int i = 0; i < SpeckCount; i++)
            {
                var seed = _speckSeeds[i];
                float k = ((t / SpeckRise) + seed.y) % 1f;
                var rt = _specks[i].rectTransform;
                float d = seed.z * size.x;
                rt.sizeDelta = new Vector2(d, d);
                float x = seed.x + 0.025f * Mathf.Sin((k * 2f + seed.y) * Mathf.PI * 2f);
                rt.anchoredPosition = new Vector2(x * size.x, Mathf.Lerp(0.08f, 0.9f, k) * size.y);
                _specks[i].color = new Color(1f, 0.88f, 0.55f, 0.9f * Mathf.Sin(k * Mathf.PI));
            }

            // gem flare: rotating, pulsing star
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 2.9f);
            float starSize = size.x * (0.16f + 0.14f * pulse);
            _star.sizeDelta = new Vector2(starSize, starSize);
            _star.localRotation = Quaternion.Euler(0, 0, t * 40f);
            _starImage.color = new Color(1f, 1f, 1f, 0.35f + 0.65f * pulse);

            // blink every few seconds
            if (_blink.sprite != null)
            {
                if (now >= _nextBlink)
                {
                    _blink.enabled = true;
                    if (now >= _nextBlink + 0.13f)
                    {
                        _blink.enabled = false;
                        _nextBlink = now + Random.Range(2.5f, 5.5f);
                    }
                }
            }
        }

        // ---- generated sprites (made once, shared) ----
        private static Sprite BandSprite()
        {
            if (_bandSprite != null) return _bandSprite;
            const int w = 64, h = 4;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f;
                float a = Mathf.Exp(-u * u * 5f) * 0.8f + Mathf.Exp(-(u - 0.45f) * (u - 0.45f) * 120f) * 0.5f;
                for (int y = 0; y < h; y++) tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(a)));
            }
            tex.Apply();
            return _bandSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        }

        private static Sprite DotSprite()
        {
            if (_dotSprite != null) return _dotSprite;
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - r) ;
                a = a * a * 0.6f + (r < 0.28f ? 0.4f : 0f);
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(a)));
            }
            tex.Apply();
            return _dotSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }

        private static Sprite StarSprite()
        {
            if (_starSprite != null) return _starSprite;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = Mathf.Abs((x + 0.5f) / n * 2f - 1f), dy = Mathf.Abs((y + 0.5f) / n * 2f - 1f);
                // four thin rays along the axes plus a soft core
                float rays = Mathf.Exp(-dx * 40f) * (1f - dy) + Mathf.Exp(-dy * 40f) * (1f - dx);
                float core = Mathf.Exp(-(dx * dx + dy * dy) * 30f);
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(rays + core)));
            }
            tex.Apply();
            return _starSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }
    }
}
