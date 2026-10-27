using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MetinAR.ArEncounter;
using MetinAR.Location;
using MetinAR.Presentation;
using MetinAR.UI;
using MetinAR.WorldMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MetinAR.Editor
{
    /// <summary>
    /// Real Play Mode smoke test for the world-map prototype (no Test Framework package in manifest).
    /// Usage: Unity -batchmode -projectPath client -executeMethod MetinAR.Editor.WorldMapPrototypeSmokeTest.Run
    /// Writes Builds/metinar-004a-smoke-results.xml (JUnit-style) and exits 0 on pass, 1 on failure.
    /// </summary>
    public static class WorldMapPrototypeSmokeTest
    {
        private const string ScenePath = "Assets/MetinAR/Scenes/WorldMapPrototype.unity";
        private const string SessionKey = "MetinAR004A.SmokeResults";
        private const string PreKey = "MetinAR004B.EditModeState";
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";

        public static void Run()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string repoRoot = Directory.GetParent(projectRoot)!.FullName;
            string resultsPath = Path.Combine(repoRoot, "Builds", "metinar-004a-smoke-results.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(resultsPath)!);
            SessionState.SetString(SessionKey, resultsPath);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SessionState.SetString(PreKey, CaptureEditModeState());
            EditorApplication.isPlaying = true;
        }

        /// <summary>
        /// Saved-scene state before Play Mode: provider kind, origin/placement not initialized,
        /// mob/NPC inactive. Format: key=value;...
        /// </summary>
        private static string CaptureEditModeState()
        {
            var sb = new StringBuilder();
            PlayerLocationService service = UnityEngine.Object.FindFirstObjectByType<PlayerLocationService>(FindObjectsInactive.Include);
            int kind = -1;
            if (service != null)
            {
                kind = new SerializedObject(service).FindProperty("_providerKind").intValue;
            }

            WorldMapOrigin origin = UnityEngine.Object.FindFirstObjectByType<WorldMapOrigin>(FindObjectsInactive.Include);
            PrototypeWorldPlacement placement = UnityEngine.Object.FindFirstObjectByType<PrototypeWorldPlacement>(FindObjectsInactive.Include);
            PrototypeMob mob = UnityEngine.Object.FindFirstObjectByType<PrototypeMob>(FindObjectsInactive.Include);
            PrototypeNpc npc = UnityEngine.Object.FindFirstObjectByType<PrototypeNpc>(FindObjectsInactive.Include);
            sb.Append("kind=").Append(kind);
            sb.Append(";originPresent=").Append(origin != null);
            sb.Append(";originHas=").Append(origin != null && origin.HasOrigin);
            sb.Append(";placementPresent=").Append(placement != null);
            sb.Append(";placementInit=").Append(placement != null && placement.IsInitialized);
            sb.Append(";mobActive=").Append(mob != null && mob.gameObject.activeSelf);
            sb.Append(";npcActive=").Append(npc != null && npc.gameObject.activeSelf);
            return sb.ToString();
        }

        [InitializeOnLoadMethod]
        private static void RegisterHook()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            string results = SessionState.GetString(SessionKey, string.Empty);
            if (state != PlayModeStateChange.EnteredPlayMode || string.IsNullOrEmpty(results))
            {
                return;
            }

            SessionState.EraseString(SessionKey);
            var go = new GameObject("SmokeTestRunner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            Runner runner = go.AddComponent<Runner>();
            runner.ResultsPath = results;
            runner.EditModeState = SessionState.GetString(PreKey, string.Empty);
            SessionState.EraseString(PreKey);
        }

        private sealed class Runner : MonoBehaviour
        {
            public string ResultsPath;
            public string EditModeState;

            private readonly List<(string name, string failure)> _results = new List<(string, string)>();
            private string _current;
            private string _currentFailure;

            private WorldMapPrototypeController _controller;
            private PlayerLocationService _location;
            private PlayerMapAvatar _avatar;
            private EditorMockMovement _movement;
            private PrototypeMob _mob;
            private WorldMapEntity _mobEntity;
            private Camera _camera;
            private HudButtonBar _bar;
            private ArEncounterLauncher _launcher;

            private void Check(bool condition, string message)
            {
                if (!condition && _currentFailure == null)
                {
                    _currentFailure = message;
                }
            }

            private void Begin(string name)
            {
                _current = name;
                _currentFailure = null;
            }

            private void End()
            {
                _results.Add((_current, _currentFailure));
                Debug.Log("[SmokeTest] " + (_currentFailure == null ? "PASS " : "FAIL ") + _current
                          + (_currentFailure == null ? string.Empty : " :: " + _currentFailure));
            }

            private static IEnumerator Frames(int n)
            {
                for (int i = 0; i < n; i++)
                {
                    yield return null;
                }
            }

            private IEnumerator Start()
            {
                yield return Frames(5);
                IEnumerator[] tests =
                {
                    A0_StartupMenuEntersWorldWithProductionNpc(),
                    A_SceneLoadsAndCoreObjectsExist(),
                    H_SceneStoresAutoProvider(),
                    I_EditorResolvesAutoToMock(),
                    J_SceneOriginAndPlacementFromFirstFix(),
                    B_MovementPathMovesPlayerAndUpdatesDistance(),
                    C_TurnPathRotatesAvatarAndCamera(),
                    D_MobClickOpensInfoPanel(),
                    E_EnterArButtonEmitsEncounterRequest(),
                    F_AggroTriggersOnceInsideRange(),
                    G_HudButtonsOpenAndClosePanels(),
                    P_MenuDragAndMerchantConversation(),
                    M_AppPauseSuspendsAndResumeRestoresWithoutOriginReset(),
                    N_ManualStopIsNotRestartedByResume(),
                    O_DisabledServiceIsNotRestartedByResume(),
                    K_OriginWaitsForFirstFixAndInitializesOnce(),
                    L_WorldMapPrototypeIsStartupScene(),
                };
                string[] names =
                {
                    "A0_StartupMenuEntersWorldWithProductionNpc",
                    "A_SceneLoadsAndCoreObjectsExist",
                    "H_SceneStoresAutoProvider",
                    "I_EditorResolvesAutoToMock",
                    "J_SceneOriginAndPlacementFromFirstFix",
                    "B_MovementPathMovesPlayerAndUpdatesDistance",
                    "C_TurnPathRotatesAvatarAndCamera",
                    "D_MobClickOpensInfoPanel",
                    "E_EnterArButtonEmitsEncounterRequest",
                    "F_AggroTriggersOnceInsideRange",
                    "G_HudButtonsOpenAndClosePanels",
                    "P_MenuDragAndMerchantConversation",
                    "M_AppPauseSuspendsAndResumeRestoresWithoutOriginReset",
                    "N_ManualStopIsNotRestartedByResume",
                    "O_DisabledServiceIsNotRestartedByResume",
                    "K_OriginWaitsForFirstFixAndInitializesOnce",
                    "L_WorldMapPrototypeIsStartupScene",
                };

                for (int i = 0; i < tests.Length; i++)
                {
                    Begin(names[i]);
                    yield return RunGuarded(tests[i]);
                    End();
                }

                WriteResultsAndExit();
            }

            private IEnumerator RunGuarded(IEnumerator test)
            {
                while (true)
                {
                    object current;
                    try
                    {
                        if (!test.MoveNext())
                        {
                            yield break;
                        }

                        current = test.Current;
                    }
                    catch (Exception ex)
                    {
                        Check(false, "Exception: " + ex.GetType().Name + ": " + ex.Message);
                        yield break;
                    }

                    yield return current;
                }
            }

            private IEnumerator A0_StartupMenuEntersWorldWithProductionNpc()
            {
                GameFrontend frontend = FindFirstObjectByType<GameFrontend>();
                Check(frontend != null && frontend.IsOpen && frontend.CurrentPage == "home", "startup menu missing");
                if (frontend == null) yield break;
                PrototypeMob mob = FindFirstObjectByType<PrototypeMob>(FindObjectsInactive.Include);
                EditorMockMovement movement = FindFirstObjectByType<EditorMockMovement>(FindObjectsInactive.Include);
                Check(mob != null && !mob.enabled, "startup menu did not pause mob behaviour");
                Check(movement != null && !movement.enabled, "startup menu did not pause test movement");
                PrototypeNpc npc = FindFirstObjectByType<PrototypeNpc>(FindObjectsInactive.Include);
                NpcModelPresenter npcView = npc != null ? npc.GetComponent<NpcModelPresenter>() : null;
                Check(npcView != null && npcView.Model != null, "production merchant model missing");

                // Keep this runner in the map, just as test A does; actual AR navigation is tested separately.
                ArEncounterLauncher launcher = FindFirstObjectByType<ArEncounterLauncher>();
                if (launcher != null) launcher.OpenSceneOnRequest = false;
                Button start = null;
                foreach (Button button in frontend.GetComponentsInChildren<Button>(true))
                {
                    TMPro.TMP_Text label = button.GetComponentInChildren<TMPro.TMP_Text>(true);
                    if (label != null && label.text == "Rozpocznij przygodę") { start = button; break; }
                }
                Check(start != null, "startup menu has no start action");
                start?.onClick.Invoke();
                yield return Frames(2);
                Check(!frontend.IsOpen, "start action did not enter world");
                Check(mob != null && mob.enabled, "start action did not resume mob behaviour");
                Check(movement != null && movement.enabled, "start action did not resume movement");
            }

            private IEnumerator A_SceneLoadsAndCoreObjectsExist()
            {
                Check(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path == ScenePath, "active scene is not WorldMapPrototype");
                _location = FindFirstObjectByType<PlayerLocationService>();
                Check(_location != null, "PlayerLocationService missing");
                Check(_location != null && _location.MockProvider != null, "MockLocationProvider not active");
                Check(_location != null && ReferenceEquals(_location.Provider, _location.MockProvider), "active provider is not the mock");
                Check(_location != null && _location.State == LocationProviderState.Running, "mock provider not running");
                _avatar = FindFirstObjectByType<PlayerMapAvatar>();
                _movement = FindFirstObjectByType<EditorMockMovement>();
                _mob = FindFirstObjectByType<PrototypeMob>();
                _controller = FindFirstObjectByType<WorldMapPrototypeController>();
                _bar = FindFirstObjectByType<HudButtonBar>();
                _camera = Camera.main;
                Check(_avatar != null && _avatar.name == "Player", "Player avatar missing");
                Check(_movement != null, "EditorMockMovement missing");
                Check(_mob != null && _mob.MobName == "Trujący Pająk", "mob missing");
                _mobEntity = _mob != null ? _mob.GetComponent<WorldMapEntity>() : null;
                Check(_mobEntity != null, "mob WorldMapEntity missing");
                Check(FindFirstObjectByType<PrototypeNpc>() != null, "NPC missing");
                Check(FindFirstObjectByType<Canvas>() != null, "Canvas missing");
                Check(FindFirstObjectByType<EventSystem>() != null, "EventSystem missing");
                Check(_controller != null, "controller missing");
                Check(_bar != null, "HudButtonBar missing");
                Check(_camera != null, "main camera missing");

                // METINAR-006: encounter requests open the AR scene; keep this runner on the world map
                // (the AR transition itself is covered by ArEncounterSmokeTest).
                _launcher = FindFirstObjectByType<ArEncounterLauncher>();
                Check(_launcher != null, "ArEncounterLauncher missing");
                if (_launcher != null)
                {
                    _launcher.OpenSceneOnRequest = false;
                }

                int missing = 0;
                foreach (GameObject go in FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    foreach (Component c in go.GetComponents<Component>())
                    {
                        if (c == null)
                        {
                            missing++;
                        }
                    }
                }

                Check(missing == 0, "missing script components: " + missing);
                yield break;
            }

            private bool EditState(string key, string expected)
            {
                foreach (string part in (EditModeState ?? string.Empty).Split(';'))
                {
                    if (part == key + "=" + expected)
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool Same(GeoCoordinate a, GeoCoordinate b)
            {
                return a.Latitude == b.Latitude && a.Longitude == b.Longitude;
            }

            private static string Fmt(GeoCoordinate c)
            {
                return "(" + c.Latitude.ToString("R") + ", " + c.Longitude.ToString("R") + ")";
            }

            private IEnumerator H_SceneStoresAutoProvider()
            {
                Check(EditState("kind", ((int)PlayerLocationService.ProviderKind.Auto).ToString()),
                    "saved scene provider kind is not Auto: " + EditModeState);
                int runtimeKind = new SerializedObject(_location).FindProperty("_providerKind").intValue;
                Check(runtimeKind == (int)PlayerLocationService.ProviderKind.Auto, "runtime serialized provider kind " + runtimeKind);
                yield break;
            }

            private IEnumerator I_EditorResolvesAutoToMock()
            {
                Check(_location.Provider is MockLocationProvider, "Auto resolved to " + (_location.Provider == null ? "null" : _location.Provider.GetType().Name));
                Check(!(_location.Provider is UnityLocationProvider), "Auto resolved to UnityLocationProvider in Editor");
                Check(_location.MockProvider != null && ReferenceEquals(_location.Provider, _location.MockProvider), "MockProvider not the active provider");
                yield break;
            }

            private IEnumerator J_SceneOriginAndPlacementFromFirstFix()
            {
                Check(EditState("originPresent", "True"), "no WorldMapOrigin in saved scene");
                Check(EditState("originHas", "False"), "origin already initialized in saved scene (before any fix): " + EditModeState);
                Check(EditState("placementPresent", "True"), "no PrototypeWorldPlacement in saved scene");
                Check(EditState("placementInit", "False"), "placement initialized before Play Mode");
                Check(EditState("mobActive", "False") && EditState("npcActive", "False"), "mob/NPC active before first fix: " + EditModeState);

                PrototypeWorldPlacement placement = FindFirstObjectByType<PrototypeWorldPlacement>();
                WorldMapOrigin origin = FindFirstObjectByType<WorldMapOrigin>();
                Check(placement != null && placement.IsInitialized, "placement not initialized in Play Mode");
                Check(origin != null && origin.HasOrigin, "origin not captured in Play Mode");
                if (placement == null || origin == null || !placement.IsInitialized || !origin.HasOrigin)
                {
                    yield break;
                }

                GeoCoordinate first = placement.FirstFixCoordinate;
                Check(Same(origin.Origin, first), "origin " + Fmt(origin.Origin) + " != first fix " + Fmt(first));
                // Mock has not moved yet, so the first fix equals the current mock coordinate.
                Check(Same(first, _location.MockProvider.Coordinate), "first fix " + Fmt(first) + " != mock coordinate " + Fmt(_location.MockProvider.Coordinate));
                GeoCoordinate expectedMob = GeoMath.OffsetMeters(first, 13.0, 2.0);
                GeoCoordinate expectedNpc = GeoMath.OffsetMeters(first, 4.0, -2.5);
                WorldMapEntity npcEntity = FindFirstObjectByType<PrototypeNpc>().GetComponent<WorldMapEntity>();
                Check(Same(placement.MobCoordinate, expectedMob) && Same(_mobEntity.Coordinate, expectedMob), "mob coordinate not OffsetMeters(firstFix, 13 N, 2 E): " + Fmt(_mobEntity.Coordinate));
                Check(Same(placement.NpcCoordinate, expectedNpc) && Same(npcEntity.Coordinate, expectedNpc), "NPC coordinate not OffsetMeters(firstFix, 4 N, -2.5 E): " + Fmt(npcEntity.Coordinate));
                Check(_mobEntity.gameObject.activeSelf && npcEntity.gameObject.activeSelf, "mob/NPC not activated after placement");

                Vector3 player = _avatar.transform.position;
                Check(new Vector2(player.x, player.z).magnitude < 0.05f, "player local start not ~(0,0): " + player);
                float mobDist = Vector3.Distance(_mob.transform.position, player);
                float npcDist = Vector3.Distance(npcEntity.transform.position, player);
                Check(Mathf.Abs(mobDist - 13.15f) < 0.1f, "mob distance " + mobDist);
                Check(Mathf.Abs(npcDist - 4.72f) < 0.1f, "NPC distance " + npcDist);
                Debug.Log("[SmokeTest] J firstFix=" + Fmt(first) + " mob=" + mobDist + " npc=" + npcDist + " player=" + player);
            }

            private IEnumerator K_OriginWaitsForFirstFixAndInitializesOnce()
            {
                var serviceGo = new GameObject("K_LocationService");
                var service = serviceGo.AddComponent<PlayerLocationService>();
                service.StopProvider(); // no fix available yet
                var origin = new GameObject("K_Origin").AddComponent<WorldMapOrigin>();
                var mobGo = new GameObject("K_Mob");
                mobGo.SetActive(false);
                var mob = mobGo.AddComponent<WorldMapEntity>();
                mob.Configure(origin, "K mob");
                var npcGo = new GameObject("K_Npc");
                npcGo.SetActive(false);
                var npc = npcGo.AddComponent<WorldMapEntity>();
                npc.Configure(origin, "K npc");
                var placement = new GameObject("K_Placement").AddComponent<PrototypeWorldPlacement>();
                placement.Configure(service, origin, mob, npc);
                int initEvents = 0;
                placement.Initialized += _ => initEvents++;

                yield return Frames(5);
                Check(service.MockProvider != null, "K: Auto did not resolve to mock");
                Check(!service.TryGetLatestFix(out _), "K: fix available while stopped");
                Check(!origin.HasOrigin, "K: origin initialized before first fix");
                Check(!placement.IsInitialized, "K: placement initialized before first fix");
                Check(!mob.HasCoordinate && !npc.HasCoordinate, "K: entities got a coordinate before first fix");
                Check(!mobGo.activeSelf && !npcGo.activeSelf, "K: entities activated before first fix");

                var firstFix = new GeoCoordinate(10.0, 20.0); // synthetic test point, not a real address
                service.MockProvider.SetCoordinate(firstFix);
                service.StartProvider();
                yield return Frames(3);
                Check(placement.IsInitialized && initEvents == 1, "K: not initialized after first fix (events " + initEvents + ")");
                Check(origin.HasOrigin && Same(origin.Origin, firstFix), "K: origin not equal to first fix");
                Check(Same(placement.FirstFixCoordinate, firstFix), "K: FirstFixCoordinate wrong");
                Check(Same(mob.Coordinate, GeoMath.OffsetMeters(firstFix, 13.0, 2.0)), "K: mob not relative to first fix " + Fmt(mob.Coordinate));
                Check(Same(npc.Coordinate, GeoMath.OffsetMeters(firstFix, 4.0, -2.5)), "K: NPC not relative to first fix " + Fmt(npc.Coordinate));
                Check(mobGo.activeSelf && npcGo.activeSelf, "K: entities not activated");

                GeoCoordinate mobBefore = mob.Coordinate;
                service.MockProvider.MoveMeters(50.0, 50.0);
                yield return Frames(5);
                Check(Same(origin.Origin, firstFix), "K: origin changed after later fix");
                Check(Same(mob.Coordinate, mobBefore) && initEvents == 1, "K: placement re-initialized after later fix");
                Check(!origin.CaptureOrigin(new GeoCoordinate(1.0, 1.0)) && Same(origin.Origin, firstFix), "K: origin capture not once-only");

                Destroy(placement.gameObject);
                Destroy(mobGo);
                Destroy(npcGo);
                Destroy(origin.gameObject);
                Destroy(serviceGo);
                yield return null;
            }

            /// <summary>
            /// Delivers the real Unity application-pause message to the scene's PlayerLocationService
            /// (same entry point Unity calls on Android/iOS pause/resume).
            /// </summary>
            private void SendAppPause(bool paused)
            {
                _location.SendMessage("OnApplicationPause", paused, SendMessageOptions.RequireReceiver);
            }

            private IEnumerator M_AppPauseSuspendsAndResumeRestoresWithoutOriginReset()
            {
                PrototypeWorldPlacement placement = FindFirstObjectByType<PrototypeWorldPlacement>();
                WorldMapOrigin origin = FindFirstObjectByType<WorldMapOrigin>();
                Check(_location.State == LocationProviderState.Running && _location.AcquisitionRequested, "precondition: provider not running/requested");
                Check(placement != null && placement.IsInitialized && origin != null && origin.HasOrigin, "precondition: placement/origin not initialized");
                if (placement == null || origin == null || !origin.HasOrigin)
                {
                    yield break;
                }

                int initEvents = 0;
                int originEvents = 0;
                Action<PrototypeWorldPlacement> onInit = _ => initEvents++;
                placement.Initialized += onInit;
                Action<GeoCoordinate> onOrigin = _ => originEvents++;
                origin.OriginCaptured += onOrigin;

                GeoCoordinate originBefore = origin.Origin;
                GeoCoordinate mockBefore = _location.MockProvider.Coordinate;
                GeoCoordinate mobBefore = _mobEntity.Coordinate;
                Vector3 avatarBefore = _avatar.transform.position;

                SendAppPause(true);
                yield return Frames(3);
                Check(_location.IsApplicationPaused, "M: service did not observe the pause");
                Check(_location.State == LocationProviderState.Stopped, "M: provider still active while paused: " + _location.State);
                Check(!_location.TryGetLatestFix(out _), "M: cached fix still usable while paused");
                Check(_location.AcquisitionRequested, "M: pause cleared the acquisition request");
                Check(Same(origin.Origin, originBefore), "M: origin changed while paused");

                SendAppPause(true); // repeated notification is ignored
                SendAppPause(false);
                yield return Frames(3);
                Check(!_location.IsApplicationPaused, "M: service did not observe the resume");
                Check(_location.State == LocationProviderState.Running, "M: provider not resumed: " + _location.State);
                Check(_location.TryGetLatestFix(out LocationFix fix) && Same(fix.Coordinate, mockBefore), "M: resumed fix differs from pre-pause position");
                Check(Same(origin.Origin, originBefore), "M: origin reset on resume " + Fmt(origin.Origin));
                Check(placement.IsInitialized && initEvents == 0 && originEvents == 0, "M: placement/origin re-initialized (init " + initEvents + ", origin " + originEvents + ")");
                Check(Same(_mobEntity.Coordinate, mobBefore), "M: mob coordinate changed on resume");
                Check(Vector3.Distance(_avatar.transform.position, avatarBefore) < 0.05f, "M: avatar moved across pause/resume: " + avatarBefore + " -> " + _avatar.transform.position);

                placement.Initialized -= onInit;
                origin.OriginCaptured -= onOrigin;
            }

            private IEnumerator N_ManualStopIsNotRestartedByResume()
            {
                WorldMapOrigin origin = FindFirstObjectByType<WorldMapOrigin>();
                GeoCoordinate originBefore = origin.Origin;

                _location.StopProvider();
                yield return Frames(2);
                SendAppPause(true);
                yield return Frames(2);
                SendAppPause(false);
                yield return Frames(3);
                Check(_location.State == LocationProviderState.Stopped, "N: manual stop restarted by resume: " + _location.State);
                Check(!_location.AcquisitionRequested, "N: acquisition still requested after manual stop");
                Check(!_location.TryGetLatestFix(out _), "N: fix available after manual stop + resume");

                // Explicit start restores the scene for the following tests.
                _location.StartProvider();
                yield return Frames(3);
                Check(_location.State == LocationProviderState.Running, "N: explicit StartProvider did not restart");
                Check(Same(origin.Origin, originBefore), "N: origin changed");
            }

            private IEnumerator O_DisabledServiceIsNotRestartedByResume()
            {
                WorldMapOrigin origin = FindFirstObjectByType<WorldMapOrigin>();
                GeoCoordinate originBefore = origin.Origin;

                _location.enabled = false;
                yield return Frames(2);
                Check(_location.State == LocationProviderState.Stopped, "O: disabling did not stop the provider");
                SendAppPause(true);
                yield return Frames(2);
                SendAppPause(false);
                yield return Frames(3);
                Check(_location.State == LocationProviderState.Stopped, "O: disabled service restarted by resume: " + _location.State);
                Check(!_location.TryGetLatestFix(out _), "O: fix available while disabled");

                _location.enabled = true; // start-on-enable restores the scene
                yield return Frames(3);
                Check(_location.State == LocationProviderState.Running, "O: re-enable did not restart");
                Check(Same(origin.Origin, originBefore), "O: origin changed");
            }

            private IEnumerator L_WorldMapPrototypeIsStartupScene()
            {
                EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
                Check(scenes.Length > 0 && scenes[0].path == ScenePath && scenes[0].enabled, "build index 0 is not enabled WorldMapPrototype: " + (scenes.Length > 0 ? scenes[0].path : "none"));
                string firstEnabled = null;
                foreach (EditorBuildSettingsScene s in scenes)
                {
                    if (s.enabled)
                    {
                        firstEnabled = s.path;
                        break;
                    }

                    if (s.path == SampleScenePath)
                    {
                        // disabled SampleScene before the first enabled scene is fine
                    }
                }

                Check(firstEnabled == ScenePath, "startup (first enabled) scene is " + firstEnabled);
                Check(firstEnabled != SampleScenePath, "SampleScene is the startup scene");
                Check(UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath(ScenePath) == 0, "SceneUtility build index of WorldMapPrototype != 0");
                foreach (EditorBuildSettingsScene s in scenes)
                {
                    if (s.path == SampleScenePath)
                    {
                        Check(!s.enabled, "SampleScene still enabled in build scenes");
                    }
                }

                yield break;
            }

            private IEnumerator B_MovementPathMovesPlayerAndUpdatesDistance()
            {
                _avatar.HeadingDegrees = 0f;
                yield return Frames(3);
                GeoCoordinate before = _location.MockProvider.Coordinate;
                Vector3 posBefore = _avatar.transform.position;
                string labelBefore = _mobEntity.LabelText;
                float distBefore = _mob.DistanceToPlayerMeters;

                // Backward (S key equivalent) for 0.5 s: 4 m south, away from the mob.
                _movement.ApplyMoveInput(new Vector2(0f, -1f), 0.5f);
                yield return Frames(3);

                GeoCoordinate after = _location.MockProvider.Coordinate;
                Vector3 posAfter = _avatar.transform.position;
                Check(after.Latitude < before.Latitude, "mock latitude did not decrease (lat " + before.Latitude + " -> " + after.Latitude + ")");
                Check(Mathf.Abs((posBefore.z - posAfter.z) - 4f) < 0.2f, "avatar did not move ~4 m south: dz=" + (posAfter.z - posBefore.z));
                Check(_mob.DistanceToPlayerMeters > distBefore + 3f, "mob distance did not increase");
                Check(_mobEntity.LabelText != labelBefore, "mob distance label unchanged: " + labelBefore);
                Debug.Log("[SmokeTest] B label '" + labelBefore.Replace("\n", " | ") + "' -> '" + _mobEntity.LabelText.Replace("\n", " | ") + "'");
            }

            private IEnumerator C_TurnPathRotatesAvatarAndCamera()
            {
                yield return new WaitForSecondsRealtime(1.5f);
                float camYawBefore = _camera.transform.eulerAngles.y;
                Transform model = _avatar.transform.Find("Model");
                float modelYawBefore = model.eulerAngles.y;

                _movement.ApplyTurnInput(1f, 0.5f); // E key equivalent, 45 deg at 90 deg/s
                Check(Mathf.Abs(Mathf.DeltaAngle(0f, _avatar.HeadingDegrees) - 45f) < 0.5f, "heading not 45: " + _avatar.HeadingDegrees);
                yield return new WaitForSecondsRealtime(1.5f);
                float camYawAfter = _camera.transform.eulerAngles.y;
                float modelYawAfter = model.eulerAngles.y;
                Check(Mathf.Abs(Mathf.DeltaAngle(modelYawBefore, modelYawAfter) - 45f) < 1f, "model yaw delta " + Mathf.DeltaAngle(modelYawBefore, modelYawAfter));
                Check(Mathf.Abs(Mathf.DeltaAngle(camYawBefore, camYawAfter)) > 20f, "camera yaw barely changed: " + camYawBefore + " -> " + camYawAfter);
                Debug.Log("[SmokeTest] C camera yaw " + camYawBefore + " -> " + camYawAfter);

                _movement.ApplyTurnInput(-1f, 0.5f); // Q key equivalent, back to 0
                Check(Mathf.Abs(Mathf.DeltaAngle(0f, _avatar.HeadingDegrees)) < 0.5f, "heading not back to 0");
                yield return new WaitForSecondsRealtime(1.5f);
            }

            private IEnumerator D_MobClickOpensInfoPanel()
            {
                var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                bool handled = ExecuteEvents.Execute(_mob.gameObject, data, ExecuteEvents.pointerClickHandler);
                Check(handled, "pointer click not handled by mob");
                yield return Frames(2);
                MobInfoPanel panel = _controller.MobInfoPanel;
                Check(panel != null && panel.gameObject.activeInHierarchy, "MobInfoPanel not visible");
                Check(panel.NameLabel == "Trujący Pająk", "name label: " + panel.NameLabel);
                Check(panel.LevelLabel == "Poziom 5", "level label: " + panel.LevelLabel);
                Check(panel.HpLabel == "HP 120/120", "hp label: " + panel.HpLabel);
                Check(panel.DistanceLabel != null && panel.DistanceLabel.StartsWith("Dystans: ") && panel.DistanceLabel.EndsWith(" m"), "distance label: " + panel.DistanceLabel);
                Check(panel.AggressiveLabel == "Agresywny: tak", "aggressive label: " + panel.AggressiveLabel);
                Debug.Log("[SmokeTest] D panel distance " + panel.DistanceLabel);
            }

            private IEnumerator E_EnterArButtonEmitsEncounterRequest()
            {
                int eventCount = 0;
                string reason = null;
                Action<string> handler = r => { eventCount++; reason = r; };
                _controller.EncounterRequested += handler;
                int before = _controller.EncounterRequestCount;
                _controller.MobInfoPanel.EnterArButton.onClick.Invoke();
                yield return Frames(2);
                _controller.EncounterRequested -= handler;
                Check(eventCount == 1, "EncounterRequested event count " + eventCount);
                Check(_controller.EncounterRequestCount == before + 1, "counter not incremented");
                Check(reason == "panel:Trujący Pająk", "reason " + reason);
                Check(_controller.IsToastVisible && _controller.LastToastMessage == "AR ENCOUNTER REQUESTED", "toast " + _controller.LastToastMessage);
                ArEncounterRequest handed = _launcher != null ? _launcher.LastSubmittedRequest : null;
                Check(handed != null && handed.MobName == "Trujący Pająk" && handed.Level == 5 && handed.CurrentHp == 120 && handed.MaxHp == 120
                      && handed.Reason == "panel:Trujący Pająk", "AR handoff request " + handed);
                Check(ArEncounterHandoff.HasPending && ReferenceEquals(ArEncounterHandoff.Pending, handed), "AR handoff not pending");
                Check(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path == ScenePath, "scene changed although OpenSceneOnRequest=false");

                _controller.MobInfoPanel.CloseButton.onClick.Invoke();
                yield return Frames(2);
                Check(!_controller.MobInfoPanel.gameObject.activeSelf, "mob panel close button did not close");
            }

            private IEnumerator F_AggroTriggersOnceInsideRange()
            {
                int encounterStart = _controller.EncounterRequestCount;
                int attackStart = _controller.AttackedCount;
                MockLocationProvider mock = _location.MockProvider;
                double previousAccuracy = mock.HorizontalAccuracyMeters;
                AggroGateSettings gate = _mob.AggroGate.Settings;
                Check(_mob.AggroGate.IsArmed, "aggro gate not armed before approach");

                // METINAR-005A: aggro needs accurate, stable fixes. Use a good outdoor accuracy for the approach.
                mock.HorizontalAccuracyMeters = 2.0;

                // Walk forward 0.5 s (4 m) -> back near origin, still > 12 m from mob.
                _movement.ApplyMoveInput(new Vector2(0f, 1f), 0.5f);
                yield return Frames(10);
                float outside = _mob.DistanceToPlayerMeters;
                Check(outside > _mob.AggroRangeMeters, "expected outside aggro range, got " + outside);
                Check(_controller.AttackedCount == attackStart, "attacked while outside range");
                Check(_controller.EncounterRequestCount == encounterStart, "encounter requested while outside range");

                // Walk forward 0.75 s (6 m) -> genuinely inside 12 m with the accuracy circle inside the range.
                _movement.ApplyMoveInput(new Vector2(0f, 1f), 0.75f);
                yield return Frames(10);
                float inside = _mob.DistanceToPlayerMeters;
                Check(inside <= _mob.AggroRangeMeters, "expected inside aggro range, got " + inside);
                Check(inside + mock.HorizontalAccuracyMeters * gate.accuracyMarginFactor <= _mob.AggroRangeMeters,
                    "approach is not confidently inside: " + inside);
                // A single fix is not enough any more (raw distance alone must not trigger).
                Check(_controller.AttackedCount == attackStart, "attacked on the first inside fix");

                // The player stays there: further fixes (1 s apart) at the same position make the approach stable.
                for (int i = 1; i < gate.requiredStableFixes; i++)
                {
                    mock.MoveMeters(0.0, 0.0);
                    yield return Frames(3);
                }

                Check(_controller.AttackedCount == attackStart + 1, "attack count " + (_controller.AttackedCount - attackStart));
                Check(_controller.EncounterRequestCount == encounterStart + 1, "encounter count " + (_controller.EncounterRequestCount - encounterStart));
                Check(_controller.LastEncounterReason == "aggro:Trujący Pająk", "reason " + _controller.LastEncounterReason);
                Check(_controller.IsToastVisible && _controller.LastToastMessage == "Zostałeś zaatakowany", "toast " + _controller.LastToastMessage);
                Check(_launcher != null && _launcher.LastSubmittedRequest != null && _launcher.LastSubmittedRequest.Reason == "aggro:Trujący Pająk"
                      && _launcher.LastSubmittedRequest.Level == 5, "aggro AR handoff " + (_launcher != null ? _launcher.LastSubmittedRequest : null));
                Check(!_mob.AggroGate.IsArmed, "aggro gate still armed after attack");

                // Staying inside (more frames and more fixes) must not attack again (hysteresis).
                yield return Frames(60);
                for (int i = 0; i < 5; i++)
                {
                    mock.MoveMeters(0.0, 0.0);
                    yield return Frames(3);
                }

                Check(_controller.AttackedCount == attackStart + 1, "attack repeated over frames: " + (_controller.AttackedCount - attackStart));
                Check(_controller.EncounterRequestCount == encounterStart + 1, "encounter repeated over frames: " + (_controller.EncounterRequestCount - encounterStart));
                mock.HorizontalAccuracyMeters = previousAccuracy;
                yield return Frames(2);
                Debug.Log("[SmokeTest] F outside=" + outside + " inside=" + inside);
            }

            private IEnumerator G_HudButtonsOpenAndClosePanels()
            {
                var cases = new (string label, Button button, GameObject panel)[]
                {
                    ("EQ", _bar.EqButton, _controller.InventoryPanel.gameObject),
                    ("Postać", _bar.CharacterButton, _controller.CharacterPanel.gameObject),
                    ("Skille", _bar.SkillsButton, _controller.SkillsPanel.gameObject),
                };
                GameObject[] all = Array.ConvertAll(cases, c => c.panel);

                foreach (var c in cases)
                {
                    c.button.onClick.Invoke();
                    yield return Frames(2);
                    Check(c.panel.activeSelf, c.label + " did not open its panel");
                    foreach (GameObject other in all)
                    {
                        if (other != c.panel)
                        {
                            Check(!other.activeSelf, c.label + ": other panel also open: " + other.name);
                        }
                    }

                    Transform close = c.panel.transform.Find("Close");
                    Button closeButton = close != null ? close.GetComponent<Button>() : null;
                    Check(closeButton != null, c.label + " panel has no Close button");
                    closeButton?.onClick.Invoke();
                    yield return Frames(2);
                    Check(!c.panel.activeSelf, c.label + " close button did not close panel");
                }

                GameFrontend frontend = FindFirstObjectByType<GameFrontend>();
                Check(frontend != null, "integrated frontend missing");
                if (frontend == null) yield break;
                foreach (var c in new (string page, Button button)[]
                         { ("settings", _bar.SettingsButton), ("home", _bar.HamburgerButton) })
                {
                    c.button.onClick.Invoke();
                    yield return Frames(2);
                    Check(frontend.IsOpen && frontend.CurrentPage == c.page, c.page + " did not open in frontend");
                    Check(!_mob.enabled && !_movement.enabled, "modal menu did not pause map behaviours");
                    Check(!_controller.SettingsPanel.gameObject.activeSelf && !_controller.HamburgerPanel.gameObject.activeSelf,
                        "old placeholder panel is still active behind frontend");
                    foreach (GameObject panel in all) Check(!panel.activeSelf, "map panel left open behind menu: " + panel.name);
                    frontend.CloseMenu();
                    yield return Frames(2);
                    Check(!frontend.IsOpen && _mob.enabled && _movement.enabled, "closing menu did not restore map");
                }
            }

            private IEnumerator P_MenuDragAndMerchantConversation()
            {
                GameFrontend frontend = FindFirstObjectByType<GameFrontend>();
                PrototypeNpc npc = FindFirstObjectByType<PrototypeNpc>();
                NpcModelPresenter presenter = npc != null ? npc.GetComponent<NpcModelPresenter>() : null;
                Animator animator = presenter != null && presenter.Model != null
                    ? presenter.Model.GetComponentInChildren<Animator>() : null;
                Check(frontend != null && presenter != null && animator != null, "menu/merchant Animator missing");
                if (frontend == null || presenter == null || animator == null) yield break;

                var click = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                try
                {
                    // A queued world click behind a modal must not start an unowned Talk state.
                    frontend.ShowSettings();
                    ExecuteEvents.Execute(npc.gameObject, click, ExecuteEvents.pointerClickHandler);
                    yield return new WaitForSecondsRealtime(0.5f);
                    Check(frontend.CurrentPage == "settings" && animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Idle"),
                        "rejected merchant conversation changed page or left NPC talking");
                    frontend.CloseMenu();

                    Collider body = presenter.Model.GetComponentInChildren<Collider>();
                    Check(body != null && body.enabled, "production merchant click collider missing/disabled");
                    if (body != null)
                        Check(ExecuteEvents.ExecuteHierarchy(body.gameObject, click, ExecuteEvents.pointerClickHandler) == npc.gameObject,
                            "click on production collider did not reach the NPC handler");
                    yield return new WaitForSecondsRealtime(0.5f);
                    Check(frontend.IsOpen && frontend.CurrentPage == "merchant", "merchant click did not open conversation");
                    Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Talk"), "accepted conversation did not play Talk");
                    frontend.ShowItems();
                    yield return new WaitForSecondsRealtime(0.5f);
                    Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Idle"), "leaving conversation did not restore Idle");

                    Canvas.ForceUpdateCanvases();
                    ScrollRect scroll = frontend.GetComponentInChildren<ScrollRect>();
                    Check(scroll != null && scroll.content.childCount > 0, "catalog scroll/content missing");
                    if (scroll == null || scroll.content.childCount == 0) yield break;
                    var header = (RectTransform)scroll.content.GetChild(0);
                    Vector2 point = RectTransformUtility.WorldToScreenPoint(null, header.TransformPoint(header.rect.center));
                    var drag = new PointerEventData(EventSystem.current)
                    {
                        button = PointerEventData.InputButton.Left, position = point, pressPosition = point
                    };
                    var hits = new List<RaycastResult>();
                    EventSystem.current.RaycastAll(drag, hits);
                    GameObject dragTarget = hits.Count > 0 ? ExecuteEvents.GetEventHandler<IDragHandler>(hits[0].gameObject) : null;
                    Check(dragTarget == scroll.gameObject, "drag on catalog text hits modal background instead of ScrollRect");
                    if (dragTarget != null)
                    {
                        drag.pointerPressRaycast = hits[0];
                        ExecuteEvents.Execute(dragTarget, drag, ExecuteEvents.initializePotentialDrag);
                        ExecuteEvents.Execute(dragTarget, drag, ExecuteEvents.beginDragHandler);
                        float before = scroll.content.anchoredPosition.y;
                        drag.position += new Vector2(0f, 160f);
                        ExecuteEvents.Execute(dragTarget, drag, ExecuteEvents.dragHandler);
                        ExecuteEvents.Execute(dragTarget, drag, ExecuteEvents.endDragHandler);
                        Check(scroll.content.anchoredPosition.y > before + 1f, "catalog did not move when dragged from text");
                    }

                    scroll.velocity = new Vector2(0f, 1000f);
                    frontend.ShowSettings();
                    Check(scroll.velocity == Vector2.zero && scroll.content.anchoredPosition == Vector2.zero,
                        "page navigation retained catalog scroll velocity/position");
                    Check(!_mob.enabled && !_movement.enabled, "page navigation resumed the map behind the modal");
                }
                finally { frontend.CloseMenu(); }
                yield return Frames(2);
                Check(_mob.enabled && _movement.enabled, "menu/merchant flow did not restore map behaviours");
            }

            private void WriteResultsAndExit()
            {
                int failures = 0;
                var sb = new StringBuilder();
                foreach (var r in _results)
                {
                    if (r.failure != null)
                    {
                        failures++;
                    }
                }

                sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
                sb.AppendLine("<testsuite name=\"WorldMapPrototypeSmokeTest\" tests=\"" + _results.Count + "\" failures=\"" + failures + "\">");
                foreach (var r in _results)
                {
                    sb.Append("  <testcase name=\"").Append(r.name).Append("\"");
                    if (r.failure == null)
                    {
                        sb.AppendLine(" />");
                    }
                    else
                    {
                        sb.AppendLine(">");
                        sb.Append("    <failure message=\"").Append(System.Security.SecurityElement.Escape(r.failure)).AppendLine("\" />");
                        sb.AppendLine("  </testcase>");
                    }
                }

                sb.AppendLine("</testsuite>");
                File.WriteAllText(ResultsPath, sb.ToString());
                Debug.Log("[SmokeTest] RESULT tests=" + _results.Count + " failures=" + failures + " -> " + ResultsPath);
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }
    }
}

