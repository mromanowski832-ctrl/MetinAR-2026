using MetinAR.Editor.AssetPipeline;
using Xunit;

namespace AssetPipeline.Tests
{
    public class AssetSourceManifestTests
    {
        private const string Original =
            "# provenance\n" +
            "asset_id: VenomousSpider\n" +
            "origin: original\n" +
            "author: Jan Kowalski\n" +
            "license: MetinAR-Owned\n" +
            "commercial_use: yes\n" +
            "created_with: Blender 4.x, Krita\n" +
            "date_added: 2026-10-20\n";

        private static List<PipelineIssue> Validate(string text, string id = "VenomousSpider")
        {
            return AssetSourceManifest.Parse(text).Validate("ASSET_SOURCE.txt", id);
        }

        private static string ThirdParty(string license, string extra = "")
        {
            return "asset_id: VenomousSpider\norigin: third_party\nauthor: Someone\nlicense: " + license +
                   "\ncommercial_use: yes\ncreated_with: Blender\ndate_added: 2026-10-20\nsource_url: https://example.org/a\nlicense_file: LICENSE.txt\n" + extra;
        }

        [Fact]
        public void OriginalAssetIsValid()
        {
            Assert.Empty(Validate(Original));
        }

        [Fact]
        public void ParsesKeysCaseInsensitivelyAndKeepsColonsInValues()
        {
            AssetSourceManifest m = AssetSourceManifest.Parse("Source_URL: https://example.org/x\r\nasset_id: A\r\n");
            Assert.Equal("https://example.org/x", m.Get("source_url"));
            Assert.Empty(m.ParseErrors);
        }

        [Fact]
        public void MalformedAndDuplicateLinesAreErrors()
        {
            AssetSourceManifest m = AssetSourceManifest.Parse("asset_id: A\nasset_id: B\nnot a pair\n");
            Assert.Equal(2, m.ParseErrors.Count);
        }

        [Fact]
        public void MissingKeysAreReported()
        {
            List<PipelineIssue> issues = Validate("asset_id: VenomousSpider\n");
            foreach (string key in new[] { "origin", "author", "license", "commercial_use", "created_with", "date_added" })
            {
                Assert.Contains(issues, i => i.Message.Contains("'" + key + "'"));
            }
        }

        [Fact]
        public void AssetIdMustMatchFolder()
        {
            Assert.Contains(Validate(Original, "Blacksmith"), i => i.Message.Contains("must match folder"));
        }

        [Fact]
        public void UnknownOriginRejected()
        {
            Assert.Contains(Validate(Original.Replace("origin: original", "origin: downloaded")), i => i.Message.StartsWith("origin must be"));
        }

        [Fact]
        public void CommercialUseMustBeYes()
        {
            Assert.Contains(Validate(Original.Replace("commercial_use: yes", "commercial_use: no")), i => i.Message.StartsWith("commercial_use"));
        }

        [Fact]
        public void OriginalMustUseProjectLicense()
        {
            Assert.Contains(Validate(Original.Replace("MetinAR-Owned", "CC0-1.0")), i => i.Message.Contains("MetinAR-Owned"));
        }

        [Theory]
        [InlineData("CC0-1.0")]
        [InlineData("MIT")]
        [InlineData("Apache-2.0")]
        public void PermissiveThirdPartyLicensesAccepted(string license)
        {
            Assert.Empty(Validate(ThirdParty(license)));
        }

        [Fact]
        public void CcByNeedsAttribution()
        {
            Assert.Contains(Validate(ThirdParty("CC-BY-4.0")), i => i.Message.Contains("attribution"));
            Assert.Empty(Validate(ThirdParty("CC-BY-4.0", "attribution: \"Spider\" by Someone, CC BY 4.0\n")));
        }

        [Theory]
        [InlineData("CC-BY-NC-4.0")]
        [InlineData("CC-BY-ND-4.0")]
        [InlineData("CC-BY-NC-SA-4.0")]
        [InlineData("Editorial use only")]
        [InlineData("Unknown")]
        [InlineData("Personal use")]
        public void NonCommercialOrUnknownLicensesRejected(string license)
        {
            Assert.Contains(Validate(ThirdParty(license, "owner_approved: yes\n")), i => i.Message.Contains("not accepted"));
        }

        [Theory]
        [InlineData("CC-BY-SA-4.0")]
        [InlineData("GPL-3.0")]
        [InlineData("Unity Asset Store EULA")]
        [InlineData("Mixamo ToS")]
        public void ShareAlikeStoreAndToSLicensesNeedOwnerDecision(string license)
        {
            Assert.Contains(Validate(ThirdParty(license)), i => i.Message.Contains("owner decision"));
            Assert.Empty(Validate(ThirdParty(license, "owner_approved: yes\n")));
        }

        [Fact]
        public void UnlistedLicenseRejected()
        {
            Assert.Contains(Validate(ThirdParty("Some-Custom-License")), i => i.Message.Contains("not on the allowed list"));
        }

        [Fact]
        public void ThirdPartyNeedsSourceAndLicenseFile()
        {
            string text = "asset_id: VenomousSpider\norigin: third_party\nauthor: X\nlicense: CC0-1.0\ncommercial_use: yes\ncreated_with: Blender\ndate_added: 2026-10-20\n";
            List<PipelineIssue> issues = Validate(text);
            Assert.Contains(issues, i => i.Message.Contains("source_url"));
            Assert.Contains(issues, i => i.Message.Contains("license_file"));
        }

        [Theory]
        [InlineData("source_url: https://metin2.example/ripped-spider\n")]
        [InlineData("notes: traced from a Metin2 screenshot\n")]
        [InlineData("notes: Gameforge model reference\n")]
        public void Metin2SourcesAreAlwaysRejected(string extra)
        {
            string text = ThirdParty("CC0-1.0").Replace("source_url: https://example.org/a\n", string.Empty) + extra;
            if (!extra.StartsWith("source_url")) text += "source_url: https://example.org/a\n";
            Assert.Contains(Validate(text), i => i.Message.Contains("Metin2"));
        }

        [Fact]
        public void GeneratedAssetsNeedTermsAndOwnerApproval()
        {
            string text = "asset_id: VenomousSpider\norigin: generated\nauthor: Owner via tool\nlicense: Tool-ToS\ncommercial_use: yes\ncreated_with: some generator\ndate_added: 2026-10-20\n";
            List<PipelineIssue> issues = Validate(text);
            Assert.Contains(issues, i => i.Message.Contains("generator_terms_url"));
            Assert.Contains(issues, i => i.Message.Contains("owner_approved"));
            Assert.Empty(Validate(text + "generator_terms_url: https://example.org/terms\nowner_approved: yes\n"));
        }

        [Fact]
        public void CommissionedNeedsRightsTransfer()
        {
            string text = Original.Replace("origin: original", "origin: commissioned");
            Assert.Contains(Validate(text), i => i.Message.Contains("rights_transfer"));
            Assert.Empty(Validate(text + "rights_transfer: contract 2026-11 (owner archive)\n"));
        }
    }
}
