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
            var clips = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<AudioClip>)
                .Where(clip => clip != null)
                .ToArray();

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
