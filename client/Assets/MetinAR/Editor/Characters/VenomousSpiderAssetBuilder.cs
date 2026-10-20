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
    /// METINAR-013: builds the URP material, animator controller and visual prefab PF_Mob_VenomousSpider from the
    /// Blender-exported FBX/PNG files in Assets/MetinAR/Characters/Mobs/VenomousSpider (METINAR-009 template).
    /// Visual prefab only: no gameplay components; the live AR placeholder (Prefabs/ArPrototypeSpider) is untouched.
    /// Menu: MetinAR/Characters/Build Venomous Spider. Batch: -executeMethod MetinAR.Editor.Characters.VenomousSpiderAssetBuilder.BuildBatch
    /// </summary>
    public static class VenomousSpiderAssetBuilder
    {
        public const string Id = "VenomousSpider";
        public const string Folder = AssetPipelineRules.MobsFolder + "/" + Id;
        public const string ModelPath = Folder + "/Models/SK_Mob_VenomousSpider.fbx";
        public const string MaterialPath = Folder + "/Materials/M_VenomousSpider.mat";
        public const string ControllerPath = Folder + "/Animations/AC_VenomousSpider.controller";
        public const string PrefabPath = Folder + "/Prefabs/PF_Mob_VenomousSpider.prefab";
        public const string ShadowMaterialPath = "Assets/MetinAR/Art/ArEncounter/M_ArBlobShadow.mat";
        public static readonly string[] States = { "Idle", "Walk", "Attack", "Hit", "Death" };

        // LODGroup screen-relative transition heights (METINAR-009 section 5, proposal).
        public static readonly float[] LodHeights = { 0.25f, 0.08f, 0.01f };

        // Body hit volume in prefab space (Unity: +Z forward, Y up), fitted to cephalothorax + abdomen.
        public static readonly Vector3 BodyColliderCenter = new Vector3(0f, 0.36f, -0.10f);
        public const float BodyColliderRadius = 0.22f;
        public const float BodyColliderHeight = 1.00f;
        public static readonly Vector3 LabelAnchor = new Vector3(0f, 0.95f, 0f);

        public static string ClipPath(string state) => Folder + "/Animations/A_" + Id + "_" + state + ".fbx";

        [MenuItem("MetinAR/Characters/Build Venomous Spider")]
        public static void BuildMenu()
        {
            Build();
            Debug.Log("[METINAR-013] Built " + PrefabPath);
        }

        public static void BuildBatch()
        {
            int code = 1;
            try
            {
                Build();
                code = 0;
            }
            catch (System.Exception e)
            {
                Debug.LogError("[METINAR-013] Build failed: " + e);
            }

            EditorApplication.Exit(code);
        }

        public static void Build()
        {
            Directory.CreateDirectory(Folder + "/Materials");
            Directory.CreateDirectory(Folder + "/Prefabs");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Material material = BuildMaterial();
            AnimatorController controller = BuildController();
            BuildPrefab(material, controller);
            AssetDatabase.SaveAssets();
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

            // No parameters/transitions: they belong to the combat/AI integration issue (METINAR-009 section 4).
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void BuildPrefab(Material material, AnimatorController controller)
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null) throw new FileNotFoundException("Missing model " + ModelPath);

            var root = new GameObject("PF_Mob_" + Id);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
                model.name = "Model";
                model.transform.SetParent(root.transform, false);

                Animator animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
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
                capsule.direction = 2;
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
                    shadow.transform.localPosition = new Vector3(0f, 0.004f, -0.02f);
                    shadow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    shadow.transform.localScale = new Vector3(1.5f, 1.4f, 1f);
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
