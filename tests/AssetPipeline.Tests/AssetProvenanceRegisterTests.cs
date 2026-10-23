using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MetinAR.Editor.AssetPipeline;
using Xunit;
using Xunit.Abstractions;

namespace AssetPipeline.Tests
{
    /// <summary>METINAR-041 E1 (#86): unit tests of the per-asset licence and provenance register rules.</summary>
    public class AssetProvenanceRegisterTests
    {
        private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private const string ValidManifest =
            "asset_id: VenomousSpider\norigin: original\nauthor: MetinAR project\nlicense: MetinAR-Owned\ncommercial_use: yes\n" +
            "created_with: Blender 4.2 project script\ndate_added: 2026-10-23\n";

        private static string Header => string.Join(",", AssetProvenanceRegister.Header);

        private static string Row(string path, string sha = Sha, string origin = "original", string author = "MetinAR project",
            string license = "MetinAR-Owned", string createdWith = "\"Unity editor script, procedural\"", string sourceUrl = "", string notes = "")
        {
            return path + "," + sha + ",Proto," + origin + "," + author + "," + license + ",yes," + createdWith + ",2026-10-23," + sourceUrl + "," + notes;
        }

        private static AssetProvenanceInput Input(string[] content, string fragment = null, Dictionary<string, string> manifests = null,
            Dictionary<string, string> audio = null, string[] extraFiles = null, List<PipelineIssue> parseIssues = null)
        {
            var input = new AssetProvenanceInput();
            input.ContentFiles.AddRange(content);
            input.AllFiles.AddRange(content);
            if (extraFiles != null) input.AllFiles.AddRange(extraFiles);
            if (manifests != null) foreach (var m in manifests) input.Manifests[m.Key] = m.Value;
            if (audio != null) foreach (var a in audio) input.AudioLedgerSources[a.Key] = a.Value;
            if (fragment != null) input.Rows.AddRange(AssetProvenanceRegister.ParseFragment("docs/assets/provenance/t.csv", fragment, parseIssues ?? new List<PipelineIssue>()));
            input.Sha256 = _ => Sha;
            return input;
        }

        private static List<string> Errors(AssetProvenanceInput input)
        {
            return AssetProvenanceRegister.Check(input).Where(i => i.Severity == IssueSeverity.Error).Select(i => i.ToString()).ToList();
        }

        [Theory]
        [InlineData("Characters/Mobs/X/Models/SK_Mob_X.fbx", true)]
        [InlineData("Textures/ItemIcons/T_ItemIcons_A_BC.png", true)]
        [InlineData("Audio/SFX/UI/SFX_UI_Click_01.wav", true)]
        [InlineData("Prefabs/A.prefab", true)]
        [InlineData("Scenes/A.unity", true)]
        [InlineData("Scripts/A.cs", false)]
        [InlineData("Textures/README.md", false)]
        [InlineData("Textures/ItemIcons/ASSET_SOURCE.txt", false)]
        [InlineData("Textures/A.png.meta", false)]
        public void ContentScope(string path, bool expected)
        {
            Assert.Equal(expected, AssetProvenanceRegister.IsContentFile(path));
        }

        [Fact]
        public void RegisteredRowPasses()
        {
            Assert.Empty(Errors(Input(new[] { "Art/A.png" }, Header + "\n" + Row("Art/A.png") + "\n")));
        }

        [Fact]
        public void CsvQuotedCommasAndCommentsAreParsed()
        {
            var issues = new List<PipelineIssue>();
            var rows = AssetProvenanceRegister.ParseFragment("f.csv", "# comment\n\n" + Header + "\r\n" + Row("Art/A.png", notes: "\"a, \"\"b\"\"\"") + "\r\n", issues);
            Assert.Empty(issues);
            Assert.Single(rows);
            Assert.Equal("a, \"b\"", rows[0].Get("notes"));
            Assert.Equal("Unity editor script, procedural", rows[0].Get("created_with"));
        }

