using System.IO;
using System.Linq;
using MetinAR.Editor.AssetPipeline;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace MetinAR.Editor.Characters
{
    /// <summary>
    /// METINAR-016: builds the URP material, animator controller and visual prefab PF_Npc_WanderingMerchant from the
    /// Blender-exported FBX/PNG files in Assets/MetinAR/Characters/NPC/WanderingMerchant (METINAR-009 template).
    /// Visual prefab only: no gameplay components. The world-map placeholder (Npc_WedrownyHandlarz) is not touched.
    /// Menu: MetinAR/Characters/Build Wandering Merchant.
    /// Batch: -executeMethod MetinAR.Editor.Characters.WanderingMerchantAssetBuilder.BuildBatch
    /// </summary>
    public static class WanderingMerchantAssetBuilder
    {
        public const string Id = "WanderingMerchant";
        public const string Folder = AssetPipelineRules.NpcFolder + "/" + Id;
        public const string ModelPath = Folder + "/Models/SK_Npc_WanderingMerchant.fbx";
        public const string MaterialPath = Folder + "/Materials/M_WanderingMerchant.mat";
        public const string ControllerPath = Folder + "/Animations/AC_WanderingMerchant.controller";
        public const string PrefabPath = Folder + "/Prefabs/PF_Npc_WanderingMerchant.prefab";
        public const string ShadowMaterialPath = "Assets/MetinAR/Art/ArEncounter/M_ArBlobShadow.mat";

        /// <summary>Required NPC states (METINAR-009) + the optional Gesture.</summary>
        public static readonly string[] States = { "Idle", "Walk", "TurnLeft", "TurnRight", "Talk", "Interact", "Gesture" };

        /// <summary>Clips whose root yaw is real motion (humanoid root rotation is not baked into the pose).</summary>
        public static readonly string[] RootRotationStates = { "TurnLeft", "TurnRight" };

        public static readonly float[] LodHeights = { 0.25f, 0.08f, 0.01f };

        public static readonly Vector3 BodyColliderCenter = new Vector3(0f, 0.92f, -0.04f);
        public const float BodyColliderRadius = 0.30f;
        public const float BodyColliderHeight = 1.84f;
        public static readonly Vector3 LabelAnchor = new Vector3(0f, 2.15f, 0f);

        public static string ClipPath(string state) => Folder + "/Animations/A_" + Id + "_" + state + ".fbx";

        [MenuItem("MetinAR/Characters/Build Wandering Merchant")]
        public static void BuildMenu()
        {
            Build();
            Debug.Log("[METINAR-016] Built " + PrefabPath);
        }

        public static void BuildBatch()
        {
            int code = 1;
            try
            {
                Build();
                Debug.Log("[METINAR-016] Built " + PrefabPath);
                code = 0;
            }
            catch (System.Exception e)
            {
                Debug.LogError("[METINAR-016] Build failed: " + e);
            }

            EditorApplication.Exit(code);
        }

        public static void Build()
        {
            Directory.CreateDirectory(Folder + "/Materials");
            Directory.CreateDirectory(Folder + "/Prefabs");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EnsureImportSettings();

            Material material = BuildMaterial();
            AnimatorController controller = BuildController();
            BuildPrefab(material, controller);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Model: Humanoid avatar created from the model (METINAR-009 NPC rule). Clips: Humanoid with
        /// 'Copy From Other Avatar' = the model's avatar, in place (root rotation/height/XZ baked into the pose,
        /// original orientation/position), except the turn clips, whose yaw stays root rotation.
        /// Re-applied every build, so files imported before the settings existed are fixed too.
        /// </summary>
        public static void EnsureImportSettings()
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            if (mi == null) throw new FileNotFoundException("Missing model " + ModelPath);
            bool dirty = false;
            if (mi.animationType != ModelImporterAnimationType.Human) { mi.animationType = ModelImporterAnimationType.Human; dirty = true; }
            if (mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; dirty = true; }
            if (mi.importAnimation) { mi.importAnimation = false; dirty = true; }
            if (dirty) mi.SaveAndReimport();

            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                throw new System.InvalidOperationException("Humanoid avatar of " + ModelPath + " is missing or invalid.");
            }

            foreach (string state in States)
            {
                string path = ClipPath(state);
                var ci = (ModelImporter)AssetImporter.GetAtPath(path);
                if (ci == null) throw new FileNotFoundException("Missing clip " + path);
                ci.animationType = ModelImporterAnimationType.Human;
                ci.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                ci.sourceAvatar = avatar;
                ci.importAnimation = true;
                ci.animationCompression = ModelImporterAnimationCompression.Optimal;
                ModelImporterClipAnimation[] clips = ci.clipAnimations.Length > 0 ? ci.clipAnimations : ci.defaultClipAnimations;
                if (clips.Length != 1) throw new System.InvalidOperationException("Expected one take in " + path);
                ModelImporterClipAnimation c = clips[0];
                c.name = AssetPipelineRules.ClipNameFromFile(Path.GetFileName(path));
                c.loopTime = AssetPipelineRules.IsLoopingState(state);
                c.loopPose = false;
                c.lockRootRotation = !RootRotationStates.Contains(state);
                c.keepOriginalOrientation = true;
                c.lockRootHeightY = true;
                c.keepOriginalPositionY = true;
                c.heightFromFeet = false;
                c.lockRootPositionXZ = true;
                c.keepOriginalPositionXZ = true;
                ci.clipAnimations = new[] { c };
                ci.SaveAndReimport();
            }
        }

        public static AnimationClip LoadClip(string state)
        {
            return AssetDatabase.LoadAllAssetsAtPath(ClipPath(state)).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        }

        private static Texture2D Tex(string role)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Textures/T_" + Id + "_" + role + ".png");
            if (t == null) throw new FileNotFoundException("Missing texture T_" + Id + "_" + role + ".png");
            return t;
        }

        private static Material BuildMaterial()
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) throw new System.InvalidOperationException("URP Lit shader not found.");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            bool create = mat == null;
            if (create) mat = new Material(lit);
            mat.shader = lit;
            mat.name = "M_" + Id;
            mat.SetFloat("_WorkflowMode", 1f);
            mat.SetFloat("_Surface", 0f);
            mat.SetTexture("_BaseMap", Tex("BC"));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BumpMap", Tex("N"));
            mat.SetFloat("_BumpScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_MetallicGlossMap", Tex("MS"));
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 1f);
            mat.SetFloat("_SmoothnessTextureChannel", 0f);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetTexture("_EmissionMap", Tex("E"));
            mat.SetColor("_EmissionColor", Color.white * 2f);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.enableInstancing = true;
            if (create) AssetDatabase.CreateAsset(mat, MaterialPath);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static AnimatorController BuildController()
        {
            // Rebuilt in place (states cleared and re-added), so the controller asset and its GUID are kept.
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            foreach (ChildAnimatorState child in sm.states)
            {
                sm.RemoveState(child.state);
            }

            for (int i = 0; i < States.Length; i++)
            {
                AnimationClip clip = LoadClip(States[i]);
                if (clip == null) throw new FileNotFoundException("Missing clip " + ClipPath(States[i]));
                AnimatorState state = sm.AddState(States[i], new Vector3(300f, 60f * i, 0f));
                state.motion = clip;
                if (States[i] == "Idle") sm.defaultState = state;
            }

            // No parameters/transitions: they belong to the NPC interaction/AI integration issue.
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void BuildPrefab(Material material, AnimatorController controller)
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null) throw new FileNotFoundException("Missing model " + ModelPath);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().First();

            var root = new GameObject("PF_Npc_" + Id);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
                model.name = "Model";
                model.transform.SetParent(root.transform, false);

                Animator animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                SkinnedMeshRenderer[] renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .OrderBy(r => AssetPipelineRules.LodIndex(r.name)).ToArray();
                if (renderers.Length != 3) throw new System.InvalidOperationException("Expected 3 LOD renderers, found " + renderers.Length);
                foreach (SkinnedMeshRenderer r in renderers)
                {
                    r.sharedMaterial = material;
                    r.shadowCastingMode = ShadowCastingMode.On;
                    r.updateWhenOffscreen = false;
                }

                LODGroup group = model.GetComponent<LODGroup>();
                if (group == null) group = model.AddComponent<LODGroup>();
                var lods = new LOD[3];
                for (int i = 0; i < 3; i++)
                {
                    lods[i] = new LOD(LodHeights[i], new Renderer[] { renderers[i] });
                }

                group.SetLODs(lods);
                group.RecalculateBounds();

                var colliders = new GameObject("Colliders").transform;
                colliders.SetParent(root.transform, false);
                var body = new GameObject("Body");
                body.transform.SetParent(colliders, false);
                var capsule = body.AddComponent<CapsuleCollider>();
                capsule.direction = 1;
                capsule.center = BodyColliderCenter;
                capsule.radius = BodyColliderRadius;
                capsule.height = BodyColliderHeight;

                var anchors = new GameObject("Anchors").transform;
                anchors.SetParent(root.transform, false);
                var label = new GameObject("Anchor_Label").transform;
                label.SetParent(anchors, false);
                label.localPosition = LabelAnchor;

                var shadowMat = AssetDatabase.LoadAssetAtPath<Material>(ShadowMaterialPath);
                if (shadowMat != null)
                {
                    GameObject shadow = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    shadow.name = "ContactShadow";
                    Object.DestroyImmediate(shadow.GetComponent<Collider>());
                    shadow.transform.SetParent(root.transform, false);
                    shadow.transform.localPosition = new Vector3(0f, 0.004f, -0.04f);
                    shadow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    shadow.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
                    var mr = shadow.GetComponent<MeshRenderer>();
                    mr.sharedMaterial = shadowMat;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                }

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
