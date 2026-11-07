using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MetinAR.Editor
{
    /// <summary>
    /// METINAR-008B P8: side-by-side map test APK (controller decision on PR #28, option b).
    ///   -executeMethod MetinAR.Editor.MapPocP8AndroidBuild.BuildP8Apk
    ///   → Builds/Android/MetinAR-mappoc-p8-dev.apk, scene [WorldMapOsmPoc],
    ///     applicationId com.metinar.game.mappoc, label "MetinAR Mapa TEST", ARM64 IL2CPP development build.
    /// Every PlayerSettings / EditorUserBuildSettings value it changes is captured first and restored in a finally
    /// block (also after a failed build or an exception), then saved, so ProjectSettings stay as they were.
    /// EditorApplication.Exit is called only after the restore has run.
    /// </summary>
    public static class MapPocP8AndroidBuild
    {
        private const string Tag = "[MapPocP8AndroidBuild] ";

        public static void BuildP8Apk()
        {
            int exitCode = 1;
            try
            {
                exitCode = BuildP8ApkInternal() ? 0 : 1;
            }
            catch (Exception e)
            {
                Debug.LogError(Tag + "EXCEPTION " + e);
                exitCode = 1;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        private static bool BuildP8ApkInternal()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string scene = OsmMapPocSceneBuilder.ScenePath;
            if (!File.Exists(Path.Combine(projectRoot, scene)))
            {
                Debug.LogError(Tag + "missing scene " + scene);
                return false;
            }

            string apkPath = Path.Combine(projectRoot, MapPocP8BuildPlan.ApkRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(apkPath)!);

            var access = new UnityBuildSettingsAccess();
            bool ok = MapPocP8BuildPlan.RunWithVariant(
                access,
                original => MapPocP8BuildPlan.VariantFrom(original, (int)ScriptingImplementation.IL2CPP, (int)AndroidArchitecture.ARM64,
                    (int)AndroidBuildSystem.Gradle, (int)AndroidBuildType.Development),
                () =>
                {
                    Debug.Log(Tag + "variant applied: " + access.Capture());
                    var options = new BuildPlayerOptions
                    {
                        scenes = new[] { scene },
                        locationPathName = apkPath,
                        target = BuildTarget.Android,
                        options = BuildOptions.Development
                    };

                    BuildReport report = BuildPipeline.BuildPlayer(options);
                    BuildSummary summary = report.summary;
                    if (summary.result != BuildResult.Succeeded)
                    {
                        Debug.LogError(Tag + "FAILED result=" + summary.result + " errors=" + summary.totalErrors);
                        return false;
                    }

                    Debug.Log(Tag + "SUCCESS path=" + apkPath + " sizeBytes=" + new FileInfo(apkPath).Length + " time=" + summary.totalTime
                              + " scene=" + scene + " id=" + MapPocP8BuildPlan.VariantApplicationIdentifier);
                    return true;
                },
                out BuildSettingsSnapshot captured);

            BuildSettingsSnapshot after = access.Capture();
            if (!after.SameAs(captured))
            {
                Debug.LogError(Tag + "RESTORE MISMATCH before=" + captured + " after=" + after);
                return false;
            }

            Debug.Log(Tag + "settings restored: " + after);
            return ok;
        }

        private sealed class UnityBuildSettingsAccess : IBuildSettingsAccess
        {
            private static readonly NamedBuildTarget Android = NamedBuildTarget.Android;

            public BuildSettingsSnapshot Capture()
            {
                return new BuildSettingsSnapshot
                {
                    ApplicationIdentifier = PlayerSettings.GetApplicationIdentifier(Android),
                    ProductName = PlayerSettings.productName,
                    ScriptingBackend = (int)PlayerSettings.GetScriptingBackend(Android),
                    TargetArchitectures = (int)PlayerSettings.Android.targetArchitectures,
                    UseCustomKeystore = PlayerSettings.Android.useCustomKeystore,
                    BuildAppBundle = EditorUserBuildSettings.buildAppBundle,
                    AndroidBuildSystem = (int)EditorUserBuildSettings.androidBuildSystem,
                    AndroidBuildType = (int)EditorUserBuildSettings.androidBuildType
                };
            }

            public void Apply(BuildSettingsSnapshot v)
            {
                foreach (Action step in RestoreSteps(v))
                {
                    step();
                }
            }

            public IEnumerable<Action> RestoreSteps(BuildSettingsSnapshot v)
            {
                yield return () => PlayerSettings.SetApplicationIdentifier(Android, v.ApplicationIdentifier);
                yield return () => PlayerSettings.productName = v.ProductName;
                yield return () => PlayerSettings.SetScriptingBackend(Android, (ScriptingImplementation)v.ScriptingBackend);
                yield return () => PlayerSettings.Android.targetArchitectures = (AndroidArchitecture)v.TargetArchitectures;
                yield return () => PlayerSettings.Android.useCustomKeystore = v.UseCustomKeystore;
                yield return () => EditorUserBuildSettings.buildAppBundle = v.BuildAppBundle;
                yield return () => EditorUserBuildSettings.androidBuildSystem = (AndroidBuildSystem)v.AndroidBuildSystem;
                yield return () => EditorUserBuildSettings.androidBuildType = (AndroidBuildType)v.AndroidBuildType;
            }

            public void Persist()
            {
                AssetDatabase.SaveAssets();
            }
        }
    }
}