        [Fact]
        public void WrongHeaderIsAnError()
        {
            var issues = new List<PipelineIssue>();
            AssetProvenanceRegister.ParseFragment("f.csv", "path,sha\nA.png,x\n", issues);
            Assert.Contains(issues, i => i.Message.Contains("header must be"));
        }

        [Fact]
        public void UnregisteredContentFileFails()
        {
            List<string> e = Errors(Input(new[] { "Art/A.png", "Art/B.png" }, Header + "\n" + Row("Art/A.png") + "\n"));
            Assert.Single(e);
            Assert.Contains("Art/B.png", e[0]);
            Assert.Contains("no provenance", e[0]);
        }

        [Fact]
        public void NearestAssetSourceManifestCoversSubfolders()
        {
            var manifests = new Dictionary<string, string> { ["Characters/Mobs/VenomousSpider"] = ValidManifest };
            Assert.Empty(Errors(Input(new[] { "Characters/Mobs/VenomousSpider/Models/SK_Mob_VenomousSpider.fbx", "Characters/Mobs/VenomousSpider/Textures/T_A_BC.png" }, manifests: manifests)));
            Assert.Equal("Characters/Mobs/VenomousSpider", AssetProvenanceRegister.NearestManifestFolder("Characters/Mobs/VenomousSpider/Models/a.fbx", manifests));
            Assert.Null(AssetProvenanceRegister.NearestManifestFolder("Characters/Mobs/Other/a.fbx", manifests));
        }

        [Fact]
        public void AudioLedgerCoversAudioFiles()
        {
            var audio = new Dictionary<string, string> { ["SFX/UI/SFX_UI_Click_01.wav"] = "MetinAR project | MetinAR-Owned | procedural" };
            Assert.Empty(Errors(Input(new[] { "Audio/SFX/UI/SFX_UI_Click_01.wav" }, audio: audio)));
        }

        [Fact]
        public void DoubleRegistrationFails()
        {
            var manifests = new Dictionary<string, string> { ["Art"] = ValidManifest };
            List<string> e = Errors(Input(new[] { "Art/A.png" }, Header + "\n" + Row("Art/A.png") + "\n", manifests));
            Assert.Contains(e, x => x.Contains("more than once"));
        }

        [Fact]
        public void DuplicateRowFails()
        {
            List<string> e = Errors(Input(new[] { "Art/A.png" }, Header + "\n" + Row("Art/A.png") + "\n" + Row("Art/A.png") + "\n"));
            Assert.Contains(e, x => x.Contains("already registered"));
        }

        [Fact]
        public void StaleRowFails()
        {
            List<string> e = Errors(Input(new string[0], Header + "\n" + Row("Art/Gone.png") + "\n"));
            Assert.Contains(e, x => x.Contains("stale row"));
        }

        [Fact]
        public void ShaMismatchFails()
        {
            List<string> e = Errors(Input(new[] { "Art/A.png" }, Header + "\n" + Row("Art/A.png", sha: new string('f', 64)) + "\n"));
            Assert.Contains(e, x => x.Contains("sha256 does not match"));
        }

        [Fact]
        public void EmptyShaOnlyForUnitySerializedAssets()
        {
            Assert.Empty(Errors(Input(new[] { "Prefabs/A.prefab" }, Header + "\n" + Row("Prefabs/A.prefab", sha: "") + "\n")));
            List<string> e = Errors(Input(new[] { "Art/A.png" }, Header + "\n" + Row("Art/A.png", sha: "") + "\n"));
            Assert.Contains(e, x => x.Contains("needs 'sha256'"));
        }

        [Theory]
        [InlineData("author", "Webzen")]
        [InlineData("author", "Gameforge 4D GmbH")]
        [InlineData("author", "YMIR Entertainment")]
        [InlineData("notes", "ripped from the Metin2 client")]
        [InlineData("source_url", "https://example.org/metin2-pack")]
        public void ForbiddenSourceInRowFails(string column, string value)
        {
            string row = column == "author" ? Row("Art/A.png", author: value)
                : column == "notes" ? Row("Art/A.png", notes: "\"" + value + "\"")
                : Row("Art/A.png", origin: "third_party", license: "CC0-1.0", sourceUrl: value, notes: "license_file");
            List<string> e = Errors(Input(new[] { "Art/A.png" }, Header + "\n" + row + "\n"));
            Assert.Contains(e, x => x.Contains("Metin2/proprietary") || x.Contains("references"));
        }

