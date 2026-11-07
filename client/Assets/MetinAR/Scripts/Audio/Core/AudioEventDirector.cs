using System;
using System.Collections.Generic;

namespace MetinAR.Audio
{
    /// <summary>Plays one clip on a mixer group. Implemented by the AudioSource pool (AudioService) and by test fakes.</summary>
    public interface IAudioOutput
    {
        void PlayOneShot(string clipId, AudioChannel channel, float volume, float pitch);
    }

    /// <summary>Device vibration. Implemented by UnityHaptics (Handheld.Vibrate) and by test fakes.</summary>
    public interface IHapticsOutput
    {
        void Vibrate();
    }

    /// <summary>
    /// METINAR-040 (#85): engine-free core of the AudioService. Turns a <see cref="GameAudioEvent"/> into exactly one
    /// clip on its channel, with variation (random variant without immediate repeat, pitch spread), anti-spam
    /// (per-event minimum interval) and optional haptics (only when enabled in <see cref="AudioSettingsModel"/>).
    /// </summary>
    public sealed class AudioEventDirector
    {
        /// <summary>Minimum gap between two vibrations, so a flurry of hits is one buzz, not a rattle.</summary>
        public const double HapticMinIntervalSeconds = 0.25;

        private readonly IAudioOutput _output;
        private readonly IHapticsOutput _haptics;
        private readonly AudioSettingsModel _settings;
        private readonly Func<double> _clock;
        private readonly Random _random;
        private readonly Dictionary<GameAudioEvent, double> _lastPlayed = new Dictionary<GameAudioEvent, double>();
        private readonly Dictionary<GameAudioEvent, int> _lastVariant = new Dictionary<GameAudioEvent, int>();
        private double _lastHaptic = double.NegativeInfinity;

        public AudioEventDirector(IAudioOutput output, IHapticsOutput haptics, AudioSettingsModel settings,
            Func<double> clockSeconds, int seed = 85)
        {
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _haptics = haptics;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _clock = clockSeconds ?? throw new ArgumentNullException(nameof(clockSeconds));
            _random = new Random(seed);
        }

        public int PlayedCount { get; private set; }
        public int SuppressedCount { get; private set; }
        public int HapticCount { get; private set; }

        /// <summary>Clip id of the last played cue (diagnostics / smoke test).</summary>
        public string LastClip { get; private set; }

        public event Action<GameAudioEvent, string> Played;

        /// <summary>Plays the cue of <paramref name="gameEvent"/>. Returns false when it was rate-limited or muted.</summary>
        public bool Handle(GameAudioEvent gameEvent)
        {
            if (!AudioCueTable.TryGet(gameEvent, out AudioCue cue)) return false;
            double now = _clock();

            if (_lastPlayed.TryGetValue(gameEvent, out double last) && now - last < cue.MinIntervalSeconds)
            {
                SuppressedCount++;
                return false;
            }

            _lastPlayed[gameEvent] = now;
            TryVibrate(cue, now);

            if (!_settings.IsAudible(cue.Channel))
            {
                SuppressedCount++;
                return false;
            }

            string clip = PickClip(cue);
            float pitch = cue.PitchMin + (float)_random.NextDouble() * (cue.PitchMax - cue.PitchMin);
            _output.PlayOneShot(clip, cue.Channel, cue.Volume, pitch);
            PlayedCount++;
            LastClip = clip;
            Played?.Invoke(gameEvent, clip);
            return true;
        }

        /// <summary>Forget anti-spam history (scene change).</summary>
        public void ResetHistory()
        {
            _lastPlayed.Clear();
            _lastVariant.Clear();
            _lastHaptic = double.NegativeInfinity;
        }

        private void TryVibrate(AudioCue cue, double now)
        {
            if (!cue.Haptic || _haptics == null || !_settings.HapticsEnabled) return;
            if (now - _lastHaptic < HapticMinIntervalSeconds) return;
            _lastHaptic = now;
            HapticCount++;
            _haptics.Vibrate();
        }

        private string PickClip(AudioCue cue)
        {
            int count = cue.Clips.Count;
            if (count == 1) return cue.Clips[0];
            int index = _random.Next(count);
            if (_lastVariant.TryGetValue(cue.Event, out int previous) && index == previous)
                index = (index + 1 + _random.Next(count - 1)) % count;
            _lastVariant[cue.Event] = index;
            return cue.Clips[index];
        }
    }
}
