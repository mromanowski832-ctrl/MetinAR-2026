using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MetinAR.Map
{
    public sealed class TileCacheEntry
    {
        public byte[] Data { get; set; }

        public DateTimeOffset FreshUntil { get; set; }

        public string ETag { get; set; }

        public string LastModified { get; set; }
    }

    /// <summary>
    /// Bounded on-device tile cache. One data file + one small metadata file per key under
    /// <c>root</c>. When the total data size exceeds <c>maxBytes</c>, least-recently-used entries are deleted.
    /// The cache lives in the app's cache directory (the OS may clear it) and is never committed or uploaded.
    /// Thread-safe.
    /// </summary>
    public sealed class DiskTileCache
    {
        private readonly object _lock = new object();
        private readonly string _root;
        private readonly long _maxBytes;
        private readonly Func<DateTimeOffset> _clock;
        private Dictionary<string, IndexEntry> _index;
        private long _totalBytes;
        private long _accessCounter;

        public DiskTileCache(string root, long maxBytes, Func<DateTimeOffset> clock = null)
        {
            if (string.IsNullOrEmpty(root))
            {
                throw new ArgumentException("Cache root is required.", nameof(root));
            }

            if (maxBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBytes));
            }

            _root = root;
            _maxBytes = maxBytes;
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        public string Root => _root;

        public long MaxBytes => _maxBytes;

        public long TotalBytes
        {
            get
            {
                lock (_lock)
                {
                    EnsureIndex();
                    return _totalBytes;
                }
            }
        }

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    EnsureIndex();
                    return _index.Count;
                }
            }
        }

        public bool TryGet(string key, out TileCacheEntry entry)
        {
            lock (_lock)
            {
                EnsureIndex();
                entry = null;
                string safe = SafeKey(key);
                if (!_index.TryGetValue(safe, out IndexEntry idx))
                {
                    return false;
                }

                try
                {
                    byte[] data = File.ReadAllBytes(DataPath(safe));
                    entry = new TileCacheEntry
                    {
                        Data = data,
                        FreshUntil = idx.FreshUntil,
                        ETag = idx.ETag,
                        LastModified = idx.LastModified
                    };
                    idx.LastAccess = ++_accessCounter;
                    return true;
                }
                catch (IOException)
                {
                    RemoveLocked(safe);
                    return false;
                }
                catch (UnauthorizedAccessException)
                {
                    RemoveLocked(safe);
                    return false;
                }
            }
        }

        public void Put(string key, TileCacheEntry entry)
        {
            if (entry == null || entry.Data == null)
            {
                return;
            }

            lock (_lock)
            {
                EnsureIndex();
                string safe = SafeKey(key);
                if (entry.Data.LongLength > _maxBytes)
                {
                    RemoveLocked(safe);
                    return;
                }

                Directory.CreateDirectory(_root);
                File.WriteAllBytes(DataPath(safe), entry.Data);
                WriteMeta(safe, entry.FreshUntil, entry.ETag, entry.LastModified);
                if (_index.TryGetValue(safe, out IndexEntry old))
                {
                    _totalBytes -= old.Size;
                }

                _index[safe] = new IndexEntry
                {
                    Size = entry.Data.LongLength,
                    FreshUntil = entry.FreshUntil,
                    ETag = entry.ETag,
                    LastModified = entry.LastModified,
                    LastAccess = ++_accessCounter
                };
                _totalBytes += entry.Data.LongLength;
                EvictLocked();
            }
        }

        /// <summary>Updates freshness metadata after a 304 Not Modified.</summary>
        public void Refresh(string key, DateTimeOffset freshUntil, string etag, string lastModified)
        {
            lock (_lock)
            {
                EnsureIndex();
                string safe = SafeKey(key);
                if (!_index.TryGetValue(safe, out IndexEntry idx))
                {
                    return;
                }

                idx.FreshUntil = freshUntil;
                idx.ETag = string.IsNullOrEmpty(etag) ? idx.ETag : etag;
                idx.LastModified = string.IsNullOrEmpty(lastModified) ? idx.LastModified : lastModified;
                idx.LastAccess = ++_accessCounter;
                WriteMeta(safe, idx.FreshUntil, idx.ETag, idx.LastModified);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                if (Directory.Exists(_root))
                {
                    foreach (string f in Directory.GetFiles(_root))
                    {
                        TryDelete(f);
                    }
                }

                _index = new Dictionary<string, IndexEntry>();
                _totalBytes = 0;
            }
        }

        private void EnsureIndex()
        {
            if (_index != null)
            {
                return;
            }

            _index = new Dictionary<string, IndexEntry>();
            _totalBytes = 0;
            if (!Directory.Exists(_root))
            {
                return;
            }

            // Rebuild from disk; file modification time orders LRU across app restarts.
            var found = new List<KeyValuePair<string, DateTime>>();
            foreach (string dataFile in Directory.GetFiles(_root, "*.tile"))
            {
                string safe = Path.GetFileNameWithoutExtension(dataFile);
                if (!TryReadMeta(safe, out DateTimeOffset fresh, out string etag, out string lm))
                {
                    TryDelete(dataFile);
                    continue;
                }

                var info = new FileInfo(dataFile);
                _index[safe] = new IndexEntry { Size = info.Length, FreshUntil = fresh, ETag = etag, LastModified = lm };
                _totalBytes += info.Length;
                found.Add(new KeyValuePair<string, DateTime>(safe, info.LastWriteTimeUtc));
            }

            found.Sort((a, b) => a.Value.CompareTo(b.Value));
            foreach (KeyValuePair<string, DateTime> kv in found)
            {
                _index[kv.Key].LastAccess = ++_accessCounter;
            }

            EvictLocked();
        }

        private void EvictLocked()
        {
            while (_totalBytes > _maxBytes && _index.Count > 0)
            {
                string oldest = null;
                long oldestAccess = long.MaxValue;
                foreach (KeyValuePair<string, IndexEntry> kv in _index)
                {
                    if (kv.Value.LastAccess < oldestAccess)
                    {
                        oldestAccess = kv.Value.LastAccess;
                        oldest = kv.Key;
                    }
                }

                RemoveLocked(oldest);
            }
        }

        private void RemoveLocked(string safe)
        {
            if (_index.TryGetValue(safe, out IndexEntry idx))
            {
                _totalBytes -= idx.Size;
                _index.Remove(safe);
            }

            TryDelete(DataPath(safe));
            TryDelete(MetaPath(safe));
        }

        private void WriteMeta(string safe, DateTimeOffset freshUntil, string etag, string lastModified)
        {
            var sb = new StringBuilder();
            sb.Append("fresh=").Append(freshUntil.UtcTicks.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("etag=").Append(Clean(etag)).Append('\n');
            sb.Append("lastmod=").Append(Clean(lastModified)).Append('\n');
            File.WriteAllText(MetaPath(safe), sb.ToString());
        }

        private bool TryReadMeta(string safe, out DateTimeOffset fresh, out string etag, out string lastModified)
        {
            fresh = default;
            etag = null;
            lastModified = null;
            string path = MetaPath(safe);
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                bool hasFresh = false;
                foreach (string line in File.ReadAllLines(path))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }

                    string k = line.Substring(0, eq);
                    string v = line.Substring(eq + 1);
                    if (k == "fresh" && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
                    {
                        fresh = new DateTimeOffset(ticks, TimeSpan.Zero);
                        hasFresh = true;
                    }
                    else if (k == "etag")
                    {
                        etag = v.Length == 0 ? null : v;
                    }
                    else if (k == "lastmod")
                    {
                        lastModified = v.Length == 0 ? null : v;
                    }
                }

                return hasFresh;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private string DataPath(string safe) => Path.Combine(_root, safe + ".tile");

        private string MetaPath(string safe) => Path.Combine(_root, safe + ".meta");

        private static string Clean(string s) => string.IsNullOrEmpty(s) ? string.Empty : s.Replace("\n", string.Empty).Replace("\r", string.Empty);

        /// <summary>Maps any key (e.g. "openfreemap/14/9099/5552") to a flat, filesystem-safe name.</summary>
        public static string SafeKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Key is required.", nameof(key));
            }

            var sb = new StringBuilder(key.Length);
            foreach (char c in key)
            {
                sb.Append(char.IsLetterOrDigit(c) || c == '-' ? c : '_');
            }

            return sb.ToString();
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private sealed class IndexEntry
        {
            public long Size;
            public DateTimeOffset FreshUntil;
            public string ETag;
            public string LastModified;
            public long LastAccess;
        }
    }
}