        [Theory]
        [InlineData("Characters/Mobs/Metin2Spider/Models/SK_Mob_Metin2Spider.fbx")]
        [InlineData("Textures/metin_2_icons/T_A.png")]
        [InlineData("Art/webzen_logo.png")]
        public void ForbiddenNameInPathFails(string path)
        {
            List<string> e = Errors(Input(new[] { path }, Header + "\n" + Row(path) + "\n"));
            Assert.Contains(e, x => x.Contains("path references"));
        }

        [Fact]
        public void ForbiddenSourceInManifestFails()
        {
            var manifests = new Dictionary<string, string> { ["Art"] = ValidManifest.Replace("author: MetinAR project", "author: Webzen") };
            List<string> e = Errors(Input(new[] { "Art/A.png" }, manifests: manifests));
            Assert.Contains(e, x => x.Contains("webzen"));
        }

        [Fact]
        public void ForbiddenSourceInAudioLedgerFails()
        {
            var audio = new Dictionary<string, string> { ["SFX/A.wav"] = "x | MetinAR-Owned | Gameforge client rip" };
            List<string> e = Errors(Input(new[] { "Audio/SFX/A.wav" }, audio: audio));
            Assert.Contains(e, x => x.Contains("gameforge"));
        }

        [Fact]
        public void RowFollowsAssetSourceLicenceRules()
        {
            List<string> e = Errors(Input(new[] { "Art/A.png" }, Header + "\n" + Row("Art/A.png", origin: "third_party", license: "CC-BY-NC-4.0") + "\n"));
            Assert.Contains(e, x => x.Contains("not accepted"));
        }

        [Fact]
        public void AudioLedgerReaderSkipsFencedExamples()
        {
            string md = "```text\n| `SFX/X.wav` | cc_by | Jane | CC-BY-4.0 | src | sha |\n```\n| File | Origin | Author | License | Source | SHA-256 |\n|---|---|---|---|---|---|\n| `SFX/UI/A.wav` | original | MetinAR project | MetinAR-Owned | procedural | `" + Sha + "` |\n";
            Dictionary<string, string> r = AssetProvenanceRegister.ReadAudioLedgerSources(md);
            Assert.Single(r);
            Assert.True(r.ContainsKey("SFX/UI/A.wav"));
        }
        private const string LedgerHeader = "| File | Origin | Author | License | Source | SHA-256 |\n|---|---|---|---|---|---|\n";
        private const string CcByHeader = "| File | Title | Author | Source URL | License | License URL | Attribution | Changes |\n|---|---|---|---|---|---|---|---|\n";

        [Fact]
        public void AudioLedgerUsesAudioParserAndScansCcByTable()
        {
            string md = LedgerHeader +
                        "| `SFX/Combat/A.wav` | cc_by | Jane Doe | CC-BY-4.0 | Jane Doe, https://example.org/1 | `" + Sha + "` |\n\n" + CcByHeader +
                        "| `SFX/Combat/A.wav` | Hit | Jane Doe | https://example.org/1 | CC-BY-4.0 | https://creativecommons.org/licenses/by/4.0/ | \"Hit\" by Jane Doe, CC BY 4.0 | resampled from a Gameforge client |\n";
            var issues = new List<PipelineIssue>();
            Dictionary<string, string> r = AssetProvenanceRegister.ReadAudioLedgerSources(md, issues);
            Assert.Empty(issues);
            List<string> e = Errors(Input(new[] { "Audio/SFX/Combat/A.wav" }, audio: r));
            Assert.Contains(e, x => x.Contains("gameforge"));
        }

