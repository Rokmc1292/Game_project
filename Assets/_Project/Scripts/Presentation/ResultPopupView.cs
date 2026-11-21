using System;
using System.Collections;
using System.Collections.Generic;
using LiarsBatting.Core;
using LiarsBatting.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // Victory / defeat popup: a heavy black-leather ledger panel with a blind-embossed double border,
    // a muted foil title ("승 리" in aged brass, "패 배" in dull red), a one-line verdict, the revealed
    // secret as cards, and a single button back to the main menu.
    //
    // Entrance (deliberately heavy, no glow or particles): the room dims, the panel sinks into place,
    // the title lands like a stamp (a short thud shakes the panel), the verdict fades in, the cards turn
    // over one by one, and the button appears last. A defeat waits a beat longer and thuds harder.
    // Art lives in Resources/Result: result_panel (9-sliced), result_title_win, result_title_lose.
    public class ResultPopupView
    {
        private enum Outcome { Win, Lose, Draw }
        // ---- timing knobs (seconds / pixels) ----
        private const float DimDuration = 0.5f;
        private const float PanelDelay = 0.1f;
        private const float PanelDuration = 0.5f;
        private const float PanelSinkPixels = 32f;
        private const float StampDelayWin = 0.65f;
        private const float StampDelayLose = 0.95f;       // a defeat lets the silence hang a little longer
        private const float StampDuration = 0.16f;
        private const float ShakeDuration = 0.16f;
        private const float ShakePixelsWin = 3f;
        private const float ShakePixelsLose = 6f;
        private const float MessageFadeDuration = 0.35f;
        private const float CardStep = 0.3f;              // gap between one card turning and the next
        private const float CardFlipDuration = 0.2f;
        private const float ButtonFadeDuration = 0.3f;
        private const float DimAlpha = 0.78f;

        public readonly RectTransform Root;

        private readonly MonoBehaviour _host;
        private readonly Func<HeroId?> _heroProvider;
        private readonly Image _dim;
        private readonly RectTransform _panel;
        private readonly CanvasGroup _panelGroup;
        private readonly RectTransform _titleRect;
        private readonly Image _title;
        private readonly CanvasGroup _titleGroup;
        private readonly Text _message;
        private readonly CanvasGroup _messageGroup;
        private readonly Text _summary;
        private readonly RevealedDigitsRow _digits;
        private readonly Text _digitsUnavailable;
        private readonly CanvasGroup _buttonGroup;
        private readonly Sprite _winTitle;
        private readonly Sprite _loseTitle;
        private readonly Text _drawTitle;          // "무 승 부": there is no title art for a draw, so it is lettered
        private readonly Text _rematchStatus;
        private readonly Button _rematchButton;
        private Coroutine _routine;
        private Coroutine _progressRoutine;
        private readonly List<string> _progressSounds = new List<string>();
        private bool _introComplete;
        private bool _settlementReceived;
        private bool _resultVoiceActive;
        private int[] _displayDigits;

        public bool IsVisible => Root.gameObject.activeInHierarchy;
        public bool IntroComplete => _introComplete;

        // onRematch: the 리매치 button, only shown on a draw (see ShowDraw).
        public ResultPopupView(Transform parent, MonoBehaviour host, Action onMainMenu, Action onRematch = null,
            Func<HeroId?> heroProvider = null)
        {
            _host = host;
            _heroProvider = heroProvider;
            _winTitle = Resources.Load<Sprite>("Result/result_title_win");
            _loseTitle = Resources.Load<Sprite>("Result/result_title_lose");

            // full-screen dim that also blocks clicks behind the popup
            var rootGo = new GameObject("ResultPopup", typeof(RectTransform), typeof(Image));
            Root = (RectTransform)rootGo.transform;
            Root.SetParent(parent, false);
            UiFactory.StretchToFillParent(Root);
            _dim = rootGo.GetComponent<Image>();
            _dim.color = new Color(0f, 0f, 0f, DimAlpha);

            var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            _panel = (RectTransform)panelGo.transform;
            _panel.SetParent(Root, false);
            _panel.anchorMin = new Vector2(0.5f, 0.5f);
            _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = new Vector2(640f, 506f);
            _panelGroup = panelGo.GetComponent<CanvasGroup>();
            var panelImage = panelGo.GetComponent<Image>();
            var panelSprite = Resources.Load<Sprite>("Result/result_panel");
            if (panelSprite != null)
            {
                panelImage.sprite = panelSprite;
                panelImage.type = Image.Type.Sliced;
                panelImage.color = Color.white;
            }
            else
            {
                Debug.LogWarning("ResultPopupView: Resources/Result/result_panel 을 찾을 수 없음");
                panelImage.color = UITheme.Bg;
            }

            var content = UiFactory.VerticalGroup(_panel, "Content", spacing: 8,
                padding: new RectOffset(70, 70, 44, 62), childAlign: TextAnchor.UpperCenter);
            UiFactory.StretchToFillParent(content);

            var titleGo = new GameObject("Title", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            titleGo.transform.SetParent(content, false);
            UiFactory.SetSize(titleGo.transform, 210, 90);
            _titleRect = (RectTransform)titleGo.transform;
            _title = titleGo.GetComponent<Image>();
            _title.preserveAspect = true;
            _title.raycastTarget = false;
            _titleGroup = titleGo.GetComponent<CanvasGroup>();

            _drawTitle = UiFactory.Text(titleGo.transform, "무 승 부", 46, new Color32(214, 160, 70, 255),
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.StretchToFillParent((RectTransform)_drawTitle.transform);
            _drawTitle.raycastTarget = false;
            _drawTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            var drawShadow = _drawTitle.gameObject.AddComponent<Shadow>();
            drawShadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
            drawShadow.effectDistance = new Vector2(2f, -3f);
            _drawTitle.gameObject.SetActive(false);

            _message = UiFactory.Text(content, "", 22, new Color32(190, 182, 166, 255), TextAnchor.MiddleCenter, FontStyle.Bold);
            _message.raycastTarget = false;
            UiFactory.SetHeight(_message, 30);
            var shadow = _message.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(1.5f, -2f);
            _messageGroup = _message.gameObject.AddComponent<CanvasGroup>();

            _digits = new RevealedDigitsRow(content, 58f, 81f, 12f);
            UiFactory.SetFlexible(_digits.Root, 1, 0);
            _digitsUnavailable = UiFactory.Text(content, "상대 숫자를 확인할 수 없습니다", 16,
                UITheme.Muted, TextAnchor.MiddleCenter);
            _digitsUnavailable.raycastTarget = false;
            UiFactory.SetHeight(_digitsUnavailable, 81f);
            _digitsUnavailable.gameObject.SetActive(false);

            // XP / level / challenge summary, filled in by SetSummary once the match is settled.
            _summary = UiFactory.Text(content, "", 14, new Color32(214, 160, 70, 255), TextAnchor.MiddleCenter, FontStyle.Bold);
            _summary.raycastTarget = false;
            UiFactory.SetHeight(_summary, 58);
            var summaryShadow = _summary.gameObject.AddComponent<Shadow>();
            summaryShadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            summaryShadow.effectDistance = new Vector2(1f, -1.5f);

            // Rematch vote line (draws only): "상대의 응답을 기다리는 중..." / "상대가 리매치를 거절하였습니다".
            _rematchStatus = UiFactory.Text(content, "", 15, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            _rematchStatus.raycastTarget = false;
            UiFactory.SetHeight(_rematchStatus, 26);
            _rematchStatus.gameObject.SetActive(false);

            var buttonRow = UiFactory.HorizontalGroup(content, "ButtonRow", spacing: 16, childAlign: TextAnchor.MiddleCenter);
            UiFactory.SetHeight(buttonRow, 52);
            UiFactory.SetFlexible(buttonRow, 1, 0);
            _buttonGroup = buttonRow.gameObject.AddComponent<CanvasGroup>();
            _rematchButton = UiFactory.Button(buttonRow, "리매치", UITheme.Clay, Color.white,
                () => onRematch?.Invoke(), 17);
            UiFactory.SetSize(_rematchButton, 200, 48);
            MenuButtonStyle.ApplyChoice(_rematchButton, UITheme.StrikeFg);
            _rematchButton.gameObject.SetActive(false);
            var button = UiFactory.Button(buttonRow, "메인 메뉴로", UITheme.Accent, Color.white,
                () => onMainMenu?.Invoke(), 17);
            UiFactory.SetSize(button, 220, 48);
            MenuButtonStyle.ApplyChoice(button, UITheme.Accent);

            Root.gameObject.SetActive(false);
        }

        public void SetSummary(string text) => _summary.text = text;

        public void SetOpponentDigits(int[] digits)
        {
            if (!IsVisible || !Judge.IsValidSecret(digits)) return;
            _displayDigits = (int[])digits.Clone();
            _digitsUnavailable.gameObject.SetActive(false);
            _digits.Root.gameObject.SetActive(true);
            if (!_introComplete) return;
            _digits.ShowDigits(_displayDigits);
            for (int i = 0; i < Judge.Digits; i++) _digits.Slot(i).rectTransform.localScale = Vector3.one;
        }

        public void SetDigitsUnavailable()
        {
            if (!IsVisible || _displayDigits != null) return;
            _digits.Root.gameObject.SetActive(false);
            _digitsUnavailable.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (_routine != null) _host.StopCoroutine(_routine);
            if (_progressRoutine != null) _host.StopCoroutine(_progressRoutine);
            _routine = null;
            _progressRoutine = null;
            _progressSounds.Clear();
            GameAudio.StopProgressEffects();
            if (_resultVoiceActive)
            {
                _resultVoiceActive = false;
                GameAudio.StopVoice();
            }
            Root.gameObject.SetActive(false);
        }

        public void SetSettlement(MatchSettlement settlement)
        {
            if (!IsVisible || _settlementReceived || settlement == null) return;
            _settlementReceived = true;
            // Limit a busy settlement to a representative achievement and one unlock.
            if (settlement.LevelAfter > settlement.LevelBefore)
                _progressSounds.Add("SFX_Progress_LevelUp");
            else if (settlement.HeroLevelAfter > settlement.HeroLevelBefore)
                _progressSounds.Add("SFX_Progress_HeroLevelUp");
            else if (settlement.Completed.Count > 0 || settlement.HeroCompleted.Count > 0)
                _progressSounds.Add("SFX_Progress_ChallengeComplete");
            if (settlement.NewRewards.Count > 0) _progressSounds.Add("SFX_Progress_RewardUnlock");
            StartProgressSounds();
        }

        private void StartProgressSounds()
        {
            if (_introComplete && IsVisible && _progressSounds.Count > 0 && _progressRoutine == null)
                _progressRoutine = _host.StartCoroutine(PlayProgressSounds());
        }

        private IEnumerator PlayProgressSounds()
        {
            foreach (string name in _progressSounds)
            {
                if (!IsVisible) break;
                GameAudio.Play(name);
                yield return new WaitForSecondsRealtime(GameAudio.Duration(name) + 0.15f);
            }
            _progressSounds.Clear();
            _progressRoutine = null;
        }

        // Online results can reserve unknown cards until the post-match exchange completes.
        public void Show(bool won, string message, int[] digits, bool waitForDigits = false)
            => Present(won ? Outcome.Win : Outcome.Lose, false, message, digits, waitForDigits);

        // A draw: lettered title, and a 리매치 button next to 메인 메뉴로. Online, the rematch needs the
        // opponent's answer too (SetRematchStatus tells how that is going); against the AI it is instant.
        public void ShowDraw(string message, int[] digits, bool rematchOnline, bool waitForDigits = false)
            => Present(Outcome.Draw, rematchOnline, message, digits, waitForDigits);

        // Updates the rematch line and the button: waiting for the answer, declined, timed out...
        public void SetRematchStatus(string text, bool canRequest)
        {
            _rematchStatus.gameObject.SetActive(!string.IsNullOrEmpty(text));
            _rematchStatus.text = text;
            _rematchButton.interactable = canRequest;
        }

        private void Present(Outcome outcome, bool online, string message, int[] digits, bool waitForDigits)
        {
            Hide();
            _resultVoiceActive = true;
            _introComplete = false;
            _settlementReceived = false;
            bool draw = outcome == Outcome.Draw;
            bool won = outcome == Outcome.Win;

            _drawTitle.gameObject.SetActive(draw);
            _title.enabled = !draw;
            _title.sprite = won ? _winTitle : _loseTitle;
            _title.color = _title.sprite != null ? Color.white : Color.clear;
            _rematchButton.gameObject.SetActive(draw);
            _rematchButton.interactable = true;
            _rematchStatus.text = draw && online ? "리매치는 두 사람이 모두 수락해야 시작됩니다." : "";
            _rematchStatus.gameObject.SetActive(_rematchStatus.text.Length > 0);
            _message.text = message;
            _summary.text = "";
            _displayDigits = Judge.IsValidSecret(digits) ? (int[])digits.Clone() : null;
            bool hasDigits = _displayDigits != null || waitForDigits;
            _digitsUnavailable.gameObject.SetActive(false);
            _digits.Root.gameObject.SetActive(hasDigits);

            // starting pose (set before the first frame so nothing flashes)
            _dim.color = new Color(0f, 0f, 0f, 0f);
            _panelGroup.alpha = 0f;
            _panel.anchoredPosition = new Vector2(0f, PanelSinkPixels);
            _titleGroup.alpha = 0f;
            _titleRect.localScale = Vector3.one * 1.3f;
            _messageGroup.alpha = 0f;
            if (hasDigits) _digits.ShowAllUnknown();
            _buttonGroup.alpha = 0f;
            _buttonGroup.interactable = false;
            _buttonGroup.blocksRaycasts = false;

            Root.gameObject.SetActive(true);
            Root.SetAsLastSibling();
            GameAudio.Play("SFX_Result_Dim_Drop");
            _routine = _host.StartCoroutine(PlayIntro(outcome, hasDigits));
        }

        private IEnumerator PlayIntro(Outcome outcome, bool hasDigits)
        {
            float stampAt = outcome == Outcome.Lose ? StampDelayLose : StampDelayWin;
            float shakeAmp = outcome == Outcome.Lose ? ShakePixelsLose : ShakePixelsWin;
            float messageAt = stampAt + 0.45f;
            float cardsAt = messageAt + 0.5f;
            float buttonAt = hasDigits ? cardsAt + 3 * CardStep + CardFlipDuration + 0.1f : cardsAt;
            float total = buttonAt + ButtonFadeDuration;

            float t = 0f;
            bool stampPlayed = false;
            int nextCardSound = 0;
            while (t < total)
            {
                if (!IsVisible)
                {
                    _routine = null;
                    yield break;
                }
                t += Time.unscaledDeltaTime;
                if (!stampPlayed && t >= stampAt + StampDuration)
                {
                    stampPlayed = true;
                    GameAudio.Play(outcome == Outcome.Draw ? "SFX_Result_Stamp_Draw" :
                        outcome == Outcome.Win ? "SFX_Result_Stamp_Win" : "SFX_Result_Stamp_Lose");
                    if (_heroProvider != null)
                    {
                        var voiceCue = outcome == Outcome.Draw ? VoiceCue.Draw :
                            outcome == Outcome.Win ? VoiceCue.Win : VoiceCue.Lose;
                        GameAudio.PlayVoice(_heroProvider(), voiceCue, 0.22f);
                    }
                }
                if (hasDigits && _displayDigits != null && nextCardSound < 4 && t >= cardsAt + nextCardSound * CardStep)
                {
                    GameAudio.Play("SFX_Result_Card_Reveal");
                    nextCardSound++;
                }

                _dim.color = new Color(0f, 0f, 0f, DimAlpha * EaseOut(Seg(t, 0f, DimDuration)));

                float panelP = EaseOut(Seg(t, PanelDelay, PanelDuration));
                float shakeT = t - (stampAt + StampDuration);
                Vector2 shake = (shakeT >= 0f && shakeT < ShakeDuration)
                    ? UnityEngine.Random.insideUnitCircle * shakeAmp * (1f - shakeT / ShakeDuration)
                    : Vector2.zero;
                _panelGroup.alpha = panelP;
                _panel.anchoredPosition = new Vector2(0f, Mathf.Lerp(PanelSinkPixels, 0f, panelP)) + shake;

                float s = Seg(t, stampAt, StampDuration);
                _titleGroup.alpha = Mathf.Clamp01(s * 3f);
                _titleRect.localScale = Vector3.one * Mathf.Lerp(1.3f, 1f, s * s);   // ease-in: it accelerates into the panel

                _messageGroup.alpha = Seg(t, messageAt, MessageFadeDuration);

                if (hasDigits)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        float f = Seg(t, cardsAt + i * CardStep, CardFlipDuration);
                        var slot = _digits.Slot(i);
                        slot.sprite = f < 0.5f || _displayDigits == null ? CardArt.Unknown() : CardArt.Digit(_displayDigits[i]);
                        slot.rectTransform.localScale = _displayDigits == null ? Vector3.one :
                            new Vector3(Mathf.Abs(1f - 2f * f), 1f, 1f);
                    }
                }

                float b = Seg(t, buttonAt, ButtonFadeDuration);
                _buttonGroup.alpha = b;
                bool clickable = b > 0.5f;
                _buttonGroup.interactable = clickable;
                _buttonGroup.blocksRaycasts = clickable;

                yield return null;
            }

            // final pose
            _dim.color = new Color(0f, 0f, 0f, DimAlpha);
            _panelGroup.alpha = 1f;
            _panel.anchoredPosition = Vector2.zero;
            _titleGroup.alpha = 1f;
            _titleRect.localScale = Vector3.one;
            _messageGroup.alpha = 1f;
            if (hasDigits)
            {
                if (_displayDigits != null) _digits.ShowDigits(_displayDigits);
                for (int i = 0; i < 4; i++) _digits.Slot(i).rectTransform.localScale = Vector3.one;
            }
            _buttonGroup.alpha = 1f;
            _buttonGroup.interactable = true;
            _buttonGroup.blocksRaycasts = true;
            _routine = null;
            _introComplete = true;
            StartProgressSounds();
        }

        private static float Seg(float t, float start, float duration)
            => Mathf.Clamp01((t - start) / duration);

        private static float EaseOut(float p)
        {
            float q = 1f - p;
            return 1f - q * q * q;
        }
    }
}
