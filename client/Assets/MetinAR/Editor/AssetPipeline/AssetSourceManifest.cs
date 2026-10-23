using System;
using System.Collections.Generic;

namespace MetinAR.Editor.AssetPipeline
{
    /// <summary>
    /// METINAR-009: provenance / licence record for one asset folder (ASSET_SOURCE.txt, "key: value" lines,
    /// '#' comments). Every per-asset folder and every shared set folder must have one.
    /// Pure C#, covered by tests/AssetPipeline.Tests.
    /// </summary>
    public sealed class AssetSourceManifest
    {
        public static readonly string[] RequiredKeys = { "asset_id", "origin", "author", "license", "commercial_use", "created_with", "date_added" };

        /// <summary>Origins: made for MetinAR by the owner/team/commissioned artist, third-party library asset, or AI/procedurally generated.</summary>
        public static readonly string[] Origins = { "original", "commissioned", "third_party", "generated" };

        /// <summary>Licences accepted without further owner approval.</summary>
        public static readonly string[] AllowedThirdPartyLicenses = { "CC0-1.0", "CC-BY-4.0", "MIT", "Apache-2.0", "BSD-3-Clause" };

        /// <summary>Licence value for assets made for and owned by the project (original / commissioned with transfer of rights).</summary>
        public const string ProjectOwnedLicense = "MetinAR-Owned";

        /// <summary>Licence families that are never accepted (non-commercial, no-derivatives, editorial, unknown).</summary>
        public static readonly string[] ForbiddenLicenseTokens = { "NC", "ND", "EDITORIAL", "UNKNOWN", "PERSONAL", "NONCOMMERCIAL" };

        /// <summary>Licences that need an explicit owner decision (owner_approved: yes) before use.</summary>
        public static readonly string[] OwnerDecisionLicenseTokens = { "SA", "GPL", "ASSETSTORE", "MIXAMO", "TOS" };

        /// <summary>Sources that can never be used (proprietary game content).</summary>
        public static readonly string[] ForbiddenSourceTokens = { "metin2", "metin 2", "webzen", "gameforge", "ymir" };

        private readonly Dictionary<string, string> _values;

        private AssetSourceManifest(Dictionary<string, string> values, List<string> parseErrors)
        {
            _values = values;
            ParseErrors = parseErrors;
        }

        public IReadOnlyList<string> ParseErrors { get; }

        public string Get(string key)
        {
            return _values.TryGetValue(key, out string v) ? v : null;
        }

        public bool Has(string key)
        {
            return !string.IsNullOrWhiteSpace(Get(key));
        }

        public bool IsYes(string key)
        {
            return string.Equals(Get(key), "yes", StringComparison.OrdinalIgnoreCase);
        }

        public static AssetSourceManifest Parse(string text)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var errors = new List<string>();
            string[] lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                int colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    errors.Add("Line " + (i + 1) + ": expected 'key: value'.");
                    continue;
                }

                string key = line.Substring(0, colon).Trim().ToLowerInvariant();
                string value = line.Substring(colon + 1).Trim();
                if (values.ContainsKey(key))
                {
                    errors.Add("Line " + (i + 1) + ": duplicate key '" + key + "'.");
                    continue;
                }

