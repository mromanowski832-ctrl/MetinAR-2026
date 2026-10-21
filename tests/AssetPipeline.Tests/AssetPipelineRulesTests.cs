using MetinAR.Editor.AssetPipeline;
using Xunit;

namespace AssetPipeline.Tests
{
    public class AssetPipelineRulesTests
    {
        private const string Spider = "Assets/MetinAR/Characters/Mobs/VenomousSpider";
        private const string Smith = "Assets/MetinAR/Characters/NPC/Blacksmith";
        private const string Hammer = "Assets/MetinAR/Weapons/BlacksmithHammer";

        private static bool HasError(string path) => AssetPipelineRules.ValidatePath(path).Any(i => i.Severity == IssueSeverity.Error);

        [Theory]
        [InlineData(Spider + "/Models/SK_Mob_VenomousSpider.fbx", AssetCategory.Mob)]
        [InlineData("Assets/MetinAR/Characters/Bosses/SpiderQueen/Models/SK_Boss_SpiderQueen.fbx", AssetCategory.Boss)]
        [InlineData(Smith + "/Prefabs/PF_Npc_Blacksmith.prefab", AssetCategory.Npc)]
        [InlineData(Hammer + "/Models/SM_Weapon_BlacksmithHammer.fbx", AssetCategory.Weapon)]
        [InlineData("Assets/MetinAR/Animations/HumanoidBase/A_HumanoidBase_Walk.fbx", AssetCategory.SharedAnimation)]
        [InlineData("Assets/MetinAR/Materials/Common/M_Common_Shadow.mat", AssetCategory.SharedMaterial)]
        [InlineData("Assets/MetinAR/Textures/Common/T_Common_Shadow_BC.png", AssetCategory.SharedTexture)]
        [InlineData("Assets/MetinAR/Characters/Player/FppArms/Models/SK_Player_FppArms.fbx", AssetCategory.Player)]
        [InlineData("Assets/MetinAR/Characters/PlayerX/Foo/Models/SK_Player_Foo.fbx", AssetCategory.Unknown)]
        [InlineData("Assets/MetinAR/Art/ArEncounter/T_ArReticle.png", AssetCategory.Unknown)]
        [InlineData("Assets/MetinAR/Prefabs/ArPrototypeSpider.prefab", AssetCategory.Unknown)]
        public void ClassifiesByFolder(string path, AssetCategory expected)
        {
            Assert.Equal(expected, AssetPipelineRules.ClassifyPath(path));
        }

        [Fact]
        public void ExistingPrototypeArtIsOutsideThePipeline()
        {
            Assert.False(AssetPipelineRules.IsPipelinePath("Assets/MetinAR/Art/ArEncounter/M_ArSpiderBody.mat"));
            Assert.False(AssetPipelineRules.IsPipelinePath("Assets/MetinAR/Prefabs/ArPrototypeSpider.prefab"));
            Assert.False(AssetPipelineRules.IsPipelinePath("Assets/MetinAR/Scenes/ArEncounterPrototype.unity"));
            Assert.False(AssetPipelineRules.IsPipelinePath("Assets/MetinAR/CharactersExtra/x.png"));
            Assert.Empty(AssetPipelineRules.ValidatePath("Assets/MetinAR/Art/ArEncounter/whatever name.png"));
        }

        [Fact]
        public void BackslashPathsAreNormalized()
        {
            Assert.Equal(AssetCategory.Mob, AssetPipelineRules.ClassifyPath("Assets\\MetinAR\\Characters\\Mobs\\VenomousSpider\\Models\\SK_Mob_VenomousSpider.fbx"));
        }

        [Fact]
        public void AssetIdAndFolder()
        {
            Assert.Equal("VenomousSpider", AssetPipelineRules.GetAssetId(Spider + "/Textures/T_VenomousSpider_BC.png"));
            Assert.Equal(Spider, AssetPipelineRules.GetAssetFolder(Spider + "/Textures/T_VenomousSpider_BC.png"));
            Assert.Equal(Spider, AssetPipelineRules.GetAssetFolder(Spider));
            Assert.Null(AssetPipelineRules.GetAssetId("Assets/MetinAR/Characters/Mobs"));
            Assert.Null(AssetPipelineRules.GetAssetId("Assets/MetinAR/Textures/Common/T_Common_X_BC.png"));
        }

