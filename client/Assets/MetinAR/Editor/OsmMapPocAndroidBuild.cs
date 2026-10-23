using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MetinAR.Editor
{
    /// <summary>
    /// METINAR-008B: ARM64 IL2CPP development APKs with an explicit scene list (EditorBuildSettings untouched).
    ///   -executeMethod MetinAR.Editor.OsmMapPocAndroidBuild.BuildPocApk       → Builds/Android/MetinAR-osm-poc-dev.apk  [WorldMapOsmPoc]
    ///   -executeMethod MetinAR.Editor.OsmMapPocAndroidBuild.BuildBaselineApk  → Builds/Android/MetinAR-osm-baseline-dev.apk [WorldMapPrototype]
    /// The baseline uses identical options so the size delta isolates the POC scene + map assets.
    /// </summary>
    public static class OsmMapPocAndroidBuild
    {
        private const string PocApk = "Builds/Android/MetinAR-osm-poc-dev.apk";
        private const string BaselineApk = "Builds/Android/MetinAR-osm-baseline-dev.apk";
        private const string BaselineScene = "Assets/MetinAR/Scenes/WorldMapPrototype.unity";

        public static void BuildPocApk()
        {
            Build(PocApk, OsmMapPocSceneBuilder.ScenePath);
        }

        public static void BuildBaselineApk()
        {
            Build(BaselineApk, BaselineScene);
        }

        private static void Build(string apkRelativePath, string scene)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string apkPath = Path.Combine(projectRoot, apkRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(apkPath)!);
            if (!File.Exists(Path.Combine(projectRoot, scene)))
            {
                Debug.LogError("[OsmMapPocAndroidBuild] missing scene " + scene);
                EditorApplication.Exit(1);
                return;
            }

            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            EditorUserBuildSettings.androidBuildType = AndroidBuildType.Development;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.useCustomKeystore = false;

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
                Debug.LogError("[OsmMapPocAndroidBuild] FAILED result=" + summary.result + " errors=" + summary.totalErrors);
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[OsmMapPocAndroidBuild] SUCCESS path=" + apkPath + " sizeBytes=" + new FileInfo(apkPath).Length
                      + " time=" + summary.totalTime + " scene=" + scene);
            EditorApplication.Exit(0);
        }
    }
}
