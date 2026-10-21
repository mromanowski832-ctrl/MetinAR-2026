using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MetinAR.Audition;
using MetinAR.Editor.Audio;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;

namespace MetinAR.Editor.Audition
{
    /// <summary>
    /// Separate listening-test APK for the A31 (com.metinar.audiotest, "MetinAR AudioTest"). Never overwrites
    /// com.metinar.game: the application identifier and product name are set ONLY for the duration of the build and
    /// restored in a finally block (check `git status` of ProjectSettings afterwards). Branch tools/audio-audition only;
    /// not part of PR #43 / #62.
    ///
    /// Unity.exe -batchmode -nographics -projectPath client -buildTarget Android
    ///   -executeMethod MetinAR.Editor.Audition.AudioAuditionBuild.BuildBatch -auditionSourceSha &lt;git sha&gt; -logFile ...
    /// Output: Builds/audio-audition/MetinAR-AudioTest-&lt;sha7&gt;.apk (Builds/ is git-ignored).
    /// </summary>
    public static class AudioAuditionBuild
    {
        public const string AuditionPackage = "com.metinar.audiotest";
        public const string AuditionProduct = "MetinAR AudioTest";
        public const string GamePackage = "com.metinar.game";
        public const string ScenePath = "Assets/MetinAR/Audition/AudioAudition.unity";
        public const int ExpectedSfxCount = 27;   // #43 (13) + #62 (14)

        public static void CreateSceneBatch()
        {
            int code = 1;
            try
            {
                CreateScene(GetArg("-auditionSourceSha") ?? "unknown");
                code = 0;
            }
            catch (Exception e)
            {
                Debug.LogError("[AudioAudition] scene FAILED: " + e);
            }

            EditorApplication.Exit(code);
        }

        public static void BuildBatch()
        {
            int code = 1;
            string originalId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            string originalProduct = PlayerSettings.productName;
            ScriptingImplementation originalBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android);
            AndroidArchitecture originalArch = PlayerSettings.Android.targetArchitectures;
            try
            {
                if (originalId != GamePackage)
                {
                    throw new InvalidOperationException("unexpected Android applicationIdentifier " + originalId + " (expected " + GamePackage + ")");
                }

                string sha = GetArg("-auditionSourceSha") ?? "unknown";
                CreateScene(sha);

                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AuditionPackage);
                PlayerSettings.productName = AuditionProduct;
                if (GetArg("-auditionBackend") == "mono")
                {
                    // faster test build (no IL2CPP C++ step); ARMv7 runs on the A31. Restored in finally.
                    PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.Mono2x);
                    PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7;
                }