        [Theory]
        [InlineData(Spider + "/ASSET_SOURCE.txt")]
        [InlineData(Spider + "/README.md")]
        [InlineData(Spider + "/Models/SK_Mob_VenomousSpider.fbx")]
        [InlineData(Spider + "/Models/SK_Mob_VenomousSpider_Body.fbx")]
        [InlineData(Spider + "/Animations/A_VenomousSpider_Idle.fbx")]
        [InlineData(Spider + "/Animations/A_VenomousSpider_Attack_02.fbx")]
        [InlineData(Spider + "/Animations/AC_VenomousSpider.controller")]
        [InlineData(Spider + "/Textures/T_VenomousSpider_BC.png")]
        [InlineData(Spider + "/Textures/T_VenomousSpider_N.png")]
        [InlineData(Spider + "/Textures/T_VenomousSpider_MS.tga")]
        [InlineData(Spider + "/Textures/T_VenomousSpider_Eyes_E.png")]
        [InlineData(Spider + "/Materials/M_VenomousSpider.mat")]
        [InlineData(Spider + "/Prefabs/PF_Mob_VenomousSpider.prefab")]
        [InlineData(Spider + "/Prefabs/PF_Mob_VenomousSpider_Elite.prefab")]
        [InlineData(Smith + "/Models/SK_Npc_Blacksmith.fbx")]
        [InlineData(Smith + "/Animations/A_Blacksmith_Hammering.fbx")]
        [InlineData(Smith + "/Animations/AM_Blacksmith_UpperBody.mask")]
        [InlineData(Hammer + "/Models/SM_Weapon_BlacksmithHammer.fbx")]
        [InlineData(Hammer + "/Prefabs/PF_Weapon_BlacksmithHammer.prefab")]
        [InlineData("Assets/MetinAR/Animations/HumanoidBase/A_HumanoidBase_Walk.fbx")]
        [InlineData("Assets/MetinAR/Animations/HumanoidBase/ASSET_SOURCE.txt")]
        [InlineData("Assets/MetinAR/Textures/Common/T_Common_Shadow_BC.png")]
        [InlineData("Assets/MetinAR/Materials/M_Shared.mat")]
        [InlineData("Assets/MetinAR/Characters/Mobs/README.md")]
        public void ValidNamesPass(string path)
        {
            Assert.Empty(AssetPipelineRules.ValidatePath(path));
        }

        [Theory]
        [InlineData(Spider + "/Models/spider.fbx")]
        [InlineData(Spider + "/Models/SK_Npc_VenomousSpider.fbx")]
        [InlineData(Spider + "/Models/SK_Mob_OtherSpider.fbx")]
        [InlineData(Spider + "/Textures/T_VenomousSpider_Diffuse.png")]
        [InlineData(Spider + "/Textures/T_VenomousSpider_BC.jpg")]
        [InlineData(Spider + "/Textures/T_OtherSpider_BC.png")]
        [InlineData(Spider + "/Animations/A_VenomousSpider.fbx")]
        [InlineData(Spider + "/Animations/A_Other_Idle.fbx")]
        [InlineData(Spider + "/Animations/VenomousSpider@Idle.fbx")]
        [InlineData(Spider + "/Animations/AC_Other.controller")]
        [InlineData(Spider + "/Materials/Spider.mat")]
        [InlineData(Spider + "/Prefabs/PF_Npc_VenomousSpider.prefab")]
        [InlineData(Spider + "/Prefabs/Spider.prefab")]
        [InlineData(Spider + "/Meshes/SK_Mob_VenomousSpider.fbx")]
        [InlineData(Spider + "/Models/Sub/SK_Mob_VenomousSpider.fbx")]
        [InlineData(Spider + "/SK_Mob_VenomousSpider.fbx")]
        [InlineData("Assets/MetinAR/Characters/Mobs/venomous_spider/ASSET_SOURCE.txt")]
        [InlineData("Assets/MetinAR/Characters/Mobs/Pająk/ASSET_SOURCE.txt")]
        [InlineData("Assets/MetinAR/Textures/ASSET_SOURCE.txt")]
        [InlineData("Assets/MetinAR/Textures/Common/Deep/T_Common_BC.png")]
        public void InvalidNamesFail(string path)
        {
            Assert.True(HasError(path), path);
        }

        [Theory]
        [InlineData(".blend")]
        [InlineData(".spp")]
        [InlineData(".psd")]
        [InlineData(".ma")]
        [InlineData(".BLEND")]
        public void DccWorkingFilesAreRejected(string ext)
        {
            List<PipelineIssue> issues = AssetPipelineRules.ValidatePath(Spider + "/Models/SK_Mob_VenomousSpider" + ext);
            Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("DCC"));
        }

