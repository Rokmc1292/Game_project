using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LiarsBatting.Core;

namespace LiarsBatting.Presentation
{
    // The 규칙집 (rulebook) opened from the main menu's top-left corner: a few short pages that explain the
    // game with examples. Page 1 teaches the strike / ball / out verdicts with the SAME lamps the game uses
    // (orange strike, green ball, red out), the other pages cover how a turn plays out and how a match ends.
    public class RulebookView
    {
        private static readonly Color Brass = new Color32(214, 160, 70, 255);
        private static readonly Color OutRed = new Color32(217, 72, 59, 255);
        private static readonly Color ChipInk = new Color32(20, 18, 14, 255);

        private readonly GameObject _root;
        private readonly Text _pageTitle;
        private readonly RectTransform _body;
        private readonly Text _pageNumber;
        private readonly Button _previous;
        private readonly Button _next;
        private readonly List<(string title, Action<RectTransform> build)> _pages = new List<(string, Action<RectTransform>)>();
        private int _page;

        public RulebookView(Transform parent)
        {
            var backdrop = UiFactory.FullScreen(parent, "Rulebook", new Color(0f, 0f, 0f, 0.82f));
            _root = backdrop.gameObject;

            var panel = UiFactory.Panel(backdrop, "RulebookPanel", UITheme.Surface2);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(860f, 680f);
            MenuButtonStyle.ApplyPanel(panel.GetComponent<Image>());

            var content = UiFactory.VerticalGroup(panel, "Content", spacing: 8,
                padding: new RectOffset(40, 40, 28, 26), childAlign: TextAnchor.UpperCenter);
            UiFactory.StretchToFillParent(content);

            var title = UiFactory.Text(content, "규칙집", 26, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.SetHeight(title, 36);
            Shade(title);
            var divider = UiFactory.Panel(content, "Divider", UITheme.Border);
            UiFactory.SetHeight(divider, 1);
            divider.GetComponent<Image>().raycastTarget = false;

            _pageTitle = UiFactory.Text(content, "", 20, Brass, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.SetHeight(_pageTitle, 30);
            Shade(_pageTitle);

            _body = UiFactory.VerticalGroup(content, "Body", spacing: 10, childAlign: TextAnchor.UpperCenter);
            _body.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;
            UiFactory.SetFlexible(_body, 1, 1);

            var footer = UiFactory.HorizontalGroup(content, "Footer", spacing: 14, childAlign: TextAnchor.MiddleCenter);
            UiFactory.SetHeight(footer, 48);
            UiFactory.SetFlexible(footer, 1, 0);
            _previous = UiFactory.Button(footer, "◀ 이전", UITheme.Surface2, UITheme.Ink, () => Go(-1), 15);
            UiFactory.SetSize(_previous, 120, 44);
            MenuButtonStyle.Apply(_previous, primary: false);
            _pageNumber = UiFactory.Text(footer, "", 15, UITheme.Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.SetSize(_pageNumber, 90, 44);
            _next = UiFactory.Button(footer, "다음 ▶", UITheme.Surface2, UITheme.Ink, () => Go(1), 15);
            UiFactory.SetSize(_next, 120, 44);
            MenuButtonStyle.Apply(_next, primary: false);
            var spacer = UiFactory.Panel(footer, "Spacer", Color.clear);
            spacer.GetComponent<Image>().raycastTarget = false;
            UiFactory.SetSize(spacer, 30, 44);
            var close = UiFactory.Button(footer, "닫기", UITheme.Accent, Color.white, Hide, 15);
            close.gameObject.name = "CloseRulebookButton";
            UiFactory.SetSize(close, 140, 44);
            MenuButtonStyle.ApplyChoice(close, UITheme.Accent);

            _pages.Add(("스트라이크 · 볼 · 아웃", BuildJudgePage));
            _pages.Add(("한 턴은 이렇게 진행됩니다", BuildTurnPage));
            _pages.Add(("승리와 무승부", BuildEndingPage));

            _root.SetActive(false);
        }

        public void Show()
        {
            _page = 0;
            Render();
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
        }

        public void Hide() => _root.SetActive(false);

        public bool IsOpen => _root.activeSelf;

        private void Go(int delta)
        {
            _page = Mathf.Clamp(_page + delta, 0, _pages.Count - 1);
            Render();
        }

        private void Render()
        {
            for (int i = _body.childCount - 1; i >= 0; i--)
            {
                var child = _body.GetChild(i).gameObject;
                child.SetActive(false);   // Destroy is end-of-frame: hide it so it can't take layout space now
                UnityEngine.Object.Destroy(child);
            }
            var page = _pages[_page];
            _pageTitle.text = page.title;
            page.build(_body);
            _pageNumber.text = $"{_page + 1} / {_pages.Count}";
            _previous.interactable = _page > 0;
            _next.interactable = _page < _pages.Count - 1;
        }

        // ---------------- page 1: strike / ball / out ----------------

        private void BuildJudgePage(RectTransform body)
        {
            Line(body, "상대의 비밀번호(중복 없는 숫자 4개)를 추측하면, 상대가 아래 기준으로 결과를 알려줍니다.", UITheme.Muted, 14);

            var secret = new[] { 1, 2, 3, 4 };
            Example(body, "스트라이크", UITheme.StrikeLamp, "숫자와 자리가 모두 같은 숫자", secret, new[] { 1, 5, 6, 7 }, new JudgeResult(1, 0));
            Example(body, "볼", UITheme.BallLamp, "숫자는 있지만 자리가 다른 숫자", secret, new[] { 5, 1, 6, 7 }, new JudgeResult(0, 1));
            Example(body, "아웃", OutRed, "같은 숫자가 하나도 없음 (불이 모두 꺼집니다)", secret, new[] { 5, 6, 7, 8 }, new JudgeResult(0, 0));
            Example(body, "함께 나오는 경우", Brass, "스트라이크와 볼이 함께 나올 수도 있습니다", secret, new[] { 1, 3, 5, 2 }, new JudgeResult(1, 2));

            Line(body, "초록 볼 4개 · 주황 스트라이크 3개. 4스트라이크는 공 대신 정답으로 표시합니다.", UITheme.Muted, 13);
        }

        // One worked example: a coloured name chip + the rule, then "비밀번호 [cards]  추측 [cards]  결과 [lamps]".
        private void Example(Transform body, string name, Color chipColor, string rule, int[] secret, int[] guess, JudgeResult result)
        {
            var block = UiFactory.VerticalGroup(body, "Example_" + name, spacing: 4, childAlign: TextAnchor.UpperCenter);
            UiFactory.SetHeight(block, 88);

            var heading = UiFactory.HorizontalGroup(block, "Heading", spacing: 10, childAlign: TextAnchor.MiddleCenter);
            heading.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            UiFactory.SetHeight(heading, 26);
            var chip = UiFactory.Panel(heading, "Chip", chipColor);
            UiFactory.SetSize(chip, name.Length > 3 ? 150 : 96, 26);
            var chipText = UiFactory.Text(chip, name, 15, name == "함께 나오는 경우" ? ChipInk : (chipColor == OutRed ? Color.white : ChipInk),
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.StretchToFillParent((RectTransform)chipText.transform);
            chipText.raycastTarget = false;
            var ruleText = UiFactory.Text(heading, rule, 15, UITheme.Ink, TextAnchor.MiddleLeft);
            ruleText.raycastTarget = false;
            UiFactory.SetFlexible(ruleText, 1, 0);

            var row = UiFactory.HorizontalGroup(block, "Row", spacing: 10, childAlign: TextAnchor.MiddleCenter);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            UiFactory.SetHeight(row, 56);

            Caption(row, "비밀번호");
            MiniCards(row, secret, null);
            Caption(row, "추측");
            MiniCards(row, guess, secret);
            Caption(row, "결과");
            var lamps = ResultLamps.Build(row, result, 18f, labels: false);
            lamps.name = "Lamps";
            if (result.IsOut)
            {
                var out_ = UiFactory.Text(row, "OUT", 16, OutRed, TextAnchor.MiddleLeft, FontStyle.Bold);
                out_.raycastTarget = false;
                UiFactory.SetSize(out_, 50, 30);
            }
        }

        private static void Caption(Transform row, string text)
        {
            var label = UiFactory.Text(row, text, 13, UITheme.Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.raycastTarget = false;
            UiFactory.SetSize(label, 58, 30);
        }

        // Four small cards. With `against` given, each card gets the colour of its verdict (orange outline =
        // strike, green = ball) so the example shows WHY the lamps light up.
        private static void MiniCards(Transform row, int[] digits, int[] against)
        {
            var cards = UiFactory.HorizontalGroup(row, "Cards", spacing: 5, childAlign: TextAnchor.MiddleCenter);
            cards.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            UiFactory.SetSize(cards, 4 * 36 + 3 * 5, 52);            for (int i = 0; i < digits.Length; i++)
            {
                var holder = UiFactory.Panel(cards, "Card" + i, Color.clear);
                holder.GetComponent<Image>().raycastTarget = false;
                UiFactory.SetSize(holder, 36, 50);
                var face = PopupParts.CardFace(holder, CardArt.Digit(digits[i]));
                if (against == null) continue;

                Color? mark = null;
                if (digits[i] == against[i]) mark = UITheme.StrikeLamp;
                else if (Array.IndexOf(against, digits[i]) >= 0) mark = UITheme.BallLamp;
                if (!mark.HasValue) continue;
                var outline = face.gameObject.AddComponent<Outline>();
                outline.effectColor = mark.Value;
                outline.effectDistance = new Vector2(2.5f, -2.5f);
            }
        }

        // ---------------- page 2: a turn ----------------

        private void BuildTurnPage(RectTransform body)
        {
            Step(body, "1", "내 턴", "상대의 비밀번호를 추측해 제출합니다. 제한시간이 지나면 무작위 숫자로 자동 제출됩니다.");
            Step(body, "2", "상대의 대답", "상대가 스트라이크·볼 결과를 알려줍니다. 상대는 LIE TOKEN(기본 2개)을 써서 거짓 결과를 말할 수 있습니다. 4스트라이크(정답)는 거짓으로 말할 수 없습니다. (허언수 제외)");
            Step(body, "3", "믿는다 / 의심한다", "결과를 믿으면 그대로 턴이 넘어갑니다. 의심했는데 진실이었다면 내 비밀번호 한 자리가 공개되고, 거짓이었다면 상대의 한 자리가 공개됩니다.");
            Step(body, "4", "공개와 패배", "한 번 공개된 자리는 상대에게 계속 보입니다. 내 비밀번호 4자리가 모두 공개되면 패배합니다.");
            Step(body, "5", "영웅 능력", "영웅마다 능력이 있습니다. 내 초상화 위에 마우스를 올리면 설명이 나옵니다.");
        }

        private void Step(Transform body, string number, string title, string text)
        {
            var row = UiFactory.HorizontalGroup(body, "Step" + number, spacing: 14, childAlign: TextAnchor.MiddleLeft);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            UiFactory.SetHeight(row, 80);

            var badge = UiFactory.Panel(row, "Number", Brass);
            UiFactory.SetSize(badge, 34, 34);
            var n = UiFactory.Text(badge, number, 18, ChipInk, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.StretchToFillParent((RectTransform)n.transform);
            n.raycastTarget = false;

            var column = UiFactory.VerticalGroup(row, "Text", spacing: 2, childAlign: TextAnchor.MiddleLeft);
            UiFactory.SetFlexible(column, 1, 0);
            var heading = UiFactory.Text(column, title, 16, UITheme.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            heading.raycastTarget = false;
            UiFactory.SetHeight(heading, 22);
            var description = UiFactory.Text(column, text, 14, UITheme.Muted, TextAnchor.UpperLeft);
            description.raycastTarget = false;
            UiFactory.SetHeight(description, 52);
        }

        // ---------------- page 3: how a match ends ----------------

        private void BuildEndingPage(RectTransform body)
        {
            Step(body, "★", "승리", "상대의 비밀번호를 4스트라이크로 맞히면 승리합니다. 상대의 비밀번호 4자리가 모두 공개되어도 승리합니다.");
            Step(body, "1", "후공의 핸디캡", "선공이 먼저 맞히면, 후공에게 마지막 추측이 한 번 주어집니다. 후공도 맞히면 무승부, 못 맞히면 선공의 승리입니다.");
            Step(body, "2", "후공이 맞히면", "후공이 먼저 맞히면 마지막 기회 없이 바로 후공의 승리입니다.");
            Step(body, "3", "예외: 연속철", "연속철이 선공이고 '연속 돌격'을 아직 쓰지 않았다면, 맞히는 순간 승리가 확정됩니다. 이미 썼다면 위의 규칙을 그대로 따릅니다.");
            Step(body, "4", "무승부와 리매치", "무승부가 되면 서로 리매치를 신청할 수 있습니다. 둘 다 수락하면 선후공을 바꿔 다시 시작하고, 한 명이라도 거절하면 그대로 끝납니다.");
        }

        // ---------------- small helpers ----------------

        private static void Line(Transform body, string text, Color color, int size)
        {
            var line = UiFactory.Text(body, text, size, color, TextAnchor.MiddleCenter);
            line.raycastTarget = false;
            UiFactory.SetHeight(line, size + 12);
        }

        private static void Shade(Text text)
        {
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(1.5f, -2f);
        }
    }
}
