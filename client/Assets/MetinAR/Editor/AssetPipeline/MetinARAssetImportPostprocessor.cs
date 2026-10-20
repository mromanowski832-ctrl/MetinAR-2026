using System.IO;
using UnityEditor;

namespace MetinAR.Editor.AssetPipeline
{
    /// <summary>
    /// METINAR-009: applies the mobile import defaults from docs/METINAR-009-ASSET-PIPELINE.md to NEW files
    /// inside the pipeline folders only (Characters/Mobs|Bosses|NPC, Weapons, Animations, Materials, Textures under
    /// Assets/MetinAR). It runs only when the asset has no import settings yet (first import), so deliberate
    /// per-asset overrides made later in the Inspector are kept. Existing prototype art (Art/, Prefabs/, Scenes/)
    /// is never touched.
    /// </summary>
    public sealed class MetinARAssetImportPostprocessor : AssetPostprocessor
    {
        public const string AndroidPlatform = "Android";
        public const string IosPlatform = "iPhone";

        private bool ShouldApply()
        {
            return AssetPipelineRules.IsPipelinePath(assetPath) && assetImporter.importSettingsMissing;
        }

        private void OnPreprocessModel()
        {
            if (!ShouldApply())
            {
                return;
            }

            var importer = assetImporter as ModelImporter;
            if (importer == null)
            {
                return;
            }

            string file = Path.GetFileName(assetPath);
            AssetCategory category = AssetPipelineRules.ClassifyPath(assetPath);
            AssetKind kind = AssetPipelineRules.GetKind(file);

            // Scale / axes: 1 unit = 1 m, FBX exported in metres, Y-up / +Z-forward baked on import.
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;

            // Scene content we never want from DCC files.
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importConstraints = false;
            importer.addCollider = false;

            // Mesh: no CPU copy at runtime, no lossy mesh compression by default, Mikk tangents.
            importer.isReadable = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.weldVertices = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.skinWeights = ModelImporterSkinWeights.Standard;

            // Materials are authored as URP .mat files in Materials/, not extracted from the FBX.
            importer.materialImportMode = ModelImporterMaterialImportMode.None;

            // Rig: humanoid for NPCs and shared (humanoid) animation sets, generic for mobs/bosses, none for weapons/static.
            if (kind == AssetKind.SkinnedModel || kind == AssetKind.AnimationSource)
            {
                bool humanoid = category == AssetCategory.Npc || category == AssetCategory.SharedAnimation;
                importer.animationType = humanoid ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;

                // Keep the FBX root node: clip files (armature only) must produce the same curve paths
                // (Armature/B_Root/...) as the mesh file (armature + meshes), otherwise clips don't bind.
                importer.preserveHierarchy = true;
            }
            else
            {
                importer.animationType = ModelImporterAnimationType.None;
            }

            // Animation: clips only from A_*.fbx files; mesh files carry no takes.
            importer.importAnimation = kind == AssetKind.AnimationSource;
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;
            importer.resampleCurves = true;
        }

        private void OnPreprocessAnimation()
        {
            if (!ShouldApply())
            {
                return;
            }

            var importer = assetImporter as ModelImporter;
            string file = Path.GetFileName(assetPath);
            if (importer == null || !AssetPipelineRules.TryParseAnimationFile(file, out _, out string state))
            {
                return;
            }

            // One clip per file: name it after the file and set looping from the state name.
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length != 1)
            {
                return;
            }

            clips[0].name = AssetPipelineRules.ClipNameFromFile(file);
            clips[0].loopTime = AssetPipelineRules.IsLoopingState(state);
            importer.clipAnimations = clips;
        }

        private void OnPreprocessTexture()
        {
            if (!ShouldApply())
            {
                return;
            }

            var importer = assetImporter as TextureImporter;
            if (importer == null)
            {
                return;
            }

            string file = Path.GetFileName(assetPath);
            TextureRole role = AssetPipelineRules.GetTextureRole(file);
            AssetCategory category = AssetPipelineRules.ClassifyPath(assetPath);
            int maxSize = MaxTextureSizeFor(assetPath, category);

            importer.textureType = role == TextureRole.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = AssetPipelineRules.IsSrgb(role);
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.mipmapEnabled = true;
            importer.isReadable = false;
            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.Compressed;

            TextureImporterFormat format = AstcFormatFor(role);
            ApplyPlatform(importer, AndroidPlatform, maxSize, format);
            ApplyPlatform(importer, IosPlatform, maxSize, format);
        }

        /// <summary>ASTC 4x4 for normal maps (quality-sensitive), ASTC 6x6 for colour and packed masks.</summary>
        public static TextureImporterFormat AstcFormatFor(TextureRole role)
        {
            return role == TextureRole.Normal ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6;
        }

        public static int MaxTextureSizeFor(string assetPath, AssetCategory category)
        {
            AssetBudget budget = AssetPipelineRules.GetBudget(category);
            string folder = AssetPipelineRules.GetAssetFolder(assetPath);
            if (folder != null)
            {
                string manifestPath = folder + "/" + AssetPipelineRules.SourceManifestFileName;
                if (File.Exists(manifestPath) && AssetSourceManifest.Parse(File.ReadAllText(manifestPath)).Has("texture_2k_justification"))
                {
                    return budget.MaxTextureSizeJustified;
                }
            }

            return budget.MaxTextureSize;
        }

        private static void ApplyPlatform(TextureImporter importer, string platform, int maxSize, TextureImporterFormat format)
        {
            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
            settings.overridden = true;
            settings.maxTextureSize = maxSize;
            settings.format = format;
            importer.SetPlatformTextureSettings(settings);
        }
    }
}
