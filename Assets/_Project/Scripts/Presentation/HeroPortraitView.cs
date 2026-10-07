using System;
using UnityEngine;
using UnityEngine.UI;
using LiarsBatting.Core;

namespace LiarsBatting.Presentation
{
    // A hero badge: the character portrait, name, and remaining ability
    // charges. Used twice per match (mine and the opponent's) -- only "mine"
    // shows remaining charges, since how much of a resource the opponent has
    // left is exactly the kind of thing this game hides (same reasoning as the
    // LIE TOKEN count). The ability's name/description isn't printed
    // permanently -- hovering the portrait shows it instead, and for heroes
    // whose ability is a single click (Paladin/Warrior/Wizard) clicking the
    // portrait is also what uses it.
    public class HeroPortraitView
    {
        public readonly RectTransform Root;
        private readonly Image _portraitImage;
        private readonly Text _nameText;
        private readonly Text _chargesText;
        private readonly GameObject _tooltip;
        private readonly Text _tooltipText;
        private readonly bool _showCharges;
        private Button _abilityButton;

        public HeroPortraitView(Transform parent, bool showCharges)
        {
            _showCharges = showCharges;
            Root = UiFactory.VerticalGroup(parent, "HeroPortrait", spacing: 3, childAlign: TextAnchor.UpperCenter);

            // The art is a 512x620 arch, so keep that aspect.
            var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(Image));
            portraitGo.transform.SetParent(Root, false);
            UiFactory.SetSize(portraitGo.transform, 76, 92);
            _portraitImage = portraitGo.GetComponent<Image>();
            _portraitImage.preserveAspect = true;

            _nameText = UiFactory.Text(Root, "", 12, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            _chargesText = UiFactory.Text(Root, "", 10, UITheme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);

            // Tooltip: a child of the portrait itself, so it needs no screen-
            // space math -- anchored to the portrait's right side (the portraits
            // sit near the top/bottom edges, where "above" or "below" would clip).
            var tooltipGo = new GameObject("Tooltip", typeof(RectTransform), typeof(Image));
            tooltipGo.transform.SetParent(portraitGo.transform, false);
            var tooltipRt = (RectTransform)tooltipGo.transform;
            tooltipRt.anchorMin = new Vector2(1f, 0.5f);
            tooltipRt.anchorMax = new Vector2(1f, 0.5f);
            tooltipRt.pivot = new Vector2(0f, 0.5f);
            tooltipRt.anchoredPosition = new Vector2(8, 0);
            tooltipRt.sizeDelta = new Vector2(190, 78);
            var tooltipImage = tooltipGo.GetComponent<Image>();
            tooltipImage.color = new Color(0.04f, 0.04f, 0.04f, 0.95f);
            tooltipImage.raycastTarget = false;
            _tooltip = tooltipGo;
            var tooltipCanvas = tooltipGo.AddComponent<Canvas>();   // always above the HUD
            tooltipCanvas.overrideSorting = true;
            tooltipCanvas.sortingOrder = 40;

            _tooltipText = UiFactory.Text(tooltipRt, "", 11, Color.white, TextAnchor.MiddleCenter);
            _tooltipText.raycastTarget = false;
            var tooltipTextRt = (RectTransform)_tooltipText.transform;
            tooltipTextRt.anchorMin = Vector2.zero;
            tooltipTextRt.anchorMax = Vector2.one;
            tooltipTextRt.offsetMin = new Vector2(8, 6);
            tooltipTextRt.offsetMax = new Vector2(-8, -6);

            var hover = portraitGo.AddComponent<HoverTrigger>();
            hover.OnEnter = () => { _tooltip.transform.SetAsLastSibling(); _tooltip.SetActive(true); };
            hover.OnExit = () => _tooltip.SetActive(false);
            _tooltip.SetActive(false);
        }

        public void SetHero(HeroId hero, int charges = -1)
        {
            var info = HeroCatalog.Get(hero);
            _nameText.text = info.Name;
            _tooltipText.text = $"{info.AbilityName}\n{info.AbilityDescription}";
            _portraitImage.sprite = Resources.Load<Sprite>($"Heroes/Hero_{hero}_Portrait");

            if (_showCharges && info.Charges > 0)
                RefreshCharges(charges, info.Charges);
            else
                _chargesText.text = "";

            // A stale "0 charges left" from a PREVIOUS game's hero must not carry
            // over and leave this game's ability permanently unusable.
            SetAbilityInteractable(charges != 0);
        }

        public void RefreshCharges(int remaining, int max)
        {
            if (_showCharges) _chargesText.text = $"{remaining}/{max}회";
        }

        // Lets the portrait itself act as the "use ability" button. No color
        // tint on press/disabled: the portrait is the hero's face, it shouldn't
        // go gray just because this hero's ability isn't click-driven.
        public void SetAbilityClickable(Action onClick)
        {
            _abilityButton = _portraitImage.gameObject.GetComponent<Button>();
            if (_abilityButton == null) _abilityButton = _portraitImage.gameObject.AddComponent<Button>();
            _abilityButton.transition = Selectable.Transition.None;
            _abilityButton.targetGraphic = _portraitImage;
            _abilityButton.onClick.RemoveAllListeners();
            if (onClick != null) _abilityButton.onClick.AddListener(() => onClick());
        }

        public void SetAbilityInteractable(bool interactable)
        {
            if (_abilityButton != null) _abilityButton.interactable = interactable;
        }
    }
}
