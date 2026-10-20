using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MetinAR.Location;
using MetinAR.Map;
using MetinAR.Map.Runtime;
using MetinAR.WorldMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MetinAR.Editor
{
    /// <summary>
    /// Play Mode smoke test for the METINAR-008B OSM map POC scene (live OpenFreeMap tiles at the public
    /// demo landmark; no GPS, no owner location).
    /// Usage: Unity -batchmode -projectPath client -executeMethod MetinAR.Editor.OsmMapPocSmokeTest.Run
    /// Writes Builds/metinar-008b-smoke-results.xml and exits 0 on pass, 1 on failure.
    /// </summary>
    public static class OsmMapPocSmokeTest
    {
        private const string SessionKey = "MetinAR008B.SmokeResults";
        private const string MissingKey = "MetinAR008B.MissingScripts";

        public static void Run()
        {
            string repoRoot = Directory.GetParent(Directory.GetParent(Application.dataPath)!.FullName)!.FullName;
            string resultsPath = Path.Combine(repoRoot, "Builds", "metinar-008b-smoke-results.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(resultsPath)!);
            EditorSceneManager.OpenScene(OsmMapPocSceneBuilder.ScenePath, OpenSceneMode.Single);
            SessionState.SetInt(MissingKey, CountMissingScripts());
            SessionState.SetString(SessionKey, resultsPath);
            EditorApplication.isPlaying = true;
        }

        /// <summary>Missing-script count over every GameObject of the open scene (including inactive).</summary>
        public static int CountMissingScripts()
        {
            int missing = 0;
            foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                }
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
            var go = new GameObject("OsmSmokeTestRunner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            Runner runner = go.AddComponent<Runner>();
            runner.ResultsPath = results;
            runner.MissingScripts = SessionState.GetInt(MissingKey, -1);
            SessionState.EraseInt(MissingKey);
        }

        private sealed class Runner : MonoBehaviour
        {
            public string ResultsPath;
            public int MissingScripts;

            private readonly List<(string name, string failure)> _results = new List<(string, string)>();
            private readonly Dictionary<string, string> _props = new Dictionary<string, string>();
            private readonly List<string> _logs = new List<string>();
            private string _current;
            private string _currentFailure;

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
                Debug.Log("[OsmSmoke] " + (_currentFailure == null ? "PASS " : "FAIL ") + _current
                          + (_currentFailure == null ? string.Empty : " :: " + _currentFailure));
            }

            private void OnEnable()
            {
                Application.logMessageReceivedThreaded += OnLog;
            }

            private void OnDisable()
            {
                Application.logMessageReceivedThreaded -= OnLog;
            }

            private void OnLog(string condition, string stackTrace, LogType type)
            {
                lock (_logs)
                {
                    _logs.Add(type + ": " + condition);
                }
            }

            private IEnumerator Start()
            {
                yield return null;
                IEnumerator body = Body();
                while (true)
                {
                    object current;
                    try
                    {
                        if (!body.MoveNext())
                        {
                            break;
                        }

                        current = body.Current;
                    }
                    catch (Exception e)
                    {
                        Check(false, "exception: " + e.GetType().Name + " " + e.Message);
                        End();
                        break;
                    }

                    yield return current;
                }

                WriteResultsAndExit();
            }

            private IEnumerator Body()
            {
                Begin("scene_has_no_missing_scripts");
                Check(MissingScripts == 0, "missing scripts: " + MissingScripts);
                End();

                var bootstrap = FindFirstObjectByType<OsmPocBootstrap>();
                var location = FindFirstObjectByType<PlayerLocationService>();
                var origin = FindFirstObjectByType<WorldMapOrigin>();
                var placement = FindFirstObjectByType<PrototypeWorldPlacement>();
                var map = FindFirstObjectByType<OsmMapView>();
                var rig = FindFirstObjectByType<MapCameraRig>();
                var attribution = FindFirstObjectByType<MapAttributionView>();
                var diagnostics = FindFirstObjectByType<OsmPocDiagnostics>();
                var avatar = FindFirstObjectByType<PlayerMapAvatar>();

                Begin("scene_wiring_present");
                Check(bootstrap && location && origin && placement && map && rig && attribution && diagnostics && avatar,
                    "a required component is missing");
                End();
                if (_currentFailure != null)
                {
                    yield break;
                }

                for (int i = 0; i < 120 && !placement.IsInitialized; i++)
                {
                    yield return null;
                }

                Begin("demo_landmark_origin_and_placement");
                Check(bootstrap.CurrentMode == OsmPocBootstrap.Mode.Demo, "bootstrap not in demo mode in Editor");
                Check(origin.HasOrigin, "origin not captured");
                Check(placement.IsInitialized, "placement not initialized");
                if (origin.HasOrigin)
                {
                    GeoCoordinate o = origin.Origin;
                    GeoCoordinate d = OsmPocBootstrap.DemoCoordinate;
                    Check(Math.Abs(o.Latitude - d.Latitude) < 1e-9 && Math.Abs(o.Longitude - d.Longitude) < 1e-9,
                        "origin is not the public demo landmark");
                }

                var entities = FindObjectsByType<WorldMapEntity>(FindObjectsSortMode.None);
                Check(entities.Length == 2, "expected mob + NPC entities, got " + entities.Length);
                foreach (WorldMapEntity e in entities)
                {
                    Check(e.gameObject.activeInHierarchy && e.IsPlaced, e.name + " not placed");
                    Vector3 expected = origin.WorldToLocal(e.Coordinate, e.transform.position.y);
                    Check((expected - e.transform.position).sqrMagnitude < 1e-4f, e.name + " transform != WorldMapOrigin projection");
                }

                End();

                Begin("local_frame_matches_world_map_origin");
                var frame = new LocalTangentFrame(origin.Origin);
                double worst = 0;
                foreach (var (n, e) in new[] { (0.0, 0.0), (300.0, 0.0), (0.0, -300.0), (-450.0, 520.0), (700.0, 700.0) })
                {
                    GeoCoordinate p = GeoMath.OffsetMeters(origin.Origin, n, e);
                    frame.ToLocalMeters(p, out double fe, out double fn);
                    origin.GetLocalMeters(p, out double on, out double oe);
                    worst = Math.Max(worst, Math.Max(Math.Abs(fe - oe), Math.Abs(fn - on)));
                }

                _props["frameParityWorstMeters"] = worst.ToString("E2");
                Check(worst < 1e-6, "LocalTangentFrame differs from WorldMapOrigin by " + worst + " m");
                End();

                float t0 = Time.realtimeSinceStartup;
                while (!map.IsInitialAreaReady && !map.IsSourceFailed && Time.realtimeSinceStartup - t0 < 90f)
                {
                    yield return null;
                }

                _props["initialAreaWaitSeconds"] = (Time.realtimeSinceStartup - t0).ToString("F1");
                _props["mapStats"] = map.Stats.Summary();
                Begin("osm_tiles_load_and_mesh");
                Check(!map.IsSourceFailed, "tile source failed: " + map.LastError);
                Check(map.IsInitialAreaReady, "initial area not ready after 90 s");
                Check(map.BuiltTileCount >= 1, "no tile built");
                Check(map.Stats.TilesFailed == 0, "tile failures: " + map.Stats.TilesFailed + " " + map.LastError);
                int roads = map.TrianglesIn(MapRenderLayer.RoadMinor) + map.TrianglesIn(MapRenderLayer.RoadMajor) + map.TrianglesIn(MapRenderLayer.Path);
                int buildings = map.TrianglesIn(MapRenderLayer.Building);
                int ground = map.TrianglesIn(MapRenderLayer.Landuse) + map.TrianglesIn(MapRenderLayer.Landcover) + map.TrianglesIn(MapRenderLayer.Park);
                _props["roadTris"] = roads.ToString();
                _props["buildingTris"] = buildings.ToString();
                _props["groundTris"] = ground.ToString();
                _props["waterTris"] = map.TrianglesIn(MapRenderLayer.Water).ToString();
                Check(roads > 0, "no road meshes");
                Check(buildings > 0, "no building meshes");
                Check(ground > 0, "no landuse/landcover/park meshes");
                End();

                Begin("attribution_visible_and_panel");
                yield return null;
                string label = attribution.LabelText;
                _props["attribution"] = label;
                Check(label.Contains("OpenStreetMap"), "attribution lacks OpenStreetMap: " + label);
                Check(label.Contains("© OpenStreetMap"), "attribution lacks © OpenStreetMap");
                Check(label.Contains("OpenFreeMap"), "attribution lacks OpenFreeMap");
                Check(label.Contains("OpenMapTiles"), "attribution lacks OpenMapTiles");
                Check(attribution.gameObject.activeInHierarchy, "attribution not active");
                attribution.InfoButton.onClick.Invoke();
                yield return null;
                Check(attribution.Panel.activeSelf, "(i) did not open the panel");
                attribution.CloseButton.onClick.Invoke();
                yield return null;
                Check(!attribution.Panel.activeSelf, "close did not close the panel");
                End();

                Begin("spawn_safety_unknown_default_deny_and_blocks");
                ISpawnSafetyService safety = map.Safety;
                SafetyResult far = safety.Evaluate(GeoMath.OffsetMeters(origin.Origin, 5000, 5000), 0);
                Check(far.Verdict == SafetyVerdict.Unknown && !far.IsSpawnAllowed, "far point not Unknown: " + far);
                ProjectedFeature road = map.BuiltFeatures.FirstOrDefault(f => f.Kind == MapFeatureKind.Road && f.Lines.Count > 0 && f.Source.RoadClass != RoadClass.Path && f.Source.RoadClass != RoadClass.Pedestrian && f.Source.RoadClass != RoadClass.None && Inside(f.Lines[0][0], f.Lines[0][1], 250));
                Check(road != null, "no road feature near the origin");
                if (road != null)
                {
                    SafetyResult onRoad = safety.Evaluate(frame.ToGeo(road.Lines[0][0], road.Lines[0][1]), 0);
                    _props["onRoadVerdict"] = onRoad.ToString();
                    Check(onRoad.Verdict == SafetyVerdict.Blocked, "road vertex not Blocked: " + onRoad);
                }

                ProjectedFeature building = map.BuiltFeatures.FirstOrDefault(f => f.Kind == MapFeatureKind.Building && f.Polygons.Count > 0 && Inside(f.Polygons[0].Exterior[0], f.Polygons[0].Exterior[1], 250));
                Check(building != null, "no building near the origin");
                if (building != null)
                {
                    double[] ring = building.Polygons[0].Exterior;
                    // A footprint vertex is always within the 2 m building buffer (centroids can fall outside concave shapes).
                    SafetyResult atBuilding = safety.Evaluate(frame.ToGeo(ring[0], ring[1]), 0);
                    _props["buildingVertexVerdict"] = atBuilding.ToString();
                    Check(atBuilding.Verdict == SafetyVerdict.Blocked, "building vertex not Blocked: " + atBuilding);
                }

                yield return new WaitForSecondsRealtime(0.7f);
                _props["mobSafety"] = diagnostics.MobSafety.ToString();
                _props["npcSafety"] = diagnostics.NpcSafety.ToString();
                _props["demoPointSafety"] = safety.Evaluate(origin.Origin, 0).ToString();
                Check(safety.TryFindSafePoint(origin.Origin, 120, 1, out GeoCoordinate _), "no safe point within 120 m of the demo square");
                End();

                Begin("camera_rig_rotate_tilt_zoom");
                rig.Snap();
                Quaternion before = rig.transform.rotation;
                float yaw0 = rig.YawDegrees;
                rig.Rotate(45f);
                rig.Snap();
                Check(Mathf.Abs(Mathf.DeltaAngle(yaw0 + 45f, rig.YawDegrees)) < 0.01f, "yaw not applied");
                Check(Quaternion.Angle(before, rig.transform.rotation) > 10f, "camera transform did not rotate");
                rig.Tilt(-500f);
                Check(rig.PitchDegrees >= 30f - 0.01f, "pitch below clamp");
                rig.Tilt(500f);
                Check(rig.PitchDegrees <= 75f + 0.01f, "pitch above clamp");
                float d0 = rig.DistanceMeters;
                rig.Zoom(0.5f);
                Check(rig.DistanceMeters < d0, "zoom in did not reduce distance");
                rig.Zoom(100f);
                Check(rig.DistanceMeters <= 80f + 0.01f, "zoom clamp");
                rig.Snap();
                Vector3 toTarget = (avatar.transform.position + Vector3.up) - rig.transform.position;
                Check(Vector3.Angle(rig.transform.forward, toTarget) < 1f, "camera not looking at the player");
                Check(Vector3.Angle(rig.transform.forward, Vector3.down) < 90f, "camera not angled down");
                End();

                Begin("neighbourhood_follows_player");
                int builtBefore = map.Stats.TilesBuilt;
                location.MockProvider.MoveMeters(0, 900); // ~1.5 z16 tiles east, still inside the demo city
                float t1 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t1 < 60f && (map.BuiltTileCount == 0 || map.Stats.TilesBuilt == builtBefore))
                {
                    yield return null;
                }

                yield return new WaitForSecondsRealtime(2f);
                Check(map.BuiltTileCount >= 1, "no tiles after moving");
                Check(map.Stats.TilesFailed == 0, "tile failures after moving: " + map.LastError);
                Check(map.TrianglesIn(MapRenderLayer.Building) > 0, "no buildings after moving");
                _props["mapStatsAfterMove"] = map.Stats.Summary();
                End();

                Begin("no_provider_types_leak_into_gameplay");
                var leaks = new List<string>();
                Assembly asm = typeof(WorldMapOrigin).Assembly;
                foreach (Type t in asm.GetTypes())
                {
                    string ns = t.Namespace ?? string.Empty;
                    if (ns != "MetinAR.WorldMap" && ns != "MetinAR.Location" && !ns.StartsWith("MetinAR.ArEncounter") && ns != "MetinAR.UI")
                    {
                        continue;
                    }

                    foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (IsMapType(f.FieldType))
                        {
                            leaks.Add(t.Name + "." + f.Name);
                        }
                    }
                }

                foreach (Type t in asm.GetTypes().Where(x => x.Namespace == "MetinAR.Map.Runtime" && x.IsPublic))
                {
                    foreach (MemberInfo m in t.GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public))
                    {
                        Type mt = (m as PropertyInfo)?.PropertyType ?? (m as FieldInfo)?.FieldType ?? (m as MethodInfo)?.ReturnType;
                        if (mt != null && (mt.Namespace ?? string.Empty).StartsWith("MetinAR.Map.Osm"))
                        {
                            leaks.Add(t.Name + "." + m.Name + " exposes " + mt.Name);
                        }
                    }
                }

                Check(leaks.Count == 0, "leaks: " + string.Join(", ", leaks));
                End();

                Begin("privacy_no_coordinates_or_tile_urls_in_logs");
                string[] logs;
                lock (_logs)
                {
                    logs = _logs.ToArray();
                }

                var coord = new Regex(@"50[.,]06\d|19[.,]93\d|19[.,]94\d|\b1[46]/\d{4,5}/\d{4,5}\b|\.pbf|openfreemap\.org/planet/");
                string bad = logs.FirstOrDefault(l => coord.IsMatch(l));
                Check(bad == null, "log leaks location: " + (bad == null ? string.Empty : bad.Substring(0, Math.Min(120, bad.Length))));
                Check(MapUserAgent.Default.StartsWith("MetinAR/"), "user agent not MetinAR");
                var so = new SerializedObject(location);
                Check(Mathf.Approximately(so.FindProperty("_diagnosticLogIntervalSeconds").floatValue, 0f), "PlayerLocationService coordinate logging enabled in POC scene");
                End();

                _props["diagnostics"] = diagnostics.LastLine;
            }

            private static bool Inside(double e, double n, double radius) => Math.Abs(e) < radius && Math.Abs(n) < radius;

            private static bool IsMapType(Type t)
            {
                if (t == null)
                {
                    return false;
                }

                if (t.IsArray)
                {
                    return IsMapType(t.GetElementType());
                }

                if (t.IsGenericType && t.GetGenericArguments().Any(IsMapType))
                {
                    return true;
                }

                string ns = t.Namespace ?? string.Empty;
                return ns == "MetinAR.Map" || ns.StartsWith("MetinAR.Map.");
            }

            private void WriteResultsAndExit()
            {
                int failures = _results.Count(r => r.failure != null);
                var sb = new StringBuilder();
                sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
                sb.AppendLine("<testsuite name=\"OsmMapPocSmokeTest\" tests=\"" + _results.Count + "\" failures=\"" + failures + "\">");
                sb.AppendLine("  <properties>");
                foreach (KeyValuePair<string, string> p in _props)
                {
                    sb.Append("    <property name=\"").Append(p.Key).Append("\" value=\"")
                        .Append(System.Security.SecurityElement.Escape(p.Value ?? string.Empty)).AppendLine("\" />");
                }

                sb.AppendLine("  </properties>");
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
                Debug.Log("[OsmSmoke] RESULT tests=" + _results.Count + " failures=" + failures + " -> " + ResultsPath);
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }
    }
}
