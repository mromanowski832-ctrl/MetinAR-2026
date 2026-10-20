using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MetinAR.ArEncounter;
using MetinAR.HandTracking;
using MetinAR.WorldMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using NQuaternion = System.Numerics.Quaternion;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using Object = UnityEngine.Object;

namespace MetinAR.Editor
{
    /// <summary>
    /// METINAR-007 real Play Mode smoke test of the hand-tracked AR sword inside the METINAR-006 encounter scene:
    /// the real camera → Sentis → landmarks pipeline running on XR Simulation camera frames, then (through the
    /// tracker's observation seam, the same downstream path as the camera) a synthetic hand that holds the sword,
    /// moves, disappears, sweeps slowly and swings fast through the ground-placed spider. Hits are observed through the
    /// <see cref="ArHandSword.SwordHit"/> event, the mob counter and the HUD toast, never through log text.
    /// Usage: Unity -batchmode -projectPath client -executeMethod MetinAR.Editor.ArSwordSmokeTest.Run
    /// Writes Builds/metinar-007-sword-smoke-results.xml and exits 0 on pass, 1 on failure.
    /// </summary>
    public static class ArSwordSmokeTest
    {
        private const string SessionKey = "MetinAR007.SwordSmokeResults";
        private const string PreKey = "MetinAR007.EditModeState";
        private const string SwordPrefabPath = "Assets/MetinAR/Prefabs/ArPrototypeSword.prefab";

        public static void Run()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string repoRoot = Directory.GetParent(projectRoot)!.FullName;
            string resultsPath = Path.Combine(repoRoot, "Builds", "metinar-007-sword-smoke-results.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(resultsPath)!);

            var sb = new StringBuilder();
            EditorSceneManager.OpenScene(ArEncounterScenes.ArEncounterScenePath, OpenSceneMode.Single);
            int missing = 0;
            foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
            {
                missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            }

            sb.Append("arMissing=").Append(missing);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SwordPrefabPath);
            int prefabMissing = -1;
            if (prefab != null)
            {
                prefabMissing = 0;
                foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
                {
                    prefabMissing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                }
            }

            sb.Append(";swordPrefabMissing=").Append(prefabMissing);
            ArHandTracker tracker = Object.FindAnyObjectByType<ArHandTracker>(FindObjectsInactive.Include);
            ArHandSword sword = Object.FindAnyObjectByType<ArHandSword>(FindObjectsInactive.Include);
            sb.Append(";trackerModels=").Append(tracker != null && tracker.PalmDetectorModel != null && tracker.HandLandmarkModel != null ? 1 : 0);
            sb.Append(";swordWired=").Append(sword != null && sword.SwordRoot != null && sword.BladeBase != null && sword.BladeTip != null
                                              && sword.Tracker == tracker ? 1 : 0);
            sb.Append(";swordColliders=").Append(prefab != null ? prefab.GetComponentsInChildren<Collider>(true).Length : -1);
            Debug.Log("[SwordSmoke] edit-mode " + sb);

