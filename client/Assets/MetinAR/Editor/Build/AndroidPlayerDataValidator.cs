using System;
using System.Collections.Generic;
using System.Globalization;

namespace MetinAR.Editor.Build
{
    /// <summary>
    /// Engine-free check of the serialized player data packed into an Android APK (entries under
    /// <c>assets/bin/Data/</c>). Unity may store a serialized file either whole (<c>sharedassets1.assets</c>)
    /// or split into 1 MB chunks (<c>sharedassets1.assets.split0..N</c>), and the player prefers the split
    /// chunks when they exist. If a stale Gradle export directory survives from an older build, the APK can
    /// contain both forms of the same file: the player then reads the old chunks against the new
    /// <c>level*</c>/<c>globalgamemanagers</c> data and crashes on scene load with a serialization layout
    /// mismatch (#31: SIGTRAP in Loading.Preload, CachedReader::OutOfBoundsError).
    /// </summary>
    public static class AndroidPlayerDataValidator
    {
        public const string DataPrefix = "assets/bin/Data/";
        private const string SplitMarker = ".split";

        /// <summary>
        /// Returns one human-readable problem per inconsistent serialized file; an empty list means the
        /// player data layout is consistent. Entry names use '/' as in a zip/APK listing.
        /// </summary>
        public static IReadOnlyList<string> FindProblems(IEnumerable<string> apkEntryNames)
        {
            if (apkEntryNames == null)
            {
                throw new ArgumentNullException(nameof(apkEntryNames));
            }

            var whole = new HashSet<string>(StringComparer.Ordinal);
            var splits = new SortedDictionary<string, SortedSet<int>>(StringComparer.Ordinal);

            foreach (string raw in apkEntryNames)
            {
                if (string.IsNullOrEmpty(raw))
                {
                    continue;
                }

                string name = raw.Replace('\\', '/');
                if (!name.StartsWith(DataPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string relative = name.Substring(DataPrefix.Length);
                if (relative.Length == 0 || relative.IndexOf('/') >= 0)
                {
                    continue; // Managed/, Resources/ etc. are not split serialized files.
                }

                if (TryParseSplit(relative, out string baseName, out int index))
                {
                    if (!splits.TryGetValue(baseName, out SortedSet<int> set))
                    {
                        set = new SortedSet<int>();
                        splits.Add(baseName, set);
                    }

                    set.Add(index);
                }
                else
                {
                    whole.Add(relative);
                }
            }

            var problems = new List<string>();
            foreach (KeyValuePair<string, SortedSet<int>> pair in splits)
            {
                string baseName = pair.Key;
                SortedSet<int> indices = pair.Value;
                if (whole.Contains(baseName))
                {
                    problems.Add(
                        DataPrefix + baseName + " exists both whole and as " + indices.Count
                        + " .split chunk(s); the player reads the chunks, which come from an older build");
                }

                int expected = 0;
                foreach (int i in indices)
                {
                    if (i != expected)
                    {
                        problems.Add(
                            DataPrefix + baseName + " split chunks are not contiguous: missing .split"
                            + expected.ToString(CultureInfo.InvariantCulture));
                        break;
                    }

                    expected++;
                }
            }

            return problems;
        }

        private static bool TryParseSplit(string relative, out string baseName, out int index)
        {
            baseName = null;
            index = -1;
            int at = relative.LastIndexOf(SplitMarker, StringComparison.Ordinal);
            if (at <= 0)
            {
                return false;
            }

            string digits = relative.Substring(at + SplitMarker.Length);
            if (digits.Length == 0)
            {
                return false;
            }

            foreach (char c in digits)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }

            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out index))
            {
                return false;
            }

            baseName = relative.Substring(0, at);
            return true;
        }
    }
}
