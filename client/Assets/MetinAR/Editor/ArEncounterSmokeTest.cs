using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MetinAR.ArEncounter;
using MetinAR.Location;
using MetinAR.UI;
using MetinAR.WorldMap;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Object = UnityEngine.Object;

namespace MetinAR.Editor
{
    /// <summary>
    /// METINAR-006 real Play Mode smoke test: world map → "Wejdź do AR" (Button.onClick) → AR encounter scene,
    /// XR Simulation scanning → ready-to-place → single placement via the "Postaw potwora" button, no duplicates,
    /// placeholder buttons, exit back to the world map, plus deterministic synthetic-frame placement and the
    /// unsupported / session-failure states.
    /// Usage: Unity -batchmode -projectPath client -executeMethod MetinAR.Editor.ArEncounterSmokeTest.Run
    /// Writes Builds/metinar-006-ar-smoke-results.xml and exits 0 on pass, 1 on failure.
    /// </summary>
    public static class ArEncounterSmokeTest
    {
        private const string SessionKey = "MetinAR006.SmokeResults";
        private const string PreKey = "MetinAR006.EditModeState";
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";

        public static void Run()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string repoRoot = Directory.GetParent(projectRoot)!.FullName;
            string resultsPath = Path.Combine(repoRoot, "Builds", "metinar-006-ar-smoke-results.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(resultsPath)!);

            var sb = new StringBuilder();
            EditorSceneManager.OpenScene(ArEncounterScenes.ArEncounterScenePath, OpenSceneMode.Single);
            sb.Append("arMissing=").Append(CountMissingInOpenScene());
            sb.Append(";spiderMissing=").Append(CountMissingInPrefab("Assets/MetinAR/Prefabs/ArPrototypeSpider.prefab"));
            sb.Append(";planeMissing=").Append(CountMissingInPrefab("Assets/MetinAR/Prefabs/ArGroundPlane.prefab"));
            EditorSceneManager.OpenScene(ArEncounterScenes.WorldMapScenePath, OpenSceneMode.Single);
            sb.Append(";wmMissing=").Append(CountMissingInOpenScene());
            Debug.Log("[ArSmoke] edit-mode " + sb);

            ArEncounterHandoff.Reset();
            SessionState.SetString(SessionKey, resultsPath);
            SessionState.SetString(PreKey, sb.ToString());
            EditorApplication.isPlaying = true;
        }

        private static int CountMissingInOpenScene()
        {
            int missing = 0;
            foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
            {
                missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            }

            return missing;
        }

        private static int CountMissingInPrefab(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                return -1;
            }

            int missing = 0;
            foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
            {
                missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            }

            return missing;
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
            var go = new GameObject("ArSmokeTestRunner");
            Object.DontDestroyOnLoad(go);
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
            private readonly List<string> _exceptions = new List<string>();
            private readonly List<string> _errors = new List<string>();
            private string _currentFailure;

            private ArEncounterSceneController _ar;
            private Action<ArFrameSnapshot> _pendingOverride;
            private int _syntheticPhase;
            private Vector3 _syntheticTarget;
            private ArSessionStatus _syntheticStatus = ArSessionStatus.SessionTracking;
            private Vector3 _realPlacedPosition;
            private bool _realPlaced;

            private void Check(bool condition, string message)
            {
                if (!condition && _currentFailure == null)
                {
                    _currentFailure = message;
                }
            }

            private static IEnumerator Frames(int n)
            {
                for (int i = 0; i < n; i++)
                {
                    yield return null;
                }
            }

            private void OnEnable()
            {
                Application.logMessageReceived += OnLog;
                SceneManager.sceneLoaded += OnSceneLoaded;
            }

            private void OnDisable()
            {
                Application.logMessageReceived -= OnLog;
                SceneManager.sceneLoaded -= OnSceneLoaded;
            }

