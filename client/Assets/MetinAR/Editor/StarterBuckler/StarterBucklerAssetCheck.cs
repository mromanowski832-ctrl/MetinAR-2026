using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MetinAR.Editor.AssetPipeline;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MetinAR.Editor.Weapons
{
    /// <summary>
    /// METINAR-032: batch verification of the starter_buckler asset in Unity (ITEMS_COMBAT_SPEC section 8 shield
    /// budget + METINAR-009 weapon template) and URP preview screenshots. Opens only a new unsaved scene and
    /// changes no asset. Batch (needs graphics, no -nographics):
    /// -executeMethod MetinAR.Editor.Weapons.StarterBucklerAssetCheck.Run
    /// Results: Builds/metinar-032/asset-check.txt, screenshots Builds/metinar-032/*.png
    /// </summary>
    public static class StarterBucklerAssetCheck
    {
        public const string OutDir = "Builds/metinar-032";

        // ITEMS_COMBAT_SPEC.md section 8 — "Shield / quiver / totem / focus (FPP)" (proposal): <= 1500 tris, 0 bones, 1 material, 512.
        public const int MaxTriangles = 1500;
        public const int TextureSize = 512;

        private static readonly List<string> Lines = new List<string>();
        private static int _fail;

        public static void Run()
        {
            Lines.Clear();
            _fail = 0;
            Directory.CreateDirectory(OutDir);
            try
            {
                Execute();
            }
            catch (Exception e)
            {
                Fail("X00 no exception", e.ToString());
            }

            var sb = new StringBuilder();
            sb.AppendLine("[METINAR-032] Starter buckler asset check: checks=" + Lines.Count(l => l.StartsWith("PASS") || l.StartsWith("FAIL")) + " failures=" + _fail);
            sb.AppendLine("INFO Unity " + Application.unityVersion + " | " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + "Z");
            foreach (string l in Lines) sb.AppendLine(l);
            File.WriteAllText(OutDir + "/asset-check.txt", sb.ToString());
            Debug.Log(sb.ToString());
            EditorApplication.Exit(_fail == 0 ? 0 : 1);
        }

        private static void Execute()
        {
            // ---------------- import settings
            var mi = (ModelImporter)AssetImporter.GetAtPath(StarterBucklerAssetBuilder.ModelPath);
            Check("C01 model import: no rig, no animation, no materials, Read/Write off, scale 1, Bake Axis Conversion, Preserve Hierarchy",
                mi != null && mi.animationType == ModelImporterAnimationType.None && !mi.importAnimation && mi.materialImportMode == ModelImporterMaterialImportMode.None
                && !mi.isReadable && Mathf.Approximately(mi.globalScale, 1f) && mi.bakeAxisConversion && mi.preserveHierarchy,
                mi == null ? "no importer" : mi.animationType + " anim=" + mi.importAnimation + " readable=" + mi.isReadable + " bakeAxis=" + mi.bakeAxisConversion + " preserve=" + mi.preserveHierarchy);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(StarterBucklerAssetBuilder.ModelPath);
            MeshFilter[] filters = model.GetComponentsInChildren<MeshFilter>(true);
            Info("model hierarchy: " + string.Join(", ", model.GetComponentsInChildren<Transform>(true).Select(t => HierPath(model.transform, t) + " pos" + t.localPosition.ToString("F4") + " rot" + t.localEulerAngles.ToString("F2"))));
            Check("C02 exactly one mesh, no SkinnedMeshRenderer (0 bones)", filters.Length == 1 && model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0, filters.Length + " mesh(es)");
            Mesh mesh = filters[0].sharedMesh;
            int tris = (int)(Enumerable.Range(0, mesh.subMeshCount).Sum(s => (long)mesh.GetIndexCount(s)) / 3);
            Info("mesh " + mesh.name + ": " + tris + " tris, " + mesh.vertexCount + " verts, " + mesh.subMeshCount + " submesh, uv0=" + mesh.HasVertexAttribute(VertexAttribute.TexCoord0) + ", tangents=" + mesh.HasVertexAttribute(VertexAttribute.Tangent));
            Check("C03 triangles <= " + MaxTriangles + " (ITEMS_COMBAT_SPEC section 8), 1 sub-mesh, UV0 + tangents", tris <= MaxTriangles && mesh.subMeshCount == 1 && mesh.HasVertexAttribute(VertexAttribute.TexCoord0) && mesh.HasVertexAttribute(VertexAttribute.Tangent), tris.ToString());

            // ---------------- geometry in model space (mesh node -> model root)
            Matrix4x4 toRoot = model.transform.worldToLocalMatrix * filters[0].transform.localToWorldMatrix;
            Bounds b = TransformBounds(mesh.bounds, toRoot);
            Info("bounds in model space (m): min " + b.min.ToString("F4") + " max " + b.max.ToString("F4") + " size " + b.size.ToString("F4"));
            Check("C04 size: diameter 0.36 m (±2 cm) in X and Y, depth <= 0.15 m", Mathf.Abs(b.size.x - 0.362f) < 0.02f && Mathf.Abs(b.size.y - 0.362f) < 0.02f && b.size.z <= 0.15f, b.size.ToString("F4"));
            Check("C05 pivot = grip: origin inside the shield footprint (|x|,|y| centred within 5 mm), front of the shield at +Z (max z > 0.08, min z > -0.03)",
                Mathf.Abs(b.center.x) < 0.005f && Mathf.Abs(b.center.y) < 0.005f && b.max.z > 0.08f && b.min.z > -0.03f, b.center.ToString("F4"));

            // facing: the boss (most forward geometry) must be at +Z and its normals must point to +Z
            Vector3[] verts = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector3 frontNormal = Vector3.zero;
            int frontCount = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 p = toRoot.MultiplyPoint3x4(verts[i]);
                if (p.z > b.max.z - 0.01f)
                {
                    frontNormal += toRoot.MultiplyVector(normals[i]);
                    frontCount++;
                }
            }

            frontNormal.Normalize();
            Check("C06 faces +Z: normals of the most forward vertices (boss top) point to +Z", frontCount > 0 && frontNormal.z > 0.9f, "n=" + frontCount + " mean normal " + frontNormal.ToString("F3"));

            // ---------------- sockets
            foreach (string s in StarterBucklerAssetBuilder.SocketNames)
            {
                Transform t = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == s);
                Vector3 pos = t == null ? Vector3.zero : model.transform.InverseTransformPoint(t.position);
                float ang = t == null ? 999f : Quaternion.Angle(Quaternion.Inverse(model.transform.rotation) * t.rotation, Quaternion.identity);
                Info("socket " + s + ": " + (t == null ? "missing" : "pos " + pos.ToString("F4") + ", rotation vs model " + ang.ToString("0.00") + " deg"));
                Check("C07 socket " + s + " present with the model's frame (rotation <= 0.5 deg)", t != null && ang <= 0.5f, t == null ? "missing" : ang.ToString("0.00") + " deg");
            }

            Transform grip = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "Socket_Grip");
            Check("C08 Socket_Grip at the pivot (<= 1 mm)", grip != null && model.transform.InverseTransformPoint(grip.position).magnitude <= 0.001f, null);
            Transform fx = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "Socket_FxBlock");
            Check("C09 Socket_FxBlock on the boss front (x,y ≈ 0, z = front, ±2 mm)", fx != null && Vector3.Distance(model.transform.InverseTransformPoint(fx.position), new Vector3(0f, 0f, b.max.z)) <= 0.002f,
                fx == null ? "missing" : model.transform.InverseTransformPoint(fx.position).ToString("F4"));

            // ---------------- textures
            foreach (string role in new[] { "BC", "N", "MS" })
            {
                string p = StarterBucklerAssetBuilder.Folder + "/Textures/T_StarterBuckler_" + role + ".png";
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                ti.GetSourceTextureWidthAndHeight(out int w, out int h);
                TextureImporterPlatformSettings a = ti.GetPlatformTextureSettings("Android");
                Info("texture " + role + ": " + w + "x" + h + " type=" + ti.textureType + " sRGB=" + ti.sRGBTexture + " max=" + ti.maxTextureSize + " Android=" + a.format + " mips=" + ti.mipmapEnabled);
                bool typeOk = role == "N" ? ti.textureType == TextureImporterType.NormalMap : ti.textureType == TextureImporterType.Default && ti.sRGBTexture == (role == "BC");
                Check("C10 texture " + role + " " + TextureSize + "x" + TextureSize + ", correct type/colour space, ASTC override", w == TextureSize && h == TextureSize && typeOk && a.overridden && a.format.ToString().StartsWith("ASTC"), w + "x" + h + " " + ti.textureType + " " + a.format);
            }

            // ---------------- material + prefab
            var mat = AssetDatabase.LoadAssetAtPath<Material>(StarterBucklerAssetBuilder.MaterialPath);
            Check("C11 M_StarterBuckler: URP Lit, BC/N/MS assigned", mat != null && mat.shader.name == "Universal Render Pipeline/Lit" && mat.GetTexture("_BaseMap") != null && mat.GetTexture("_BumpMap") != null && mat.GetTexture("_MetallicGlossMap") != null, mat == null ? "missing" : mat.shader.name);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StarterBucklerAssetBuilder.PrefabPath);
            Check("C12 PF_Weapon_StarterBuckler loads (root identity, scale 1)", prefab != null && prefab.transform.localPosition == Vector3.zero && prefab.transform.localRotation == Quaternion.identity && prefab.transform.localScale == Vector3.one, null);
            int missing = prefab.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            Check("C13 prefab: 0 missing scripts, no MonoBehaviours, no colliders (visual only)", missing == 0 && prefab.GetComponentsInChildren<MonoBehaviour>(true).Length == 0 && prefab.GetComponentsInChildren<Collider>(true).Length == 0, missing.ToString());
            MeshRenderer[] mrs = prefab.GetComponentsInChildren<MeshRenderer>(true);
            Check("C14 prefab: 1 renderer, 1 material (M_StarterBuckler), shadow casting off (FPP)", mrs.Length == 1 && mrs[0].sharedMaterials.Length == 1 && mrs[0].sharedMaterial == mat && mrs[0].shadowCastingMode == ShadowCastingMode.Off, mrs.Length + " renderer(s)");

            List<PipelineIssue> issues = AssetPipelineValidator.ValidateFolders(new[] { StarterBucklerAssetBuilder.Folder }, out int files);
            foreach (PipelineIssue i in issues) Info("validator: " + i);
            Check("C15 asset pipeline validator on Weapons/StarterBuckler: 0 errors, 0 warnings", issues.Count == 0, files + " files, " + issues.Count + " issues");

            Screenshots(prefab);
        }

        private static string HierPath(Transform root, Transform t)
        {
            return t == root ? t.name : HierPath(root, t.parent) + "/" + t.name;
        }

        private static Bounds TransformBounds(Bounds local, Matrix4x4 m)
        {
            var result = new Bounds(m.MultiplyPoint3x4(local.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                result.Encapsulate(m.MultiplyPoint3x4(new Vector3(
                    (i & 1) == 0 ? local.min.x : local.max.x,
                    (i & 2) == 0 ? local.min.y : local.max.y,
                    (i & 4) == 0 ? local.min.z : local.max.z)));
            }

            return result;
        }

        private static void Screenshots(GameObject prefab)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.transform.rotation = Quaternion.Euler(35f, 200f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.48f);
            var cam = new GameObject("Cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.32f, 0.35f);
            cam.fieldOfView = 35f;
            cam.nearClipPlane = 0.01f;
            Shot(cam, new Vector3(0f, 0.03f, 0.75f), "unity_front.png");
            Shot(cam, new Vector3(0.42f, 0.28f, 0.55f), "unity_hero_34.png");
            Shot(cam, new Vector3(-0.4f, 0.22f, -0.55f), "unity_back_34.png");
            Shot(cam, new Vector3(0.75f, 0.0f, 0.02f), "unity_side.png");
            UnityEngine.Object.DestroyImmediate(inst);
        }

        private static void Shot(Camera cam, Vector3 pos, string file)
        {
            const int w = 1000, h = 1000;
            cam.transform.position = pos;
            cam.transform.LookAt(new Vector3(0f, 0f, 0.04f));
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes(OutDir + "/" + file, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
            Info("screenshot " + file);
        }

        private static void Info(string s) => Lines.Add("INFO " + s);

        private static void Check(string name, bool ok, string detail)
        {
            if (ok) Lines.Add("PASS " + name + (detail != null ? " — " + detail : string.Empty));
            else Fail(name, detail);
        }

        private static void Fail(string name, string detail)
        {
            _fail++;
            Lines.Add("FAIL " + name + (detail != null ? " — " + detail : string.Empty));
        }
    }
}
