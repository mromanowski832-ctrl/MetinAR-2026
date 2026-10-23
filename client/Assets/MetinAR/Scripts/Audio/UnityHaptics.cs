using System;
using UnityEngine;

namespace MetinAR.Audio
{
    /// <summary>
    /// Short vibration pulse. Android: Vibrator one-shot of <see cref="PulseMilliseconds"/> ms (Handheld.Vibrate is
    /// ~0.5 s, too long for a hit); fallback and iOS: Handheld.Vibrate. Referencing Handheld.Vibrate makes Unity add
    /// the normal-level android.permission.VIBRATE to the manifest. The AudioEventDirector calls this only when
    /// vibration is switched on in the settings.
    /// </summary>
    public sealed class UnityHaptics : IHapticsOutput
    {
        public const long PulseMilliseconds = 35;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _vibrator;
        private bool _vibratorResolved;
        private int _sdk;
#endif

        public int Calls { get; private set; }

        public void Vibrate()
        {
            Calls++;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (TryAndroidPulse()) return;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Audio] vibrator pulse failed, falling back to Handheld.Vibrate: " + e.Message);
                _vibrator = null;
            }

            Handheld.Vibrate();
#elif UNITY_IOS && !UNITY_EDITOR
            Handheld.Vibrate();
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private bool TryAndroidPulse()
        {
            if (!_vibratorResolved)
            {
                _vibratorResolved = true;
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    _sdk = version.GetStatic<int>("SDK_INT");
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            }

            if (_vibrator == null || !_vibrator.Call<bool>("hasVibrator")) return false;
            if (_sdk >= 26)
            {
                using (var effectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                using (AndroidJavaObject effect = effectClass.CallStatic<AndroidJavaObject>("createOneShot", PulseMilliseconds, -1))
                    _vibrator.Call("vibrate", effect);
            }
            else
            {
                _vibrator.Call("vibrate", PulseMilliseconds);
            }

            return true;
        }
#endif
    }
}
