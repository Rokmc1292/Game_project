using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LiarsBatting.Presentation
{
    public class GameAudio : MonoBehaviour
    {
        private const float MusicVolume = 0.28f;
        private const float EffectsVolume = 0.65f;
        private const float DefaultVolume = 0.8f;
        private const string MusicVolumeKey = "lb_audio_music_volume";
        private const string EffectsVolumeKey = "lb_audio_effects_volume";
        private const string MusicMutedKey = "lb_audio_music_muted";
        private const string EffectsMutedKey = "lb_audio_effects_muted";
        private const int EffectVoices = 12;
        private const float ClimaxHoldSeconds = 8f;
        private enum EffectCategory { Hover, UI, Timer, Gameplay, Hero, Result, Progress }
        private enum MusicPhase { Lobby, Match, FinalChance, Result }

        private readonly struct EffectSettings
        {
            public readonly EffectCategory Category;
            public readonly int Priority;
            public readonly float Cooldown;
            public readonly float Duck;

            public EffectSettings(EffectCategory category, int priority, float cooldown = 0.04f, float duck = 1f)
            {
                Category = category;
                Priority = priority;
                Cooldown = cooldown;
                Duck = duck;
            }
        }

        private static GameAudio _instance;
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, float> _lastPlayed = new Dictionary<string, float>();
        private readonly HashSet<string> _missingClips = new HashSet<string>();
        private AudioSource _music;
        private AudioSource[] _effects;
        private readonly int[] _voicePriority = new int[EffectVoices];
        private readonly EffectCategory[] _voiceCategory = new EffectCategory[EffectVoices];
        private readonly long[] _voiceOrder = new long[EffectVoices];
        private readonly float[] _voiceGain = new float[EffectVoices];
        private readonly float[] _voiceDuck = new float[EffectVoices];
        private Coroutine _musicTransition;
        private string _musicName;
        private long _sequence;
        private float _musicGain;
        private float _duckGain = 1f;
        private MusicPhase _musicPhase;
        private float _climaxUntil;
        private float _musicVolume;
        private float _effectsVolume;
        private bool _musicMuted;
        private bool _effectsMuted;

        public static float MusicLevel => _instance != null ? _instance._musicVolume : DefaultVolume;
        public static float EffectsLevel => _instance != null ? _instance._effectsVolume : DefaultVolume;
        public static bool MusicMuted => _instance != null && _instance._musicMuted;
        public static bool EffectsMuted => _instance != null && _instance._effectsMuted;
#if UNITY_EDITOR
        public static event System.Action<string> EditorEffectPlayed;
#endif

        public static void Initialize()
        {
            if (_instance != null) return;
            var go = new GameObject("GameAudio");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GameAudio>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void OnEnable()
        {
            if (!Application.isPlaying || (_instance != null && _instance != this)) return;
            _instance = this;
            Setup();
        }

        private void Setup()
        {
            if (_musicTransition != null) StopCoroutine(_musicTransition);
            _lastPlayed.Clear();
            _musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumeKey, DefaultVolume));
            _effectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(EffectsVolumeKey, DefaultVolume));
            _musicMuted = PlayerPrefs.GetInt(MusicMutedKey, 0) != 0;
            _effectsMuted = PlayerPrefs.GetInt(EffectsMutedKey, 0) != 0;
            _clips.Clear();
            var catalog = Resources.Load<AudioCatalog>("AudioCatalog");
            if (catalog == null || catalog.Clips == null)
                Debug.LogWarning("Audio catalog is missing. Open the project in Unity to generate it.");
            else
                foreach (var clip in catalog.Clips)
                    if (clip != null && !_clips.TryAdd(clip.name, clip))
                        Debug.LogError($"Duplicate audio clip name: {clip.name}");

            if (FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
            var sources = new List<AudioSource>(GetComponents<AudioSource>());
            while (sources.Count < EffectVoices + 1) sources.Add(gameObject.AddComponent<AudioSource>());
            _music = sources[0];
            _music.playOnAwake = false;
            _music.spatialBlend = 0f;
            _musicTransition = null;
            if (_music.isPlaying)
            {
                _musicName = _music.clip != null ? _music.clip.name : null;
                if (_musicGain <= 0f) _musicGain = MusicVolume;
            }
            _effects = new AudioSource[EffectVoices];
            for (int i = 0; i < _effects.Length; i++)
            {
                _effects[i] = sources[i + 1];
                _effects[i].playOnAwake = false;
                _effects[i].spatialBlend = 0f;
            }
            ApplyVolumes();
        }

        private void Update()
        {
            if (_effects == null) return;
            float duckTarget = 1f;
            for (int i = 0; i < _effects.Length; i++)
                if (_effects[i].isPlaying && _voiceGain[i] > 0f)
                    duckTarget = Mathf.Min(duckTarget, _voiceDuck[i]);
            _duckGain = Mathf.MoveTowards(_duckGain, duckTarget,
                Time.unscaledDeltaTime * (duckTarget < _duckGain ? 8f : 3f));
            if (_musicPhase == MusicPhase.Match && _climaxUntil > 0f && Time.unscaledTime >= _climaxUntil)
            {
                _climaxUntil = 0f;
                ContinueMatchMusic();
            }
            ApplyMusicVolume();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        public static void SetMusicLevel(float value)
        {
            if (_instance == null) return;
            _instance._musicVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(MusicVolumeKey, _instance._musicVolume);
            _instance.ApplyMusicVolume();
        }

        public static void SetEffectsLevel(float value)
        {
            if (_instance == null) return;
            _instance._effectsVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(EffectsVolumeKey, _instance._effectsVolume);
            _instance.ApplyVolumes();
        }

        public static void SetMusicMuted(bool muted)
        {
            if (_instance == null) return;
            _instance._musicMuted = muted;
            PlayerPrefs.SetInt(MusicMutedKey, muted ? 1 : 0);
            _instance.ApplyMusicVolume();
        }

        public static void SetEffectsMuted(bool muted)
        {
            if (_instance == null) return;
            _instance._effectsMuted = muted;
            PlayerPrefs.SetInt(EffectsMutedKey, muted ? 1 : 0);
            _instance.ApplyVolumes();
        }

        public static void SaveSettings() => PlayerPrefs.Save();

        private void OnApplicationPause(bool paused)
        {
            if (paused) SaveSettings();
        }

        private void OnApplicationQuit() => SaveSettings();

        private void ApplyMusicVolume()
        {
            float duck = _effectsMuted || _effectsVolume <= 0f ? 1f : _duckGain;
            _music.volume = _musicMuted ? 0f : _musicGain * duck * _musicVolume;
        }

        private void ApplyVolumes()
        {
            ApplyMusicVolume();
            for (int i = 0; i < _effects.Length; i++)
                _effects[i].volume = _effectsMuted ? 0f : _voiceGain[i] * _effectsVolume;
        }

        public static void Play(string name, float pitch = 1f, float volume = 1f)
        {
            if (!TryGetClip(name, out var clip)) return;
            var audio = _instance;
            var settings = SettingsFor(name);
            float now = Time.unscaledTime;
            if (audio._lastPlayed.TryGetValue(name, out float last) && now - last < settings.Cooldown) return;
            int categoryCount = 0;
            int categoryReplace = -1;
            int sameClip = -1;
            int free = -1;
            int replace = -1;
            for (int i = 0; i < audio._effects.Length; i++)
            {
                if (!audio._effects[i].isPlaying)
                {
                    if (free < 0) free = i;
                    continue;
                }
                if (audio._effects[i].clip == clip) sameClip = i;
                if (audio._voiceCategory[i] == settings.Category)
                {
                    categoryCount++;
                    if (audio._voicePriority[i] <= settings.Priority &&
                        (categoryReplace < 0 || audio._voiceOrder[i] < audio._voiceOrder[categoryReplace]))
                        categoryReplace = i;
                }
                if (audio._voicePriority[i] < settings.Priority &&
                    (replace < 0 || audio._voicePriority[i] < audio._voicePriority[replace] ||
                     (audio._voicePriority[i] == audio._voicePriority[replace] &&
                      audio._voiceOrder[i] < audio._voiceOrder[replace])))
                    replace = i;
            }
            int chosen = sameClip >= 0 ? sameClip :
                categoryCount >= CategoryLimit(settings.Category) ? categoryReplace :
                free >= 0 ? free : replace;
            if (chosen < 0) return;
            var source = audio._effects[chosen];
            source.Stop();
            source.clip = clip;
            source.pitch = Mathf.Clamp(pitch, 0.1f, 3f);
            audio._voiceGain[chosen] = EffectsVolume * Mathf.Clamp01(volume) * EffectBalance(name);
            source.volume = audio._effectsMuted ? 0f : audio._voiceGain[chosen] * audio._effectsVolume;
            audio._voicePriority[chosen] = settings.Priority;
            audio._voiceCategory[chosen] = settings.Category;
            audio._voiceDuck[chosen] = settings.Duck;
            audio._voiceOrder[chosen] = ++audio._sequence;
            audio._lastPlayed[name] = now;
            source.Play();
#if UNITY_EDITOR
            EditorEffectPlayed?.Invoke(name);
#endif
        }

        private static bool TryGetClip(string name, out AudioClip clip)
        {
            clip = null;
            if (_instance == null || string.IsNullOrEmpty(name)) return false;
            if (_instance._clips.TryGetValue(name, out clip)) return true;
            if ((Application.isEditor || Debug.isDebugBuild) && _instance._missingClips.Add(name))
                Debug.LogWarning($"Audio clip is missing: {name}");
            return false;
        }

        public static float Duration(string name)
            => TryGetClip(name, out var clip) ? clip.length : 0f;

        public static void Stop(string name)
        {
            if (_instance == null) return;
            for (int i = 0; i < _instance._effects.Length; i++)
                if (_instance._effects[i].clip != null && _instance._effects[i].clip.name == name)
                    _instance.StopVoice(i);
        }

        public static void StopEffects()
        {
            if (_instance == null) return;
            for (int i = 0; i < _instance._effects.Length; i++) _instance.StopVoice(i);
            _instance._duckGain = 1f;
            _instance.ApplyMusicVolume();
        }

        public static void StopProgressEffects()
        {
            if (_instance == null) return;
            for (int i = 0; i < _instance._effects.Length; i++)
                if (_instance._voiceCategory[i] == EffectCategory.Progress) _instance.StopVoice(i);
        }

        private void StopVoice(int index)
        {
            _effects[index].Stop();
            _effects[index].clip = null;
            _voiceDuck[index] = 1f;
            _voiceGain[index] = 0f;
        }

        // Conservative cue gains; RMS measurements guide matching, listening remains necessary.
        private static float EffectBalance(string name)
        {
            switch (name)
            {
                case "SFX_Result_Dim_Drop": return 0.51f;
                case "SFX_Invite_Notification": return 0.52f;
                case "SFX_Trust_Accept": return 0.58f;
                case "SFX_Hero_Paladin_Restore": return 0.59f;
                case "SFX_Turn_Start_Opponent": return 0.67f;
                case "SFX_Turn_Start_My": return 0.69f;
                case "SFX_Timer_Timeout": return 0.72f;
                case "SFX_Hero_Wizard_Clairvoyance": return 0.73f;
                case "SFX_Challenge_Doubt":
                case "SFX_Reveal_Punished": return 0.78f;
                case "SFX_Hero_Priest_Inquiry":
                case "SFX_Result_Stamp_Lose": return 0.79f;
                case "SFX_Judge_Out": return 0.87f;
                case "SFX_Hero_Rogue_Insight":
                case "SFX_Result_Stamp_Win": return 0.88f;
                case "SFX_Hero_Warrior_Rush": return 0.89f;
                case "SFX_LieToken_Spend": return 0.92f;
                case "SFX_Reveal_Busted": return 0.97f;
                case "SFX_Truth_Declare": return 0.98f;
                case "SFX_Result_Stamp_Draw": return 0.85f;
                case "SFX_LastGuess_Start":
                case "SFX_Match_Start": return 0.8f;
                case "SFX_Digit_Reveal": return 0.65f;
                case "SFX_Secret_Lock":
                case "SFX_Hero_Select_Confirm":
                case "SFX_Cosmetic_Equip":
                case "SFX_UI_Error": return 0.6f;
                case "SFX_Hero_DemonHunter_Seal": return 0.75f;
                case "SFX_Priest_Answer_Yes": return 0.7f;
                case "SFX_Priest_Answer_No": return 0.53f;
                case "SFX_Progress_ChallengeComplete": return 0.58f;
                case "SFX_Rematch_Requested":
                case "SFX_Rematch_Ready":
                case "SFX_Warrior_ExtraTurn":
                case "SFX_Progress_LevelUp":
                case "SFX_Progress_HeroLevelUp":
                case "SFX_Progress_RewardUnlock": return 0.75f;
                default: return 1f;
            }
        }

        private static int CategoryLimit(EffectCategory category)
        {
            switch (category)
            {
                case EffectCategory.Hover:
                case EffectCategory.Timer:
                case EffectCategory.Progress: return 1;
                case EffectCategory.UI:
                case EffectCategory.Hero: return 2;
                default: return 4;
            }
        }

        private static EffectSettings SettingsFor(string name)
        {
            switch (name)
            {
                case "SFX_UI_Hover":
                case "SFX_Card_Hover": return new EffectSettings(EffectCategory.Hover, 0, 0.07f);
                case "SFX_UI_Error": return new EffectSettings(EffectCategory.UI, 2, 2f);
                case "SFX_UI_Click":
                case "SFX_Card_Select":
                case "SFX_Card_Deselect":
                case "SFX_Cosmetic_Equip":
                case "SFX_Hero_Select_Confirm": return new EffectSettings(EffectCategory.UI, 1);
                case "SFX_Timer_Tick_Urgent": return new EffectSettings(EffectCategory.Timer, 3);
                case "SFX_Timer_Timeout":
                case "SFX_LastGuess_Start": return new EffectSettings(EffectCategory.Gameplay, 5, duck: 0.55f);
                case "SFX_Judge_Strike":
                case "SFX_Judge_Ball":
                case "SFX_Judge_Out": return new EffectSettings(EffectCategory.Gameplay, 5, duck: 0.5f);
                case "SFX_Reveal_Busted":
                case "SFX_Reveal_Punished":
                case "SFX_Digit_Reveal": return new EffectSettings(EffectCategory.Gameplay, 5);
                case "SFX_Hero_Paladin_Restore":
                case "SFX_Hero_Warrior_Rush":
                case "SFX_Hero_Wizard_Clairvoyance":
                case "SFX_Hero_Priest_Inquiry":
                case "SFX_Hero_Rogue_Insight":
                case "SFX_Hero_DemonHunter_Seal":
                    return new EffectSettings(EffectCategory.Hero, 4, duck: 0.4f);
                case "SFX_Result_Dim_Drop":
                case "SFX_Result_Card_Reveal":
                case "SFX_Result_Stamp_Win":
                case "SFX_Result_Stamp_Lose":
                case "SFX_Result_Stamp_Draw": return new EffectSettings(EffectCategory.Result, 6);
                case "SFX_Progress_LevelUp":
                case "SFX_Progress_HeroLevelUp":
                case "SFX_Progress_ChallengeComplete":
                case "SFX_Progress_RewardUnlock": return new EffectSettings(EffectCategory.Progress, 4);
                default: return new EffectSettings(EffectCategory.Gameplay, 3);
            }
        }

        public static void LobbyMusic()
        {
            if (_instance == null) return;
            _instance._musicPhase = MusicPhase.Lobby;
            _instance._climaxUntil = 0f;
            Music("BGM_Lobby_Noir");
        }

        public static void StartMatchMusic()
        {
            if (_instance == null) return;
            _instance._musicPhase = MusicPhase.Match;
            _instance._climaxUntil = 0f;
            Music("BGM_Match_Tension");
        }

        public static void ContinueMatchMusic()
        {
            if (_instance == null || _instance._musicPhase != MusicPhase.Match) return;
            Music(Time.unscaledTime < _instance._climaxUntil ? "BGM_Match_Climax" : "BGM_Match_Tension");
        }

        public static void ReportResponse(int reportedStrikes)
        {
            if (_instance == null || _instance._musicPhase != MusicPhase.Match) return;
            if (reportedStrikes == 3) _instance._climaxUntil = Time.unscaledTime + ClimaxHoldSeconds;
            ContinueMatchMusic();
        }

        public static void BeginFinalChance()
        {
            if (_instance == null || _instance._musicPhase == MusicPhase.FinalChance ||
                _instance._musicPhase == MusicPhase.Result) return;
            _instance._musicPhase = MusicPhase.FinalChance;
            _instance._climaxUntil = 0f;
            Play("SFX_LastGuess_Start");
            Music("BGM_Match_FinalChance");
        }

        public static void ResultMusic(bool won, bool draw = false)
        {
            if (_instance == null || _instance._musicPhase == MusicPhase.Result) return;
            _instance._musicPhase = MusicPhase.Result;
            _instance._climaxUntil = 0f;
            StopEffects();
            Music(draw ? "BGM_Draw_Stinger" : won ? "BGM_Win_Stinger" : "BGM_Lose_Stinger", false);
        }

        public static void Music(string name, bool loop = true)
        {
            if (!TryGetClip(name, out var clip)) return;
            if (_instance._musicName == name &&
                (_instance._music.isPlaying || _instance._musicTransition != null)) return;
            if (_instance._musicTransition != null) _instance.StopCoroutine(_instance._musicTransition);
            _instance._musicName = name;
            _instance._musicTransition = _instance.StartCoroutine(_instance.SwitchMusic(clip, loop));
        }

        private IEnumerator SwitchMusic(AudioClip clip, bool loop)
        {
            const float fadeSeconds = 0.35f;
            float startVolume = _musicGain;
            float elapsed = 0f;
            if (_music.isPlaying)
            {
                while (elapsed < fadeSeconds)
                {
                    elapsed += Time.unscaledDeltaTime;
                    _musicGain = Mathf.Lerp(startVolume, 0f, elapsed / fadeSeconds);
                    yield return null;
                }
            }
            _music.Stop();
            _music.clip = clip;
            _music.loop = loop;
            _musicGain = 0f;
            _music.Play();
            elapsed = 0f;
            while (elapsed < fadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                _musicGain = Mathf.Lerp(0f, MusicVolume, elapsed / fadeSeconds);
                yield return null;
            }
            _musicGain = MusicVolume;
            _musicTransition = null;
        }
    }

}
