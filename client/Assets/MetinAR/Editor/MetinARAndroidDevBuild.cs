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
    /// </summary>
    public static class MetinARAndroidDevBuild
    {
        private const string ApkRelativePath = "Builds/Android/MetinAR-dev.apk";

        public static void BuildDevelopmentApk()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string apkPath = Path.Combine(projectRoot, ApkRelativePath);
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
