using System.Collections;
using System.IO;
using MetinAR.WorldMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MetinAR.Editor
{
    /// <summary>
    /// Batchmode playmode screenshots of the world-map prototype.
    /// Usage: -executeMethod MetinAR.Editor.CaptureWorldMapPrototypeScreenshots.Capture
    /// </summary>
    public static class CaptureWorldMapPrototypeScreenshots
    {
        private const string ScenePath = "Assets/MetinAR/Scenes/WorldMapPrototype.unity";
        private const string SessionKey = "MetinAR004.CaptureDir";

        [InitializeOnLoadMethod]
        private static void RegisterHook()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static void Capture()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string repoRoot = Directory.GetParent(projectRoot)!.FullName;
            string outputDirectory = Path.Combine(repoRoot, "Builds", "metinar-004-screenshots");
            Directory.CreateDirectory(outputDirectory);
            SessionState.SetString(SessionKey, outputDirectory);

            string sceneFull = Path.Combine(projectRoot, ScenePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(sceneFull))
            {
                Debug.LogError("[CaptureWorldMapPrototypeScreenshots] Scene missing: " + ScenePath);
                EditorApplication.Exit(1);
                return;
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            string dir = SessionState.GetString(SessionKey, string.Empty);
            if (state != PlayModeStateChange.EnteredPlayMode || string.IsNullOrEmpty(dir))
            {
                return;
            }

            SessionState.EraseString(SessionKey);

            var go = new GameObject("ScreenshotRunner");
            Object.DontDestroyOnLoad(go);
            var runner = go.AddComponent<ScreenshotPlaymodeRunner>();
            runner.OutputDirectory = dir;
        }

        private sealed class ScreenshotPlaymodeRunner : MonoBehaviour
        {
            public string OutputDirectory;

            private IEnumerator Start()
            {
                yield return null;
                yield return new WaitForSecondsRealtime(1.25f);

                WorldMapPrototypeController controller = Object.FindFirstObjectByType<WorldMapPrototypeController>();
                PrototypeMob mob = Object.FindFirstObjectByType<PrototypeMob>();
                Camera cam = Camera.main;
                if (cam != null)
                {
                    PrepareCamera(cam);
                }

                yield return null;
                yield return new WaitForSecondsRealtime(0.5f);

                SaveCameraShot(cam, Path.Combine(OutputDirectory, "01-map-hud.png"));
                yield return new WaitForSecondsRealtime(0.35f);

                mob?.OpenInfoPanel();
                yield return null;
                yield return new WaitForSecondsRealtime(0.35f);
                SaveCameraShot(cam, Path.Combine(OutputDirectory, "02-mob-info.png"));
                yield return new WaitForSecondsRealtime(0.35f);

                controller?.HideAllPanels();
                if (controller != null && controller.InventoryPanel != null)
                {
                    controller.ShowOnlyPanel(controller.InventoryPanel.gameObject);
                }

                yield return null;
                yield return new WaitForSecondsRealtime(0.35f);
                SaveCameraShot(cam, Path.Combine(OutputDirectory, "03-inventory.png"));
                yield return new WaitForSecondsRealtime(0.35f);

                controller?.HideAllPanels();
                if (controller != null && controller.CharacterPanel != null)
                {
                    controller.ShowOnlyPanel(controller.CharacterPanel.gameObject);
                }

                yield return null;
                yield return new WaitForSecondsRealtime(0.35f);
                SaveCameraShot(cam, Path.Combine(OutputDirectory, "04-character.png"));
                yield return new WaitForSecondsRealtime(0.5f);

                Debug.Log("[CaptureWorldMapPrototypeScreenshots] Done -> " + OutputDirectory);
                EditorApplication.Exit(0);
            }

            private static RenderTexture _rt;

            private static void PrepareCamera(Camera cam)
            {
                if (_rt == null)
                {
                    _rt = new RenderTexture(1080, 1920, 24, RenderTextureFormat.ARGB32);
                    _rt.Create();
                }

                cam.targetTexture = _rt;
                cam.aspect = 1080f / 1920f;
                foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                    {
                        canvas.renderMode = RenderMode.ScreenSpaceCamera;
                        canvas.worldCamera = cam;
                        canvas.planeDistance = 1f;
                    }
                }
            }

            private static void SaveCameraShot(Camera cam, string absolutePath)
            {
                if (cam == null)
                {
                    Debug.LogError("[Capture] No camera for " + absolutePath);
                    return;
                }

                PrepareCamera(cam);
                Canvas.ForceUpdateCanvases();
                cam.Render();
                RenderTexture prevActive = RenderTexture.active;
                RenderTexture.active = _rt;
                var tex = new Texture2D(_rt.width, _rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prevActive;
                File.WriteAllBytes(absolutePath, tex.EncodeToPNG());
                Object.Destroy(tex);
                Debug.Log("[Capture] wrote " + absolutePath);
            }
        }
    }
}