            ArEncounterHandoff.Reset();
            SessionState.SetString(SessionKey, resultsPath);
            SessionState.SetString(PreKey, sb.ToString());
            EditorSceneManager.OpenScene(ArEncounterScenes.WorldMapScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
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
            var go = new GameObject("SwordSmokeTestRunner");
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
            private readonly HashSet<SwordHitDecision> _decisions = new HashSet<SwordHitDecision>();
            private string _currentFailure;

            private ArEncounterSceneController _ar;
            private ArHandTracker _tracker;
            private ArHandSword _sword;
            private ArHandDebugOverlay _overlay;
            private bool _installOverrides;
            private Vector3 _syntheticTarget;
            private int _hitEvents;
            private ArSwordHitInfo _lastEvent;
            private bool _pulseAtHit;

            // Synthetic hand (world space): grip position, blade direction and blade "up" as a function of Time.time.
            private bool _handVisible;
            private Func<float, (Vector3 grip, Vector3 blade, Vector3 up)> _pose;
            private Vector3 _grip;
            private Vector3 _aim;
            private Vector3 _sweepUp;
            private Vector3 _sweepNormal;
            private readonly NVector3[] _cameraPoints = new NVector3[HandLandmark.Count];
            private readonly NVector2[] _viewport = new NVector2[HandLandmark.Count];
            private readonly NVector3[] _world = new NVector3[HandLandmark.Count];

            private const float StartAngle = 100f;
            private const float EndAngle = -25f;

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

            private static IEnumerator Seconds(float s)
            {
                float end = Time.realtimeSinceStartup + s;
                while (Time.realtimeSinceStartup < end)
                {
                    yield return null;
                }
            }

            private static IEnumerator WaitUntil(Func<bool> condition, float seconds)
            {
                float end = Time.realtimeSinceStartup + seconds;
                while (!condition() && Time.realtimeSinceStartup < end)
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

            private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
            {
                if (scene.path != ArEncounterScenes.ArEncounterScenePath)
                {
                    return;
                }

                _ar = FindAnyObjectByType<ArEncounterSceneController>();
                _tracker = FindAnyObjectByType<ArHandTracker>();
                _sword = FindAnyObjectByType<ArHandSword>();
                _overlay = FindAnyObjectByType<ArHandDebugOverlay>();
                if (_installOverrides)
                {
                    if (_ar != null)
                    {
                        _ar.FrameSourceOverride = SyntheticFrame;
                    }

                    if (_tracker != null)
                    {
                        _tracker.ObservationOverride = HandOverride;
                    }
                }

                if (_sword != null)
                {
                    _sword.SwordHit += OnSwordHit;
                }
            }

            private void OnSwordHit(ArSwordHitInfo info)
            {
                _hitEvents++;
                _lastEvent = info;
                _pulseAtHit = _ar != null && _ar.SpawnedMob != null && _ar.SpawnedMob.IsHitPulseActive;
            }

            private void Update()
            {
                if (_sword != null)
                {
                    _decisions.Add(_sword.LastDecision);
                }
            }

            private void SyntheticFrame(ArFrameSnapshot f)
            {
                f.SessionStatus = ArSessionStatus.SessionTracking;
                f.Planes.Add(new ArPlaneObservation("synthetic-floor", true, true, 3f, 3f, false));
                f.HasTarget = true;
                f.TargetPlaneId = "synthetic-floor";
                f.TargetPose = new Pose(_syntheticTarget, _ar != null ? _ar.FacingCamera(_syntheticTarget) : Quaternion.identity);
                f.TargetFromReticle = true;
            }

            /// <summary>
            /// Builds a synthetic hand whose fist holds the sword at the requested world pose and reports it exactly as
            /// the camera pipeline would (viewport landmarks + metric world landmarks, relative to the AR camera).
            /// </summary>
            private void HandOverride(HandObservation o)
            {
                if (!_handVisible || _pose == null || _tracker == null || _tracker.ArCamera == null)
                {
                    return;
                }

                (Vector3 grip, Vector3 blade, Vector3 up) = _pose(Time.time);
                Transform cam = _tracker.ArCamera.transform;
                Quaternion toCamera = Quaternion.Inverse(cam.rotation);
                Vector3 gripCam = toCamera * (grip - cam.position);
                Quaternion desired = Quaternion.LookRotation(toCamera * blade, toCamera * up);

                // Hand-local sword frame used by the estimator (fist): blade = pinky MCP → index MCP, up = wrist → middle MCP.
                NVector3[] local = SyntheticHand.LocalPoints;
                Vector3 across = ToU(local[HandLandmark.IndexMcp] - local[HandLandmark.PinkyMcp]).normalized;
                Vector3 along = ToU(local[HandLandmark.MiddleMcp] - local[HandLandmark.Wrist]);
                along = (along - Vector3.Dot(along, across) * across).normalized;
                Quaternion handLocalSword = Quaternion.LookRotation(across, along);
                Quaternion hand = desired * Quaternion.Inverse(handLocalSword);

                SyntheticHand.CameraPoints(NVector3.Zero, ToN(hand), _cameraPoints);
                NVector3 gripLocal = SyntheticHand.GripCentre(_cameraPoints);
                NVector3 wrist = ToN(gripCam) - gripLocal;
                SyntheticHand.CameraPoints(wrist, ToN(hand), _cameraPoints);
                Matrix4x4 p = _tracker.ArCamera.projectionMatrix;
                SyntheticHand.Observe(_cameraPoints, new CameraProjection(p.m00, p.m11, p.m02, p.m12), _viewport, _world);
                Array.Copy(_viewport, o.Viewport, HandLandmark.Count);
                Array.Copy(_world, o.World, HandLandmark.Count);
                o.HasHand = true;
                o.Presence = 1f;
                o.Handedness = 1f;
                o.PalmScore = 1f;
            }

            private static Vector3 ToU(NVector3 v) => new Vector3(v.X, v.Y, v.Z);

            private static NVector3 ToN(Vector3 v) => new NVector3(v.x, v.y, v.z);

            private static NQuaternion ToN(Quaternion q) => new NQuaternion(q.x, q.y, q.z, q.w);

            private Vector3 BladeAt(float angleDegrees)
            {
                float a = angleDegrees * Mathf.Deg2Rad;
                return (Mathf.Cos(a) * _aim + Mathf.Sin(a) * _sweepUp).normalized;
            }

            private void HoldAt(float angle)
            {
                Vector3 g = _grip;
                Vector3 blade = BladeAt(angle);
                Vector3 up = _sweepNormal;
                _pose = _ => (g, blade, up);
            }

            private void SweepFrom(float from, float to, float seconds)
            {
                float t0 = Time.time;
                Vector3 g = _grip;
                Vector3 up = _sweepNormal;
                _pose = t => (g, BladeAt(Mathf.Lerp(from, to, Mathf.Clamp01((t - t0) / seconds))), up);
            }

            private IEnumerator Start()
            {
                yield return Frames(5);
                var tests = new (string, Func<IEnumerator>)[]
                {
                    ("S01_SceneWiringModelsAndNoMissingScripts", S01_SceneWiringModelsAndNoMissingScripts),
                    ("S02_RealCameraPipelineRunsOnXrSimulationFrames", S02_RealCameraPipelineRunsOnXrSimulationFrames),
                    ("S03_SpiderPlacedOnGroundWithPlaceButton", S03_SpiderPlacedOnGroundWithPlaceButton),
                    ("S04_NoHandMeansNoSword", S04_NoHandMeansNoSword),
                    ("S05_SwordAttachedToTrackedHandInWorldSpace", S05_SwordAttachedToTrackedHandInWorldSpace),
                    ("S06_SwordFollowsHandTranslationAndRotation", S06_SwordFollowsHandTranslationAndRotation),
                    ("S07_SwordIsNotCameraFixed", S07_SwordIsNotCameraFixed),
                    ("S08_TrackingLostHidesSwordAfterHold", S08_TrackingLostHidesSwordAfterHold),
                    ("S09_SlowSweepThroughSpiderDoesNotHit", S09_SlowSweepThroughSpiderDoesNotHit),
                    ("S10_FastSwingHitsSpiderOnce", S10_FastSwingHitsSpiderOnce),
                    ("S11_SameSwingNoRepeatThenNewSwingHits", S11_SameSwingNoRepeatThenNewSwingHits),
                    ("S12_TrackingLossDuringSwingDoesNotHit", S12_TrackingLossDuringSwingDoesNotHit),
                    ("S13_ExitReturnsToWorldMapAndStopsTracking", S13_ExitReturnsToWorldMapAndStopsTracking),
                    ("S14_NoExceptionsLogged", S14_NoExceptionsLogged),
                };

                foreach (var (name, test) in tests)
                {
                    _currentFailure = null;
                    yield return RunGuarded(test());
                    _results.Add((name, _currentFailure));
                    Debug.Log("[SwordSmoke] " + (_currentFailure == null ? "PASS " : "FAIL ") + name
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

            private static int MobCount() => FindObjectsByType<ArEncounterMob>(FindObjectsInactive.Include).Length;

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

            private IEnumerator OpenArFromWorldMapPanel()
            {
                PrototypeWorldPlacement placement = FindAnyObjectByType<PrototypeWorldPlacement>();
                yield return WaitUntil(() => placement != null && placement.IsInitialized, 10f);
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
            }

            private IEnumerator ExitToWorldMap()
            {
                if (_ar == null)
                {
                    yield break;
                }

                _ar.Hud.ExitButton.onClick.Invoke();
                yield return WaitUntil(() => ActiveScenePath == ArEncounterScenes.WorldMapScenePath, 10f);
                yield return Frames(5);
            }

            private bool Ready => _ar != null && _tracker != null && _sword != null && _ar.SpawnedMob != null && _ar.State == ArEncounterState.Placed;

            // ---------------------------------------------------------------- tests

            private IEnumerator S01_SceneWiringModelsAndNoMissingScripts()
            {
                Check(EditState("arMissing", "0"), "AR scene missing scripts: " + EditModeState);
                Check(EditState("swordPrefabMissing", "0"), "sword prefab missing / missing scripts: " + EditModeState);
                Check(EditState("trackerModels", "1"), "tracker models not assigned: " + EditModeState);
                Check(EditState("swordWired", "1"), "sword not wired: " + EditModeState);
                Check(EditState("swordColliders", "0"), "sword prefab has physics colliders: " + EditModeState);
                Check(ActiveScenePath == ArEncounterScenes.WorldMapScenePath, "play mode did not start on the world map: " + ActiveScenePath);
                yield break;
            }

            private IEnumerator S02_RealCameraPipelineRunsOnXrSimulationFrames()
            {
                _installOverrides = false;
                yield return OpenArFromWorldMapPanel();
                Check(_ar != null && _tracker != null && _sword != null && _overlay != null, "hand tracking objects missing in AR scene");
                if (_tracker == null || _ar == null)
                {
                    yield break;
                }

                Check(RuntimeMissingScripts() == 0, "AR runtime missing scripts");
                Check(_tracker.ObservationOverride == null, "override unexpectedly installed");
                yield return WaitUntil(() => _tracker.InferenceCount >= 5 || _tracker.LastError != null, 45f);
                Check(_tracker.ModelsLoaded, "Sentis models not loaded: " + _tracker.LastError);
                Check(_tracker.CpuImageCount > 0, "no AR camera CPU image acquired (XR Simulation)");
                Check(_tracker.InferenceCount >= 5, "inference did not run on camera frames: " + _tracker.InferenceCount + " error=" + _tracker.LastError);
                Check(_tracker.LastError == null, "tracker error: " + _tracker.LastError);
                Check(_tracker.LastImageSize.x > 0 && Mathf.Max(_tracker.LastImageSize.x, _tracker.LastImageSize.y) <= 640, "image size " + _tracker.LastImageSize);
                Check(_tracker.PalmDetectorRuns > 0, "palm detector never ran");
                Check(_tracker.Latest.Source == "camera", "latest observation source " + _tracker.Latest.Source);
                yield return Seconds(4f); // let the one-shot mapping probe run (needs ≥ 3 s in the scene)
                if (_tracker.HandFrames == 0)
                {
                    Check(!_sword.IsSwordVisible, "sword visible although no hand was detected");
                    Check(_overlay.IsVisible && _overlay.StatusText == ArHandDebugOverlay.NoHandText, "overlay status '" + _overlay.StatusText + "'");
                    Check(_overlay.VisibleDotCount == 0, "landmark dots visible without a hand");
                }

                Debug.Log("[SwordSmoke] S02 backend=" + _tracker.BackendName + " cpuImages=" + _tracker.CpuImageCount + " inferences=" + _tracker.InferenceCount
                          + " detectorRuns=" + _tracker.PalmDetectorRuns + " handFrames=" + _tracker.HandFrames + " lastMs=" + _tracker.LastInferenceMilliseconds.ToString("0.0")
                          + " image=" + _tracker.LastImageSize + " displayMatrix=" + _tracker.HasDisplayMatrix + " probe='" + _tracker.MappingProbeResult + "'"
                          + " state=" + _ar.State);
                yield return ExitToWorldMap();
                Check(ActiveScenePath == ArEncounterScenes.WorldMapScenePath, "exit after real pipeline failed");
            }

            private IEnumerator S03_SpiderPlacedOnGroundWithPlaceButton()
            {
                _installOverrides = true;
                _handVisible = false;
                yield return OpenArFromWorldMapPanel();
                if (_ar == null || _tracker == null || _sword == null)
                {
                    Check(false, "AR / hand objects missing");
                    yield break;
                }

                Check(_tracker.ObservationOverride != null && _ar.FrameSourceOverride != null, "overrides not installed");
                Transform cam = _ar.ArCamera.transform;
                Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                if (fwd.sqrMagnitude < 0.5f)
                {
                    fwd = Vector3.forward;
                }

                _syntheticTarget = cam.position + fwd * 1.4f + Vector3.down * 1.1f;
                yield return WaitUntil(() => _ar.State == ArEncounterState.ReadyToPlace, 10f);
                Check(_ar.State == ArEncounterState.ReadyToPlace, "not ready to place: " + _ar.State);
                _ar.Hud.PlaceButton.onClick.Invoke();
                yield return Frames(3);
                Check(_ar.State == ArEncounterState.Placed && _ar.SpawnCount == 1 && MobCount() == 1, "placement: " + _ar.State + " mobs " + MobCount());
                ArEncounterMob mob = _ar.SpawnedMob;
                Check(mob != null && Vector3.Distance(mob.transform.position, _syntheticTarget) < 0.01f, "spider not on synthetic ground");
                if (mob == null)
                {
                    yield break;
                }

                Check(mob.HpLabel == "HP 120/120" && mob.SwordHitCount == 0, "spider state " + mob.HpLabel + " hits " + mob.SwordHitCount);

                // Arm's-length grip in front of the spider's nearest hit-box point, sweeping in the vertical plane toward it.
                Transform t = mob.transform;
                Vector3 local = t.InverseTransformPoint(cam.position);
                Vector3 min = mob.HitVolumeCenter - mob.HitVolumeHalfExtents;
                Vector3 max = mob.HitVolumeCenter + mob.HitVolumeHalfExtents;
                Vector3 nearest = t.TransformPoint(Vector3.Max(min, Vector3.Min(max, local)));
                _grip = nearest + (cam.position - nearest).normalized * 0.45f;
                _aim = (nearest - _grip).normalized;
                _sweepUp = Vector3.ProjectOnPlane(Vector3.up, _aim);
                _sweepUp = _sweepUp.sqrMagnitude > 1e-4f ? _sweepUp.normalized : cam.up;
                _sweepNormal = Vector3.Cross(_aim, _sweepUp).normalized;
                float gripDistance = Vector3.Distance(_grip, cam.position);
                Check(gripDistance > 0.25f && gripDistance < 0.8f, "grip not at arm's length: " + gripDistance);
                Debug.Log("[SwordSmoke] S03 spider=" + mob.transform.position + " nearest=" + nearest + " grip=" + _grip + " gripFromCamera=" + gripDistance.ToString("0.00"));
            }

            private IEnumerator S04_NoHandMeansNoSword()
            {
                if (!Ready)
                {
                    Check(false, "precondition: spider not placed");
                    yield break;
                }

                _handVisible = false;
                yield return Seconds(0.6f);
                Check(!_sword.IsSwordVisible, "sword visible without a hand");
                Check(_overlay != null && _overlay.IsVisible && _overlay.StatusText == ArHandDebugOverlay.NoHandText, "overlay '" + _overlay?.StatusText + "'");
                Check(_overlay != null && _overlay.VisibleDotCount == 0, "dots without hand");
                Check(_sword.HitCount == 0 && _hitEvents == 0, "hit without a hand");
            }

            private IEnumerator S05_SwordAttachedToTrackedHandInWorldSpace()
            {
                if (!Ready)
                {
                    Check(false, "precondition");
                    yield break;
                }

                HoldAt(StartAngle);
                _handVisible = true;
                yield return Seconds(0.8f);
                Check(_sword.IsSwordVisible && _sword.IsHolding == false, "sword not shown for a tracked hand");
                CheckSwordPose(_grip, BladeAt(StartAngle), "S05");
                Check(_sword.SwordRoot.parent != _ar.ArCamera.transform && !_sword.SwordRoot.IsChildOf(_ar.ArCamera.transform), "sword parented to the camera");
                float bladeLength = Vector3.Distance(_sword.BladeBase.position, _sword.BladeTip.position);
                Check(Mathf.Abs(bladeLength - 0.75f) < 0.01f, "blade length " + bladeLength);
                Check(_overlay.StatusText != null && _overlay.StatusText.StartsWith("Ręka: wykryta"), "overlay '" + _overlay.StatusText + "'");
                Check(_overlay.VisibleDotCount == HandLandmark.Count, "dots " + _overlay.VisibleDotCount);
                Check(_sword.ContactFrames == 0 && _sword.HitCount == 0, "contact at the start pose (frames " + _sword.ContactFrames + ")");
            }

            private void CheckSwordPose(Vector3 expectedGrip, Vector3 expectedBlade, string label)
            {
                float d = Vector3.Distance(_sword.SwordRoot.position, expectedGrip);
                float a = Vector3.Angle(_sword.SwordRoot.forward, expectedBlade);
                Debug.Log("[SwordSmoke] " + label + " sword error " + (d * 100f).ToString("0.0") + " cm, " + a.ToString("0.0") + " deg");
                Check(d < 0.03f, label + ": sword grip " + (d * 100f).ToString("0.0") + " cm from the hand");
                Check(a < 4f, label + ": blade direction off by " + a.ToString("0.0") + " deg");
            }

            private IEnumerator S06_SwordFollowsHandTranslationAndRotation()
            {
                if (!Ready)
                {
                    Check(false, "precondition");
                    yield break;
                }

                Vector3 original = _grip;
                Vector3 moved = _grip + _ar.ArCamera.transform.right * 0.15f + Vector3.up * 0.05f;
                Vector3 blade = Quaternion.AngleAxis(40f, _sweepNormal) * BladeAt(StartAngle);
                Vector3 up = _sweepNormal;
                _pose = _ => (moved, blade, up);
                yield return Seconds(0.8f);
                CheckSwordPose(moved, blade, "S06 moved");
                _grip = original;
                HoldAt(StartAngle);
                yield return Seconds(0.8f);
                CheckSwordPose(_grip, BladeAt(StartAngle), "S06 back");
                Check(_sword.HitCount == 0, "hit while repositioning");
            }

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

            private IEnumerator S07_SwordIsNotCameraFixed()
            {
                if (!Ready)
                {
                    Check(false, "precondition");
                    yield break;
                }

                Transform simCam = FindSimulationCamera();
                Check(simCam != null, "XR Simulation camera not found");
                if (simCam == null)
                {
                    yield break;
                }

                Transform cam = _ar.ArCamera.transform;
                Vector3 camStart = cam.position;
                Vector3 swordInCamBefore = cam.InverseTransformPoint(_sword.SwordRoot.position);
                Vector3 simPos = simCam.position;
                Quaternion simRot = simCam.rotation;
                simCam.SetPositionAndRotation(simPos + simCam.right * 0.25f, Quaternion.AngleAxis(-12f, Vector3.up) * simRot);
                yield return Seconds(1.0f);
                float camMoved = Vector3.Distance(cam.position, camStart);
                Vector3 swordInCamAfter = cam.InverseTransformPoint(_sword.SwordRoot.position);
                Check(camMoved > 0.15f, "AR camera did not follow the simulated device: moved " + camMoved);
                CheckSwordPose(_grip, BladeAt(StartAngle), "S07 camera moved");
                Check(Vector3.Distance(swordInCamBefore, swordInCamAfter) > 0.15f, "sword moved with the camera (camera-fixed)");
                simCam.SetPositionAndRotation(simPos, simRot);
                yield return Seconds(1.0f);
                CheckSwordPose(_grip, BladeAt(StartAngle), "S07 camera back");
                Check(_sword.HitCount == 0, "hit while the camera moved");
            }

            private IEnumerator S08_TrackingLostHidesSwordAfterHold()
            {
                if (!Ready)
                {
                    Check(false, "precondition");
                    yield break;
                }

                _handVisible = false;
                yield return Seconds(0.08f);
                Check(_sword.IsSwordVisible, "sword vanished instantly on a single lost frame (no hold)");
                yield return Seconds(0.6f);
                Check(!_sword.IsSwordVisible, "sword still visible after tracking was lost");
                Check(_overlay.StatusText == ArHandDebugOverlay.NoHandText, "overlay '" + _overlay.StatusText + "'");
                _handVisible = true;
                yield return Seconds(0.6f);
                Check(_sword.IsSwordVisible, "sword not back after the hand reappeared");
                CheckSwordPose(_grip, BladeAt(StartAngle), "S08 reacquired");
            }

            private IEnumerator S09_SlowSweepThroughSpiderDoesNotHit()
            {
                if (!Ready)
                {
                    Check(false, "precondition");
                    yield break;
                }

                HoldAt(StartAngle);
                yield return Seconds(0.5f);
                int contactsBefore = _sword.ContactFrames;
                _decisions.Clear();
                SweepFrom(StartAngle, EndAngle, 5f);
                yield return Seconds(5.3f);
                Check(_sword.ContactFrames > contactsBefore, "slow sweep never touched the spider's hit box");
                Check(_decisions.Contains(SwordHitDecision.RejectedTooSlow), "TooSlow never decided: " + string.Join(",", _decisions));
                Check(_sword.HitCount == 0 && _hitEvents == 0 && _ar.SpawnedMob.SwordHitCount == 0, "slow sweep counted as a hit");
                SweepFrom(EndAngle, StartAngle, 5f);
                yield return Seconds(5.3f);
                Check(_sword.HitCount == 0, "slow return counted as a hit");
                Debug.Log("[SwordSmoke] S09 contactFrames=" + (_sword.ContactFrames - contactsBefore) + " decisions=" + string.Join(",", _decisions));
            }

            private IEnumerator S10_FastSwingHitsSpiderOnce()
            {
                if (!Ready)
                {
                    Check(false, "precondition");
                    yield break;
                }

                HoldAt(StartAngle);
                yield return Seconds(0.6f);
                ArEncounterMob mob = _ar.SpawnedMob;
                int before = _hitEvents;
                SweepFrom(StartAngle, EndAngle, 0.3f);
                yield return Seconds(0.25f);
                yield return Seconds(0.5f);
                Check(_hitEvents == before + 1, "fast swing hit events " + (_hitEvents - before));
                Check(_sword.HitCount == 1 && mob.SwordHitCount == 1, "hit counters " + _sword.HitCount + "/" + mob.SwordHitCount);
                Check(_lastEvent.MobName == "Trujący Pająk" && _lastEvent.HitNumber == 1, "hit info " + _lastEvent.MobName + " #" + _lastEvent.HitNumber);
                Check(_lastEvent.TipSpeed >= 1.2f, "hit tip speed " + _lastEvent.TipSpeed);
                Check(_pulseAtHit, "spider hit feedback (pulse) not active at the hit");
                Check(mob.HpLabel == "HP 120/120" && Mathf.Abs(mob.HpFillFraction - 1f) < 1e-4f, "HP changed (no damage formula yet): " + mob.HpLabel);
                Check(_ar.Hud.LastToastMessage == ArHandSword.DefaultHitToast, "toast '" + _ar.Hud.LastToastMessage + "'");
                Check(_ar.State == ArEncounterState.Placed && MobCount() == 1 && _ar.SpawnCount == 1, "encounter changed by the hit");
                Debug.Log("[SwordSmoke] S10 hit tipSpeed=" + _lastEvent.TipSpeed.ToString("0.00") + " m/s");
                SweepFrom(EndAngle, StartAngle, 5f);
                yield return Seconds(5.3f);
                Check(_sword.HitCount == 1, "slow return produced a hit");
            }

            private IEnumerator S11_SameSwingNoRepeatThenNewSwingHits()
            {
                if (!Ready)
                {
                    Check(false, "precondition");
                    yield break;
                }

                HoldAt(StartAngle);
                yield return Seconds(1.0f);
                int before = _sword.HitCount;
                _decisions.Clear();

                // One continuous fast back-and-forth swing through the spider (never pausing): exactly one hit.
                float t0 = Time.time;
                Vector3 g = _grip;
                Vector3 up = _sweepNormal;
                const float leg = 0.3f;
                _pose = t =>
                {
                    float phase = Mathf.Clamp((t - t0) / leg, 0f, 4f);
                    float k = Mathf.PingPong(phase, 1f);
                    return (g, BladeAt(Mathf.Lerp(StartAngle, EndAngle, k)), up);
                };
                yield return Seconds(4 * leg + 0.1f);
                HoldAt(StartAngle);
                Check(_sword.HitCount == before + 1, "continuous swing hits " + (_sword.HitCount - before) + " (expected 1)");
                Check(_decisions.Contains(SwordHitDecision.RejectedSameSwing) || _decisions.Contains(SwordHitDecision.RejectedCooldown),
                    "repeat contact was not gated: " + string.Join(",", _decisions));

                // Stop, then a new fast swing after the cooldown: a second hit.
                yield return Seconds(1.0f);
                SweepFrom(StartAngle, EndAngle, 0.3f);
                yield return Seconds(0.8f);
                Check(_sword.HitCount == before + 2, "new swing did not hit: " + (_sword.HitCount - before));
                Check(_ar.SpawnedMob.SwordHitCount == _sword.HitCount && _hitEvents == _sword.HitCount, "event/mob counters disagree");
                Check(_ar.SpawnedMob.HpLabel == "HP 120/120", "HP changed: " + _ar.SpawnedMob.HpLabel);
                SweepFrom(EndAngle, StartAngle, 5f);
                yield return Seconds(5.3f);
                Debug.Log("[SwordSmoke] S11 decisions=" + string.Join(",", _decisions));
            }

            private IEnumerator S12_TrackingLossDuringSwingDoesNotHit()
            {
                if (!Ready)
                {
                    Check(false, "precondition");
                    yield break;
                }

                HoldAt(StartAngle);
                yield return Seconds(1.0f);
                int before = _sword.HitCount;
                _decisions.Clear();

                // The hand disappears for the part of a fast swing that would cross the spider and reappears inside it.
                SweepFrom(StartAngle, EndAngle, 0.6f);
                yield return Seconds(0.12f);
                _handVisible = false;
                yield return Seconds(0.6f);
                _handVisible = true;
                yield return Seconds(0.6f);
                Check(_sword.HitCount == before, "hit through a tracking gap: " + (_sword.HitCount - before));
                Check(_decisions.Contains(SwordHitDecision.RejectedJustAcquired) || _decisions.Contains(SwordHitDecision.RejectedNotTracked)
                      || _decisions.Contains(SwordHitDecision.RejectedTrackingJump), "gap not gated: " + string.Join(",", _decisions));
                SweepFrom(EndAngle, StartAngle, 5f);
                yield return Seconds(5.3f);
                Check(_sword.HitCount == before, "slow return hit");
                Debug.Log("[SwordSmoke] S12 decisions=" + string.Join(",", _decisions));
            }

            private IEnumerator S13_ExitReturnsToWorldMapAndStopsTracking()
            {
                if (_ar == null)
                {
                    Check(false, "no AR controller");
                    yield break;
                }

                yield return ExitToWorldMap();
                Check(ActiveScenePath == ArEncounterScenes.WorldMapScenePath, "not back on world map: " + ActiveScenePath);
                Check(FindAnyObjectByType<ArHandTracker>() == null && FindAnyObjectByType<ArHandSword>() == null && MobCount() == 0,
                    "hand tracking / sword / mob left after exit");
                PrototypeWorldPlacement placement = FindAnyObjectByType<PrototypeWorldPlacement>();
                yield return WaitUntil(() => placement != null && placement.IsInitialized, 10f);
                Check(placement != null && placement.IsInitialized, "world map did not re-initialize");
                _ar = null;
                _tracker = null;
                _sword = null;
            }

            private IEnumerator S14_NoExceptionsLogged()
            {
                Check(_exceptions.Count == 0, "exceptions: " + string.Join(" | ", _exceptions));
                Debug.Log("[SwordSmoke] logged errors (" + _errors.Count + "): " + string.Join(" | ", _errors));
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
                sb.AppendLine("<testsuite name=\"ArSwordSmokeTest\" tests=\"" + _results.Count + "\" failures=\"" + failures + "\">");
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
                Debug.Log("[SwordSmoke] RESULT tests=" + _results.Count + " failures=" + failures + " -> " + ResultsPath);
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }
    }
}