        [Fact]
        public void WeaponAsSkinnedMeshWarnsOnly()
        {
            List<PipelineIssue> issues = AssetPipelineRules.ValidatePath(Hammer + "/Models/SK_Weapon_BlacksmithHammer.fbx");
            Assert.Single(issues);
            Assert.Equal(IssueSeverity.Warning, issues[0].Severity);
        }

        [Theory]
        [InlineData("T_VenomousSpider_BC.png", TextureRole.BaseColor, true)]
        [InlineData("T_VenomousSpider_N.png", TextureRole.Normal, false)]
        [InlineData("T_VenomousSpider_MS.png", TextureRole.MetallicSmoothness, false)]
        [InlineData("T_VenomousSpider_AO.tga", TextureRole.Occlusion, false)]
        [InlineData("T_VenomousSpider_Eyes_E.png", TextureRole.Emission, true)]
        [InlineData("T_VenomousSpider_M.png", TextureRole.Mask, false)]
        [InlineData("spider.png", TextureRole.Unknown, false)]
        public void TextureRolesAndColourSpace(string file, TextureRole role, bool srgb)
        {
            Assert.Equal(role, AssetPipelineRules.GetTextureRole(file));
            Assert.Equal(srgb, AssetPipelineRules.IsSrgb(role));
        }

        [Theory]
        [InlineData("SK_Mob_VenomousSpider.fbx", AssetKind.SkinnedModel)]
        [InlineData("SM_Weapon_BlacksmithHammer.fbx", AssetKind.StaticModel)]
        [InlineData("A_VenomousSpider_Idle.fbx", AssetKind.AnimationSource)]
        [InlineData("T_VenomousSpider_BC.png", AssetKind.Texture)]
        [InlineData("M_VenomousSpider.mat", AssetKind.Material)]
        [InlineData("PF_Mob_VenomousSpider.prefab", AssetKind.Prefab)]
        [InlineData("AC_VenomousSpider.controller", AssetKind.AnimatorController)]
        [InlineData("AM_Blacksmith_UpperBody.mask", AssetKind.AvatarMask)]
        [InlineData("ASSET_SOURCE.txt", AssetKind.SourceManifest)]
        [InlineData("README.md", AssetKind.Readme)]
        [InlineData("foo.fbx", AssetKind.Unknown)]
        public void KindFromPrefix(string file, AssetKind kind)
        {
            Assert.Equal(kind, AssetPipelineRules.GetKind(file));
        }

        [Fact]
        public void AnimationFileParsingAndLooping()
        {
            Assert.True(AssetPipelineRules.TryParseAnimationFile("A_VenomousSpider_Walk.fbx", out string id, out string state));
            Assert.Equal("VenomousSpider", id);
            Assert.Equal("Walk", state);
            Assert.True(AssetPipelineRules.TryParseAnimationFile("A_VenomousSpider_Attack_02.fbx", out _, out state));
            Assert.Equal("Attack", state);
            Assert.False(AssetPipelineRules.TryParseAnimationFile("A_VenomousSpider.fbx", out _, out _));
            Assert.Equal("A_VenomousSpider_Walk", AssetPipelineRules.ClipNameFromFile("A_VenomousSpider_Walk.fbx"));
            Assert.True(AssetPipelineRules.IsLoopingState("Idle"));
            Assert.True(AssetPipelineRules.IsLoopingState("Walk"));
            Assert.True(AssetPipelineRules.IsLoopingState("Hammering"));
            Assert.False(AssetPipelineRules.IsLoopingState("Attack"));
            Assert.False(AssetPipelineRules.IsLoopingState("Death"));
            Assert.False(AssetPipelineRules.IsLoopingState("Hit"));
        }

        [Fact]
        public void RequiredStates()
        {
            Assert.Equal(new[] { "Idle", "Walk", "Attack", "Hit", "Death" }, AssetPipelineRules.MissingRequiredStates(AssetCategory.Mob, new string[0]));
            Assert.Empty(AssetPipelineRules.MissingRequiredStates(AssetCategory.Mob, new[] { "Idle", "Walk", "Attack", "Hit", "Death", "Run" }));
            Assert.Equal(new[] { "Death" }, AssetPipelineRules.MissingRequiredStates(AssetCategory.Boss, new[] { "Idle", "Walk", "Attack", "Hit" }));
            Assert.Equal(new[] { "TurnLeft", "TurnRight", "Interact" }, AssetPipelineRules.MissingRequiredStates(AssetCategory.Npc, new[] { "Idle", "Walk", "Talk", "Hammering" }));
            Assert.Empty(AssetPipelineRules.MissingRequiredStates(AssetCategory.Weapon, new string[0]));
        }

