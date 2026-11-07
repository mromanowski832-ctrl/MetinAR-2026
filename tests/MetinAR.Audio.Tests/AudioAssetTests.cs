using System.Text.RegularExpressions;
using MetinAR.Audio;
using MetinAR.Editor.Audio;
using Xunit;

namespace MetinAR.Audio.Tests
{
    /// <summary>Static checks of the committed Unity assets used by the AudioService (no Unity needed).</summary>
    public class AudioAssetTests
    {
        private const string MixerGuid = "ea672754a8a154e47b2a6351d3883b4f";
        private static readonly string LibraryPath =
            Path.Combine(Repo.MetinArRoot(), "Resources", "Audio", "MetinARAudioLibrary.asset");

        private static AudioLicenseLedger Ledger() =>
            AudioLicenseLedger.Parse(File.ReadAllText(Path.Combine(Repo.AudioRoot(), AudioPipelineRules.LicenseLedgerFileName)));

        public static IEnumerable<object[]> ClipIds() => AudioCueTable.AllClipIds().Select(id => new object[] { id });

        [Theory]
        [MemberData(nameof(ClipIds))]
        public void EveryCueClipExistsAndIsLicensedWithMatchingSha(string id)
        {
            string path = Path.Combine(Repo.AudioRoot(), id);
            Assert.True(File.Exists(path), "missing clip " + path);
            Assert.True(File.Exists(path + ".meta"), "missing .meta " + path);

            AudioLicenseLedger ledger = Ledger();
            Assert.Empty(ledger.ParseErrors);
            AudioLicenseEntry entry = ledger.Find(id);
            Assert.True(entry != null, "not in LICENSES.md: " + id);
            Assert.Equal(AudioPipelineRules.Sha256Hex(File.ReadAllBytes(path)), entry.Sha256);
            Assert.True(AudioPipelineRules.IsAllowedLicense(entry.License), id + " licence " + entry.License);
            Assert.False(AudioLicenseLedger.IsRestrictedCc(entry.License), id + " has NC/ND/SA");
            Assert.DoesNotMatch(new Regex("(?i)(^|[-_ ])(NC|ND|SA)([-_ .]|$)"), entry.License);
            string source = (entry.Source ?? "").ToLowerInvariant() + " " + (entry.Author ?? "").ToLowerInvariant();
            foreach (string word in AudioPipelineRules.ForbiddenSourceWords) Assert.DoesNotContain(word, source);
        }

        [Fact]
        public void LibraryAssetReferencesEveryCueClipByItsMetaGuid()
        {
            Assert.True(File.Exists(LibraryPath), LibraryPath);
            Assert.True(File.Exists(LibraryPath + ".meta"));
            string text = File.ReadAllText(LibraryPath).Replace("\r\n", "\n");

            string scriptGuid = Repo.Guid(Path.Combine(Repo.MetinArRoot(), "Scripts", "Audio", "AudioLibrary.cs.meta"));
            Assert.Contains("m_Script: {fileID: 11500000, guid: " + scriptGuid + ", type: 3}", text);
            Assert.Contains("_mixer: {fileID: 24100000, guid: " + MixerGuid + ", type: 2}", text);
            Assert.Equal(MixerGuid, Repo.Guid(Path.Combine(Repo.AudioRoot(), "Mixers", "MetinARMixer.mixer.meta")));

            var entries = new Dictionary<string, string>();
            var m = Regex.Matches(text, @"- id: (\S+)\n    clip: \{fileID: 8300000, guid: ([0-9a-f]{32}), type: 3\}");
            foreach (Match x in m) entries.Add(x.Groups[1].Value, x.Groups[2].Value);

            List<string> ids = AudioCueTable.AllClipIds();
            Assert.Equal(ids.OrderBy(s => s, StringComparer.Ordinal), entries.Keys.OrderBy(s => s, StringComparer.Ordinal));
            foreach (string id in ids)
                Assert.Equal(Repo.Guid(Path.Combine(Repo.AudioRoot(), id + ".meta")), entries[id]);
        }

