using System.IO;
using MetinAR.Editor.AssetPipeline;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MetinAR.Editor.Weapons
{
    /// <summary>
    /// METINAR-032: builds the URP material and the visual prefab PF_Weapon_StarterBuckler (starter_buckler,
    /// "Lipowy Puklerz") from the Blender-exported FBX/PNG files in Assets/MetinAR/Weapons/StarterBuckler.
    /// Visual prefab only (no gameplay components, no collider). Pivot = centre of the grip, handle along +Y,
    /// front of the shield towards +Z: it is meant to sit on a left-hand grip frame (integration issue).
    /// Menu: MetinAR/Weapons/Build Starter Buckler. Batch: -executeMethod MetinAR.Editor.Weapons.StarterBucklerAssetBuilder.BuildBatch
    /// </summary>
    public static class StarterBucklerAssetBuilder
    {
        public const string Id = "StarterBuckler";
        public const string Folder = AssetPipelineRules.WeaponsFolder + "/" + Id;
        public const string ModelPath = Folder + "/Models/SM_Weapon_StarterBuckler.fbx";
        public const string MaterialPath = Folder + "/Materials/M_StarterBuckler.mat";
        public const string PrefabPath = Folder + "/Prefabs/PF_Weapon_StarterBuckler.prefab";
        public static readonly string[] SocketNames = { "Socket_Grip", "Socket_FxBlock" };

        [MenuItem("MetinAR/Weapons/Build Starter Buckler")]
        public static void BuildMenu()
        {
            Build();
            Debug.Log("[METINAR-032] Built " + PrefabPath);
        }

        public static void BuildBatch()
        {
            int code = 1;
            try
            {
                Build();
                Debug.Log("[METINAR-032] Built " + PrefabPath);
                code = 0;
            }
            catch (System.Exception e)
            {
                Debug.LogError("[METINAR-032] Build failed: " + e);
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
            BuildPrefab(material);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Static prop: no rig, no animation, no extracted materials, Read/Write off (METINAR-009 defaults), plus
        /// Preserve Hierarchy so the FBX root node and its socket children keep their names and frames.
        /// </summary>
        public static void EnsureImportSettings()
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            if (mi == null) throw new FileNotFoundException("Missing model " + ModelPath);
            bool dirty = false;
            if (!mi.preserveHierarchy) { mi.preserveHierarchy = true; dirty = true; }
            if (mi.animationType != ModelImporterAnimationType.None) { mi.animationType = ModelImporterAnimationType.None; dirty = true; }
            if (mi.importAnimation) { mi.importAnimation = false; dirty = true; }
            if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
            if (mi.isReadable) { mi.isReadable = false; dirty = true; }
            if (dirty) mi.SaveAndReimport();
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
            mat.enableInstancing = true;
            if (create) AssetDatabase.CreateAsset(mat, MaterialPath);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void BuildPrefab(Material material)
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null) throw new FileNotFoundException("Missing model " + ModelPath);

            var root = new GameObject("PF_Weapon_" + Id);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
                model.name = "Model";
                model.transform.SetParent(root.transform, false);
                foreach (MeshRenderer r in model.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.sharedMaterial = material;
                    // camera-attached FPP prop (same as the FPP sword in PR #36): no shadow casting
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = true;
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
