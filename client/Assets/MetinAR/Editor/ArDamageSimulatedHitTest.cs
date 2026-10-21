using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MetinAR.ArEncounter;
using MetinAR.Gameplay.Combat;
using MetinAR.Progression;
using MetinAR.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MetinAR.Editor
{
    /// <summary>
    /// METINAR-020 (S2a) Play Mode test with simulated FPP hits (no AR device, no FPP hands needed):
    /// the real spider prefab gets <c>HitDetected(mob, swingId)</c> events through <see cref="ArEncounterCombat"/>
    /// → DamageResolver → HP label/bar → death → EXP and talary in <see cref="LocalPlayerSession"/> → world-map HUD
    /// (level, 4 × 25% orbs with fractional fill, talary). Also checks that the AR scene adds the combat component.
    /// Usage: Unity -batchmode -projectPath client -executeMethod MetinAR.Editor.ArDamageSimulatedHitTest.Run
    /// Writes Builds/metinar-020-ar-damage-results.xml and exits 0 on pass, 1 on failure.
    /// </summary>
    public static class ArDamageSimulatedHitTest
    {
        private const string SessionKey = "MetinAR020.DamageResults";
        private const string PreKey = "MetinAR020.EditModeState";
        private const string SpiderPrefabPath = "Assets/MetinAR/Prefabs/ArPrototypeSpider.prefab";

        public static void Run()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string repoRoot = Directory.GetParent(projectRoot)!.FullName;
            string resultsPath = Path.Combine(repoRoot, "Builds", "metinar-020-ar-damage-results.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(resultsPath)!);

            var sb = new StringBuilder();
            EditorSceneManager.OpenScene(ArEncounterScenes.ArEncounterScenePath, OpenSceneMode.Single);
            sb.Append("arMissing=").Append(CountMissingInOpenScene());
            sb.Append(";spiderMissing=").Append(CountMissingInPrefab(SpiderPrefabPath));
            EditorSceneManager.OpenScene(ArEncounterScenes.WorldMapScenePath, OpenSceneMode.Single);
            sb.Append(";wmMissing=").Append(CountMissingInOpenScene());
            Debug.Log("[ArDamageTest] edit-mode " + sb);

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
            var go = new GameObject("ArDamageTestRunner");
            Object.DontDestroyOnLoad(go);
            Runner runner = go.AddComponent<Runner>();
            runner.ResultsPath = results;
            runner.EditModeState = SessionState.GetString(PreKey, string.Empty);
            runner.SpiderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SpiderPrefabPath);
            SessionState.EraseString(PreKey);
        }

        private sealed class Runner : MonoBehaviour
        {
            public string ResultsPath;
            public string EditModeState;
            public GameObject SpiderPrefab;

            private readonly List<(string name, string failure)> _results = new List<(string, string)>();
            private readonly List<string> _exceptions = new List<string>();
            private string _currentFailure;

            private WorldMapHud _hud;
            private ArEncounterCombat _combat;
            private ArEncounterMob _mob;
            private readonly List<CombatHitReport> _reports = new List<CombatHitReport>();
            private int _defeatedEvents;
            private MobKillReward _reward;

            private void Check(bool condition, string message)
            {
                if (!condition && _currentFailure == null)
                {
                    _currentFailure = message;
                }
            }

            private void OnEnable()
            {
                Application.logMessageReceived += OnLog;
            }

            private void OnDisable()
            {
                Application.logMessageReceived -= OnLog;
            }

            private void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Exception)
                {
                    _exceptions.Add(condition);
                }
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
                var tests = new (string, Func<IEnumerator>)[]
                {
                    ("T01_NoMissingScripts", T01_NoMissingScripts),
                    ("T02_HudStartsAtLevel1_EmptyOrbs_ZeroTalary", T02_HudStartsAtLevel1_EmptyOrbs_ZeroTalary),
                    ("T03_SpiderBindsWithSpecStats", T03_SpiderBindsWithSpecStats),
                    ("T04_SimulatedHitReducesHpLabelAndBar", T04_SimulatedHitReducesHpLabelAndBar),
                    ("T05_SameSwingAndForeignMobAreIgnored", T05_SameSwingAndForeignMobAreIgnored),
                    ("T06_HitsUntilDeath_AwardExpAndTalaryOnce", T06_HitsUntilDeath_AwardExpAndTalaryOnce),
                    ("T07_DeadMobHidesBody_IgnoresFurtherHits", T07_DeadMobHidesBody_IgnoresFurtherHits),
                    ("T08_HudShowsFractionalOrbsAndTalary", T08_HudShowsFractionalOrbsAndTalary),
                    ("T09_SecondKillLevelsUp", T09_SecondKillLevelsUp),
                    ("T10_ArSceneAddsCombatComponent", T10_ArSceneAddsCombatComponent),
                    ("T11_NoExceptionsLogged", T11_NoExceptionsLogged),
                };

                foreach (var (name, test) in tests)
                {
                    _currentFailure = null;
                    yield return RunGuarded(test());
                    _results.Add((name, _currentFailure));
                    Debug.Log("[ArDamageTest] " + (_currentFailure == null ? "PASS " : "FAIL ") + name
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

            private ArEncounterMob SpawnSpider(string name, Vector3 position)
            {
                GameObject go = Instantiate(SpiderPrefab, position, Quaternion.identity);
                go.name = name;
                var mob = go.GetComponent<ArEncounterMob>();
                mob.Configure(ArEncounterRequest.PrototypeTestSpider("metinar-020-test"), Camera.main);
                return mob;
            }

            private IEnumerator T01_NoMissingScripts()
            {
                Check(EditModeState == "arMissing=0;spiderMissing=0;wmMissing=0", "missing scripts: " + EditModeState);
                Check(SpiderPrefab != null && SpiderPrefab.GetComponent<ArEncounterMob>() != null, "spider prefab / ArEncounterMob missing");
                yield break;
            }

            private IEnumerator T02_HudStartsAtLevel1_EmptyOrbs_ZeroTalary()
            {
                LocalPlayerSession.Reset();
                yield return Frames(2);
                _hud = FindAnyObjectByType<WorldMapHud>();
                Check(_hud != null, "WorldMapHud not found in world map scene");
                if (_hud == null)
                {
                    yield break;
                }

                Check(_hud.BindToLocalPlayer, "HUD not bound to local player");
                Check(_hud.LevelLabel == "Lv 1", "level label " + _hud.LevelLabel);
                Check(Mathf.Approximately(_hud.FilledExpOrbs, 0f), "orbs " + _hud.FilledExpOrbs);
                Check(_hud.TalaryLabel == "0", "talary label " + _hud.TalaryLabel);
                Check(_hud.ExpOrbs != null && _hud.ExpOrbs.Length == 4, "4 EXP orbs");
                foreach (Image orb in _hud.ExpOrbs)
                {
                    Check(orb != null && orb.type == Image.Type.Filled && Mathf.Approximately(orb.fillAmount, 0f), "orb not empty/filled type");
                }
            }

            private IEnumerator T03_SpiderBindsWithSpecStats()
            {
                _mob = SpawnSpider("TestSpider", new Vector3(0f, -50f, 3f));
                var combatGo = new GameObject("TestCombat");
                _combat = combatGo.AddComponent<ArEncounterCombat>();
                _combat.SeedOverride = 20201021UL;
                _combat.HitResolved += r => _reports.Add(r);
                _combat.MobDefeated += (m, reward) =>
                {
                    _defeatedEvents++;
                    _reward = reward;
                };
                _combat.Bind(_mob);
                yield return null;
                EncounterCombatSession s = _combat.Session;
                Check(s != null, "no session");
                if (s == null)
                {
                    yield break;
                }

                Check(s.Mob.Id == MobCombatProfile.VenomousSpiderId && s.Mob.Level == 5, "profile " + s.Mob.Id + " " + s.Mob.Level);
                Check(s.MobMaxHp == 120 && s.MobHp == 120 && s.Mob.Def == 15, "spider hp/def " + s.MobHp + "/" + s.MobMaxHp + " def " + s.Mob.Def);
                Check(_mob.HpLabel == "HP 120/120" && Mathf.Approximately(_mob.HpFillFraction, 1f), "label " + _mob.HpLabel);
            }

            private IEnumerator T04_SimulatedHitReducesHpLabelAndBar()
            {
                _combat.HandleHitDetected(_mob, 1);
                yield return null;
                CombatHitReport r = _combat.LastReport;
                Check(r != null && r.Status == CombatHitStatus.Resolved, "hit not resolved");
                if (r == null)
                {
                    yield break;
                }

                int hp = _combat.Session.MobHp;
                Check(r.HpBefore == 120 && r.HpAfter == hp, "hp " + r.HpBefore + "->" + r.HpAfter);
                if (r.Damage.Landed)
                {
                    // Warrior lvl 1 vs spider: 8–10 non-crit, ≤ 16 crit (Atk 11.3 × 58/73 × spread/crit).
                    Check(r.Damage.Damage >= 8 && r.Damage.Damage <= 16, "damage " + r.Damage.Damage);
                    Check(hp < 120, "hp not reduced");
                }

                Check(_mob.HpLabel == "HP " + hp + "/120", "label " + _mob.HpLabel);
                Check(Mathf.Abs(_mob.HpFillFraction - hp / 120f) < 1e-4f, "fill " + _mob.HpFillFraction);
            }

            private IEnumerator T05_SameSwingAndForeignMobAreIgnored()
            {
                int hp = _combat.Session.MobHp;
                _combat.HandleHitDetected(_mob, 1);
                Check(_combat.LastReport.Status == CombatHitStatus.DuplicateSwing && _combat.Session.MobHp == hp, "same swing hit twice");

                ArEncounterMob other = SpawnSpider("OtherSpider", new Vector3(5f, -50f, 3f));
                CombatHitReport last = _combat.LastReport;
                _combat.HandleHitDetected(other, 2);
                Check(_combat.LastReport == last && _combat.Session.MobHp == hp, "foreign mob changed the fight");
                Check(other.HpLabel == "HP 120/120", "foreign mob label " + other.HpLabel);
                Destroy(other.gameObject);
                yield return null;
            }

            private IEnumerator T06_HitsUntilDeath_AwardExpAndTalaryOnce()
            {
                int swing = 2;
                int previousHp = _combat.Session.MobHp;
                while (!_combat.Session.MobDefeated && swing < 60)
                {
                    _combat.HandleHitDetected(_mob, swing++);
                    int hp = _combat.Session.MobHp;
                    Check(hp <= previousHp, "hp increased");
                    Check(_mob.IsDefeated || _mob.HpLabel == "HP " + hp + "/120", "label lag " + _mob.HpLabel);
                    previousHp = hp;
                    yield return null;
                }

                int swings = _combat.Session.SwingsResolved;
                Check(_combat.Session.MobDefeated, "spider not defeated after " + swings + " swings");
                Check(swings >= 11 && swings <= 18, "TTK swings " + swings);
                Check(_defeatedEvents == 1, "defeated events " + _defeatedEvents);
                Check(_mob.IsDefeated && _mob.HpLabel == ArEncounterMob.DefeatedLabel && Mathf.Approximately(_mob.HpFillFraction, 0f), "death label " + _mob.HpLabel);
                Check(_reward.Exp == 101, "exp reward " + _reward.Exp);
                Check(_reward.Talary >= 10 && _reward.Talary <= 18, "talary reward " + _reward.Talary);
                PlayerProgress p = LocalPlayerSession.Progress;
                Check(p.TotalExp == 101 && p.Talary == _reward.Talary && p.Level == 1, "progress exp " + p.TotalExp + " talary " + p.Talary + " lvl " + p.Level);
                Debug.Log("[ArDamageTest] kill swings=" + swings + " exp=" + _reward.Exp + " talary=" + _reward.Talary);
            }

            private IEnumerator T07_DeadMobHidesBody_IgnoresFurtherHits()
            {
                PlayerProgress p = LocalPlayerSession.Progress;
                long exp = p.TotalExp;
                long talary = p.Talary;
                _combat.HandleHitDetected(_mob, 999);
                Check(_combat.LastReport.Status == CombatHitStatus.MobAlreadyDefeated, "hit after death " + _combat.LastReport.Status);
                Check(p.TotalExp == exp && p.Talary == talary && _defeatedEvents == 1, "second reward");
                float t = 0f;
                while (!_mob.IsBodyHidden && t < 3f)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }

                Check(_mob.IsBodyHidden, "body not hidden after death");
                Transform model = _mob.transform.Find("Model");
                Check(model != null && !model.GetComponentInChildren<Renderer>().enabled, "model renderer still enabled");
                Check(_mob.LabelRoot.GetComponentInChildren<Renderer>().enabled, "label hidden");
            }

            private IEnumerator T08_HudShowsFractionalOrbsAndTalary()
            {
                yield return Frames(2);
                if (_hud == null)
                {
                    Check(false, "no HUD");
                    yield break;
                }

                // 101 / 160 EXP of level 1 = 2.525 orbs → fills 1, 1, 0.525, 0.
                Check(Mathf.Abs(_hud.FilledExpOrbs - 2.525f) < 1e-4f, "orbs " + _hud.FilledExpOrbs);
                float[] expected = { 1f, 1f, 0.525f, 0f };
                for (int i = 0; i < 4; i++)
                {
                    Check(Mathf.Abs(_hud.ExpOrbs[i].fillAmount - expected[i]) < 1e-4f, "orb " + i + " fill " + _hud.ExpOrbs[i].fillAmount);
                }

                Check(_hud.LevelLabel == "Lv 1", "level " + _hud.LevelLabel);
                Check(_hud.TalaryLabel == _reward.Talary.ToString(), "talary label " + _hud.TalaryLabel);
            }

            private IEnumerator T09_SecondKillLevelsUp()
            {
                ArEncounterMob second = SpawnSpider("SecondSpider", new Vector3(0f, -50f, 6f));
                _combat.SeedOverride = 7UL;
                _combat.Bind(second);
                int swing = 1;
                while (!_combat.Session.MobDefeated && swing < 60)
                {
                    _combat.HandleHitDetected(second, swing++);
                }

                yield return Frames(2);
                PlayerProgress p = LocalPlayerSession.Progress;
                CombatHitReport kill = _combat.LastReport;
                Check(kill != null && kill.Killed && kill.LevelsGained == 1 && kill.PlayerLevelAfter == 2, "no level-up on second kill");
                Check(p.Level == 2 && p.TotalExp == 202, "progress lvl " + p.Level + " exp " + p.TotalExp);
                Check(_hud.LevelLabel == "Lv 2", "HUD level " + _hud.LevelLabel);
                // 202 − 160 = 42 of 270 → 0.622 orbs.
                Check(Mathf.Abs(_hud.FilledExpOrbs - 42f / 270f * 4f) < 1e-4f, "orbs after level-up " + _hud.FilledExpOrbs);
                Check(_defeatedEvents == 2, "defeated events " + _defeatedEvents);
            }

            private IEnumerator T10_ArSceneAddsCombatComponent()
            {
                SceneManager.LoadScene(ArEncounterScenes.ArEncounterScenePath);
                float t = 0f;
                ArEncounterSceneController ar = null;
                while (ar == null && t < 10f)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                    ar = FindAnyObjectByType<ArEncounterSceneController>();
                }

                Check(ar != null, "AR scene controller not found");
                if (ar == null)
                {
                    yield break;
                }

                yield return Frames(2);
                Check(ar.Combat != null && ar.Combat.gameObject == ar.gameObject, "combat component not added");
                Check(ar.GetComponents<ArEncounterCombat>().Length == 1, "duplicate combat components");
                Check(ar.Combat.Session == null, "session before placement");
            }

            private IEnumerator T11_NoExceptionsLogged()
            {
                Check(_exceptions.Count == 0, "exceptions: " + string.Join(" | ", _exceptions));
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
                sb.AppendLine("<testsuite name=\"ArDamageSimulatedHitTest\" tests=\"" + _results.Count + "\" failures=\"" + failures + "\">");
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
                Debug.Log("[ArDamageTest] RESULT tests=" + _results.Count + " failures=" + failures + " -> " + ResultsPath);
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }
    }
}
