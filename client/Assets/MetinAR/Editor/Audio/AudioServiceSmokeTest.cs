using System;
using System.Collections.Generic;
using MetinAR.Audio;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace MetinAR.Editor.Audio
{
    /// <summary>
    /// METINAR-040 (#85) editor smoke: inside Unity, every <see cref="GameAudioEvent"/> plays exactly one imported clip
    /// from the Resources library on the expected mixer group, the four volume parameters are exposed on the mixer,
    /// volume 0 maps to −80 dB, and vibration off never reaches the haptics device.
    /// Unity -batchmode -nographics -projectPath client -executeMethod MetinAR.Editor.Audio.AudioServiceSmokeTest.Run
    /// Log: "[AudioServiceSmoke] PASS events=… clips=…" or "[AudioServiceSmoke] FAIL …"; exit code 0/1.
    /// </summary>
    public static class AudioServiceSmokeTest
    {
        private const string Tag = "[AudioServiceSmoke] ";

        public static void Run()
        {
            int failures;
            try
            {
                failures = Execute();
            }
            catch (Exception e)
            {
                Debug.LogError(Tag + "FAIL exception " + e);
                failures = 1;
            }

            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        [MenuItem("MetinAR/Audio/Run Audio Service Smoke")]
        public static void RunMenu() => Execute();

        public static int Execute()
        {
            var errors = new List<string>();
            var library = Resources.Load<AudioLibrary>(AudioService.LibraryResourcePath);
            if (library == null)
            {
                Debug.LogError(Tag + "FAIL library missing at Resources/" + AudioService.LibraryResourcePath);
                return 1;
            }

            AudioMixer mixer = library.Mixer;
            if (mixer == null) errors.Add("library has no mixer");
            else
            {
                foreach (string p in new[] { AudioSettingsModel.MasterParameter, AudioSettingsModel.SfxParameter, AudioSettingsModel.UiParameter, AudioSettingsModel.MusicParameter })
                    if (!mixer.GetFloat(p, out _)) errors.Add("mixer parameter not exposed: " + p);
                foreach (string g in new[] { "SFX", "UI", "Music" })
                    if (mixer.FindMatchingGroups(g).Length == 0) errors.Add("mixer group missing: " + g);
            }

            foreach (string id in AudioCueTable.AllClipIds())
            {
                if (!library.TryGetClip(id, out AudioClip clip)) errors.Add("clip not in library: " + id);
                else if (clip.length <= 0f) errors.Add("clip empty: " + id);
            }

            var output = new LibraryOutput(library);
            var haptics = new CountingHaptics();
            var settings = new AudioSettingsModel();
            double now = 0;
            var director = new AudioEventDirector(output, haptics, settings, () => now);
            int events = 0;
            foreach (GameAudioEvent e in Enum.GetValues(typeof(GameAudioEvent)))
            {
                events++;
                now += 10.0;
                int before = output.Plays;
                bool played = director.Handle(e);
                int plays = output.Plays - before;
                AudioCue cue = AudioCueTable.Get(e);
                if (!played || plays != 1) errors.Add(e + " played " + plays + " clips (expected exactly 1)");
                else if (output.LastChannel != cue.Channel) errors.Add(e + " routed to " + output.LastChannel + " expected " + cue.Channel);
                else Debug.Log(Tag + "event=" + e + " clip=" + output.LastClip + " channel=" + output.LastChannel);
            }

            settings.HapticsEnabled = false;
            int hapticsBefore = haptics.Calls;
            now += 10.0;
            director.Handle(GameAudioEvent.SwordHit);
            now += 10.0;
            director.Handle(GameAudioEvent.PlayerHurt);
            if (haptics.Calls != hapticsBefore) errors.Add("vibration off still vibrated");

            if (AudioSettingsModel.ToDecibels(0f) > -79.9f) errors.Add("volume 0 is not -80 dB");
            errors.AddRange(output.Errors);

            foreach (string err in errors) Debug.LogError(Tag + "FAIL " + err);
            if (errors.Count == 0) Debug.Log(Tag + "PASS events=" + events + " clips=" + AudioCueTable.AllClipIds().Count);
            return errors.Count;
        }

        private sealed class LibraryOutput : IAudioOutput
        {
            private readonly AudioLibrary _library;
            public LibraryOutput(AudioLibrary library) => _library = library;
            public int Plays;
            public string LastClip;
            public AudioChannel LastChannel;
            public readonly List<string> Errors = new List<string>();

            public void PlayOneShot(string clipId, AudioChannel channel, float volume, float pitch)
            {
                Plays++;
                LastClip = clipId;
                LastChannel = channel;
                if (!_library.TryGetClip(clipId, out _)) Errors.Add("played clip not in library: " + clipId);
            }
        }

        private sealed class CountingHaptics : IHapticsOutput
        {
            public int Calls;
            public void Vibrate() => Calls++;
        }
    }
}
