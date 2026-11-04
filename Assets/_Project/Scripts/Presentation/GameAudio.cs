using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LiarsBatting.Presentation
{
    public class GameAudio : MonoBehaviour
    {
        private const float MusicVolume = 0.28f;
        private const float EffectsVolume = 0.65f;
        private const int EffectVoices = 7;
        private static GameAudio _instance;
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private AudioSource _music;
        private AudioSource[] _effects;
        private readonly int[] _voicePriority = new int[EffectVoices];
        private readonly long[] _voiceOrder = new long[EffectVoices];
        private Coroutine _musicTransition;
        private string _musicName;
        private long _sequence;
        private float _musicGain;
        private float _duckGain = 1f;
        private float _duckTarget = 1f;
        private float _duckUntil;

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
        }

        private void Update()
        {
            if (Time.unscaledTime >= _duckUntil) _duckTarget = 1f;
            _duckGain = Mathf.MoveTowards(_duckGain, _duckTarget, Time.unscaledDeltaTime * 2f);
            _music.volume = _musicGain * _duckGain;
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
            source.volume = EffectsVolume * Mathf.Clamp01(volume);
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
