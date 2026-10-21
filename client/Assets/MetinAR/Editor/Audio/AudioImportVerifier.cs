using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace MetinAR.Editor.Audio
{
    /// <summary>
    /// METINAR-018: editor-side check that Unity really applies the audio pipeline profile. For every audio file under
    /// Assets/MetinAR/Audio it reads the AudioImporter (default + Android + iOS sample settings, mono, background load)
    /// and the imported AudioClip (channels, length, load type) and compares them with AudioPipelineRules.
    /// Batch mode: Unity.exe -batchmode -nographics -projectPath client -executeMethod MetinAR.Editor.Audio.AudioImportVerifier.RunBatch -quit
    /// Exit code 0 = all clips OK, 1 = at least one problem. Read-only: it never changes import settings.
    /// </summary>
    public static class AudioImportVerifier
    {
        [MenuItem("MetinAR/Audio/Verify Audio Import Settings")]
        public static void RunMenu()
        {
            int problems = Run();
            Debug.Log("[AudioVerify] finished, problems=" + problems);
        }

        public static void RunBatch()
        {
            int problems = Run();
            EditorApplication.Exit(problems == 0 ? 0 : 1);
        }

        public static int Run()
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioPipelineRules.Root });
            int problems = 0;
            int checkedClips = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var list = new List<string>();
                Check(path, list);
                checkedClips++;
                if (list.Count == 0)
                {
                    Debug.Log("[AudioVerify] OK   " + path + " " + Describe(path));
                }
                else
                {
                    problems += list.Count;
                    foreach (string p in list)
                    {
                        Debug.LogError("[AudioVerify] FAIL " + path + ": " + p);
                    }
                }
            }

            if (checkedClips == 0)
            {
                Debug.LogError("[AudioVerify] FAIL no AudioClip found under " + AudioPipelineRules.Root);
                problems++;
            }

            problems += CheckMixer();
            Debug.Log("[AudioVerify] clips=" + checkedClips + " problems=" + problems + " result=" + (problems == 0 ? "PASS" : "FAIL"));
            return problems;
        }

        /// <summary>The project mixer (if committed) must load and contain Master with the child groups SFX, UI, Music.</summary>
        private static int CheckMixer()
        {
            if (!File.Exists(AudioMixerBuilder.MixerPath))
            {
                Debug.LogWarning("[AudioVerify] WARN no mixer at " + AudioMixerBuilder.MixerPath);
                return 0;
            }

            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AudioMixerBuilder.MixerPath);
            if (mixer == null)
            {
                Debug.LogError("[AudioVerify] FAIL mixer does not load: " + AudioMixerBuilder.MixerPath);
                return 1;
            }

            int problems = 0;
            AudioMixerGroup[] all = mixer.FindMatchingGroups(string.Empty);
            var names = new List<string>();
            foreach (AudioMixerGroup g in all)
            {
                names.Add(g.name);
            }

            AudioMixerGroup[] master = mixer.FindMatchingGroups(AudioMixerBuilder.MasterGroup);
            if (master.Length == 0 || master[0].name != AudioMixerBuilder.MasterGroup)
            {
                Debug.LogError("[AudioVerify] FAIL mixer has no Master group");
                problems++;
            }

            foreach (string child in AudioMixerBuilder.ChildGroups)
            {
                AudioMixerGroup[] found = mixer.FindMatchingGroups(AudioMixerBuilder.MasterGroup + "/" + child);
                if (found.Length != 1 || found[0].name != child)
                {
                    Debug.LogError("[AudioVerify] FAIL mixer group Master/" + child + " missing (found " + found.Length + ")");
                    problems++;
                }
            }

            Debug.Log("[AudioVerify] mixer " + AudioMixerBuilder.MixerPath + " groups=[" + string.Join(", ", names) + "] " + (problems == 0 ? "OK" : "FAIL"));
            return problems;
        }

        private static string Describe(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            AudioImporterSampleSettings a = importer.GetOverrideSampleSettings(MetinAR.Editor.Audio.AudioImportPostprocessor.AndroidPlatform);
            return string.Format("len={0:0.000}s ch={1} hz={2} clipLoadType={3} android={4}/{5} preload={6} mono={7}",
                clip.length, clip.channels, clip.frequency, clip.loadType, a.compressionFormat, a.loadType, a.preloadAudioData, importer.forceToMono);
        }

        private static void Check(string path, List<string> problems)
        {
            AudioCategory category = AudioPipelineRules.ClassifyPath(path);
            AudioImportProfile profile = AudioPipelineRules.GetProfile(category);
            if (profile == null)
            {
                problems.Add("outside the pipeline folders");
                return;
            }

            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
            {
                problems.Add("no AudioImporter");
                return;
            }

            if (importer.forceToMono != profile.ForceToMono)
            {
                problems.Add("forceToMono=" + importer.forceToMono);
            }

            if (importer.loadInBackground != profile.LoadInBackground)
            {
                problems.Add("loadInBackground=" + importer.loadInBackground);
            }

            CheckSettings("default", importer.defaultSampleSettings, profile, problems);
            foreach (string platform in new[] { AudioImportPostprocessor.AndroidPlatform, AudioImportPostprocessor.IosPlatform })
            {
                if (!importer.ContainsSampleSettingsOverride(platform))
                {
                    problems.Add(platform + " override missing");
                    continue;
                }

                CheckSettings(platform, importer.GetOverrideSampleSettings(platform), profile, problems);
            }

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                problems.Add("AudioClip did not import");
                return;
            }

            if (profile.ForceToMono && clip.channels != 1)
            {
                problems.Add("clip channels=" + clip.channels);
            }

            if ((int)clip.loadType != profile.LoadType)
            {
                problems.Add("clip loadType=" + clip.loadType);
            }

            AudioBudget budget = AudioPipelineRules.GetBudgetForPath(path);
            if (budget != null && clip.length > budget.MaxSeconds)
            {
                problems.Add("clip length " + clip.length + " s > " + budget.MaxSeconds);
            }

            if (!File.Exists(path + ".meta"))
            {
                problems.Add(".meta missing");
            }
        }

        private static void CheckSettings(string label, AudioImporterSampleSettings s, AudioImportProfile profile, List<string> problems)
        {
            if ((int)s.loadType != profile.LoadType)
            {
                problems.Add(label + ".loadType=" + s.loadType);
            }

            if ((int)s.compressionFormat != profile.CompressionFormat)
            {
                problems.Add(label + ".compressionFormat=" + s.compressionFormat);
            }

            if (s.preloadAudioData != profile.PreloadAudioData)
            {
                problems.Add(label + ".preloadAudioData=" + s.preloadAudioData);
            }
        }
    }
}
