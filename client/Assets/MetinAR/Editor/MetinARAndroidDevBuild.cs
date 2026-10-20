using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MetinAR.Editor
{
    /// <summary>
    /// Batchmode helper: Android ARM64 IL2CPP development APK (no release signing).
    /// Usage: -executeMethod MetinAR.Editor.MetinARAndroidDevBuild.BuildDevelopmentApk
    ///        -executeMethod MetinAR.Editor.MetinARAndroidDevBuild.BuildWorldMapDevelopmentApk
    ///        -executeMethod MetinAR.Editor.MetinARAndroidDevBuild.BuildArEncounterDevelopmentApk
    /// </summary>
    public static class MetinARAndroidDevBuild
    {
        private const string ApkRelativePath = "Builds/Android/MetinAR-dev.apk";
        private const string WorldMapApkRelativePath = "Builds/Android/MetinAR-worldmap-dev.apk";
        private const string WorldMapScenePath = "Assets/MetinAR/Scenes/WorldMapPrototype.unity";
        private const string ArEncounterApkRelativePath = "Builds/Android/MetinAR-ar-encounter-dev.apk";
        private const string ArEncounterScenePath = "Assets/MetinAR/Scenes/ArEncounterPrototype.unity";

        public static void BuildDevelopmentApk()
        {
            Build(ApkRelativePath, null);
        }

        /// <summary>
        /// Development APK whose first (startup) scene must be WorldMapPrototype.
        /// </summary>
        public static void BuildWorldMapDevelopmentApk()
        {
            Build(WorldMapApkRelativePath, WorldMapScenePath);
        }

        /// <summary>
        /// METINAR-006: development APK with the world map as startup scene (index 0) and the AR encounter scene included.
        /// </summary>
        public static void BuildArEncounterDevelopmentApk()
        {
            Build(ArEncounterApkRelativePath, WorldMapScenePath, ArEncounterScenePath);
        }

        private static void Build(string apkRelativePath, string requiredStartupScene, string requiredScene = null)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string apkPath = Path.Combine(projectRoot, apkRelativePath);
            string apkDir = Path.GetDirectoryName(apkPath)!;
            Directory.CreateDirectory(apkDir);

            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            EditorUserBuildSettings.androidBuildType = AndroidBuildType.Development;

            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.useCustomKeystore = false;

            string[] scenes = GetEnabledScenes();
            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled scenes in Build Settings.");
            }

            if (requiredStartupScene != null && scenes[0] != requiredStartupScene)
            {
                Debug.LogError("[MetinARAndroidDevBuild] startup scene is " + scenes[0] + ", expected " + requiredStartupScene);
                EditorApplication.Exit(1);
                return;
            }

            if (requiredScene != null && Array.IndexOf(scenes, requiredScene) < 0)
            {
                Debug.LogError("[MetinARAndroidDevBuild] required scene not enabled: " + requiredScene);
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[MetinARAndroidDevBuild] scenes=" + string.Join(",", scenes));

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = apkPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError(
                    "[MetinARAndroidDevBuild] FAILED result="
                    + summary.result
                    + " errors="
                    + summary.totalErrors
                    + " path="
                    + apkPath);
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log(
                "[MetinARAndroidDevBuild] SUCCESS path="
                + apkPath
                + " sizeBytes="
                + (File.Exists(apkPath) ? new FileInfo(apkPath).Length : 0)
                + " time="
                + summary.totalTime);
            EditorApplication.Exit(0);
        }

        private static string[] GetEnabledScenes()
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                {
                    list.Add(scene.path);
                }
            }

            return list.ToArray();
        }
    }
}
