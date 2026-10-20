using System.Collections.Generic;
using MetinAR.ArEncounter;
using MetinAR.HandTracking;
using TMPro;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

namespace MetinAR.Editor
{
    /// <summary>
    /// METINAR-007: builds the original procedural prototype sword (mesh, materials, prefab) and adds hand tracking
    /// (<see cref="ArHandTracker"/>), the hand-attached sword (<see cref="ArHandSword"/>) and the hand-tracking HUD
    /// readout to the existing AR encounter scene. Idempotent (re-running replaces its own objects only).
    /// Usage: Unity -batchmode -quit -projectPath client -executeMethod MetinAR.Editor.ArHandSwordPrototypeBuilder.BuildAll
    /// (also called from <see cref="ArEncounterPrototypeSceneBuilder.BuildAll"/>).
    /// </summary>
    public static class ArHandSwordPrototypeBuilder
    {
        public const string ModelFolder = "Assets/MetinAR/ThirdParty/BlazeHand";
        public const string PalmModelPath = ModelFolder + "/hand_detector.onnx";
        public const string LandmarkModelPath = ModelFolder + "/hand_landmarks_detector.onnx";
        public const string SwordPrefabPath = "Assets/MetinAR/Prefabs/ArPrototypeSword.prefab";
        private const string ArtFolder = "Assets/MetinAR/Art/ArEncounter";
        private const string BladeMeshPath = ArtFolder + "/MSH_ArSwordBlade.asset";
        private const string DotSpritePath = ArtFolder + "/UI_ArDisc.png";

        public const float BladeStart = 0.09f;
        public const float BladeLength = 0.75f;

        private static readonly Color Stone = new Color(0.11f, 0.10f, 0.095f, 0.90f);
        private static readonly Color Parchment = new Color(0.95f, 0.88f, 0.70f, 1f);

        public static void BuildAll()
        {
            Apply();
            Debug.Log("[ArHandSwordPrototypeBuilder] done");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }

        public static void Apply()
        {
            GameObject swordPrefab = BuildSwordPrefab();
            Scene scene = EditorSceneManager.OpenScene(ArEncounterScenes.ArEncounterScenePath, OpenSceneMode.Single);
            AddToScene(swordPrefab);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[ArHandSwordPrototypeBuilder] AR scene updated: " + ArEncounterScenes.ArEncounterScenePath);
        }

        // ------------------------------------------------------------------ sword