            private void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Exception)
                {
                    _exceptions.Add(condition);
                }
                else if (type == LogType.Error)
                {
                    _errors.Add(condition);
                }
            }

            /// <summary>Runs after Awake/OnEnable and before Start of the loaded scene, so an override applies from the first frame.</summary>
            private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
            {
                if (scene.path != ArEncounterScenes.ArEncounterScenePath)
                {
                    return;
                }

                _ar = FindAnyObjectByType<ArEncounterSceneController>();
                if (_ar != null && _pendingOverride != null)
                {
                    _ar.FrameSourceOverride = _pendingOverride;
                }

                _pendingOverride = null;
            }

            private IEnumerator Start()
            {
                yield return Frames(5);
                var tests = new (string, Func<IEnumerator>)[]
                {
                    ("T01_WorldMapHasLauncherAndBuildScenes", T01_WorldMapHasLauncherAndBuildScenes),
                    ("T02_NoMissingScriptsInScenesAndPrefabs", T02_NoMissingScriptsInScenesAndPrefabs),
                    ("T03_EnterArButtonHandsOffRequestAndOpensArScene", T03_EnterArButtonHandsOffRequestAndOpensArScene),
                    ("T04_ArSceneConsumedRequestAndHasArRigAndHud", T04_ArSceneConsumedRequestAndHasArRigAndHud),
                    ("T05_BeforeGroundNothingCanBePlaced", T05_BeforeGroundNothingCanBePlaced),
                    ("T06_XrSimulationSessionRunsAndScans", T06_XrSimulationSessionRunsAndScans),
                    ("T07_XrSimulationDetectsGroundReadyToPlace", T07_XrSimulationDetectsGroundReadyToPlace),
                    ("T08_PlaceButtonSpawnsOneAnchoredMonsterOnGround", T08_PlaceButtonSpawnsOneAnchoredMonsterOnGround),
                    ("T09_RepeatedPlaceNeverDuplicatesAndStaysStable", T09_RepeatedPlaceNeverDuplicatesAndStaysStable),
                    ("T10_FppActionsAndRemainingPlaceholders", T10_FppActionsAndRemainingPlaceholders),
                    ("T10b_FppAttackAuraAndHitGeometry", T10b_FppAttackAuraAndHitGeometry),
                    ("T11_ExitButtonReturnsToWorldMap", T11_ExitButtonReturnsToWorldMap),
                    ("T12_SyntheticFrames_ScanReadyPlacedSingleSpawn", T12_SyntheticFrames_ScanReadyPlacedSingleSpawn),
                    ("T13_UnsupportedStateIsClearAndNonCrashing", T13_UnsupportedStateIsClearAndNonCrashing),
                    ("T14_SessionStartTimeoutFailsThenRecovers", T14_SessionStartTimeoutFailsThenRecovers),
                    ("T15_NoExceptionsLogged", T15_NoExceptionsLogged),
                };

                foreach (var (name, test) in tests)
                {
                    _currentFailure = null;
                    yield return RunGuarded(test());
                    _results.Add((name, _currentFailure));
                    Debug.Log("[ArSmoke] " + (_currentFailure == null ? "PASS " : "FAIL ") + name
                              + (_currentFailure == null ? string.Empty : " :: " + _currentFailure));
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

            private static string ActiveScenePath => SceneManager.GetActiveScene().path;

            private static IEnumerator WaitUntil(Func<bool> condition, float seconds)
            {
                float end = Time.realtimeSinceStartup + seconds;
                while (!condition() && Time.realtimeSinceStartup < end)
                {
                    yield return null;
                }
            }

            private static int RuntimeMissingScripts()
            {
                int missing = 0;
                foreach (GameObject go in FindObjectsByType<GameObject>(FindObjectsInactive.Include))
                {
                    foreach (Component c in go.GetComponents<Component>())
                    {
                        if (c == null)
                        {
                            missing++;
                        }
                    }
                }

                return missing;
            }

            private static int MobCount()
            {
                return FindObjectsByType<ArEncounterMob>(FindObjectsInactive.Include).Length;
            }

            private IEnumerator OpenArFromWorldMapPanel()
            {
                PrototypeWorldPlacement placement = FindAnyObjectByType<PrototypeWorldPlacement>();
                yield return WaitUntil(() => placement != null && placement.IsInitialized, 10f);
                Check(placement != null && placement.IsInitialized, "world map placement not initialized");
                PrototypeMob mob = FindAnyObjectByType<PrototypeMob>();
                WorldMapPrototypeController controller = FindAnyObjectByType<WorldMapPrototypeController>();
                Check(mob != null && controller != null, "world map mob/controller missing");
                if (mob == null || controller == null)
                {
                    yield break;
                }

                var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                Check(ExecuteEvents.Execute(mob.gameObject, data, ExecuteEvents.pointerClickHandler), "mob pointer click not handled");
                yield return Frames(2);
                Check(controller.MobInfoPanel.gameObject.activeInHierarchy, "mob info panel not open");
                controller.MobInfoPanel.EnterArButton.onClick.Invoke();
                yield return WaitUntil(() => ActiveScenePath == ArEncounterScenes.ArEncounterScenePath, 10f);
                Check(ActiveScenePath == ArEncounterScenes.ArEncounterScenePath, "AR scene not opened: " + ActiveScenePath);
                yield return Frames(2);
                _ar = FindAnyObjectByType<ArEncounterSceneController>();
            }

            // ---------------------------------------------------------------- tests

            private IEnumerator T01_WorldMapHasLauncherAndBuildScenes()
            {
                Check(ActiveScenePath == ArEncounterScenes.WorldMapScenePath, "active scene " + ActiveScenePath);
                ArEncounterLauncher launcher = FindAnyObjectByType<ArEncounterLauncher>();
                Check(launcher != null, "ArEncounterLauncher missing in world map");
                Check(launcher != null && launcher.OpenSceneOnRequest, "launcher does not open the AR scene by default");
                Check(launcher != null && launcher.ArScenePath == ArEncounterScenes.ArEncounterScenePath, "launcher AR scene path");
                EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
                Check(scenes.Length >= 2, "build scene count " + scenes.Length);
                Check(scenes.Length > 0 && scenes[0].path == ArEncounterScenes.WorldMapScenePath && scenes[0].enabled, "index 0 is not enabled WorldMapPrototype");
                Check(scenes.Length > 1 && scenes[1].path == ArEncounterScenes.ArEncounterScenePath && scenes[1].enabled, "index 1 is not enabled ArEncounterPrototype");
                Check(SceneUtility.GetBuildIndexByScenePath(ArEncounterScenes.WorldMapScenePath) == 0, "WM runtime build index != 0");
                Check(SceneUtility.GetBuildIndexByScenePath(ArEncounterScenes.ArEncounterScenePath) == 1, "AR runtime build index != 1");
                foreach (EditorBuildSettingsScene s in scenes)
                {
                    if (s.path == SampleScenePath)
                    {
                        Check(!s.enabled, "SampleScene enabled");
                    }
                }

                yield break;
            }

            private IEnumerator T02_NoMissingScriptsInScenesAndPrefabs()
            {
                Check(EditState("arMissing", "0"), "AR scene missing scripts: " + EditModeState);
                Check(EditState("wmMissing", "0"), "world map missing scripts: " + EditModeState);
                Check(EditState("spiderMissing", "0"), "spider prefab missing scripts: " + EditModeState);
                Check(EditState("planeMissing", "0"), "plane prefab missing scripts: " + EditModeState);
                Check(RuntimeMissingScripts() == 0, "world map runtime missing scripts");
                yield break;
            }

            private IEnumerator T03_EnterArButtonHandsOffRequestAndOpensArScene()
            {
                ArEncounterLauncher launcher = FindAnyObjectByType<ArEncounterLauncher>();
                int before = ArEncounterHandoff.SubmittedCount;
                IEnumerator open = OpenArFromWorldMapPanel();
                while (open.MoveNext())
                {
                    yield return open.Current;
                    if (launcher != null && launcher.LastSubmittedRequest != null && ArEncounterHandoff.SubmittedCount == before + 1)
                    {
                        ArEncounterRequest r = launcher.LastSubmittedRequest;
                        Check(r.MobName == "Trujący Pająk" && r.Level == 5 && r.CurrentHp == 120 && r.MaxHp == 120 && r.Aggressive,
                            "submitted request " + r);
                        Check(r.Reason == "panel:Trujący Pająk", "reason " + r.Reason);
                    }
                }

                Check(ArEncounterHandoff.SubmittedCount == before + 1, "handoff submitted " + (ArEncounterHandoff.SubmittedCount - before));
                Check(_ar != null, "ArEncounterSceneController missing after opening");
            }

            private IEnumerator T04_ArSceneConsumedRequestAndHasArRigAndHud()
            {
                Check(_ar != null, "no AR controller");
                if (_ar == null)
                {
                    yield break;
                }

                Check(_ar.RequestFromWorldMap, "request not from world map");
                Check(_ar.Request != null && _ar.Request.MobName == "Trujący Pająk" && _ar.Request.Level == 5 && _ar.Request.MaxHp == 120,
                    "request " + _ar.Request);
                Check(!ArEncounterHandoff.HasPending, "handoff still pending after AR scene consumed it");
                Check(RuntimeMissingScripts() == 0, "AR runtime missing scripts");
                Check(FindAnyObjectByType<ARSession>() != null && FindAnyObjectByType<ARInputManager>() != null, "AR Session / ARInputManager missing");
                XROrigin origin = FindAnyObjectByType<XROrigin>();
                Check(origin != null && origin.Camera != null && origin.Camera == Camera.main, "XR Origin / AR camera missing");
                Check(origin != null && origin.Camera.GetComponent<ARCameraManager>() != null && origin.Camera.GetComponent<ARCameraBackground>() != null
                      && origin.Camera.GetComponent<TrackedPoseDriver>() != null, "AR camera components missing");
                Check(_ar.PlaneManager != null && _ar.PlaneManager.requestedDetectionMode == PlaneDetectionMode.Horizontal, "plane manager not horizontal");
                Check(_ar.PlaneManager != null && _ar.PlaneManager.planePrefab != null, "plane prefab missing");
                Check(FindAnyObjectByType<ARRaycastManager>() != null && FindAnyObjectByType<ARAnchorManager>() != null, "raycast/anchor manager missing");
                Check(FindAnyObjectByType<PlayerLocationService>() == null, "GPS location service present inside the AR encounter");
                ArEncounterHud hud = _ar.Hud;
                Check(hud != null, "HUD missing");
                Check(hud != null && hud.ActionButtons.Length == 3 && hud.NavButtons.Length == 4, "action/nav button counts");
                foreach (string n in new[] { "PlayerCard", "Radar", "QuestTracker", "NavBar", "Status", "PlaceMonster", "ExitAr" })
                {
                    Check(GameObject.Find(n) != null, "HUD element missing: " + n);
                }

                Check(MobCount() == 0, "monster exists before placement");
                Check(_ar.SpawnCount == 0, "spawn count before placement");
                Debug.Log("[ArSmoke] T04 states " + string.Join(",", _ar.StateHistory) + " session=" + ARSession.state);
            }

            private IEnumerator T05_BeforeGroundNothingCanBePlaced()
            {
                if (_ar == null)
                {
                    Check(false, "no AR controller");
                    yield break;
                }

                ArEncounterState s = _ar.State;
                Check(s == ArEncounterState.Starting || s == ArEncounterState.Scanning, "initial state " + s);
                Check(!_ar.Hud.IsPlaceButtonInteractable, "place button interactable before ground");
                Check(!string.IsNullOrEmpty(_ar.Hud.StatusLabel), "no status text");
                if (s == ArEncounterState.Starting || s == ArEncounterState.Scanning)
                {
                    _ar.Hud.PlaceButton.onClick.Invoke();
                    yield return Frames(2);
                    Check(_ar.SpawnCount == 0 && MobCount() == 0, "spawned before ground was detected");
                    Check(_ar.LastPlacementResult == ArPlacementResult.NotReady || _ar.LastPlacementResult == null
                          || _ar.State == ArEncounterState.ReadyToPlace, "placement result " + _ar.LastPlacementResult);
                }

                Debug.Log("[ArSmoke] T05 state=" + _ar.State + " status='" + _ar.Hud.StatusLabel + "'");
            }

            private IEnumerator T06_XrSimulationSessionRunsAndScans()
            {
                yield return WaitUntil(() => _ar != null && (_ar.State == ArEncounterState.Scanning || _ar.State == ArEncounterState.ReadyToPlace), 30f);
                Check(_ar != null && (_ar.State == ArEncounterState.Scanning || _ar.State == ArEncounterState.ReadyToPlace),
                    "XR Simulation session did not reach scanning: state=" + (_ar != null ? _ar.State.ToString() : "-") + " session=" + ARSession.state);
                Check(_ar != null && (_ar.LastSessionStatus == ArSessionStatus.SessionInitializing || _ar.LastSessionStatus == ArSessionStatus.SessionTracking),
                    "session status " + (_ar != null ? _ar.LastSessionStatus.ToString() : "-"));
                Check(_ar != null && IndexOf(ArEncounterState.Scanning) >= 0, "Scanning state never observed");
                Debug.Log("[ArSmoke] T06 state=" + (_ar != null ? _ar.State.ToString() : "-") + " status='" + (_ar != null ? _ar.Hud.StatusLabel : "") + "'");
            }

            /// <summary>
            /// XR Simulation's simulated device camera (same object the Editor user moves with mouse/WASD).
            /// XR Simulation only scans the environment while this camera moves, like a real phone.
            /// Found by type name to avoid a compile-time dependency on the simulation assembly.
            /// </summary>
            private static Transform FindSimulationCamera()
            {
                foreach (MonoBehaviour b in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                {
                    if (b != null && b.GetType().Name == "SimulationCameraPoseProvider")
                    {
                        return b.transform;
                    }
                }

                return null;
            }

            private IEnumerator T07_XrSimulationDetectsGroundReadyToPlace()
            {
                Transform simCam = FindSimulationCamera();
                Check(simCam != null, "XR Simulation camera pose provider not found");
                if (simCam != null)
                {
                    // Simulated "scan the terrain" gesture: tilt the phone toward the floor and sweep slowly.
                    Vector3 startPos = simCam.position;
                    float startYaw = simCam.eulerAngles.y;
                    Debug.Log("[ArSmoke] T07 sim camera start pose " + startPos + " yaw=" + startYaw + " pitch=" + simCam.eulerAngles.x);
                    float t0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t0 < 90f
                           && !(_ar != null && _ar.State == ArEncounterState.ReadyToPlace && _ar.HasPlacementTarget))
                    {
                        float t = Time.realtimeSinceStartup - t0;
                        float pitch = 42f + 8f * Mathf.Sin(t * 0.9f);
                        float yaw = startYaw + 30f * Mathf.Sin(t * 0.45f);
                        simCam.SetPositionAndRotation(
                            startPos + new Vector3(0.15f * Mathf.Sin(t * 0.6f), 0f, 0.1f * Mathf.Sin(t * 0.35f)),
                            Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, Vector3.right));
                        yield return null;
                    }

                    // Hold the phone still on the found ground for a moment.
                    yield return Frames(10);
                    Debug.Log("[ArSmoke] T07 scan took " + (Time.realtimeSinceStartup - t0) + " s, sim camera " + simCam.position + " " + simCam.eulerAngles);
                }

                yield return WaitUntil(() => _ar != null && _ar.State == ArEncounterState.ReadyToPlace && _ar.HasPlacementTarget, 5f);
                int planes = 0;
                int horizontal = 0;
                if (_ar != null && _ar.PlaneManager != null)
                {
                    foreach (ARPlane p in _ar.PlaneManager.trackables)
                    {
                        planes++;
                        if (p.alignment == PlaneAlignment.HorizontalUp)
                        {
                            horizontal++;
                        }
                    }
                }

                Debug.Log("[ArSmoke] T07 state=" + (_ar != null ? _ar.State.ToString() : "-") + " planes=" + planes + " horizontalUp=" + horizontal
                          + " target=" + (_ar != null && _ar.HasPlacementTarget) + " fromReticle=" + (_ar != null && _ar.PlacementTargetFromReticle));
                Check(_ar != null && _ar.State == ArEncounterState.ReadyToPlace, "XR Simulation never reached ReadyToPlace (planes=" + planes + ", horizontalUp=" + horizontal + ")");
                Check(_ar != null && _ar.HasPlacementTarget, "no placement target on suitable ground");
                Check(_ar != null && _ar.Hud.IsPlaceButtonInteractable, "place button not interactable when ready");
                Check(_ar != null && _ar.PlacementReticle != null && _ar.PlacementReticle.gameObject.activeSelf, "reticle not shown on ground");
                Check(MobCount() == 0, "monster spawned before confirm");
            }

            private IEnumerator T08_PlaceButtonSpawnsOneAnchoredMonsterOnGround()
            {
                if (_ar == null || _ar.State != ArEncounterState.ReadyToPlace)
                {
                    Check(false, "precondition: not ReadyToPlace (" + (_ar != null ? _ar.State.ToString() : "-") + ")");
                    yield break;
                }

                Pose target = _ar.PlacementTargetPose;
                string planeId = _ar.PlacementTargetPlaneId;
                int placedEvents = 0;
                Action<ArEncounterMob> onPlaced = _ => placedEvents++;
                _ar.MobPlaced += onPlaced;
                _ar.Hud.PlaceButton.onClick.Invoke();
                yield return Frames(3);
                _ar.MobPlaced -= onPlaced;

                Check(_ar.LastPlacementResult == ArPlacementResult.Placed, "placement result " + _ar.LastPlacementResult);
                Check(_ar.State == ArEncounterState.Placed, "state " + _ar.State);
                Check(_ar.SpawnCount == 1 && MobCount() == 1 && placedEvents == 1, "spawn count " + _ar.SpawnCount + " mobs " + MobCount() + " events " + placedEvents);
                Check(_ar.Machine.PlacedPlaneId == planeId, "placed plane id");
                ArEncounterMob mob = _ar.SpawnedMob;
                Check(mob != null, "spawned mob missing");
                if (mob == null)
                {
                    yield break;
                }

                ARPlane plane = null;
                foreach (ARPlane p in _ar.PlaneManager.trackables)
                {
                    if (p.trackableId.ToString() == planeId)
                    {
                        plane = p;
                    }
                }

                Check(plane != null, "placed plane not found among trackables: " + planeId);
                Check(Vector3.Distance(mob.transform.position, target.position) < 0.05f, "mob not at target pose: " + mob.transform.position + " vs " + target.position);
                if (plane != null)
                {
                    float height = Vector3.Dot(mob.transform.position - plane.transform.position, plane.normal);
                    Check(Mathf.Abs(height) < 0.03f, "mob not on the plane surface, height " + height);
                }

                Check(_ar.MobAnchor != null && mob.transform.parent == _ar.MobAnchor.transform, "mob is not parented to an AR anchor");
                Check(mob.GetComponentInParent<Canvas>() == null, "mob is a UI overlay");
                Check(mob.GetComponentsInChildren<MeshRenderer>(true).Length >= 20, "mob has few 3D renderers: " + mob.GetComponentsInChildren<MeshRenderer>(true).Length);
                Check(Vector3.Angle(mob.transform.up, Vector3.up) < 1f, "mob not upright");
                Vector3 toCam = Vector3.ProjectOnPlane(_ar.ArCamera.transform.position - mob.transform.position, Vector3.up);
                Check(toCam.sqrMagnitude < 1e-4f || Vector3.Angle(mob.transform.forward, toCam) < 5f, "mob does not face the camera");
                Check(mob.NameLabel == "Trujący Pająk" && mob.LevelLabel == "Poz. 5" && mob.HpLabel == "HP 120/120", "labels " + mob.NameLabel + "/" + mob.LevelLabel + "/" + mob.HpLabel);
                Check(Mathf.Abs(mob.HpFillFraction - 1f) < 1e-4f, "hp fill " + mob.HpFillFraction);
                Check(!_ar.Hud.IsPlaceButtonVisible, "place button still visible");
                Check(!_ar.PlacementReticle.gameObject.activeSelf, "reticle still visible");
                Check(_ar.Hud.IsRadarMobDotVisible, "radar mob dot not shown");
                int iScan = IndexOf(ArEncounterState.Scanning);
                int iReady = IndexOf(ArEncounterState.ReadyToPlace);
                int iPlaced = IndexOf(ArEncounterState.Placed);
                Check(iScan >= 0 && iReady > iScan && iPlaced > iReady, "state order " + string.Join(",", _ar.StateHistory));
                _realPlacedPosition = mob.transform.position;
                _realPlaced = true;
                Debug.Log("[ArSmoke] T08 placed at " + mob.transform.position + " plane=" + planeId + " anchor=" + (_ar.MobAnchor != null)
                          + " fromReticle=" + _ar.PlacementTargetFromReticle + " states=" + string.Join(",", _ar.StateHistory));
            }

            private int IndexOf(ArEncounterState s)
            {
                for (int i = 0; i < _ar.StateHistory.Count; i++)
                {
                    if (_ar.StateHistory[i] == s)
                    {
                        return i;
                    }
                }

                return -1;
            }

            private IEnumerator T09_RepeatedPlaceNeverDuplicatesAndStaysStable()
            {
                if (_ar == null || !_realPlaced)
                {
                    Check(false, "precondition: not placed");
                    yield break;
                }

                for (int i = 0; i < 5; i++)
                {
                    _ar.Hud.PlaceButton.onClick.Invoke();
                    yield return null;
                }

                for (int i = 0; i < 3; i++)
                {
                    Check(_ar.ConfirmPlacement() == ArPlacementResult.AlreadyPlaced, "repeat placement not AlreadyPlaced");
                }

                yield return Frames(120);
                Check(MobCount() == 1 && _ar.SpawnCount == 1 && _ar.Machine.PlacementCount == 1, "duplicates: mobs " + MobCount() + " spawns " + _ar.SpawnCount);
                Check(_ar.State == ArEncounterState.Placed, "state " + _ar.State);
                float drift = Vector3.Distance(_ar.SpawnedMob.transform.position, _realPlacedPosition);
                Check(drift < 0.05f, "mob drifted " + drift + " m");
                int visiblePlanes = 0;
                foreach (ARPlane p in _ar.PlaneManager.trackables)
                {
                    if (p.gameObject.activeSelf)
                    {
                        visiblePlanes++;
                    }
                }

                Check(visiblePlanes == 0, "plane visuals still shown after placement: " + visiblePlanes);
                Debug.Log("[ArSmoke] T09 drift=" + drift);
            }

            private IEnumerator T10_FppActionsAndRemainingPlaceholders()
            {
                if (_ar == null)
                {
                    Check(false, "no AR controller");
                    yield break;
                }

                var all = new List<Button>();
                for (int i = 2; i < _ar.Hud.ActionButtons.Length; i++) all.Add(_ar.Hud.ActionButtons[i]);
                all.AddRange(_ar.Hud.NavButtons);
                ArEncounterState stateBefore = _ar.State;
                int mobsBefore = MobCount();
                Check(stateBefore == ArEncounterState.Placed, "precondition: encounter not placed (" + stateBefore + ")");
                foreach (Button b in all)
                {
                    b.onClick.Invoke();
                    yield return Frames(1);
                    Check(_ar.Hud.IsToastVisible && _ar.Hud.LastToastMessage == ArEncounterHud.UnassignedNotice, b.name + " toast " + _ar.Hud.LastToastMessage);
                    Check(_ar.State == stateBefore && MobCount() == mobsBefore && _ar.SpawnCount == mobsBefore
                          && ActiveScenePath == ArEncounterScenes.ArEncounterScenePath, b.name + " changed the encounter");
                }
            }

            private IEnumerator T10b_FppAttackAuraAndHitGeometry()
            {
                FppCombatController combat = _ar != null ? _ar.FppCombat : null;
                Check(combat != null, "FPP component missing");
                if (combat == null) yield break;
                var savedFrameSource = _ar.FrameSourceOverride;
                Vector3 savedPosition = _ar.SpawnedMob.transform.position;
                Quaternion savedRotation = _ar.SpawnedMob.transform.rotation;
                _ar.FrameSourceOverride = frame => frame.SessionStatus = ArSessionStatus.SessionTracking;
                yield return Frames(3);
                try
                {
                    Check(combat.Ready && combat.View != null && combat.View.gameObject.activeInHierarchy, "FPP view not ready");
                    if (combat.View == null) yield break;
                    Check(combat.View.RightGrip != null && combat.View.LeftHand != null, "virtual hands missing");
                    Check(_ar.Hud.ActionButtons[0].interactable && _ar.Hud.ActionButtons[1].interactable, "combat buttons not enabled");
                    Pose pose = FppHandsView.SwordPose(FppCombatAction.Attack, 0.30f);
                    Vector3 localMid = pose.position + pose.rotation * Vector3.Lerp(FppHandsView.BladeBase, FppHandsView.BladeTip, 0.5f);
                    Transform target = _ar.SpawnedMob.transform;
                    target.rotation = _ar.ArCamera.transform.rotation;
                    target.position = _ar.ArCamera.transform.TransformPoint(localMid) - target.TransformVector(new Vector3(0, 0.33f, -0.13f));
                    Check(combat.BladeIntersects(_ar.SpawnedMob, 0.30f), "near target should intersect blade");
                    target.position += Vector3.right * 20f;
                    Check(!combat.BladeIntersects(_ar.SpawnedMob, 0.30f), "distant target incorrectly intersects blade");
                    int before = combat.HitCount;
                    _ar.Hud.ActionButtons[0].onClick.Invoke();
                    Check(combat.Timeline.Action == FppCombatAction.Attack, "Attack button not bound");
                    int swing = combat.Timeline.SwingId;
                    _ar.Hud.ActionButtons[0].onClick.Invoke();
                    _ar.Hud.ActionButtons[1].onClick.Invoke();
                    Check(combat.Timeline.SwingId == swing && combat.Timeline.Action == FppCombatAction.Attack, "tap spam restarted/overlapped attack");
                    yield return WaitUntil(() => combat.Timeline.Action == FppCombatAction.Idle, 3f);
                    Check(combat.HitCount == before, "attack hit a distant target");
                    target.position = _ar.ArCamera.transform.TransformPoint(localMid) - target.TransformVector(new Vector3(0, 0.33f, -0.13f));
                    _ar.Hud.ActionButtons[0].onClick.Invoke();
                    yield return WaitUntil(() => combat.Timeline.Action == FppCombatAction.Idle, 3f);
                    Check(combat.HitCount == before + 1, "near swing must hit exactly once");
                    before = combat.HitCount;
                    _ar.Hud.ActionButtons[1].onClick.Invoke();
                    Check(combat.Timeline.Action == FppCombatAction.Aura, "Aura button not bound");
                    yield return WaitUntil(() => combat.Timeline.Elapsed >= 0.4f, 3f);
                    Check(combat.Timeline.Action == FppCombatAction.Aura, "Aura cancelled too early");
                    Vector3 expectedLeft = combat.View.RightGrip.localPosition + combat.View.RightGrip.localRotation * new Vector3(0, -0.12f, 0);
                    Check(Vector3.Distance(combat.View.LeftHand.localPosition, expectedLeft) < 0.015f, "left hand did not grip hilt");
                    yield return WaitUntil(() => combat.Timeline.Action == FppCombatAction.Idle, 3f);
                    Check(combat.Timeline.AuraActive, "Aura blade effect missing after activation");
                    Check(combat.HitCount == before, "Aura must not deal hits");
                    Check(!combat.RequestAura(), "active Aura can be spammed");
                    _ar.FrameSourceOverride = frame => frame.SessionStatus = ArSessionStatus.SessionInitializing;
                    yield return Frames(2);
                    Check(!combat.Ready && !combat.Timeline.AuraActive && !combat.View.gameObject.activeSelf, "tracking loss did not cancel/hide FPP");
                    Check(!combat.RequestAttack() && !combat.RequestAura(), "actions accepted with tracking lost");
                }
                finally
                {
                    combat.CancelActions();
                    _ar.SpawnedMob.transform.SetPositionAndRotation(savedPosition, savedRotation);
                    _ar.FrameSourceOverride = savedFrameSource;
                }
            }

            private IEnumerator T11_ExitButtonReturnsToWorldMap()
            {
                if (_ar == null)
                {
                    Check(false, "no AR controller");
                    yield break;
                }

                _ar.Hud.ExitButton.onClick.Invoke();
                yield return WaitUntil(() => ActiveScenePath == ArEncounterScenes.WorldMapScenePath, 10f);
                yield return Frames(5);
                Check(ActiveScenePath == ArEncounterScenes.WorldMapScenePath, "not back on world map: " + ActiveScenePath);
                Check(FindAnyObjectByType<ArEncounterSceneController>() == null && MobCount() == 0, "AR objects left after exit");
                Check(!ArEncounterHandoff.HasPending, "handoff pending after exit");
                Check(FindAnyObjectByType<FppHandsView>() == null, "FPP view leaked after scene exit");
                PrototypeWorldPlacement placement = FindAnyObjectByType<PrototypeWorldPlacement>();
                yield return WaitUntil(() => placement != null && placement.IsInitialized, 10f);
                Check(placement != null && placement.IsInitialized, "world map did not re-initialize");
                Check(FindAnyObjectByType<ArEncounterLauncher>() != null, "launcher missing after return");
                _ar = null;
            }

            /// <summary>Synthetic frames through the controller's frame seam (deterministic, independent of XR Simulation plane discovery).</summary>
            private void SyntheticFrame(ArFrameSnapshot f)
            {
                f.SessionStatus = _syntheticStatus;
                if (_syntheticPhase == 1)
                {
                    f.Planes.Add(new ArPlaneObservation("synthetic-small", true, true, 0.2f, 0.2f, false));
                }
                else if (_syntheticPhase >= 2)
                {
                    f.Planes.Add(new ArPlaneObservation("synthetic-wall", false, true, 2f, 2f, false));
                    f.Planes.Add(new ArPlaneObservation("synthetic-floor", true, true, 2f, 3f, false));
                    f.HasTarget = true;
                    f.TargetPlaneId = "synthetic-floor";
                    f.TargetPose = new Pose(_syntheticTarget, _ar != null ? _ar.FacingCamera(_syntheticTarget) : Quaternion.identity);
                    f.TargetFromReticle = true;
                }
            }

            private IEnumerator T12_SyntheticFrames_ScanReadyPlacedSingleSpawn()
            {
                _syntheticPhase = 0;
                _syntheticStatus = ArSessionStatus.SessionTracking;
                _pendingOverride = SyntheticFrame;
                IEnumerator open = OpenArFromWorldMapPanel();
                while (open.MoveNext())
                {
                    yield return open.Current;
                }

                Check(_ar != null && _ar.FrameSourceOverride != null, "override not installed");
                if (_ar == null)
                {
                    yield break;
                }

                Transform cam = _ar.ArCamera.transform;
                Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                _syntheticTarget = cam.position + fwd * 1.5f + Vector3.down * 1.2f;
                yield return Frames(3);
                Check(_ar.State == ArEncounterState.Scanning, "phase0 state " + _ar.State);
                Check(!_ar.Hud.IsPlaceButtonInteractable, "phase0 place interactable");
                _ar.Hud.PlaceButton.onClick.Invoke();
                yield return Frames(1);
                Check(_ar.LastPlacementResult == ArPlacementResult.NotReady && MobCount() == 0, "phase0 placed: " + _ar.LastPlacementResult);

                _syntheticPhase = 1; // too small
                yield return Frames(3);
                Check(_ar.State == ArEncounterState.Scanning, "phase1 small plane state " + _ar.State);

                _syntheticPhase = 2; // suitable floor
                yield return Frames(3);
                Check(_ar.State == ArEncounterState.ReadyToPlace && _ar.HasPlacementTarget, "phase2 state " + _ar.State);
                Check(_ar.Hud.IsPlaceButtonInteractable, "phase2 place not interactable");
                Check(_ar.PlacementReticle.gameObject.activeSelf && Vector3.Distance(_ar.PlacementReticle.position, _syntheticTarget) < 0.01f, "reticle not at target");
                Check(MobCount() == 0, "spawned before confirm");

                _ar.Hud.PlaceButton.onClick.Invoke();
                yield return Frames(2);
                Check(_ar.State == ArEncounterState.Placed && _ar.SpawnCount == 1 && MobCount() == 1, "placement: state " + _ar.State + " mobs " + MobCount());
                Check(_ar.SpawnedMob != null && Vector3.Distance(_ar.SpawnedMob.transform.position, _syntheticTarget) < 0.01f, "mob not at synthetic ground pose");
                for (int i = 0; i < 5; i++)
                {
                    _ar.Hud.PlaceButton.onClick.Invoke();
                    Check(_ar.ConfirmPlacement() == ArPlacementResult.AlreadyPlaced, "repeat not AlreadyPlaced");
                    yield return null;
                }

                Check(MobCount() == 1 && _ar.SpawnCount == 1, "duplicate spawn: " + MobCount());

                // Tracking degrades after placement: the monster stays where it is.
                Vector3 placedAt = _ar.SpawnedMob.transform.position;
                _syntheticStatus = ArSessionStatus.SessionInitializing;
                yield return Frames(5);
                Check(_ar.State == ArEncounterState.Placed && _ar.Machine.IsTrackingLimited, "tracking-limited not reported");
                Check(_ar.Hud.StatusLabel != null && _ar.Hud.StatusLabel.StartsWith("Śledzenie ograniczone"), "status " + _ar.Hud.StatusLabel);
                Check(Vector3.Distance(_ar.SpawnedMob.transform.position, placedAt) < 0.001f && MobCount() == 1, "mob moved/duplicated during tracking loss");
                _syntheticStatus = ArSessionStatus.SessionTracking;
                yield return Frames(3);
                Check(!_ar.Machine.IsTrackingLimited, "tracking not restored");
                int iScan = IndexOf(ArEncounterState.Scanning);
                int iReady = IndexOf(ArEncounterState.ReadyToPlace);
                int iPlaced = IndexOf(ArEncounterState.Placed);
                Check(iScan >= 0 && iReady > iScan && iPlaced > iReady, "state order " + string.Join(",", _ar.StateHistory));
                Debug.Log("[ArSmoke] T12 states " + string.Join(",", _ar.StateHistory));

                _ar.Hud.ExitButton.onClick.Invoke();
                yield return WaitUntil(() => ActiveScenePath == ArEncounterScenes.WorldMapScenePath, 10f);
                yield return Frames(3);
                Check(ActiveScenePath == ArEncounterScenes.WorldMapScenePath, "exit failed");
            }

            private IEnumerator T13_UnsupportedStateIsClearAndNonCrashing()
            {
                _syntheticPhase = 0;
                _syntheticStatus = ArSessionStatus.Unsupported;
                _pendingOverride = SyntheticFrame;
                SceneManager.LoadScene(ArEncounterScenes.ArEncounterScenePath); // direct open (no world-map request)
                yield return WaitUntil(() => ActiveScenePath == ArEncounterScenes.ArEncounterScenePath, 10f);
                yield return Frames(5);
                Check(_ar != null, "AR controller missing");
                if (_ar == null)
                {
                    yield break;
                }

                Check(!_ar.RequestFromWorldMap && _ar.Request != null && _ar.Request.MobName == "Trujący Pająk", "direct-open fallback request " + _ar.Request);
                Check(_ar.State == ArEncounterState.Unsupported, "state " + _ar.State);
                Check(_ar.Hud.IsFailurePanelVisible && _ar.Hud.FailureTitle == "AR niedostępne" && !string.IsNullOrEmpty(_ar.Hud.FailureLabel), "failure panel " + _ar.Hud.FailureTitle);
                Check(!_ar.Hud.IsPlaceButtonVisible && !_ar.Hud.IsStatusVisible, "place/status still visible");
                Check(_ar.ConfirmPlacement() == ArPlacementResult.NotReady && MobCount() == 0, "placed while unsupported");

                // Later frames that look fine must not revive an unsupported device.
                _syntheticStatus = ArSessionStatus.SessionTracking;
                _syntheticPhase = 2;
                yield return Frames(5);
                Check(_ar.State == ArEncounterState.Unsupported && MobCount() == 0, "unsupported not terminal: " + _ar.State);

                _ar.Hud.FailureBackButton.onClick.Invoke();
                yield return WaitUntil(() => ActiveScenePath == ArEncounterScenes.WorldMapScenePath, 10f);
                yield return Frames(3);
                Check(ActiveScenePath == ArEncounterScenes.WorldMapScenePath, "failure back button did not return to the world map");
            }

            private IEnumerator T14_SessionStartTimeoutFailsThenRecovers()
            {
                _syntheticPhase = 0;
                _syntheticStatus = ArSessionStatus.None;
                _pendingOverride = SyntheticFrame;
                SceneManager.LoadScene(ArEncounterScenes.ArEncounterScenePath);
                yield return WaitUntil(() => ActiveScenePath == ArEncounterScenes.ArEncounterScenePath, 10f);
                yield return Frames(3);
                if (_ar == null)
                {
                    Check(false, "AR controller missing");
                    yield break;
                }

                Check(_ar.State == ArEncounterState.Starting, "state before timeout " + _ar.State);
                float timeout = _ar.Machine.Settings.startTimeoutSeconds;
                yield return WaitUntil(() => _ar.State == ArEncounterState.Failed, timeout + 5f);
                Check(_ar.State == ArEncounterState.Failed, "no failure after " + timeout + " s: " + _ar.State);
                Check(_ar.Hud.IsFailurePanelVisible && _ar.Hud.FailureTitle == "Błąd sesji AR" && !string.IsNullOrEmpty(_ar.Hud.FailureLabel), "failure panel");
                Check(_ar.ConfirmPlacement() == ArPlacementResult.NotReady && MobCount() == 0, "placed while failed");

                _syntheticStatus = ArSessionStatus.SessionTracking;
                yield return Frames(3);
                Check(_ar.State == ArEncounterState.Scanning && !_ar.Hud.IsFailurePanelVisible, "did not recover to scanning: " + _ar.State);

                _ar.Hud.ExitButton.onClick.Invoke();
                yield return WaitUntil(() => ActiveScenePath == ArEncounterScenes.WorldMapScenePath, 10f);
                Check(ActiveScenePath == ArEncounterScenes.WorldMapScenePath, "exit failed");
            }

            private IEnumerator T15_NoExceptionsLogged()
            {
                Check(_exceptions.Count == 0, "exceptions: " + string.Join(" | ", _exceptions));
                Debug.Log("[ArSmoke] logged errors (" + _errors.Count + "): " + string.Join(" | ", _errors));
                yield break;
            }

            private void WriteResultsAndExit()
            {
                int failures = 0;
                foreach (var r in _results)
                {
                    if (r.failure != null)
                    {
                        failures++;
                    }
                }

                var sb = new StringBuilder();
                sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
                sb.AppendLine("<testsuite name=\"ArEncounterSmokeTest\" tests=\"" + _results.Count + "\" failures=\"" + failures + "\">");
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
                Debug.Log("[ArSmoke] RESULT tests=" + _results.Count + " failures=" + failures + " -> " + ResultsPath);
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }
    }
}

