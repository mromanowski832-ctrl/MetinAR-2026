using System.Text;
using MetinAR.Editor.Audio;
using Xunit;

namespace AssetPipeline.Tests
{
    /// <summary>METINAR-018: unit tests of the pure audio rules (synthetic inputs).</summary>
    public class AudioPipelineRulesTests
    {
        private const string Sha = "0000000000000000000000000000000000000000000000000000000000000000";

        internal static byte[] Wav(int channels, int rate, int bits, int frames, int formatTag = 1)
        {
            int block = channels * bits / 8;
            int data = frames * block;
            var b = new byte[44 + data];
            void Str(int o, string s) { Encoding.ASCII.GetBytes(s).CopyTo(b, o); }
            void U32(int o, int v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }
            void U16(int o, int v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
            Str(0, "RIFF"); U32(4, 36 + data); Str(8, "WAVE");
            Str(12, "fmt "); U32(16, 16); U16(20, formatTag); U16(22, channels); U32(24, rate); U32(28, rate * block); U16(32, block); U16(34, bits);
            Str(36, "data"); U32(40, data);
            return b;
        }

        private static byte[] Ogg(int channels, int rate, long granule)
        {
            // Page 1: one segment holding a 30-byte Vorbis identification header; page 2: last page with granule.
            var b = new byte[28 + 30 + 27];
            Encoding.ASCII.GetBytes("OggS").CopyTo(b, 0);
            b[5] = 2; b[26] = 1; b[27] = 30;
            b[28] = 1; Encoding.ASCII.GetBytes("vorbis").CopyTo(b, 29);
            b[28 + 11] = (byte)channels;
            b[28 + 12] = (byte)rate; b[28 + 13] = (byte)(rate >> 8); b[28 + 14] = (byte)(rate >> 16);
            int p = 58;
            Encoding.ASCII.GetBytes("OggS").CopyTo(b, p);
            b[p + 5] = 4;
            for (int i = 0; i < 8; i++) { b[p + 6 + i] = (byte)(granule >> (8 * i)); }
            return b;
        }

        internal static string Meta(int loadType, int compression, string quality = "1", int mono = 1, int bg = 0, int preload = 1, bool android = true, bool ios = true, int normalize = 0, bool legacyKeys = false)
        {
            string s = "      serializedVersion: 2\n      loadType: " + loadType + "\n      sampleRateSetting: 0\n      sampleRateOverride: 44100\n      compressionFormat: " + compression +
                       "\n      quality: " + quality + "\n      conversionMode: 0\n      preloadAudioData: " + preload + "\n";
            string overrides = (android ? "    " + (legacyKeys ? "7" : "Android") + ":\n" + s : string.Empty) + (ios ? "    " + (legacyKeys ? "4" : "iPhone") + ":\n" + s : string.Empty);
            return "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nAudioImporter:\n  externalObjects: {}\n  serializedVersion: " + (legacyKeys ? 7 : 8) + "\n  defaultSettings:\n" +
                   s.Replace("      ", "    ") + "  platformSettingOverrides:" + (overrides.Length > 0 ? "\n" + overrides : " {}\n") +
                   "  forceToMono: " + mono + "\n  normalize: " + normalize + "\n  loadInBackground: " + bg + "\n  ambisonic: 0\n  3D: 1\n  userData: \n";
        }

        private static string Ledger(string file, string origin = "original", string license = "MetinAR-Owned", string source = "procedural", string sha = Sha, string author = "MetinAR project")
        {
            return "# x\n\n| File | Origin | Author | License | Source | SHA-256 |\n|---|---|---|---|---|---|\n| `" + file + "` | " + origin + " | " + author + " | " + license + " | " + source + " | `" + sha + "` |\n";
        }

        [Theory]
        [InlineData("Assets/MetinAR/Audio/SFX/Combat/SFX_Combat_SwordHit_01.wav", AudioCategory.CombatSfx)]
        [InlineData("C:\\p\\client\\Assets\\MetinAR\\Audio\\SFX\\UI\\SFX_UI_Click_01.wav", AudioCategory.UiSfx)]
        [InlineData("Assets/MetinAR/Audio/SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Hit_01.wav", AudioCategory.MobSfx)]
        [InlineData("Assets/MetinAR/Audio/Music/MUS_Overworld.ogg", AudioCategory.Music)]
        [InlineData("Assets/MetinAR/Audio/Ambience/AMB_Forest_01.ogg", AudioCategory.Ambience)]
        [InlineData("Assets/MetinAR/Audio/SFX/Mobs/VenomousSpider/Extra/x.wav", AudioCategory.None)]
        [InlineData("Assets/MetinAR/Audio/x.wav", AudioCategory.None)]
        [InlineData("Assets/Samples/Audio/Button Pop.wav", AudioCategory.None)]
        public void ClassifiesFolders(string path, AudioCategory expected)
        {
            Assert.Equal(expected, AudioPipelineRules.ClassifyPath(path));
        }

        [Theory]
        [InlineData("Assets/MetinAR/Audio/SFX/Combat/SFX_Combat_SwordHit_01.wav", true)]
        [InlineData("Assets/MetinAR/Audio/SFX/Combat/SFX_UI_Click_01.wav", false)]
        [InlineData("Assets/MetinAR/Audio/SFX/Combat/sword_hit.wav", false)]
        [InlineData("Assets/MetinAR/Audio/SFX/Combat/SFX_Combat_SwordHit_01.ogg", false)]
        [InlineData("Assets/MetinAR/Audio/SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Step_03.wav", true)]
        [InlineData("Assets/MetinAR/Audio/SFX/Mobs/VenomousSpider/SFX_Mob_Wolf_Step_03.wav", false)]
        [InlineData("Assets/MetinAR/Audio/Music/MUS_Overworld.ogg", true)]
        [InlineData("Assets/MetinAR/Audio/Music/MUS_Overworld.wav", false)]
        public void ChecksNaming(string path, bool ok)
        {
            Assert.Equal(ok, AudioPipelineRules.CheckName(path) == null);
        }

        [Fact]
        public void SfxProfileIsAdpcmDecompressOnLoadMonoAndMusicStreamsVorbis()
        {
            AudioImportProfile sfx = AudioPipelineRules.GetProfile(AudioCategory.MobSfx);
            Assert.Equal(AudioLoadTypeValue.DecompressOnLoad, sfx.LoadType);
            Assert.Equal(AudioCompressionValue.Adpcm, sfx.CompressionFormat);
            Assert.True(sfx.ForceToMono);
            AudioImportProfile music = AudioPipelineRules.GetProfile(AudioCategory.Music);
            Assert.Equal(AudioLoadTypeValue.Streaming, music.LoadType);
            Assert.Equal(AudioCompressionValue.Vorbis, music.CompressionFormat);
            Assert.True(music.LoadInBackground);
            Assert.False(music.PreloadAudioData);
            Assert.Null(AudioPipelineRules.GetProfile(AudioCategory.None));
        }

        [Fact]
        public void ParsesWavHeader()
        {
            AudioFormatInfo i = AudioFormatInfo.ParseWav(Wav(1, 44100, 16, 22050));
            Assert.Null(i.Error);
            Assert.Equal(1, i.Channels);
            Assert.Equal(44100, i.SampleRate);
            Assert.Equal(16, i.BitsPerSample);
            Assert.Equal(0.5, i.Seconds, 6);
        }

        [Fact]
        public void RejectsNonPcmAndGarbageWav()
        {
            Assert.NotNull(AudioFormatInfo.ParseWav(Wav(1, 44100, 16, 10, formatTag: 3)).Error);
            Assert.NotNull(AudioFormatInfo.ParseWav(Encoding.ASCII.GetBytes("ID3 not a wav at all")).Error);
        }

        [Fact]
        public void ParsesOggVorbisHeaderAndLength()
        {
            AudioFormatInfo i = AudioFormatInfo.ParseOggVorbis(Ogg(2, 48000, 48000L * 90));
            Assert.Null(i.Error);
            Assert.Equal(2, i.Channels);
            Assert.Equal(48000, i.SampleRate);
            Assert.Equal(90.0, i.Seconds, 6);
            Assert.NotNull(AudioFormatInfo.ParseOggVorbis(Wav(1, 44100, 16, 100)).Error);
        }

        [Fact]
        public void BudgetCatchesLongStereoAndHighRateSfx()
        {
            AudioBudget b = AudioPipelineRules.GetBudget(AudioCategory.CombatSfx);
            Assert.Empty(b.Check(AudioFormatInfo.ParseWav(Wav(1, 44100, 16, 44100)), 88244));
            Assert.Contains(b.Check(AudioFormatInfo.ParseWav(Wav(2, 44100, 16, 1000)), 4044), p => p.Contains("mono"));
            Assert.Contains(b.Check(AudioFormatInfo.ParseWav(Wav(1, 96000, 16, 1000)), 2044), p => p.Contains("sample rate"));
            Assert.Contains(b.Check(AudioFormatInfo.ParseWav(Wav(1, 22050, 16, 22050 * 3)), 132344), p => p.Contains("duration"));
            Assert.Contains(b.Check(AudioFormatInfo.ParseWav(Wav(1, 44100, 24, 1000)), 3044), p => p.Contains("bits"));
            AudioBudget ui = AudioPipelineRules.GetBudget(AudioCategory.UiSfx);
            Assert.Contains(ui.Check(AudioFormatInfo.ParseWav(Wav(1, 44100, 16, 30000)), 60044), p => p.Contains("duration"));
        }

        [Fact]
        public void MetaReaderMatchesProfile()
        {
            AudioImportProfile sfx = AudioPipelineRules.GetProfile(AudioCategory.CombatSfx);
            Assert.Empty(sfx.Compare(AudioMetaSettings.Parse(Meta(0, 2))));
            AudioMetaSettings m = AudioMetaSettings.Parse(Meta(0, 2));
            Assert.Equal("0123456789abcdef0123456789abcdef", m.Guid);
            Assert.True(m.ForceToMono);
        }

        [Fact]
        public void MetaReaderAcceptsUnity6PlatformNamesAndLegacyNumericKeys()
        {
            AudioImportProfile sfx = AudioPipelineRules.GetProfile(AudioCategory.UiSfx);
            AudioMetaSettings v8 = AudioMetaSettings.Parse(Meta(0, 2));
            AudioMetaSettings v7 = AudioMetaSettings.Parse(Meta(0, 2, legacyKeys: true));
            Assert.Equal(8, v8.SerializedVersion);
            Assert.Equal(7, v7.SerializedVersion);
            Assert.NotNull(v8.Android);
            Assert.NotNull(v8.Ios);
            Assert.NotNull(v7.Android);
            Assert.NotNull(v7.Ios);
            Assert.Empty(sfx.Compare(v8));
            Assert.Empty(sfx.Compare(v7));
        }

        [Fact]
        public void MetaReaderReportsWrongSettings()
        {
            AudioImportProfile sfx = AudioPipelineRules.GetProfile(AudioCategory.CombatSfx);
            Assert.Contains(sfx.Compare(AudioMetaSettings.Parse(Meta(0, 0))), p => p.Contains("PCM"));
            Assert.Contains(sfx.Compare(AudioMetaSettings.Parse(Meta(2, 2))), p => p.Contains("loadType"));
            Assert.Contains(sfx.Compare(AudioMetaSettings.Parse(Meta(0, 2, mono: 0))), p => p.Contains("forceToMono"));
            Assert.Contains(sfx.Compare(AudioMetaSettings.Parse(Meta(0, 2, android: false))), p => p.Contains("Android"));
            Assert.Contains(sfx.Compare(AudioMetaSettings.Parse(Meta(0, 2, ios: false))), p => p.Contains("iOS"));
            Assert.Contains(sfx.Compare(AudioMetaSettings.Parse(Meta(0, 2, normalize: 1))), p => p.Contains("normalize"));
            Assert.Contains(sfx.Compare(AudioMetaSettings.Parse("fileFormatVersion: 2\nTextScriptImporter:\n")), p => p.Contains("AudioImporter"));
            AudioImportProfile music = AudioPipelineRules.GetProfile(AudioCategory.Music);
            Assert.Empty(music.Compare(AudioMetaSettings.Parse(Meta(2, 1, "0.5", mono: 0, bg: 1, preload: 0))));
            Assert.Contains(music.Compare(AudioMetaSettings.Parse(Meta(2, 1, "0.9", mono: 0, bg: 1, preload: 0))), p => p.Contains("quality"));
        }

        [Fact]
        public void LedgerParsesAndValidatesOriginal()
        {
            AudioLicenseLedger l = AudioLicenseLedger.Parse(Ledger("SFX/UI/SFX_UI_Click_01.wav"));
            Assert.Single(l.Entries);
            Assert.Equal("SFX/UI/SFX_UI_Click_01.wav", l.Entries[0].File);
            Assert.Empty(l.Validate());
        }

        [Theory]
        [InlineData("original", "CC0-1.0", "procedural", "must use licence MetinAR-Owned")]
        [InlineData("cc0", "CC0-1.0", "freesound, no link", "URL")]
        [InlineData("cc0", "CC0-1.0", "ripped from Metin2 client https://x.org/a", "proprietary")]
        [InlineData("third_party", "Proprietary-Pack", "https://shop.example/pack", "not allowed")]
        [InlineData("third_party", "CC-BY-4.0", "Jane Doe, https://example.org/sound/2", "not allowed")]
        [InlineData("bought", "MetinAR-Owned", "store", "must be original, cc0 or third_party")]
        public void LedgerRejectsBadProvenance(string origin, string license, string source, string expected)
        {
            var issues = AudioLicenseLedger.Parse(Ledger("SFX/UI/SFX_UI_Click_01.wav", origin, license, source)).Validate();
            Assert.Contains(issues, i => i.Message.Contains(expected));
        }

        [Fact]
        public void LedgerAcceptsDocumentedCc0AndReportsDuplicatesAndBadSha()
        {
            Assert.Empty(AudioLicenseLedger.Parse(Ledger("SFX/UI/SFX_UI_Click_01.wav", "cc0", "CC0-1.0", "Jane Doe, https://example.org/sound/1")).Validate());
            string dup = Ledger("a.wav") + "| `a.wav` | original | x | MetinAR-Owned | y | `" + Sha + "` |\n";
            Assert.Contains(AudioLicenseLedger.Parse(dup).Validate(), i => i.Message.Contains("duplicate"));
            Assert.Contains(AudioLicenseLedger.Parse(Ledger("a.wav", sha: "abc")).Validate(), i => i.Message.Contains("SHA-256"));
            Assert.Contains(AudioLicenseLedger.Parse(Ledger("a.wav", author: " ")).Validate(), i => i.Message.Contains("author"));
        }

        [Fact]
        public void ValidateAssetRequiresLedgerEntryMatchingShaAndMeta()
        {
            byte[] wav = Wav(1, 44100, 16, 4410);
            string path = "Assets/MetinAR/Audio/SFX/UI/SFX_UI_Click_01.wav";
            string sha = AudioPipelineRules.Sha256Hex(wav);
            Assert.Empty(AudioPipelineRules.ValidateAsset(path, wav, Meta(0, 2), AudioLicenseLedger.Parse(Ledger("SFX/UI/SFX_UI_Click_01.wav", sha: sha))));
            Assert.Contains(AudioPipelineRules.ValidateAsset(path, wav, Meta(0, 2), AudioLicenseLedger.Parse(Ledger("SFX/UI/Other_01.wav", sha: sha))), i => i.Message.Contains("no entry"));
            Assert.Contains(AudioPipelineRules.ValidateAsset(path, wav, Meta(0, 2), AudioLicenseLedger.Parse(Ledger("SFX/UI/SFX_UI_Click_01.wav"))), i => i.Message.Contains("does not match"));
            Assert.Contains(AudioPipelineRules.ValidateAsset(path, wav, null, AudioLicenseLedger.Parse(Ledger("SFX/UI/SFX_UI_Click_01.wav", sha: sha))), i => i.Message.Contains(".meta"));
            Assert.Contains(AudioPipelineRules.ValidateAsset(path, wav, Meta(0, 2), null), i => i.Message.Contains("LICENSES.md"));
        }
    }

