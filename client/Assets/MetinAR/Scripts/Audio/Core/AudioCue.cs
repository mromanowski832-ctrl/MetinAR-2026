using System;
using System.Collections.Generic;

namespace MetinAR.Audio
{
    /// <summary>How one <see cref="GameAudioEvent"/> sounds: clip variants, channel, level, pitch spread, anti-spam, haptics.</summary>
    public sealed class AudioCue
    {
        public AudioCue(GameAudioEvent gameEvent, AudioChannel channel, string[] clips, float volume = 1f,
            float pitchMin = 1f, float pitchMax = 1f, float minIntervalSeconds = 0.05f, bool haptic = false)
        {
            if (clips == null || clips.Length == 0) throw new ArgumentException("A cue needs at least one clip.", nameof(clips));
            if (volume <= 0f || volume > 1f) throw new ArgumentOutOfRangeException(nameof(volume));
            if (pitchMin <= 0f || pitchMax < pitchMin) throw new ArgumentOutOfRangeException(nameof(pitchMin));
            if (minIntervalSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(minIntervalSeconds));
            Event = gameEvent;
            Channel = channel;
            Clips = clips;
            Volume = volume;
            PitchMin = pitchMin;
            PitchMax = pitchMax;
            MinIntervalSeconds = minIntervalSeconds;
            Haptic = haptic;
        }

        public GameAudioEvent Event { get; }
        public AudioChannel Channel { get; }

        /// <summary>Clip ids = paths relative to client/Assets/MetinAR/Audio, the same keys as Audio/LICENSES.md.</summary>
        public IReadOnlyList<string> Clips { get; }

        public float Volume { get; }
        public float PitchMin { get; }
        public float PitchMax { get; }

        /// <summary>A repeat of the same event inside this window is dropped (anti-spam).</summary>
        public float MinIntervalSeconds { get; }

        /// <summary>Short vibration with the sound (only when the player has vibration on).</summary>
        public bool Haptic { get; }
    }
}
