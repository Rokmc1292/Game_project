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
        private static readonly List<string> HeardVoices = new List<string>();
        public static MemoryFirestore Database { get; private set; }
        public static int Assertions { get; private set; }

        public static void ConfigureOffline()
        {
            GameBootstrap.EditorFirestoreFactory = host => Database = new MemoryFirestore(host);
        }

        public static IEnumerator Run(GameBootstrap game, GameAudio audio)
        {
            Assertions = 0;
            Heard.Clear();
            HeardVoices.Clear();
            GameAudio.EditorEffectPlayed += Record;
            GameAudio.EditorVoicePlayed += RecordVoice;
            try
            {
                var catalog = Resources.Load<AudioCatalog>("AudioCatalog");
                var nonVoiceClips = catalog.Clips.Where(clip => !clip.name.StartsWith("VO_", StringComparison.Ordinal)).ToArray();
                var voiceClips = catalog.Clips.Where(clip => clip.name.StartsWith("VO_", StringComparison.Ordinal)).ToArray();
                Check(nonVoiceClips.Length == 57, "Expected all 57 BGM and SFX clips.");
                Check(voiceClips.Length == 84, "Expected all 84 character VO clips (7 heroes x 12 cues).");
                Check(AudioCatalog.RequiredNames.All(name => nonVoiceClips.Any(clip => clip.name == name)),
                    "A required cue is missing.");
                Check(catalog.Clips.Select(clip => clip.name).Distinct().Count() == catalog.Clips.Length,
                    "Duplicate clip names.");
                Check(catalog.Clips.All(clip => clip.frequency == 48000 && clip.length > 0f),
                    "An imported clip is empty or has an unexpected sample rate.");
                Check(voiceClips.All(clip => clip.channels == 1),
                    "All character VO clips must be Mono (1 channel).");
                foreach (HeroId heroId in Enum.GetValues(typeof(HeroId)))
                {
                    foreach (VoiceCue voiceCue in Enum.GetValues(typeof(VoiceCue)))
                    {
                        string expectedVo = $"VO_{heroId}_{voiceCue}_01";
                        Check(voiceClips.Any(clip => clip.name == expectedVo),
                            "Missing character VO clip in catalog: " + expectedVo);
                    }
                }
                foreach (var clip in catalog.Clips)
                {
                    string path = AssetDatabase.GetAssetPath(clip);
                    var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                    var settings = importer.defaultSampleSettings;
                    bool streaming = path.Contains("/BGM/") && !path.Contains("_Stinger");
                    bool voiceAsset = path.Contains("/VO/");
                    var expectedLoadType = streaming ? AudioClipLoadType.Streaming
                        : voiceAsset ? AudioClipLoadType.CompressedInMemory
                        : AudioClipLoadType.DecompressOnLoad;
                    var expectedCompression = (streaming || voiceAsset)
                        ? AudioCompressionFormat.Vorbis
                        : AudioCompressionFormat.PCM;
                    Check(settings.loadType == expectedLoadType &&
                        settings.compressionFormat == expectedCompression &&
                        Mathf.Approximately(settings.quality, 0.85f) &&
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
                GameAudio.EditorVoicePlayed -= RecordVoice;
            }
        }

        public static IEnumerator RunVoice(GameBootstrap game, GameAudio audio)
        {
            int startAssertions = Assertions;
            Heard.Clear();
            HeardVoices.Clear();
            GameAudio.EditorEffectPlayed += Record;
            GameAudio.EditorVoicePlayed += RecordVoice;
            try
            {
                // 1. Dedicated _voice channel isolation, BGM 0.45f ducking, mute independence, and missing-clip safety
            GameAudio.StartMatchMusic();
            yield return new WaitForSecondsRealtime(0.45f);
            HeardVoices.Clear();
            GameAudio.PlayVoice(HeroId.DemonHunter, VoiceCue.Lose, 0f); // ~2.7s clip
            var voiceSrc = Sources(audio).Last();
            Check(voiceSrc.isPlaying && voiceSrc.clip != null && voiceSrc.clip.name == "VO_DemonHunter_Lose_01",
                "Dedicated voice source did not play VO_DemonHunter_Lose_01.");
            for (int i = 0; i < 16; i++)
            {
                GameAudio.Play("SFX_UI_Click");
                GameAudio.Play("SFX_Card_Select");
                GameAudio.Play("SFX_Trust_Accept");
                GameAudio.Play("SFX_Hero_Wizard_Clairvoyance");
            }
            Check(voiceSrc.isPlaying && voiceSrc.clip.name == "VO_DemonHunter_Lose_01",
                "Flooding SFX stole or interrupted the dedicated character voice source.");
            // Stop effects only to isolate voice ducking measurement, then replay voice cleanly
            GameAudio.StopEffects();
            GameAudio.PlayVoice(HeroId.DemonHunter, VoiceCue.Lose, 0f);
            yield return new WaitForSecondsRealtime(0.15f);
            float expectedDuckedMusic = Get<float>(audio, "_musicGain") * 0.45f * GameAudio.MusicLevel;
            Check(Mathf.Abs(Sources(audio)[0].volume - expectedDuckedMusic) < 0.005f,
                $"Voice playback did not duck BGM to 0.45f (got {Sources(audio)[0].volume}, expected {expectedDuckedMusic}).");
            // Muting voice must release BGM ducking immediately; unmuting restores 0.45f ducking
            bool prevVoiceMuted = GameAudio.VoiceMuted;
            GameAudio.SetVoiceMuted(true);
            float expectedUnduckedMusic = Get<float>(audio, "_musicGain") * GameAudio.MusicLevel;
            Check(Mathf.Approximately(Sources(audio)[0].volume, expectedUnduckedMusic) &&
                Mathf.Approximately(voiceSrc.volume, 0f),
                "Muting voice did not silence voice or release BGM ducking.");
            GameAudio.SetVoiceMuted(false);
            Check(Mathf.Abs(Sources(audio)[0].volume - expectedDuckedMusic) < 0.005f && voiceSrc.volume > 0f,
                "Unmuting voice did not restore voice volume and BGM ducking.");
            GameAudio.SetVoiceMuted(prevVoiceMuted);
            // Missing VO clip must skip silently without interrupting current voice or logging warnings
            GameAudio.PlayVoice((HeroId)999, VoiceCue.Select, 0f);
            GameAudio.PlayVoice((HeroId?)null, VoiceCue.Select, 0f);
            Check(voiceSrc.isPlaying && voiceSrc.clip.name == "VO_DemonHunter_Lose_01",
                "Missing/null voice cue interrupted active voice playback.");
            GameAudio.StopVoice();
            Check(!voiceSrc.isPlaying && Mathf.Approximately(Sources(audio)[0].volume, expectedUnduckedMusic),
                "StopVoice did not stop playback and restore BGM volume.");

            // 2. Select + DemonHunter 9-seal queuing (no cutoff at 0.15s) & MatchStart preemption
            Call(game, "StartAiMatch", AiDifficulty.Beginner);
            Get<GameState>(game, "_state").AiHero = HeroId.DemonHunter;
            HeardVoices.Clear();
            Call(game, "OnHeroChosen", HeroId.Warrior);
            Call(game, "ShowSetupScreen"); // duplicate ShowSetupScreen call must not double-queue DemonHunter
            yield return new WaitForSecondsRealtime(0.25f);
            Check(HeardVoices.SequenceEqual(new[] { "VO_Warrior_Select_01" }),
                "DemonHunter setup voice cut off hero Select voice too early.");
            float waitSelectDone = GameAudio.Duration("VO_Warrior_Select_01") + 0.35f;
            yield return new WaitForSecondsRealtime(waitSelectDone);
            Check(HeardVoices.SequenceEqual(new[] { "VO_Warrior_Select_01", "VO_DemonHunter_Ability_01" }),
                "DemonHunter 9-seal voice did not follow Select voice once.");
            HeardVoices.Clear();
            Call(game, "OnSecretChosen", new[] { 0, 1, 2, 3 });
            yield return new WaitForSecondsRealtime(0.4f);
            Check(HeardVoices.SequenceEqual(new[] { "VO_Warrior_MatchStart_01" }),
                "MatchStart voice did not play once on ShowFirstTurnBanner.");
            ResetGame(game);

            // 3. Active & Passive Hero Abilities + Spoiler-Free Privacy
            // Paladin heal (local only)
            StartAi(game, HeroId.Paladin);
            Get<GameState>(game, "_state").PlayerLieTokens = 1;
            HeardVoices.Clear();
            Call(game, "UsePaladinHeal");
            yield return new WaitForSecondsRealtime(0.2f);
            Check(HeardVoices.SequenceEqual(new[] { "VO_Paladin_Ability_01" }), "Paladin heal voice failed.");
            ResetGame(game);

            // Warrior reservation (silent VO) -> actual extra turn (plays VO_Warrior_Ability_01)
            StartAi(game, HeroId.Warrior);
            HeardVoices.Clear();
            Call(game, "UseWarriorAbility");
            yield return new WaitForSecondsRealtime(0.2f);
            Check(HeardVoices.Count == 0, "Warrior ability reservation leaked VO early.");
            Call(game, "EndMyAttackTurnAndPassToOpponent");
            yield return new WaitForSecondsRealtime(0.2f);
            Check(HeardVoices.SequenceEqual(new[] { "VO_Warrior_Ability_01" }),
                "Actual Warrior extra turn did not play VO_Warrior_Ability_01.");
            ResetGame(game);

            // Wizard ability (player + online opponent trigger vs reply)
            StartAi(game, HeroId.Wizard);
            HeardVoices.Clear();
            Call(game, "UseWizardAbility");
            yield return new WaitForSecondsRealtime(0.22f);
            Check(HeardVoices.SequenceEqual(new[] { "VO_Wizard_Ability_01" }), "Wizard ability voice failed.");
            ResetGame(game);

            // Priest query & defense choice
            StartAi(game, HeroId.Priest);
            HeardVoices.Clear();
            Call(game, "OnPriestQuerySubmitted", 0, 1);
            yield return new WaitForSecondsRealtime(0.2f);
            Check(HeardVoices.SequenceEqual(new[] { "VO_Priest_Ability_01" }), "Priest query voice failed.");
            ResetGame(game);

            // Rogue insight (local only)
            StartAi(game, HeroId.Rogue);
            HeardVoices.Clear();
            var overlay = Get<ChoiceOverlayView>(game, "_choiceOverlay");
            Call(game, "PromptTrustOrChallenge", new JudgeResult(1, 1), (bool?)true,
                (Action)(() => { }), (Action)(() => { }));
            var choiceRoot = Get<GameObject>(overlay, "_root");
            choiceRoot.GetComponentsInChildren<UnityEngine.UI.Button>()
                .Last(b => b.name.StartsWith("Button_진실 간파")).onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.2f);
            Check(HeardVoices.SequenceEqual(new[] { "VO_Rogue_Ability_01" }), "Rogue insight voice failed.");
            ResetGame(game);

            // Hunter 4S hide (VO_Hunter_Ability_01) vs normal Bluff (VO_Hunter_Bluff_01) vs Truth (silent)
            StartAi(game, HeroId.Hunter);
            HeardVoices.Clear();
            Call(game, "PromptDefenseChoice", new[] { 0, 1, 2, 3 }, new JudgeResult(4, 0), 2, true,
                (Action<JudgeResult, bool>)((r, u) => { }));
            choiceRoot = Get<GameObject>(overlay, "_root");
            choiceRoot.GetComponentsInChildren<UnityEngine.UI.Button>()
                .Last(b => b.name.StartsWith("Button_LIE TOKEN")).onClick.Invoke();
            yield return null;
            choiceRoot.GetComponentsInChildren<UnityEngine.UI.Button>()
                .Last(b => b.name == "Button_이 결과로 전송").onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.2f);
            Check(HeardVoices.SequenceEqual(new[] { "VO_Hunter_Ability_01" }),
                "Hunter hiding 4S win did not play VO_Hunter_Ability_01.");
            GameAudio.StopVoice();
            yield return null;
            HeardVoices.Clear();
            Call(game, "PromptDefenseChoice", new[] { 0, 1, 2, 4 }, new JudgeResult(3, 0), 1, true,
                (Action<JudgeResult, bool>)((r, u) => { }));
            choiceRoot.GetComponentsInChildren<UnityEngine.UI.Button>()
                .Last(b => b.name.StartsWith("Button_LIE TOKEN")).onClick.Invoke();
            yield return null;
            var strikeButtons = choiceRoot.transform.GetComponentsInChildren<Transform>()
                .Last(t => t.name == "Row_스트라이크").GetComponentsInChildren<UnityEngine.UI.Button>();
            strikeButtons[2].onClick.Invoke(); // 3S 0B -> 2S 0B (valid plausible fake result)
            choiceRoot.GetComponentsInChildren<UnityEngine.UI.Button>()
                .Last(b => b.name == "Button_이 결과로 전송").onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.2f);
            Check(HeardVoices.SequenceEqual(new[] { "VO_Hunter_Bluff_01" }),
                "Non-win LIE TOKEN bluff did not play VO_Hunter_Bluff_01.");
            GameAudio.StopVoice();
            yield return null;
            HeardVoices.Clear();
            Call(game, "PromptDefenseChoice", new[] { 0, 1, 2, 4 }, new JudgeResult(3, 0), 1, true,
                (Action<JudgeResult, bool>)((r, u) => { }));
            choiceRoot.GetComponentsInChildren<UnityEngine.UI.Button>()
                .Last(b => b.name == "Button_진실대로 전송").onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.2f);
            Check(HeardVoices.Count == 0, "Truthful defense unexpectedly played a voice.");
            ResetGame(game);

            // 4. Challenge -> CatchLie & Challenge -> Punished conversational queueing
            StartAi(game, HeroId.Warrior); // short crisp clips (~0.5~0.8s)
            HeardVoices.Clear();
            Call(game, "PromptTrustOrChallenge", new JudgeResult(1, 1), (bool?)true,
                (Action)(() => { }),
                (Action)(() => Call(game, "RevealAiDigit", "적중!", "상대가 거짓말을 했습니다", (Action)(() => { }))));
            choiceRoot = Get<GameObject>(overlay, "_root");
            choiceRoot.GetComponentsInChildren<UnityEngine.UI.Button>()
                .Last(b => b.name == "Button_거짓말 같다 (의심)").onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.2f);
            Check(HeardVoices.SequenceEqual(new[] { "VO_Warrior_Challenge_01" }),
                "CatchLie cut off Challenge instead of queuing behind it.");
            yield return new WaitForSecondsRealtime(GameAudio.Duration("VO_Warrior_Challenge_01") + 0.35f);
            Check(HeardVoices.SequenceEqual(new[] { "VO_Warrior_Challenge_01", "VO_Warrior_CatchLie_01" }),
                "Queued CatchLie did not follow Challenge.");
            ResetGame(game);

            // Same-frame 3-cue sequence (Bluff -> AI Challenge preempts Bluff -> Punished queues behind Challenge)
            StartAi(game, HeroId.Hunter);
            HeardVoices.Clear();
            GameAudio.PlayVoice(HeroId.Hunter, VoiceCue.Bluff, 0.08f);
            GameAudio.PlayVoice(HeroId.Warrior, VoiceCue.Challenge, 0.05f);
            GameAudio.PlayVoice(HeroId.Hunter, VoiceCue.Punished, 0.12f);
            yield return new WaitForSecondsRealtime(0.2f);
            Check(HeardVoices.SequenceEqual(new[] { "VO_Warrior_Challenge_01" }),
                "Instant AI Challenge did not preempt unstarted Bluff.");
            yield return new WaitForSecondsRealtime(GameAudio.Duration("VO_Warrior_Challenge_01") + 0.35f);
            Check(HeardVoices.SequenceEqual(new[] { "VO_Warrior_Challenge_01", "VO_Hunter_Punished_01" }),
                "Punished did not follow AI Challenge cleanly.");
            ResetGame(game);

            // Online opponent challenge, DefendTruth, duplicate reveal protection, and online opponent Wizard
            StartAi(game, HeroId.Rogue);
            Get<GameState>(game, "_state").AiHero = HeroId.Priest;
            var net = new NetworkMatchController(Database, game, "vo-online-test", MatchSide.A, "me", "opp");
            Set(game, "_network", net);
            Set(game, "_isOnlineMatch", true);
            Call(game, "WireNetworkEvents");
            HeardVoices.Clear();
            Raise(net, "OnChallengeNeedsMyJudgement");
            yield return new WaitForSecondsRealtime(0.15f);
            Check(HeardVoices.SequenceEqual(new[] { "VO_Priest_Challenge_01" }),
                "Online opponent challenge did not play opponent hero's Challenge voice.");
            GameAudio.StopVoice();
            HeardVoices.Clear();
            Set(game, "_awaitingMyChallengeOutcome", false);
            Raise(net, "OnOpponentRevealed", MatchSide.B, 1, 7);
            Raise(net, "OnOpponentRevealed", MatchSide.B, 1, 7); // duplicate poll must not replay voice
            yield return new WaitForSecondsRealtime(0.25f);
            Check(HeardVoices.SequenceEqual(new[] { "VO_Rogue_DefendTruth_01" }),
                "Online wrong-challenge reveal did not play DefendTruth exactly once.");
            GameAudio.StopVoice();
            HeardVoices.Clear();
            Raise(net, "OnOpponentWizardReveal", 0, 5);
            Raise(net, "OnMustRevealForWizard");
            yield return new WaitForSecondsRealtime(0.22f);
            Check(HeardVoices.SequenceEqual(new[] { "VO_Wizard_Ability_01" }),
                "Online opponent Wizard did not play VO_Wizard_Ability_01 once.");
            GameAudio.StopVoice();
            HeardVoices.Clear();
            Set(game, "_wizardAwaitingReply", true);
            Raise(net, "OnOpponentWizardReveal", 2, 8);
            yield return new WaitForSecondsRealtime(0.22f);
            Check(HeardVoices.Count == 0, "Online Wizard reply replayed VO_Wizard_Ability_01.");
            net.Stop();
            Set(game, "_isOnlineMatch", false);
            Set(game, "_network", null);
            ResetGame(game);

            // 5. FinalChance (second mover only) & ResultPopup Win / Lose / Draw + Hide cancellation
            StartAi(game, HeroId.Hunter);
            HeardVoices.Clear();
            Call(game, "GrantAiLastGuess");
            yield return new WaitForSecondsRealtime(0.4f);
            Check(HeardVoices.Count == 0, "Opponent's last guess wrongly played my FinalChance voice.");
            ResetGame(game);

            StartAi(game, HeroId.Hunter);
            HeardVoices.Clear();
            Call(game, "BeginPlayerLastGuess");
            yield return new WaitForSecondsRealtime(0.42f);
            Check(HeardVoices.SequenceEqual(new[] { "VO_Hunter_FinalChance_01" }),
                "BeginPlayerLastGuess did not play VO_Hunter_FinalChance_01.");
            ResetGame(game);

            StartAi(game, HeroId.Wizard);
            var resPopup = Get<ResultPopupView>(game, "_resultPopup");
            HeardVoices.Clear();
            Call(game, "EndGame", true, "승리 대사 검수");
            yield return new WaitForSecondsRealtime(1.2f); // stamp at 0.81s + 0.22s = 1.03s
            Check(HeardVoices.SequenceEqual(new[] { "VO_Wizard_Win_01" }),
                "Victory popup did not play VO_Wizard_Win_01 after stamp.");
            resPopup.Hide();
            Check(!Sources(audio).Last().isPlaying, "Hiding ResultPopupView did not stop playing result voice.");

            StartAi(game, HeroId.Paladin);
            HeardVoices.Clear();
            Call(game, "EndGame", false, "패배 대사 검수");
            yield return new WaitForSecondsRealtime(1.5f); // lose stamp at 1.11s + 0.22s = 1.33s
            Check(HeardVoices.SequenceEqual(new[] { "VO_Paladin_Lose_01" }),
                "Defeat popup did not play VO_Paladin_Lose_01 after stamp.");
            resPopup.Hide();

            StartAi(game, HeroId.Priest);
            HeardVoices.Clear();
            Call(game, "EndGameDraw", "무승부 대사 검수");
            yield return new WaitForSecondsRealtime(1.2f); // draw stamp at 0.81s + 0.22s = 1.03s
            Check(HeardVoices.SequenceEqual(new[] { "VO_Priest_Draw_01" }),
                "Draw popup did not play VO_Priest_Draw_01 after stamp.");
            resPopup.Hide();

            // Early Hide before +0.22s must cancel pending result voice
            StartAi(game, HeroId.Rogue);
            HeardVoices.Clear();
            Call(game, "EndGame", true, "조기 닫기 검수");
            yield return new WaitForSecondsRealtime(0.88f); // right after stamp (0.81s), before +0.22s voice (1.03s)
            resPopup.Hide();
            yield return new WaitForSecondsRealtime(0.35f);
            Check(HeardVoices.Count == 0, "Closing ResultPopupView before +0.22s did not cancel pending result voice.");
            ResetGame(game);
            Debug.Log($"Character voice integration checks passed: {Assertions - startAssertions} assertions.");
            }
            finally
            {
                GameAudio.EditorEffectPlayed -= Record;
                GameAudio.EditorVoicePlayed -= RecordVoice;
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
        private static void RecordVoice(string name) => HeardVoices.Add(name);
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
