using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MetinAR.ArEncounter;
using MetinAR.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace MetinAR.Editor.Presentation
{
    /// <summary>Real Unity gate for the combined tree; previous asset-only reports do not satisfy this gate.</summary>
    public static class PresentationIntegrationCheck
    {
        private static int _checks;

        [MenuItem("MetinAR/Presentation/Validate integrated art and menu")]
        public static void Run()
        {
            bool success = false;
            _checks = 0;
            try
            {
                if (Application.isPlaying) throw new InvalidOperationException("Run this gate outside Play Mode.");
                GameArtCatalog catalog = GameArtCatalog.Load();
                Check(catalog != null, "Resources catalog exists");
                Check(catalog.Items != null && catalog.Items.Length == 21, "21 item entries");
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (ItemArt item in catalog.Items)
                {
                    Check(item != null && !string.IsNullOrEmpty(item.DefinitionId), "item has a definition id");
                    Check(ids.Add(item.DefinitionId), "unique id: " + item.DefinitionId);
                    Check(item.Icon != null, "icon: " + item.DefinitionId);
                    Check(!string.IsNullOrWhiteSpace(item.DisplayName), "name: " + item.DefinitionId);
                    Check(item.Icon.texture.width == 256 && item.Icon.texture.height == 256, "256px icon: " + item.DefinitionId);
                }
                foreach (string id in TableIds()) Check(ids.Contains(id), "table item covered: " + id);
                Check(catalog.Find("starter_sword")?.ModelPrefab != null, "starter sword model reference");
                Check(catalog.Find("starter_buckler")?.ModelPrefab == catalog.StarterBuckler, "buckler model reference");
                Check(catalog.FppArms != null && catalog.WanderingMerchant != null && catalog.StarterBuckler != null,
                    "all three visual prefab references resolve");
                foreach (GameObject prefab in new[] { catalog.FppArms, catalog.WanderingMerchant, catalog.StarterBuckler,
                             catalog.Find("starter_sword").ModelPrefab })
                    foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
                        Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) == 0,
                            prefab.name + "/" + child.name + " has no missing scripts");
                Check(catalog.WanderingMerchant.GetComponentInChildren<LODGroup>(true) != null, "merchant LODGroup");
                Check(catalog.WanderingMerchant.GetComponentInChildren<Animator>(true) != null, "merchant Animator");
                Check(catalog.WanderingMerchant.GetComponentInChildren<SkinnedMeshRenderer>(true) != null, "merchant skinned mesh");
                var merchantController = catalog.WanderingMerchant.GetComponentInChildren<Animator>(true).runtimeAnimatorController
                    as UnityEditor.Animations.AnimatorController;
                Check(merchantController != null, "merchant controller resolves");
                foreach (string state in new[] { "Idle", "Talk" })
                    Check(merchantController.layers.Any(layer => layer.stateMachine.states.Any(child =>
                        child.state.name == state && child.state.motion is AnimationClip clip && clip.length > 0f)),
                        "merchant has a nonempty " + state + " clip");
                CheckRig(catalog);
                CheckMenu(catalog);
                success = true;
                Debug.Log("[PresentationIntegrationCheck] PASS checks=" + _checks
                    + ". Android/A31 appearance and FPS remain UNVERIFIED.");
            }
            catch (Exception exception)
            {
                Debug.LogError("[PresentationIntegrationCheck] FAIL after " + _checks + " checks: " + exception);
            }
            if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
        }

        private static IEnumerable<string> TableIds()
        {
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            foreach (string file in new[] { "items_starter.csv", "shop_starter_merchant.csv", "skill_tier_items.csv" })
            {
                string path = Path.Combine(repo, "docs/design/tables", file);
                Check(File.Exists(path), "table exists: " + file);
                foreach (string line in File.ReadLines(path).Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string id = line.Split(',')[0].Trim();
                    if (id.Contains("<classId>"))
                        foreach (string classId in new[] { "warrior", "ninja", "shaman", "slajter" })
                            yield return id.Replace("<classId>", classId);
                    else yield return id;
                }
            }
        }

        private static void CheckRig(GameArtCatalog catalog)
        {
            var probe = new GameObject("FppPresentationProbe") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var view = probe.AddComponent<FppHandsView>();
                FppModelPresenter presenter = FppModelPresenter.Attach(view, catalog.FppArms);
                Check(presenter != null, "production rig attaches");
                CheckSamples(presenter, FppCombatAction.Idle, 2f);
                CheckSamples(presenter, FppCombatAction.Attack, FppCombatTimeline.AttackSeconds);
                CheckSamples(presenter, FppCombatAction.Aura, FppCombatTimeline.AuraCastSeconds);
                // A translated/rotated camera must not change the camera-local hit/visual contract.
                probe.transform.SetPositionAndRotation(new Vector3(3f, 1.6f, -4f), Quaternion.Euler(12f, 85f, -7f));
                CheckSamples(presenter, FppCombatAction.Attack, FppCombatTimeline.AttackSeconds);
                CheckSamples(presenter, FppCombatAction.Aura, FppCombatTimeline.AuraCastSeconds);
                CheckSamples(presenter, FppCombatAction.Idle, 2f);
            }
            finally { UnityEngine.Object.DestroyImmediate(probe); }
        }

        private static void CheckSamples(FppModelPresenter presenter, FppCombatAction action, float duration)
        {
            for (int i = 0; i <= Mathf.CeilToInt(duration * 100f); i++)
            {
                float time = Mathf.Min(i * 0.01f, duration);
                Check(presenter.Sample(action, time, time), action + " blade alignment at " + time.ToString("F2")
                    + " s (" + presenter.LastBladeErrorMetres.ToString("F4") + " m)");
            }
        }

        private static void CheckMenu(GameArtCatalog catalog)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("FrontendProbe");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            float originalVolume = AudioListener.volume;
            try
            {
                var frontend = root.AddComponent<GameFrontend>();
                frontend.Initialize(catalog);
                frontend.ShowHome();
                Check(frontend.IsOpen && frontend.CurrentPage == "home", "home opens");
                Button start = root.GetComponentsInChildren<Button>(true).FirstOrDefault(button =>
                {
                    var label = button.GetComponentInChildren<TMPro.TMP_Text>(true);
                    return label != null && (label.text == "Rozpocznij przygodę" || label.text == "Wróć do świata");
                });
                Check(start != null, "real start button exists");
                start.onClick.Invoke();
                Check(!frontend.IsOpen, "start button closes menu and enters world");
                frontend.ShowSettings();
                Check(frontend.IsOpen && frontend.CurrentPage == "settings", "settings opens");
                Check(root.GetComponentsInChildren<Slider>(true).Length == 1, "settings contains one volume slider");
                frontend.ShowItems();
                Check(frontend.IsOpen && frontend.CurrentPage == "items", "item catalog opens");
                ScrollRect scroll = root.GetComponentInChildren<ScrollRect>(true);
                Check(scroll != null && scroll.viewport != null && scroll.content != null, "catalog scroll is connected");
                Image surface = scroll.viewport.GetComponent<Image>();
                Check(surface != null && surface.raycastTarget, "catalog text/spacing can receive scroll gestures");
                scroll.velocity = new Vector2(0f, 1000f);
                frontend.ShowSettings();
                Check(scroll.velocity == Vector2.zero, "page navigation stops catalog inertia");
                frontend.CloseMenu();
                Check(!frontend.IsOpen, "menu closes");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
                AudioListener.volume = originalVolume;
            }
        }

        private static void Check(bool condition, string message)
        {
            _checks++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
