using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MetinAR.ArEncounter;
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
    /// #91: final gauntlets/forearms (two material slots: glove M_FppArms_GloveWarrior01 + sleeve M_FppArms, glove
    /// swappable), three weapon prefabs (PF_Weapon_FppSword, PF_Weapon_FppSword02, PF_Weapon_FppGreatsword), all
    /// combo/block/aura clips with loop flags and OnFppAnimEvent events from clip_reference.json, FppArmsAnimationEvents
    /// receiver on the animated model. The #63 buckler is NOT built here (B_Socket_ShieldL is its attach point).
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
        /// <summary>Buckler attach point (#59 / PR #63 SM_Weapon_StarterBuckler pivot: fist-grip centre, handle +Y, front +Z).</summary>
        public const string ShieldSocketBone = "B_Socket_ShieldL";
        public const string GloveId = "GloveWarrior01";
        public const string GloveMaterialPath = ArmsFolder + "/Materials/M_FppArms_" + GloveId + ".mat";
        public const string EventFunction = "OnFppAnimEvent";
        /// <summary>Written by art-source/FppHandsSword/fpp_build.py final91 (relative to the Unity project folder).</summary>
        public const string ClipReferencePath = "../art-source/FppHandsSword/clip_reference.json";

        /// <summary>V3 contract clips (#34/#36, unchanged values) followed by the #91 combo/block/aura clips.</summary>
        public static readonly string[] V3States = { "Idle", "Attack", "AuraActivate" };
        public static readonly string[] ComboStates =
        {
            "Attack1H_01", "Attack1H_02", "Attack1H_03", "Attack2H_01", "Attack2H_02", "Attack2H_03", "AuraActivate2H",
            "ShieldBlock", "ShieldBlockHold", "ShieldBlockEnd", "Idle2H", "ShieldIdle", "AuraActivate1H",
        };
        public static readonly string[] States = V3States.Concat(ComboStates).ToArray();
        /// <summary>Looping clips (AssetPipelineRules only knows "Idle"; the builder forces the rest).</summary>
        public static readonly string[] LoopStates = { "Idle", "Idle2H", "ShieldIdle", "ShieldBlockHold" };
        public static bool IsLoop(string state) => Array.IndexOf(LoopStates, state) >= 0;

        public sealed class WeaponDef
        {
            public string Id;
            public string Folder => AssetPipelineRules.WeaponsFolder + "/" + Id;
            public string ModelPath => Folder + "/Models/SM_Weapon_" + Id + ".fbx";
            public string MaterialPath => Folder + "/Materials/M_" + Id + ".mat";
            public string PrefabPath => Folder + "/Prefabs/PF_Weapon_" + Id + ".prefab";
        }

        /// <summary>FppSword = 1H (nested in PF_Player_FppArms, V3 checks), FppSword02 = second 1H (same hilt/sockets), FppGreatsword = 2H.</summary>
        public static readonly WeaponDef[] Weapons = { new WeaponDef { Id = SwordId }, new WeaponDef { Id = "FppSword02" }, new WeaponDef { Id = "FppGreatsword" } };

        public static WeaponDef WeaponOf(string meshName) => Weapons.First(w => "SM_Weapon_" + w.Id == meshName);
        public static readonly string[] SwordSockets = { "Socket_GripR", "Socket_GripL", "Socket_FxBladeBase", "Socket_FxBladeTip", "Socket_FxAuraBurst", "Socket_FxGuardGem" };

        /// <summary>Contract the clips are baked from (art-source/FppHandsSword/fpp_contract.py CONTRACT_ID = FppPoseContract.RetestV3.Id).</summary>
        public const string ContractId = "v3-a31-retest";
        /// <summary>Uniform scale of the nested sword model around B_Socket_Sword (FppViewFraming.SwordScale, #31 V3).</summary>
        public const float SwordScale = 0.75f;

        public const float ClipRotationError = 0.05f;
        public const float ClipPositionError = 0.05f;
        public const float ClipScaleError = 0.05f;

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
            catch (Exception e)
            {
                Debug.LogError("[METINAR-FPP-ART-001] Build failed: " + e);
            }

            EditorApplication.Exit(code);
        }

        public static void Build()
        {
            foreach (string d in new[] { ArmsFolder + "/Materials", ArmsFolder + "/Prefabs" }.Concat(Weapons.SelectMany(w => new[] { w.Folder + "/Materials", w.Folder + "/Prefabs" })))
            {
                Directory.CreateDirectory(d);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EnsureImportSettings();
            Material armsMat = BuildMaterial(ArmsMaterialPath, ArmsFolder, ArmsId, emission: Tex(ArmsFolder, ArmsId, "E", false) != null);
            Material gloveMat = BuildMaterial(GloveMaterialPath, ArmsFolder, ArmsId + "_" + GloveId, emission: true);
            AnimatorController controller = BuildController();
            GameObject swordPrefab = null;
            foreach (WeaponDef w in Weapons)
            {
                Material m = BuildMaterial(w.MaterialPath, w.Folder, w.Id, emission: true);
                GameObject prefab = BuildSwordPrefab(m, w);
                if (w.Id == SwordId) swordPrefab = prefab;
            }

            BuildArmsPrefab(new[] { gloveMat, armsMat }, controller, swordPrefab);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Re-applies the settings the METINAR-009 postprocessor sets on first import (files imported before the
        /// Player rule existed keep their old settings): Generic rig + Preserve Hierarchy on the arms and clip files,
        /// clip names/looping, no rig on the sword.
        /// </summary>
        public static void EnsureImportSettings()
        {
            ClipReference refs = LoadClipReference();
            foreach (string path in new[] { ArmsModelPath }.Concat(States.Select(ClipPath)))
            {
                if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) throw new FileNotFoundException("Missing model " + path);
                bool dirty = false;
                if (!mi.preserveHierarchy) { mi.preserveHierarchy = true; dirty = true; }
                if (mi.animationType != ModelImporterAnimationType.Generic) { mi.animationType = ModelImporterAnimationType.Generic; dirty = true; }
                if (path.Contains("/Animations/") && (mi.animationRotationError > ClipRotationError || mi.animationPositionError > ClipPositionError || mi.animationScaleError > ClipScaleError))
                {
                    // keep Optimal compression but with tight tolerances: the left socket is the end of a 4-bone chain,
                    // default 0.5 deg errors moved it ~4 mm (measured); the contract check allows 2 mm / 1 deg
                    mi.animationRotationError = ClipRotationError;
                    mi.animationPositionError = ClipPositionError;
                    mi.animationScaleError = ClipScaleError;
                    dirty = true;
                }
                if (path.Contains("/Animations/"))
                {
                    ModelImporterClipAnimation[] clips = mi.clipAnimations.Length > 0 ? mi.clipAnimations : mi.defaultClipAnimations;
                    string state = StateOfClipPath(path);
                    string name = AssetPipelineRules.ClipNameFromFile(Path.GetFileName(path));
                    bool loop = IsLoop(state);
                    AnimationEvent[] events = ClipEvents(state, refs);
                    if (clips.Length == 1 && (clips[0].name != name || clips[0].loopTime != loop || mi.clipAnimations.Length == 0 || !SameEvents(clips[0].events, events)))
                    {
                        clips[0].name = name;
                        clips[0].loopTime = loop;
                        clips[0].events = events;
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

        public static string StateOfClipPath(string path)
        {
            string f = Path.GetFileNameWithoutExtension(path);
            string prefix = "A_" + ArmsId + "_";
            return f.StartsWith(prefix, StringComparison.Ordinal) ? f.Substring(prefix.Length) : f;
        }

        // ---- clip_reference.json (JsonUtility layout written by fpp_build.py final91)
        [Serializable] public sealed class EventRef { public string id; public float time; }

        /// <summary>Prefab-root (model parent) space; position xyz + quaternion xyzw, every frame.</summary>
        [Serializable] public sealed class SampleRef { public float t; public float[] sword; public float[] gripL; public float[] shieldL; }

        [Serializable]
        public sealed class ClipRef
        {
            public string state, file, weapon;
            public int frames;
            public float seconds;
            public bool loop, buckler, leftGrips;
            public EventRef[] events;
            public SampleRef[] samples;
        }

        [Serializable] public sealed class ClipReference { public string contract_id; public int fps; public string buckler_asset; public ClipRef[] clips; }

        public static ClipReference LoadClipReference()
        {
            if (!File.Exists(ClipReferencePath)) throw new FileNotFoundException("Missing " + ClipReferencePath + " (fpp_build.py final91)");
            var r = JsonUtility.FromJson<ClipReference>(File.ReadAllText(ClipReferencePath));
            if (r == null || r.clips == null || r.contract_id != ContractId) throw new InvalidOperationException("clip_reference.json unreadable or contract id != " + ContractId);
            return r;
        }

        public static ClipRef FindClip(ClipReference r, string state) => r.clips.FirstOrDefault(c => c.state == state);

        /// <summary>Model-importer clip events (normalized time) for one state: OnFppAnimEvent(id).</summary>
        public static AnimationEvent[] ClipEvents(string state, ClipReference r)
        {
            ClipRef c = FindClip(r, state);
            if (c == null) throw new InvalidOperationException("clip_reference.json has no clip " + state);
            var list = new List<AnimationEvent>();
            foreach (EventRef e in c.events)
            {
                list.Add(new AnimationEvent { functionName = EventFunction, stringParameter = e.id, time = Mathf.Clamp01(c.seconds > 0f ? e.time / c.seconds : 0f) });
            }

            return list.ToArray();
        }

        private static bool SameEvents(AnimationEvent[] a, AnimationEvent[] b)
        {
            int n = a?.Length ?? 0;
            if (n != (b?.Length ?? 0)) return false;
            for (int i = 0; i < n; i++)
            {
                if (a[i].functionName != b[i].functionName || a[i].stringParameter != b[i].stringParameter || Mathf.Abs(a[i].time - b[i].time) > 1e-5f) return false;
            }

            return true;
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
            if (lit == null) throw new InvalidOperationException("URP Lit shader not found.");
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

        /// <summary>
        /// Idempotent: an existing AC_FppArms.controller is updated in place (same asset GUID, same state sub-assets and
        /// fileIDs); only a missing controller is created. Re-running the builder on unchanged clips leaves the file
        /// byte-identical (no fileID churn, see #36).
        /// </summary>
        private static AnimatorController BuildController()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            bool dirty = false;
            if (controller.layers.Length == 0)
            {
                controller.AddLayer("Base Layer");
                dirty = true;
            }

            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            foreach (ChildAnimatorState child in sm.states)
            {
                if (Array.IndexOf(States, child.state.name) < 0)
                {
                    sm.RemoveState(child.state);
                    dirty = true;
                }
            }

            for (int i = 0; i < States.Length; i++)
            {
                AnimationClip clip = LoadClip(States[i]);
                if (clip == null) throw new FileNotFoundException("Missing clip " + ClipPath(States[i]));
                AnimatorState state = sm.states.Select(c => c.state).FirstOrDefault(s => s.name == States[i]);
                if (state == null)
                {
                    state = sm.AddState(States[i], new Vector3(300f + 260f * (i / 8), 60f * (i % 8), 0f));
                    dirty = true;
                }

                if (state.motion != clip)
                {
                    state.motion = clip;
                    dirty = true;
                }

                if (state.transitions.Length > 0)
                {
                    state.transitions = new AnimatorStateTransition[0];
                    dirty = true;
                }

                if (States[i] == "Idle" && sm.defaultState != state)
                {
                    sm.defaultState = state;
                    dirty = true;
                }
            }

            // No parameters/transitions: driving the states from FppCombatTimeline belongs to the #31 integration.
            if (dirty) EditorUtility.SetDirty(controller);
            return controller;
        }

        private static GameObject BuildSwordPrefab(Material material, WeaponDef w)
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(w.ModelPath);
            if (modelAsset == null) throw new FileNotFoundException("Missing model " + w.ModelPath);
            var root = new GameObject("PF_Weapon_" + w.Id);
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
                    if (FindDeep(model.transform, socket) == null) throw new InvalidOperationException(w.Id + " socket missing: " + socket);
                }

                return PrefabUtility.SaveAsPrefabAsset(root, w.PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <param name="materials">slot 0 = glove (M_FppArms_GloveWarrior01, swappable), slot 1 = sleeve/forearm (M_FppArms)</param>
        private static void BuildArmsPrefab(Material[] materials, AnimatorController controller, GameObject swordPrefab)
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
                if (model.GetComponent<FppArmsAnimationEvents>() == null) model.AddComponent<FppArmsAnimationEvents>(); // OnFppAnimEvent receiver

                foreach (SkinnedMeshRenderer r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    int slots = r.sharedMesh != null ? r.sharedMesh.subMeshCount : 1;
                    if (slots != materials.Length) throw new InvalidOperationException(r.name + ": " + slots + " submeshes, expected " + materials.Length + " (glove + sleeve)");
                    r.sharedMaterials = materials;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.updateWhenOffscreen = true; // bounds follow the swing; camera-attached FPP mesh
                }

                Transform socket = FindDeep(model.transform, SwordSocketBone);
                if (socket == null) throw new InvalidOperationException("Missing " + SwordSocketBone);
                if (FindDeep(model.transform, LeftGripSocketBone) == null) throw new InvalidOperationException("Missing " + LeftGripSocketBone);
                if (FindDeep(model.transform, ShieldSocketBone) == null) throw new InvalidOperationException("Missing " + ShieldSocketBone);
                var sword = (GameObject)PrefabUtility.InstantiatePrefab(swordPrefab);
                sword.name = "PF_Weapon_" + SwordId;
                sword.transform.SetParent(socket, false);
                sword.transform.localPosition = Vector3.zero;
                sword.transform.localRotation = Quaternion.identity;
                sword.transform.localScale = Vector3.one * SwordScale; // contract V3: sword drawn at 0.75 around the grip

                PrefabUtility.SaveAsPrefabAsset(root, ArmsPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
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