        [Fact]
        public void OrphanCcByRowIsScannedToo()
        {
            string md = CcByHeader + "| `SFX/B.wav` | Webzen theme | X | https://example.org/2 | CC-BY-4.0 | https://creativecommons.org/licenses/by/4.0/ | x | unmodified |\n";
            var issues = new List<PipelineIssue>();
            Dictionary<string, string> r = AssetProvenanceRegister.ReadAudioLedgerSources(md, issues);
            Assert.Empty(r);
            Assert.Contains(issues, i => i.Message.Contains("webzen"));
        }

        [Fact]
        public void SixCellRowsOutsideTheLedgerTableAreNotCoverage()
        {
            // outdated 6-column CC-BY header: must not be read as ledger rows (the old 6-cell heuristic would have)
            string md = "| File | Title | Author | Source URL | License | Changes |\n|---|---|---|---|---|---|\n| `SFX/C.wav` | T | A | https://example.org | CC-BY-4.0 | none |\n";
            var issues = new List<PipelineIssue>();
            Dictionary<string, string> r = AssetProvenanceRegister.ReadAudioLedgerSources(md, issues);
            Assert.Empty(r);
            Assert.Contains(issues, i => i.Message.Contains("outdated CC-BY"));
        }

        [Fact]
        public void AudioLedgerFormatErrorsAreReported()
        {
            string md = LedgerHeader + "| `SFX/D.wav` | original | MetinAR project | MetinAR-Owned | procedural |\n";
            var issues = new List<PipelineIssue>();
            AssetProvenanceRegister.ReadAudioLedgerSources(md, issues);
            Assert.Contains(issues, i => i.Message.Contains("expected 6 columns"));
        }

        [Theory]
        [InlineData("CC-BY-SA-4.0")]
        [InlineData("CC-BY-NC-4.0")]
        [InlineData("CC-BY-3.0")]
        public void CsvRowsAcceptOnlyDocumentedThirdPartyLicences(string license)
        {
            // METINAR-009 §11 / MODES_DATA_MODEL §5: graphics accept CC-BY-4.0; SA needs owner_approved (not in the CSV).
            List<string> e = Errors(Input(new[] { "Art/A.png" }, Header + "\n" + Row("Art/A.png", origin: "third_party", license: license, sourceUrl: "https://example.org/a") + "\n"));
            Assert.Contains(e, x => x.Contains("Licence '" + license + "'"));
        }

        [Fact]
        public void CsvRowAcceptsCcBy40Licence()
        {
            List<string> e = Errors(Input(new[] { "Art/A.png" }, Header + "\n" + Row("Art/A.png", origin: "third_party", license: "CC-BY-4.0", sourceUrl: "https://example.org/a") + "\n"));
            Assert.DoesNotContain(e, x => x.Contains("Licence '"));
        }
    }

    /// <summary>
    /// METINAR-041 E1 (#86): the real repository. Every content file under client/Assets/MetinAR has exactly one
    /// provenance source (ASSET_SOURCE.txt / Audio/LICENSES.md / docs/assets/provenance/*.csv) and nothing names Metin2,
    /// Webzen, Gameforge or YMIR.
    /// </summary>
    public class AssetProvenanceRepositoryTests
    {
        private readonly ITestOutputHelper _output;

        public AssetProvenanceRepositoryTests(ITestOutputHelper output)
        {
            _output = output;
        }

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

        [Fact]
        public void EveryAssetIsRegisteredAndClean()
        {
            var issues = new List<PipelineIssue>();
            AssetProvenanceInput input = AssetProvenanceRegister.LoadFromRepository(RepoRoot(), issues);
            issues.AddRange(AssetProvenanceRegister.Check(input));
            _output.WriteLine("[METINAR-041 E1] content files " + input.ContentFiles.Count + ", ASSET_SOURCE.txt " + input.Manifests.Count +
                              ", audio ledger rows " + input.AudioLedgerSources.Count + ", register rows " + input.Rows.Count + ", issues " + issues.Count);
            foreach (PipelineIssue i in issues) _output.WriteLine(i.ToString());
            Assert.True(input.ContentFiles.Count > 0, "scope is empty: wrong repository root?");
            Assert.True(issues.Count == 0, string.Join("\n", issues.Select(i => i.ToString())));
        }
    }
}
