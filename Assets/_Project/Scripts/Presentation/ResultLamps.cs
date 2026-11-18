using System;
using UnityEngine;
using UnityEngine.UI;
using LiarsBatting.Core;

namespace LiarsBatting.Presentation
{
    // A judge result drawn as two rows of four round lamps instead of "1S 2B" text:
    // the top row counts balls (green), the bottom row strikes (orange), each lit from the left.
    // Nothing lit = no strike and no ball. The same layout is used to show a result and to
    // pick one (BuildPicker), so what the defender sets is exactly what the guesser sees.
    public static class ResultLamps
    {
        private const float LabelWidth = 80f;

        private static Sprite _circle;

        // A soft-edged white disc, tinted per lamp. Built once at runtime so no art asset is needed.
        public static Sprite Circle
        {
            get
            {
                if (_circle != null) return _circle;
                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
                var pixels = new Color32[n * n];
                float r = n / 2f;
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                    byte a = (byte)(Mathf.Clamp01(r - 1f - d + 0.5f) * 255);
                    pixels[y * n + x] = new Color32(255, 255, 255, a);
                }
                tex.SetPixels32(pixels);
                tex.Apply();
                return _circle = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
            }
        }

        // Read-only lamps. labels: show "볼" / "스트라이크" beside the rows (popups); off for the
        // compact history rows.
        public static RectTransform Build(Transform parent, JudgeResult result, float size = 22f, bool labels = true)
        {
            var grid = Grid(parent, size, labels, out var ballLamps, out var strikeLamps);
            Paint(ballLamps, result.Ball, UITheme.BallLamp);
            Paint(strikeLamps, result.Strike, UITheme.StrikeLamp);
            return grid;
        }

        // Clickable lamps for choosing what to report. Clicking the n-th lamp of a row lights
        // lamps 1..n; clicking the last lit lamp again turns it off. onChanged gets the new
        // result after every click.
        public static RectTransform BuildPicker(Transform parent, JudgeResult start, float size,
            Action<JudgeResult> onChanged)
        {
            var grid = Grid(parent, size, true, out var ballLamps, out var strikeLamps);
            int balls = start.Ball, strikes = start.Strike;

            void Repaint()
            {
                Paint(ballLamps, balls, UITheme.BallLamp);
                Paint(strikeLamps, strikes, UITheme.StrikeLamp);
            }

            void MakeClickable(Image[] lamps, Func<int> get, Action<int> set)
            {
                for (int i = 0; i < lamps.Length; i++)
                {
                    int count = i + 1;
                    lamps[i].raycastTarget = true;
                    var button = lamps[i].gameObject.AddComponent<Button>();
                    button.targetGraphic = lamps[i];
                    button.transition = Selectable.Transition.None;   // the colour itself is the feedback
                    button.onClick.AddListener(() =>
                    {
                        set(get() == count ? count - 1 : count);
                        Repaint();
                        GameAudio.Play("SFX_UI_Click", volume: 0.6f);
                        onChanged?.Invoke(new JudgeResult(strikes, balls));
                    });
                }
            }

            MakeClickable(ballLamps, () => balls, v => balls = v);
            MakeClickable(strikeLamps, () => strikes, v => strikes = v);
            Repaint();
            return grid;
        }

        private static void Paint(Image[] lamps, int lit, Color on)
        {
            for (int i = 0; i < lamps.Length; i++) lamps[i].color = i < lit ? on : UITheme.LampOff;
        }

        // Ball row on top, strike row below.
        private static RectTransform Grid(Transform parent, float size, bool labels,
            out Image[] ballLamps, out Image[] strikeLamps)
        {
            float rowGap = Mathf.Max(2f, Mathf.Round(size * 0.25f));
            var grid = UiFactory.VerticalGroup(parent, "ResultLamps", spacing: rowGap, childAlign: TextAnchor.MiddleCenter);
            grid.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;

            ballLamps = Row(grid, "볼", UITheme.BallLamp, size, labels);
            strikeLamps = Row(grid, "스트라이크", UITheme.StrikeLamp, size, labels);

            float lampGap = Mathf.Max(2f, Mathf.Round(size * 0.3f));
            float width = size * Judge.Digits + lampGap * (Judge.Digits - 1)
                + (labels ? 2 * (LabelWidth + lampGap) : 0f);
            UiFactory.SetSize(grid, width, size * 2 + rowGap);
            return grid;
        }

        private static Image[] Row(Transform grid, string label, Color labelColor, float size, bool labels)
        {
            float lampGap = Mathf.Max(2f, Mathf.Round(size * 0.3f));
            var row = UiFactory.HorizontalGroup(grid, "Row_" + label, spacing: lampGap, childAlign: TextAnchor.MiddleCenter);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            UiFactory.SetHeight(row, size);

            if (labels)
            {
                var text = UiFactory.Text(row, label, 14, labelColor, TextAnchor.MiddleRight, FontStyle.Bold);
                UiFactory.SetSize(text, LabelWidth, size);
            }

            var lamps = new Image[Judge.Digits];
            for (int i = 0; i < lamps.Length; i++) lamps[i] = MakeLamp(row, size);

            if (labels)
            {
                // Mirror of the label's width on the right, so the lamps sit centred.
                var spacer = UiFactory.Panel(row, "Spacer", Color.clear);
                spacer.GetComponent<Image>().raycastTarget = false;
                UiFactory.SetSize(spacer, LabelWidth, size);
            }
            return lamps;
        }

        private static Image MakeLamp(Transform parent, float size)
        {
            var go = new GameObject("Lamp", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = Circle;
            image.preserveAspect = true;
            image.raycastTarget = false;
            UiFactory.SetSize(image, size, size);
            return image;
        }
    }
}