    /// <summary>
    /// METINAR-018: the licence + budget gate on the real repository content. Every audio file under
    /// client/Assets/MetinAR/Audio needs a LICENSES.md row with a matching SHA-256, must fit its category budget
    /// and must carry the mobile import profile in its .meta file.
    /// </summary>
    public class AudioRepositoryTests
    {
        private static string RepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "client", "Assets")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            Assert.NotNull(dir);
            return dir;
        }

        private static string AudioRoot()
        {
            return Path.Combine(RepoRoot(), "client", "Assets", "MetinAR", "Audio");
        }

        private static List<string> AudioFiles()
        {
            return Directory.GetFiles(AudioRoot(), "*", SearchOption.AllDirectories)
                .Where(f => AudioPipelineRules.IsAudioFile(f))
                .Select(f => f.Replace('\\', '/'))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
        }

        private static AudioLicenseLedger Ledger()
        {
            string path = Path.Combine(AudioRoot(), AudioPipelineRules.LicenseLedgerFileName);
            Assert.True(File.Exists(path), "Audio/LICENSES.md missing");
            return AudioLicenseLedger.Parse(File.ReadAllText(path));
        }

        [Fact]
        public void LedgerItselfIsValid()
        {
            List<AudioIssue> issues = Ledger().Validate();
            Assert.True(issues.Count == 0, string.Join("\n", issues));
        }

