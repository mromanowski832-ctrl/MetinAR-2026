using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MetinAR.Editor.AssetPipeline
{
    /// <summary>
    /// METINAR-009: pure (UnityEngine-free) rules of the mobile 3D asset pipeline: folder layout, naming,
    /// texture roles, per-category budgets and provenance (ASSET_SOURCE.txt) validation.
    /// Used by the Unity import postprocessor / validator and covered by tests/AssetPipeline.Tests.
    /// Budget numbers are PROPOSALS (see docs/METINAR-009-ASSET-PIPELINE.md) until measured on devices
    /// and confirmed by the owner.
    /// </summary>
    public static class AssetPipelineRules
    {
        public const string Root = "Assets/MetinAR";
        public const string MobsFolder = Root + "/Characters/Mobs";
        public const string BossesFolder = Root + "/Characters/Bosses";
        public const string NpcFolder = Root + "/Characters/NPC";
        public const string WeaponsFolder = Root + "/Weapons";
        /// <summary>METINAR-FPP-ART-001: first-person player body parts (hands/forearms rig) shown on the AR camera.</summary>
        public const string PlayerFolder = Root + "/Characters/Player";
        public const string SharedAnimationsFolder = Root + "/Animations";
        public const string SharedMaterialsFolder = Root + "/Materials";
        public const string SharedTexturesFolder = Root + "/Textures";
        public const string SourceManifestFileName = "ASSET_SOURCE.txt";
        public const string ReadmeFileName = "README.md";

        /// <summary>Folders the pipeline owns. Nothing outside them (e.g. Art/ArEncounter, Prefabs, Scenes) is touched.</summary>
        public static readonly string[] PipelineFolders =
        {
            MobsFolder, BossesFolder, NpcFolder, WeaponsFolder, PlayerFolder,
            SharedAnimationsFolder, SharedMaterialsFolder, SharedTexturesFolder,
        };

        /// <summary>Allowed sub-folders inside one per-asset folder, e.g. Characters/Mobs/VenomousSpider/Models.</summary>
        public static readonly string[] AssetSubFolders = { "Models", "Textures", "Materials", "Animations", "Prefabs" };

        /// <summary>DCC working files must stay outside Assets (Unity would need the DCC installed to import them).</summary>
        public static readonly string[] ForbiddenExtensions = { ".blend", ".max", ".ma", ".mb", ".spp", ".ztl", ".zpr", ".psd", ".kra", ".xcf" };

        public static readonly string[] MobRequiredStates = { "Idle", "Walk", "Attack", "Hit", "Death" };
        public static readonly string[] MobOptionalStates = { "Run", "Spawn", "Aggro", "Attack02", "Stun" };
        public static readonly string[] NpcRequiredStates = { "Idle", "Walk", "TurnLeft", "TurnRight", "Talk", "Interact" };
        public static readonly string[] NpcOptionalStates = { "Gesture", "Hit", "Death" };
        /// <summary>METINAR-FPP-ART-001: states of the first-person arms (PR #32 FppCombatController actions).</summary>
        public static readonly string[] PlayerRequiredStates = { "Idle", "Attack", "AuraActivate" };

        /// <summary>Clip states that loop by default.</summary>
        public static readonly string[] LoopingStates = { "Idle", "Walk", "Run", "Hammering", "Talk" };

        private const string IdPattern = "[A-Z][A-Za-z0-9]*";
        private static readonly Regex IdRegex = new Regex("^" + IdPattern + "$");
        private static readonly Regex StateRegex = new Regex("^[A-Z][A-Za-z0-9]*$");
        private static readonly Regex ModelRegex = new Regex("^(SK|SM)_(Mob|Boss|Npc|Weapon|Player)_(" + IdPattern + ")(?:_(" + IdPattern + "))?\\.fbx$");
        private static readonly Regex AnimationRegex = new Regex("^A_(" + IdPattern + ")_(" + IdPattern + ")(?:_(\\d{2}))?\\.fbx$");
        private static readonly Regex TextureRegex = new Regex("^T_(" + IdPattern + ")(?:_(" + IdPattern + "))?_(BC|N|MS|AO|E|M)\\.(png|tga)$");
        private static readonly Regex MaterialRegex = new Regex("^M_(" + IdPattern + ")(?:_(" + IdPattern + "))?\\.mat$");
        private static readonly Regex PrefabRegex = new Regex("^PF_(Mob|Boss|Npc|Weapon|Player)_(" + IdPattern + ")(?:_(" + IdPattern + "))?\\.prefab$");
        private static readonly Regex ControllerRegex = new Regex("^AC_(" + IdPattern + ")\\.controller$");
        private static readonly Regex MaskRegex = new Regex("^AM_(" + IdPattern + ")(?:_(" + IdPattern + "))?\\.mask$");

        public static AssetBudget GetBudget(AssetCategory category)
        {
            switch (category)
            {
                case AssetCategory.Mob:
                    return new AssetBudget(category, maxTrianglesLod0: 8000, maxBones: 40, maxMaterials: 2, maxTextureSize: 1024, maxTextureSizeJustified: 2048, requiredLods: 3);
                case AssetCategory.Boss:
                    return new AssetBudget(category, maxTrianglesLod0: 20000, maxBones: 75, maxMaterials: 3, maxTextureSize: 2048, maxTextureSizeJustified: 2048, requiredLods: 3);
                case AssetCategory.Npc:
                    return new AssetBudget(category, maxTrianglesLod0: 20000, maxBones: 75, maxMaterials: 3, maxTextureSize: 2048, maxTextureSizeJustified: 2048, requiredLods: 3);
                case AssetCategory.Weapon:
                    return new AssetBudget(category, maxTrianglesLod0: 3000, maxBones: 0, maxMaterials: 1, maxTextureSize: 1024, maxTextureSizeJustified: 2048, requiredLods: 1);
                case AssetCategory.Player:
                    // FPP arms: always on screen at a fixed distance from the camera, never more than one instance,
                    // no LOD switching (constant screen size). PROPOSAL until measured on the A31 (METINAR-FPP-ART-001 §5).
                    return new AssetBudget(category, maxTrianglesLod0: 10000, maxBones: 40, maxMaterials: 2, maxTextureSize: 1024, maxTextureSizeJustified: 2048, requiredLods: 1);
                default:
                    return new AssetBudget(category, maxTrianglesLod0: 0, maxBones: 0, maxMaterials: 0, maxTextureSize: 2048, maxTextureSizeJustified: 2048, requiredLods: 0);
            }
        }

        /// <summary>Maximum LOD1 / LOD2 triangle count as a fraction of LOD0.</summary>
        public const double MaxLod1Ratio = 0.60;
        public const double MaxLod2Ratio = 0.30;

        public static string Normalize(string assetPath)
        {
            return string.IsNullOrEmpty(assetPath) ? string.Empty : assetPath.Replace('\\', '/').TrimEnd('/');
        }

        public static bool IsPipelinePath(string assetPath)
        {
            string p = Normalize(assetPath);
            foreach (string folder in PipelineFolders)
            {
                if (p == folder || p.StartsWith(folder + "/", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Category from the folder: Characters/Mobs → Mob, Characters/Bosses → Boss, Characters/NPC → Npc, Weapons → Weapon, Characters/Player → Player.</summary>
        public static AssetCategory ClassifyPath(string assetPath)
        {
            string p = Normalize(assetPath);
            if (Under(p, MobsFolder)) return AssetCategory.Mob;
            if (Under(p, BossesFolder)) return AssetCategory.Boss;
            if (Under(p, NpcFolder)) return AssetCategory.Npc;
            if (Under(p, WeaponsFolder)) return AssetCategory.Weapon;
            if (Under(p, PlayerFolder)) return AssetCategory.Player;
            if (Under(p, SharedAnimationsFolder)) return AssetCategory.SharedAnimation;
            if (Under(p, SharedMaterialsFolder)) return AssetCategory.SharedMaterial;
            if (Under(p, SharedTexturesFolder)) return AssetCategory.SharedTexture;
            return AssetCategory.Unknown;
        }

        public static bool IsPerAssetCategory(AssetCategory c)
        {
            return c == AssetCategory.Mob || c == AssetCategory.Boss || c == AssetCategory.Npc || c == AssetCategory.Weapon || c == AssetCategory.Player;
        }

        public static string CategoryToken(AssetCategory c)
        {
            switch (c)
            {
                case AssetCategory.Mob: return "Mob";
                case AssetCategory.Boss: return "Boss";
                case AssetCategory.Npc: return "Npc";
                case AssetCategory.Weapon: return "Weapon";
                case AssetCategory.Player: return "Player";
                default: return null;
            }
        }

        public static string CategoryFolder(AssetCategory c)
        {
            switch (c)
            {
                case AssetCategory.Mob: return MobsFolder;
                case AssetCategory.Boss: return BossesFolder;
                case AssetCategory.Npc: return NpcFolder;
                case AssetCategory.Weapon: return WeaponsFolder;
                case AssetCategory.Player: return PlayerFolder;
                case AssetCategory.SharedAnimation: return SharedAnimationsFolder;
                case AssetCategory.SharedMaterial: return SharedMaterialsFolder;
                case AssetCategory.SharedTexture: return SharedTexturesFolder;
                default: return null;
            }
        }

        /// <summary>For per-asset categories: the asset id folder (e.g. "VenomousSpider"), else null.</summary>
        public static string GetAssetId(string assetPath)
        {
            string p = Normalize(assetPath);
            AssetCategory c = ClassifyPath(p);
            if (!IsPerAssetCategory(c))
            {
                return null;
            }

            string rest = p.Length > CategoryFolder(c).Length ? p.Substring(CategoryFolder(c).Length + 1) : string.Empty;
            if (rest.Length == 0)
            {
                return null;
            }

            int slash = rest.IndexOf('/');
            return slash < 0 ? rest : rest.Substring(0, slash);
        }

        /// <summary>The asset id folder path, e.g. Assets/MetinAR/Characters/Mobs/VenomousSpider.</summary>
        public static string GetAssetFolder(string assetPath)
        {
            string id = GetAssetId(assetPath);
            return id == null ? null : CategoryFolder(ClassifyPath(assetPath)) + "/" + id;
        }

        public static AssetKind GetKind(string fileName)
        {
            string f = fileName ?? string.Empty;
            if (f == SourceManifestFileName) return AssetKind.SourceManifest;
            if (f == ReadmeFileName) return AssetKind.Readme;
            if (f.StartsWith("A_", StringComparison.Ordinal) && f.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return AssetKind.AnimationSource;
            if (f.StartsWith("SK_", StringComparison.Ordinal)) return AssetKind.SkinnedModel;
            if (f.StartsWith("SM_", StringComparison.Ordinal)) return AssetKind.StaticModel;
            if (f.StartsWith("T_", StringComparison.Ordinal)) return AssetKind.Texture;
            if (f.StartsWith("M_", StringComparison.Ordinal)) return AssetKind.Material;
            if (f.StartsWith("PF_", StringComparison.Ordinal)) return AssetKind.Prefab;
            if (f.StartsWith("AC_", StringComparison.Ordinal)) return AssetKind.AnimatorController;
            if (f.StartsWith("AM_", StringComparison.Ordinal)) return AssetKind.AvatarMask;
            return AssetKind.Unknown;
        }

        /// <summary>Texture role from the file-name suffix (T_&lt;Id&gt;[_&lt;Part&gt;]_&lt;Role&gt;.png).</summary>
        public static TextureRole GetTextureRole(string fileName)
        {
            Match m = TextureRegex.Match(fileName ?? string.Empty);
            if (!m.Success)
            {
                return TextureRole.Unknown;
            }

            switch (m.Groups[3].Value)
            {
                case "BC": return TextureRole.BaseColor;
                case "N": return TextureRole.Normal;
                case "MS": return TextureRole.MetallicSmoothness;
                case "AO": return TextureRole.Occlusion;
                case "E": return TextureRole.Emission;
                case "M": return TextureRole.Mask;
                default: return TextureRole.Unknown;
            }
        }

        /// <summary>Colour data (base colour, emission) is sRGB; data maps (normal, MS, AO, mask) are linear.</summary>
        public static bool IsSrgb(TextureRole role)
        {
            return role == TextureRole.BaseColor || role == TextureRole.Emission;
        }

        public static bool IsLoopingState(string state)
        {
            return Array.IndexOf(LoopingStates, state) >= 0;
        }

        /// <summary>Parses A_&lt;Id&gt;_&lt;State&gt;[_NN].fbx → (id, state); false if the name is not a clip name.</summary>
        public static bool TryParseAnimationFile(string fileName, out string id, out string state)
        {
            Match m = AnimationRegex.Match(fileName ?? string.Empty);
            id = m.Success ? m.Groups[1].Value : null;
            state = m.Success ? m.Groups[2].Value : null;
            return m.Success;
        }

        /// <summary>The clip name used inside Unity: file name without extension (A_VenomousSpider_Idle).</summary>
        public static string ClipNameFromFile(string fileName)
        {
            string f = fileName ?? string.Empty;
            int dot = f.LastIndexOf('.');
            return dot < 0 ? f : f.Substring(0, dot);
        }

        /// <summary>Validates one asset path (folder placement + naming). Does not open the file.</summary>
        public static List<PipelineIssue> ValidatePath(string assetPath)
        {
            var issues = new List<PipelineIssue>();
            string p = Normalize(assetPath);
            if (!IsPipelinePath(p))
            {
                return issues;
            }

            AssetCategory category = ClassifyPath(p);
            int slash = p.LastIndexOf('/');
            string fileName = p.Substring(slash + 1);
            string ext = Extension(fileName);

            foreach (string forbidden in ForbiddenExtensions)
            {
                if (string.Equals(ext, forbidden, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(PipelineIssue.Error(p, "DCC working file '" + ext + "' must not be inside Assets; keep it in the art-source store outside the Unity project and export FBX/PNG."));
                    return issues;
                }
            }

            if (fileName == ReadmeFileName && slash >= 0 && Array.IndexOf(PipelineFolders, p.Substring(0, slash)) >= 0)
            {
                return issues;
            }

            if (IsPerAssetCategory(category))
            {
                ValidatePerAssetPath(p, category, fileName, issues);
            }
            else
            {
                ValidateSharedPath(p, category, fileName, issues);
            }

            return issues;
        }

        private static void ValidatePerAssetPath(string p, AssetCategory category, string fileName, List<PipelineIssue> issues)
        {
            string categoryFolder = CategoryFolder(category);
            string rest = p.Substring(categoryFolder.Length).TrimStart('/');
            string[] parts = rest.Split('/');
            string id = parts[0];
            if (!IdRegex.IsMatch(id))
            {
                issues.Add(PipelineIssue.Error(p, "Asset id folder '" + id + "' must be PascalCase ASCII (e.g. VenomousSpider)."));
                return;
            }

            if (parts.Length == 2)
            {
                if (fileName != SourceManifestFileName && fileName != ReadmeFileName)
                {
                    issues.Add(PipelineIssue.Error(p, "Only " + SourceManifestFileName + " and " + ReadmeFileName + " may sit directly in the asset folder; use Models/Textures/Materials/Animations/Prefabs."));
                }

                return;
            }

            string sub = parts[1];
            if (Array.IndexOf(AssetSubFolders, sub) < 0)
            {
                issues.Add(PipelineIssue.Error(p, "Unknown sub-folder '" + sub + "'; allowed: " + string.Join(", ", AssetSubFolders) + "."));
                return;
            }

            if (parts.Length > 3)
            {
                issues.Add(PipelineIssue.Error(p, "No nested folders below " + sub + "/."));
                return;
            }

            string token = CategoryToken(category);
            switch (sub)
            {
                case "Models":
                    Match mm = ModelRegex.Match(fileName);
                    if (!mm.Success)
                    {
                        issues.Add(PipelineIssue.Error(p, "Model must be named SK_" + token + "_" + id + "[_Part].fbx (skinned) or SM_" + token + "_" + id + "[_Part].fbx (static)."));
                    }
                    else if (mm.Groups[2].Value != token || mm.Groups[3].Value != id)
                    {
                        issues.Add(PipelineIssue.Error(p, "Model category/id must match its folder: expected *_" + token + "_" + id + "."));
                    }
                    else if (category == AssetCategory.Weapon && mm.Groups[1].Value != "SM")
                    {
                        issues.Add(PipelineIssue.Warning(p, "Weapons are normally static meshes (SM_) attached to a hand/socket bone."));
                    }

                    break;
                case "Animations":
                    if (Extension(fileName) == ".mask")
                    {
                        if (!MaskRegex.IsMatch(fileName)) issues.Add(PipelineIssue.Error(p, "Avatar mask must be named AM_" + id + "[_Part].mask."));
                        break;
                    }

                    if (Extension(fileName) == ".controller")
                    {
                        CheckController(p, fileName, id, issues);
                        break;
                    }

                    if (!TryParseAnimationFile(fileName, out string animId, out string state))
                    {
                        issues.Add(PipelineIssue.Error(p, "Animation must be named A_" + id + "_<State>[_NN].fbx (one clip per file)."));
                    }
                    else if (animId != id)
                    {
                        issues.Add(PipelineIssue.Error(p, "Animation id '" + animId + "' must match its folder id '" + id + "'."));
                    }
                    else if (!StateRegex.IsMatch(state))
                    {
                        issues.Add(PipelineIssue.Error(p, "State must be PascalCase."));
                    }

                    break;
                case "Textures":
                    CheckTexture(p, fileName, id, issues);
                    break;
                case "Materials":
                    CheckMaterial(p, fileName, id, issues);
                    break;
                case "Prefabs":
                    Match pm = PrefabRegex.Match(fileName);
                    if (!pm.Success || pm.Groups[1].Value != token || pm.Groups[2].Value != id)
                    {
                        issues.Add(PipelineIssue.Error(p, "Prefab must be named PF_" + token + "_" + id + "[_Variant].prefab."));
                    }

                    break;
            }
        }

        private static void ValidateSharedPath(string p, AssetCategory category, string fileName, List<PipelineIssue> issues)
        {
            string folder = CategoryFolder(category);
            string rest = p.Substring(folder.Length).TrimStart('/');
            if (rest.Length == 0)
            {
                return;
            }

            string[] parts = rest.Split('/');
            if (parts.Length > 2)
            {
                issues.Add(PipelineIssue.Error(p, "Shared folders allow one grouping level: " + folder + "/<SetId>/<file>."));
                return;
            }

            string setId = parts.Length == 2 ? parts[0] : null;
            if (setId != null && !IdRegex.IsMatch(setId))
            {
                issues.Add(PipelineIssue.Error(p, "Set folder '" + setId + "' must be PascalCase ASCII."));
                return;
            }

            if (parts.Length == 1 && fileName == SourceManifestFileName)
            {
                issues.Add(PipelineIssue.Error(p, SourceManifestFileName + " belongs in a set folder (" + folder + "/<SetId>/)."));
                return;
            }

            if (fileName == SourceManifestFileName)
            {
                return;
            }

            switch (category)
            {
                case AssetCategory.SharedAnimation:
                    if (Extension(fileName) == ".mask")
                    {
                        if (!MaskRegex.IsMatch(fileName)) issues.Add(PipelineIssue.Error(p, "Avatar mask must be named AM_<SetId>[_Part].mask."));
                    }
                    else if (Extension(fileName) == ".controller")
                    {
                        CheckController(p, fileName, setId, issues);
                    }
                    else if (!TryParseAnimationFile(fileName, out string animSet, out _))
                    {
                        issues.Add(PipelineIssue.Error(p, "Shared animation must be named A_<SetId>_<State>[_NN].fbx."));
                    }
                    else if (setId != null && animSet != setId)
                    {
                        issues.Add(PipelineIssue.Error(p, "Animation set id '" + animSet + "' must match its folder '" + setId + "'."));
                    }

                    break;
                case AssetCategory.SharedMaterial:
                    CheckMaterial(p, fileName, setId, issues);
                    break;
                case AssetCategory.SharedTexture:
                    CheckTexture(p, fileName, setId, issues);
                    break;
            }
        }

        private static void CheckController(string p, string fileName, string id, List<PipelineIssue> issues)
        {
            Match m = ControllerRegex.Match(fileName);
            if (!m.Success || (id != null && m.Groups[1].Value != id))
            {
                issues.Add(PipelineIssue.Error(p, "Animator controller must be named AC_" + (id ?? "<Id>") + ".controller."));
            }
        }

        private static void CheckTexture(string p, string fileName, string id, List<PipelineIssue> issues)
        {
            Match m = TextureRegex.Match(fileName);
            if (!m.Success)
            {
                issues.Add(PipelineIssue.Error(p, "Texture must be named T_" + (id ?? "<Id>") + "[_Part]_<BC|N|MS|AO|E|M>.png/.tga."));
            }
            else if (id != null && m.Groups[1].Value != id)
            {
                issues.Add(PipelineIssue.Error(p, "Texture id '" + m.Groups[1].Value + "' must match its folder '" + id + "'."));
            }
        }

        private static void CheckMaterial(string p, string fileName, string id, List<PipelineIssue> issues)
        {
            Match m = MaterialRegex.Match(fileName);
            if (!m.Success)
            {
                issues.Add(PipelineIssue.Error(p, "Material must be named M_" + (id ?? "<Id>") + "[_Part].mat."));
            }
            else if (id != null && m.Groups[1].Value != id)
            {
                issues.Add(PipelineIssue.Error(p, "Material id '" + m.Groups[1].Value + "' must match its folder '" + id + "'."));
            }
        }

        /// <summary>
        /// Checks measured mesh numbers against the category budget. <paramref name="trianglesPerLod"/> is
        /// LOD0, LOD1, LOD2... as found in the model.
        /// </summary>
        public static List<PipelineIssue> CheckBudget(string assetPath, AssetCategory category, IReadOnlyList<int> trianglesPerLod, int bones, int materials)
        {
            var issues = new List<PipelineIssue>();
            AssetBudget b = GetBudget(category);
            if (b.MaxTrianglesLod0 <= 0)
            {
                return issues;
            }

            int lodCount = trianglesPerLod == null ? 0 : trianglesPerLod.Count;
            if (lodCount < b.RequiredLods)
            {
                issues.Add(PipelineIssue.Error(assetPath, "Needs " + b.RequiredLods + " LOD level(s) (_LOD0.._LOD" + (b.RequiredLods - 1) + "), found " + lodCount + "."));
            }

            if (lodCount > 0)
            {
                int lod0 = trianglesPerLod[0];
                if (lod0 > b.MaxTrianglesLod0)
                {
                    issues.Add(PipelineIssue.Error(assetPath, "LOD0 has " + lod0 + " triangles; " + category + " budget is " + b.MaxTrianglesLod0 + "."));
                }

                double[] maxRatio = { 1.0, MaxLod1Ratio, MaxLod2Ratio };
                for (int i = 1; i < lodCount; i++)
                {
                    if (trianglesPerLod[i] >= trianglesPerLod[i - 1])
                    {
                        issues.Add(PipelineIssue.Error(assetPath, "LOD" + i + " (" + trianglesPerLod[i] + " tris) must have fewer triangles than LOD" + (i - 1) + "."));
                    }
                    else if (i < maxRatio.Length && lod0 > 0 && trianglesPerLod[i] > lod0 * maxRatio[i])
                    {
                        issues.Add(PipelineIssue.Warning(assetPath, "LOD" + i + " is " + Math.Round(100.0 * trianglesPerLod[i] / lod0) + "% of LOD0; target <= " + Math.Round(100 * maxRatio[i]) + "%."));
                    }
                }
            }

            if (bones > b.MaxBones)
            {
                issues.Add(PipelineIssue.Error(assetPath, bones + " bones; " + category + " budget is " + b.MaxBones + "."));
            }

            if (materials > b.MaxMaterials)
            {
                issues.Add(PipelineIssue.Error(assetPath, materials + " materials/sub-meshes; " + category + " budget is " + b.MaxMaterials + "."));
            }

            return issues;
        }

        public static List<PipelineIssue> CheckTextureSize(string assetPath, AssetCategory category, int width, int height, bool justifiedInManifest)
        {
            var issues = new List<PipelineIssue>();
            AssetBudget b = GetBudget(category);
            int cap = justifiedInManifest ? b.MaxTextureSizeJustified : b.MaxTextureSize;
            int largest = Math.Max(width, height);
            if (largest > cap)
            {
                issues.Add(PipelineIssue.Error(assetPath, "Texture " + width + "x" + height + " exceeds " + cap + " for " + category + (justifiedInManifest ? "." : " (2K needs texture_2k_justification in " + SourceManifestFileName + ").")));
            }

            if (!IsPowerOfTwo(width) || !IsPowerOfTwo(height))
            {
                issues.Add(PipelineIssue.Error(assetPath, "Texture " + width + "x" + height + " must be power-of-two (ASTC/mipmaps)."));
            }

            return issues;
        }

        /// <summary>Required animation states missing from a set of clip states.</summary>
        public static List<string> MissingRequiredStates(AssetCategory category, IEnumerable<string> presentStates)
        {
            string[] required = category == AssetCategory.Npc ? NpcRequiredStates
                : (category == AssetCategory.Mob || category == AssetCategory.Boss) ? MobRequiredStates
                : category == AssetCategory.Player ? PlayerRequiredStates
                : new string[0];
            var present = new HashSet<string>(presentStates ?? new string[0]);
            var missing = new List<string>();
            foreach (string s in required)
            {
                if (!present.Contains(s))
                {
                    missing.Add(s);
                }
            }

            return missing;
        }

        /// <summary>LOD index from a mesh object name ("SK_Mob_VenomousSpider_LOD1" → 1); names without a suffix count as LOD0.</summary>
        public static int LodIndex(string objectName)
        {
            string n = objectName ?? string.Empty;
            int i = n.LastIndexOf("_LOD", StringComparison.OrdinalIgnoreCase);
            if (i >= 0 && int.TryParse(n.Substring(i + 4), out int lod) && lod >= 0)
            {
                return lod;
            }

            return 0;
        }

        public static bool IsPowerOfTwo(int v)
        {
            return v > 0 && (v & (v - 1)) == 0;
        }

        private static bool Under(string p, string folder)
        {
            return p == folder || p.StartsWith(folder + "/", StringComparison.Ordinal);
        }

        private static string Extension(string fileName)
        {
            int dot = fileName.LastIndexOf('.');
            return dot < 0 ? string.Empty : fileName.Substring(dot).ToLowerInvariant();
        }
    }

    public enum AssetCategory
    {
        Unknown,
        Mob,
        Boss,
        Npc,
        Weapon,
        SharedAnimation,
        SharedMaterial,
        SharedTexture,
        Player,
    }

    public enum AssetKind
    {
        Unknown,
        SkinnedModel,
        StaticModel,
        AnimationSource,
        Texture,
        Material,
        Prefab,
        AnimatorController,
        AvatarMask,
        SourceManifest,
        Readme,
    }

    public enum TextureRole
    {
        Unknown,
        BaseColor,
        Normal,
        MetallicSmoothness,
        Occlusion,
        Emission,
        Mask,
    }

    public sealed class AssetBudget
    {
        public AssetBudget(AssetCategory category, int maxTrianglesLod0, int maxBones, int maxMaterials, int maxTextureSize, int maxTextureSizeJustified, int requiredLods)
        {
            Category = category;
            MaxTrianglesLod0 = maxTrianglesLod0;
            MaxBones = maxBones;
            MaxMaterials = maxMaterials;
            MaxTextureSize = maxTextureSize;
            MaxTextureSizeJustified = maxTextureSizeJustified;
            RequiredLods = requiredLods;
        }

        public AssetCategory Category { get; }
        public int MaxTrianglesLod0 { get; }
        public int MaxBones { get; }
        public int MaxMaterials { get; }
        public int MaxTextureSize { get; }
        public int MaxTextureSizeJustified { get; }
        public int RequiredLods { get; }
    }

    public enum IssueSeverity
    {
        Warning,
        Error,
    }

    public sealed class PipelineIssue
    {
        private PipelineIssue(IssueSeverity severity, string path, string message)
        {
            Severity = severity;
            Path = path;
            Message = message;
        }

        public IssueSeverity Severity { get; }
        public string Path { get; }
        public string Message { get; }

        public static PipelineIssue Error(string path, string message) => new PipelineIssue(IssueSeverity.Error, path, message);

        public static PipelineIssue Warning(string path, string message) => new PipelineIssue(IssueSeverity.Warning, path, message);

        public override string ToString() => Severity.ToString().ToUpperInvariant() + " " + Path + ": " + Message;
    }
}
