using UnityEngine;

namespace MetinAR.Audio
{
    /// <summary>PlayerPrefs persistence for audio settings until the save/settings service (#71) takes over.</summary>
    public sealed class PlayerPrefsAudioSettingsStore : IAudioSettingsStore
    {
        public bool TryGetFloat(string key, out float value)
        {
            if (!PlayerPrefs.HasKey(key))
            {
                value = 0f;
                return false;
            }

            value = PlayerPrefs.GetFloat(key);
            return true;
        }

        public void SetFloat(string key, float value) => PlayerPrefs.SetFloat(key, value);

        public bool TryGetInt(string key, out int value)
        {
            if (!PlayerPrefs.HasKey(key))
            {
                value = 0;
                return false;
            }

            value = PlayerPrefs.GetInt(key);
            return true;
        }

        public void SetInt(string key, int value) => PlayerPrefs.SetInt(key, value);

        public void Save() => PlayerPrefs.Save();
    }
}