        [Fact]
        public void EveryAudioFileHasLicenceBudgetAndMobileImportSettings()
        {
            AudioLicenseLedger ledger = Ledger();
            var issues = new List<AudioIssue>();
            List<string> files = AudioFiles();
            Assert.NotEmpty(files);
            foreach (string f in files)
            {
                string meta = File.Exists(f + ".meta") ? File.ReadAllText(f + ".meta") : null;
                issues.AddRange(AudioPipelineRules.ValidateAsset(f, File.ReadAllBytes(f), meta, ledger));
            }

            Assert.True(issues.Count == 0, string.Join("\n", issues));
        }

        [Fact]
        public void EveryLedgerRowPointsToAnExistingFile()
        {
            var existing = new HashSet<string>(AudioFiles().Select(AudioPipelineRules.LedgerPath));
            List<string> stale = Ledger().Entries.Select(e => e.File).Where(f => !existing.Contains(f)).ToList();
            Assert.True(stale.Count == 0, "LICENSES.md rows without a file: " + string.Join(", ", stale));
        }

        [Fact]
        public void TotalSfxSourceSizeWithinBudget()
        {
            long total = AudioFiles().Where(f => AudioPipelineRules.IsSfx(AudioPipelineRules.ClassifyPath(f))).Sum(f => new FileInfo(f).Length);
            Assert.True(total <= AudioPipelineRules.TotalSfxSourceBytesBudget, "SFX total " + total + " B > " + AudioPipelineRules.TotalSfxSourceBytesBudget + " B");
        }

