using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MetinAR.Editor
{
    /// <summary>
    /// Batchmode helper: Android ARM64 IL2CPP development APK (no release signing).
    /// Usage: -executeMethod MetinAR.Editor.MetinARAndroidDevBuild.BuildDevelopmentApk
    ///        -executeMethod MetinAR.Editor.MetinARAndroidDevBuild.BuildWorldMapDevelopmentApk
    ///        -executeMethod MetinAR.Editor.MetinARAndroidDevBuild.BuildArEncounterDevelopmentApk
    ///        -executeMethod MetinAR.Editor.MetinARAndroidDevBuild.BuildArEncounterQaApk   (QA variant, com.metinar.game.qa)
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
            ExitWith(Build(ApkRelativePath, null));
        }

        /// <summary>
        /// Development APK whose first (startup) scene must be WorldMapPrototype.
        /// </summary>
        public static void BuildWorldMapDevelopmentApk()
        {
            ExitWith(Build(WorldMapApkRelativePath, WorldMapScenePath));
        }

        /// <summary>
        /// METINAR-006: development APK with the world map as startup scene (index 0) and the AR encounter scene included.
        /// </summary>
        public static void BuildArEncounterDevelopmentApk()
        {
            ExitWith(Build(ArEncounterApkRelativePath, WorldMapScenePath, ArEncounterScenePath));
        }

        /// <summary>
        /// METINAR-037 (#73): the same scenes as <see cref="BuildArEncounterDevelopmentApk"/>, but with the QA identity from
        /// <see cref="QaApkVariant"/> (applicationId com.metinar.game.qa, label "MetinAR QA", own APK file), so the QA
        /// build installs next to com.metinar.game and never replaces it. The project's applicationIdentifier and
        /// productName are captured first and restored in a finally block (also after a failed build or an exception)
        /// and saved, so ProjectSettings stay as committed. EditorApplication.Exit runs only after the restore.
        /// </summary>
        public static void BuildArEncounterQaApk()
        {
            NamedBuildTarget android = NamedBuildTarget.Android;
            string originalId = PlayerSettings.GetApplicationIdentifier(android);
            string originalName = PlayerSettings.productName;
            string problem = QaApkVariant.SideBySideProblem(originalId, QaApkVariant.ApplicationIdentifier);
            if (problem != null)
            {
                Debug.LogError(Tag + "FAILED " + problem);
                ExitWith(false);
                return;
            }

            bool ok = false;
            try
            {
                PlayerSettings.SetApplicationIdentifier(android, QaApkVariant.ApplicationIdentifier);
                PlayerSettings.productName = QaApkVariant.ProductName;
                Debug.Log(Tag + "QA variant id=" + QaApkVariant.ApplicationIdentifier + " name=" + QaApkVariant.ProductName
                          + " (project id=" + originalId + ")");
                ok = Build(QaApkVariant.ApkRelativePath, WorldMapScenePath, ArEncounterScenePath);
            }
            catch (Exception e)
            {
                Debug.LogError(Tag + "FAILED exception " + e);
                ok = false;
            }
            finally
            {
                PlayerSettings.SetApplicationIdentifier(android, originalId);
                PlayerSettings.productName = originalName;
                AssetDatabase.SaveAssets();
            }

            string restoredId = PlayerSettings.GetApplicationIdentifier(android);
            if (restoredId != originalId || PlayerSettings.productName != originalName)
            {
                Debug.LogError(Tag + "RESTORE MISMATCH id=" + restoredId + " name=" + PlayerSettings.productName);
                ok = false;
            }
            else
            {
                Debug.Log(Tag + "settings restored id=" + restoredId + " name=" + originalName);
            }

            ExitWith(ok);
        }

        private const string Tag = "[MetinARAndroidDevBuild] ";

        private static void ExitWith(bool ok)
        {
            EditorApplication.Exit(ok ? 0 : 1);
        }

        private static bool Build(string apkRelativePath, string requiredStartupScene, string requiredScene = null)
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
                return false;
            }

            if (requiredScene != null && Array.IndexOf(scenes, requiredScene) < 0)
            {
                Debug.LogError("[MetinARAndroidDevBuild] required scene not enabled: " + requiredScene);
                return false;
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
                return false;
            }

            Debug.Log(
                "[MetinARAndroidDevBuild] SUCCESS path="
                + apkPath
                + " sizeBytes="
                + (File.Exists(apkPath) ? new FileInfo(apkPath).Length : 0)
                + " time="
                + summary.totalTime
                + " id="
                + PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android));
            return true;
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
