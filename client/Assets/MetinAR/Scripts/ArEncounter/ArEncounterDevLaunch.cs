using UnityEngine;
using UnityEngine.SceneManagement;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// Development-build-only test hook: when the Android activity was started with the boolean intent extra
    /// <c>metinar_open_ar</c> (e.g. <c>adb shell am start … --ez metinar_open_ar true</c>), the AR encounter opens directly
    /// with the prototype test spider, so device checks do not depend on tapping the world map. No effect in release
    /// builds, in the Editor, or without the extra.
    /// </summary>
    public static class ArEncounterDevLaunch
    {
        public const string IntentExtra = "metinar_open_ar";

#if UNITY_ANDROID && !UNITY_EDITOR
        private static bool _consumed;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnFirstSceneLoaded()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_consumed || !Debug.isDebugBuild)
            {
                return;
            }

            _consumed = true;
            bool open = false;
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject intent = activity.Call<AndroidJavaObject>("getIntent"))
                {
                    open = intent != null && intent.Call<bool>("getBooleanExtra", IntentExtra, false);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[ArEncounterDevLaunch] intent read failed: " + ex.Message);
            }

            if (open)
            {
                Debug.Log("[ArEncounterDevLaunch] " + IntentExtra + "=true → opening AR encounter directly (development build)");
                ArEncounterHandoff.Submit(ArEncounterRequest.PrototypeTestSpider("dev-launch"));
                SceneManager.LoadScene(ArEncounterScenes.ArEncounterScenePath);
            }
#endif
        }
    }
}
