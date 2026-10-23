using System;
using System.Collections.Generic;
using System.Globalization;

namespace MetinAR.Map
{
    /// <summary>Result of interpreting HTTP caching headers for one response.</summary>
    public readonly struct CacheDirective
    {
        public CacheDirective(bool store, DateTimeOffset freshUntil, string etag, string lastModified)
        {
            Store = store;
            FreshUntil = freshUntil;
            ETag = etag;
            LastModified = lastModified;
        }

        /// <summary>False for <c>no-store</c>: the body must not be written to disk.</summary>
        public bool Store { get; }

        /// <summary>Until when the entry may be used without revalidation.</summary>
        public DateTimeOffset FreshUntil { get; }

        public string ETag { get; }

        public string LastModified { get; }
    }

    /// <summary>
    /// Pure, private-client interpretation of Cache-Control / Expires / Age / ETag / Last-Modified
    /// (RFC 9111 subset): no-store → don't store; no-cache → store but always revalidate;
    /// max-age (minus Age) wins over Expires; otherwise <paramref name="fallbackTtl"/>.
    /// The freshness lifetime is capped at <paramref name="maxTtl"/> so a decade-long max-age does not pin data forever.
    /// </summary>
    public static class HttpCachePolicy
    {
        public static CacheDirective Evaluate(
            IReadOnlyDictionary<string, string> headers,
            DateTimeOffset now,
            TimeSpan fallbackTtl,
            TimeSpan maxTtl)
        {
            string cacheControl = Get(headers, "Cache-Control");
            string etag = Get(headers, "ETag");
            string lastModified = Get(headers, "Last-Modified");

            bool noStore = false;
            bool noCache = false;
            long? maxAge = null;
            if (!string.IsNullOrEmpty(cacheControl))
            {
                foreach (string raw in cacheControl.Split(','))
                {
                    string d = raw.Trim();
                    if (d.Length == 0)
                    {
                        continue;
                    }

                    string lower = d.ToLowerInvariant();
                    if (lower == "no-store")
                    {
                        noStore = true;
                    }
                    else if (lower == "no-cache" || lower.StartsWith("no-cache=", StringComparison.Ordinal))
                    {
                        noCache = true;
                    }
                    else if (lower.StartsWith("max-age", StringComparison.Ordinal))
                    {
                        int eq = lower.IndexOf('=');
                        if (eq > 0 && long.TryParse(lower.Substring(eq + 1).Trim().Trim('"'), NumberStyles.Integer, CultureInfo.InvariantCulture, out long secs))
                        {
                            maxAge = Math.Max(0, secs);
                        }
                    }
                }
            }

            if (noStore)
            {
                return new CacheDirective(false, now, etag, lastModified);
            }

            if (noCache)
            {
                return new CacheDirective(true, now, etag, lastModified);
            }

            TimeSpan lifetime;
            if (maxAge.HasValue)
            {
                long age = 0;
                string ageHeader = Get(headers, "Age");
                if (!string.IsNullOrEmpty(ageHeader))
                {
                    long.TryParse(ageHeader.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out age);
                }

                lifetime = TimeSpan.FromSeconds(Math.Max(0, Math.Min(maxAge.Value - age, (long)maxTtl.TotalSeconds)));
            }
            else if (TryParseHttpDate(Get(headers, "Expires"), out DateTimeOffset expires))
            {
                DateTimeOffset dateBase = TryParseHttpDate(Get(headers, "Date"), out DateTimeOffset date) ? date : now;
                lifetime = expires - dateBase;
                if (lifetime < TimeSpan.Zero)
                {
                    lifetime = TimeSpan.Zero;
                }
            }
            else
            {
                lifetime = fallbackTtl;
            }

            if (lifetime > maxTtl)
            {
                lifetime = maxTtl;
            }

            return new CacheDirective(true, now + lifetime, etag, lastModified);
        }

        public static bool TryParseHttpDate(string value, out DateTimeOffset result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return DateTimeOffset.TryParseExact(
                value.Trim(),
                "r",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out result);
        }

        private static string Get(IReadOnlyDictionary<string, string> headers, string name)
        {
            if (headers == null)
            {
                return null;
            }

            if (headers.TryGetValue(name, out string v))
            {
                return v;
            }

            foreach (KeyValuePair<string, string> kv in headers)
            {
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return kv.Value;
                }
            }

            return null;
        }
    }
}
