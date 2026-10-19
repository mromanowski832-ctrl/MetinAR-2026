using System.Collections.Generic;
using System.IO;
using MetinAR.Location;
using MetinAR.UI;
using MetinAR.WorldMap;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Rendering;

namespace MetinAR.Editor
{
    /// <summary>
    /// Builds Assets/MetinAR/Scenes/WorldMapPrototype.unity with HUD + placeholders.
    /// Usage: -executeMethod MetinAR.Editor.WorldMapPrototypeSceneBuilder.BuildScene
    /// </summary>
    public static class WorldMapPrototypeSceneBuilder
    {
        private const string ScenePath = "Assets/MetinAR/Scenes/WorldMapPrototype.unity";
        private const string ArtFolder = "Assets/MetinAR/Art/Prototype";
        private const string ScenesFolder = "Assets/MetinAR/Scenes";

        private static Sprite _circleSprite;

        public static void BuildScene()
        {
            EnsureFolders();
            Dictionary<string, Material> mats = CreateMaterials();
            _circleSprite = GetOrCreateCircleSprite();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Lighting
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.65f, 0.75f);
            RenderSettings.ambientEquatorColor = new Color(0.35f, 0.4f, 0.35f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.18f, 0.15f);

            // Ground
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(40f, 1f, 40f);
            ApplyMat(ground, mats["Ground"]);

            // Systems
            var systems = new GameObject("Systems");
            var origin = systems.AddComponent<WorldMapOrigin>();
            // No origin is serialized: PrototypeWorldPlacement captures it from the first runtime fix.

            var locationGo = new GameObject("PlayerLocationService");
            locationGo.transform.SetParent(systems.transform, false);
            var locationService = locationGo.AddComponent<PlayerLocationService>();
            SetPrivateField(locationService, "_providerKind", PlayerLocationService.ProviderKind.Auto);

            var controllerGo = new GameObject("WorldMapPrototypeController");
            controllerGo.transform.SetParent(systems.transform, false);
            var controller = controllerGo.AddComponent<WorldMapPrototypeController>();
            controllerGo.AddComponent<WorldMapPrototypeBootstrap>();

            // Player
            GameObject player = BuildPlayer(mats);
            var playerAvatar = player.AddComponent<PlayerMapAvatar>();
            playerAvatar.Configure(locationService, origin, player.transform.Find("Model"));
            var mockMove = player.AddComponent<EditorMockMovement>();
            mockMove.Configure(locationService, playerAvatar);

            // Camera
            var camGo = new GameObject("MapCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.45f, 0.7f, 0.95f);
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 200f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<UnityEngine.EventSystems.PhysicsRaycaster>();
            var follow = camGo.AddComponent<MapCameraFollow>();
            follow.Configure(player.transform, playerAvatar);
            camGo.transform.position = new Vector3(0f, 15f, -13f);

            // Mob ~15 m north-east
            GameObject mob = BuildMob(mats);
            var mobEntity = mob.AddComponent<WorldMapEntity>();
            TextMeshPro mobLabel = mob.transform.Find("Label").GetComponent<TextMeshPro>();
            mobEntity.Configure(origin, "Trujący Pająk", mobLabel, player.transform, true);
            var prototypeMob = mob.AddComponent<PrototypeMob>();
            mob.AddComponent<MobClickTarget>().Configure(prototypeMob);

            // NPC ~12 m west
            GameObject npc = BuildNpc(mats);
            var npcEntity = npc.AddComponent<WorldMapEntity>();
            TextMeshPro npcLabel = npc.transform.Find("Label").GetComponent<TextMeshPro>();
            npcEntity.Configure(origin, "Wędrowny Handlarz", npcLabel, player.transform, false);
            npc.AddComponent<PrototypeNpc>().Configure("Wędrowny Handlarz");

            // Mob/NPC stay inactive until the first fix places them (no premature (0,0) placement).
            var placement = systems.AddComponent<PrototypeWorldPlacement>();
            placement.Configure(locationService, origin, mobEntity, npcEntity);
            mob.SetActive(false);
            npc.SetActive(false);

            // EventSystem
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            #if ENABLE_INPUT_SYSTEM
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif

            // Canvas HUD
            BuildHud(
                controller,
                prototypeMob,
                out MobInfoPanel mobInfo,
                out InventoryPanel inventory,
                out CharacterPanel character,
                out PlaceholderPanel skills,
                out PlaceholderPanel settings,
                out PlaceholderPanel hamburger,
                out WorldMapHud hud,
                out TextMeshProUGUI toastText,
                out GameObject toastRoot);

            prototypeMob.Configure(
                "Trujący Pająk",
                5,
                120,
                true,
                12f,
                player.transform,
                mobEntity,
                mobInfo,
                controller);

            controller.Configure(
                hud,
                mobInfo,
                inventory,
                character,
                skills,
                settings,
                hamburger,
                toastText,
                toastRoot);

            // Hide panels by default
            mobInfo.gameObject.SetActive(false);
            inventory.gameObject.SetActive(false);
            character.gameObject.SetActive(false);
            skills.gameObject.SetActive(false);
            settings.gameObject.SetActive(false);
            hamburger.gameObject.SetActive(false);
            toastRoot.SetActive(false);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[WorldMapPrototypeSceneBuilder] Saved " + ScenePath);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/MetinAR"))
            {
                AssetDatabase.CreateFolder("Assets", "MetinAR");
            }

