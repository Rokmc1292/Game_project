using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using LiarsBatting.Core;
using LiarsBatting.AI;
using LiarsBatting.Presentation;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LiarsBatting.Editor
{
    // Run with -batchmode -executeMethod LiarsBatting.Editor.AudioSmokeTest.Run.
    [InitializeOnLoad]
    public static class AudioSmokeTest
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string SessionKey = "LiarsBatting.AudioSmokeTest";
        private const string StartedKey = "LiarsBatting.AudioSmokeTest.Started";
        private static double _deadline;
        private static double _stepStarted;
        private static int _step;
        private static int _timeouts;
        private static CountdownTimerView _timer;
        private static GameObject _settingsMenu;
        private static bool _settingsMenuWasActive;

        static AudioSmokeTest()
        {
            if (!SessionState.GetBool(SessionKey, false)) return;
            _deadline = EditorApplication.timeSinceStartup + 180;
            _step = SessionState.GetBool(StartedKey, false) ? 0 : -1;
            _stepStarted = EditorApplication.timeSinceStartup;
            AudioUpgradeTests.ConfigureOffline();
            EditorApplication.update += Tick;
        }

        public static void Run()
        {
            AudioCatalogBuilder.RebuildCatalog();
            SessionState.SetBool(SessionKey, true);
            SessionState.EraseBool(StartedKey);
            _deadline = EditorApplication.timeSinceStartup + 180;
            _step = -1;
            _timeouts = 0;
            _stepStarted = EditorApplication.timeSinceStartup;
            AudioUpgradeTests.ConfigureOffline();
            EditorApplication.update += Tick;
        }

        public static void CompilePlayer()
        {
            AudioCatalogBuilder.RebuildCatalog();
            var settings = new ScriptCompilationSettings
            {
                target = BuildTarget.StandaloneWindows64,
                group = BuildTargetGroup.Standalone,
                subtarget = (int)StandaloneBuildSubtarget.Player
            };
            var result = PlayerBuildInterface.CompilePlayerScripts(settings, "Library/AudioValidationPlayer");
            if (result.assemblies == null || !result.assemblies.Any(path => Path.GetFileName(path) == "Assembly-CSharp.dll"))
                throw new InvalidOperationException("Player script compilation did not produce the game assembly.");
            Debug.Log($"Audio player script compilation passed: {result.assemblies.Count()} assemblies.");
        }

        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline)
                    throw new Exception("Audio smoke test timed out.");
                if (!EditorApplication.isPlaying)
                {
                    if (_step == -1 && !EditorApplication.isCompiling && !EditorApplication.isUpdating &&
                        EditorApplication.timeSinceStartup - _stepStarted >= 10)
                    {
                        _step = 0;
                        SessionState.SetBool(StartedKey, true);
                        EditorApplication.isPlaying = true;
                    }
                    return;
                }

                var audio = UnityEngine.Object.FindAnyObjectByType<GameAudio>();
                var game = UnityEngine.Object.FindAnyObjectByType<GameBootstrap>();
                if (audio == null || game == null) return;
                var sources = audio.GetComponents<AudioSource>();

                switch (_step)
                {
                    case 0:
                        Require(MusicName(audio) == "BGM_Lobby_Noir", "Lobby music was not selected.");
                        var catalog = Resources.Load<AudioCatalog>("AudioCatalog");
                        Require(catalog != null && catalog.Clips != null &&
                            catalog.Clips.Any(clip => clip != null && clip.name == "SFX_Timer_Timeout"),
                            "Audio catalog is incomplete.");
                        TestSoundSettings(audio, game);
                        _settingsMenu = (GameObject)typeof(GameBootstrap)
                            .GetField("_mainMenuScreen", PrivateInstance).GetValue(game);
                        _settingsMenuWasActive = _settingsMenu.activeSelf;
                        _settingsMenu.SetActive(true);
                        Invoke(game, "ShowSoundSettings");
                        _stepStarted = EditorApplication.timeSinceStartup;
                        _step = 10;
                        break;
                    case 10:
                        if (EditorApplication.timeSinceStartup - _stepStarted < 0.25) break;
                        try
                        {
                            // The dialog now lives on the root (a sibling of the main menu), so it also works in a match.
                            var dialog = _settingsMenu.transform.parent.Find("SoundSettings");
                            var musicSlider = dialog.GetComponentsInChildren<Slider>()
                                .First(slider => slider.name == "MusicSlider");
                            var effectsSlider = dialog.GetComponentsInChildren<Slider>()
                                .First(slider => slider.name == "EffectsSlider");
                            var voiceSlider = dialog.GetComponentsInChildren<Slider>()
                                .First(slider => slider.name == "VoiceSlider");
                            var musicToggle = dialog.GetComponentsInChildren<Toggle>()
                                .First(toggle => toggle.name == "MusicMute");
                            var effectsToggle = dialog.GetComponentsInChildren<Toggle>()
                                .First(toggle => toggle.name == "EffectsMute");
                            var voiceToggle = dialog.GetComponentsInChildren<Toggle>()
                                .First(toggle => toggle.name == "VoiceMute");
                            var closeBtn = dialog.GetComponentsInChildren<Button>()
                                .First(button => button.name == "CloseSoundSettingsButton");
                            Require(ReceivesPointer<IPointerDownHandler>(musicSlider.gameObject),
                                "Music slider cannot receive pointer input.");
                            Require(ReceivesPointer<IPointerDownHandler>(effectsSlider.gameObject),
                                "Effects slider cannot receive pointer input.");
                            Require(ReceivesPointer<IPointerDownHandler>(voiceSlider.gameObject),
                                "Voice slider cannot receive pointer input.");
                            Require(ReceivesPointer<IPointerClickHandler>(musicToggle.gameObject),
                                "Music mute cannot receive pointer input.");
                            Require(ReceivesPointer<IPointerClickHandler>(effectsToggle.gameObject),
                                "Effects mute cannot receive pointer input.");
                            Require(ReceivesPointer<IPointerClickHandler>(voiceToggle.gameObject),
                                "Voice mute cannot receive pointer input.");
                            Require(ReceivesPointer<IPointerClickHandler>(closeBtn.gameObject),
                                "Close sound settings button cannot receive pointer input.");
                        }
                        finally
                        {
                            Invoke(game, "CloseSoundSettings");
                            _settingsMenu.SetActive(_settingsMenuWasActive);
                        }
                        var canvas = UiFactory.CreateCanvas("AudioSmokeTestCanvas");
                        Application.runInBackground = true;
                        _timer = new CountdownTimerView(canvas.transform, game);
                        _timer.Start(1.5f, () => _timeouts++);
                        Require(HasClip(sources, "SFX_Timer_Tick_Urgent"), "Timer tick was not selected.");
                        _timer.Stop();
                        _stepStarted = EditorApplication.timeSinceStartup;
                        _step = 1;
                        break;
                    case 1:
                        if (EditorApplication.timeSinceStartup - _stepStarted < 1.8) break;
                        Require(_timeouts == 0 && !HasClip(sources, "SFX_Timer_Timeout"),
                            "Cancelled timer emitted a timeout.");
                        _timer.Start(0.2f, () => _timeouts++);
                        _stepStarted = EditorApplication.timeSinceStartup;
                        _step = 2;
                        break;
                    case 2:
                        if (_timeouts == 0 && EditorApplication.timeSinceStartup - _stepStarted < 8) break;
                        Require(_timeouts == 1 && HasClip(sources, "SFX_Timer_Timeout"),
                            "Expired timer did not emit a timeout.");
                        Invoke(game, "StartAiMatch", AiDifficulty.Beginner);
                        Invoke(game, "OnHeroChosen", HeroId.Paladin);
                        Invoke(game, "OnSecretChosen", new[] { 0, 1, 2, 3 });
                        Require(MusicName(audio) == "BGM_Match_Tension", "Match music was not selected.");
                        GameAudio.Play("SFX_Hero_Paladin_Restore");
                        Require(HasClip(sources, "SFX_Hero_Paladin_Restore"), "Hero sound was not selected.");
                        _step = 3;
                        audio.StartCoroutine(RunUpgradeTests(game, audio));
                        break;
                }
            }
            catch (Exception ex)
            {
                Finish(ex);
            }
        }

        private static IEnumerator RunUpgradeTests(GameBootstrap game, GameAudio audio)
        {
            foreach (var tests in new[] { AudioUpgradeTests.Run(game, audio), AudioUpgradeTests.RunVoice(game, audio), ResultRulesTests.Run(game) })
            {
                while (true)
                {
                    object wait = null;
                    bool more = false;
                    Exception failure = null;
                    try
                    {
                        more = tests.MoveNext();
                        if (more) wait = tests.Current;
                    }
                    catch (Exception ex) { failure = ex; }
                    if (failure != null || !more)
                    {
                        (tests as IDisposable)?.Dispose();
                        if (failure != null) { Finish(failure); yield break; }
                        break;
                    }
                    yield return wait;
                }
            }
            Finish(null);
        }

        private static void Finish(Exception failure)
        {
            if (failure != null) Debug.LogException(failure);
            else Debug.Log("Audio smoke test passed: settings, timer cancellation, timeout, and upgraded audio integration.");
            GameBootstrap.EditorFirestoreFactory = null;
            SessionState.EraseBool(SessionKey);
            SessionState.EraseBool(StartedKey);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(failure == null ? 0 : 1);
        }

        private static string MusicName(GameAudio audio)
            => (string)typeof(GameAudio).GetField("_musicName", PrivateInstance).GetValue(audio);

        private static void TestSoundSettings(GameAudio audio, GameBootstrap game)
        {
            string[] keys =
            {
                "lb_audio_music_volume", "lb_audio_effects_volume", "lb_audio_voice_volume",
                "lb_audio_music_muted", "lb_audio_effects_muted", "lb_audio_voice_muted"
            };
            bool[] existed = keys.Select(PlayerPrefs.HasKey).ToArray();
            float music = GameAudio.MusicLevel;
            float effects = GameAudio.EffectsLevel;
            float voice = GameAudio.VoiceLevel;
            bool musicMuted = GameAudio.MusicMuted;
            bool effectsMuted = GameAudio.EffectsMuted;
            bool voiceMuted = GameAudio.VoiceMuted;
            var gainField = typeof(GameAudio).GetField("_musicGain", PrivateInstance);
            float originalGain = (float)gainField.GetValue(audio);
            var menu = (GameObject)typeof(GameBootstrap).GetField("_mainMenuScreen", PrivateInstance).GetValue(game);
            bool menuActive = menu.activeSelf;
            try
            {
                foreach (string key in keys) PlayerPrefs.DeleteKey(key);
                typeof(GameAudio).GetMethod("Setup", PrivateInstance).Invoke(audio, null);
                Require(Approximately(GameAudio.MusicLevel, 0.8f) && Approximately(GameAudio.EffectsLevel, 0.8f) &&
                    Approximately(GameAudio.VoiceLevel, 0.8f) &&
                    !GameAudio.MusicMuted && !GameAudio.EffectsMuted && !GameAudio.VoiceMuted,
                    "Default audio settings are incorrect.");

                menu.SetActive(true);
                var open = menu.GetComponentsInChildren<Button>(true)
                    .First(button => button.name == "SoundSettingsButton");
                open.onClick.Invoke();
                var dialog = menu.transform.parent.Find("SoundSettings").gameObject;
                Require(dialog.activeSelf, "Settings button did not open the dialog.");
                var sliders = dialog.GetComponentsInChildren<Slider>(true);
                var toggles = dialog.GetComponentsInChildren<Toggle>(true);
                var musicSlider = sliders.First(slider => slider.name == "MusicSlider");
                var effectsSlider = sliders.First(slider => slider.name == "EffectsSlider");
                var voiceSlider = sliders.First(slider => slider.name == "VoiceSlider");
                var musicToggle = toggles.First(toggle => toggle.name == "MusicMute");
                var effectsToggle = toggles.First(toggle => toggle.name == "EffectsMute");
                var voiceToggle = toggles.First(toggle => toggle.name == "VoiceMute");
                Require(Approximately(musicSlider.value, 0.8f) && Approximately(effectsSlider.value, 0.8f) &&
                    Approximately(voiceSlider.value, 0.8f),
                    "Settings sliders did not show the saved levels.");

                gainField.SetValue(audio, 0.28f);
                musicSlider.value = 0.5f;
                effectsSlider.value = 0.4f;
                voiceSlider.value = 0.6f;
                GameAudio.Play("SFX_Trust_Accept");
                GameAudio.PlayVoice(HeroId.Hunter, VoiceCue.Select, 0f);
                var allSources = audio.GetComponents<AudioSource>();
                var effect = allSources.First(source =>
                    source.clip != null && source.clip.name == "SFX_Trust_Accept");
                var voiceSource = allSources.First(source =>
                    source.clip != null && source.clip.name == "VO_Hunter_Select_01");
                var musicSource = allSources[0];
                Require(Approximately(musicSource.volume, 0.28f * 0.5f) &&
                    Approximately(effect.volume, 0.65f * 0.58f * 0.4f) &&
                    Approximately(voiceSource.volume, 0.85f * 0.6f),
                    "Independent levels or effect/voice balancing were not applied.");
                effectsSlider.value = 0.2f;
                voiceSlider.value = 0.3f;
                Require(Approximately(effect.volume, 0.65f * 0.58f * 0.2f) &&
                    Approximately(voiceSource.volume, 0.85f * 0.3f),
                    "Changing effects or voice volume did not update a playing source.");
                musicToggle.isOn = true;
                Require(Approximately(musicSource.volume, 0f) && effect.volume > 0f && voiceSource.volume > 0f,
                    "Muting music affected effects or voice.");
                effectsToggle.isOn = true;
                Require(Approximately(effect.volume, 0f) && voiceSource.volume > 0f,
                    "Muting effects did not update a playing effect or affected voice.");
                voiceToggle.isOn = true;
                Require(Approximately(voiceSource.volume, 0f), "Muting voice did not silence the playing voice.");
                musicToggle.isOn = false;
                effectsToggle.isOn = false;
                voiceToggle.isOn = false;
                Require(Approximately(musicSource.volume, 0.28f * 0.5f) &&
                    Approximately(effect.volume, 0.65f * 0.58f * 0.2f) &&
                    Approximately(voiceSource.volume, 0.85f * 0.3f),
                    "Unmuting did not restore the chosen levels.");
                effectsToggle.isOn = true;
                voiceToggle.isOn = true;
                GameAudio.StopVoice();
                var close = dialog.GetComponentsInChildren<Button>(true)
                    .First(button => button.name == "CloseSoundSettingsButton");
                close.onClick.Invoke();
                Require(!dialog.activeSelf, "Settings dialog did not close.");
                typeof(GameAudio).GetMethod("Setup", PrivateInstance).Invoke(audio, null);
                Require(Approximately(GameAudio.MusicLevel, 0.5f) &&
                    Approximately(GameAudio.EffectsLevel, 0.2f) &&
                    Approximately(GameAudio.VoiceLevel, 0.3f) &&
                    GameAudio.EffectsMuted && GameAudio.VoiceMuted && !GameAudio.MusicMuted,
                    "Audio settings were not restored from PlayerPrefs.");
                open.onClick.Invoke();
                Require(Approximately(musicSlider.value, 0.5f) && Approximately(effectsSlider.value, 0.2f) &&
                    Approximately(voiceSlider.value, 0.3f) &&
                    effectsToggle.isOn && voiceToggle.isOn && !musicToggle.isOn,
                    "Reopened settings did not reflect saved values.");
                close.onClick.Invoke();
            }
            finally
            {
                GameAudio.StopVoice();
                GameAudio.SetMusicLevel(music);
                GameAudio.SetEffectsLevel(effects);
                GameAudio.SetVoiceLevel(voice);
                GameAudio.SetMusicMuted(musicMuted);
                GameAudio.SetEffectsMuted(effectsMuted);
                GameAudio.SetVoiceMuted(voiceMuted);
                for (int i = 0; i < keys.Length; i++)
                    if (!existed[i]) PlayerPrefs.DeleteKey(keys[i]);
                GameAudio.SaveSettings();
                gainField.SetValue(audio, originalGain);
                menu.SetActive(menuActive);
            }
        }

        private static bool Approximately(float a, float b) => Mathf.Abs(a - b) < 0.0001f;

        private static bool ReceivesPointer<T>(GameObject control) where T : IEventSystemHandler
        {
            var rect = (RectTransform)control.transform;
            var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var eventData = new PointerEventData(EventSystem.current) { position = point };
            var results = new List<RaycastResult>();
            control.GetComponentInParent<GraphicRaycaster>().Raycast(eventData, results);
            bool received = results.Count > 0 && ExecuteEvents.GetEventHandler<T>(results[0].gameObject) == control;
            if (!received)
                Debug.LogWarning($"Pointer test {control.name}: screen {Screen.width}x{Screen.height}, " +
                    $"rect {rect.rect}, position {point}, hits {string.Join(", ", results.Select(r => r.gameObject.name))}");
            return received;
        }

        private static bool HasClip(AudioSource[] sources, string name)
            => sources.Any(source => source.clip != null && source.clip.name == name);

        private static void Invoke(GameBootstrap game, string name, params object[] args)
            => typeof(GameBootstrap).GetMethod(name, PrivateInstance).Invoke(game, args);

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
