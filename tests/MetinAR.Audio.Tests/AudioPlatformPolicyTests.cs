using System.Text.Json;
using Xunit;

namespace MetinAR.Audio.Tests
{
    /// <summary>
    /// METINAR-040 (#85): haptics need android.permission.VIBRATE (normal level, approved by the production manager on
    /// #85). The QA ApkAudit gate (tools/qa/Invoke-MetinARQa.ps1 → Test-MetinARPermissionPolicy) reads
    /// tools/qa/qa-config.json: VIBRATE must stay on the allow-list and off the forbidden list, so the audit passes it.
    /// The player must still be able to turn vibration off in game ("Wibracje: WŁ/WYŁ").
    /// </summary>
    public class AudioPlatformPolicyTests
    {
        private const string Vibrate = "android.permission.VIBRATE";

        private static (List<string> allowed, List<string> forbidden) Permissions()
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo.Root(), "tools", "qa", "qa-config.json")));
            JsonElement p = doc.RootElement.GetProperty("permissions");
            List<string> Read(string name) => p.GetProperty(name).EnumerateArray().Select(e => e.GetString()!).ToList();
            return (Read("allowed"), Read("forbidden"));
        }

        [Fact]
        public void ApkAuditAllowsVibrateAndStillForbidsBackgroundLocation()
        {
            var (allowed, forbidden) = Permissions();
            Assert.Contains(Vibrate, allowed);
            Assert.DoesNotContain(Vibrate, forbidden);
            Assert.Contains("android.permission.ACCESS_BACKGROUND_LOCATION", forbidden);
            Assert.DoesNotContain("android.permission.ACCESS_BACKGROUND_LOCATION", allowed);
        }

        [Fact]
        public void SettingsScreenHasVibrationSwitchWiredToSettings()
        {
            string overlay = File.ReadAllText(Path.Combine(Repo.MetinArRoot(), "Scripts", "Audio", "AudioSettingsOverlay.cs"));
            Assert.Contains("haptics.onClick.AddListener(ToggleHaptics)", overlay);
            Assert.Contains("s.HapticsEnabled = !s.HapticsEnabled", overlay);
            Assert.Contains("\"Wibracje: WŁ\"", overlay);
            Assert.Contains("\"Wibracje: WYŁ\"", overlay);

            string service = File.ReadAllText(Path.Combine(Repo.MetinArRoot(), "Scripts", "Audio", "AudioService.cs"));
            Assert.Contains("public void SetHaptics(bool enabled) => Settings.HapticsEnabled = enabled;", service);
        }

        [Fact]
        public void SwitchingVibrationOffInSettingsStopsAllVibrationAndPersists()
        {
            var store = new MemoryStore();
            var settings = new AudioSettingsModel(store);
            settings.HapticsEnabled = false; // what the "Wibracje: WYŁ" button does
            var haptics = new CountingHaptics();
            double t = 0;
            var director = new AudioEventDirector(new RecordingOutput(), haptics, settings, () => t, 1);
            foreach (GameAudioEvent e in new[] { GameAudioEvent.SwordHit, GameAudioEvent.SwordCrit, GameAudioEvent.ShieldBlock, GameAudioEvent.PlayerHurt })
            {
                t += 1;
                director.Handle(e);
            }
            Assert.Equal(0, haptics.Calls);
            Assert.False(new AudioSettingsModel(store).HapticsEnabled);
        }
    }
}