            if (!AssetDatabase.IsValidFolder("Assets/MetinAR/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets/MetinAR", "Scenes");
            }

            if (!AssetDatabase.IsValidFolder("Assets/MetinAR/Art"))
            {
                AssetDatabase.CreateFolder("Assets/MetinAR", "Art");
            }

            if (!AssetDatabase.IsValidFolder(ArtFolder))
            {
                AssetDatabase.CreateFolder("Assets/MetinAR/Art", "Prototype");
            }
        }

        private static Dictionary<string, Material> CreateMaterials()
        {
            var result = new Dictionary<string, Material>();
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                lit = Shader.Find("HDRP/Lit");
            }

            if (lit == null)
            {
                lit = Shader.Find("Standard");
            }

            result["Ground"] = GetOrCreateMat("Ground", lit, new Color(0.28f, 0.55f, 0.28f));
            result["PlayerBody"] = GetOrCreateMat("PlayerBody", lit, new Color(0.2f, 0.45f, 0.85f));
            result["PlayerAccent"] = GetOrCreateMat("PlayerAccent", lit, new Color(0.95f, 0.8f, 0.2f));
            result["Mob"] = GetOrCreateMat("Mob", lit, new Color(0.65f, 0.15f, 0.55f));
            result["MobEye"] = GetOrCreateMat("MobEye", lit, new Color(0.95f, 0.2f, 0.15f));
            result["Npc"] = GetOrCreateMat("Npc", lit, new Color(0.2f, 0.7f, 0.85f));
            result["NpcHat"] = GetOrCreateMat("NpcHat", lit, new Color(0.15f, 0.35f, 0.7f));
            return result;
        }

        private static Material GetOrCreateMat(string name, Shader shader, Color color)
        {
            string path = ArtFolder + "/M_" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.color = color;
                if (existing.HasProperty("_BaseColor"))
                {
                    existing.SetColor("_BaseColor", color);
                }

                EditorUtility.SetDirty(existing);
                return existing;
            }

            var mat = new Material(shader);
            mat.name = "M_" + name;
            mat.color = color;
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static Sprite GetOrCreateCircleSprite()
        {
            string path = ArtFolder + "/UI_Circle.png";
            if (!File.Exists(path))
            {
                const int size = 128;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                float r = size * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x + 0.5f - r;
                        float dy = y + 0.5f - r;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(r - d);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                }

                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null && importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void ApplyMat(GameObject go, Material mat)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = mat;
            }
        }

        private static GameObject BuildPlayer(Dictionary<string, Material> mats)
        {
            var root = new GameObject("Player");
            root.transform.position = Vector3.zero;

            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(model.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);
            ApplyMat(body, mats["PlayerBody"]);
            Object.DestroyImmediate(body.GetComponent<Collider>());

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(model.transform, false);
            head.transform.localPosition = new Vector3(0f, 2.05f, 0f);
            head.transform.localScale = Vector3.one * 0.55f;
            ApplyMat(head, mats["PlayerAccent"]);
            Object.DestroyImmediate(head.GetComponent<Collider>());

            GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "FacingMarker";
            nose.transform.SetParent(model.transform, false);
            nose.transform.localPosition = new Vector3(0f, 1.4f, 0.4f);
            nose.transform.localScale = new Vector3(0.2f, 0.2f, 0.35f);
            ApplyMat(nose, mats["PlayerAccent"]);
            Object.DestroyImmediate(nose.GetComponent<Collider>());

            // Invisible click collider not needed for player
            var capsuleCol = root.AddComponent<CapsuleCollider>();
            capsuleCol.center = new Vector3(0f, 1f, 0f);
            capsuleCol.height = 2f;
            capsuleCol.radius = 0.4f;

            return root;
        }

        private static GameObject BuildMob(Dictionary<string, Material> mats)
        {
            var root = new GameObject("Mob_TrujacyPajak");

            GameObject abdomen = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            abdomen.name = "Abdomen";
            abdomen.transform.SetParent(root.transform, false);
            abdomen.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            abdomen.transform.localScale = new Vector3(1.2f, 0.8f, 1.4f);
            ApplyMat(abdomen, mats["Mob"]);

            GameObject cephalothorax = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cephalothorax.name = "Cephalothorax";
            cephalothorax.transform.SetParent(root.transform, false);
            cephalothorax.transform.localPosition = new Vector3(0f, 0.7f, 0.7f);
            cephalothorax.transform.localScale = new Vector3(0.7f, 0.55f, 0.7f);
            ApplyMat(cephalothorax, mats["Mob"]);
            Object.DestroyImmediate(cephalothorax.GetComponent<Collider>());

            for (int i = 0; i < 4; i++)
            {
                float z = -0.3f + i * 0.25f;
                GameObject legL = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                legL.name = "LegL" + i;
                legL.transform.SetParent(root.transform, false);
                legL.transform.localPosition = new Vector3(-0.7f, 0.35f, z);
                legL.transform.localRotation = Quaternion.Euler(0f, 0f, 65f);
                legL.transform.localScale = new Vector3(0.08f, 0.55f, 0.08f);
                ApplyMat(legL, mats["MobEye"]);
                Object.DestroyImmediate(legL.GetComponent<Collider>());

                GameObject legR = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                legR.name = "LegR" + i;
                legR.transform.SetParent(root.transform, false);
                legR.transform.localPosition = new Vector3(0.7f, 0.35f, z);
                legR.transform.localRotation = Quaternion.Euler(0f, 0f, -65f);
                legR.transform.localScale = new Vector3(0.08f, 0.55f, 0.08f);
                ApplyMat(legR, mats["MobEye"]);
                Object.DestroyImmediate(legR.GetComponent<Collider>());
            }

            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.text = "Trujący Pająk";
            tmp.fontSize = 5f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2(6f, 1.6f);

            // Keep abdomen collider for OnMouseDown
            return root;
        }

        private static GameObject BuildNpc(Dictionary<string, Material> mats)
        {
            var root = new GameObject("Npc_WedrownyHandlarz");

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            ApplyMat(body, mats["Npc"]);

            GameObject hat = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hat.name = "Hat";
            hat.transform.SetParent(root.transform, false);
            hat.transform.localPosition = new Vector3(0f, 2.0f, 0f);
            hat.transform.localScale = new Vector3(0.85f, 0.25f, 0.85f);
            ApplyMat(hat, mats["NpcHat"]);
            Object.DestroyImmediate(hat.GetComponent<Collider>());

            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 2.6f, 0f);
            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.text = "Wędrowny Handlarz";
            tmp.fontSize = 4.5f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.85f, 0.95f, 1f);
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2(6f, 1f);

            return root;
        }

        private static void BuildHud(
            WorldMapPrototypeController controller,
            PrototypeMob mob,
            out MobInfoPanel mobInfo,
            out InventoryPanel inventory,
            out CharacterPanel character,
            out PlaceholderPanel skills,
            out PlaceholderPanel settings,
            out PlaceholderPanel hamburger,
            out WorldMapHud hud,
            out TextMeshProUGUI toastText,
            out GameObject toastRoot)
        {
            var canvasGo = new GameObject("HUD_Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var safe = CreateUiObject("SafeArea", canvasGo.transform);
            StretchFull(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            // Top-left player card
            var playerCard = CreatePanel("PlayerCard", safe, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -16f), new Vector2(420f, 160f));
            var portrait = CreateImage("Portrait", playerCard, new Color(0.25f, 0.45f, 0.8f));
            SetRect(portrait.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(96f, 96f));
            var nick = CreateTmp("Nickname", playerCard, "Wędrowiec", 36, FontStyles.Bold, TextAlignmentOptions.Left);
            SetTopBand(nick.rectTransform, 130f, 12f, 16f, 48f);
            var level = CreateTmp("Level", playerCard, "Lv 12", 28, FontStyles.Normal, TextAlignmentOptions.Left);
            SetTopBand(level.rectTransform, 130f, 56f, 16f, 36f);
            var hpFill = CreateFillBar("HpBar", playerCard, new Color(0.85f, 0.2f, 0.2f), out Image hpImage);
            SetTopBand(hpFill, 130f, 100f, 16f, 22f);
            var hpText = CreateTmp("HpText", playerCard, "HP 420/500", 22, FontStyles.Normal, TextAlignmentOptions.Left);
            SetTopBand(hpText.rectTransform, 134f, 100f, 16f, 22f);
            var mpFill = CreateFillBar("MpBar", playerCard, new Color(0.2f, 0.4f, 0.9f), out Image mpImage);
            SetTopBand(mpFill, 130f, 128f, 16f, 22f);
            var mpText = CreateTmp("MpText", playerCard, "MP 180/200", 22, FontStyles.Normal, TextAlignmentOptions.Left);
            SetTopBand(mpText.rectTransform, 134f, 128f, 16f, 22f);

            // Top-right circular radar (ring + disc + dots)
            var radarRing = CreateImage("Radar", safe, new Color(0.85f, 0.75f, 0.35f, 1f));
            radarRing.sprite = _circleSprite;
            SetRect(radarRing.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -16f), new Vector2(200f, 200f));
            var radarDisc = CreateImage("RadarDisc", radarRing.rectTransform, new Color(0.05f, 0.16f, 0.1f, 0.95f));
            radarDisc.sprite = _circleSprite;
            SetRect(radarDisc.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(184f, 184f));
            var radarInner = CreateImage("RadarRange", radarDisc.rectTransform, new Color(0.3f, 0.8f, 0.4f, 0.18f));
            radarInner.sprite = _circleSprite;
            SetRect(radarInner.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100f, 100f));
            var radarLabel = CreateTmp("RadarLabel", radarDisc.rectTransform, "N", 22, FontStyles.Bold, TextAlignmentOptions.Center);
            SetTopBand(radarLabel.rectTransform, 0f, 6f, 0f, 26f);
            var radarDot = CreateImage("PlayerDot", radarDisc.rectTransform, new Color(0.3f, 0.95f, 0.4f));
            radarDot.sprite = _circleSprite;
            SetRect(radarDot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 18f));
            var mobDot = CreateImage("MobDot", radarDisc.rectTransform, new Color(0.95f, 0.25f, 0.75f));
            mobDot.sprite = _circleSprite;
            SetRect(mobDot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(12f, 55f), new Vector2(14f, 14f));
            var npcDot = CreateImage("NpcDot", radarDisc.rectTransform, new Color(0.3f, 0.75f, 1f));
            npcDot.sprite = _circleSprite;
            SetRect(npcDot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-15f, 22f), new Vector2(14f, 14f));

            // Quest tracker right-middle
            var quest = CreatePanel("QuestTracker", safe, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-16f, 80f), new Vector2(420f, 100f));
            var questText = CreateTmp("QuestText", quest, "Zabij 5 pająków: 2/5", 30, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            StretchFull(questText.rectTransform);
            questText.margin = new Vector4(12, 8, 12, 8);

            // Bottom-left circular coin (with off-center mark so rotation is visible) + 4 circular EXP orbs
            var coinPanel = CreateUiObject("CoinExp", safe);
            SetRect(coinPanel, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 190f), new Vector2(420f, 100f));
            var coinRim = CreateImage("CoinRim", coinPanel, new Color(0.65f, 0.45f, 0.08f));
            coinRim.sprite = _circleSprite;
            SetRect(coinRim.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(84f, 84f));
            var coin = CreateImage("Coin", coinRim.rectTransform, new Color(0.98f, 0.8f, 0.2f));
            coin.sprite = _circleSprite;
            SetRect(coin.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(70f, 70f));
            var coinMark = CreateImage("CoinMark", coin.rectTransform, new Color(0.65f, 0.45f, 0.08f));
            SetRect(coinMark.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 16f), new Vector2(12f, 30f));
            var expRoot = CreateUiObject("ExpSegments", coinPanel);
            SetRect(expRoot, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(110f, 0f), new Vector2(280f, 64f));
            var expImages = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                var ring = CreateImage("ExpRing" + i, expRoot, new Color(0.08f, 0.12f, 0.2f, 0.95f));
                ring.sprite = _circleSprite;
                SetRect(ring.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(i * 70f, 0f), new Vector2(60f, 60f));
                var orb = CreateImage("Exp" + i, ring.rectTransform, new Color(0.3f, 0.8f, 1f, i < 2 ? 1f : 0.25f));
                orb.sprite = _circleSprite;
                SetRect(orb.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(46f, 46f));
                var pct = CreateTmp("Pct" + i, ring.rectTransform, "25%", 16, FontStyles.Bold, TextAlignmentOptions.Center);
                StretchFull(pct.rectTransform);
                expImages[i] = orb;
            }

            // Bottom button bar
            var bar = CreatePanel("ButtonBar", safe, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(1000f, 140f));
            string[] labels = { "EQ", "Skille", "Ustawienia", "Postać", "Menu" };
            var buttons = new Button[5];
            for (int i = 0; i < 5; i++)
            {
                var btnGo = CreatePanel("Btn_" + labels[i], bar, new Vector2(i / 5f, 0f), new Vector2((i + 1) / 5f, 1f), Vector2.zero, Vector2.zero);
                btnGo.offsetMin = new Vector2(10f, 10f);
                btnGo.offsetMax = new Vector2(-10f, -10f);
                var img = btnGo.GetComponent<Image>();
                img.color = new Color(0.12f, 0.16f, 0.22f, 0.95f);
                var btn = btnGo.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                var t = CreateTmp("Label", btnGo, labels[i], 30, FontStyles.Bold, TextAlignmentOptions.Center);
                StretchFull(t.rectTransform);
                buttons[i] = btn;
            }

            hud = canvasGo.AddComponent<WorldMapHud>();
            hud.Configure(nick, level, hpText, mpText, hpImage, mpImage, questText, coin.rectTransform, expImages);

            // Panels
            mobInfo = BuildMobInfoPanel(safe, controller);
            inventory = BuildInventoryPanel(safe);
            character = BuildCharacterPanel(safe);
            skills = BuildPlaceholder(safe, "SkillsPanel", "Skille", "Lista umiejętności — prototyp (brak logiki).");
            settings = BuildPlaceholder(safe, "SettingsPanel", "Ustawienia", "Ustawienia — prototyp (brak logiki).");
            hamburger = BuildPlaceholder(safe, "HamburgerPanel", "Menu", "Menu główne — prototyp (brak logiki).");

            var buttonBar = bar.gameObject.AddComponent<HudButtonBar>();
            buttonBar.Configure(buttons[0], buttons[1], buttons[2], buttons[3], buttons[4], controller);

            // Toast
            toastRoot = CreatePanel("Toast", safe, new Vector2(0.5f, 0.65f), new Vector2(0.5f, 0.65f), Vector2.zero, new Vector2(700f, 100f)).gameObject;
            toastRoot.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.05f, 0.88f);
            toastText = CreateTmp("ToastText", toastRoot.transform, "", 34, FontStyles.Bold, TextAlignmentOptions.Center);
            StretchFull(toastText.rectTransform);
        }

        private static MobInfoPanel BuildMobInfoPanel(RectTransform parent, WorldMapPrototypeController controller)
        {
            var panel = CreatePanel("MobInfoPanel", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720f, 640f));
            panel.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.14f, 1f);
            var title = CreateTmp("Name", panel, "Mob", 40, FontStyles.Bold, TextAlignmentOptions.Center);
            SetTopBand(title.rectTransform, 24f, 24f, 24f, 60f);
            var level = CreateTmp("Level", panel, "Poziom", 30, FontStyles.Normal, TextAlignmentOptions.Center);
            SetTopBand(level.rectTransform, 24f, 90f, 24f, 40f);
            var hp = CreateTmp("Hp", panel, "HP", 30, FontStyles.Normal, TextAlignmentOptions.Center);
            SetTopBand(hp.rectTransform, 24f, 140f, 24f, 40f);
            var dist = CreateTmp("Distance", panel, "Dystans", 30, FontStyles.Normal, TextAlignmentOptions.Center);
            SetTopBand(dist.rectTransform, 24f, 190f, 24f, 40f);
            var aggr = CreateTmp("Aggressive", panel, "Agresywny", 28, FontStyles.Normal, TextAlignmentOptions.Center);
            SetTopBand(aggr.rectTransform, 24f, 240f, 24f, 40f);

            Button enter = CreateTextButton("EnterAr", panel, "Wejdź do AR", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(400f, 80f));
            Button close = CreateTextButton("Close", panel, "Zamknij", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(280f, 64f));

            var comp = panel.gameObject.AddComponent<MobInfoPanel>();
            comp.Configure(title, level, hp, dist, aggr, enter, close, controller);
            return comp;
        }

        private static InventoryPanel BuildInventoryPanel(RectTransform parent)
        {
            var panel = CreatePanel("InventoryPanel", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800f, 900f));
            panel.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.14f, 1f);
            var title = CreateTmp("Title", panel, "Ekwipunek", 40, FontStyles.Bold, TextAlignmentOptions.Center);
            SetTopBand(title.rectTransform, 16f, 16f, 16f, 56f);

            var grid = CreateUiObject("Slots", panel);
            SetRect(grid, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(640f, 640f));
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(140f, 140f);
            layout.spacing = new Vector2(12f, 12f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 4;
            layout.childAlignment = TextAnchor.MiddleCenter;
            for (int i = 0; i < 16; i++)
            {
                var slot = CreateImage("Slot" + i, grid, new Color(0.18f, 0.22f, 0.28f, 1f));
                slot.rectTransform.sizeDelta = new Vector2(140f, 140f);
            }

            Button close = CreateTextButton("Close", panel, "Zamknij", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(280f, 64f));
            var comp = panel.gameObject.AddComponent<InventoryPanel>();
            comp.Configure(grid, close, 16);
            return comp;
        }

        private static CharacterPanel BuildCharacterPanel(RectTransform parent)
        {
            var panel = CreatePanel("CharacterPanel", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 1000f));
            panel.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.14f, 1f);
            var title = CreateTmp("Title", panel, "Postać", 40, FontStyles.Bold, TextAlignmentOptions.Center);
            SetTopBand(title.rectTransform, 16f, 16f, 16f, 56f);

            var preview = CreateImage("Preview", panel, new Color(0.2f, 0.45f, 0.85f));
            SetRect(preview.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -220f), new Vector2(180f, 220f));

            var stats = CreateTmp("Stats", panel, "Siła 12\nŻywotność 14\nInteligencja 10\nZręczność 11", 30, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            SetRect(stats.rectTransform, new Vector2(0.25f, 0.3f), new Vector2(0.25f, 0.3f), new Vector2(0f, 0f), new Vector2(320f, 280f));

            var skills = CreateTmp("Skills", panel, "Umiejętności (prototyp)\n• Cios\n• Blok\n• Sprint", 30, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            SetRect(skills.rectTransform, new Vector2(0.75f, 0.3f), new Vector2(0.75f, 0.3f), new Vector2(0f, 0f), new Vector2(320f, 280f));

            Button close = CreateTextButton("Close", panel, "Zamknij", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(280f, 64f));
            var comp = panel.gameObject.AddComponent<CharacterPanel>();
            comp.Configure(stats, skills, close);
            return comp;
        }

        private static PlaceholderPanel BuildPlaceholder(RectTransform parent, string name, string title, string body)
        {
            var panel = CreatePanel(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700f, 500f));
            panel.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.14f, 1f);
            var titleT = CreateTmp("Title", panel, title, 40, FontStyles.Bold, TextAlignmentOptions.Center);
            SetTopBand(titleT.rectTransform, 16f, 24f, 16f, 60f);
            var bodyT = CreateTmp("Body", panel, body, 30, FontStyles.Normal, TextAlignmentOptions.Center);
            StretchFull(bodyT.rectTransform);
            bodyT.margin = new Vector4(32, 100, 32, 100);
            Button close = CreateTextButton("Close", panel, "Zamknij", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(280f, 64f));
            var comp = panel.gameObject.AddComponent<PlaceholderPanel>();
            comp.Configure(titleT, bodyT, close, title, body);
            return comp;
        }

        private static Button CreateTextButton(
            string name,
            RectTransform parent,
            string label,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPos,
            Vector2 size)
        {
            var go = CreatePanel(name, parent, anchorMin, anchorMax, anchoredPos, size);
            go.GetComponent<Image>().color = new Color(0.2f, 0.55f, 0.35f, 1f);
            var btn = go.gameObject.AddComponent<Button>();
            btn.targetGraphic = go.GetComponent<Image>();
            var t = CreateTmp("Label", go, label, 32, FontStyles.Bold, TextAlignmentOptions.Center);
            StretchFull(t.rectTransform);
            return btn;
        }

        private static RectTransform CreateFillBar(string name, RectTransform parent, Color fillColor, out Image fillImage)
        {
            var bg = CreateImage(name, parent, new Color(0.1f, 0.1f, 0.1f, 0.8f));
            var fill = CreateImage("Fill", bg.rectTransform, fillColor);
            StretchFull(fill.rectTransform);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0.8f;
            fillImage = fill;
            return bg.rectTransform;
        }

        private static RectTransform CreatePanel(
            string name,
            RectTransform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPos,
            Vector2 size)
        {
            var rt = CreateUiObject(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0.07f, 0.09f, 0.12f, 0.82f);
            SetRect(rt, anchorMin, anchorMax, anchoredPos, size);
            return rt;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var rt = CreateUiObject(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        private static TextMeshProUGUI CreateTmp(
            string name,
            Transform parent,
            string text,
            float fontSize,
            FontStyles style,
            TextAlignmentOptions align)
        {
            var rt = CreateUiObject(name, parent);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.alignment = align;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static RectTransform CreateUiObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// Layout helper. Point anchors (min==max): pivot inferred from corner, size = width/height.
        /// Stretch anchors: sizeDelta applied (often 0,0); caller may set offsetMin/Max after.
        /// </summary>
        private static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            if (Mathf.Approximately(anchorMin.x, anchorMax.x) && Mathf.Approximately(anchorMin.y, anchorMax.y))
            {
                rt.pivot = new Vector2(anchorMin.x, anchorMin.y);
            }
            else
            {
                rt.pivot = new Vector2(0.5f, 0.5f);
            }

            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
        }

        private static void SetTopBand(RectTransform rt, float left, float top, float right, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(left, -top - height);
            rt.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>
        /// Makes <paramref name="scenePath"/> build index 0 (enabled). Other existing entries are kept
        /// after it; SampleScene is kept in the list but disabled so it is never the startup scene.
        /// </summary>
        private static void AddSceneToBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>();
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                if (s.path == scenePath)
                {
                    continue;
                }

                bool enabled = s.enabled && !s.path.EndsWith("/SampleScene.unity");
                scenes.Add(new EditorBuildSettingsScene(s.path, enabled));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(target, value);
            }
        }
    }
}