        [Fact]
        public void MixerExposesVolumeOfEachGroup()
        {
            string text = File.ReadAllText(Path.Combine(Repo.AudioRoot(), "Mixers", "MetinARMixer.mixer")).Replace("\r\n", "\n");
            var volumeGuidByGroup = new Dictionary<string, string>();
            foreach (string doc in text.Split("\n--- "))
            {
                if (!doc.StartsWith("!u!243 &", StringComparison.Ordinal)) continue;
                string name = Regex.Match(doc, @"\n  m_Name: (\S+)").Groups[1].Value;
                string vol = Regex.Match(doc, @"\n  m_Volume: ([0-9a-f]{32})").Groups[1].Value;
                volumeGuidByGroup[name] = vol;
            }

            var exposed = new Dictionary<string, string>();
            foreach (Match x in Regex.Matches(text, @"\n  - guid: ([0-9a-f]{32})\n    name: (\w+)"))
                exposed[x.Groups[2].Value] = x.Groups[1].Value;

            Assert.Equal(4, exposed.Count);
            Assert.Equal(volumeGuidByGroup["Master"], exposed[AudioSettingsModel.MasterParameter]);
            Assert.Equal(volumeGuidByGroup["SFX"], exposed[AudioSettingsModel.SfxParameter]);
            Assert.Equal(volumeGuidByGroup["UI"], exposed[AudioSettingsModel.UiParameter]);
            Assert.Equal(volumeGuidByGroup["Music"], exposed[AudioSettingsModel.MusicParameter]);
        }

        [Fact]
        public void NewScriptsHaveMetaFilesWithUniqueGuids()
        {
            var files = Directory.GetFiles(Path.Combine(Repo.MetinArRoot(), "Scripts", "Audio"), "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".meta", StringComparison.Ordinal))
                .Concat(new[]
                {
                    Path.Combine(Repo.MetinArRoot(), "Editor", "Audio", "AudioLibraryBuilder.cs"),
                    Path.Combine(Repo.MetinArRoot(), "Editor", "Audio", "AudioServiceSmokeTest.cs"),
                    LibraryPath,
                }).ToList();
            var all = Directory.GetFiles(Path.Combine(Repo.Root(), "client", "Assets"), "*.meta", SearchOption.AllDirectories)
                .GroupBy(Repo.Guid).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.Empty(all);
            foreach (string f in files) Assert.True(File.Exists(f + ".meta"), "missing .meta for " + f);
        }

        [Fact]
        public void CoreHasNoEngineReferencesAndOnlyUnityHapticsVibrates()
        {
            string core = Path.Combine(Repo.MetinArRoot(), "Scripts", "Audio", "Core");
            string asmdef = File.ReadAllText(Path.Combine(core, "MetinAR.Audio.asmdef"));
            Assert.Contains("\"noEngineReferences\": true", asmdef);
            foreach (string f in Directory.GetFiles(core, "*.cs"))
                Assert.DoesNotContain("UnityEngine", File.ReadAllText(f));

            var vibrators = Directory.GetFiles(Path.Combine(Repo.Root(), "client", "Assets", "MetinAR"), "*.cs", SearchOption.AllDirectories)
                .Where(f => File.ReadAllText(f).Contains("Handheld.Vibrate()"))
                .Select(Path.GetFileName).ToList();
            Assert.Equal(new[] { "UnityHaptics.cs" }, vibrators);
        }

        [Fact]
        public void DeveloperCombatFilesAreNotReferencedForWriting()
        {
            // The bridge only subscribes to public events; it must not call combat mutators.
            string bridge = File.ReadAllText(Path.Combine(Repo.MetinArRoot(), "Scripts", "Audio", "AudioGameEventBridge.cs"));
            foreach (string mutator in new[] { "RequestAttack", "RequestAura", "HandleHitDetected", "CancelActions", "ConfirmPlacement", "ExitToWorldMap", ".Bind(" })
                Assert.DoesNotContain(mutator, bridge);
        }
    }
}
