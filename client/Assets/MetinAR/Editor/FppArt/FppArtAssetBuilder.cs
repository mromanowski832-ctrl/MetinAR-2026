using System.IO;
using System.Linq;
using MetinAR.Editor.AssetPipeline;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace MetinAR.Editor.FppArt
{
    /// <summary>
    /// METINAR-FPP-ART-001: builds the URP materials, the animator controller and the two VISUAL prefabs
    /// PF_Weapon_FppSword and PF_Player_FppArms from the Blender exports (art-source/FppHandsSword).
    /// Preview assets only: no gameplay components, no scene changes, no integration with FppCombatController (#31).
    /// Menu: MetinAR/FPP Art/Build Hands + Sword. Batch: -executeMethod MetinAR.Editor.FppArt.FppArtAssetBuilder.BuildBatch
    /// </summary>
    public static class FppArtAssetBuilder
    {
        public const string ArmsId = "FppArms";
        public const string SwordId = "FppSword";
        public const string ArmsFolder = AssetPipelineRules.PlayerFolder + "/" + ArmsId;
        public const string SwordFolder = AssetPipelineRules.WeaponsFolder + "/" + SwordId;
        public const string ArmsModelPath = ArmsFolder + "/Models/SK_Player_FppArms.fbx";
        public const string SwordModelPath = SwordFolder + "/Models/SM_Weapon_FppSword.fbx";
        public const string ArmsMaterialPath = ArmsFolder + "/Materials/M_FppArms.mat";
        public const string SwordMaterialPath = SwordFolder + "/Materials/M_FppSword.mat";
        public const string ControllerPath = ArmsFolder + "/Animations/AC_FppArms.controller";
        public const string ArmsPrefabPath = ArmsFolder + "/Prefabs/PF_Player_FppArms.prefab";
        public const string SwordPrefabPath = SwordFolder + "/Prefabs/PF_Weapon_FppSword.prefab";
        public const string SwordSocketBone = "B_Socket_Sword";
        public const string LeftGripSocketBone = "B_Socket_GripL";
        public static readonly string[] States = { "Idle", "Attack", "AuraActivate" };
        public static readonly string[] SwordSockets = { "Grip_Right", "Grip_Left", "FX_BladeBase", "FX_BladeTip", "FX_AuraBurst", "FX_GuardGem" };

        public static string ClipPath(string state) => ArmsFolder + "/Animations/A_" + ArmsId + "_" + state + ".fbx";

        [MenuItem("MetinAR/FPP Art/Build Hands + Sword")]
        public static void BuildMenu()
        {
            Build();
            Debug.Log("[METINAR-FPP-ART-001] Built " + ArmsPrefabPath + " and " + SwordPrefabPath);
        }

        public static void BuildBatch()
        {
            int code = 1;
            try
            {
                Build();
                Debug.Log("[METINAR-FPP-ART-001] Built " + ArmsPrefabPath + " and " + SwordPrefabPath);
                code = 0;
            }
            catch (System.Exception e)
            {
                Debug.LogError("[METINAR-FPP-ART-001] Build failed: " + e);
            }

            EditorApplication.Exit(code);
        }

        public static void Build()
        {
            foreach (string d in new[] { ArmsFolder + "/Materials", ArmsFolder + "/Prefabs", SwordFolder + "/Materials", SwordFolder + "/Prefabs" })
            {
                Directory.CreateDirectory(d);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EnsureImportSettings();
            Material armsMat = BuildMaterial(ArmsMaterialPath, ArmsFolder, ArmsId, emission: false);
            Material swordMat = BuildMaterial(SwordMaterialPath, SwordFolder, SwordId, emission: true);
            AnimatorController controller = BuildController();
            GameObject swordPrefab = BuildSwordPrefab(swordMat);
            BuildArmsPrefab(armsMat, controller, swordPrefab);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Re-applies the settings the METINAR-009 postprocessor sets on first import (files imported before the
        /// Player rule existed keep their old settings): Generic rig + Preserve Hierarchy on the arms and clip files,
        /// clip names/looping, no rig on the sword.
        /// </summary>
        public static void EnsureImportSettings()
        {
            foreach (string path in new[] { ArmsModelPath }.Concat(States.Select(ClipPath)))
            {
                if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) throw new FileNotFoundException("Missing model " + path);
                bool dirty = false;
                if (!mi.preserveHierarchy) { mi.preserveHierarchy = true; dirty = true; }
                if (mi.animationType != ModelImporterAnimationType.Generic) { mi.animationType = ModelImporterAnimationType.Generic; dirty = true; }
                if (path.Contains("/Animations/"))
                {
                    ModelImporterClipAnimation[] clips = mi.clipAnimations.Length > 0 ? mi.clipAnimations : mi.defaultClipAnimations;
                    AssetPipelineRules.TryParseAnimationFile(Path.GetFileName(path), out _, out string state);
                    string name = AssetPipelineRules.ClipNameFromFile(Path.GetFileName(path));
                    if (clips.Length == 1 && (clips[0].name != name || clips[0].loopTime != AssetPipelineRules.IsLoopingState(state) || mi.clipAnimations.Length == 0))
                    {
                        clips[0].name = name;
                        clips[0].loopTime = AssetPipelineRules.IsLoopingState(state);
                        mi.clipAnimations = clips;
                        dirty = true;
                    }
                }

                if (dirty)
                {
                    mi.SaveAndReimport();
                    Debug.Log("[METINAR-FPP-ART-001] Import settings fixed on " + path);
                }
            }
        }

        public static AnimationClip LoadClip(string state)
        {
            return AssetDatabase.LoadAllAssetsAtPath(ClipPath(state)).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        }

        private static Texture2D Tex(string folder, string id, string role, bool required = true)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/Textures/T_" + id + "_" + role + ".png");
            if (t == null && required) throw new FileNotFoundException("Missing texture T_" + id + "_" + role + ".png");
            return t;
        }

        private static Material BuildMaterial(string path, string folder, string id, bool emission)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) throw new System.InvalidOperationException("URP Lit shader not found.");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = mat == null;
            if (create) mat = new Material(lit);
            mat.shader = lit;
            mat.name = Path.GetFileNameWithoutExtension(path);
            mat.SetFloat("_WorkflowMode", 1f);
            mat.SetFloat("_Surface", 0f);
            mat.SetTexture("_BaseMap", Tex(folder, id, "BC"));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BumpMap", Tex(folder, id, "N"));
            mat.SetFloat("_BumpScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_MetallicGlossMap", Tex(folder, id, "MS"));
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 1f);
            mat.SetFloat("_SmoothnessTextureChannel", 0f);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            if (emission)
            {
                mat.SetTexture("_EmissionMap", Tex(folder, id, "E"));
                mat.SetColor("_EmissionColor", Color.white * 2f);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            mat.enableInstancing = true;
            if (create) AssetDatabase.CreateAsset(mat, path);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static AnimatorController BuildController()
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            {
                AssetDatabase.DeleteAsset(ControllerPath); // regenerated build output of this tool only
            }

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            for (int i = 0; i < States.Length; i++)
            {
                AnimationClip clip = LoadClip(States[i]);
                if (clip == null) throw new FileNotFoundException("Missing clip " + ClipPath(States[i]));
                AnimatorState state = sm.AddState(States[i], new Vector3(300f, 60f * i, 0f));
                state.motion = clip;
                if (States[i] == "Idle") sm.defaultState = state;
            }

            // No parameters/transitions: driving the states from FppCombatTimeline belongs to the #31 integration.
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static GameObject BuildSwordPrefab(Material material)
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SwordModelPath);
            if (modelAsset == null) throw new FileNotFoundException("Missing model " + SwordModelPath);
            var root = new GameObject("PF_Weapon_" + SwordId);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
                model.name = "Model";
                model.transform.SetParent(root.transform, false);
                foreach (MeshRenderer r in model.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.sharedMaterial = material;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }

                foreach (string socket in SwordSockets)
                {
                    if (FindDeep(model.transform, socket) == null) throw new System.InvalidOperationException("Sword socket missing: " + socket);
                }

                return PrefabUtility.SaveAsPrefabAsset(root, SwordPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void BuildArmsPrefab(Material material, AnimatorController controller, GameObject swordPrefab)
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ArmsModelPath);
            if (modelAsset == null) throw new FileNotFoundException("Missing model " + ArmsModelPath);
            var root = new GameObject("PF_Player_" + ArmsId);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
                model.name = "Model";
                model.transform.SetParent(root.transform, false);

                Animator animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // camera-attached: never culled

                foreach (SkinnedMeshRenderer r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    r.sharedMaterial = material;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.updateWhenOffscreen = true; // bounds follow the swing; camera-attached FPP mesh
                }

                Transform socket = FindDeep(model.transform, SwordSocketBone);
                if (socket == null) throw new System.InvalidOperationException("Missing " + SwordSocketBone);
                if (FindDeep(model.transform, LeftGripSocketBone) == null) throw new System.InvalidOperationException("Missing " + LeftGripSocketBone);
                var sword = (GameObject)PrefabUtility.InstantiatePrefab(swordPrefab);
                sword.name = "PF_Weapon_" + SwordId;
                sword.transform.SetParent(socket, false);
                sword.transform.localPosition = Vector3.zero;
                sword.transform.localRotation = Quaternion.identity;
                sword.transform.localScale = Vector3.one;

                PrefabUtility.SaveAsPrefabAsset(root, ArmsPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        public static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform f = FindDeep(c, name);
                if (f != null) return f;
            }

            return null;
        }
    }
}
