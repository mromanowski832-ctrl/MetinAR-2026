using System.IO;
using System.Linq;
using MetinAR.Editor.AssetPipeline;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace MetinAR.Editor.WanderingMerchant
{
    /// <summary>
    /// METINAR-016: builds the URP material, animator controller and visual prefab PF_Npc_WanderingMerchant from the
    /// Blender-exported FBX/PNG files in Assets/MetinAR/Characters/NPC/WanderingMerchant (METINAR-009 template, NPC
    /// variant: Humanoid avatar, LOD0/1/2, Anchor_Label/Anchor_Interact/Socket_HandR, no gameplay components).
    /// Menu: MetinAR/NPC/Build Wandering Merchant.
    /// Batch: -executeMethod MetinAR.Editor.WanderingMerchant.WanderingMerchantAssetBuilder.BuildBatch
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

        /// <summary>All six required NPC states of the METINAR-009 validator.</summary>
        public static readonly string[] States = { "Idle", "Walk", "TurnLeft", "TurnRight", "Talk", "Interact" };

        /// <summary>Turn clips keep their 90° body rotation as root rotation for the NPC integration; all others are baked in place.</summary>
        public static bool KeepsRootRotation(string state) => state == "TurnLeft" || state == "TurnRight";

        public static readonly float[] LodHeights = { 0.30f, 0.10f, 0.012f };
        public static readonly Vector3 BodyColliderCenter = new Vector3(0f, 0.92f, 0.02f);
        public const float BodyColliderRadius = 0.28f;
        public const float BodyColliderHeight = 1.84f;
        public static readonly Vector3 LabelAnchor = new Vector3(0f, 2.10f, 0f);
        public static readonly Vector3 InteractAnchor = new Vector3(0f, 1.10f, 0.60f);

        public static string ClipPath(string state) => Folder + "/Animations/A_" + Id + "_" + state + ".fbx";

        [MenuItem("MetinAR/NPC/Build Wandering Merchant")]
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
            Avatar avatar = EnsureModelAvatar();
            EnsureClipImportSettings(avatar);
            Material material = BuildMaterial();
            AnimatorController controller = BuildController();
            BuildPrefab(material, controller, avatar);
            AssetDatabase.SaveAssets();
        }

        public static Avatar LoadAvatar()
        {
            return AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
        }

        /// <summary>Humanoid, Create From This Model (bone names follow Unity's HumanBodyBones, so auto-mapping is exact).</summary>
        public static Avatar EnsureModelAvatar()
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            if (mi == null) throw new FileNotFoundException("Missing model " + ModelPath);
            if (mi.animationType != ModelImporterAnimationType.Human || mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.SaveAndReimport();
            }

            Avatar avatar = LoadAvatar();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                throw new System.InvalidOperationException("Humanoid avatar of " + ModelPath + " is missing or invalid.");
            }

            return avatar;
        }

        /// <summary>Clip files: Humanoid, Copy From Other Avatar (the SK_ model), one clip with loop/root settings per state.</summary>
        public static void EnsureClipImportSettings(Avatar avatar)
        {
            foreach (string state in States)
            {
                var ci = (ModelImporter)AssetImporter.GetAtPath(ClipPath(state));
                if (ci == null) throw new FileNotFoundException("Missing clip " + ClipPath(state));
                ci.animationType = ModelImporterAnimationType.Human;
                ci.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                ci.sourceAvatar = avatar;
                ci.importAnimation = true;
                ci.animationCompression = ModelImporterAnimationCompression.Optimal;
                ModelImporterClipAnimation[] clips = ci.defaultClipAnimations;
                if (clips == null || clips.Length != 1) throw new System.InvalidOperationException(ClipPath(state) + " must hold exactly one take.");
                ModelImporterClipAnimation c = clips[0];
                c.name = "A_" + Id + "_" + state;
                c.loopTime = AssetPipelineRules.IsLoopingState(state);
                c.loopPose = false;
                c.lockRootRotation = !KeepsRootRotation(state);   // Bake Into Pose: rotation
                c.keepOriginalOrientation = true;                 // based upon Original
                c.lockRootHeightY = true;                         // Bake Into Pose: position Y
                c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = true;                      // in place: Bake Into Pose XZ
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
            mat.SetColor("_EmissionColor", Color.white * 1.5f);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.enableInstancing = true;
            if (create) AssetDatabase.CreateAsset(mat, MaterialPath);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static AnimatorController BuildController()
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            {
                AssetDatabase.DeleteAsset(ControllerPath);
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

            // No parameters/transitions: they belong to the NPC interaction integration (METINAR-009 section 4).
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void BuildPrefab(Material material, AnimatorController controller, Avatar avatar)
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null) throw new FileNotFoundException("Missing model " + ModelPath);

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
                var interact = new GameObject("Anchor_Interact").transform;
                interact.SetParent(anchors, false);
                interact.localPosition = InteractAnchor;

                // Socket for a held prop: child of the humanoid right-hand bone.
                Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                if (hand == null) throw new System.InvalidOperationException("Avatar has no RightHand bone.");
                var socket = new GameObject("Socket_HandR").transform;
                socket.SetParent(hand, false);

                var shadowMat = AssetDatabase.LoadAssetAtPath<Material>(ShadowMaterialPath);
                if (shadowMat != null)
                {
                    GameObject shadow = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    shadow.name = "ContactShadow";
                    Object.DestroyImmediate(shadow.GetComponent<Collider>());
                    shadow.transform.SetParent(root.transform, false);
                    shadow.transform.localPosition = new Vector3(0f, 0.004f, 0.02f);
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
