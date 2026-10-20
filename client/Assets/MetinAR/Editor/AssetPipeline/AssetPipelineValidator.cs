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
    /// METINAR-009: validates everything inside the pipeline folders against docs/METINAR-009-ASSET-PIPELINE.md:
    /// placement and naming, ASSET_SOURCE.txt provenance, mesh/LOD/bone/material budgets, texture size and import
    /// settings, prefab template (LODGroup, missing scripts, ground pivot) and required animation states.
    /// Read-only: it never changes assets.
    /// Menu: MetinAR/Asset Pipeline/Validate. Batch: -executeMethod MetinAR.Editor.AssetPipeline.AssetPipelineValidator.RunBatch
    /// </summary>
    public static class AssetPipelineValidator
    {
        public const string ReportPath = "Builds/metinar-009-asset-validation.txt";
        private const float GroundTolerance = 0.02f;

        [MenuItem("MetinAR/Asset Pipeline/Validate")]
        public static void ValidateMenu()
        {
            List<PipelineIssue> issues = ValidateFolders(AssetPipelineRules.PipelineFolders, out int files);
            string report = FormatReport(issues, files);
            Debug.Log(report);
            int errors = issues.Count(i => i.Severity == IssueSeverity.Error);
            EditorUtility.DisplayDialog("MetinAR asset pipeline", errors == 0 ? "No errors (" + files + " files)." : errors + " error(s); see Console.", "OK");
        }

        public static void RunBatch()
        {
            int exitCode = 1;
            try
            {
                List<PipelineIssue> issues = ValidateFolders(AssetPipelineRules.PipelineFolders, out int files);
                string report = FormatReport(issues, files);
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, report);
                Debug.Log(report);
                exitCode = issues.Any(i => i.Severity == IssueSeverity.Error) ? 1 : 0;
            }
            catch (Exception e)
            {
                Debug.LogError("[METINAR-009] Validator crashed: " + e);
            }

            EditorApplication.Exit(exitCode);
        }

        public static string FormatReport(List<PipelineIssue> issues, int files)
        {
            var sb = new StringBuilder();
            int errors = issues.Count(i => i.Severity == IssueSeverity.Error);
            sb.AppendLine("[METINAR-009] Asset pipeline validation: files=" + files + " errors=" + errors + " warnings=" + (issues.Count - errors));
            foreach (PipelineIssue issue in issues)
            {
                sb.AppendLine(issue.ToString());
            }

            return sb.ToString();
        }

        public static List<PipelineIssue> ValidateFolders(IEnumerable<string> folders, out int fileCount)
        {
            var issues = new List<PipelineIssue>();
            fileCount = 0;
            foreach (string folder in folders)
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                foreach (string raw in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                {
                    string path = AssetPipelineRules.Normalize(raw);
                    if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    fileCount++;
                    List<PipelineIssue> pathIssues = AssetPipelineRules.ValidatePath(path);
                    issues.AddRange(pathIssues);
                    if (pathIssues.Any(i => i.Severity == IssueSeverity.Error))
                    {
                        continue;
                    }

                    ValidateFile(path, issues);
                }

                ValidateAssetFolders(folder, issues);
            }

            return issues;
        }

        private static void ValidateAssetFolders(string folder, List<PipelineIssue> issues)
        {
            string normalized = AssetPipelineRules.Normalize(folder);
            if (Array.IndexOf(AssetPipelineRules.PipelineFolders, normalized) >= 0)
            {
                foreach (string raw in Directory.GetDirectories(normalized))
                {
                    ValidateAssetFolder(AssetPipelineRules.Normalize(raw), issues);
                }
            }
            else if (AssetPipelineRules.GetAssetFolder(normalized) == normalized)
            {
                ValidateAssetFolder(normalized, issues);
            }
        }

        /// <summary>One per-asset folder (or shared set folder): provenance record + required animation states.</summary>
        private static void ValidateAssetFolder(string assetFolder, List<PipelineIssue> issues)
        {
            AssetCategory category = AssetPipelineRules.ClassifyPath(assetFolder);
            {
                string id = Path.GetFileName(assetFolder);
                string manifestPath = assetFolder + "/" + AssetPipelineRules.SourceManifestFileName;
                if (!File.Exists(manifestPath))
                {
                    issues.Add(PipelineIssue.Error(assetFolder, "Missing " + AssetPipelineRules.SourceManifestFileName + " (provenance/licence record is mandatory)."));
                }
                else
                {
                    issues.AddRange(AssetSourceManifest.Parse(File.ReadAllText(manifestPath)).Validate(manifestPath, id));
                }

                if (category == AssetCategory.Mob || category == AssetCategory.Boss || category == AssetCategory.Npc)
                {
                    var states = new List<string>();
                    string animFolder = assetFolder + "/Animations";
                    if (Directory.Exists(animFolder))
                    {
                        foreach (string f in Directory.GetFiles(animFolder, "A_*.fbx"))
                        {
                            if (AssetPipelineRules.TryParseAnimationFile(Path.GetFileName(f), out _, out string state))
                            {
                                states.Add(state);
                            }
                        }
                    }

                    List<string> missing = AssetPipelineRules.MissingRequiredStates(category, states);
                    bool hasPrefab = Directory.Exists(assetFolder + "/Prefabs") && Directory.GetFiles(assetFolder + "/Prefabs", "PF_*.prefab").Length > 0;
                    if (missing.Count > 0)
                    {
                        string msg = "Missing required animation states: " + string.Join(", ", missing) + ".";
                        issues.Add(hasPrefab ? PipelineIssue.Error(assetFolder, msg) : PipelineIssue.Warning(assetFolder, msg + " (asset in progress: no prefab yet)"));
                    }
                }
            }
        }

        private static void ValidateFile(string path, List<PipelineIssue> issues)
        {
            string file = Path.GetFileName(path);
            AssetCategory category = AssetPipelineRules.ClassifyPath(path);
            switch (AssetPipelineRules.GetKind(file))
            {
                case AssetKind.SkinnedModel:
                case AssetKind.StaticModel:
                    ValidateModel(path, category, issues);
                    break;
                case AssetKind.AnimationSource:
                    ValidateAnimationSource(path, issues);
                    break;
                case AssetKind.Texture:
                    ValidateTexture(path, category, issues);
                    break;
                case AssetKind.Prefab:
                    ValidatePrefab(path, category, issues);
                    break;
                case AssetKind.Material:
                    ValidateMaterial(path, issues);
                    break;
            }
        }

        private static void ValidateModel(string path, AssetCategory category, List<PipelineIssue> issues)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                issues.Add(PipelineIssue.Error(path, "Not imported as a model."));
                return;
            }

            if (importer.isReadable) issues.Add(PipelineIssue.Warning(path, "Read/Write enabled: doubles mesh memory; disable unless runtime CPU access is documented."));
            if (!Mathf.Approximately(importer.globalScale, 1f) || !importer.useFileScale) issues.Add(PipelineIssue.Error(path, "Scale Factor must be 1 with Convert Units on; fix the export (metres) instead."));
            if (importer.importCameras || importer.importLights) issues.Add(PipelineIssue.Warning(path, "Cameras/lights imported from FBX."));
            if (category == AssetCategory.Npc && importer.animationType != ModelImporterAnimationType.Human) issues.Add(PipelineIssue.Error(path, "NPC rigs must be Humanoid."));
            if ((category == AssetCategory.Mob || category == AssetCategory.Boss) && Path.GetFileName(path).StartsWith("SK_", StringComparison.Ordinal) && importer.animationType != ModelImporterAnimationType.Generic) issues.Add(PipelineIssue.Warning(path, "Mob/boss rigs are Generic by default (non-humanoid skeletons)."));

            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null)
            {
                issues.Add(PipelineIssue.Error(path, "Model has no root GameObject."));
                return;
            }

            var lodTris = new SortedDictionary<int, int>();
            var lodSubMeshes = new SortedDictionary<int, int>();
            int bones = 0;
            Bounds? bounds = null;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = null;
                if (r is SkinnedMeshRenderer smr)
                {
                    mesh = smr.sharedMesh;
                    bones = Math.Max(bones, smr.bones != null ? smr.bones.Length : 0);
                }
                else
                {
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    mesh = mf != null ? mf.sharedMesh : null;
                }

                if (mesh == null)
                {
                    continue;
                }

                int lod = AssetPipelineRules.LodIndex(r.gameObject.name);
                lodTris.TryGetValue(lod, out int t);
                lodTris[lod] = t + TriangleCount(mesh);
                lodSubMeshes.TryGetValue(lod, out int s);
                lodSubMeshes[lod] = s + mesh.subMeshCount;
                if (lod == 0)
                {
                    Bounds b = r.bounds;
                    if (bounds.HasValue)
                    {
                        Bounds acc = bounds.Value;
                        acc.Encapsulate(b);
                        bounds = acc;
                    }
                    else
                    {
                        bounds = b;
                    }
                }
            }

            if (lodTris.Count == 0)
            {
                issues.Add(PipelineIssue.Error(path, "Model contains no mesh."));
                return;
            }

            issues.AddRange(AssetPipelineRules.CheckBudget(path, category, lodTris.Values.ToList(), bones, lodSubMeshes.Values.Max()));
            if (bounds.HasValue)
            {
                Bounds b = bounds.Value;
                if (category != AssetCategory.Weapon && Mathf.Abs(b.min.y) > GroundTolerance)
                {
                    issues.Add(PipelineIssue.Warning(path, "Pivot is not on the ground: mesh min Y = " + b.min.y.ToString("0.000") + " m (expected 0 ± " + GroundTolerance + ")."));
                }

                float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
                if (size < 0.05f || size > 10f)
                {
                    issues.Add(PipelineIssue.Error(path, "Implausible size " + size.ToString("0.00") + " m: check export units (1 unit = 1 m)."));
                }
            }
        }

        private static void ValidateAnimationSource(string path, List<PipelineIssue> issues)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                issues.Add(PipelineIssue.Error(path, "Not imported as a model."));
                return;
            }

            ModelImporterClipAnimation[] clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            if (clips.Length != 1)
            {
                issues.Add(PipelineIssue.Error(path, "Animation files hold exactly one clip; found " + clips.Length + "."));
                return;
            }

            string expected = AssetPipelineRules.ClipNameFromFile(Path.GetFileName(path));
            if (clips[0].name != expected) issues.Add(PipelineIssue.Warning(path, "Clip is named '" + clips[0].name + "', expected '" + expected + "'."));
            if (importer.animationCompression != ModelImporterAnimationCompression.Optimal) issues.Add(PipelineIssue.Warning(path, "Animation compression should be Optimal."));
            if (importer.animationType == ModelImporterAnimationType.Human && importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther)
            {
                issues.Add(PipelineIssue.Warning(path, "Humanoid clip files should use 'Copy From Other Avatar' (the SK_ model's avatar)."));
            }
        }

        private static void ValidateTexture(string path, AssetCategory category, List<PipelineIssue> issues)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                issues.Add(PipelineIssue.Error(path, "Not imported as a texture."));
                return;
            }

            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            bool justified = false;
            string folder = AssetPipelineRules.GetAssetFolder(path);
            if (folder != null && File.Exists(folder + "/" + AssetPipelineRules.SourceManifestFileName))
            {
                justified = AssetSourceManifest.Parse(File.ReadAllText(folder + "/" + AssetPipelineRules.SourceManifestFileName)).Has("texture_2k_justification");
            }

            issues.AddRange(AssetPipelineRules.CheckTextureSize(path, category, width, height, justified));

            TextureRole role = AssetPipelineRules.GetTextureRole(Path.GetFileName(path));
            if (role == TextureRole.Normal && importer.textureType != TextureImporterType.NormalMap) issues.Add(PipelineIssue.Error(path, "_N texture must be imported as Normal map."));
            if (role != TextureRole.Normal && importer.sRGBTexture != AssetPipelineRules.IsSrgb(role)) issues.Add(PipelineIssue.Error(path, "sRGB must be " + AssetPipelineRules.IsSrgb(role) + " for " + role + "."));
            if (importer.isReadable) issues.Add(PipelineIssue.Warning(path, "Read/Write enabled on a texture (doubles memory)."));
            if (!importer.mipmapEnabled) issues.Add(PipelineIssue.Warning(path, "Mipmaps disabled on a 3D texture."));
            foreach (string platform in new[] { MetinARAssetImportPostprocessor.AndroidPlatform, MetinARAssetImportPostprocessor.IosPlatform })
            {
                TextureImporterPlatformSettings s = importer.GetPlatformTextureSettings(platform);
                if (!s.overridden || !s.format.ToString().StartsWith("ASTC", StringComparison.Ordinal))
                {
                    issues.Add(PipelineIssue.Warning(path, platform + " override should be ASTC (found " + (s.overridden ? s.format.ToString() : "no override") + ")."));
                }
            }
        }

        private static void ValidateMaterial(string path, List<PipelineIssue> issues)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || material.shader == null)
            {
                issues.Add(PipelineIssue.Error(path, "Material has no shader."));
                return;
            }

            string shader = material.shader.name;
            if (!shader.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal) && !shader.StartsWith("Shader Graphs/MetinAR", StringComparison.Ordinal))
            {
                issues.Add(PipelineIssue.Error(path, "Shader '" + shader + "' is not a URP shader (use URP Lit/Simple Lit/Unlit or an approved MetinAR Shader Graph)."));
            }
        }

        private static void ValidatePrefab(string path, AssetCategory category, List<PipelineIssue> issues)
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null)
            {
                issues.Add(PipelineIssue.Error(path, "Prefab could not be loaded."));
                return;
            }

            int missing = 0;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            }

            if (missing > 0) issues.Add(PipelineIssue.Error(path, missing + " missing script(s)."));
            if (root.transform.localScale != Vector3.one) issues.Add(PipelineIssue.Error(path, "Prefab root scale must be (1,1,1); scale belongs in the model export."));
            if (root.transform.localRotation != Quaternion.identity) issues.Add(PipelineIssue.Warning(path, "Prefab root should not be rotated; face +Z in the model."));

            AssetBudget budget = AssetPipelineRules.GetBudget(category);
            if (budget.RequiredLods > 1 && root.GetComponentInChildren<LODGroup>(true) == null) issues.Add(PipelineIssue.Error(path, "Character prefabs need a LODGroup (LOD0/1/2)."));
            if ((category == AssetCategory.Mob || category == AssetCategory.Boss) && root.GetComponentInChildren<Collider>(true) == null) issues.Add(PipelineIssue.Warning(path, "No collider: mobs need a simple body collider (capsule/box) as the hit volume."));
            if ((category == AssetCategory.Mob || category == AssetCategory.Boss || category == AssetCategory.Npc) && FindDeep(root.transform, "Anchor_Label") == null) issues.Add(PipelineIssue.Warning(path, "No 'Anchor_Label' child for the name/HP label."));
            foreach (MeshCollider mc in root.GetComponentsInChildren<MeshCollider>(true))
            {
                issues.Add(PipelineIssue.Warning(path, "MeshCollider on '" + mc.name + "': use primitive colliders on mobile."));
            }
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform f = FindDeep(c, name);
                if (f != null) return f;
            }

            return null;
        }

        private static int TriangleCount(Mesh mesh)
        {
            long indices = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) == MeshTopology.Triangles)
                {
                    indices += mesh.GetIndexCount(s);
                }
            }

            return (int)(indices / 3);
        }
    }
}