        [Theory]
        [InlineData(AssetCategory.Mob, 8000, 40, 2, 1024, 3)]
        [InlineData(AssetCategory.Boss, 20000, 75, 3, 2048, 3)]
        [InlineData(AssetCategory.Npc, 20000, 75, 3, 2048, 3)]
        [InlineData(AssetCategory.Weapon, 3000, 0, 1, 1024, 1)]
        [InlineData(AssetCategory.Player, 10000, 40, 2, 1024, 1)]
        public void BudgetsMatchTheDocument(AssetCategory c, int tris, int bones, int mats, int tex, int lods)
        {
            AssetBudget b = AssetPipelineRules.GetBudget(c);
            Assert.Equal(tris, b.MaxTrianglesLod0);
            Assert.Equal(bones, b.MaxBones);
            Assert.Equal(mats, b.MaxMaterials);
            Assert.Equal(tex, b.MaxTextureSize);
            Assert.Equal(2048, b.MaxTextureSizeJustified);
            Assert.Equal(lods, b.RequiredLods);
        }

        [Fact]
        public void BudgetWithinLimitsHasNoIssues()
        {
            Assert.Empty(AssetPipelineRules.CheckBudget("x", AssetCategory.Mob, new[] { 6000, 3000, 1500 }, 32, 1));
            Assert.Empty(AssetPipelineRules.CheckBudget("x", AssetCategory.Npc, new[] { 18000, 9000, 4500 }, 65, 3));
            Assert.Empty(AssetPipelineRules.CheckBudget("x", AssetCategory.Weapon, new[] { 1800 }, 0, 1));
        }