        private static Material Mat(string name, Color color, float metallic, float smoothness)
        {
            string path = ArtFolder + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Diamond-section tapered blade along +Z (edges ±Y, flats ±X), flat-shaded, original geometry.</summary>
        private static Mesh BuildBladeMesh()
        {
            const float baseWidth = 0.05f;
            const float midWidth = 0.04f;
            const float thickness = 0.009f;
            float z0 = BladeStart;
            float z1 = BladeStart + BladeLength - 0.13f;
            float zTip = BladeStart + BladeLength;
            Vector3[] Section(float z, float w) => new[]
            {
                new Vector3(0f, 0.5f * w, z),
                new Vector3(0.5f * thickness, 0f, z),
                new Vector3(0f, -0.5f * w, z),
                new Vector3(-0.5f * thickness, 0f, z),
            };

            Vector3[] s0 = Section(z0, baseWidth);
            Vector3[] s1 = Section(z1, midWidth);
            var tip = new Vector3(0f, 0f, zTip);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                int i = vertices.Count;
                // Unity front faces are clockwise seen from outside, i.e. cross(b − a, c − a) points outward.
                if (Vector3.Dot(n, outward) >= 0f)
                {
                    vertices.Add(a);
                    vertices.Add(b);
                    vertices.Add(c);
                }
                else
                {
                    vertices.Add(a);
                    vertices.Add(c);
                    vertices.Add(b);
                }

                triangles.Add(i);
                triangles.Add(i + 1);
                triangles.Add(i + 2);
            }

            for (int k = 0; k < 4; k++)
            {
                int k2 = (k + 1) % 4;
                Vector3 mid = 0.25f * (s0[k] + s0[k2] + s1[k] + s1[k2]);
                Vector3 outward = new Vector3(mid.x, mid.y, 0f);
                Tri(s0[k], s0[k2], s1[k2], outward);
                Tri(s0[k], s1[k2], s1[k], outward);
                Vector3 midTip = (s1[k] + s1[k2] + tip) / 3f;
                Tri(s1[k], s1[k2], tip, new Vector3(midTip.x, midTip.y, 0.02f));
            }

            Tri(s0[0], s0[1], s0[2], Vector3.back);
            Tri(s0[0], s0[2], s0[3], Vector3.back);

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(BladeMeshPath);
            bool create = mesh == null;
            if (create)
            {
                mesh = new Mesh();
            }

            mesh.Clear();
            mesh.name = "MSH_ArSwordBlade";
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (create)
            {
                AssetDatabase.CreateAsset(mesh, BladeMeshPath);
            }

            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, Quaternion? rot = null)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            Renderer r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        private static GameObject BuildSwordPrefab()
        {
            Material steel = Mat("M_ArSwordBlade", new Color(0.80f, 0.82f, 0.86f), 0.9f, 0.78f);
            Material bronze = Mat("M_ArSwordGuard", new Color(0.62f, 0.42f, 0.20f), 0.85f, 0.55f);
            Material leather = Mat("M_ArSwordGrip", new Color(0.20f, 0.12f, 0.07f), 0.0f, 0.25f);
            Material gold = Mat("M_ArSwordPommel", new Color(0.86f, 0.68f, 0.34f), 0.9f, 0.6f);
            Mesh blade = BuildBladeMesh();

            // Pivot = grip centre (held in the palm). Local +Z = blade direction, ±Y = edges.
            var root = new GameObject("ArPrototypeSword");
            var model = new GameObject("Model").transform;
            model.SetParent(root.transform, false);
            Prim(PrimitiveType.Cylinder, "Grip", model, Vector3.zero, new Vector3(0.032f, 0.07f, 0.032f), leather, Quaternion.Euler(90f, 0f, 0f));
            Prim(PrimitiveType.Cube, "Guard", model, new Vector3(0f, 0f, 0.077f), new Vector3(0.024f, 0.17f, 0.03f), bronze);
            Prim(PrimitiveType.Sphere, "Pommel", model, new Vector3(0f, 0f, -0.082f), Vector3.one * 0.045f, gold);
            var bladeGo = new GameObject("Blade");
            bladeGo.transform.SetParent(model, false);
            bladeGo.AddComponent<MeshFilter>().sharedMesh = blade;
            var bladeRenderer = bladeGo.AddComponent<MeshRenderer>();
            bladeRenderer.sharedMaterial = steel;
            bladeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var bladeBase = new GameObject("BladeBase").transform;
            bladeBase.SetParent(root.transform, false);
            bladeBase.localPosition = new Vector3(0f, 0f, BladeStart);
            var bladeTip = new GameObject("BladeTip").transform;
            bladeTip.SetParent(root.transform, false);
            bladeTip.localPosition = new Vector3(0f, 0f, BladeStart + BladeLength);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, SwordPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ------------------------------------------------------------------ scene

        private static void DestroyNamed(string name)
        {
            foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
            {
                if (go != null && go.name == name && go.scene.IsValid())
                {
                    Object.DestroyImmediate(go);
                }
            }
        }

        private static void AddToScene(GameObject swordPrefab)
        {
            foreach (string n in new[] { "HandTracking", "ArPrototypeSword", "HandLandmarkDots", "HandTrackingStatus" })
            {
                DestroyNamed(n);
            }

            ArEncounterSceneController controller = Object.FindAnyObjectByType<ArEncounterSceneController>(FindObjectsInactive.Include);
            ARCameraManager cameraManager = Object.FindAnyObjectByType<ARCameraManager>(FindObjectsInactive.Include);
            if (controller == null || cameraManager == null)
            {
                throw new System.InvalidOperationException("AR encounter scene is missing its controller or ARCameraManager.");
            }

            Camera cam = cameraManager.GetComponent<Camera>();
            var palm = AssetDatabase.LoadAssetAtPath<ModelAsset>(PalmModelPath);
            var landmarks = AssetDatabase.LoadAssetAtPath<ModelAsset>(LandmarkModelPath);
            if (palm == null || landmarks == null)
            {
                throw new System.InvalidOperationException("Hand models not imported as ModelAsset: " + PalmModelPath + ", " + LandmarkModelPath);
            }

            var sword = (GameObject)PrefabUtility.InstantiatePrefab(swordPrefab);
            sword.name = "ArPrototypeSword";
            sword.SetActive(false);

            var trackingGo = new GameObject("HandTracking");
            var tracker = trackingGo.AddComponent<ArHandTracker>();
            tracker.Configure(palm, landmarks, cameraManager, cam, controller);
            var handSword = trackingGo.AddComponent<ArHandSword>();
            handSword.Configure(tracker, controller, sword.transform, sword.transform.Find("BladeBase"), sword.transform.Find("BladeTip"));

            // HUD readout: landmark dots over the camera image (behind the HUD) + one status line under the AR status.
            GameObject canvasGo = GameObject.Find("AR_HUD_Canvas");
            Transform safe = canvasGo != null ? canvasGo.transform.Find("SafeArea") : null;
            if (canvasGo == null || safe == null)
            {
                throw new System.InvalidOperationException("AR_HUD_Canvas/SafeArea missing.");
            }

            var dotSprite = AssetDatabase.LoadAssetAtPath<Sprite>(DotSpritePath);
            var dotsRoot = new GameObject("HandLandmarkDots", typeof(RectTransform)).GetComponent<RectTransform>();
            dotsRoot.SetParent(canvasGo.transform, false);
            dotsRoot.anchorMin = Vector2.zero;
            dotsRoot.anchorMax = Vector2.one;
            dotsRoot.offsetMin = Vector2.zero;
            dotsRoot.offsetMax = Vector2.zero;
            dotsRoot.SetSiblingIndex(0);
            var dots = new RectTransform[HandLandmark.Count];
            for (int i = 0; i < dots.Length; i++)
            {
                var dot = new GameObject("Landmark" + i.ToString("00"), typeof(RectTransform)).GetComponent<RectTransform>();
                dot.SetParent(dotsRoot, false);
                dot.anchorMin = Vector2.zero;
                dot.anchorMax = Vector2.zero;
                dot.pivot = new Vector2(0.5f, 0.5f);
                bool tip = i == 4 || i == 8 || i == 12 || i == 16 || i == 20;
                dot.sizeDelta = Vector2.one * (i == 0 ? 26f : tip ? 20f : 16f);
                var img = dot.gameObject.AddComponent<Image>();
                img.sprite = dotSprite;
                img.color = i == 0 ? new Color(1f, 0.55f, 0.15f, 0.95f) : new Color(0.35f, 0.9f, 1f, 0.9f);
                img.raycastTarget = false;
                dot.gameObject.SetActive(false);
                dots[i] = dot;
            }

            var status = new GameObject("HandTrackingStatus", typeof(RectTransform)).GetComponent<RectTransform>();
            status.SetParent(safe, false);
            status.anchorMin = status.anchorMax = status.pivot = new Vector2(0.5f, 1f);
            status.anchoredPosition = new Vector2(0f, -432f);
            status.sizeDelta = new Vector2(640f, 54f);
            var panel = status.gameObject.AddComponent<Image>();
            panel.color = Stone;
            panel.raycastTarget = false;
            var labelRt = new GameObject("HandTrackingStatusText", typeof(RectTransform)).GetComponent<RectTransform>();
            labelRt.SetParent(status, false);
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = labelRt.offsetMax = Vector2.zero;
            var label = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = ArHandDebugOverlay.NoHandText;
            label.fontSize = 26;
            label.color = Parchment;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            status.gameObject.SetActive(false);

            var overlay = trackingGo.AddComponent<ArHandDebugOverlay>();
            overlay.Configure(tracker, dotsRoot, dots, label, status.gameObject);
        }
    }
}
