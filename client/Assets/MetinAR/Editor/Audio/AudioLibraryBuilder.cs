using System;
using System.Collections.Generic;
using MetinAR.Audio;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace MetinAR.Editor.Audio
{
    /// <summary>
    /// METINAR-040 (#85): (re)writes Resources/Audio/MetinARAudioLibrary.asset from <see cref="AudioCueTable"/> —
    /// every cue/loop clip id → its imported AudioClip, plus the project mixer. Run after editing the cue table:
    /// MetinAR ▸ Audio ▸ Rebuild Audio Library, or
    /// Unity -batchmode -nographics -projectPath client -executeMethod MetinAR.Editor.Audio.AudioLibraryBuilder.BuildBatch
    /// </summary>
    public static class AudioLibraryBuilder
    {
        public const string LibraryFolder = "Assets/MetinAR/Resources/Audio";
        public const string LibraryPath = LibraryFolder + "/MetinARAudioLibrary.asset";

        [MenuItem("MetinAR/Audio/Rebuild Audio Library")]
        public static void BuildMenu() => Build();

        public static void BuildBatch()
        {
            bool ok;
            try
            {
                ok = Build();
            }
            catch (Exception e)
            {
                Debug.LogError("[AudioLibraryBuilder] FAIL " + e);
                ok = false;
            }

            EditorApplication.Exit(ok ? 0 : 1);
        }

        public static bool Build()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AudioMixerBuilder.MixerPath);
            if (mixer == null)
            {
                Debug.LogError("[AudioLibraryBuilder] FAIL mixer missing: " + AudioMixerBuilder.MixerPath);
                return false;
            }

            var entries = new List<AudioLibrary.Entry>();
            bool ok = true;
            foreach (string id in AudioCueTable.AllClipIds())
            {
                string path = AudioPipelineRules.Root + "/" + id;
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null)
                {
                    Debug.LogError("[AudioLibraryBuilder] FAIL clip missing: " + path);
                    ok = false;
                    continue;
                }

                entries.Add(new AudioLibrary.Entry { id = id, clip = clip });
            }

            if (!ok) return false;
            if (!AssetDatabase.IsValidFolder("Assets/MetinAR/Resources")) AssetDatabase.CreateFolder("Assets/MetinAR", "Resources");
            if (!AssetDatabase.IsValidFolder(LibraryFolder)) AssetDatabase.CreateFolder("Assets/MetinAR/Resources", "Audio");

            var library = AssetDatabase.LoadAssetAtPath<AudioLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<AudioLibrary>();
                library.Set(mixer, entries);
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            else
            {
                library.Set(mixer, entries);
                EditorUtility.SetDirty(library);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[AudioLibraryBuilder] wrote " + LibraryPath + " clips=" + entries.Count);
            return true;
        }
    }
}
