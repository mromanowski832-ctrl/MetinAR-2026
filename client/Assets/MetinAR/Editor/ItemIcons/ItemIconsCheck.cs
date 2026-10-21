using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MetinAR.Editor.AssetPipeline;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MetinAR.Editor.ItemIcons
{
    /// <summary>
    /// METINAR-032: verifies the 9 starter-kit item icons (ITEMS_COMBAT_SPEC §7/§8) after import and renders a
    /// preview of the real imported sprites in an unsaved scene.
    /// Batch: -executeMethod MetinAR.Editor.ItemIcons.ItemIconsCheck.Run  (report: Builds/metinar-032/icon-check.txt)
    /// </summary>
    public static class ItemIconsCheck
    {
        public const string Folder = "Assets/MetinAR/Textures/ItemIcons";
        public const string OutDir = "Builds/metinar-032";

        /// <summary>Item ids from docs/design/tables/items_starter.csv and their texture part names.</summary>
        public static readonly string[] ItemIds =
        {
            "starter_jerkin", "starter_boots", "starter_buckler", "amber_earring", "starter_gloves",
            "starter_sword", "potion_hp_s", "antidote", "ore_shard",
        };

        private static readonly StringBuilder Report = new StringBuilder();
        private static int checks;
        private static int failures;

        public static string PartName(string itemId)
        {
            return string.Concat(itemId.Split('_').Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
        }

        public static string IconPath(string itemId)
        {
            return Folder + "/T_ItemIcons_" + PartName(itemId) + "_BC.png";
        }

        [MenuItem("MetinAR/Item Icons/Check Starter Icons")]
        public static void RunMenu()
        {
            Execute();
            Debug.Log(Report.ToString());
        }

        public static void Run()
        {
            int code = 1;
            try
            {
                Execute();
                code = failures == 0 ? 0 : 1;
            }
            catch (Exception e)
            {
                Fail("X00 unexpected exception", e.ToString());
            }
            finally
            {
                Directory.CreateDirectory(OutDir);
                string header = "[METINAR-032] Item icon check: checks=" + checks + " failures=" + failures + "\n";
                File.WriteAllText(OutDir + "/icon-check.txt", header + Report);
                Debug.Log(header + Report);
                EditorApplication.Exit(code);
            }
        }

        private static void Execute()
        {
            Report.Clear();
            checks = 0;
            failures = 0;
            Info("Unity " + Application.unityVersion + ", colour space " + PlayerSettings.colorSpace);

            var sprites = new List<Sprite>();
            foreach (string id in ItemIds)
            {
                string path = IconPath(id);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (!Check("C01 " + id + ": " + Path.GetFileName(path) + " imported as texture", importer != null, path))
                {
                    continue;
                }

                importer.GetSourceTextureWidthAndHeight(out int w, out int h);
                Check("C02 " + id + ": source 256x256 PNG with alpha", w == 256 && h == 256 && importer.DoesSourceTextureHaveAlpha(), w + "x" + h + " alpha=" + importer.DoesSourceTextureHaveAlpha());
                Check("C03 " + id + ": Sprite (Single), sRGB, alpha is transparency, Read/Write off", importer.textureType == TextureImporterType.Sprite && importer.spriteImportMode == SpriteImportMode.Single && importer.sRGBTexture && importer.alphaIsTransparency && !importer.isReadable,
                    importer.textureType + "/" + importer.spriteImportMode + " sRGB=" + importer.sRGBTexture + " aIsT=" + importer.alphaIsTransparency + " rw=" + importer.isReadable);
                var formats = new List<string>();
                bool astc = true;
                foreach (string platform in new[] { "Android", "iPhone" })
                {
                    TextureImporterPlatformSettings s = importer.GetPlatformTextureSettings(platform);
                    astc &= s.overridden && s.format.ToString().StartsWith("ASTC", StringComparison.Ordinal) && s.maxTextureSize == 256;
                    formats.Add(platform + "=" + (s.overridden ? s.format + "@" + s.maxTextureSize : "default"));
                }

                Check("C04 " + id + ": max 256, ASTC overrides on Android and iOS, clamp", importer.maxTextureSize == 256 && astc && importer.wrapMode == TextureWrapMode.Clamp, string.Join(", ", formats));

                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (Check("C05 " + id + ": Sprite asset loads", sprite != null, sprite != null ? sprite.rect.ToString() : "null"))
                {
                    sprites.Add(sprite);
                }

                // Pixel content straight from the committed PNG (independent of compression).
                var png = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                png.LoadImage(File.ReadAllBytes(path));
                Color32[] px = png.GetPixels32();
                int opaque = px.Count(c => c.a > 128);
                bool cornersClear = new[] { 0, 255, 255 * 256, 256 * 256 - 1 }.All(i => px[i].a == 0);
                bool borderClear = true;
                for (int i = 0; i < 256; i++)
                {
                    borderClear &= px[i].a < 8 && px[255 * 256 + i].a < 8 && px[i * 256].a < 8 && px[i * 256 + 255].a < 8;
                }

                float coverage = opaque / 65536f;
                Check("C06 " + id + ": transparent background and margin, readable silhouette (15-75 % coverage)", cornersClear && borderClear && coverage > 0.15f && coverage < 0.75f, "coverage " + (coverage * 100f).ToString("0.0") + " %");
                UnityEngine.Object.DestroyImmediate(png);
            }

            string manifest = Folder + "/" + AssetPipelineRules.SourceManifestFileName;
            Check("C07 ASSET_SOURCE.txt present (origin original, MetinAR-Owned)", File.Exists(manifest), manifest);
            List<PipelineIssue> issues = AssetPipelineValidator.ValidateFolders(new[] { Folder }, out int files);
            foreach (PipelineIssue i in issues) Info("validator: " + i);
            Check("C08 METINAR-009 asset validator on Textures/ItemIcons: 0 errors, 0 warnings", issues.Count == 0, files + " files, " + issues.Count + " issues");

            if (sprites.Count == ItemIds.Length)
            {
                Preview(sprites, 1200, "unity_icons_grid.png");
                Preview(sprites, 420, "unity_icons_small.png");
                Check("C09 preview rendered from the imported sprites", File.Exists(OutDir + "/unity_icons_grid.png") && File.Exists(OutDir + "/unity_icons_small.png"), OutDir);
            }
        }

        /// <summary>3x3 grid of the imported sprites on inventory-like slots, orthographic camera, unsaved scene.</summary>
        private static void Preview(List<Sprite> sprites, int size, string file)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Quads with URP Unlit materials (the imported sprite texture as _BaseMap, alpha-blended): the same shader
            // family the game uses, and it renders reliably with Camera.Render in batch mode.
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Info("preview shader " + unlit.name);
            var slotMat = new Material(unlit);
            slotMat.SetColor("_BaseColor", new Color(0.25f, 0.235f, 0.23f));
            for (int i = 0; i < sprites.Count; i++)
            {
                int r = i / 3, c = i % 3;
                var pos = new Vector3((c - 1) * 3.0f, (1 - r) * 3.0f, 0f);
                GameObject bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
                bg.transform.position = pos + new Vector3(0, 0, 0.1f);
                bg.transform.localScale = Vector3.one * 2.8f;
                bg.GetComponent<Renderer>().sharedMaterial = slotMat;
                var mat = new Material(unlit);
                mat.SetTexture("_BaseMap", sprites[i].texture);
                mat.SetColor("_BaseColor", Color.white);
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                GameObject icon = GameObject.CreatePrimitive(PrimitiveType.Quad);
                icon.name = sprites[i].name;
                icon.transform.position = pos;
                icon.transform.localScale = Vector3.one * 2.6f;
                icon.GetComponent<Renderer>().sharedMaterial = mat;
            }

            var cam = new GameObject("Cam").AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 4.6f;
            cam.transform.position = new Vector3(0, 0, -10);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.115f, 0.13f);
            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            Directory.CreateDirectory(OutDir);
            File.WriteAllBytes(OutDir + "/" + file, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
            Info("preview " + file + " " + size + "x" + size);
        }

        private static bool Check(string name, bool ok, string detail)
        {
            checks++;
            if (!ok) failures++;
            Report.AppendLine((ok ? "PASS " : "FAIL ") + name + " — " + detail);
            return ok;
        }

        private static void Fail(string name, string detail)
        {
            Check(name, false, detail);
        }

        private static void Info(string line)
        {
            Report.AppendLine("INFO " + line);
        }
    }
}
