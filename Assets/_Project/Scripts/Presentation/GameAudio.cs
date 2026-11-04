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
        private const int EffectVoices = 7;
        private static GameAudio _instance;
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private AudioSource _music;
        private AudioSource[] _effects;
        private readonly int[] _voicePriority = new int[EffectVoices];
        private readonly long[] _voiceOrder = new long[EffectVoices];
        private readonly float[] _voiceGain = new float[EffectVoices];
        private Coroutine _musicTransition;
        private string _musicName;
        private long _sequence;
        private float _musicGain;
        private float _duckGain = 1f;
        private float _duckTarget = 1f;
        private float _duckUntil;
        private float _musicVolume;
        private float _effectsVolume;
        private bool _musicMuted;
        private bool _effectsMuted;

        public static float MusicLevel => _instance != null ? _instance._musicVolume : DefaultVolume;
        public static float EffectsLevel => _instance != null ? _instance._effectsVolume : DefaultVolume;
        public static bool MusicMuted => _instance != null && _instance._musicMuted;
        public static bool EffectsMuted => _instance != null && _instance._effectsMuted;

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
                    if (clip != null) _clips[clip.name] = clip;

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
            if (Time.unscaledTime >= _duckUntil) _duckTarget = 1f;
            _duckGain = Mathf.MoveTowards(_duckGain, _duckTarget, Time.unscaledDeltaTime * 2f);
            ApplyMusicVolume();
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
            if (_instance == null || !_instance._clips.TryGetValue(name, out var clip)) return;
            var audio = _instance;
            int priority = EffectPriority(name);
            int limit = priority == 2 ? 4 : priority == 1 ? 2 : 1;
            int sameCount = 0;
            int oldestSame = -1;
            int free = -1;
            int replace = -1;
            for (int i = 0; i < audio._effects.Length; i++)
            {
                if (!audio._effects[i].isPlaying)
                {
                    if (free < 0) free = i;
                    continue;
                }
                if (audio._voicePriority[i] == priority)
                {
                    sameCount++;
                    if (oldestSame < 0 || audio._voiceOrder[i] < audio._voiceOrder[oldestSame])
                        oldestSame = i;
                }
                if (audio._voicePriority[i] <= priority &&
                    (replace < 0 || audio._voicePriority[i] < audio._voicePriority[replace] ||
                     (audio._voicePriority[i] == audio._voicePriority[replace] &&
                      audio._voiceOrder[i] < audio._voiceOrder[replace])))
                    replace = i;
            }
            int chosen = sameCount >= limit ? oldestSame : free >= 0 ? free : replace;
            if (chosen < 0) return;
            var source = audio._effects[chosen];
            source.Stop();
            source.clip = clip;
            source.pitch = pitch;
            audio._voiceGain[chosen] = EffectsVolume * Mathf.Clamp01(volume) * EffectBalance(name);
            source.volume = audio._effectsMuted ? 0f : audio._voiceGain[chosen] * audio._effectsVolume;
            audio._voicePriority[chosen] = priority;
            audio._voiceOrder[chosen] = ++audio._sequence;
            source.Play();

            float duck = name.StartsWith("SFX_Hero_") ? 0.4f :
                name.StartsWith("SFX_Judge_") ? 0.5f : 1f;
            if (duck < 1f)
            {
                audio._duckTarget = Mathf.Min(audio._duckTarget, duck);
                audio._duckUntil = Mathf.Max(audio._duckUntil,
                    Time.unscaledTime + clip.length / Mathf.Max(0.1f, pitch) + 0.5f);
            }
        }

        // Attenuate louder clips, based on their measured RMS; leave quieter UI sounds untouched.
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
                default: return 1f;
            }
        }

        private static int EffectPriority(string name)
        {
            if (name.EndsWith("_Hover")) return 0;
            if (name == "SFX_Timer_Timeout" || name.StartsWith("SFX_Judge_") ||
                name.StartsWith("SFX_Reveal_") || name.StartsWith("SFX_Result_Stamp_"))
                return 2;
            return 1;
        }

        public static void Music(string name, bool loop = true)
        {
            if (_instance == null || !_instance._clips.TryGetValue(name, out var clip)) return;
            if (_instance._musicName == name && _instance._music.isPlaying) return;
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
