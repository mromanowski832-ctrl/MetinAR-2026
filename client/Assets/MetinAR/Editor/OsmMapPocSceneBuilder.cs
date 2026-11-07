using MetinAR.Location;
using MetinAR.Map.Runtime;
using MetinAR.WorldMap;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MetinAR.Editor
{
    /// <summary>
    /// Builds the isolated METINAR-008B scene <c>Assets/MetinAR/Scenes/WorldMapOsmPoc.unity</c>.
    /// Does not touch WorldMapPrototype, its builder, or EditorBuildSettings.
    /// Usage: Unity -batchmode -projectPath client -executeMethod MetinAR.Editor.OsmMapPocSceneBuilder.BuildScene
    /// </summary>
    public static class OsmMapPocSceneBuilder
    {
        public const string ScenePath = "Assets/MetinAR/Scenes/WorldMapOsmPoc.unity";
        public const string ArtFolder = "Assets/MetinAR/Art/MapPoc";
        public const string StylePath = ArtFolder + "/OsmMapStyle.asset";

        [MenuItem("MetinAR/Build OSM Map POC Scene (008B)")]
        public static void BuildScene()
        {
            EnsureFolder("Assets/MetinAR", "Scenes");
            EnsureFolder("Assets/MetinAR", "Art");
            EnsureFolder("Assets/MetinAR/Art", "MapPoc");

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Shader simple = Shader.Find("Universal Render Pipeline/Simple Lit") ?? lit;
            MapStyleProfile style = CreateStyle(lit, simple);
            Material playerMat = Mat("Player", lit, new Color(0.2f, 0.45f, 0.95f));
            Material mobMat = Mat("Mob", lit, new Color(0.75f, 0.15f, 0.55f));
            Material npcMat = Mat("Npc", lit, new Color(0.2f, 0.75f, 0.85f));
            Material baseMat = Mat("BaseGround", simple, new Color(0.62f, 0.66f, 0.58f));

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.shadows = LightShadows.None; // mobile budget; buildings read fine with lambert shading
            lightGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.47f, 0.45f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.24f, 0.22f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.78f, 0.84f, 0.9f);
            RenderSettings.fogStartDistance = 250f;
            RenderSettings.fogEndDistance = 700f;

            // Neutral base under the tiles so missing / offline tiles never show the void.
            GameObject baseGround = GameObject.CreatePrimitive(PrimitiveType.Quad);
            baseGround.name = "BaseGround";
            Object.DestroyImmediate(baseGround.GetComponent<Collider>());
            baseGround.transform.SetPositionAndRotation(new Vector3(0f, -0.05f, 0f), Quaternion.Euler(90f, 0f, 0f));
            baseGround.transform.localScale = new Vector3(3000f, 3000f, 1f);
            var baseRenderer = baseGround.GetComponent<MeshRenderer>();
            baseRenderer.sharedMaterial = baseMat;
            baseRenderer.shadowCastingMode = ShadowCastingMode.Off;

            // Location + origin (existing contracts, unchanged).
            var systems = new GameObject("Systems");
            var origin = systems.AddComponent<WorldMapOrigin>();
            var locationGo = new GameObject("PlayerLocationService");
            locationGo.transform.SetParent(systems.transform, false);
            var location = locationGo.AddComponent<PlayerLocationService>();
            var locSo = new SerializedObject(location);
            locSo.FindProperty("_providerKind").intValue = (int)PlayerLocationService.ProviderKind.Mock;
            locSo.FindProperty("_startOnEnable").boolValue = false; // OsmPocBootstrap chooses demo vs GPS
            locSo.FindProperty("_diagnosticLogIntervalSeconds").floatValue = 2f; // dev-build logcat only (contains lat/lon: never paste it anywhere)
            locSo.ApplyModifiedPropertiesWithoutUndo();

            // Player.
            // Player = raw GPS position (gameplay). Visual = smoothed marker (model + camera target only).
            var player = new GameObject("Player");
            var visual = new GameObject("Visual");
            visual.transform.SetParent(player.transform, false);
            GameObject model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            model.name = "Model";
            model.transform.SetParent(visual.transform, false);
            model.transform.localPosition = new Vector3(0f, 1f, 0f);
            model.transform.localScale = new Vector3(1.2f, 1f, 1.2f);
            Object.DestroyImmediate(model.GetComponent<Collider>());
            model.GetComponent<MeshRenderer>().sharedMaterial = playerMat;
            GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Heading";
            nose.transform.SetParent(model.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0.3f, 0.6f);
            nose.transform.localScale = new Vector3(0.3f, 0.3f, 0.5f);
            Object.DestroyImmediate(nose.GetComponent<Collider>());
            nose.GetComponent<MeshRenderer>().sharedMaterial = playerMat;
            var avatar = player.AddComponent<PlayerMapAvatar>();
            avatar.Configure(location, origin, model.transform);
            var mockMove = player.AddComponent<EditorMockMovement>();
            mockMove.Configure(location, avatar);
            var smoothing = player.AddComponent<OsmPocAvatarSmoothing>();
            smoothing.Configure(location, origin, player.transform, visual.transform);

            // Mob + NPC placed through the unchanged PrototypeWorldPlacement offsets.
            GameObject mob = Marker("Mob", PrimitiveType.Sphere, mobMat, new Vector3(2.2f, 2.2f, 2.2f), "Mob (POC)", out TextMeshPro mobLabel);
            var mobEntity = mob.AddComponent<WorldMapEntity>();
            mobEntity.Configure(origin, "Mob (POC)", mobLabel, player.transform, true);
            var prototypeMob = mob.AddComponent<PrototypeMob>();
            prototypeMob.Configure("Mob (POC)", 1, 100, false, 0f, player.transform, mobEntity, null, null);
            mob.SetActive(false);

            GameObject npc = Marker("Npc", PrimitiveType.Cylinder, npcMat, new Vector3(1.2f, 1.2f, 1.2f), "NPC (POC)", out TextMeshPro npcLabel);
            var npcEntity = npc.AddComponent<WorldMapEntity>();
            npcEntity.Configure(origin, "NPC (POC)", npcLabel, player.transform, false);
            npc.AddComponent<PrototypeNpc>().Configure("NPC (POC)");
            npc.SetActive(false);

            var placement = systems.AddComponent<PrototypeWorldPlacement>();
            placement.Configure(location, origin, mobEntity, npcEntity);

            // OSM map.
            var mapGo = new GameObject("OsmMap");
            var map = mapGo.AddComponent<OsmMapView>();
            map.Configure(origin, player.transform, style);

            // Camera.
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 900f;
            cam.fieldOfView = 50f;
            camGo.AddComponent<AudioListener>();
            var rig = camGo.AddComponent<MapCameraRig>();
            rig.Configure(visual.transform, avatar); // follows the smoothed marker
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 27f, -22f), Quaternion.Euler(50f, 0f, 0f));

            // UI.
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

            var canvasGo = new GameObject("Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var safe = new GameObject("SafeArea", typeof(RectTransform));
            safe.transform.SetParent(canvasGo.transform, false);
            Stretch((RectTransform)safe.transform);
            safe.AddComponent<MetinAR.UI.SafeAreaFitter>();
            Transform ui = safe.transform;

            // Attribution: bottom-right, always visible.
            GameObject attrBg = Panel("Attribution", ui, new Color(1f, 1f, 1f, 0.75f));
            var attrRt = (RectTransform)attrBg.transform;
            attrRt.anchorMin = attrRt.anchorMax = attrRt.pivot = new Vector2(1f, 0f);
            attrRt.anchoredPosition = new Vector2(-8f, 8f);
            attrRt.sizeDelta = new Vector2(760f, 44f);
            TextMeshProUGUI attrLabel = Text("Label", attrBg.transform, "OpenFreeMap  © OpenMapTiles  Data from OpenStreetMap", 22f, TextAlignmentOptions.MidlineRight, new Color(0.1f, 0.1f, 0.1f));
            var attrLabelRt = attrLabel.rectTransform;
            Stretch(attrLabelRt);
            attrLabelRt.offsetMin = new Vector2(8f, 0f);
            attrLabelRt.offsetMax = new Vector2(-60f, 0f);
            Button info = ButtonWithLabel("Info", attrBg.transform, "(i)", new Vector2(52f, 40f));
            var infoRt = (RectTransform)info.transform;
            infoRt.anchorMin = infoRt.anchorMax = infoRt.pivot = new Vector2(1f, 0.5f);
            infoRt.anchoredPosition = new Vector2(-4f, 0f);

            GameObject attrPanel = Panel("AttributionPanel", ui, new Color(0.08f, 0.09f, 0.12f, 0.94f));
            var apRt = (RectTransform)attrPanel.transform;
            apRt.anchorMin = apRt.anchorMax = apRt.pivot = new Vector2(0.5f, 0.5f);
            apRt.sizeDelta = new Vector2(900f, 760f);
            TextMeshProUGUI apText = Text("Body", attrPanel.transform, string.Empty, 30f, TextAlignmentOptions.TopLeft, Color.white);
            Stretch(apText.rectTransform);
            apText.rectTransform.offsetMin = new Vector2(40f, 170f);
            apText.rectTransform.offsetMax = new Vector2(-40f, -40f);
            Button openCopyright = ButtonWithLabel("OpenCopyright", attrPanel.transform, "openstreetmap.org/copyright", new Vector2(520f, 90f));
            var ocRt = (RectTransform)openCopyright.transform;
            ocRt.anchorMin = ocRt.anchorMax = ocRt.pivot = new Vector2(0f, 0f);
            ocRt.anchoredPosition = new Vector2(40f, 40f);
            Button close = ButtonWithLabel("Close", attrPanel.transform, "Close", new Vector2(240f, 90f));
            var clRt = (RectTransform)close.transform;
            clRt.anchorMin = clRt.anchorMax = clRt.pivot = new Vector2(1f, 0f);
            clRt.anchoredPosition = new Vector2(-40f, 40f);
            var attribution = canvasGo.AddComponent<MapAttributionView>();
            attribution.Configure(map, attrLabel, info, attrPanel, apText, openCopyright, close);
            attrPanel.SetActive(false);

            // Diagnostics overlay (top-left).
            TextMeshProUGUI diag = Text("Diagnostics", ui, "OSM POC", 22f, TextAlignmentOptions.TopLeft, new Color(0.05f, 0.05f, 0.05f));
            var dRt = diag.rectTransform;
            dRt.anchorMin = dRt.anchorMax = dRt.pivot = new Vector2(0f, 1f);
            dRt.anchoredPosition = new Vector2(16f, -16f);
            dRt.sizeDelta = new Vector2(1040f, 420f);
            diag.raycastTarget = false;

            // Location choice (device only; Editor/batch auto-picks the demo landmark).
            GameObject choice = Panel("LocationChoice", ui, new Color(0.08f, 0.09f, 0.12f, 0.94f));
            var chRt = (RectTransform)choice.transform;
            chRt.anchorMin = chRt.anchorMax = chRt.pivot = new Vector2(0.5f, 0.5f);
            chRt.sizeDelta = new Vector2(900f, 640f);
            TextMeshProUGUI chText = Text("Body", choice.transform,
                "METINAR-008B OSM map POC\n\nUse my GPS: foreground location only; the map host sees the ~1 km area you load.\n\nDemo: public landmark (Rynek Główny, Kraków) for repeatable performance runs; no GPS used.",
                30f, TextAlignmentOptions.TopLeft, Color.white);
            Stretch(chText.rectTransform);
            chText.rectTransform.offsetMin = new Vector2(40f, 170f);
            chText.rectTransform.offsetMax = new Vector2(-40f, -40f);
            Button gps = ButtonWithLabel("UseGps", choice.transform, "Use my GPS", new Vector2(380f, 100f));
            var gRt = (RectTransform)gps.transform;
            gRt.anchorMin = gRt.anchorMax = gRt.pivot = new Vector2(0f, 0f);
            gRt.anchoredPosition = new Vector2(40f, 40f);
            Button demo = ButtonWithLabel("UseDemo", choice.transform, "Demo: Kraków", new Vector2(380f, 100f));
            var deRt = (RectTransform)demo.transform;
            deRt.anchorMin = deRt.anchorMax = deRt.pivot = new Vector2(1f, 0f);
            deRt.anchoredPosition = new Vector2(-40f, 40f);
            choice.SetActive(false);

            var bootstrap = systems.AddComponent<OsmPocBootstrap>();
            bootstrap.Configure(location, choice, gps, demo);
            var diagnostics = canvasGo.AddComponent<OsmPocDiagnostics>();
            diagnostics.Configure(map, bootstrap, mobEntity, npcEntity, diag);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[OsmMapPocSceneBuilder] Saved " + ScenePath);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }

        private static MapStyleProfile CreateStyle(Shader lit, Shader simple)
        {
            MapStyleProfile style = AssetDatabase.LoadAssetAtPath<MapStyleProfile>(StylePath);
            if (style == null)
            {
                style = ScriptableObject.CreateInstance<MapStyleProfile>();
                AssetDatabase.CreateAsset(style, StylePath);
            }

            style.Configure(
                Mat("Landcover", simple, new Color(0.66f, 0.78f, 0.55f)),
                Mat("Landuse", simple, new Color(0.80f, 0.78f, 0.72f)),
                Mat("Park", simple, new Color(0.52f, 0.74f, 0.45f)),
                Mat("Water", simple, new Color(0.45f, 0.66f, 0.86f)),
                Mat("RoadMinor", simple, new Color(0.97f, 0.97f, 0.95f)),
                Mat("RoadMajor", simple, new Color(0.99f, 0.86f, 0.55f)),
                Mat("Path", simple, new Color(0.86f, 0.74f, 0.62f)),
                Mat("Rail", simple, new Color(0.55f, 0.55f, 0.58f)),
                Mat("Building", lit, new Color(0.88f, 0.84f, 0.78f)));
            EditorUtility.SetDirty(style);
            return style;
        }

        private static Material Mat(string name, Shader shader, Color color)
        {
            string path = ArtFolder + "/M_Osm" + name + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = "M_Osm" + name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            mat.color = color;
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }

            if (mat.HasProperty("_Smoothness"))
            {
                mat.SetFloat("_Smoothness", 0.1f);
            }

            mat.enableInstancing = false;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static GameObject Marker(string name, PrimitiveType type, Material mat, Vector3 scale, string label, out TextMeshPro text)
        {
            var root = new GameObject(name);
            GameObject body = GameObject.CreatePrimitive(type);
            body.name = "Model";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, scale.y * 0.5f, 0f);
            body.transform.localScale = scale;
            body.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, scale.y + 1.6f, 0f);
            text = labelGo.AddComponent<TextMeshPro>();
            text.text = label;
            text.fontSize = 5f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.rectTransform.sizeDelta = new Vector2(10f, 3f);
            return root;
        }

        private static GameObject Panel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return go;
        }

        private static TextMeshProUGUI Text(string name, Transform parent, string text, float size, TextAlignmentOptions align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.textWrappingMode = TextWrappingModes.Normal;
            return t;
        }

        private static Button ButtonWithLabel(string name, Transform parent, string label, Vector2 size)
        {
            GameObject go = Panel(name, parent, new Color(0.18f, 0.42f, 0.85f, 1f));
            ((RectTransform)go.transform).sizeDelta = size;
            var button = go.AddComponent<Button>();
            TextMeshProUGUI t = Text("Label", go.transform, label, 28f, TextAlignmentOptions.Center, Color.white);
            Stretch(t.rectTransform);
            t.raycastTarget = false;
            return button;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + child))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
