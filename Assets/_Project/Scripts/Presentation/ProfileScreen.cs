using System;
using System.Collections.Generic;
using LiarsBatting.Core;
using LiarsBatting.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // The 프로필 screen: my picture (one of the hero portraits), nickname, record against people,
    // a portrait picker and the nickname change that used to be its own screen. Pure view: the
    // caller (GameBootstrap) does the saving through ProfileStore and reports back with
    // SetStatus / ShowProfile.
    public class ProfileScreen
    {
        public readonly GameObject Root;

        public Action OnBack;
        public Action<HeroId> OnIconPicked;
        public Action<string> OnRenameRequested;

        private static readonly Color Brass = new Color32(214, 160, 70, 255);

        private readonly Image _bigPortrait;
        private readonly Text _nicknameText;
        private readonly Text _recordText;
        private readonly Text _winRateText;
        private readonly Text _statusText;
        private readonly InputField _renameInput;
        private readonly Button _renameButton;
        private readonly Dictionary<HeroId, Outline> _iconHighlights = new Dictionary<HeroId, Outline>();
        private readonly List<Button> _iconButtons = new List<Button>();

        public ProfileScreen(Transform root)
        {
            Root = new GameObject("ProfileScreen", typeof(RectTransform));
            var rootRt = (RectTransform)Root.transform;
            rootRt.SetParent(root, false);
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = new Vector2(0, -56);   // below the header

            var panel = UiFactory.Panel(rootRt, "ProfilePanel", UITheme.Surface2);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(900f, 560f);
            MenuButtonStyle.ApplyPanel(panel.GetComponent<Image>());

            var columns = UiFactory.HorizontalGroup(panel, "Columns", spacing: 40,
                padding: new RectOffset(40, 40, 32, 28), childAlign: TextAnchor.UpperLeft);
            UiFactory.StretchToFillParent(columns);
            columns.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = true;

            // ---- left: who I am ----
            var left = UiFactory.VerticalGroup(columns, "Identity", spacing: 10, childAlign: TextAnchor.UpperCenter);
            UiFactory.SetWidth(left, 280);

            var title = UiFactory.Text(left, "프로필", 24, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.SetHeight(title, 34);
            AddShadow(title);

            var frame = UiFactory.Panel(left, "PortraitFrame", new Color(0.04f, 0.035f, 0.03f, 0.9f));
            UiFactory.SetSize(frame, 200, 242);
            var frameOutline = frame.gameObject.AddComponent<Outline>();
            frameOutline.effectColor = Brass;
            frameOutline.effectDistance = new Vector2(2f, -2f);
            var bigPortraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(Image));
            var bigPortraitRt = (RectTransform)bigPortraitGo.transform;
            bigPortraitRt.SetParent(frame, false);
            UiFactory.StretchToFillParent(bigPortraitRt);
            bigPortraitRt.offsetMin = new Vector2(8, 8);
            bigPortraitRt.offsetMax = new Vector2(-8, -8);
            _bigPortrait = bigPortraitGo.GetComponent<Image>();
            _bigPortrait.preserveAspect = true;
            _bigPortrait.raycastTarget = false;

            _nicknameText = UiFactory.Text(left, "", 22, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.SetHeight(_nicknameText, 32);
            AddShadow(_nicknameText);

            var divider = UiFactory.Panel(left, "Divider", UITheme.Border);
            UiFactory.SetHeight(divider, 1);

            var recordLabel = UiFactory.Text(left, "사람 상대 전적", 13, UITheme.Muted, TextAnchor.MiddleCenter);
            UiFactory.SetHeight(recordLabel, 20);
            _recordText = UiFactory.Text(left, "", 26, Brass, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.SetHeight(_recordText, 36);
            AddShadow(_recordText);
            _winRateText = UiFactory.Text(left, "", 13, UITheme.Muted, TextAnchor.MiddleCenter);
            UiFactory.SetHeight(_winRateText, 20);

            // ---- right: what I can change ----
            var right = UiFactory.VerticalGroup(columns, "Settings", spacing: 10, childAlign: TextAnchor.UpperLeft);
            UiFactory.SetFlexible(right, 1, 0);

            var iconLabel = UiFactory.Text(right, "프로필 사진", 15, Brass, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiFactory.SetHeight(iconLabel, 24);

            var grid = UiFactory.Grid(right, "IconGrid", columns: 4, cellSize: 96, spacing: 10);
            var gridLayout = grid.GetComponent<GridLayoutGroup>();
            gridLayout.cellSize = new Vector2(96, 118);
            gridLayout.childAlignment = TextAnchor.UpperLeft;
            UiFactory.SetHeight(grid, 118 * 2 + 10);
            foreach (var info in HeroCatalog.All) BuildIconCell(grid, info);

            var renameLabel = UiFactory.Text(right, "닉네임 변경 (최대 12자)", 15, Brass, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiFactory.SetHeight(renameLabel, 24);

            var renameRow = UiFactory.HorizontalGroup(right, "RenameRow", spacing: 10);
            UiFactory.SetHeight(renameRow, 48);
            _renameInput = UiFactory.InputField(renameRow, "새 닉네임");
            _renameInput.GetComponent<Image>().color = new Color(0.04f, 0.035f, 0.03f, 0.9f);
            var inputOutline = _renameInput.gameObject.AddComponent<Outline>();
            inputOutline.effectColor = UITheme.Border;
            inputOutline.effectDistance = new Vector2(1.5f, -1.5f);
            UiFactory.SetFlexible(_renameInput, 1, 0);
            _renameButton = UiFactory.Button(renameRow, "변경", UITheme.Surface2, UITheme.Ink, () =>
            {
                string name = _renameInput.text.Trim();
                if (name.Length == 0) { SetStatus("새 닉네임을 입력해주세요.", true); return; }
                OnRenameRequested?.Invoke(name);
            }, 15);
            UiFactory.SetSize(_renameButton, 110, 48);
            MenuButtonStyle.Apply(_renameButton, primary: false);

            _statusText = UiFactory.Text(right, "", 13, UITheme.Muted, TextAnchor.MiddleLeft);
            UiFactory.SetHeight(_statusText, 20);

            var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            spacer.transform.SetParent(right, false);
            spacer.GetComponent<LayoutElement>().flexibleHeight = 1;

            var backRow = UiFactory.HorizontalGroup(right, "BackRow", spacing: 0, childAlign: TextAnchor.MiddleRight);
            UiFactory.SetHeight(backRow, 48);
            var back = UiFactory.Button(backRow, "메인 메뉴로", UITheme.Surface2, UITheme.Ink, () => OnBack?.Invoke(), 15);
            UiFactory.SetSize(back, 180, 48);
            MenuButtonStyle.Apply(back, primary: false);

            Root.SetActive(false);
        }

        private void BuildIconCell(Transform grid, HeroInfo info)
        {
            var hero = info.Id;
            var cell = new GameObject($"Icon_{hero}", typeof(RectTransform), typeof(Image), typeof(Button));
            cell.transform.SetParent(grid, false);
            var bg = cell.GetComponent<Image>();
            bg.color = new Color(0.04f, 0.035f, 0.03f, 0.9f);
            var button = cell.GetComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(() => GameAudio.Play("SFX_UI_Click", volume: 0.6f));
            button.onClick.AddListener(() => OnIconPicked?.Invoke(hero));
            cell.AddComponent<ButtonHoverSound>();
            _iconButtons.Add(button);

            var highlight = cell.AddComponent<Outline>();
            highlight.effectColor = Brass;
            highlight.effectDistance = new Vector2(3f, -3f);
            highlight.enabled = false;
            _iconHighlights[hero] = highlight;

            var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(Image));
            var portraitRt = (RectTransform)portraitGo.transform;
            portraitRt.SetParent(cell.transform, false);
            UiFactory.StretchToFillParent(portraitRt);
            portraitRt.offsetMin = new Vector2(6, 22);
            portraitRt.offsetMax = new Vector2(-6, -4);
            var portrait = portraitGo.GetComponent<Image>();
            portrait.sprite = PortraitOf(hero);
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;

            var name = UiFactory.Text(cell.transform, info.Name, 12, UITheme.Ink, TextAnchor.MiddleCenter);
            name.raycastTarget = false;
            var nameRt = (RectTransform)name.transform;
            nameRt.anchorMin = new Vector2(0, 0);
            nameRt.anchorMax = new Vector2(1, 0);
            nameRt.pivot = new Vector2(0.5f, 0);
            nameRt.anchoredPosition = new Vector2(0, 2);
            nameRt.sizeDelta = new Vector2(0, 20);
        }

        public static Sprite PortraitOf(HeroId hero) => Resources.Load<Sprite>($"Heroes/Hero_{hero}_Portrait");

        // Shown right away with what's known (the nickname) while the profile loads.
        public void ShowLoading(string nickname)
        {
            _nicknameText.text = nickname;
            _recordText.text = "…";
            _winRateText.text = "불러오는 중";
            _renameInput.text = "";
            SetStatus("", false);
            SetInteractable(false);
        }

        public void ShowProfile(PlayerProfile profile)
        {
            _nicknameText.text = profile.Nickname;
            _recordText.text = profile.RecordText;
            _winRateText.text = profile.HumanGames == 0
                ? "아직 사람과 대전한 기록이 없습니다"
                : $"승률 {profile.WinRatePercent}% · 총 {profile.HumanGames}판";
            SetSelectedIcon(profile.ProfileIcon);
            SetInteractable(true);
        }

        public void SetSelectedIcon(HeroId hero)
        {
            _bigPortrait.sprite = PortraitOf(hero);
            foreach (var pair in _iconHighlights) pair.Value.enabled = pair.Key == hero;
        }

        public void SetInteractable(bool interactable)
        {
            foreach (var b in _iconButtons) b.interactable = interactable;
            _renameButton.interactable = interactable;
        }

        public void ClearRenameInput() => _renameInput.text = "";

        public void SetStatus(string message, bool isError)
        {
            _statusText.text = message;
            _statusText.color = isError ? UITheme.Clay : UITheme.Muted;
        }

        private static void AddShadow(Text text)
        {
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(1.5f, -2f);
        }
    }
}
