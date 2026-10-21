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
        [InlineData("third_party", "CC-BY-4.0", "Jane Doe, https://example.org/sound/2", "requires origin 'cc_by'")]
        [InlineData("bought", "MetinAR-Owned", "store", "must be original, cc0 or cc_by")]
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

        // ---- CC-BY (owner decision 2026-10-21, #42): allowed with full attribution; NC / ND / SA excluded.

        private const string CcByFile = "SFX/Combat/SFX_Combat_SwordHit_02.wav";
        private const string CcByAuthor = "Jane Doe";

        private const string AttributionHeader = "| File | Title | Author | Source URL | License | License URL | Attribution | Changes |\n|---|---|---|---|---|---|---|---|\n";

        private static string DefaultLicenseUrl(string license)
        {
            return AudioLicenseLedger.CcByLicenseUrl(license) ?? "https://creativecommons.org/licenses/by/4.0/";
        }

        private static string DefaultAttribution(string license)
        {
            string version = license.StartsWith("CC-BY-", StringComparison.Ordinal) ? license.Substring(6) : "4.0";
            return "\"Steel Hit\" by Jane Doe (https://example.org/sounds/123), licensed under CC BY " + version + "; trimmed and converted to mono";
        }

        private static string CcByLedger(string license = "CC-BY-4.0", string title = "Steel Hit", string author = CcByAuthor,
            string url = "https://example.org/sounds/123", string attrLicense = null, string changes = "trimmed to 0.4 s, mono, peak -1 dBFS",
            bool attributionRow = true, string ledgerSource = "Jane Doe, https://example.org/sounds/123", string licenseUrl = null, string attribution = null)
        {
            string text = Ledger(CcByFile, "cc_by", license, ledgerSource, author: CcByAuthor);
            if (attributionRow)
            {
                text += "\n## CC-BY attribution\n\n" + AttributionHeader +
                        "| `" + CcByFile + "` | " + title + " | " + author + " | " + url + " | " + (attrLicense ?? license) + " | " +
                        (licenseUrl ?? DefaultLicenseUrl(attrLicense ?? license)) + " | " + (attribution ?? DefaultAttribution(license)) + " | " + changes + " |\n";
            }

            return text;
        }

        [Theory]
        [InlineData("CC-BY-4.0")]
        [InlineData("CC-BY-3.0")]
        [InlineData("CC-BY-2.5")]
        public void CcByWithCompleteAttributionIsValid(string license)
        {
            AudioLicenseLedger l = AudioLicenseLedger.Parse(CcByLedger(license));
            Assert.Single(l.Attributions);
            Assert.Equal("Steel Hit", l.Attributions[0].Title);
            Assert.Equal(AudioLicenseLedger.CcByLicenseUrl(license), l.Attributions[0].LicenseUrl);
            Assert.Contains("Jane Doe", l.Attributions[0].Attribution);
            List<AudioIssue> issues = l.Validate();
            Assert.True(issues.Count == 0, string.Join("\n", issues));
            Assert.True(AudioPipelineRules.IsAllowedLicense(license));
        }

        [Theory]
        [InlineData("https://creativecommons.org/licenses/by/4.0")]
        [InlineData("HTTPS://creativecommons.org/licenses/by/4.0/")]
        public void LicenceUrlWithoutTrailingSlashOrUppercaseSchemeIsAccepted(string url)
        {
            Assert.Empty(AudioLicenseLedger.Parse(CcByLedger(licenseUrl: url)).Validate());
        }

        [Theory]
        [InlineData("Steel Hit by Jane Doe — CC-BY-4.0")]
        [InlineData("steel hit / jane doe / cc by 4.0 / https://example.org/sounds/123")]
        public void AttributionTextAcceptsCommonSpellings(string attribution)
        {
            Assert.Empty(AudioLicenseLedger.Parse(CcByLedger(attribution: attribution)).Validate());
        }

        [Fact]
        public void OutdatedSixColumnAttributionTableIsRejected()
        {
            string text = Ledger(CcByFile, "cc_by", "CC-BY-4.0", "Jane Doe, https://example.org/sounds/123", author: CcByAuthor) +
                          "\n| File | Title | Author | Source URL | License | Changes |\n|---|---|---|---|---|---|\n" +
                          "| `" + CcByFile + "` | Steel Hit | Jane Doe | https://example.org/sounds/123 | CC-BY-4.0 | unmodified |\n";
            List<AudioIssue> issues = AudioLicenseLedger.Parse(text).Validate();
            Assert.Contains(issues, i => i.Message.Contains("outdated CC-BY attribution table"));
            Assert.Contains(issues, i => i.Message.Contains("attribution table"));
        }

        [Fact]
        public void CcByUnmodifiedIsAcceptedWhenStatedExplicitly()
        {
            Assert.Empty(AudioLicenseLedger.Parse(CcByLedger(changes: "unmodified")).Validate());
        }

        [Theory]
        [InlineData("CC-BY-NC-4.0")]
        [InlineData("CC-BY-ND-4.0")]
        [InlineData("CC-BY-SA-4.0")]
        [InlineData("CC-BY-NC-SA-4.0")]
        [InlineData("CC-BY-NC-ND-4.0")]
        [InlineData("CC-BY-SA-3.0")]
        [InlineData("cc-by-nc-4.0")]
        [InlineData("CC BY-NC 4.0")]
        public void CcByWithNcNdSaIsRejected(string license)
        {
            Assert.True(AudioLicenseLedger.IsRestrictedCc(license));
            Assert.False(AudioPipelineRules.IsAllowedLicense(license));
            List<AudioIssue> issues = AudioLicenseLedger.Parse(CcByLedger(license)).Validate();
            Assert.Contains(issues, i => i.Message.Contains("NC/ND/SA"));
        }

        [Theory]
        [InlineData("CC-BY")]
        [InlineData("CC-BY-4")]
        [InlineData("CC-BY-4.0-international")]
        [InlineData("CC BY 4.0")]
        public void MalformedCcByVersionIsRejected(string license)
        {
            Assert.False(AudioLicenseLedger.IsRestrictedCc(license));
            List<AudioIssue> issues = AudioLicenseLedger.Parse(CcByLedger(license)).Validate();
            Assert.Contains(issues, i => i.Message.Contains("not allowed"));
        }

        [Theory]
        [InlineData("title", "", "title missing")]
        [InlineData("title", "-", "title missing")]
        [InlineData("author", " ", "author missing")]
        [InlineData("author", "Someone Else", "differs from the ledger author")]
        [InlineData("url", "", "source URL")]
        [InlineData("url", "example.org/sounds/123", "source URL")]
        [InlineData("license", "", "licence version missing")]
        [InlineData("license", "CC-BY-3.0", "differs from the ledger licence")]
        [InlineData("license", "CC-BY-SA-4.0", "NC/ND/SA")]
        [InlineData("changes", "", "changes missing")]
        [InlineData("changes", "—", "changes missing")]
        [InlineData("licenseUrl", "", "licence URL missing")]
        [InlineData("licenseUrl", "https://creativecommons.org/licenses/by/3.0/", "licence URL must be https://creativecommons.org/licenses/by/4.0/")]
        [InlineData("licenseUrl", "https://creativecommons.org/licenses/by-sa/4.0/", "licence URL must be")]
        [InlineData("attribution", "", "attribution text missing")]
        [InlineData("attribution", "-", "attribution text missing")]
        [InlineData("attribution", "Sound by Jane Doe, CC BY 4.0", "must name the title")]
        [InlineData("attribution", "\"Steel Hit\", CC BY 4.0", "must name the author")]
        [InlineData("attribution", "\"Steel Hit\" by Jane Doe", "must name the licence")]
        [InlineData("attribution", "\"Steel Hit\" by Jane Doe, CC BY 3.0", "must name the licence")]
        public void CcByAttributionFieldsAreRequired(string field, string value, string expected)
        {
            string text = field switch
            {
                "title" => CcByLedger(title: value),
                "author" => CcByLedger(author: value),
                "url" => CcByLedger(url: value),
                "license" => CcByLedger(attrLicense: value),
                "licenseUrl" => CcByLedger(licenseUrl: value),
                "attribution" => CcByLedger(attribution: value),
                _ => CcByLedger(changes: value),
            };
            List<AudioIssue> issues = AudioLicenseLedger.Parse(text).Validate();
            Assert.Contains(issues, i => i.Message.Contains(expected));
        }

        [Fact]
        public void ExamplesInFencedBlocksAreNotLedgerData()
        {
            string text = "```text\n| File | Origin | Author | License | Source | SHA-256 |\n|---|---|---|---|---|---|\n| `x.wav` | cc_by | A | CC-BY-4.0 | https://e.org/x | `" + Sha + "` |\n```\n" + CcByLedger();
            AudioLicenseLedger l = AudioLicenseLedger.Parse(text);
            Assert.Single(l.Entries);
            Assert.Equal(CcByFile, l.Entries[0].File);
            Assert.Empty(l.Validate());
        }

        [Fact]
        public void CcByWithoutAttributionRowIsRejected()
        {
            Assert.Contains(AudioLicenseLedger.Parse(CcByLedger(attributionRow: false)).Validate(), i => i.Message.Contains("attribution table"));
        }

        [Fact]
        public void CcByLedgerRowNeedsUrlAndAuthor()
        {
            Assert.Contains(AudioLicenseLedger.Parse(CcByLedger(ledgerSource: "Jane Doe, freesound")).Validate(), i => i.Message.Contains("download URL"));
        }

        [Fact]
        public void AttributionRowWithoutCcByEntryIsRejected()
        {
            string text = Ledger("SFX/UI/SFX_UI_Click_01.wav") +
                          "\n" + AttributionHeader +
                          "| `SFX/UI/SFX_UI_Click_01.wav` | Click | x | https://example.org/a | CC-BY-4.0 | https://creativecommons.org/licenses/by/4.0/ | Click by x, CC BY 4.0 | none |\n";
            Assert.Contains(AudioLicenseLedger.Parse(text).Validate(), i => i.Message.Contains("without a cc_by ledger entry"));
        }

        [Fact]
        public void DuplicateAttributionRowsAndWrongColumnCountAreParseErrors()
        {
            string row = "| `" + CcByFile + "` | Steel Hit | Jane Doe | https://example.org/sounds/123 | CC-BY-4.0 | https://creativecommons.org/licenses/by/4.0/ | Steel Hit by Jane Doe, CC BY 4.0 | unmodified |\n";
            Assert.Contains(AudioLicenseLedger.Parse(CcByLedger() + "\n" + AttributionHeader + row + row).Validate(),
                i => i.Message.Contains("duplicate attribution"));
            Assert.Contains(AudioLicenseLedger.Parse(CcByLedger(changes: "a | b")).Validate(), i => i.Message.Contains("columns"));
        }

        [Fact]
        public void CcByLicenceOnOriginalOrCc0OriginIsRejected()
        {
            Assert.Contains(AudioLicenseLedger.Parse(Ledger("a.wav", "original", "CC-BY-4.0")).Validate(), i => i.Message.Contains("MetinAR-Owned"));
            Assert.Contains(AudioLicenseLedger.Parse(Ledger("a.wav", "cc0", "CC-BY-4.0", "x https://example.org/a")).Validate(), i => i.Message.Contains("CC0-1.0"));
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
        public void ProjectMixerHasMasterWithSfxUiMusicChildren()
        {
            // METINAR-018: Audio/Mixers/MetinARMixer.mixer created by AudioMixerBuilder in Unity 6000.6.4f1 batch mode
            // and re-verified in a separate Unity process (AudioImportVerifier). Here: static YAML check.
            string path = Path.Combine(AudioRoot(), "Mixers", "MetinARMixer.mixer");
            Assert.True(File.Exists(path), "mixer missing: " + path);
            Assert.True(File.Exists(path + ".meta"), "mixer .meta missing");
            string text = File.ReadAllText(path).Replace("\r\n", "\n");
            var groups = new Dictionary<string, string>();   // name -> fileID
            var children = new Dictionary<string, List<string>>();
            foreach (string doc in text.Split(new[] { "\n--- " }, StringSplitOptions.None))
            {
                if (!doc.StartsWith("!u!243 &", StringComparison.Ordinal))
                {
                    continue; // 243 = AudioMixerGroupController
                }

                string id = doc.Substring(8, doc.IndexOf('\n') - 8).Trim();
                string name = doc.Split('\n').Select(l => l.Trim()).First(l => l.StartsWith("m_Name:", StringComparison.Ordinal)).Substring(7).Trim();
                groups[name] = id;
                var ids = new List<string>();
                bool inChildren = false;
                foreach (string line in doc.Split('\n'))
                {
                    string l = line.Trim();
                    if (l.StartsWith("m_Children:", StringComparison.Ordinal))
                    {
                        inChildren = true;
                        continue;
                    }

                    if (inChildren && l.StartsWith("- {fileID: ", StringComparison.Ordinal))
                    {
                        ids.Add(l.Substring(11).TrimEnd('}').Trim());
                    }
                    else if (inChildren)
                    {
                        inChildren = false;
                    }
                }

                children[name] = ids;
            }

            Assert.Contains("Master", groups.Keys);
            Assert.Contains("m_MasterGroup: {fileID: " + groups["Master"] + "}", text);
            foreach (string child in new[] { "SFX", "UI", "Music" })
            {
                Assert.True(groups.ContainsKey(child), "group " + child + " missing");
                Assert.Contains(groups[child], children["Master"]);
            }

            Assert.Equal(4, groups.Count);
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
