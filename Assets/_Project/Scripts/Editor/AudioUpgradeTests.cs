using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LiarsBatting.AI;
using LiarsBatting.Core;
using LiarsBatting.Network;
using LiarsBatting.Presentation;
using LiarsBatting.Progression;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace LiarsBatting.Editor
{
    public static class AudioUpgradeTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly List<string> Heard = new List<string>();
        public static MemoryFirestore Database { get; private set; }
        public static int Assertions { get; private set; }

        public static void ConfigureOffline()
        {
            GameBootstrap.EditorFirestoreFactory = host => Database = new MemoryFirestore(host);
        }

        public static IEnumerator Run(GameBootstrap game, GameAudio audio)
        {
            Assertions = 0;
            GameAudio.EditorEffectPlayed += Record;
            try
            {
                var catalog = Resources.Load<AudioCatalog>("AudioCatalog");
                Check(catalog.Clips.Length == 57, "Expected all 57 old and new clips.");
                Check(AudioCatalog.RequiredNames.All(name => catalog.Clips.Any(clip => clip.name == name)),
                    "A required cue is missing.");
                Check(catalog.Clips.Select(clip => clip.name).Distinct().Count() == catalog.Clips.Length,
                    "Duplicate clip names.");
                Check(catalog.Clips.All(clip => clip.frequency == 48000 && clip.length > 0f),
                    "An imported clip is empty or has an unexpected sample rate.");
                foreach (var clip in catalog.Clips)
                {
                    string path = AssetDatabase.GetAssetPath(clip);
                    var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                    var settings = importer.defaultSampleSettings;
                    bool streaming = path.Contains("/BGM/") && !path.Contains("_Stinger");
                    Check(settings.loadType == (streaming ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad) &&
                        settings.compressionFormat == (streaming ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM) &&
                        settings.preloadAudioData == !streaming && importer.loadInBackground == streaming &&
                        !importer.forceToMono, "Unexpected loading policy: " + clip.name);
                }

                GameAudio.StopEffects();
                yield return new WaitForSecondsRealtime(0.1f);
                Heard.Clear();
                GameAudio.Play("SFX_Hero_Wizard_Clairvoyance");
                var hero = Sources(audio).First(source => source.clip != null &&
                    source.clip.name == "SFX_Hero_Wizard_Clairvoyance");
                foreach (string cue in new[] { "SFX_UI_Click", "SFX_Card_Select", "SFX_Card_Deselect",
                    "SFX_UI_Hover", "SFX_Card_Hover", "SFX_Timer_Tick_Urgent", "SFX_Trust_Accept" })
                    GameAudio.Play(cue);
                Check(hero.isPlaying && hero.clip.name == "SFX_Hero_Wizard_Clairvoyance",
                    "UI or timer requests stole the hero voice.");
                Check(Heard.Count(name => name == "SFX_Hero_Wizard_Clairvoyance") == 1,
                    "Hero playback duplicated.");
                GameAudio.Play("SFX_UI_Error");
                GameAudio.Play("SFX_UI_Error");
                Check(Heard.Count(name => name == "SFX_UI_Error") == 1, "Error cooldown failed.");
                GameAudio.StopEffects();
                yield return new WaitForSecondsRealtime(0.8f);
                Check(Mathf.Approximately(Sources(audio)[0].volume,
                    Get<float>(audio, "_musicGain") * GameAudio.MusicLevel * (GameAudio.MusicMuted ? 0f : 1f)),
                    "Ducking remained after voices were stopped.");

                GameAudio.StartMatchMusic();
                GameAudio.ReportResponse(3);
                Check(Music(audio) == "BGM_Match_Climax", "Reported 3S did not enter climax.");
                GameAudio.ContinueMatchMusic();
                Check(Music(audio) == "BGM_Match_Climax", "A turn update reset climax too soon.");
                Set(audio, "_climaxUntil", Time.unscaledTime - 0.01f);
                yield return null;
                Check(Music(audio) == "BGM_Match_Tension", "Climax hold did not expire.");
                Heard.Clear();
                GameAudio.BeginFinalChance();
                GameAudio.BeginFinalChance();
                GameAudio.ContinueMatchMusic();
                GameAudio.ReportResponse(3);
                Check(Music(audio) == "BGM_Match_FinalChance", "Final chance was overwritten.");
                Check(Heard.Count(name => name == "SFX_LastGuess_Start") == 1,
                    "Last-guess entry was replayed.");
                yield return new WaitForSecondsRealtime(0.8f);
                Check(Sources(audio)[0].isPlaying && Sources(audio)[0].clip.name == "BGM_Match_FinalChance" &&
                    Sources(audio)[0].loop, "Final-chance loop never started.");

                ResetGame(game);
                var timer = Get<CountdownTimerView>(game, "_timer");
                Heard.Clear();
                timer.StartSpectating(0.15f);
                yield return new WaitForSecondsRealtime(0.25f);
                Check(!Heard.Any(name => name.StartsWith("SFX_Timer_")), "Spectator clock emitted a warning.");
                timer.Start(1f, () => { });
                Check(Heard.Contains("SFX_Timer_Tick_Urgent"), "Local clock did not tick.");
                timer.Stop();
                Check(!Sources(audio).Any(source => source.clip != null &&
                    source.clip.name == "SFX_Timer_Tick_Urgent" && source.isPlaying), "Cancelled tick kept playing.");

                Call(game, "StartAiMatch", AiDifficulty.Beginner);
                Get<GameState>(game, "_state").AiHero = HeroId.DemonHunter;
                Heard.Clear();
                Call(game, "OnHeroChosen", HeroId.Paladin);
                Call(game, "OnHeroChosen", HeroId.Wizard);
                Call(game, "ShowSetupScreen");
                Check(Heard.Count(name => name == "SFX_Hero_Select_Confirm") == 1 &&
                    Get<GameState>(game, "_state").PlayerHero == HeroId.Paladin, "Hero confirmation repeated.");
                Check(Heard.Count(name => name == "SFX_Hero_DemonHunter_Seal") == 1, "Seal announcement repeated.");
                Call(game, "OnSecretChosen", new[] { 0, 1, 2, 3 });
                Call(game, "OnSecretChosen", new[] { 4, 5, 6, 7 });
                Check(Heard.Count(name => name == "SFX_Secret_Lock") == 1 &&
                    Get<GameState>(game, "_state").PlayerSecret.SequenceEqual(new[] { 0, 1, 2, 3 }),
                    "Secret confirmation repeated.");
                Check(Heard.Count(name => name == "SFX_Match_Start") == 1 &&
                    !Heard.Contains("SFX_UI_Click"), "Match start or confirmation doubled generic clicks.");
                ResetGame(game);
                StartAi(game, HeroId.Warrior);
                Heard.Clear();
                Call(game, "UseWarriorAbility");
                Check(Heard.Contains("SFX_Hero_Warrior_Rush") && !Heard.Contains("SFX_Warrior_ExtraTurn"),
                    "Reserved Warrior ability announced an extra turn too early.");
                Call(game, "EndMyAttackTurnAndPassToOpponent");
                Check(Heard.Count(name => name == "SFX_Warrior_ExtraTurn") == 1 &&
                    Get<MatchStats>(game, "_stats").OnExtraTurn, "Actual Warrior extra turn was silent.");
                ResetGame(game);
                yield return new WaitForSecondsRealtime(0.1f);

                StartAi(game, HeroId.Wizard);
                var state = Get<GameState>(game, "_state");
                Heard.Clear();
                Call(game, "RefreshRevealedRows");
                Call(game, "RefreshRevealedRows");
                Check(!Heard.Contains("SFX_Digit_Reveal"), "A UI refresh emitted reveal audio.");
                Call(game, "UseWizardAbility");
                Check(Heard.Contains("SFX_Hero_Wizard_Clairvoyance") && Heard.Contains("SFX_Digit_Reveal"),
                    "Wizard activation or reveal cue missing.");
                Check(state.PlayerRevealed.Count(value => value) == 1 &&
                    state.AiRevealed.Count(value => value) == 1, "Wizard reveal rules changed.");

                ResetGame(game);
                yield return new WaitForSecondsRealtime(0.1f);
                StartAi(game, HeroId.Paladin);
                Heard.Clear();
                var guess = DifferentGuess(Get<GameState>(game, "_state").AiSecret);
                var picker = Get<CardPickerView>(game, "_attackPicker");
                foreach (int digit in guess) Call(picker, "PlaceDigit", digit);
                Call(picker, "Submit");
                Check(Heard.Count(name => name == "SFX_Guess_Submit") == 1, "Manual submission did not sound once.");
                ResetGame(game);
                yield return new WaitForSecondsRealtime(0.1f);
                StartAi(game, HeroId.Paladin);
                Heard.Clear();
                Call(game, "OnPlayerGuessSubmitted", DifferentGuess(Get<GameState>(game, "_state").AiSecret));
                Check(Heard.Count(name => name == "SFX_Guess_Submit") == 1, "Automatic submission did not sound once.");
                ResetGame(game);
                yield return new WaitForSecondsRealtime(0.1f);
                StartAi(game, HeroId.Paladin);
                var overlay = Get<ChoiceOverlayView>(game, "_choiceOverlay");
                Heard.Clear();
                Call(game, "PromptTrustOrChallenge", new JudgeResult(1, 1), (bool?)false,
                    (Action)(() => { }), (Action)(() => { }));
                var choiceRoot = Get<GameObject>(overlay, "_root");
                choiceRoot.GetComponentsInChildren<UnityEngine.UI.Button>()
                    .First(button => button.name == "Button_믿는다").onClick.Invoke();
                yield return new WaitForSecondsRealtime(0.6f);
                Check(!Heard.Any(name => name.StartsWith("SFX_Judge_")), "A stale delayed judgement played.");
                Check(Heard.Count(name => name == "SFX_Trust_Accept") == 1, "Trust feedback duplicated.");

                ResetGame(game);
                Heard.Clear();
                Call(game, "PromptTrustOrChallenge", new JudgeResult(3, 0), (bool?)false,
                    (Action)(() => { }), (Action)(() => { }));
                yield return new WaitForSecondsRealtime(0.6f);
                string[] honestPublicCues = Heard.ToArray();
                ResetGame(game);
                Heard.Clear();
                Call(game, "PromptTrustOrChallenge", new JudgeResult(3, 0), (bool?)true,
                    (Action)(() => { }), (Action)(() => { }));
                yield return new WaitForSecondsRealtime(0.6f);
                Check(honestPublicCues.SequenceEqual(Heard) && Music(audio) == "BGM_Match_Climax",
                    "The same public report leaked hidden truth through audio.");
                ResetGame(game);
                Heard.Clear();
                Call(game, "ShowPriestAnswer", 0, 1, true, (Action)(() => { }));
                Call(game, "ShowPriestAnswer", 0, 1, false, (Action)(() => { }));
                Check(Heard.Contains("SFX_Priest_Answer_Yes") && Heard.Contains("SFX_Priest_Answer_No"),
                    "Priest answer cues missing.");
                overlay.Hide();
                GameAudio.StopEffects();

                StartAi(game, HeroId.Paladin);
                var network = new NetworkMatchController(Database, game, "audio-test", MatchSide.A, "audio", "other");
                Set(game, "_network", network);
                Set(game, "_isOnlineMatch", true);
                Call(game, "WireNetworkEvents");
                Heard.Clear();
                Set(network, "_warriorKeepTurn", true);
                Raise(network, "OnOpponentGuess", new[] { 4, 5, 6, 7 });
                Check(!Heard.Contains("SFX_Warrior_ExtraTurn"), "Opponent's reservation was announced early.");
                Set(network, "_warriorKeepTurn", false);
                Raise(network, "OnOpponentGuess", new[] { 4, 5, 6, 8 });
                Check(Heard.Count(name => name == "SFX_Warrior_ExtraTurn") == 1,
                    "Fast opponent follow-up lost its extra-turn cue.");
                timer.Stop();
                overlay.Hide();
                Set(game, "_defendingNow", false);
                var networkPopup = Get<ResultPopupView>(game, "_resultPopup");
                networkPopup.ShowDraw("request test", null, true);
                Set(game, "_rematchAnswered", false);
                Heard.Clear();
                Raise(network, "OnRematchRequestedByOpponent");
                Check(Heard.Count(name => name == "SFX_Rematch_Requested") == 1,
                    "Opponent rematch request was silent.");
                networkPopup.Hide();
                yield return new WaitForSecondsRealtime(0.1f);
                Heard.Clear();
                Raise(network, "OnRematchRequestedByOpponent");
                Check(!Heard.Contains("SFX_Rematch_Requested"), "A hidden result emitted a rematch notification.");
                Heard.Clear();
                Raise(network, "OnOpponentPriestQuery", 0, 4);
                Check(Heard.Contains("SFX_Hero_Priest_Inquiry"), "Online opponent Priest was silent.");
                timer.Stop();
                overlay.Hide();
                Heard.Clear();
                Raise(network, "OnOpponentWizardReveal", 0, 4);
                Raise(network, "OnMustRevealForWizard");
                Check(Heard.Count(name => name == "SFX_Hero_Wizard_Clairvoyance") == 1,
                    "Online opponent Wizard activation was missing or duplicated.");
                yield return new WaitForSecondsRealtime(0.1f);
                Heard.Clear();
                Set(game, "_wizardAwaitingReply", true);
                Raise(network, "OnOpponentWizardReveal", 1, 5);
                Check(!Heard.Contains("SFX_Hero_Wizard_Clairvoyance"),
                    "A Wizard reply incorrectly replayed activation.");
                Set(game, "_awaitingMyChallengeOutcome", false);
                Heard.Clear();
                Raise(network, "OnOpponentRevealed", MatchSide.B, 2, 6);
                Raise(network, "OnOpponentRevealed", MatchSide.B, 2, 6);
                Check(Heard.Count(name => name == "SFX_Digit_Reveal") <= 1,
                    "Duplicate network reveal emitted duplicate audio.");
                Set(game, "_isOnlineMatch", false);
                Set(game, "_network", null);
                ResetGame(game);

                StartAi(game, HeroId.Paladin);
                Heard.Clear();
                Call(game, "BeginPlayerLastGuess");
                Check(Music(audio) == "BGM_Match_FinalChance", "Last-guess flow chose the wrong BGM.");
                Check(Heard.Count(name => name == "SFX_LastGuess_Start") == 1,
                    "Last-guess flow did not emit one entry sound.");
                Check(!Heard.Any(name => name.Contains("Stamp_Win")), "A provisional correct guess celebrated a win.");
                ResetGame(game);

                StartAi(game, HeroId.Paladin);
                Heard.Clear();
                Call(game, "EndGameDraw", "audio test draw");
                Check(Music(audio) == "BGM_Draw_Stinger", "Draw flow used lobby or win BGM.");
                yield return new WaitForSecondsRealtime(1.25f);
                Check(Heard.Count(name => name == "SFX_Result_Stamp_Draw") == 1 &&
                    !Heard.Contains("SFX_Result_Stamp_Win") && !Heard.Contains("SFX_Result_Stamp_Lose"),
                    "Draw stamp is not neutral.");
                var popup = Get<ResultPopupView>(game, "_resultPopup");
                popup.Hide();
                GameAudio.StartMatchMusic();
                GameAudio.ResultMusic(true);
                popup.Show(true, "no known opponent cards", null);
                Heard.Clear();
                yield return new WaitForSecondsRealtime(2.2f);
                Check(!Heard.Contains("SFX_Result_Card_Reveal"), "Unknown online cards emitted flip sounds.");
                Check(Heard.Contains("SFX_Result_Stamp_Win"), "Win stamp missing.");
                popup.Hide();
                GameAudio.StartMatchMusic();
                GameAudio.ResultMusic(false);
                popup.Show(false, "defeat", null);
                Heard.Clear();
                yield return new WaitForSecondsRealtime(1.3f);
                Check(Heard.Contains("SFX_Result_Stamp_Lose") && Music(audio) == "BGM_Lose_Stinger",
                    "Defeat feedback is wrong.");

                popup.Hide();
                popup.ShowDraw("growth queue", null, false);
                var settlement = new MatchSettlement { LevelBefore = 1, LevelAfter = 2,
                    HeroLevelBefore = 1, HeroLevelAfter = 2 };
                settlement.Completed.Add("challenge");
                settlement.NewRewards.Add(new RewardInfo("test", RewardKind.Title, "test reward", 2));
                Heard.Clear();
                popup.SetSettlement(settlement);
                popup.SetSettlement(settlement);
                Check(!Heard.Any(name => name.StartsWith("SFX_Progress_")), "Growth played before the intro ended.");
                yield return new WaitForSecondsRealtime(2.05f);
                Check(Heard.Count(name => name == "SFX_Progress_LevelUp") == 1,
                    "Level-up queue did not start once after intro.");
                Check(!Heard.Contains("SFX_Progress_HeroLevelUp") &&
                    !Heard.Contains("SFX_Progress_ChallengeComplete"), "Busy growth queue was not coalesced.");
                yield return new WaitForSecondsRealtime(1.5f);
                Check(Heard.Count(name => name == "SFX_Progress_RewardUnlock") == 1, "Unlock was not queued once.");
                popup.Hide();
                Check(!Sources(audio).Any(source => source.isPlaying && source.clip != null &&
                    source.clip.name.StartsWith("SFX_Progress_")), "Leaving the popup did not stop reward audio.");
                Heard.Clear();
                popup.SetSettlement(settlement);
                yield return new WaitForSecondsRealtime(0.2f);
                Check(!Heard.Any(), "A hidden result accepted a late settlement.");

                popup.ShowDraw("no growth", null, false);
                popup.SetSettlement(null);
                popup.SetSettlement(new MatchSettlement { LevelBefore = 1, LevelAfter = 1,
                    HeroLevelBefore = 1, HeroLevelAfter = 1 });
                Heard.Clear();
                yield return new WaitForSecondsRealtime(2.05f);
                Check(!Heard.Any(name => name.StartsWith("SFX_Progress_")), "No-growth settlement was celebrated.");
                popup.Hide();
                popup.ShowDraw("mastery", null, false);
                popup.SetSettlement(new MatchSettlement { HeroLevelBefore = 1, HeroLevelAfter = 2 });
                Heard.Clear();
                yield return new WaitForSecondsRealtime(2.05f);
                Check(Heard.Contains("SFX_Progress_HeroLevelUp"), "Hero mastery cue missing.");
                popup.Hide();
                popup.ShowDraw("challenge", null, false);
                var completed = new MatchSettlement();
                completed.HeroCompleted.Add("hero challenge");
                popup.SetSettlement(completed);
                Heard.Clear();
                yield return new WaitForSecondsRealtime(2.05f);
                Check(Heard.Contains("SFX_Progress_ChallengeComplete"), "Challenge cue missing.");
                popup.Hide();

                bool saved = false;
                bool failed = false;
                var store = new ProfileStore(Database);
                Database.HoldProfileWrites = true;
                store.SettleMatch("settlement-test", new MatchStats { Hero = HeroId.Paladin },
                    value => saved = true, error => failed = true);
                yield return new WaitForSecondsRealtime(0.1f);
                Check(!saved && !failed && Database.HeldProfileWrites.Count == 1,
                    "Settlement reported success before the save completed.");
                Database.ReleaseProfileWrites(false);
                yield return new WaitForSecondsRealtime(0.1f);
                Check(!saved && failed && store.Current == null, "Failed save was celebrated or kept stale progress.");
                Database.HoldProfileWrites = false;

                Call(game, "ShowProfileScreen");
                yield return new WaitForSecondsRealtime(0.15f);
                Database.HoldProfileWrites = true;
                Heard.Clear();
                var defaultCards = RewardCatalog.Find(RewardCatalog.DefaultCards);
                Call(game, "OnCosmeticEquipRequested", defaultCards);
                yield return new WaitForSecondsRealtime(0.1f);
                Check(!Heard.Contains("SFX_Cosmetic_Equip") && Database.HeldProfileWrites.Count == 1,
                    "Equip success played before saving.");
                Database.ReleaseProfileWrites(true);
                Check(Heard.Count(name => name == "SFX_Cosmetic_Equip") == 1, "Saved equip was silent.");
                yield return new WaitForSecondsRealtime(2.1f);
                Heard.Clear();
                Call(game, "OnCosmeticEquipRequested", defaultCards);
                Database.ReleaseProfileWrites(false);
                Check(!Heard.Contains("SFX_Cosmetic_Equip") && Heard.Contains("SFX_UI_Error"),
                    "Failed equip used success feedback or lacked visible error feedback.");
                Database.HoldProfileWrites = false;

                ResetGame(game);
                StartAi(game, HeroId.Paladin);
                Database.HoldProfileWrites = true;
                Call(game, "EndGameDraw", "late settlement");
                yield return new WaitForSecondsRealtime(0.1f);
                Check(Database.HeldProfileWrites.Count == 1, "Expected one pending settlement.");
                Call(game, "ShowMainMenu");
                Heard.Clear();
                Database.ReleaseProfileWrites(true);
                yield return new WaitForSecondsRealtime(0.15f);
                Check(!Heard.Any(name => name.StartsWith("SFX_Progress_")),
                    "A late settlement played over the main menu.");
                Database.HoldProfileWrites = false;

                ResetGame(game);
                StartAi(game, HeroId.Paladin);
                Call(game, "EndGameDraw", "rematch");
                Heard.Clear();
                Call(game, "OnRematchClicked");
                Check(Heard.Count(name => name == "SFX_Rematch_Ready") == 1 &&
                    !Heard.Contains("SFX_Match_Found"), "Rematch readiness duplicated matchmaking audio.");
                Check(Music(audio) == "BGM_Lobby_Noir", "Rematch preparation kept the result music.");
                ResetGame(game);
                Check(Database.Requests > 0, "Offline backend was not exercised.");
                Debug.Log($"Audio upgrade integration checks passed: {Assertions} assertions; " +
                    $"{Database.Requests} in-memory requests, zero live Firebase requests.");
            }
            finally
            {
                GameAudio.EditorEffectPlayed -= Record;
            }
        }

        private static void StartAi(GameBootstrap game, HeroId hero)
        {
            Call(game, "StartAiMatch", AiDifficulty.Beginner);
            var state = Get<GameState>(game, "_state");
            state.AiHero = HeroId.Hunter;
            Call(game, "OnHeroChosen", hero);
            Call(game, "OnSecretChosen", new[] { 0, 1, 2, 3 });
            ResetGame(game);
            Set(game, "_myTurnActive", true);
        }

        private static void ResetGame(GameBootstrap game)
        {
            game.StopAllCoroutines();
            Get<CountdownTimerView>(game, "_timer").Stop();
            Get<ChoiceOverlayView>(game, "_choiceOverlay").Hide();
            Call(game, "CancelTurnAnnouncement");
            GameAudio.StopEffects();
        }

        private static int[] DifferentGuess(int[] secret)
            => secret.SequenceEqual(new[] { 4, 5, 6, 7 }) ? new[] { 4, 5, 6, 8 } : new[] { 4, 5, 6, 7 };
        private static void Record(string name) => Heard.Add(name);
        private static AudioSource[] Sources(GameAudio audio) => audio.GetComponents<AudioSource>();
        private static string Music(GameAudio audio) => Get<string>(audio, "_musicName");
        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        private static void Call(object target, string name, params object[] args)
            => target.GetType().GetMethod(name, Private).Invoke(target, args);
        private static void Raise(object target, string name, params object[] args)
            => ((Delegate)target.GetType().GetField(name, Private).GetValue(target))?.DynamicInvoke(args);
        private static void Check(bool condition, string message)
        {
            Assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }

    // All test reads and writes stay in memory, including startup profile/invite requests.
    public sealed class MemoryFirestore : FirestoreClient
    {
        private readonly MonoBehaviour _host;
        private readonly Dictionary<string, JObject> _documents = new Dictionary<string, JObject>();
        public readonly List<(Action success, Action<string> failure)> HeldProfileWrites =
            new List<(Action, Action<string>)>();
        public bool HoldProfileWrites;
        public bool RejectResultSecretWrites;
        public int ResultSecretWrites { get; private set; }
        public int Requests { get; private set; }

        public MemoryFirestore(MonoBehaviour host) : base(host) => _host = host;

        public override void GetDocument(string path, Action<JObject> onSuccess, Action<string> onError)
            => Later(() => onSuccess?.Invoke(_documents.TryGetValue(path, out var doc) ? (JObject)doc.DeepClone() :
                path.StartsWith("players/") ? new JObject { ["fields"] = new JObject() } : null));
        public override void ListCollection(string path, Action<JArray> onSuccess, Action<string> onError)
            => Later(() => onSuccess?.Invoke(new JArray()));
        public override void SetDocument(string path, JObject fields, Action onSuccess, Action<string> onError)
            => Later(() => { _documents[path] = new JObject { ["fields"] = fields.DeepClone() }; onSuccess?.Invoke(); });
        public override void CreateDocument(string collection, string docId, JObject fields,
            Action onSuccess, Action onExists, Action<string> onError)
            => SetDocument(collection + "/" + docId, fields, onSuccess, onError);
        public override void DeleteDocument(string path, Action onSuccess, Action<string> onError)
            => Later(() => { _documents.Remove(path); onSuccess?.Invoke(); });
        public override void PatchFields(string path, JObject fields, string[] fieldPaths,
            Action onSuccess, Action<string> onError)
        {
            void Commit()
            {
                if (!_documents.TryGetValue(path, out var doc))
                    _documents[path] = doc = new JObject { ["fields"] = new JObject() };
                var target = (JObject)doc["fields"];
                foreach (string field in fieldPaths) target[field] = fields[field]?.DeepClone();
                onSuccess?.Invoke();
            }
            Requests++;
            if (fieldPaths.Any(field => field == "resultSecretA" || field == "resultSecretB"))
            {
                ResultSecretWrites++;
                bool finished = _documents.TryGetValue(path, out var match) &&
                    (string)match["fields"]?["status"]?["stringValue"] == "finished";
                if (RejectResultSecretWrites || !finished)
                {
                    _host.StartCoroutine(Deferred(() => onError?.Invoke("simulated result-secret permission denied")));
                    return;
                }
            }
            if (HoldProfileWrites && path.StartsWith("players/"))
                HeldProfileWrites.Add((Commit, onError));
            else
                _host.StartCoroutine(Deferred(Commit));
        }
        public void ReleaseProfileWrites(bool success)
        {
            var pending = HeldProfileWrites.ToArray();
            HeldProfileWrites.Clear();
            foreach (var item in pending)
                if (success) item.success?.Invoke(); else item.failure?.Invoke("simulated save failure");
        }
        private void Later(Action action)
        {
            Requests++;
            _host.StartCoroutine(Deferred(action));
        }
        private static IEnumerator Deferred(Action action)
        {
            yield return null;
            action();
        }
    }
}
