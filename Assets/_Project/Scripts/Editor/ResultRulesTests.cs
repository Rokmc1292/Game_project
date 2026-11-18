using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using LiarsBatting.AI;
using LiarsBatting.Core;
using LiarsBatting.Network;
using LiarsBatting.Presentation;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace LiarsBatting.Editor
{
    public static class ResultRulesTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int _assertions;

        public static IEnumerator Run(GameBootstrap game)
        {
            _assertions = 0;
            var canvas = UiFactory.CreateCanvas("ResultRulesTestCanvas");
            var readOnly = ResultLamps.Build(canvas.transform, new JudgeResult(2, 1));
            Check(LampCount(readOnly, "Row_볼") == 4, "Ball row must retain four lamps.");
            Check(LampCount(readOnly, "Row_스트라이크") == 3, "Strike row must have three lamps.");
            var solved = ResultLamps.Build(canvas.transform, new JudgeResult(4, 0));
            Check(solved.GetComponent<Text>()?.text == "정답" &&
                solved.GetComponentsInChildren<Image>().Length == 0, "4S must not look like 3S.");
            Check(Judge.Evaluate(new[] { 0, 1, 2, 3 }, new[] { 0, 1, 2, 3 }).IsWin,
                "Display changes altered the four-digit win rule.");

            JudgeResult selected = default;
            var picker = ResultLamps.BuildPicker(canvas.transform, new JudgeResult(0, 0), 24f,
                result => selected = result);
            var strikeButtons = picker.Find("Row_스트라이크").GetComponentsInChildren<Button>();
            Check(strikeButtons.Length == 3, "Picker still allows a fourth strike lamp.");
            strikeButtons[2].onClick.Invoke();
            Check(selected.Strike == 3 && selected.Ball == 0, "Third strike cannot be selected.");
            strikeButtons[2].onClick.Invoke();
            Check(selected.Strike == 2, "Click-to-reduce strike behavior regressed.");
            strikeButtons[1].onClick.Invoke();
            strikeButtons[0].onClick.Invoke();
            picker.Find("Row_볼").GetComponentsInChildren<Button>()[3].onClick.Invoke();
            Check(selected.Ball == 4 && selected.Strike <= 3, "Fourth ball cannot be selected.");
            var clamped = ResultLamps.BuildPicker(canvas.transform, new JudgeResult(4, 0), 24f,
                result => selected = result);
            clamped.Find("Row_볼").GetComponentsInChildren<Button>()[0].onClick.Invoke();
            Check(selected.Strike == 3 && !selected.IsWin, "Picker retained an invisible fourth strike.");
            UnityEngine.Object.Destroy(canvas.gameObject);

            Call(game, "ShowMainMenu");
            var rulebook = Get<RulebookView>(game, "_rulebook");
            rulebook.Show();
            yield return null;
            Canvas.ForceUpdateCanvases();
            var body = Get<RectTransform>(rulebook, "_body");
            var examples = body.GetComponentsInChildren<RectTransform>()
                .Where(rect => rect.name == "Lamps").ToArray();
            Check(examples.Length == 4 && examples.All(rect => LampCount(rect, "Row_볼") == 4 &&
                LampCount(rect, "Row_스트라이크") == 3), "Rulebook illustrations were not updated.");
            var legend = body.GetComponentsInChildren<Text>().First(text => text.text.Contains("주황 스트라이크 3개"));
            Check(legend.preferredHeight <= legend.rectTransform.rect.height + 1f,
                "Rulebook legend overflows its reserved height.");
            Capture(body.GetComponentInParent<Canvas>(), "Logs/RulebookThreeStrikes.png");
            rulebook.Hide();

            StartAi(game);
            var state = Get<GameState>(game, "_state");
            JudgeResult hunterChoice = default;
            bool hunterUsedToken = false;
            Call(game, "PromptFakeLamps", new JudgeResult(4, 0), (Action<JudgeResult, bool>)((result, used) =>
            {
                hunterChoice = result;
                hunterUsedToken = used;
            }));
            var overlay = Get<ChoiceOverlayView>(game, "_choiceOverlay");
            var fakeSend = Get<GameObject>(overlay, "_root").GetComponentsInChildren<Button>()
                .First(button => button.name == "Button_이 결과로 전송");
            Check(fakeSend.interactable, "Hunter's default fake result is not synchronized with its three lamps.");
            fakeSend.onClick.Invoke();
            Check(hunterChoice.Equals(new JudgeResult(3, 0)) && hunterUsedToken,
                "Hunter sent an invisible 4S instead of the visible fake result.");
            state.AiSecret = new[] { 4, 5, 6, 7 };
            Call(game, "EndGame", false, "패배 결과 검수");
            var popup = Get<ResultPopupView>(game, "_resultPopup");
            Check(Get<int[]>(popup, "_displayDigits").SequenceEqual(state.AiSecret),
                "AI defeat still displays my own secret.");
            Check(!Get<int[]>(popup, "_displayDigits").SequenceEqual(state.PlayerSecret),
                "AI defeat substituted my secret.");
            yield return new WaitForSecondsRealtime(3.6f);
            var row = Get<RevealedDigitsRow>(popup, "_digits");
            Check(Enumerable.Range(0, 4).All(i => row.Slot(i).sprite == CardArt.Digit(state.AiSecret[i])),
                "AI defeat card sprites do not match the opponent.");
            Capture(popup.Root.GetComponentInParent<Canvas>(), "Logs/DefeatOpponentDigits.png");
            popup.Hide();

            var db = AudioUpgradeTests.Database;
            const string matchId = "result-rule-test";
            string path = "matches/" + matchId;
            var fields = new JObject
            {
                ["status"] = FirestoreClient.StringField("in_progress"),
                ["action"] = FirestoreClient.StringField("turn_done"),
                ["seq"] = FirestoreClient.IntField(42)
            };
            db.SetDocument(path, fields, null, null);
            yield return null;
            var a = new NetworkMatchController(db, game, matchId, MatchSide.A, "A", "B");
            var b = new NetworkMatchController(db, game, matchId, MatchSide.B, "B", "A");
            int[] receivedA = null, receivedB = null;
            int failures = 0;
            int writesBefore = db.ResultSecretWrites;
            int[] ownA = { 0, 1, 2, 3 };
            a.BeginResultReveal(ownA, value => receivedA = value, () => failures++);
            b.BeginResultReveal(new[] { 4, 5, 6, 7 }, value => receivedB = value, () => failures++);
            ownA[0] = 9;
            yield return new WaitForSecondsRealtime(0.8f);
            Check(db.ResultSecretWrites == writesBefore && receivedA == null && receivedB == null,
                "Full secrets were published or accepted during an active match.");
            var finished = new JObject
            {
                ["status"] = FirestoreClient.StringField("finished"),
                ["action"] = FirestoreClient.StringField("finished"),
                ["rematchA"] = FirestoreClient.StringField("yes")
            };
            db.PatchFields(path, finished, new[] { "status", "action", "rematchA" }, null, null);
            yield return new WaitForSecondsRealtime(1.6f);
            Check(receivedA != null && receivedA.SequenceEqual(new[] { 4, 5, 6, 7 }),
                "Side A did not receive side B's result digits.");
            Check(receivedB != null && receivedB.SequenceEqual(new[] { 0, 1, 2, 3 }),
                "Side B lost the leading zero or the local secret snapshot changed.");
            Check(failures == 0 && db.ResultSecretWrites == writesBefore + 2,
                "Result exchange failed or duplicated publishing.");
            JObject saved = null;
            db.GetDocument(path, value => saved = value, null);
            yield return new WaitForSecondsRealtime(0.1f);
            Check(FirestoreClient.ReadString(saved, "status", "") == "finished" &&
                FirestoreClient.ReadString(saved, "action", "") == "finished" &&
                FirestoreClient.ReadInt(saved, "seq", 0) == 42 &&
                FirestoreClient.ReadString(saved, "rematchA", "") == "yes",
                "Secret exchange overwrote the match result, sequence, or rematch vote.");

            var decode = typeof(NetworkMatchController).GetMethod("TryDecodeResultSecret",
                BindingFlags.Static | BindingFlags.NonPublic);
            foreach (string invalid in new[] { "", "123", "12345", "12a4", "0000", "-123" })
                Check(!(bool)decode.Invoke(null, new object[] { invalid, null }), "Accepted malformed secret: " + invalid);
            int cancelledCallbacks = 0;
            a.BeginResultReveal(new[] { 0, 1, 2, 3 }, value => cancelledCallbacks++, () => cancelledCallbacks++);
            a.Stop();
            yield return new WaitForSecondsRealtime(0.1f);
            Check(cancelledCallbacks == 0, "Stopped result exchange delivered a stale callback.");

            StartAi(game);
            state = Get<GameState>(game, "_state");
            state.AiSecret = new int[4];
            Set(game, "_network", a);
            Set(game, "_isOnlineMatch", true);
            Call(game, "EndOnlineGame", false, "온라인 패배 결과 검수");
            Check(Get<int[]>(popup, "_displayDigits") == null,
                "Online defeat displayed my secret or unknown digits as zeros.");
            yield return new WaitForSecondsRealtime(0.2f);
            Check(Get<int[]>(popup, "_displayDigits")?.SequenceEqual(new[] { 4, 5, 6, 7 }) == true,
                "Online defeat did not update to the opponent's digits.");
            Check(!state.AiRevealed.Any(value => value), "Result reveal changed in-match XP reveal counts.");
            yield return new WaitForSecondsRealtime(3.6f);
            Check(Enumerable.Range(0, 4).All(i => row.Slot(i).sprite == CardArt.Digit(i + 4)),
                "Online result intro restored stale cards.");
            popup.Hide();
            popup.SetOpponentDigits(new[] { 9, 8, 7, 6 });
            Check(Get<int[]>(popup, "_displayDigits").SequenceEqual(new[] { 4, 5, 6, 7 }),
                "A hidden result accepted a late secret.");
            a.Stop();
            b.Stop();
            Set(game, "_network", null);
            Set(game, "_isOnlineMatch", false);

            db.RejectResultSecretWrites = true;
            var denied = new NetworkMatchController(db, game, matchId, MatchSide.A, "A", "B");
            int deniedResults = 0, deniedFailures = 0;
            denied.BeginResultReveal(new[] { 0, 1, 2, 3 }, value => deniedResults++, () => deniedFailures++);
            yield return new WaitForSecondsRealtime(0.2f);
            Check(deniedResults == 0 && deniedFailures == 1, "Denied result exchange did not fail safely.");
            denied.Stop();
            db.RejectResultSecretWrites = false;
            popup.Show(false, "상대 연결 종료", null, waitForDigits: true);
            popup.SetDigitsUnavailable();
            Check(!row.Root.gameObject.activeSelf &&
                Get<Text>(popup, "_digitsUnavailable").gameObject.activeSelf,
                "Unavailable result fell back to fabricated or local digits.");
            popup.Hide();
            Call(game, "ShowMainMenu");
            Debug.Log($"Result and rule regression checks passed: {_assertions} assertions; no live server writes.");
        }

        private static int LampCount(Transform root, string row)
            => root.Find(row).GetComponentsInChildren<Image>().Count(image => image.name == "Lamp");

        private static void Capture(Canvas canvas, string path)
        {
            var previousMode = canvas.renderMode;
            var previousCamera = canvas.worldCamera;
            float previousDistance = canvas.planeDistance;
            var previousTarget = RenderTexture.active;
            var cameraObject = new GameObject("ResultRulesCaptureCamera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            var target = new RenderTexture(1280, 720, 24);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                Canvas.ForceUpdateCanvases();
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                Check(image.GetPixels32().Where((pixel, i) => i % 100 == 0).Distinct().Count() > 16,
                    "UI capture is blank: " + path);
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previousTarget;
                canvas.renderMode = previousMode;
                canvas.worldCamera = previousCamera;
                canvas.planeDistance = previousDistance;
                Canvas.ForceUpdateCanvases();
                target.Release();
                UnityEngine.Object.Destroy(target);
                UnityEngine.Object.Destroy(image);
                UnityEngine.Object.Destroy(cameraObject);
            }
        }

        private static void StartAi(GameBootstrap game)
        {
            Call(game, "StartAiMatch", AiDifficulty.Beginner);
            Get<GameState>(game, "_state").AiHero = HeroId.Hunter;
            Call(game, "OnHeroChosen", HeroId.Paladin);
            Call(game, "OnSecretChosen", new[] { 0, 1, 2, 3 });
            game.StopAllCoroutines();
            Get<CountdownTimerView>(game, "_timer").Stop();
            Get<ChoiceOverlayView>(game, "_choiceOverlay").Hide();
            Call(game, "CancelTurnAnnouncement");
            GameAudio.StopEffects();
        }

        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        private static void Call(object target, string name, params object[] args)
            => target.GetType().GetMethod(name, Private).Invoke(target, args);
        private static void Check(bool condition, string message)
        {
            _assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
