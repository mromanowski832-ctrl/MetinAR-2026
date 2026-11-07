using System;

namespace MetinAR.Audio
{
    /// <summary>Persistence port for audio settings (PlayerPrefs today; the #71 settings/save service later).</summary>
    public interface IAudioSettingsStore
    {
        bool TryGetFloat(string key, out float value);
        void SetFloat(string key, float value);
        bool TryGetInt(string key, out int value);
        void SetInt(string key, int value);
        void Save();
    }

    /// <summary>
    /// METINAR-040 (#85): player audio settings — separate SFX / Music / UI volumes (0..1), a master volume and the
    /// vibration switch. Volumes map to the exposed AudioMixer parameters (<see cref="MixerParameter"/>) in decibels;
    /// 0 mutes the group (−80 dB).
    /// </summary>
    public sealed class AudioSettingsModel
    {
        public const string MasterParameter = "MasterVolume";
        public const string SfxParameter = "SfxVolume";
        public const string UiParameter = "UiVolume";
        public const string MusicParameter = "MusicVolume";

        public const string KeyPrefix = "metinar.audio.";
        public const string MasterKey = KeyPrefix + "master";
        public const string SfxKey = KeyPrefix + "sfx";
        public const string UiKey = KeyPrefix + "ui";
        public const string MusicKey = KeyPrefix + "music";
        public const string HapticsKey = KeyPrefix + "haptics";

        public const float MutedDecibels = -80f;
        public const float DefaultMaster = 1f;
        public const float DefaultSfx = 1f;
        public const float DefaultUi = 0.8f;
        public const float DefaultMusic = 0.7f;

        private readonly IAudioSettingsStore _store;
        private float _master = DefaultMaster;
        private float _sfx = DefaultSfx;
        private float _ui = DefaultUi;
        private float _music = DefaultMusic;
        private bool _haptics = true;

        public AudioSettingsModel(IAudioSettingsStore store = null)
        {
            _store = store;
            Load();
        }

        /// <summary>Raised after any value changes (UI sliders, reload).</summary>
        public event Action Changed;

        public float Master
        {
            get => _master;
            set => SetValue(ref _master, value, DefaultMaster, MasterKey);
        }

        public bool HapticsEnabled
        {
            get => _haptics;
            set
            {
                if (_haptics == value) return;
                _haptics = value;
                if (_store != null)
                {
                    _store.SetInt(HapticsKey, value ? 1 : 0);
                    _store.Save();
                }

                Changed?.Invoke();
            }
        }

        public float GetVolume(AudioChannel channel)
        {
            switch (channel)
            {
                case AudioChannel.Sfx: return _sfx;
                case AudioChannel.Ui: return _ui;
                case AudioChannel.Music: return _music;
                default: throw new ArgumentOutOfRangeException(nameof(channel), channel, null);
            }
        }

        public void SetVolume(AudioChannel channel, float value)
        {
            switch (channel)
            {
                case AudioChannel.Sfx: SetValue(ref _sfx, value, DefaultSfx, SfxKey); break;
                case AudioChannel.Ui: SetValue(ref _ui, value, DefaultUi, UiKey); break;
                case AudioChannel.Music: SetValue(ref _music, value, DefaultMusic, MusicKey); break;
                default: throw new ArgumentOutOfRangeException(nameof(channel), channel, null);
            }
        }

        /// <summary>Master × channel; 0 means the channel is silent and nothing should be started on it.</summary>
        public float EffectiveVolume(AudioChannel channel) => _master * GetVolume(channel);

        public bool IsAudible(AudioChannel channel) => EffectiveVolume(channel) > 0.0001f;

        public void Load()
        {
            if (_store == null) return;
            _master = Read(MasterKey, DefaultMaster);
            _sfx = Read(SfxKey, DefaultSfx);
            _ui = Read(UiKey, DefaultUi);
            _music = Read(MusicKey, DefaultMusic);
            _haptics = !_store.TryGetInt(HapticsKey, out int h) || h != 0;
            Changed?.Invoke();
        }

        public static string MixerParameter(AudioChannel channel)
        {
            switch (channel)
            {
                case AudioChannel.Sfx: return SfxParameter;
                case AudioChannel.Ui: return UiParameter;
                case AudioChannel.Music: return MusicParameter;
                default: throw new ArgumentOutOfRangeException(nameof(channel), channel, null);
            }
        }

        /// <summary>Linear 0..1 → mixer attenuation in dB. 0 (or below) → −80 dB = muted.</summary>
        public static float ToDecibels(float linear)
        {
            if (float.IsNaN(linear) || linear <= 0.0001f) return MutedDecibels;
            if (linear >= 1f) return 0f;
            float db = (float)(20.0 * Math.Log10(linear));
            return db < MutedDecibels ? MutedDecibels : db;
        }

        public static float Clamp01(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private float Read(string key, float fallback) =>
            _store.TryGetFloat(key, out float v) ? Clamp01(v, fallback) : fallback;

        private void SetValue(ref float field, float value, float fallback, string key)
        {
            float clamped = Clamp01(value, fallback);
            if (Math.Abs(field - clamped) < 0.00001f) return;
            field = clamped;
            if (_store != null)
            {
                _store.SetFloat(key, clamped);
                _store.Save();
            }

            Changed?.Invoke();
        }
    }
}
