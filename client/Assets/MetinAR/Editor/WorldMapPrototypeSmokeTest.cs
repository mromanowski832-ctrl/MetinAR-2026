using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MetinAR.Location;
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

        public static void Run()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string repoRoot = Directory.GetParent(projectRoot)!.FullName;
            string resultsPath = Path.Combine(repoRoot, "Builds", "metinar-004a-smoke-results.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(resultsPath)!);
            SessionState.SetString(SessionKey, resultsPath);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
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
            var go = new GameObject("SmokeTestRunner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>().ResultsPath = results;
        }

        private sealed class Runner : MonoBehaviour
        {
            public string ResultsPath;

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
                    A_SceneLoadsAndCoreObjectsExist(),
                    B_MovementPathMovesPlayerAndUpdatesDistance(),
                    C_TurnPathRotatesAvatarAndCamera(),
                    D_MobClickOpensInfoPanel(),
                    E_EnterArButtonEmitsEncounterRequest(),
                    F_AggroTriggersOnceInsideRange(),
                    G_HudButtonsOpenAndClosePanels(),
                };
                string[] names =
                {
                    "A_SceneLoadsAndCoreObjectsExist",
                    "B_MovementPathMovesPlayerAndUpdatesDistance",
                    "C_TurnPathRotatesAvatarAndCamera",
                    "D_MobClickOpensInfoPanel",
                    "E_EnterArButtonEmitsEncounterRequest",
                    "F_AggroTriggersOnceInsideRange",
                    "G_HudButtonsOpenAndClosePanels",
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

                _controller.MobInfoPanel.CloseButton.onClick.Invoke();
                yield return Frames(2);
                Check(!_controller.MobInfoPanel.gameObject.activeSelf, "mob panel close button did not close");
            }

            private IEnumerator F_AggroTriggersOnceInsideRange()
            {
                int encounterStart = _controller.EncounterRequestCount;
                int attackStart = _controller.AttackedCount;

                // Walk forward 0.5 s (4 m) -> back near origin, still > 12 m from mob.
                _movement.ApplyMoveInput(new Vector2(0f, 1f), 0.5f);
                yield return Frames(10);
                float outside = _mob.DistanceToPlayerMeters;
                Check(outside > _mob.AggroRangeMeters, "expected outside aggro range, got " + outside);
                Check(_controller.AttackedCount == attackStart, "attacked while outside range");
                Check(_controller.EncounterRequestCount == encounterStart, "encounter requested while outside range");

                // Walk forward 0.75 s (6 m) -> inside 12 m.
                _movement.ApplyMoveInput(new Vector2(0f, 1f), 0.75f);
                yield return Frames(3);
                float inside = _mob.DistanceToPlayerMeters;
                Check(inside <= _mob.AggroRangeMeters, "expected inside aggro range, got " + inside);
                Check(_controller.AttackedCount == attackStart + 1, "attack count " + (_controller.AttackedCount - attackStart));
                Check(_controller.EncounterRequestCount == encounterStart + 1, "encounter count " + (_controller.EncounterRequestCount - encounterStart));
                Check(_controller.LastEncounterReason == "aggro:Trujący Pająk", "reason " + _controller.LastEncounterReason);
                Check(_controller.IsToastVisible && _controller.LastToastMessage == "Zostałeś zaatakowany", "toast " + _controller.LastToastMessage);

                yield return Frames(60);
                Check(_controller.AttackedCount == attackStart + 1, "attack repeated over frames: " + (_controller.AttackedCount - attackStart));
                Check(_controller.EncounterRequestCount == encounterStart + 1, "encounter repeated over frames: " + (_controller.EncounterRequestCount - encounterStart));
                Debug.Log("[SmokeTest] F outside=" + outside + " inside=" + inside);
            }

            private IEnumerator G_HudButtonsOpenAndClosePanels()
            {
                var cases = new (string label, Button button, GameObject panel)[]
                {
                    ("EQ", _bar.EqButton, _controller.InventoryPanel.gameObject),
                    ("Postać", _bar.CharacterButton, _controller.CharacterPanel.gameObject),
                    ("Skille", _bar.SkillsButton, _controller.SkillsPanel.gameObject),
                    ("Ustawienia", _bar.SettingsButton, _controller.SettingsPanel.gameObject),
                    ("Menu", _bar.HamburgerButton, _controller.HamburgerPanel.gameObject),
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