        [Fact]
        public void StarterCombatSetIsComplete()
        {
            // METINAR-018 scope: sword swing, hit, shield block, spider attack/hit/death/steps, UI click.
            string[] required =
            {
                "SFX/Combat/SFX_Combat_SwordSwing_01.wav",
                "SFX/Combat/SFX_Combat_SwordHit_01.wav",
                "SFX/Combat/SFX_Combat_ShieldBlock_01.wav",
                "SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Attack_01.wav",
                "SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Hit_01.wav",
                "SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Death_01.wav",
                "SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Step_01.wav",
                "SFX/UI/SFX_UI_Click_01.wav",
            };
            var existing = new HashSet<string>(AudioFiles().Select(AudioPipelineRules.LedgerPath));
            Assert.All(required, r => Assert.Contains(r, existing));
        }

        [Fact]
        public void CommittedMetasUseTheUnity6000_6AudioImporterLayout()
        {
            // Layout confirmed on LuzMen (Unity 6000.6.4f1, METINAR-018): AudioImporter serializedVersion 8,
            // platformSettingOverrides keyed by platform name (Android, iPhone).
            foreach (string f in AudioFiles())
            {
                AudioMetaSettings m = AudioMetaSettings.Parse(File.ReadAllText(f + ".meta"));
                Assert.True(m.SerializedVersion == 8, f + ".meta AudioImporter serializedVersion " + m.SerializedVersion + " (expected 8)");
                string text = File.ReadAllText(f + ".meta");
                Assert.Contains("\n    Android:\n", text.Replace("\r\n", "\n"));
                Assert.Contains("\n    iPhone:\n", text.Replace("\r\n", "\n"));
            }
        }

        [Fact]
        public void MetaGuidsAreUniqueAcrossTheProject()
        {
            var seen = new Dictionary<string, string>();
            foreach (string f in AudioFiles())
            {
                string guid = AudioMetaSettings.Parse(File.ReadAllText(f + ".meta")).Guid;
                Assert.False(string.IsNullOrEmpty(guid), f + ".meta has no guid");
                Assert.False(seen.ContainsKey(guid), "duplicate guid " + guid + " in " + f + " and " + (seen.ContainsKey(guid) ? seen[guid] : string.Empty));
                seen[guid] = f;
            }

            string assets = Path.Combine(RepoRoot(), "client", "Assets");
            foreach (string meta in Directory.GetFiles(assets, "*.meta", SearchOption.AllDirectories))
            {
                string norm = meta.Replace('\\', '/');
                if (seen.Values.Any(v => v + ".meta" == norm))
                {
                    continue;
                }

                foreach (string line in File.ReadLines(meta).Take(3))
                {
                    if (line.StartsWith("guid: ", StringComparison.Ordinal))
                    {
                        string g = line.Substring(6).Trim();
                        Assert.False(seen.ContainsKey(g), "audio guid " + g + " collides with " + meta);
                    }
                }
            }
        }
    }
}
