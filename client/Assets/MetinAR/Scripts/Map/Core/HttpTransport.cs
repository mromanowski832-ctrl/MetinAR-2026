using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MetinAR.Map
{
    /// <summary>Minimal HTTP response used by the map tile pipeline.</summary>
    public sealed class HttpTileResponse
    {
        public HttpTileResponse()
        {
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>HTTP status code, or 0 when no response arrived (network error / timeout).</summary>
        public int StatusCode { get; set; }

        public byte[] Body { get; set; }

        /// <summary>Response headers, case-insensitive. Repeated headers are joined with ", ".</summary>
        public Dictionary<string, string> Headers { get; }

        public bool TimedOut { get; set; }

        public string Error { get; set; }
    }

    /// <summary>
    /// Transport seam. Production uses UnityWebRequest; tests use a fake. Implementations must send the
    /// given headers (including User-Agent) and must not add telemetry or extra identifiers.
    /// </summary>
    public interface ITileHttpTransport
    {
        Task<HttpTileResponse> GetAsync(
            string url,
            IReadOnlyDictionary<string, string> requestHeaders,
            TimeSpan timeout,
            CancellationToken cancellationToken);
    }
}