        [Fact]
        public void BudgetViolationsAreErrors()
        {
            List<PipelineIssue> issues = AssetPipelineRules.CheckBudget("x", AssetCategory.Mob, new[] { 9000, 4000 }, 50, 3);
            Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("LOD level"));
            Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("LOD0 has 9000"));
            Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("bones"));
            Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("materials"));
        }

        [Fact]
        public void LodsMustShrink()
        {
            List<PipelineIssue> grow = AssetPipelineRules.CheckBudget("x", AssetCategory.Mob, new[] { 6000, 6000, 1000 }, 30, 1);
            Assert.Contains(grow, i => i.Severity == IssueSeverity.Error && i.Message.StartsWith("LOD1"));
            List<PipelineIssue> weak = AssetPipelineRules.CheckBudget("x", AssetCategory.Mob, new[] { 6000, 5000, 2500 }, 30, 1);
            Assert.Contains(weak, i => i.Severity == IssueSeverity.Warning && i.Message.StartsWith("LOD1"));
            Assert.Contains(weak, i => i.Severity == IssueSeverity.Warning && i.Message.StartsWith("LOD2"));
            Assert.DoesNotContain(weak, i => i.Severity == IssueSeverity.Error);
        }

        [Theory]
        [InlineData(AssetCategory.Mob, 1024, 1024, false, true)]
        [InlineData(AssetCategory.Mob, 2048, 2048, false, false)]
        [InlineData(AssetCategory.Mob, 2048, 2048, true, true)]
        [InlineData(AssetCategory.Npc, 2048, 2048, false, true)]
        [InlineData(AssetCategory.Npc, 4096, 4096, true, false)]
        [InlineData(AssetCategory.Mob, 1000, 1000, false, false)]
        [InlineData(AssetCategory.Weapon, 512, 1024, false, true)]
        public void TextureSizes(AssetCategory c, int w, int h, bool justified, bool ok)
        {
            Assert.Equal(ok, AssetPipelineRules.CheckTextureSize("t", c, w, h, justified).Count == 0);
        }

        [Theory]
        [InlineData("SK_Mob_VenomousSpider_LOD0", 0)]
        [InlineData("SK_Mob_VenomousSpider_LOD1", 1)]
        [InlineData("SK_Mob_VenomousSpider_lod2", 2)]
        [InlineData("SK_Mob_VenomousSpider", 0)]
        [InlineData("Body_LODx", 0)]
        public void LodIndexFromName(string name, int lod)
        {
            Assert.Equal(lod, AssetPipelineRules.LodIndex(name));
        }

        // ---- METINAR-FPP-ART-001: Player category (first-person arms)
        private const string Arms = "Assets/MetinAR/Characters/Player/FppArms";

        [Theory]
        [InlineData(Arms + "/ASSET_SOURCE.txt")]
        [InlineData(Arms + "/Models/SK_Player_FppArms.fbx")]
        [InlineData(Arms + "/Animations/A_FppArms_Idle.fbx")]
        [InlineData(Arms + "/Animations/A_FppArms_AuraActivate.fbx")]
        [InlineData(Arms + "/Animations/AC_FppArms.controller")]
        [InlineData(Arms + "/Textures/T_FppArms_MS.png")]
        [InlineData(Arms + "/Materials/M_FppArms.mat")]
        [InlineData(Arms + "/Prefabs/PF_Player_FppArms.prefab")]
        [InlineData("Assets/MetinAR/Weapons/FppSword/Models/SM_Weapon_FppSword.fbx")]
        [InlineData("Assets/MetinAR/Weapons/FppSword/Prefabs/PF_Weapon_FppSword.prefab")]
        public void PlayerNamesPass(string path)
        {
            Assert.False(HasError(path), string.Join("; ", AssetPipelineRules.ValidatePath(path)));
        }

        [Theory]
        [InlineData(Arms + "/Models/SK_Mob_FppArms.fbx")]
        [InlineData(Arms + "/Models/SK_Player_Other.fbx")]
        [InlineData(Arms + "/Prefabs/PF_Weapon_FppArms.prefab")]
        [InlineData(Arms + "/Animations/A_Other_Idle.fbx")]
        [InlineData(Arms + "/Rig/SK_Player_FppArms.fbx")]
        [InlineData("Assets/MetinAR/Characters/Player/fpp_arms/Models/SK_Player_fpp_arms.fbx")]
        [InlineData(Arms + "/Models/FppArms.blend")]
        public void PlayerBadNamesFail(string path)
        {
            Assert.True(HasError(path));
        }

        [Fact]
        public void PlayerFolderIsAPipelineFolderNotSilentlySkipped()
        {
            Assert.Contains(AssetPipelineRules.PlayerFolder, AssetPipelineRules.PipelineFolders);
            Assert.True(AssetPipelineRules.IsPipelinePath(Arms + "/Models/SK_Player_FppArms.fbx"));
            Assert.Equal(AssetCategory.Player, AssetPipelineRules.ClassifyPath(Arms + "/Models/SK_Player_FppArms.fbx"));
            Assert.Equal("FppArms", AssetPipelineRules.GetAssetId(Arms + "/Textures/T_FppArms_BC.png"));
            Assert.Equal(Arms, AssetPipelineRules.GetAssetFolder(Arms + "/Textures/T_FppArms_BC.png"));
            Assert.Equal("Player", AssetPipelineRules.CategoryToken(AssetCategory.Player));
        }

        [Fact]
        public void PlayerBudgetAndStates()
        {
            AssetBudget b = AssetPipelineRules.GetBudget(AssetCategory.Player);
            Assert.Equal(10000, b.MaxTrianglesLod0);
            Assert.Equal(40, b.MaxBones);
            Assert.Equal(2, b.MaxMaterials);
            Assert.Equal(1024, b.MaxTextureSize);
            Assert.Equal(1, b.RequiredLods);
            Assert.Empty(AssetPipelineRules.CheckBudget("a", AssetCategory.Player, new[] { 4932 }, 29, 1));
            Assert.Contains(AssetPipelineRules.CheckBudget("a", AssetCategory.Player, new[] { 12000 }, 41, 3), i => i.Message.Contains("bones"));
            Assert.Equal(new[] { "Idle", "Attack", "AuraActivate" }, AssetPipelineRules.MissingRequiredStates(AssetCategory.Player, new string[0]));
            Assert.Empty(AssetPipelineRules.MissingRequiredStates(AssetCategory.Player, new[] { "Idle", "Attack", "AuraActivate" }));
            Assert.True(AssetPipelineRules.IsLoopingState("Idle"));
            Assert.False(AssetPipelineRules.IsLoopingState("AuraActivate"));
        }

        // ---- METINAR-FPP-ART-001 regression: adding Player must not change the existing categories
        [Fact]
        public void PipelineFoldersAreExistingOnesPlusPlayer()
        {
            Assert.Equal(new[]
            {
                "Assets/MetinAR/Characters/Mobs", "Assets/MetinAR/Characters/Bosses", "Assets/MetinAR/Characters/NPC",
                "Assets/MetinAR/Weapons", "Assets/MetinAR/Characters/Player",
                "Assets/MetinAR/Animations", "Assets/MetinAR/Materials", "Assets/MetinAR/Textures",
            }, AssetPipelineRules.PipelineFolders);
        }

        [Theory]
        [InlineData(Spider + "/Models/SK_Player_VenomousSpider.fbx")]
        [InlineData(Spider + "/Prefabs/PF_Player_VenomousSpider.prefab")]
        [InlineData(Smith + "/Models/SK_Player_Blacksmith.fbx")]
        [InlineData(Hammer + "/Models/SM_Player_BlacksmithHammer.fbx")]
        [InlineData(Hammer + "/Prefabs/PF_Player_BlacksmithHammer.prefab")]
        [InlineData("Assets/MetinAR/Characters/Bosses/SpiderQueen/Models/SK_Player_SpiderQueen.fbx")]
        [InlineData(Arms + "/Models/SK_Npc_FppArms.fbx")]
        [InlineData(Arms + "/Models/SM_Weapon_FppArms.fbx")]
        [InlineData(Arms + "/Prefabs/PF_Mob_FppArms.prefab")]
        public void PlayerTokenDoesNotLeakIntoOtherCategories(string path)
        {
            Assert.True(HasError(path), path);
        }

        [Theory]
        [InlineData(Spider + "/Models/SK_Mob_VenomousSpider.fbx", AssetCategory.Mob, "VenomousSpider")]
        [InlineData("Assets/MetinAR/Characters/Bosses/SpiderQueen/Models/SK_Boss_SpiderQueen.fbx", AssetCategory.Boss, "SpiderQueen")]
        [InlineData(Smith + "/Models/SK_Npc_Blacksmith.fbx", AssetCategory.Npc, "Blacksmith")]
        [InlineData(Hammer + "/Models/SM_Weapon_BlacksmithHammer.fbx", AssetCategory.Weapon, "BlacksmithHammer")]
        [InlineData("Assets/MetinAR/Weapons/FppSword/Models/SM_Weapon_FppSword.fbx", AssetCategory.Weapon, "FppSword")]
        [InlineData(Arms + "/Models/SK_Player_FppArms.fbx", AssetCategory.Player, "FppArms")]
        public void EveryPerAssetCategoryIsValidatedNotSkipped(string path, AssetCategory c, string id)
        {
            Assert.True(AssetPipelineRules.IsPipelinePath(path));
            Assert.Equal(c, AssetPipelineRules.ClassifyPath(path));
            Assert.True(AssetPipelineRules.IsPerAssetCategory(c));
            Assert.Equal(id, AssetPipelineRules.GetAssetId(path));
            Assert.False(HasError(path), string.Join("; ", AssetPipelineRules.ValidatePath(path)));
            // a wrongly named file in the same folder must be caught (i.e. the folder is really validated)
            string bad = path.Substring(0, path.LastIndexOf('/') + 1) + "wrong_name.fbx";
            Assert.True(HasError(bad), bad);
        }

        [Fact]
        public void ExistingCategoryRulesUnchangedByPlayer()
        {
            Assert.Equal(new[] { "Idle", "Walk", "Attack", "Hit", "Death" }, AssetPipelineRules.MobRequiredStates);
            Assert.Equal(new[] { "Idle", "Walk", "TurnLeft", "TurnRight", "Talk", "Interact" }, AssetPipelineRules.NpcRequiredStates);
            Assert.Equal(new[] { "Idle", "Walk", "Run", "Hammering", "Talk" }, AssetPipelineRules.LoopingStates);
            Assert.Contains(AssetPipelineRules.CheckBudget("a", AssetCategory.Weapon, new[] { 1452 }, 1, 1), i => i.Message.Contains("bones"));
            Assert.Contains(AssetPipelineRules.CheckBudget("a", AssetCategory.Mob, new[] { 4932 }, 29, 1), i => i.Message.Contains("LOD"));
            Assert.Empty(AssetPipelineRules.CheckBudget("a", AssetCategory.Player, new[] { 4932 }, 29, 1));
        }
    }
}
