using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MetinAR.Editor.AssetPipeline
{
    /// <summary>
    /// METINAR-041 E1 (#86): per-asset licence and provenance register. Every content file under
    /// <c>client/Assets/MetinAR</c> (models, textures, materials, animations, prefabs, scenes, audio, fonts, shaders,
    /// video) must be covered by <b>exactly one</b> provenance source:
    /// <list type="number">
    /// <item>the nearest <c>ASSET_SOURCE.txt</c> in the file's folder or a parent folder (METINAR-009 section 11:
    /// per-asset and shared set folders, e.g. <c>Characters/Mobs/VenomousSpider/</c>, <c>Textures/ItemIcons/</c>);</item>
    /// <item>for audio under <c>Audio/</c>: a row in <c>Audio/LICENSES.md</c> (METINAR-018; SHA-256 checked there);</item>
    /// <item>otherwise a row (with SHA-256) in a register fragment <c>docs/assets/provenance/*.csv</c>.</item>
    /// </list>
    /// Any source that names Metin2 / Webzen / Gameforge / YMIR, and any asset path containing those names, fails.
    /// Fragments are split per PR/asset set so open PRs can add their own rows without merge conflicts.
    /// Pure C# (System.IO only), covered by tests/AssetPipeline.Tests.
    /// </summary>
    public static class AssetProvenanceRegister
    {
        /// <summary>Folder of the register fragments, relative to the repository root.</summary>
        public const string RegisterFolder = "docs/assets/provenance";

        /// <summary>Scope root, relative to the repository root.</summary>
        public const string ScopeRoot = "client/Assets/MetinAR";

        public const string AudioFolder = "Audio";

        /// <summary>CSV header of a register fragment (order fixed).</summary>
        public static readonly string[] Header =
        {
            "path", "sha256", "asset_id", "origin", "author", "license", "commercial_use", "created_with", "date_added", "source_url", "notes"
        };

        /// <summary>Content (art/audio/scene) file types that need provenance. Code, docs, data tables and .meta are out of scope.</summary>
        public static readonly string[] ContentExtensions =
        {
            // models
            ".fbx", ".obj", ".blend", ".gltf", ".glb", ".dae", ".3ds", ".max", ".ma", ".mb",
            // textures / images
            ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff", ".exr", ".hdr", ".dds", ".bmp", ".gif", ".svg",
            // Unity art assets
            ".mat", ".anim", ".controller", ".overridecontroller", ".mask", ".prefab", ".unity", ".mixer", ".physicmaterial",
            ".rendertexture", ".cubemap", ".spriteatlas", ".spriteatlasv2", ".fontsettings", ".shader", ".shadergraph", ".shadersubgraph", ".vfx",
            // audio
            ".wav", ".ogg", ".mp3", ".aif", ".aiff", ".flac", ".m4a", ".aac",
            // fonts / video
            ".ttf", ".otf", ".mp4", ".webm", ".mov",
        };

        /// <summary>
        /// Unity-serialized (YAML) assets that editor scripts and normal scene/prefab work re-save. For these the register
        /// row may leave <c>sha256</c> empty (a given value is still checked); every other content type needs a matching SHA-256.
        /// </summary>
        public static readonly string[] UnitySerializedExtensions =
        {
            ".mat", ".anim", ".controller", ".overridecontroller", ".mask", ".prefab", ".unity", ".mixer", ".physicmaterial",
            ".rendertexture", ".cubemap", ".spriteatlas", ".spriteatlasv2", ".fontsettings", ".shadergraph", ".shadersubgraph", ".vfx",
        };

        public static bool IsUnitySerialized(string path)
        {
            string ext = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
            return Array.IndexOf(UnitySerializedExtensions, ext) >= 0;
        }

        /// <summary>Names of proprietary game content / rights holders that may never appear as a source or in an asset path.</summary>
        public static string[] ForbiddenTokens => AssetSourceManifest.ForbiddenSourceTokens;

        public static bool IsContentFile(string path)
        {
            string ext = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
            return Array.IndexOf(ContentExtensions, ext) >= 0;
        }

        public static string Normalize(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').Trim().TrimStart('/');
        }

        public static string FindForbiddenToken(string text)
        {
            string v = (text ?? string.Empty).ToLowerInvariant();
            foreach (string bad in ForbiddenTokens)
            {
                if (v.Contains(bad))
                {
                    return bad;
                }
            }

            // also catch "metin_2", "metin-2", "Metin2" split by separators in file names
            string squashed = v.Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty).Replace(".", string.Empty);
            foreach (string bad in ForbiddenTokens)
            {
                string b = bad.Replace(" ", string.Empty);
                if (squashed.Contains(b))
                {
                    return bad;
                }
            }

            return null;
        }

        /// <summary>Minimal RFC 4180 CSV reader (quoted fields, doubled quotes, commas/newlines inside quotes).</summary>
        public static List<List<string>> ReadCsv(string text, out string error)
        {
            error = null;
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            bool fieldStarted = false;
            string s = (text ?? string.Empty).Replace("\r\n", "\n");
            if (s.Length > 0 && s[0] == '\uFEFF') s = s.Substring(1);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < s.Length && s[i + 1] == '"') { field.Append('"'); i++; }
                        else quoted = false;
                    }
                    else field.Append(c);
                    continue;
                }

                if (c == '"' && field.Length == 0) { quoted = true; fieldStarted = true; }
                else if (c == ',') { row.Add(field.ToString()); field.Clear(); fieldStarted = true; }
                else if (c == '\n')
                {
                    row.Add(field.ToString()); field.Clear();
                    rows.Add(row); row = new List<string>(); fieldStarted = false;
                }
                else { field.Append(c); fieldStarted = true; }
            }

            if (quoted) error = "unterminated quoted field";
            if (fieldStarted || field.Length > 0) { row.Add(field.ToString()); rows.Add(row); }
            return rows;
        }

        /// <summary>Parses one register fragment. Blank lines and lines starting with '#' are ignored.</summary>
        public static List<AssetProvenanceRow> ParseFragment(string fragmentName, string text, List<PipelineIssue> issues)
        {
            var result = new List<AssetProvenanceRow>();
            List<List<string>> rows = ReadCsv(text, out string error);
            if (error != null) issues.Add(PipelineIssue.Error(fragmentName, error));
            bool headerSeen = false;
            int line = 0;
            foreach (List<string> r in rows)
            {
                line++;
                if (r.Count == 0 || (r.Count == 1 && r[0].Trim().Length == 0) || r[0].TrimStart().StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!headerSeen)
                {
                    headerSeen = true;
                    bool ok = r.Count == Header.Length;
                    for (int i = 0; ok && i < Header.Length; i++) ok = string.Equals(r[i].Trim(), Header[i], StringComparison.OrdinalIgnoreCase);
                    if (!ok) issues.Add(PipelineIssue.Error(fragmentName, "header must be: " + string.Join(",", Header)));
                    continue;
                }

                if (r.Count != Header.Length)
                {
                    issues.Add(PipelineIssue.Error(fragmentName, "row " + line + ": expected " + Header.Length + " columns, got " + r.Count + "."));
                    continue;
                }

                var row = new AssetProvenanceRow { Fragment = fragmentName, Row = line };
                for (int i = 0; i < Header.Length; i++) row.Values[Header[i]] = r[i].Trim();
                result.Add(row);
            }

            if (!headerSeen) issues.Add(PipelineIssue.Error(fragmentName, "empty fragment (header missing)."));
            return result;
        }

        /// <summary>Checks the whole scope. All paths are relative to <see cref="ScopeRoot"/> with '/' separators.</summary>
        public static List<PipelineIssue> Check(AssetProvenanceInput input)
        {
            var issues = new List<PipelineIssue>();

            // 1) every ASSET_SOURCE.txt in scope must be a valid record
            foreach (KeyValuePair<string, string> m in input.Manifests)
            {
                string manifestPath = ScopeRoot + "/" + (m.Key.Length == 0 ? string.Empty : m.Key + "/") + AssetPipelineRules.SourceManifestFileName;
                issues.AddRange(AssetSourceManifest.Parse(m.Value).Validate(manifestPath, null));
                string bad = FindForbiddenToken(AssetSourceManifest.Parse(m.Value).Get("asset_id"));
                if (bad != null) issues.Add(PipelineIssue.Error(manifestPath, "asset_id references '" + bad + "'."));
            }

            // 2) register rows: valid record, file exists, SHA matches, no duplicates
            var rowsByPath = new Dictionary<string, AssetProvenanceRow>(StringComparer.Ordinal);
            var contentSet = new HashSet<string>(input.ContentFiles, StringComparer.Ordinal);
            foreach (AssetProvenanceRow row in input.Rows)
            {
                string where = row.Fragment + " row " + row.Row;
                string path = Normalize(row.Get("path"));
                if (path.Length == 0)
                {
                    issues.Add(PipelineIssue.Error(where, "empty 'path'."));
                    continue;
                }

                if (rowsByPath.TryGetValue(path, out AssetProvenanceRow first))
                {
                    issues.Add(PipelineIssue.Error(where, "'" + path + "' is already registered in " + first.Fragment + " row " + first.Row + "."));
                    continue;
                }

                rowsByPath[path] = row;
                if (!contentSet.Contains(path))
                {
                    issues.Add(PipelineIssue.Error(where, "'" + path + "' does not exist under " + ScopeRoot + " or is not a content file (stale row?)."));
                }
                else
                {
                    string actual = input.Sha256 != null ? input.Sha256(path) : null;
                    string expected = (row.Get("sha256") ?? string.Empty).ToLowerInvariant();
                    if (expected.Length == 0 && !IsUnitySerialized(path))
                    {
                        issues.Add(PipelineIssue.Error(where, "'" + path + "' needs 'sha256' (only Unity-serialized assets may leave it empty)."));
                    }
                    else if (expected.Length > 0 && actual != null && expected != actual)
                    {
                        issues.Add(PipelineIssue.Error(where, "sha256 does not match the bytes of '" + path + "' (re-check provenance, then update the row)."));
                    }
                }

                foreach (PipelineIssue i in AssetSourceManifest.Parse(row.ToManifestText()).Validate(where, null))
                {
                    issues.Add(i);
                }

                string bad = FindForbiddenToken(row.Get("asset_id"));
                if (bad != null) issues.Add(PipelineIssue.Error(where, "asset_id references '" + bad + "'."));
            }

            // 3) audio ledger rows must not name forbidden sources either
            foreach (KeyValuePair<string, string> a in input.AudioLedgerSources)
            {
                string bad = FindForbiddenToken(a.Value);
                if (bad != null) issues.Add(PipelineIssue.Error(ScopeRoot + "/" + AudioFolder + "/" + a.Key, "Audio/LICENSES.md row references '" + bad + "'."));
            }

            // 4) every file in scope: no forbidden names; every content file: exactly one provenance source
            foreach (string file in input.AllFiles)
            {
                string bad = FindForbiddenToken(file);
                if (bad != null) issues.Add(PipelineIssue.Error(ScopeRoot + "/" + file, "path references '" + bad + "': Metin2/proprietary game content is never allowed."));
            }

            foreach (string file in input.ContentFiles)
            {
                var sources = new List<string>();
                string manifestFolder = NearestManifestFolder(file, input.Manifests);
                if (manifestFolder != null) sources.Add((manifestFolder.Length == 0 ? string.Empty : manifestFolder + "/") + AssetPipelineRules.SourceManifestFileName);
                if (file.StartsWith(AudioFolder + "/", StringComparison.Ordinal) && input.AudioLedgerSources.ContainsKey(file.Substring(AudioFolder.Length + 1)))
                {
                    sources.Add(AudioFolder + "/LICENSES.md");
                }

                if (rowsByPath.TryGetValue(file, out AssetProvenanceRow row)) sources.Add(row.Fragment);

                if (sources.Count == 0)
                {
                    issues.Add(PipelineIssue.Error(ScopeRoot + "/" + file,
                        "no provenance: add ASSET_SOURCE.txt to its asset folder, a row in Audio/LICENSES.md (audio) or a row in " + RegisterFolder + "/<fragment>.csv."));
                }
                else if (sources.Count > 1)
                {
                    issues.Add(PipelineIssue.Error(ScopeRoot + "/" + file, "registered more than once (" + string.Join(", ", sources) + "); keep exactly one source."));
                }
            }

            return issues;
        }

        /// <summary>Nearest folder (the file's own folder or a parent, "" = scope root) that has an ASSET_SOURCE.txt.</summary>
        public static string NearestManifestFolder(string file, IDictionary<string, string> manifests)
        {
            string dir = file;
            while (true)
            {
                int slash = dir.LastIndexOf('/');
                dir = slash < 0 ? string.Empty : dir.Substring(0, slash);
                if (manifests.ContainsKey(dir)) return dir;
                if (dir.Length == 0) return null;
            }
        }

        /// <summary>Builds the input from a repository checkout (box test, CI, or the Unity editor).</summary>
        public static AssetProvenanceInput LoadFromRepository(string repoRoot, List<PipelineIssue> issues)
        {
            string scope = Path.Combine(repoRoot, ScopeRoot.Replace('/', Path.DirectorySeparatorChar));
            var input = new AssetProvenanceInput();
            foreach (string full in Directory.GetFiles(scope, "*", SearchOption.AllDirectories))
            {
                string rel = Normalize(full.Substring(scope.Length));
                if (rel.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                input.AllFiles.Add(rel);
                if (Path.GetFileName(rel) == AssetPipelineRules.SourceManifestFileName)
                {
                    int slash = rel.LastIndexOf('/');
                    input.Manifests[slash < 0 ? string.Empty : rel.Substring(0, slash)] = File.ReadAllText(full);
                }

                if (IsContentFile(rel)) input.ContentFiles.Add(rel);
            }

            input.AllFiles.Sort(StringComparer.Ordinal);
            input.ContentFiles.Sort(StringComparer.Ordinal);

            string ledger = Path.Combine(scope, AudioFolder, "LICENSES.md");
            if (File.Exists(ledger))
            {
                foreach (KeyValuePair<string, string> e in ReadAudioLedgerSources(File.ReadAllText(ledger)))
                {
                    input.AudioLedgerSources[e.Key] = e.Value;
                }
            }

            string register = Path.Combine(repoRoot, RegisterFolder.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(register))
            {
                string[] fragments = Directory.GetFiles(register, "*.csv", SearchOption.TopDirectoryOnly);
                Array.Sort(fragments, StringComparer.Ordinal);
                foreach (string f in fragments)
                {
                    input.Rows.AddRange(ParseFragment(RegisterFolder + "/" + Path.GetFileName(f), File.ReadAllText(f), issues));
                }
            }

            input.Sha256 = rel =>
            {
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(File.ReadAllBytes(Path.Combine(scope, rel.Replace('/', Path.DirectorySeparatorChar))));
                    var sb = new StringBuilder(64);
                    foreach (byte b in hash) sb.Append(b.ToString("x2"));
                    return sb.ToString();
                }
            };
            return input;
        }

        /// <summary>
        /// Rows of the main table in Audio/LICENSES.md (| `File` | Origin | Author | License | Source | SHA-256 |), outside
        /// fenced blocks: file (relative to Audio/) → "author | license | source" (for the forbidden-source scan).
        /// Full ledger validation (SHA, licences, attribution) stays in AudioPipelineRules / AudioRepositoryTests.
        /// </summary>
        public static Dictionary<string, string> ReadAudioLedgerSources(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            bool fenced = false;
            foreach (string raw in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("```", StringComparison.Ordinal)) { fenced = !fenced; continue; }
                if (fenced || !line.StartsWith("|", StringComparison.Ordinal)) continue;
                string[] cells = line.Trim('|').Split('|');
                if (cells.Length != 6) continue;
                string file = cells[0].Trim().Trim('`').Trim();
                if (!file.Contains("/") || !IsContentFile(file)) continue;
                if (!result.ContainsKey(file)) result[file] = cells[2].Trim() + " | " + cells[3].Trim() + " | " + cells[4].Trim();
            }

            return result;
        }
    }

    public sealed class AssetProvenanceRow
    {
        public string Fragment { get; set; }
        public int Row { get; set; }
        public Dictionary<string, string> Values { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public string Get(string key)
        {
            return Values.TryGetValue(key, out string v) ? v : null;
        }

        /// <summary>The row as an ASSET_SOURCE.txt record, so the same licence/origin rules apply (AssetSourceManifest).</summary>
        public string ToManifestText()
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in Values)
            {
                if (kv.Key == "path" || kv.Key == "sha256" || string.IsNullOrWhiteSpace(kv.Value)) continue;
                sb.Append(kv.Key).Append(": ").Append(kv.Value.Replace("\n", " ")).Append('\n');
            }

            return sb.ToString();
        }
    }

    public sealed class AssetProvenanceInput
    {
        /// <summary>Every non-.meta file in scope (relative to client/Assets/MetinAR).</summary>
        public List<string> AllFiles { get; } = new List<string>();

        /// <summary>Content files in scope (see <see cref="AssetProvenanceRegister.ContentExtensions"/>).</summary>
        public List<string> ContentFiles { get; } = new List<string>();

        /// <summary>Folder (relative, "" = scope root) → ASSET_SOURCE.txt text.</summary>
        public Dictionary<string, string> Manifests { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Audio/LICENSES.md rows: file relative to Audio/ → author | licence | source.</summary>
        public Dictionary<string, string> AudioLedgerSources { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public List<AssetProvenanceRow> Rows { get; } = new List<AssetProvenanceRow>();

        /// <summary>Relative path → lowercase hex SHA-256 of the file bytes (null = not checked).</summary>
        public Func<string, string> Sha256 { get; set; }
    }
}
