using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MetinAR.Editor.AssetPipeline
{
    /// <summary>
    /// METINAR-009: batch self-test of the import postprocessor and validator inside a real Unity project.
    /// Creates temporary generated textures (no third-party content) in a throw-away pipeline folder and in a
    /// non-pipeline folder, checks the applied import settings and validator verdicts, then deletes both folders.
    /// Touches no scene, prefab or existing asset.
    /// Batch: -executeMethod MetinAR.Editor.AssetPipeline.AssetPipelineSelfTest.Run
    /// </summary>
    public static class AssetPipelineSelfTest
    {
        public const string ResultsPath = "Builds/metinar-009-asset-selftest.txt";
        private const string TestId = "PipelineSelfTest";
        private const string AssetFolder = AssetPipelineRules.MobsFolder + "/" + TestId;
        private const string OutsideFolder = AssetPipelineRules.Root + "/_PipelineSelfTestOutside";

        private static readonly List<string> Results = new List<string>();
        private static int _failures;

        public static void Run()
        {
            Results.Clear();
            _failures = 0;
            try
            {
                Execute();
            }
            catch (Exception e)
            {
                Fail("S00 no exception", e.ToString());
            }
            finally
            {
                Cleanup();
                Check("S99 temporary folders removed", !AssetDatabase.IsValidFolder(AssetFolder) && !AssetDatabase.IsValidFolder(OutsideFolder), null);
            }

            var sb = new StringBuilder();
            sb.AppendLine("[METINAR-009] Asset pipeline self-test: tests=" + Results.Count + " failures=" + _failures);
            foreach (string r in Results) sb.AppendLine(r);
            Directory.CreateDirectory(Path.GetDirectoryName(ResultsPath));
            File.WriteAllText(ResultsPath, sb.ToString());
            Debug.Log(sb.ToString());
            EditorApplication.Exit(_failures == 0 ? 0 : 1);
        }

        private static void Execute()
        {
            Cleanup();
            Directory.CreateDirectory(AssetFolder + "/Textures");
            Directory.CreateDirectory(OutsideFolder);
            File.WriteAllText(AssetFolder + "/" + AssetPipelineRules.SourceManifestFileName,
                "asset_id: " + TestId + "\norigin: original\nauthor: MetinAR self-test (generated in Unity)\nlicense: " + AssetSourceManifest.ProjectOwnedLicense +
                "\ncommercial_use: yes\ncreated_with: Unity Texture2D.EncodeToPNG\ndate_added: 2026-10-20\n");

            WritePng(AssetFolder + "/Textures/T_" + TestId + "_BC.png", 256, new Color(0.3f, 0.1f, 0.3f));
            WritePng(AssetFolder + "/Textures/T_" + TestId + "_N.png", 256, new Color(0.5f, 0.5f, 1f));
            WritePng(AssetFolder + "/Textures/T_" + TestId + "_MS.png", 256, new Color(0f, 0f, 0f, 0.4f));
            WritePng(OutsideFolder + "/T_Outside_N.png", 64, new Color(0.5f, 0.5f, 1f));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var bc = (TextureImporter)AssetImporter.GetAtPath(AssetFolder + "/Textures/T_" + TestId + "_BC.png");
            var n = (TextureImporter)AssetImporter.GetAtPath(AssetFolder + "/Textures/T_" + TestId + "_N.png");
            var ms = (TextureImporter)AssetImporter.GetAtPath(AssetFolder + "/Textures/T_" + TestId + "_MS.png");
            var outside = (TextureImporter)AssetImporter.GetAtPath(OutsideFolder + "/T_Outside_N.png");

            Check("S01 _BC imported as sRGB Default texture", bc != null && bc.textureType == TextureImporterType.Default && bc.sRGBTexture, Describe(bc));
            Check("S02 _N imported as Normal map", n != null && n.textureType == TextureImporterType.NormalMap, Describe(n));
            Check("S03 _MS imported linear (sRGB off)", ms != null && !ms.sRGBTexture, Describe(ms));
            Check("S04 Read/Write off, mipmaps on", bc != null && !bc.isReadable && bc.mipmapEnabled && !n.isReadable && !ms.isReadable, Describe(bc));
            Check("S05 mob max texture size 1024 (no 2K justification)", bc != null && bc.maxTextureSize == 1024, Describe(bc));
            Check("S06 Android/iOS ASTC 6x6 for _BC", Platform(bc, "Android", TextureImporterFormat.ASTC_6x6) && Platform(bc, "iPhone", TextureImporterFormat.ASTC_6x6), PlatformInfo(bc));
            Check("S07 Android/iOS ASTC 4x4 for _N", Platform(n, "Android", TextureImporterFormat.ASTC_4x4) && Platform(n, "iPhone", TextureImporterFormat.ASTC_4x4), PlatformInfo(n));
            Check("S08 files outside pipeline folders untouched (still Default, no override)", outside != null && outside.textureType == TextureImporterType.Default && !outside.GetPlatformTextureSettings("Android").overridden, Describe(outside));

            List<PipelineIssue> clean = AssetPipelineValidator.ValidateFolders(new[] { AssetFolder }, out int files);
            Check("S09 valid self-test folder: 0 errors", files == 4 && clean.All(i => i.Severity != IssueSeverity.Error), Join(clean) + " files=" + files);
            Check("S10 missing animation states reported as warning (no prefab yet)", clean.Any(i => i.Severity == IssueSeverity.Warning && i.Message.Contains("Missing required animation states")), Join(clean));

            // Wrong name + oversize texture + bad licence must be reported.
            WritePng(AssetFolder + "/Textures/spider_diffuse.png", 64, Color.gray);
            WritePng(AssetFolder + "/Textures/T_" + TestId + "_Big_BC.png", 2048, Color.gray);
            File.WriteAllText(AssetFolder + "/" + AssetPipelineRules.SourceManifestFileName,
                "asset_id: " + TestId + "\norigin: third_party\nauthor: someone\nlicense: CC-BY-NC-4.0\ncommercial_use: no\ncreated_with: unknown\ndate_added: 2026-10-20\n");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            List<PipelineIssue> dirty = AssetPipelineValidator.ValidateFolders(new[] { AssetFolder }, out _);
            Check("S11 bad file name reported", dirty.Any(i => i.Severity == IssueSeverity.Error && i.Path.EndsWith("spider_diffuse.png", StringComparison.Ordinal)), Join(dirty));
            Check("S12 2048 mob texture without justification reported", dirty.Any(i => i.Severity == IssueSeverity.Error && i.Path.EndsWith("_Big_BC.png", StringComparison.Ordinal) && i.Message.Contains("exceeds")), Join(dirty));
            Check("S13 non-commercial licence / commercial_use: no reported", dirty.Count(i => i.Severity == IssueSeverity.Error && i.Path.EndsWith(AssetPipelineRules.SourceManifestFileName, StringComparison.Ordinal)) >= 2, Join(dirty));
        }

        private static void Cleanup()
        {
            if (AssetDatabase.IsValidFolder(AssetFolder)) AssetDatabase.DeleteAsset(AssetFolder);
            if (AssetDatabase.IsValidFolder(OutsideFolder)) AssetDatabase.DeleteAsset(OutsideFolder);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void WritePng(string path, int size, Color color)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        private static bool Platform(TextureImporter importer, string platform, TextureImporterFormat format)
        {
            if (importer == null) return false;
            TextureImporterPlatformSettings s = importer.GetPlatformTextureSettings(platform);
            return s.overridden && s.format == format && s.maxTextureSize == importer.maxTextureSize;
        }

        private static string PlatformInfo(TextureImporter importer)
        {
            if (importer == null) return "null importer";
            TextureImporterPlatformSettings a = importer.GetPlatformTextureSettings("Android");
            TextureImporterPlatformSettings i = importer.GetPlatformTextureSettings("iPhone");
            return "Android " + a.overridden + "/" + a.format + "/" + a.maxTextureSize + ", iPhone " + i.overridden + "/" + i.format + "/" + i.maxTextureSize;
        }

        private static string Describe(TextureImporter importer)
        {
            return importer == null ? "null importer" : importer.textureType + " sRGB=" + importer.sRGBTexture + " readable=" + importer.isReadable + " mips=" + importer.mipmapEnabled + " max=" + importer.maxTextureSize;
        }

        private static string Join(List<PipelineIssue> issues)
        {
            return string.Join(" | ", issues.Select(i => i.ToString()));
        }

        private static void Check(string name, bool ok, string detail)
        {
            if (ok)
            {
                Results.Add("PASS " + name);
            }
            else
            {
                Fail(name, detail);
            }
        }

        private static void Fail(string name, string detail)
        {
            _failures++;
            Results.Add("FAIL " + name + (detail != null ? " — " + detail : string.Empty));
        }
    }
}
