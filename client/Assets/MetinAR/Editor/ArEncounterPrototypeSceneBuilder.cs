using System.Collections.Generic;
using System.IO;
using MetinAR.ArEncounter;
using MetinAR.UI;
using MetinAR.WorldMap;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace MetinAR.Editor
{
    /// <summary>
    /// METINAR-006: builds the AR encounter prototype scene, the procedural spider / plane prefabs and
    /// procedural HUD sprites (original placeholder art only), registers the scene as build index 1
    /// (world map stays index 0) and adds <see cref="ArEncounterLauncher"/> to the world-map scene.
    /// Usage: Unity -batchmode -nographics -quit -projectPath client -executeMethod MetinAR.Editor.ArEncounterPrototypeSceneBuilder.BuildAll
    /// </summary>
    public static class ArEncounterPrototypeSceneBuilder
    {
        private const string ArtFolder = "Assets/MetinAR/Art/ArEncounter";
        private const string PrefabFolder = "Assets/MetinAR/Prefabs";
        private const string SpiderPrefabPath = PrefabFolder + "/ArPrototypeSpider.prefab";
        private const string PlanePrefabPath = PrefabFolder + "/ArGroundPlane.prefab";

        // Dark-fantasy palette (original): stone, leather, bronze/gold metal.
        private static readonly Color Stone = new Color(0.11f, 0.10f, 0.095f, 0.90f);
        private static readonly Color StoneLight = new Color(0.20f, 0.18f, 0.16f, 0.95f);
        private static readonly Color Leather = new Color(0.25f, 0.15f, 0.085f, 0.94f);
        private static readonly Color Gold = new Color(0.86f, 0.68f, 0.34f, 1f);
        private static readonly Color Bronze = new Color(0.52f, 0.35f, 0.18f, 1f);
        private static readonly Color Parchment = new Color(0.95f, 0.88f, 0.70f, 1f);

        private static Sprite _circle;
        private static Sprite _ring;
        private static Sprite _panel;
        private static Sprite _bar;

        public static void BuildAll()
        {
            BuildArtAndPrefabs(out GameObject spiderPrefab, out GameObject planePrefab, out Material reticleMat);
            BuildArScene(spiderPrefab, planePrefab, reticleMat);
            ArHandSwordPrototypeBuilder.Apply(); // METINAR-007: hand tracking + hand-attached sword
            RegisterBuildScenes();
            AddLauncherToWorldMap();
            AssetDatabase.SaveAssets();
            Debug.Log("[ArEncounterPrototypeSceneBuilder] done");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }

        // ------------------------------------------------------------------ assets

        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + child))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static void BuildArtAndPrefabs(out GameObject spiderPrefab, out GameObject planePrefab, out Material reticleMat)
        {
            EnsureFolder("Assets/MetinAR", "Art");
            EnsureFolder("Assets/MetinAR/Art", "ArEncounter");
            EnsureFolder("Assets/MetinAR", "Prefabs");

            _circle = GetOrCreateSprite("UI_ArDisc", 128, (x, y, n) =>
            {
                float d = Dist(x, y, n);
                float a = Mathf.Clamp01(n * 0.5f - d);
                return new Color(1f, 1f, 1f, a);
            }, Vector4.zero);
            _ring = GetOrCreateSprite("UI_ArRing", 256, (x, y, n) =>
            {
                float r = n * 0.5f;
                float d = Dist(x, y, n);
                float outer = Mathf.Clamp01(r - d);
                float inner = Mathf.Clamp01(d - (r - 18f));
                float a = outer * inner;
                // Metallic shading: brighter top-left, darker bottom-right, bevel in the middle of the band.
                float band = Mathf.Clamp01((d - (r - 18f)) / 18f);
                float bevel = 0.75f + 0.25f * Mathf.Sin(band * Mathf.PI);
                float light = 0.8f + 0.2f * ((x - y) / (float)n);
                float v = Mathf.Clamp01(bevel * light);
                return new Color(v, v, v, a);
            }, Vector4.zero);
            _panel = GetOrCreateSprite("UI_ArPanel", 64, (x, y, n) =>
            {
                // Rounded rect with a bright 3 px rim and subtle vertical gradient (tinted per use).
                float cx = Mathf.Max(Mathf.Abs(x + 0.5f - n * 0.5f) - (n * 0.5f - 12f), 0f);
                float cy = Mathf.Max(Mathf.Abs(y + 0.5f - n * 0.5f) - (n * 0.5f - 12f), 0f);
                float d = Mathf.Sqrt(cx * cx + cy * cy);
                float a = Mathf.Clamp01(12f - d);
                bool rim = d > 8.5f;
                float grad = 0.82f + 0.18f * (y / (float)n);
                float noise = 0.96f + 0.04f * Mathf.PerlinNoise(x * 0.31f, y * 0.29f);
                float v = rim ? 1f : 0.62f * grad * noise;
                return new Color(v, v, v, a);
            }, new Vector4(16, 16, 16, 16));
            _bar = GetOrCreateSprite("UI_ArBar", 32, (x, y, n) =>
            {
                float v = 0.75f + 0.25f * Mathf.Sin((y + 0.5f) / n * Mathf.PI);
                return new Color(v, v, v, 1f);
            }, new Vector4(4, 4, 4, 4));

            Texture2D shadowTex = GetOrCreateTexture("T_ArBlobShadow", 128, (x, y, n) =>
            {
                float d = Dist(x, y, n) / (n * 0.5f);
                float a = Mathf.Clamp01(1f - d);
                return new Color(0f, 0f, 0f, a * a * 0.75f);
            });
            Texture2D reticleTex = GetOrCreateTexture("T_ArReticle", 128, (x, y, n) =>
            {
                float d = Dist(x, y, n) / (n * 0.5f);
                float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.85f) * 14f);
                float dot = Mathf.Clamp01((0.12f - d) * 30f);
                return new Color(1f, 1f, 1f, Mathf.Max(ring, dot));
            });
            Texture2D planeTex = GetOrCreateTexture("T_ArPlaneGrid", 64, (x, y, n) =>
            {
                bool line = (x % 16) < 1 || (y % 16) < 1;
                return new Color(1f, 1f, 1f, line ? 0.55f : 0.12f);
            });

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Material body = GetOrCreateMat("M_ArSpiderBody", lit, new Color(0.16f, 0.09f, 0.16f), false, null, 0.35f);
            Material accent = GetOrCreateMat("M_ArSpiderAccent", lit, new Color(0.35f, 0.85f, 0.18f), false, null, 0.5f);
            Material leg = GetOrCreateMat("M_ArSpiderLeg", lit, new Color(0.10f, 0.07f, 0.08f), false, null, 0.3f);
            Material eye = GetOrCreateMat("M_ArSpiderEye", unlit, new Color(1f, 0.15f, 0.08f), false, null, 0f);
            Material fang = GetOrCreateMat("M_ArSpiderFang", lit, new Color(0.85f, 0.82f, 0.7f), false, null, 0.6f);
            Material shadow = GetOrCreateMat("M_ArBlobShadow", unlit, Color.white, true, shadowTex, 0f);
            Material hpBg = GetOrCreateMat("M_ArHpBack", unlit, new Color(0.08f, 0.05f, 0.04f, 0.85f), true, null, 0f);
            Material hpFill = GetOrCreateMat("M_ArHpFill", unlit, new Color(0.85f, 0.12f, 0.1f), false, null, 0f);
            Material hpRim = GetOrCreateMat("M_ArHpRim", unlit, Gold, false, null, 0f);
            reticleMat = GetOrCreateMat("M_ArReticle", unlit, new Color(Gold.r, Gold.g, Gold.b, 0.95f), true, reticleTex, 0f);
            Material planeMat = GetOrCreateMat("M_ArPlane", unlit, new Color(0.95f, 0.8f, 0.45f, 0.35f), true, planeTex, 0f);
            Material planeLine = GetOrCreateMat("M_ArPlaneLine", unlit, new Color(Gold.r, Gold.g, Gold.b, 0.8f), true, null, 0f);

            spiderPrefab = BuildSpiderPrefab(body, accent, leg, eye, fang, shadow, hpBg, hpFill, hpRim);
            planePrefab = BuildPlanePrefab(planeMat, planeLine);
        }

        private static float Dist(int x, int y, int n)
        {
            float dx = x + 0.5f - n * 0.5f;
            float dy = y + 0.5f - n * 0.5f;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private delegate Color PixelFn(int x, int y, int size);

        private static void WritePng(string path, int size, PixelFn fn)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    tex.SetPixel(x, y, fn(x, y, size));
                }
            }

            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        private static Sprite GetOrCreateSprite(string name, int size, PixelFn fn, Vector4 border)
        {
            string path = ArtFolder + "/" + name + ".png";
            if (!File.Exists(path))
            {
                WritePng(path, size, fn);
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null && (importer.textureType != TextureImporterType.Sprite || importer.spriteBorder != border))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.spriteBorder = border;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Texture2D GetOrCreateTexture(string name, int size, PixelFn fn)
        {
            string path = ArtFolder + "/" + name + ".png";
            if (!File.Exists(path))
            {
                WritePng(path, size, fn);
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null && !importer.alphaIsTransparency)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material GetOrCreateMat(string name, Shader shader, Color color, bool transparent, Texture2D tex, float smoothness)
        {
            string path = ArtFolder + "/" + name + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.shader = shader;
            mat.SetColor("_BaseColor", color);
            mat.color = color;
            if (tex != null)
            {
                mat.SetTexture("_BaseMap", tex);
                mat.mainTexture = tex;
            }

            if (mat.HasProperty("_Smoothness"))
            {
                mat.SetFloat("_Smoothness", smoothness);
            }

            if (transparent)
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_Cull", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)RenderQueue.Transparent;
            }

            EditorUtility.SetDirty(mat);
            return mat;
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
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>Cylinder segment between two local points.</summary>
        private static void Segment(string name, Transform parent, Vector3 a, Vector3 b, float thickness, Material mat)
        {
            Vector3 dir = b - a;
            Prim(PrimitiveType.Capsule, name, parent, (a + b) * 0.5f,
                new Vector3(thickness, dir.magnitude * 0.5f + thickness * 0.5f, thickness), mat,
                Quaternion.FromToRotation(Vector3.up, dir.normalized));
        }

        private static GameObject BuildSpiderPrefab(Material body, Material accent, Material leg, Material eye, Material fang, Material shadow, Material hpBg, Material hpFill, Material hpRim)
        {
            // Procedural placeholder spider (~1.3 m leg span), pivot on the ground, facing +Z.
            var root = new GameObject("ArPrototypeSpider");
            var model = new GameObject("Model").transform;
            model.SetParent(root.transform, false);

            Prim(PrimitiveType.Sphere, "Abdomen", model, new Vector3(0f, 0.36f, -0.26f), new Vector3(0.52f, 0.42f, 0.64f), body);
            Prim(PrimitiveType.Sphere, "AbdomenMarkFront", model, new Vector3(0f, 0.55f, -0.16f), new Vector3(0.18f, 0.08f, 0.16f), accent);
            Prim(PrimitiveType.Sphere, "AbdomenMarkBack", model, new Vector3(0f, 0.53f, -0.38f), new Vector3(0.24f, 0.08f, 0.2f), accent);
            Prim(PrimitiveType.Sphere, "Cephalothorax", model, new Vector3(0f, 0.30f, 0.14f), new Vector3(0.34f, 0.26f, 0.36f), body);
            for (int i = 0; i < 2; i++)
            {
                float sx = i == 0 ? -1f : 1f;
                Prim(PrimitiveType.Sphere, "Eye" + i, model, new Vector3(0.06f * sx, 0.38f, 0.30f), Vector3.one * 0.06f, eye);
                Prim(PrimitiveType.Sphere, "EyeSmall" + i, model, new Vector3(0.11f * sx, 0.35f, 0.28f), Vector3.one * 0.035f, eye);
                Segment("Fang" + i, model, new Vector3(0.04f * sx, 0.24f, 0.30f), new Vector3(0.03f * sx, 0.12f, 0.36f), 0.035f, fang);
            }

            float[] hipZ = { 0.24f, 0.16f, 0.07f, -0.02f };
            float[] footZ = { 0.62f, 0.30f, -0.05f, -0.42f };
            for (int i = 0; i < 4; i++)
            {
                for (int s = 0; s < 2; s++)
                {
                    float sx = s == 0 ? -1f : 1f;
                    var hip = new Vector3(0.13f * sx, 0.30f, hipZ[i]);
                    var knee = new Vector3(0.40f * sx, 0.52f, Mathf.Lerp(hipZ[i], footZ[i], 0.55f));
                    var foot = new Vector3(0.62f * sx, 0.01f, footZ[i]);
                    string side = s == 0 ? "L" : "R";
                    Segment("Leg" + side + i + "_Upper", model, hip, knee, 0.05f, leg);
                    Segment("Leg" + side + i + "_Lower", model, knee, foot, 0.04f, leg);
                }
            }

            // Soft contact shadow so it reads as standing on the real ground.
            Prim(PrimitiveType.Quad, "ContactShadow", root.transform, new Vector3(0f, 0.004f, -0.02f), new Vector3(1.5f, 1.4f, 1f), shadow, Quaternion.Euler(90f, 0f, 0f));

            // World-space label: name, level, HP bar (turned toward the AR camera at runtime).
            var labelRoot = new GameObject("Label").transform;
            labelRoot.SetParent(root.transform, false);
            labelRoot.localPosition = new Vector3(0f, 0.95f, 0f);
            TextMeshPro name = Tmp3D("Name", labelRoot, new Vector3(0f, 0.20f, 0f), 1.5f, FontStyles.Bold, Parchment, new Vector2(2.2f, 0.25f));
            TextMeshPro level = Tmp3D("Level", labelRoot, new Vector3(0f, 0.07f, 0f), 1.0f, FontStyles.Bold, Gold, new Vector2(1.5f, 0.15f));
            Prim(PrimitiveType.Quad, "HpRim", labelRoot, new Vector3(0f, -0.05f, 0.002f), new Vector3(0.84f, 0.095f, 1f), hpRim);
            Prim(PrimitiveType.Quad, "HpBack", labelRoot, new Vector3(0f, -0.05f, 0.001f), new Vector3(0.8f, 0.07f, 1f), hpBg);
            var fillPivot = new GameObject("HpFillPivot").transform;
            fillPivot.SetParent(labelRoot, false);
            fillPivot.localPosition = new Vector3(-0.4f, -0.05f, 0f);
            Prim(PrimitiveType.Quad, "HpFill", fillPivot, new Vector3(0.4f, 0f, 0f), new Vector3(0.8f, 0.07f, 1f), hpFill);
            TextMeshPro hp = Tmp3D("HpText", labelRoot, new Vector3(0f, -0.05f, -0.002f), 0.55f, FontStyles.Bold, Color.white, new Vector2(0.8f, 0.07f));

            var mob = root.AddComponent<ArEncounterMob>();
            mob.ConfigureVisuals(labelRoot, name, level, hp, fillPivot);
            name.text = "Trujący Pająk";
            level.text = "Poz. 5";
            hp.text = "HP 120/120";

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, SpiderPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static TextMeshPro Tmp3D(string name, Transform parent, Vector3 pos, float size, FontStyles style, Color color, Vector2 rect)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.outlineWidth = 0.2f;
            tmp.outlineColor = new Color32(10, 6, 4, 255);
            tmp.rectTransform.sizeDelta = rect;
            return tmp;
        }

        private static GameObject BuildPlanePrefab(Material planeMat, Material lineMat)
        {
            var go = new GameObject("ArGroundPlane");
            go.AddComponent<ARPlane>();
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterial = planeMat;
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = lineMat;
            line.useWorldSpace = false;
            line.widthMultiplier = 0.01f;
            line.loop = true;
            go.AddComponent<ARPlaneMeshVisualizer>();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, PlanePrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ------------------------------------------------------------------ scene

        private static void BuildArScene(GameObject spiderPrefab, GameObject planePrefab, Material reticleMat)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.0f;
            light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.52f, 0.5f);

            // AR Session (same composition as AR Foundation's "GameObject/XR/AR Session").
            var sessionGo = new GameObject("AR Session");
            var session = sessionGo.AddComponent<ARSession>();
            sessionGo.AddComponent<ARInputManager>();

            // XR Origin (Mobile AR) mirroring AR Foundation's XROriginCreateUtil.
            var originGo = new GameObject("XR Origin");
            var origin = originGo.AddComponent<XROrigin>();
            var offsetGo = new GameObject("Camera Offset");
            offsetGo.transform.SetParent(originGo.transform, false);
            var camGo = new GameObject("Main Camera");
            camGo.transform.SetParent(offsetGo.transform, false);
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<ARCameraManager>();
            camGo.AddComponent<ARCameraBackground>();
            var tpd = camGo.AddComponent<TrackedPoseDriver>();
            cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20f;
            var positionAction = new InputAction("Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
            positionAction.AddBinding("<HandheldARInputDevice>/devicePosition");
            var rotationAction = new InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
            rotationAction.AddBinding("<HandheldARInputDevice>/deviceRotation");
            tpd.positionInput = new InputActionProperty(positionAction);
            tpd.rotationInput = new InputActionProperty(rotationAction);
            origin.CameraFloorOffsetObject = offsetGo;
            origin.Camera = cam;

            var planeManager = originGo.AddComponent<ARPlaneManager>();
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            planeManager.planePrefab = planePrefab;
            var raycastManager = originGo.AddComponent<ARRaycastManager>();
            var anchorManager = originGo.AddComponent<ARAnchorManager>();

            // Placement reticle (flat gold ring on the detected ground).
            GameObject reticle = GameObject.CreatePrimitive(PrimitiveType.Quad);
            reticle.name = "PlacementReticle";
            Object.DestroyImmediate(reticle.GetComponent<Collider>());
            reticle.GetComponent<Renderer>().sharedMaterial = reticleMat;
            var reticleRoot = new GameObject("PlacementReticleRoot");
            reticle.transform.SetParent(reticleRoot.transform, false);
            reticle.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            reticle.transform.localScale = new Vector3(0.45f, 0.45f, 1f);
            reticleRoot.SetActive(false);

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

            ArEncounterHud hud = BuildHud();

            var systems = new GameObject("Systems");
            var controller = systems.AddComponent<ArEncounterSceneController>();
            controller.Configure(session, origin, planeManager, raycastManager, anchorManager, cam, hud, spiderPrefab, reticleRoot.transform);

            EditorSceneManager.SaveScene(scene, ArEncounterScenes.ArEncounterScenePath);
            Debug.Log("[ArEncounterPrototypeSceneBuilder] Saved " + ArEncounterScenes.ArEncounterScenePath);
        }

        private static ArEncounterHud BuildHud()
        {
            var canvasGo = new GameObject("AR_HUD_Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            RectTransform safe = Ui("SafeArea", canvasGo.transform);
            Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            // --- Top-left: portrait (metal ring) + level badge + name + HP/MP bars (placeholder values as in the world-map HUD).
            RectTransform card = Ui("PlayerCard", safe);
            Place(card, new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(520f, 170f));
            Image cardBg = Img("CardPanel", card, Stone, _panel, Image.Type.Sliced);
            Place(cardBg.rectTransform, new Vector2(0f, 0.5f), new Vector2(80f, 0f), new Vector2(440f, 130f));
            Image portraitRing = Img("PortraitRing", card, Gold, _ring, Image.Type.Simple);
            Place(portraitRing.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(170f, 170f));
            Image portrait = Img("Portrait", portraitRing.rectTransform, new Color(0.22f, 0.18f, 0.24f), _circle, Image.Type.Simple);
            Place(portrait.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(136f, 136f));
            Tmp("PortraitGlyph", portrait.rectTransform, "W", 72, Parchment, TextAlignmentOptions.Center, true);
            Image badge = Img("LevelBadge", portraitRing.rectTransform, Bronze, _circle, Image.Type.Simple);
            Place(badge.rectTransform, new Vector2(1f, 0f), new Vector2(6f, -6f), new Vector2(62f, 62f));
            Tmp("LevelText", badge.rectTransform, "12", 30, Parchment, TextAlignmentOptions.Center, true);
            TextMeshProUGUI nick = Tmp("Nickname", card, "Wędrowiec", 34, Parchment, TextAlignmentOptions.Left, false);
            Place(nick.rectTransform, new Vector2(0f, 1f), new Vector2(190f, -22f), new Vector2(310f, 44f));
            Bar("HpBar", card, new Vector2(190f, -76f), new Color(0.72f, 0.1f, 0.1f), 0.84f, "HP 420/500");
            Bar("MpBar", card, new Vector2(190f, -114f), new Color(0.16f, 0.32f, 0.82f), 0.9f, "MP 180/200");

            // Exit back to the world map (explicit, under the player card).
            Button exit = TextButton("ExitAr", safe, "Wyjdź z AR", new Vector2(0f, 1f), new Vector2(24f, -214f), new Vector2(270f, 76f), Leather, 28);

            // --- Top-right: circular radar.
            Image radarRing = Img("Radar", safe, Gold, _ring, Image.Type.Simple);
            Place(radarRing.rectTransform, new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(230f, 230f));
            Image radarDisc = Img("RadarDisc", radarRing.rectTransform, new Color(0.06f, 0.08f, 0.06f, 0.88f), _circle, Image.Type.Simple);
            Place(radarDisc.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(196f, 196f));
            Image radarRange = Img("RadarRange", radarDisc.rectTransform, new Color(Gold.r, Gold.g, Gold.b, 0.35f), _ring, Image.Type.Simple);
            Place(radarRange.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(110f, 110f));
            TextMeshProUGUI north = Tmp("RadarN", radarDisc.rectTransform, "N", 22, Gold, TextAlignmentOptions.Center, true);
            Place(north.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(40f, 28f));
            Image playerDot = Img("PlayerDot", radarDisc.rectTransform, new Color(0.45f, 0.95f, 0.5f), _circle, Image.Type.Simple);
            Place(playerDot.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 18f));
            Image mobDot = Img("MobDot", radarDisc.rectTransform, new Color(0.95f, 0.2f, 0.15f), _circle, Image.Type.Simple);
            Place(mobDot.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 18f));
            mobDot.gameObject.SetActive(false);

            // --- Status banner (top centre, below the top row; centre of the screen stays clear).
            RectTransform statusRoot = Ui("Status", safe);
            Place(statusRoot, new Vector2(0.5f, 1f), new Vector2(0f, -300f), new Vector2(820f, 120f));
            Img("StatusPanel", statusRoot, Stone, _panel, Image.Type.Sliced).rectTransform.StretchFill();
            TextMeshProUGUI status = Tmp("StatusText", statusRoot, "Uruchamianie AR...", 30, Parchment, TextAlignmentOptions.Center, false);
            status.rectTransform.StretchFill();
            status.margin = new Vector4(24, 10, 24, 10);
            status.textWrappingMode = TextWrappingModes.Normal;

            // --- Lower-left: quest tracker (placeholder quest from the world-map prototype).
            RectTransform quest = Ui("QuestTracker", safe);
            Place(quest, new Vector2(0f, 0f), new Vector2(24f, 470f), new Vector2(430f, 130f));
            Img("QuestPanel", quest, Leather, _panel, Image.Type.Sliced).rectTransform.StretchFill();
            TextMeshProUGUI questTitle = Tmp("QuestTitle", quest, "Zadanie", 26, Gold, TextAlignmentOptions.TopLeft, false);
            Place(questTitle.rectTransform, new Vector2(0f, 1f), new Vector2(22f, -14f), new Vector2(380f, 34f));
            TextMeshProUGUI questText = Tmp("QuestText", quest, "Zabij 5 pająków: 2/5", 30, Parchment, TextAlignmentOptions.TopLeft, false);
            Place(questText.rectTransform, new Vector2(0f, 1f), new Vector2(22f, -54f), new Vector2(390f, 60f));

            // --- Right: compact circular action buttons (no assigned function yet).
            var actions = new List<Button>
            {
                CircleButton("Action_I", safe, "I", new Vector2(-36f, 290f), 190f, 64),
                CircleButton("Action_II", safe, "II", new Vector2(-250f, 270f), 120f, 40),
                CircleButton("Action_III", safe, "III", new Vector2(-60f, 500f), 120f, 40),
            };

            // --- Centre-bottom: prototype placement confirm.
            Button place = TextButton("PlaceMonster", safe, "Postaw potwora", new Vector2(0.5f, 0f), new Vector2(-60f, 300f), new Vector2(420f, 104f), Leather, 36);

            // --- Bottom: fantasy navigation bar (no assigned functions yet).
            RectTransform nav = Ui("NavBar", safe);
            Place(nav, new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(1048f, 156f));
            Img("NavPanel", nav, Leather, _panel, Image.Type.Sliced).rectTransform.StretchFill();
            Image navRim = Img("NavRim", nav, Gold, _bar, Image.Type.Sliced);
            Place(navRim.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(1000f, 6f));
            string[] navLabels = { "Ekwipunek", "Umiejętności", "Zadania", "Menu" };
            var navButtons = new List<Button>();
            for (int i = 0; i < navLabels.Length; i++)
            {
                RectTransform slot = Ui("Nav_" + i, nav);
                slot.anchorMin = new Vector2(i / (float)navLabels.Length, 0f);
                slot.anchorMax = new Vector2((i + 1) / (float)navLabels.Length, 1f);
                slot.offsetMin = new Vector2(12f, 14f);
                slot.offsetMax = new Vector2(-12f, -18f);
                Image slotBg = slot.gameObject.AddComponent<Image>();
                slotBg.sprite = _panel;
                slotBg.type = Image.Type.Sliced;
                slotBg.color = StoneLight;
                Image icon = Img("Icon", slot, Bronze, _ring, Image.Type.Simple);
                Place(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(64f, 64f));
                TextMeshProUGUI label = Tmp("Label", slot, navLabels[i], 24, Parchment, TextAlignmentOptions.Bottom, true);
                label.rectTransform.StretchFill();
                label.margin = new Vector4(4, 4, 4, 10);
                var btn = slot.gameObject.AddComponent<Button>();
                btn.targetGraphic = slotBg;
                navButtons.Add(btn);
            }

            // --- Toast.
            RectTransform toast = Ui("Toast", safe);
            Place(toast, new Vector2(0.5f, 0.5f), new Vector2(0f, 330f), new Vector2(760f, 100f));
            Img("ToastPanel", toast, new Color(0.05f, 0.04f, 0.03f, 0.9f), _panel, Image.Type.Sliced).rectTransform.StretchFill();
            TextMeshProUGUI toastText = Tmp("ToastText", toast, string.Empty, 30, Parchment, TextAlignmentOptions.Center, true);
            toastText.rectTransform.StretchFill();
            toast.gameObject.SetActive(false);

            // --- Failure panel (unsupported / session failure).
            RectTransform fail = Ui("FailurePanel", safe);
            Place(fail, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860f, 560f));
            Img("FailureBg", fail, new Color(0.09f, 0.075f, 0.07f, 0.97f), _panel, Image.Type.Sliced).rectTransform.StretchFill();
            TextMeshProUGUI failTitle = Tmp("Title", fail, "AR niedostępne", 44, Gold, TextAlignmentOptions.Center, false);
            Place(failTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(780f, 64f));
            TextMeshProUGUI failText = Tmp("Body", fail, string.Empty, 30, Parchment, TextAlignmentOptions.Center, false);
            Place(failText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(760f, 240f));
            failText.textWrappingMode = TextWrappingModes.Normal;
            Button failBack = TextButton("Back", fail, "Wróć do mapy", new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(420f, 96f), Leather, 34);
            fail.gameObject.SetActive(false);

            var hud = canvasGo.AddComponent<ArEncounterHud>();
            hud.Configure(status, statusRoot.gameObject, place, exit, fail.gameObject, failTitle, failText, failBack,
                actions.ToArray(), navButtons.ToArray(), toast.gameObject, toastText, mobDot.rectTransform);
            return hud;
        }

        private static void Bar(string name, RectTransform parent, Vector2 pos, Color fillColor, float fill, string label)
        {
            Image back = Img(name, parent, new Color(0.04f, 0.03f, 0.03f, 0.95f), _bar, Image.Type.Sliced);
            Place(back.rectTransform, new Vector2(0f, 1f), pos, new Vector2(300f, 30f));
            Image fillImg = Img("Fill", back.rectTransform, fillColor, _bar, Image.Type.Filled);
            fillImg.rectTransform.StretchFill();
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillAmount = fill;
            TextMeshProUGUI t = Tmp("Text", back.rectTransform, label, 20, Color.white, TextAlignmentOptions.Center, true);
            t.rectTransform.StretchFill();
        }

        private static Button CircleButton(string name, RectTransform parent, string label, Vector2 pos, float size, float fontSize)
        {
            Image ring = Img(name, parent, Gold, _ring, Image.Type.Simple);
            Place(ring.rectTransform, new Vector2(1f, 0f), pos, new Vector2(size, size));
            Image disc = Img("Disc", ring.rectTransform, new Color(0.16f, 0.12f, 0.1f, 0.92f), _circle, Image.Type.Simple);
            Place(disc.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.82f, size * 0.82f));
            Tmp("Label", disc.rectTransform, label, fontSize, Parchment, TextAlignmentOptions.Center, true).rectTransform.StretchFill();
            var btn = ring.gameObject.AddComponent<Button>();
            btn.targetGraphic = disc;
            ring.raycastTarget = true;
            return btn;
        }

        private static Button TextButton(string name, RectTransform parent, string label, Vector2 anchor, Vector2 pos, Vector2 size, Color color, float fontSize)
        {
            RectTransform rt = Ui(name, parent);
            Place(rt, anchor, pos, size);
            Image rim = Img("Rim", rt, Gold, _panel, Image.Type.Sliced);
            rim.rectTransform.StretchFill();
            Image bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0f);
            Image inner = Img("Inner", rt, color, _panel, Image.Type.Sliced);
            inner.rectTransform.StretchFill();
            inner.rectTransform.offsetMin = new Vector2(5f, 5f);
            inner.rectTransform.offsetMax = new Vector2(-5f, -5f);
            Tmp("Label", rt, label, fontSize, Parchment, TextAlignmentOptions.Center, true).rectTransform.StretchFill();
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = inner;
            var colors = btn.colors;
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
            btn.colors = colors;
            return btn;
        }

        private static RectTransform Ui(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image Img(string name, Transform parent, Color color, Sprite sprite, Image.Type type)
        {
            RectTransform rt = Ui(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = type;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI Tmp(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align, bool bold)
        {
            RectTransform rt = Ui(name, parent);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            return tmp;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>Point-anchored rect whose pivot equals the anchor (corner-relative placement).</summary>
        private static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        // ------------------------------------------------------------------ build settings / world map

        /// <summary>World map stays build index 0 (enabled); the AR encounter scene is index 1 (enabled); SampleScene stays disabled.</summary>
        public static void RegisterBuildScenes()
        {
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(ArEncounterScenes.WorldMapScenePath, true),
                new EditorBuildSettingsScene(ArEncounterScenes.ArEncounterScenePath, true),
            };
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                if (s.path == ArEncounterScenes.WorldMapScenePath || s.path == ArEncounterScenes.ArEncounterScenePath)
                {
                    continue;
                }

                scenes.Add(new EditorBuildSettingsScene(s.path, s.enabled && !s.path.EndsWith("/SampleScene.unity")));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>Adds the AR encounter launcher next to the world-map controller (idempotent; nothing else changes).</summary>
        public static void AddLauncherToWorldMap()
        {
            Scene scene = EditorSceneManager.OpenScene(ArEncounterScenes.WorldMapScenePath, OpenSceneMode.Single);
            WorldMapPrototypeController controller = Object.FindAnyObjectByType<WorldMapPrototypeController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogError("[ArEncounterPrototypeSceneBuilder] WorldMapPrototypeController not found");
                return;
            }

            ArEncounterLauncher launcher = controller.GetComponent<ArEncounterLauncher>();
            if (launcher == null)
            {
                launcher = controller.gameObject.AddComponent<ArEncounterLauncher>();
            }

            launcher.Configure(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }

    internal static class RectTransformFillExtensions
    {
        public static void StretchFill(this RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
