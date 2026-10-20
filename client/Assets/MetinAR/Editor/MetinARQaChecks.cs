using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MetinAR.Editor
{
    /// <summary>
    /// METINAR-011 QA gate (edit mode, read-only): missing-script scan + build-scene / build-index check.
    /// Does not modify scenes, prefabs or settings.
    /// Usage: Unity -batchmode -projectPath client -executeMethod MetinAR.Editor.MetinARQaChecks.Run
    ///        [-metinarQaResults &lt;path.json&gt;] [-metinarQaExpectedScenes "Assets/A.unity;Assets/B.unity"]
    ///        [-metinarQaPrefabRoots "Assets/MetinAR"]
    /// Exits 0 when there are 0 missing scripts and the enabled build scenes match the expected list in order.
    /// </summary>
    public static class MetinARQaChecks
    {
        private const string DefaultExpectedScenes =
            "Assets/MetinAR/Scenes/WorldMapPrototype.unity;Assets/MetinAR/Scenes/ArEncounterPrototype.unity";

        private const string DefaultPrefabRoots = "Assets/MetinAR";

        public static void Run()
        {
            int exitCode = 1;
            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
                string repoRoot = Directory.GetParent(projectRoot)!.FullName;
                string resultsPath = GetArg("-metinarQaResults")
                    ?? Path.Combine(repoRoot, "Builds", "qa", "metinar-qa-unity-checks.json");
                string[] expected = Split(GetArg("-metinarQaExpectedScenes") ?? DefaultExpectedScenes);
                string[] prefabRoots = Split(GetArg("-metinarQaPrefabRoots") ?? DefaultPrefabRoots);

                var failures = new List<string>();
                var json = new StringBuilder();
                json.Append("{\n");

                // 1. Build scenes / indices.
                var enabled = new List<string>();
                json.Append("  \"buildScenes\": [");
                bool first = true;
                foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
                {
                    json.Append(first ? "\n" : ",\n");
                    first = false;
                    json.Append("    {\"path\": \"").Append(Esc(s.path)).Append("\", \"enabled\": ")
                        .Append(s.enabled ? "true" : "false").Append(", \"exists\": ")
                        .Append(File.Exists(Path.Combine(projectRoot, s.path)) ? "true" : "false").Append("}");
                    if (s.enabled && !string.IsNullOrEmpty(s.path))
                    {
                        enabled.Add(s.path);
                        if (!File.Exists(Path.Combine(projectRoot, s.path)))
                        {
                            failures.Add("enabled build scene file missing: " + s.path);
                        }
                    }
                }

                json.Append("\n  ],\n");
                for (int i = 0; i < expected.Length; i++)
                {
                    if (i >= enabled.Count || enabled[i] != expected[i])
                    {
                        failures.Add("build index " + i + " expected " + expected[i] + " but was "
                            + (i < enabled.Count ? enabled[i] : "<none>"));
                    }
                }

                if (enabled.Count > expected.Length)
                {
                    failures.Add("unexpected extra enabled build scenes: "
                        + string.Join(",", enabled.GetRange(expected.Length, enabled.Count - expected.Length)));
                }

                // 2. Missing scripts in enabled build scenes.
                int totalMissing = 0;
                json.Append("  \"scenes\": [");
                first = true;
                foreach (string scenePath in enabled)
                {
                    if (!File.Exists(Path.Combine(projectRoot, scenePath)))
                    {
                        continue;
                    }

                    var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    int missing = 0;
                    var where = new List<string>();
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                        {
                            int m = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                            if (m > 0)
                            {
                                missing += m;
                                where.Add(HierarchyPath(t));
                            }
                        }
                    }

                    totalMissing += missing;
                    if (missing > 0)
                    {
                        failures.Add("missing scripts in " + scenePath + ": " + missing + " (" + string.Join(", ", where) + ")");
                    }

                    json.Append(first ? "\n" : ",\n");
                    first = false;
                    json.Append("    {\"path\": \"").Append(Esc(scenePath)).Append("\", \"missingScripts\": ").Append(missing).Append("}");
                }

                json.Append("\n  ],\n");

                // 3. Missing scripts in prefabs under the given roots.
                json.Append("  \"prefabs\": [");
                first = true;
                int prefabCount = 0;
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", prefabRoots))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null)
                    {
                        continue;
                    }

                    prefabCount++;
                    int missing = 0;
                    foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
                    {
                        missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    }

                    totalMissing += missing;
                    if (missing > 0)
                    {
                        failures.Add("missing scripts in prefab " + path + ": " + missing);
                    }

                    json.Append(first ? "\n" : ",\n");
                    first = false;
                    json.Append("    {\"path\": \"").Append(Esc(path)).Append("\", \"missingScripts\": ").Append(missing).Append("}");
                }

                json.Append("\n  ],\n");
                json.Append("  \"expectedScenes\": [");
                for (int i = 0; i < expected.Length; i++)
                {
                    json.Append(i == 0 ? "" : ", ").Append("\"").Append(Esc(expected[i])).Append("\"");
                }

                json.Append("],\n");
                json.Append("  \"prefabsScanned\": ").Append(prefabCount).Append(",\n");
                json.Append("  \"totalMissingScripts\": ").Append(totalMissing).Append(",\n");
                json.Append("  \"failures\": [");
                for (int i = 0; i < failures.Count; i++)
                {
                    json.Append(i == 0 ? "" : ", ").Append("\"").Append(Esc(failures[i])).Append("\"");
                }

                json.Append("],\n");
                json.Append("  \"result\": \"").Append(failures.Count == 0 ? "PASS" : "FAIL").Append("\"\n}\n");

                Directory.CreateDirectory(Path.GetDirectoryName(resultsPath)!);
                File.WriteAllText(resultsPath, json.ToString());
                foreach (string f in failures)
                {
                    Debug.LogError("[MetinARQa] FAIL " + f);
                }

                Debug.Log("[MetinARQa] RESULT " + (failures.Count == 0 ? "PASS" : "FAIL")
                    + " scenes=" + enabled.Count + " prefabs=" + prefabCount + " missingScripts=" + totalMissing
                    + " failures=" + failures.Count + " -> " + resultsPath);
                exitCode = failures.Count == 0 ? 0 : 1;
            }
            catch (Exception e)
            {
                Debug.LogError("[MetinARQa] EXCEPTION " + e);
                exitCode = 1;
            }

            EditorApplication.Exit(exitCode);
        }

        private static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static string[] Split(string value)
        {
            var list = new List<string>();
            foreach (string part in value.Split(';'))
            {
                string p = part.Trim();
                if (p.Length > 0)
                {
                    list.Add(p);
                }
            }

            return list.ToArray();
        }

        private static string HierarchyPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }

            return path;
        }

        private static string Esc(string s)
        {
            return (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