                values[key] = value;
            }

            return new AssetSourceManifest(values, errors);
        }

        /// <summary>Validates the record. <paramref name="expectedAssetId"/> is the folder id (null = not checked).</summary>
        public List<PipelineIssue> Validate(string manifestPath, string expectedAssetId)
        {
            var issues = new List<PipelineIssue>();
            foreach (string e in ParseErrors)
            {
                issues.Add(PipelineIssue.Error(manifestPath, e));
            }

            foreach (string key in RequiredKeys)
            {
                if (!Has(key))
                {
                    issues.Add(PipelineIssue.Error(manifestPath, "Missing required key '" + key + "'."));
                }
            }

            if (expectedAssetId != null && Has("asset_id") && Get("asset_id") != expectedAssetId)
            {
                issues.Add(PipelineIssue.Error(manifestPath, "asset_id '" + Get("asset_id") + "' must match folder '" + expectedAssetId + "'."));
            }

            string origin = Get("origin");
            if (origin != null && Array.IndexOf(Origins, origin) < 0)
            {
                issues.Add(PipelineIssue.Error(manifestPath, "origin must be one of: " + string.Join(", ", Origins) + "."));
            }

            if (Has("commercial_use") && !IsYes("commercial_use"))
            {
                issues.Add(PipelineIssue.Error(manifestPath, "commercial_use must be 'yes'; assets that cannot be used commercially are not accepted."));
            }

            foreach (string key in new[] { "source_url", "author", "created_with", "notes", "license" })
            {
                string v = (Get(key) ?? string.Empty).ToLowerInvariant();
                foreach (string bad in ForbiddenSourceTokens)
                {
                    if (v.Contains(bad))
                    {
                        issues.Add(PipelineIssue.Error(manifestPath, "'" + key + "' references '" + bad + "': Metin2/proprietary game content is never allowed (models, textures, animations, music, UI)."));
                    }
                }
            }

            string license = Get("license");
            if (license != null)
            {
                ValidateLicense(manifestPath, origin, license, issues);
            }

            if (origin == "third_party")
            {
                if (!Has("source_url")) issues.Add(PipelineIssue.Error(manifestPath, "third_party assets need 'source_url'."));
                if (!Has("license_file")) issues.Add(PipelineIssue.Error(manifestPath, "third_party assets need 'license_file' (licence text committed next to the asset or under docs/licenses)."));
                if (license == "CC-BY-4.0" && !Has("attribution")) issues.Add(PipelineIssue.Error(manifestPath, "CC-BY-4.0 needs the exact 'attribution' line for the credits screen."));
            }

            if (origin == "generated")
            {
                if (!Has("generator_terms_url")) issues.Add(PipelineIssue.Error(manifestPath, "generated assets need 'generator_terms_url' (the tool's output-ownership terms)."));
                if (!IsYes("owner_approved")) issues.Add(PipelineIssue.Error(manifestPath, "generated (AI/procedural-service) assets need 'owner_approved: yes' — output rights depend on the tool's terms."));
            }

            if (origin == "commissioned" && !Has("rights_transfer"))
            {
                issues.Add(PipelineIssue.Error(manifestPath, "commissioned assets need 'rights_transfer' (contract/invoice reference granting commercial rights)."));
            }

            return issues;
        }

        private void ValidateLicense(string manifestPath, string origin, string license, List<PipelineIssue> issues)
        {
            string upper = license.ToUpperInvariant();
            string[] tokens = upper.Split(new[] { '-', ' ', '_', '/', '.' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string bad in ForbiddenLicenseTokens)
            {
                if (Array.IndexOf(tokens, bad) >= 0)
                {
                    issues.Add(PipelineIssue.Error(manifestPath, "Licence '" + license + "' is not accepted (non-commercial / no-derivatives / editorial / unknown)."));
                    return;
                }
            }

            if (origin == "original" || origin == "commissioned")
            {
                if (license != ProjectOwnedLicense)
                {
                    issues.Add(PipelineIssue.Error(manifestPath, origin + " assets use license '" + ProjectOwnedLicense + "'."));
                }

                return;
            }

            if (Array.IndexOf(AllowedThirdPartyLicenses, license) >= 0)
            {
                return;
            }

            foreach (string decision in OwnerDecisionLicenseTokens)
            {
                if (Array.IndexOf(tokens, decision) >= 0 || upper.Replace(" ", string.Empty).Contains(decision))
                {
                    if (!IsYes("owner_approved"))
                    {
                        issues.Add(PipelineIssue.Error(manifestPath, "Licence '" + license + "' needs an explicit owner decision ('owner_approved: yes')."));
                    }

                    return;
                }
            }

            if (origin == "generated" && IsYes("owner_approved"))
            {
                return;
            }

            issues.Add(PipelineIssue.Error(manifestPath, "Licence '" + license + "' is not on the allowed list (" + string.Join(", ", AllowedThirdPartyLicenses) + ")."));
        }
    }
}
