using System;
using System.Linq;
using LiarsBatting.Presentation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LiarsBatting.Editor
{
    [InitializeOnLoad]
    public class AudioCatalogBuilder : AssetPostprocessor, IPreprocessBuildWithReport
    {
        private const string AudioFolder = "Assets/_Project/Audio";
        private const string CatalogPath = "Assets/_Project/Resources/AudioCatalog.asset";
        private static bool _syncQueued;

        static AudioCatalogBuilder()
        {
            QueueSync();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) Sync();
                else if (state == PlayModeStateChange.EnteredEditMode) QueueSync();
            };
        }

        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report) => Sync();

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(AudioFolder + "/", StringComparison.Ordinal)) return;
            ApplyAudioSettings((AudioImporter)assetImporter, assetPath);
        }

        private static bool ApplyAudioSettings(AudioImporter importer, string path)
        {
            bool longMusic = path.Contains("/BGM/") && !path.Contains("_Stinger");
            bool voice = path.Contains("/VO/");
            var targetLoadType = longMusic ? AudioClipLoadType.Streaming
                : voice ? AudioClipLoadType.CompressedInMemory
                : AudioClipLoadType.DecompressOnLoad;
            var targetCompression = (longMusic || voice)
                ? AudioCompressionFormat.Vorbis
                : AudioCompressionFormat.PCM;
            bool targetPreload = !longMusic;
            bool targetBackground = longMusic;

            var settings = importer.defaultSampleSettings;
            bool changed = settings.loadType != targetLoadType ||
                settings.compressionFormat != targetCompression ||
                settings.sampleRateSetting != AudioSampleRateSetting.PreserveSampleRate ||
                !Mathf.Approximately(settings.quality, 0.85f) ||
                settings.preloadAudioData != targetPreload ||
                importer.loadInBackground != targetBackground ||
                importer.forceToMono;

            if (!changed) return false;

            settings.loadType = targetLoadType;
            settings.compressionFormat = targetCompression;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.quality = 0.85f;
            settings.preloadAudioData = targetPreload;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = targetBackground;
            importer.forceToMono = false;
            return true;
        }

        public static void RebuildCatalog()
        {
            Sync();
            var catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
            if (catalog == null || catalog.Clips == null || catalog.Clips.Length == 0)
                throw new InvalidOperationException("Audio catalog could not be populated.");
            Debug.Log($"Audio catalog ready: {catalog.Clips.Length} clips.");
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
            string[] moved, string[] movedFrom)
        {
            if (TouchesAudio(imported) || TouchesAudio(deleted) ||
                TouchesAudio(moved) || TouchesAudio(movedFrom))
                QueueSync();
        }

        private static bool TouchesAudio(string[] paths)
            => paths.Any(path => path == AudioFolder || path.StartsWith(AudioFolder + "/", StringComparison.Ordinal));

        private static void QueueSync()
        {
            if (_syncQueued) return;
            _syncQueued = true;
            EditorApplication.delayCall += () =>
            {
                _syncQueued = false;
                if (!EditorApplication.isPlayingOrWillChangePlaymode) Sync();
            };
        }

        private static void Sync()
        {
            if (!AssetDatabase.IsValidFolder(AudioFolder)) return;
            var paths = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            foreach (string path in paths)
            {
                if (AssetImporter.GetAtPath(path) is AudioImporter importer && ApplyAudioSettings(importer, path))
                    importer.SaveAndReimport();
            }

            var clips = paths
                .Select(AssetDatabase.LoadAssetAtPath<AudioClip>)
                .Where(clip => clip != null)
                .ToArray();
            var duplicates = clips.GroupBy(clip => clip.name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
            if (duplicates.Length > 0)
                throw new InvalidOperationException("Duplicate audio names: " + string.Join(", ", duplicates));
            var names = new System.Collections.Generic.HashSet<string>(clips.Select(clip => clip.name));
            var missing = AudioCatalog.RequiredNames.Where(name => !names.Contains(name)).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException("Required audio clips are missing: " + string.Join(", ", missing));

            var catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<AudioCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            if (catalog.Clips != null && catalog.Clips.SequenceEqual(clips)) return;
            catalog.Clips = clips;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }
    }
}
