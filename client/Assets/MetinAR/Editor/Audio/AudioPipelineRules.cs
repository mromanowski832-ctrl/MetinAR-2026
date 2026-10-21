using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MetinAR.Editor.Audio
{
    /// <summary>
    /// METINAR-018: pure (UnityEngine-free) rules of the audio pipeline: folder layout, naming, mobile import
    /// profiles for the Samsung Galaxy A31, per-category budgets, the licence ledger (Audio/LICENSES.md) and a
    /// reader for the AudioImporter section of .meta files. Used by the Unity import postprocessor and covered by
    /// tests/AssetPipeline.Tests. Budget numbers are PROPOSALS (docs/METINAR-018-AUDIO-PIPELINE.md) until measured
    /// on the device and confirmed by the owner.
    /// </summary>
    public static class AudioPipelineRules
    {
        public const string Root = "Assets/MetinAR/Audio";
        public const string SfxFolder = Root + "/SFX";
        public const string CombatSfxFolder = SfxFolder + "/Combat";
        public const string MobSfxFolder = SfxFolder + "/Mobs";
        public const string UiSfxFolder = SfxFolder + "/UI";
        public const string MusicFolder = Root + "/Music";
        public const string AmbienceFolder = Root + "/Ambience";
        public const string LicenseLedgerFileName = "LICENSES.md";

        /// <summary>
        /// Keys of AudioImporter platformSettingOverrides. Unity 6000.6 (AudioImporter serializedVersion 8) writes
        /// platform names; older layouts (serializedVersion 7) used BuildTargetGroup numbers (Android 7, iOS 4).
        /// Both are accepted when reading.
        /// </summary>
        public const string AndroidOverrideKey = "Android";
        public const string IosOverrideKey = "iPhone";
        public const string LegacyAndroidOverrideKey = "7";
        public const string LegacyIosOverrideKey = "4";

        public static readonly string[] AudioExtensions = { ".wav", ".ogg", ".mp3", ".aif", ".aiff", ".flac", ".m4a", ".aac" };

        /// <summary>
        /// Fixed licences accepted in the ledger. In addition, plain attribution licences CC-BY-x.y are accepted with a
        /// complete attribution row (owner decision 2026-10-21, #42). NC / ND / SA variants stay excluded; any other
        /// licence needs an owner decision first.
        /// </summary>
        public static readonly string[] AllowedLicenses = { "MetinAR-Owned", "CC0-1.0" };

        public static bool IsAllowedLicense(string license)
        {
            return Array.IndexOf(AllowedLicenses, license) >= 0 || AudioLicenseLedger.IsCcBy(license);
        }

        /// <summary>Words that must never appear as the source of a MetinAR sound (proprietary game content).</summary>
        public static readonly string[] ForbiddenSourceWords = { "metin2", "metin 2", "gameforge", "ymir" };

        private const string IdPattern = "[A-Z][A-Za-z0-9]*";
        private static readonly Regex SfxCombatUiRegex = new Regex("^SFX_(Combat|UI)_(" + IdPattern + ")_(\\d{2})\\.wav$");
        private static readonly Regex SfxMobRegex = new Regex("^SFX_Mob_(" + IdPattern + ")_(" + IdPattern + ")_(\\d{2})\\.wav$");
        private static readonly Regex MusicRegex = new Regex("^MUS_(" + IdPattern + ")(?:_(\\d{2}))?\\.ogg$");
        private static readonly Regex AmbienceRegex = new Regex("^AMB_(" + IdPattern + ")_(\\d{2})\\.ogg$");

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            string p = path.Replace('\\', '/');
            int i = p.IndexOf("Assets/", StringComparison.Ordinal);
            return i >= 0 ? p.Substring(i) : p;
        }

        public static bool IsAudioFile(string path)
        {
            string p = NormalizePath(path).ToLowerInvariant();
            foreach (string ext in AudioExtensions)
            {
                if (p.EndsWith(ext, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsPipelinePath(string path)
        {
            return NormalizePath(path).StartsWith(Root + "/", StringComparison.Ordinal);
        }

        /// <summary>Path relative to the Audio root, as written in LICENSES.md (e.g. "SFX/UI/SFX_UI_Click_01.wav").</summary>
        public static string LedgerPath(string path)
        {
            string p = NormalizePath(path);
            return p.StartsWith(Root + "/", StringComparison.Ordinal) ? p.Substring(Root.Length + 1) : p;
        }

        public static AudioCategory ClassifyPath(string path)
        {
            string p = NormalizePath(path);
            string folder = p.Contains("/") ? p.Substring(0, p.LastIndexOf('/')) : string.Empty;
            if (folder == CombatSfxFolder)
            {
                return AudioCategory.CombatSfx;
            }

            if (folder == UiSfxFolder)
            {
                return AudioCategory.UiSfx;
            }

            if (folder.StartsWith(MobSfxFolder + "/", StringComparison.Ordinal) && folder.IndexOf('/', MobSfxFolder.Length + 1) < 0)
            {
                return AudioCategory.MobSfx;
            }

            if (folder == MusicFolder)
            {
                return AudioCategory.Music;
            }

            if (folder == AmbienceFolder)
            {
                return AudioCategory.Ambience;
            }

            return AudioCategory.None;
        }

        public static bool IsSfx(AudioCategory c)
        {
            return c == AudioCategory.CombatSfx || c == AudioCategory.MobSfx || c == AudioCategory.UiSfx;
        }

        /// <summary>Returns null when the file name follows the convention for its folder, else the reason.</summary>
        public static string CheckName(string path)
        {
            string p = NormalizePath(path);
            string file = p.Substring(p.LastIndexOf('/') + 1);
            AudioCategory c = ClassifyPath(p);
            switch (c)
            {
                case AudioCategory.CombatSfx:
                case AudioCategory.UiSfx:
                {
                    Match m = SfxCombatUiRegex.Match(file);
                    string expected = c == AudioCategory.CombatSfx ? "Combat" : "UI";
                    if (!m.Success || m.Groups[1].Value != expected)
                    {
                        return "expected SFX_" + expected + "_<Event>_<NN>.wav";
                    }

                    return null;
                }

                case AudioCategory.MobSfx:
                {
                    string mobFolder = p.Substring(MobSfxFolder.Length + 1);
                    mobFolder = mobFolder.Substring(0, mobFolder.IndexOf('/'));
                    Match m = SfxMobRegex.Match(file);
                    if (!m.Success || m.Groups[1].Value != mobFolder)
                    {
                        return "expected SFX_Mob_" + mobFolder + "_<Event>_<NN>.wav";
                    }

                    return null;
                }

                case AudioCategory.Music:
                    return MusicRegex.IsMatch(file) ? null : "expected MUS_<Name>[_<NN>].ogg";
                case AudioCategory.Ambience:
                    return AmbienceRegex.IsMatch(file) ? null : "expected AMB_<Name>_<NN>.ogg";
                default:
                    return "audio file outside the pipeline folders (SFX/Combat, SFX/Mobs/<MobId>, SFX/UI, Music, Ambience)";
            }
        }

        /// <summary>
        /// Import profile for the A31 (and default for every platform). SFX: ADPCM + Decompress On Load (smallest
        /// runtime CPU, ~3.5x smaller in the build than PCM; tiny PCM footprint in RAM for clips under 2 s).
        /// Music: Vorbis + Streaming (never fully in RAM). Ambience: Vorbis + Compressed In Memory.
        /// </summary>
        public static AudioImportProfile GetProfile(AudioCategory category)
        {
            switch (category)
            {
                case AudioCategory.CombatSfx:
                case AudioCategory.MobSfx:
                case AudioCategory.UiSfx:
                    return new AudioImportProfile(AudioLoadTypeValue.DecompressOnLoad, AudioCompressionValue.Adpcm, 1f, true, false, true);
                case AudioCategory.Music:
                    return new AudioImportProfile(AudioLoadTypeValue.Streaming, AudioCompressionValue.Vorbis, 0.5f, false, true, false);
                case AudioCategory.Ambience:
                    return new AudioImportProfile(AudioLoadTypeValue.CompressedInMemory, AudioCompressionValue.Vorbis, 0.4f, false, true, false);
                default:
                    return null;
            }
        }

        public static AudioBudget GetBudget(AudioCategory category)
        {
            switch (category)
            {
                case AudioCategory.CombatSfx:
                case AudioCategory.MobSfx:
                    return new AudioBudget(".wav", 2.0, 256 * 1024, 48000, true, 16);
                case AudioCategory.UiSfx:
                    return new AudioBudget(".wav", 0.5, 64 * 1024, 48000, true, 16);
                case AudioCategory.Music:
                    return new AudioBudget(".ogg", 240.0, 6 * 1024 * 1024, 48000, false, 0);
                case AudioCategory.Ambience:
                    return new AudioBudget(".ogg", 60.0, 2 * 1024 * 1024, 48000, false, 0);
                default:
                    return null;
            }
        }

        /// <summary>Budget for all SFX source files together (lossless WAV in the repo, before Unity compression).</summary>
        public const long TotalSfxSourceBytesBudget = 2L * 1024 * 1024;

        public static string Sha256Hex(byte[] data)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] h = sha.ComputeHash(data);
                var sb = new StringBuilder(64);
                foreach (byte b in h)
                {
                    sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }

                return sb.ToString();
            }
        }

        /// <summary>Full validation of one audio asset: name, folder, format, budget, licence entry and .meta import settings.</summary>
        public static List<AudioIssue> ValidateAsset(string assetPath, byte[] data, string metaText, AudioLicenseLedger ledger)
        {
            var issues = new List<AudioIssue>();
            string p = NormalizePath(assetPath);
            AudioCategory category = ClassifyPath(p);
            string nameProblem = CheckName(p);
            if (nameProblem != null)
            {
                issues.Add(AudioIssue.Error(p, "name/folder: " + nameProblem));
            }

            AudioBudget budget = GetBudget(category);
            if (budget != null)
            {
                if (!p.EndsWith(budget.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(AudioIssue.Error(p, "format: " + category + " must be " + budget.Extension));
                }

                AudioFormatInfo info = budget.Extension == ".wav" ? AudioFormatInfo.ParseWav(data) : AudioFormatInfo.ParseOggVorbis(data);
                if (info.Error != null)
                {
                    issues.Add(AudioIssue.Error(p, "format: " + info.Error));
                }
                else
                {
                    foreach (string problem in budget.Check(info, data.LongLength))
                    {
                        issues.Add(AudioIssue.Error(p, "budget: " + problem));
                    }
                }
            }

            if (ledger == null)
            {
                issues.Add(AudioIssue.Error(p, "licence: " + LicenseLedgerFileName + " missing"));
            }
            else
            {
                AudioLicenseEntry entry = ledger.Find(LedgerPath(p));
                if (entry == null)
                {
                    issues.Add(AudioIssue.Error(p, "licence: no entry in " + LicenseLedgerFileName));
                }
                else if (!string.Equals(entry.Sha256, Sha256Hex(data), StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(AudioIssue.Error(p, "licence: SHA-256 in " + LicenseLedgerFileName + " does not match the file (re-check provenance)"));
                }
            }

            AudioImportProfile profile = GetProfile(category);
            if (profile != null)
            {
                if (metaText == null)
                {
                    issues.Add(AudioIssue.Error(p, "meta: .meta file missing"));
                }
                else
                {
                    AudioMetaSettings meta = AudioMetaSettings.Parse(metaText);
                    foreach (string problem in profile.Compare(meta))
                    {
                        issues.Add(AudioIssue.Error(p, "meta: " + problem));
                    }
                }
            }

            return issues;
        }
    }

    public enum AudioCategory
    {
        None,
        CombatSfx,
        MobSfx,
        UiSfx,
        Music,
        Ambience,
    }

    /// <summary>Numeric values of UnityEngine.AudioClipLoadType as serialised in .meta files.</summary>
    public static class AudioLoadTypeValue
    {
        public const int DecompressOnLoad = 0;
        public const int CompressedInMemory = 1;
        public const int Streaming = 2;
    }

    /// <summary>Numeric values of UnityEngine.AudioCompressionFormat as serialised in .meta files.</summary>
    public static class AudioCompressionValue
    {
        public const int Pcm = 0;
        public const int Vorbis = 1;
        public const int Adpcm = 2;
    }

    public sealed class AudioImportProfile
    {
        public AudioImportProfile(int loadType, int compressionFormat, float quality, bool forceToMono, bool loadInBackground, bool preloadAudioData)
        {
            LoadType = loadType;
            CompressionFormat = compressionFormat;
            Quality = quality;
            ForceToMono = forceToMono;
            LoadInBackground = loadInBackground;
            PreloadAudioData = preloadAudioData;
        }

        public int LoadType { get; }
        public int CompressionFormat { get; }
        public float Quality { get; }
        public bool ForceToMono { get; }
        public bool LoadInBackground { get; }
        public bool PreloadAudioData { get; }

        /// <summary>Differences between the profile and a parsed .meta (default settings and the Android override).</summary>
        public List<string> Compare(AudioMetaSettings meta)
        {
            var problems = new List<string>();
            if (!meta.IsAudioImporter)
            {
                problems.Add("not an AudioImporter .meta");
                return problems;
            }

            CompareFlag(problems, "forceToMono", meta.ForceToMono, ForceToMono);
            CompareFlag(problems, "loadInBackground", meta.LoadInBackground, LoadInBackground);
            CompareFlag(problems, "normalize", meta.Normalize, false);
            CompareSample(problems, "defaultSettings", meta.Default);
            if (meta.Android == null)
            {
                problems.Add("Android override (platformSettingOverrides." + AudioPipelineRules.AndroidOverrideKey + ") missing");
            }
            else
            {
                CompareSample(problems, "Android", meta.Android);
            }

            if (meta.Ios == null)
            {
                problems.Add("iOS override (platformSettingOverrides." + AudioPipelineRules.IosOverrideKey + ") missing");
            }
            else
            {
                CompareSample(problems, "iOS", meta.Ios);
            }

            return problems;
        }

        private void CompareSample(List<string> problems, string label, AudioMetaSampleSettings s)
        {
            if (s == null)
            {
                problems.Add(label + " missing");
                return;
            }

            if (s.LoadType != LoadType)
            {
                problems.Add(label + ".loadType=" + s.LoadType + " expected " + LoadType);
            }

            if (s.CompressionFormat != CompressionFormat)
            {
                problems.Add(label + ".compressionFormat=" + s.CompressionFormat + " expected " + CompressionFormat);
            }

            if (s.CompressionFormat == AudioCompressionValue.Vorbis && Math.Abs(s.Quality - Quality) > 0.011f)
            {
                problems.Add(label + ".quality=" + s.Quality.ToString(CultureInfo.InvariantCulture) + " expected " + Quality.ToString(CultureInfo.InvariantCulture));
            }

            if (s.PreloadAudioData != PreloadAudioData)
            {
                problems.Add(label + ".preloadAudioData=" + s.PreloadAudioData + " expected " + PreloadAudioData);
            }

            if (s.CompressionFormat == AudioCompressionValue.Pcm)
            {
                problems.Add(label + ".compressionFormat is PCM (uncompressed) - not allowed on mobile");
            }
        }

        private static void CompareFlag(List<string> problems, string name, bool? actual, bool expected)
        {
            if (actual == null)
            {
                problems.Add(name + " missing");
            }
            else if (actual.Value != expected)
            {
                problems.Add(name + "=" + actual.Value + " expected " + expected);
            }
        }
    }

    public sealed class AudioBudget
    {
        public AudioBudget(string extension, double maxSeconds, long maxBytes, int maxSampleRate, bool requireMono, int requiredBits)
        {
            Extension = extension;
            MaxSeconds = maxSeconds;
            MaxBytes = maxBytes;
            MaxSampleRate = maxSampleRate;
            RequireMono = requireMono;
            RequiredBits = requiredBits;
        }

        public string Extension { get; }
        public double MaxSeconds { get; }
        public long MaxBytes { get; }
        public int MaxSampleRate { get; }
        public bool RequireMono { get; }

        /// <summary>0 = not checked (compressed formats).</summary>
        public int RequiredBits { get; }

        public List<string> Check(AudioFormatInfo info, long fileBytes)
        {
            var problems = new List<string>();
            if (info.Seconds > MaxSeconds)
            {
                problems.Add(string.Format(CultureInfo.InvariantCulture, "duration {0:0.000} s > {1:0.###} s", info.Seconds, MaxSeconds));
            }

            if (info.Seconds <= 0)
            {
                problems.Add("empty clip");
            }

            if (fileBytes > MaxBytes)
            {
                problems.Add("size " + fileBytes + " B > " + MaxBytes + " B");
            }

            if (info.SampleRate > MaxSampleRate || info.SampleRate < 8000)
            {
                problems.Add("sample rate " + info.SampleRate + " Hz outside 8000.." + MaxSampleRate);
            }

            if (RequireMono && info.Channels != 1)
            {
                problems.Add("channels " + info.Channels + " (SFX must be mono; spatialised by AudioSource)");
            }

            if (info.Channels > 2)
            {
                problems.Add("channels " + info.Channels + " > 2");
            }

            if (RequiredBits > 0 && info.BitsPerSample != RequiredBits)
            {
                problems.Add("bits per sample " + info.BitsPerSample + " expected " + RequiredBits);
            }

            return problems;
        }
    }

    /// <summary>Header information read straight from the file bytes (RIFF/WAVE PCM or Ogg Vorbis).</summary>
    public sealed class AudioFormatInfo
    {
        public string Format { get; private set; }
        public int Channels { get; private set; }
        public int SampleRate { get; private set; }
        public int BitsPerSample { get; private set; }
        public long Frames { get; private set; }
        public string Error { get; private set; }

        public double Seconds
        {
            get { return SampleRate > 0 ? (double)Frames / SampleRate : 0; }
        }

        private static AudioFormatInfo Fail(string format, string error)
        {
            return new AudioFormatInfo { Format = format, Error = error };
        }

        private static uint U32(byte[] d, int o)
        {
            return (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));
        }

        private static int U16(byte[] d, int o)
        {
            return d[o] | (d[o + 1] << 8);
        }

        private static bool Tag(byte[] d, int o, string tag)
        {
            if (o + tag.Length > d.Length)
            {
                return false;
            }

            for (int i = 0; i < tag.Length; i++)
            {
                if (d[o + i] != (byte)tag[i])
                {
                    return false;
                }
            }

            return true;
        }

        public static AudioFormatInfo ParseWav(byte[] d)
        {
            if (d == null || d.Length < 12 || !Tag(d, 0, "RIFF") || !Tag(d, 8, "WAVE"))
            {
                return Fail("wav", "not a RIFF/WAVE file");
            }

            int pos = 12;
            int fmtTag = -1, channels = 0, rate = 0, bits = 0, blockAlign = 0;
            long dataBytes = -1;
            while (pos + 8 <= d.Length)
            {
                uint size = U32(d, pos + 4);
                int body = pos + 8;
                if (Tag(d, pos, "fmt ") && body + 16 <= d.Length)
                {
                    fmtTag = U16(d, body);
                    channels = U16(d, body + 2);
                    rate = (int)U32(d, body + 4);
                    blockAlign = U16(d, body + 12);
                    bits = U16(d, body + 14);
                }
                else if (Tag(d, pos, "data"))
                {
                    dataBytes = Math.Min(size, (long)d.Length - body);
                }

                long next = (long)body + size + (size & 1);
                if (next > int.MaxValue)
                {
                    break;
                }

                pos = (int)next;
            }

            if (fmtTag < 0)
            {
                return Fail("wav", "no fmt chunk");
            }

            if (fmtTag != 1)
            {
                return Fail("wav", "WAVE format tag " + fmtTag + " (only 16-bit integer PCM sources are accepted)");
            }

            if (dataBytes < 0)
            {
                return Fail("wav", "no data chunk");
            }

            if (blockAlign <= 0 || channels <= 0)
            {
                return Fail("wav", "invalid fmt chunk");
            }

            return new AudioFormatInfo { Format = "wav", Channels = channels, SampleRate = rate, BitsPerSample = bits, Frames = dataBytes / blockAlign };
        }

        /// <summary>Reads channels/rate from the Vorbis identification header and the length from the last page's granule position.</summary>
        public static AudioFormatInfo ParseOggVorbis(byte[] d)
        {
            if (d == null || d.Length < 58 || !Tag(d, 0, "OggS"))
            {
                return Fail("ogg", "not an Ogg file");
            }

            int segments = d[26];
            int packet = 27 + segments;
            if (packet + 16 > d.Length || d[packet] != 1 || !Tag(d, packet + 1, "vorbis"))
            {
                return Fail("ogg", "first packet is not a Vorbis identification header");
            }

            int channels = d[packet + 11];
            int rate = (int)U32(d, packet + 12);
            long granule = -1;
            for (int i = d.Length - 27; i >= 0; i--)
            {
                if (Tag(d, i, "OggS") && d[i + 4] == 0)
                {
                    granule = (long)((ulong)U32(d, i + 6) | ((ulong)U32(d, i + 10) << 32));
                    break;
                }
            }

            if (granule <= 0)
            {
                return Fail("ogg", "no final granule position");
            }

            return new AudioFormatInfo { Format = "ogg", Channels = channels, SampleRate = rate, BitsPerSample = 0, Frames = granule };
        }
    }

    public sealed class AudioLicenseEntry
    {
        public string File { get; set; }
        public string Origin { get; set; }
        public string Author { get; set; }
        public string License { get; set; }
        public string Source { get; set; }
        public string Sha256 { get; set; }
        public int Line { get; set; }
    }

    /// <summary>
    /// One row of the "CC-BY attribution" table (required for every CC-BY file). Field names and order follow the
    /// credits spec (docs/design/MODES_DATA_MODEL.md section 5, PR #44) so the credits.json generator of #40 can read
    /// them 1:1: title, author, sourceUrl, license, licenseUrl, attribution, changes.
    /// </summary>
    public sealed class AudioAttributionEntry
    {
        public string File { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string SourceUrl { get; set; }
        public string License { get; set; }
        public string LicenseUrl { get; set; }

        /// <summary>Ready-to-display attribution text (TASL), shown on the credits screen as is.</summary>
        public string Attribution { get; set; }
        public string Changes { get; set; }
        public int Line { get; set; }
    }

    /// <summary>
    /// Parser for Audio/LICENSES.md. Every audio file has one row in the ledger table:
    /// | File | Origin | Author | License | Source | SHA-256 |
    /// Origin: original | cc0 | cc_by. File is relative to Assets/MetinAR/Audio and wrapped in back-ticks.
    /// Every cc_by file additionally has one row in the attribution table:
    /// | File | Title | Author | Source URL | License | License URL | Attribution | Changes |
    /// </summary>
    public sealed class AudioLicenseLedger
    {
        private static readonly string[] Header = { "file", "origin", "author", "license", "source", "sha-256" };
        private static readonly string[] AttributionHeader = { "file", "title", "author", "source url", "license", "license url", "attribution", "changes" };

        /// <summary>Header of the first CC-BY table draft (3f032cb) - rejected with a clear message so old rows are not silently ignored.</summary>
        private static readonly string[] OutdatedAttributionHeader = { "file", "title", "author", "source url", "license", "changes" };
        private static readonly Regex Sha = new Regex("^[0-9a-f]{64}$");
        private static readonly Regex Url = new Regex("https?://\\S+");
        private static readonly Regex AbsoluteUrl = new Regex("^https?://[^\\s/]+\\.[^\\s]+$");

        /// <summary>Plain attribution licence CC BY x.y (no NC / ND / SA elements), e.g. CC-BY-4.0, CC-BY-3.0.</summary>
        private static readonly Regex CcBy = new Regex("^CC-BY-(\\d)\\.(\\d)$");

        /// <summary>Any CC licence carrying NonCommercial, NoDerivatives or ShareAlike (also combinations), in any spelling.</summary>
        private static readonly Regex CcRestricted = new Regex("(?i)^CC[-_ ]?BY.*[-_ ](NC|ND|SA)([-_ .]|$)");

        public List<AudioLicenseEntry> Entries { get; } = new List<AudioLicenseEntry>();
        public List<AudioAttributionEntry> Attributions { get; } = new List<AudioAttributionEntry>();
        public List<string> ParseErrors { get; } = new List<string>();

        public AudioLicenseEntry Find(string ledgerPath)
        {
            foreach (AudioLicenseEntry e in Entries)
            {
                if (e.File == ledgerPath)
                {
                    return e;
                }
            }

            return null;
        }

        public AudioAttributionEntry FindAttribution(string ledgerPath)
        {
            foreach (AudioAttributionEntry a in Attributions)
            {
                if (a.File == ledgerPath)
                {
                    return a;
                }
            }

            return null;
        }

        public static bool IsCcBy(string license)
        {
            return CcBy.IsMatch(license ?? string.Empty);
        }

        /// <summary>Canonical deed URL for CC-BY-x.y (https://creativecommons.org/licenses/by/x.y/), or null.</summary>
        public static string CcByLicenseUrl(string license)
        {
            Match m = CcBy.Match(license ?? string.Empty);
            return m.Success ? "https://creativecommons.org/licenses/by/" + m.Groups[1].Value + "." + m.Groups[2].Value + "/" : null;
        }

        public static bool IsRestrictedCc(string license)
        {
            return CcRestricted.IsMatch((license ?? string.Empty).Trim());
        }

        private static string Cell(string s)
        {
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '`' && s[s.Length - 1] == '`')
            {
                s = s.Substring(1, s.Length - 2).Trim();
            }

            return s;
        }

        private static bool Matches(List<string> cells, string[] header)
        {
            if (cells.Count != header.Length)
            {
                return false;
            }

            for (int k = 0; k < header.Length; k++)
            {
                if (!string.Equals(cells[k], header[k], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        public static AudioLicenseLedger Parse(string text)
        {
            var ledger = new AudioLicenseLedger();
            string[] lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            int table = 0; // 0 = none, 1 = ledger, 2 = attribution
            bool inFence = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    // Fenced blocks hold format examples (LICENSES.md header); they are never ledger data.
                    inFence = !inFence;
                    table = 0;
                    continue;
                }

                if (inFence || !line.StartsWith("|", StringComparison.Ordinal))
                {
                    table = 0;
                    continue;
                }

                string[] raw = line.Trim('|').Split('|');
                var cells = new List<string>();
                foreach (string c in raw)
                {
                    cells.Add(Cell(c));
                }

                if (table == 0)
                {
                    table = Matches(cells, Header) ? 1 : Matches(cells, AttributionHeader) ? 2 : 0;
                    if (Matches(cells, OutdatedAttributionHeader))
                    {
                        ledger.ParseErrors.Add("line " + (i + 1) + ": outdated CC-BY attribution table; use | File | Title | Author | Source URL | License | License URL | Attribution | Changes |");
                    }

                    continue;
                }

                if (cells.TrueForAll(c => c.Replace("-", string.Empty).Replace(":", string.Empty).Length == 0))
                {
                    continue;
                }

                int expected = table == 1 ? Header.Length : AttributionHeader.Length;
                if (cells.Count != expected)
                {
                    ledger.ParseErrors.Add("line " + (i + 1) + ": expected " + expected + " columns, got " + cells.Count);
                    continue;
                }

                if (table == 1)
                {
                    var e = new AudioLicenseEntry
                    {
                        File = cells[0], Origin = cells[1], Author = cells[2], License = cells[3], Source = cells[4], Sha256 = cells[5].ToLowerInvariant(), Line = i + 1,
                    };
                    if (ledger.Find(e.File) != null)
                    {
                        ledger.ParseErrors.Add("line " + (i + 1) + ": duplicate entry for " + e.File);
                        continue;
                    }

                    ledger.Entries.Add(e);
                }
                else
                {
                    var a = new AudioAttributionEntry
                    {
                        File = cells[0], Title = cells[1], Author = cells[2], SourceUrl = cells[3], License = cells[4], LicenseUrl = cells[5],
                        Attribution = cells[6], Changes = cells[7], Line = i + 1,
                    };
                    if (ledger.FindAttribution(a.File) != null)
                    {
                        ledger.ParseErrors.Add("line " + (i + 1) + ": duplicate attribution for " + a.File);
                        continue;
                    }

                    ledger.Attributions.Add(a);
                }
            }

            return ledger;
        }

        private static bool Blank(string s)
        {
            return string.IsNullOrWhiteSpace(s) || s.Trim() == "-" || s.Trim() == "—";
        }

        /// <summary>Checks every entry on its own (provenance completeness, allowed licence). File existence is checked by the caller.</summary>
        public List<AudioIssue> Validate()
        {
            var issues = new List<AudioIssue>();
            foreach (string err in ParseErrors)
            {
                issues.Add(AudioIssue.Error(AudioPipelineRules.LicenseLedgerFileName, err));
            }

            foreach (AudioLicenseEntry e in Entries)
            {
                string where = AudioPipelineRules.LicenseLedgerFileName + ":" + e.Line + " " + e.File;
                if (Blank(e.Author))
                {
                    issues.Add(AudioIssue.Error(where, "author missing"));
                }

                if (Blank(e.Source))
                {
                    issues.Add(AudioIssue.Error(where, "source missing"));
                }

                if (!Sha.IsMatch(e.Sha256 ?? string.Empty))
                {
                    issues.Add(AudioIssue.Error(where, "SHA-256 must be 64 lowercase hex characters"));
                }

                bool restricted = IsRestrictedCc(e.License);
                if (restricted)
                {
                    issues.Add(AudioIssue.Error(where, "licence '" + e.License + "' has NC/ND/SA terms - excluded (owner decision #42)"));
                }
                else if (!AudioPipelineRules.IsAllowedLicense(e.License))
                {
                    issues.Add(AudioIssue.Error(where, "licence '" + e.License + "' not allowed (allowed: " + string.Join(", ", AudioPipelineRules.AllowedLicenses) + ", CC-BY-x.y with attribution; others need an owner decision)"));
                }

                string lowerAll = ((e.Source ?? string.Empty) + " " + (e.Author ?? string.Empty) + " " + (e.File ?? string.Empty)).ToLowerInvariant();
                foreach (string w in AudioPipelineRules.ForbiddenSourceWords)
                {
                    if (lowerAll.Contains(w))
                    {
                        issues.Add(AudioIssue.Error(where, "proprietary source '" + w + "' is forbidden"));
                    }
                }

                switch (e.Origin)
                {
                    case "original":
                        if (e.License != "MetinAR-Owned")
                        {
                            issues.Add(AudioIssue.Error(where, "origin 'original' must use licence MetinAR-Owned"));
                        }

                        break;
                    case "cc0":
                        if (e.License != "CC0-1.0")
                        {
                            issues.Add(AudioIssue.Error(where, "origin 'cc0' must use licence CC0-1.0"));
                        }

                        if (!Url.IsMatch(e.Source ?? string.Empty))
                        {
                            issues.Add(AudioIssue.Error(where, "cc0 source must include the download URL"));
                        }

                        break;
                    case "cc_by":
                        if (!restricted && !IsCcBy(e.License))
                        {
                            issues.Add(AudioIssue.Error(where, "origin 'cc_by' must use licence CC-BY-x.y (e.g. CC-BY-4.0)"));
                        }

                        if (!Url.IsMatch(e.Source ?? string.Empty))
                        {
                            issues.Add(AudioIssue.Error(where, "cc_by source must include the download URL"));
                        }

                        ValidateAttribution(e, FindAttribution(e.File), issues);
                        break;
                    default:
                        issues.Add(AudioIssue.Error(where, "origin '" + e.Origin + "' must be original, cc0 or cc_by"));
                        break;
                }

                if (e.Origin != "cc_by" && IsCcBy(e.License))
                {
                    issues.Add(AudioIssue.Error(where, "CC-BY licence requires origin 'cc_by'"));
                }
            }

            foreach (AudioAttributionEntry a in Attributions)
            {
                AudioLicenseEntry e = Find(a.File);
                if (e == null || e.Origin != "cc_by")
                {
                    issues.Add(AudioIssue.Error(AudioPipelineRules.LicenseLedgerFileName + ":" + a.Line + " " + a.File, "attribution row without a cc_by ledger entry"));
                }
            }

            return issues;
        }

        private static void ValidateAttribution(AudioLicenseEntry e, AudioAttributionEntry a, List<AudioIssue> issues)
        {
            string where = AudioPipelineRules.LicenseLedgerFileName + ":" + e.Line + " " + e.File;
            if (a == null)
            {
                issues.Add(AudioIssue.Error(where, "CC-BY file needs a row in the CC-BY attribution table (title, author, source URL, licence, licence URL, attribution, changes)"));
                return;
            }

            string at = AudioPipelineRules.LicenseLedgerFileName + ":" + a.Line + " " + a.File;
            if (Blank(a.Title))
            {
                issues.Add(AudioIssue.Error(at, "attribution title missing"));
            }

            if (Blank(a.Author))
            {
                issues.Add(AudioIssue.Error(at, "attribution author missing"));
            }
            else if (!Blank(e.Author) && !string.Equals(a.Author.Trim(), e.Author.Trim(), StringComparison.Ordinal))
            {
                issues.Add(AudioIssue.Error(at, "attribution author differs from the ledger author"));
            }

            if (Blank(a.SourceUrl) || !AbsoluteUrl.IsMatch(a.SourceUrl.Trim()))
            {
                issues.Add(AudioIssue.Error(at, "attribution source URL missing or not an http(s) URL"));
            }

            if (Blank(a.License))
            {
                issues.Add(AudioIssue.Error(at, "attribution licence version missing"));
            }
            else if (IsRestrictedCc(a.License))
            {
                issues.Add(AudioIssue.Error(at, "attribution licence '" + a.License + "' has NC/ND/SA terms - excluded"));
            }
            else if (!IsCcBy(a.License))
            {
                issues.Add(AudioIssue.Error(at, "attribution licence version must be CC-BY-x.y"));
            }
            else if (a.License != e.License)
            {
                issues.Add(AudioIssue.Error(at, "attribution licence differs from the ledger licence"));
            }

            string expectedLicenseUrl = CcByLicenseUrl(a.License);
            if (Blank(a.LicenseUrl))
            {
                issues.Add(AudioIssue.Error(at, "attribution licence URL missing"));
            }
            else if (expectedLicenseUrl != null && !string.Equals(a.LicenseUrl.Trim().TrimEnd('/') + "/", expectedLicenseUrl, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(AudioIssue.Error(at, "attribution licence URL must be " + expectedLicenseUrl));
            }

            if (Blank(a.Attribution))
            {
                issues.Add(AudioIssue.Error(at, "attribution text missing (ready-to-display TASL text for the credits screen)"));
            }
            else
            {
                string text = a.Attribution;
                if (!Blank(a.Title) && text.IndexOf(a.Title.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                {
                    issues.Add(AudioIssue.Error(at, "attribution text must name the title"));
                }

                if (!Blank(a.Author) && text.IndexOf(a.Author.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                {
                    issues.Add(AudioIssue.Error(at, "attribution text must name the author"));
                }

                Match v = CcBy.Match(a.License ?? string.Empty);
                if (v.Success && !Regex.IsMatch(text, "(?i)CC[- ]BY[- ]" + v.Groups[1].Value + "\\." + v.Groups[2].Value))
                {
                    issues.Add(AudioIssue.Error(at, "attribution text must name the licence (e.g. CC BY " + v.Groups[1].Value + "." + v.Groups[2].Value + ")"));
                }
            }

            if (Blank(a.Changes))
            {
                issues.Add(AudioIssue.Error(at, "attribution changes missing (describe edits, or write 'unmodified')"));
            }
        }
    }

    public sealed class AudioMetaSampleSettings
    {
        public int LoadType { get; set; } = -1;
        public int CompressionFormat { get; set; } = -1;
        public float Quality { get; set; } = -1f;
        public bool PreloadAudioData { get; set; }
    }

    /// <summary>Minimal line reader for the AudioImporter block of a Unity .meta file (no YAML library needed).</summary>
    public sealed class AudioMetaSettings
    {
        public bool IsAudioImporter { get; private set; }
        public string Guid { get; private set; }
        public bool? ForceToMono { get; private set; }
        public bool? LoadInBackground { get; private set; }

        /// <summary>Unity "Normalize" import flag. Must be 0: peak levels are set at synthesis/mastering time.</summary>
        public bool? Normalize { get; private set; }
        public int SerializedVersion { get; private set; }
        public AudioMetaSampleSettings Default { get; private set; }
        public AudioMetaSampleSettings Android { get; private set; }
        public AudioMetaSampleSettings Ios { get; private set; }

        private static int Indent(string line)
        {
            int n = 0;
            while (n < line.Length && line[n] == ' ')
            {
                n++;
            }

            return n;
        }

        public static AudioMetaSettings Parse(string text)
        {
            var m = new AudioMetaSettings();
            AudioMetaSampleSettings current = null;
            int currentIndent = -1;
            bool inOverrides = false;
            foreach (string rawLine in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                if (rawLine.Trim().Length == 0)
                {
                    continue;
                }

                int indent = Indent(rawLine);
                string line = rawLine.Trim();
                int colon = line.IndexOf(':');
                string key = colon >= 0 ? line.Substring(0, colon).Trim() : line;
                string value = colon >= 0 ? line.Substring(colon + 1).Trim() : string.Empty;

                if (indent == 0)
                {
                    current = null;
                    inOverrides = false;
                    if (key == "AudioImporter")
                    {
                        m.IsAudioImporter = true;
                    }
                    else if (key == "guid")
                    {
                        m.Guid = value;
                    }

                    continue;
                }

                if (!m.IsAudioImporter)
                {
                    continue;
                }

                if (current != null && indent <= currentIndent)
                {
                    current = null;
                }

                if (indent == 2)
                {
                    inOverrides = key == "platformSettingOverrides";
                    if (key == "defaultSettings")
                    {
                        m.Default = current = new AudioMetaSampleSettings();
                        currentIndent = indent;
                    }
                    else if (key == "forceToMono")
                    {
                        m.ForceToMono = value == "1";
                    }
                    else if (key == "loadInBackground")
                    {
                        m.LoadInBackground = value == "1";
                    }
                    else if (key == "normalize")
                    {
                        m.Normalize = value == "1";
                    }
                    else if (key == "serializedVersion")
                    {
                        int sv;
                        m.SerializedVersion = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out sv) ? sv : 0;
                    }

                    continue;
                }

                if (inOverrides && indent == 4 && value.Length == 0)
                {
                    current = new AudioMetaSampleSettings();
                    currentIndent = indent;
                    if (key == AudioPipelineRules.AndroidOverrideKey || key == AudioPipelineRules.LegacyAndroidOverrideKey)
                    {
                        m.Android = current;
                    }
                    else if (key == AudioPipelineRules.IosOverrideKey || key == AudioPipelineRules.LegacyIosOverrideKey)
                    {
                        m.Ios = current;
                    }

                    continue;
                }

                if (current == null)
                {
                    continue;
                }

                int iv;
                bool isInt = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out iv);
                switch (key)
                {
                    case "loadType":
                        current.LoadType = isInt ? iv : -1;
                        break;
                    case "compressionFormat":
                        current.CompressionFormat = isInt ? iv : -1;
                        break;
                    case "quality":
                        float q;
                        current.Quality = float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out q) ? q : -1f;
                        break;
                    case "preloadAudioData":
                        current.PreloadAudioData = value == "1";
                        break;
                }
            }

            return m;
        }
    }

    public sealed class AudioIssue
    {
        private AudioIssue(bool isError, string path, string message)
        {
            IsError = isError;
            Path = path;
            Message = message;
        }

        public bool IsError { get; }
        public string Path { get; }
        public string Message { get; }

        public static AudioIssue Error(string path, string message)
        {
            return new AudioIssue(true, path, message);
        }

        public static AudioIssue Warning(string path, string message)
        {
            return new AudioIssue(false, path, message);
        }

        public override string ToString()
        {
            return (IsError ? "ERROR " : "WARN ") + Path + ": " + Message;
        }
    }
}