                EditorUserBuildSettings.buildAppBundle = false;
                EditorUserBuildSettings.androidBuildType = AndroidBuildType.Development;
                Debug.Log("[AudioAudition] building id=" + PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) +
                          " product=" + PlayerSettings.productName + " backend=" + PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) +
                          " arch=" + PlayerSettings.Android.targetArchitectures);

                string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
                string apk = Path.Combine(projectRoot, "Builds", "audio-audition", "MetinAR-AudioTest-" + (sha.Length > 7 ? sha.Substring(0, 7) : sha) + ".apk");
                Directory.CreateDirectory(Path.GetDirectoryName(apk)!);
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = apk,
                    target = BuildTarget.Android,
                    options = BuildOptions.Development,
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError("[AudioAudition] FAILED result=" + report.summary.result + " errors=" + report.summary.totalErrors);
                }
                else
                {
                    Debug.Log("[AudioAudition] SUCCESS path=" + apk + " sizeBytes=" + new FileInfo(apk).Length + " time=" + report.summary.totalTime);
                    code = 0;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[AudioAudition] FAILED: " + e);
            }
            finally
            {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, originalId);
                PlayerSettings.productName = originalProduct;
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, originalBackend);
                PlayerSettings.Android.targetArchitectures = originalArch;
                AssetDatabase.SaveAssets();
                Debug.Log("[AudioAudition] restored id=" + PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) + " product=" + PlayerSettings.productName +
                          " backend=" + PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) + " arch=" + PlayerSettings.Android.targetArchitectures);
            }

            EditorApplication.Exit(code);
        }

        public static void CreateScene(string sourceSha)
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AudioMixerBuilder.MixerPath);
            if (mixer == null)
            {
                throw new InvalidOperationException("mixer missing: " + AudioMixerBuilder.MixerPath);
            }

            AudioMixerGroup sfx = mixer.FindMatchingGroups("Master/SFX").FirstOrDefault(g => g.name == "SFX");
            AudioMixerGroup ui = mixer.FindMatchingGroups("Master/UI").FirstOrDefault(g => g.name == "UI");
            if (sfx == null || ui == null)
            {
                throw new InvalidOperationException("mixer groups SFX / UI not found");
            }

            var entries = new List<AudioAuditionController.Entry>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioPipelineRules.SfxFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AudioCategory c = AudioPipelineRules.ClassifyPath(path);
                if (!AudioPipelineRules.IsSfx(c))
                {
                    continue;
                }

                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                entries.Add(new AudioAuditionController.Entry
                {
                    label = Path.GetFileNameWithoutExtension(path).Replace("SFX_", string.Empty),
                    group = c == AudioCategory.UiSfx || c == AudioCategory.StingerSfx ? "UI" : "SFX",
                    peakDbfs = PeakDbfs(path),
                    clip = clip,
                });
            }

            entries = entries.OrderBy(e => e.group == "UI" ? 1 : 0).ThenBy(e => e.label, StringComparer.Ordinal).ToList();
            if (entries.Count != ExpectedSfxCount || entries.Any(e => e.clip == null))
            {
                throw new InvalidOperationException("expected " + ExpectedSfxCount + " SFX clips, found " + entries.Count);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Camera");
            Camera camera = cam.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
            cam.AddComponent<AudioListener>();

            var go = new GameObject("AudioAudition");
            var ctl = go.AddComponent<AudioAuditionController>();
            ctl.mixer = mixer;
            ctl.sfxGroup = sfx;
            ctl.uiGroup = ui;
            ctl.sfxSource = NewSource(go, "SfxSource", sfx);
            ctl.uiSource = NewSource(go, "UiSource", ui);
            ctl.loopSource = NewSource(go, "AuraLoopSource", sfx);
            ctl.auraStart = Clip("Combat/SFX_Combat_AuraStart_01.wav");
            ctl.auraLoop = Clip("Combat/SFX_Combat_AuraLoop_01.wav");
            ctl.auraStop = Clip("Combat/SFX_Combat_AuraStop_01.wav");
            ctl.entries = entries.ToArray();
            ctl.sourceSha = sourceSha.Length > 7 ? sourceSha.Substring(0, 7) : sourceSha;

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException("could not save " + ScenePath);
            }

            Debug.Log("[AudioAudition] scene " + ScenePath + " clips=" + entries.Count + " (SFX " + entries.Count(e => e.group == "SFX") + ", UI " + entries.Count(e => e.group == "UI") + ")");
        }

        private static AudioSource NewSource(GameObject parent, string name, AudioMixerGroup group)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            var s = child.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;   // 2D: listening test of the clips themselves
            s.outputAudioMixerGroup = group;
            return s;
        }

        private static AudioClip Clip(string relative)
        {
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioPipelineRules.SfxFolder + "/" + relative);
            if (c == null)
            {
                throw new InvalidOperationException("clip missing: " + relative);
            }

            return c;
        }

        private static float PeakDbfs(string assetPath)
        {
            string full = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, assetPath);
            short[] x = AudioFormatInfo.ReadPcm16(File.ReadAllBytes(full));
            int peak = 0;
            foreach (short v in x)
            {
                peak = Math.Max(peak, Math.Abs((int)v));
            }

            return peak > 0 ? 20f * Mathf.Log10(peak / 32768f) : -96f;
        }

        private static string GetArg(string name)
        {
            string[] a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }
    }
}
