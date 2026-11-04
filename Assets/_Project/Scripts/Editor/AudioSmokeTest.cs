using System;
using System.Linq;
using System.Reflection;
using LiarsBatting.Core;
using LiarsBatting.Presentation;
using UnityEditor;
using UnityEngine;

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

        static AudioSmokeTest()
        {
            if (!SessionState.GetBool(SessionKey, false)) return;
            _deadline = EditorApplication.timeSinceStartup + 45;
            _step = SessionState.GetBool(StartedKey, false) ? 0 : -1;
            _stepStarted = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        public static void Run()
        {
            AudioCatalogBuilder.RebuildCatalog();
            SessionState.SetBool(SessionKey, true);
            SessionState.EraseBool(StartedKey);
            _deadline = EditorApplication.timeSinceStartup + 45;
            _step = -1;
            _timeouts = 0;
            _stepStarted = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
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
                        Invoke(game, "StartAiMatch");
                        Invoke(game, "OnHeroChosen", HeroId.Paladin);
                        Invoke(game, "OnSecretChosen", new[] { 0, 1, 2, 3 });
                        Require(MusicName(audio) == "BGM_Match_Tension", "Match music was not selected.");
                        GameAudio.Play("SFX_Hero_Paladin_Restore");
                        Require(HasClip(sources, "SFX_Hero_Paladin_Restore"), "Hero sound was not selected.");
                        Debug.Log("Audio smoke test passed: lobby, match, effects, timer cancellation and timeout.");
                        SessionState.EraseBool(SessionKey);
                        SessionState.EraseBool(StartedKey);
                        EditorApplication.update -= Tick;
                        EditorApplication.Exit(0);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                SessionState.EraseBool(SessionKey);
                SessionState.EraseBool(StartedKey);
                EditorApplication.update -= Tick;
                EditorApplication.Exit(1);
            }
        }

        private static string MusicName(GameAudio audio)
            => (string)typeof(GameAudio).GetField("_musicName", PrivateInstance).GetValue(audio);

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
