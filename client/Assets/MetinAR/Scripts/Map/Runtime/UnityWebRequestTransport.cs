using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace MetinAR.Map.Runtime
{
    /// <summary>
    /// <see cref="ITileHttpTransport"/> on top of <see cref="UnityWebRequest"/>. UnityWebRequest may only be
    /// touched on the main thread, while <see cref="CachedHttpFetcher"/> continues on pool threads
    /// (ConfigureAwait(false)), so every request is posted to the main-thread SynchronizationContext captured
    /// at construction. Sends only the headers it is given (User-Agent + cache validators); no cookies,
    /// no analytics, no identifiers.
    /// </summary>
    public sealed class UnityWebRequestTransport : ITileHttpTransport
    {
        private readonly SynchronizationContext _main;
        private readonly int _mainThreadId;
        private bool _userAgentWarningLogged;

        /// <summary>Must be constructed on the Unity main thread (e.g. in Awake/Start).</summary>
        public UnityWebRequestTransport()
        {
            _main = SynchronizationContext.Current
                    ?? throw new InvalidOperationException("UnityWebRequestTransport must be created on the Unity main thread.");
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public Task<HttpTileResponse> GetAsync(
            string url,
            IReadOnlyDictionary<string, string> headers,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<HttpTileResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled(cancellationToken);
                return tcs.Task;
            }

            if (Thread.CurrentThread.ManagedThreadId == _mainThreadId)
            {
                Send(url, headers, timeout, cancellationToken, tcs);
            }
            else
            {
                _main.Post(_ => Send(url, headers, timeout, cancellationToken, tcs), null);
            }

            return tcs.Task;
        }

        private void Send(
            string url,
            IReadOnlyDictionary<string, string> headers,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            TaskCompletionSource<HttpTileResponse> tcs)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled(cancellationToken);
                return;
            }

            UnityWebRequest request;
            try
            {
                request = UnityWebRequest.Get(url);
            }
            catch (Exception e)
            {
                tcs.TrySetResult(new HttpTileResponse { StatusCode = 0, Error = e.GetType().Name });
                return;
            }

            request.timeout = Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));
            request.redirectLimit = 3;
            if (headers != null)
            {
                foreach (KeyValuePair<string, string> h in headers)
                {
                    try
                    {
                        request.SetRequestHeader(h.Key, h.Value);
                    }
                    catch (Exception)
                    {
                        // Some platforms forbid overriding certain headers (e.g. User-Agent on WebGL).
                        if (!_userAgentWarningLogged)
                        {
                            _userAgentWarningLogged = true;
                            Debug.LogWarning("[OsmMap] Platform refused request header '" + h.Key + "'.");
                        }
                    }
                }
            }

            bool finished = false;
            CancellationTokenRegistration registration = default;
            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(() => _main.Post(_ =>
                {
                    if (!finished)
                    {
                        request.Abort();
                    }
                }, null));
            }

            UnityWebRequestAsyncOperation op = request.SendWebRequest();
            op.completed += _ =>
            {
                finished = true;
                registration.Dispose();
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(cancellationToken);
                        return;
                    }

                    var response = new HttpTileResponse();
                    if (request.result == UnityWebRequest.Result.ConnectionError
                        || request.result == UnityWebRequest.Result.DataProcessingError)
                    {
                        response.StatusCode = 0;
                        response.Error = request.error;
                        response.TimedOut = request.error != null
                                            && request.error.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                    else
                    {
                        response.StatusCode = (int)request.responseCode;
                        response.Body = request.downloadHandler != null ? request.downloadHandler.data : null;
                        Dictionary<string, string> responseHeaders = request.GetResponseHeaders();
                        if (responseHeaders != null)
                        {
                            foreach (KeyValuePair<string, string> h in responseHeaders)
                            {
                                response.Headers[h.Key] = h.Value;
                            }
                        }
                    }

                    tcs.TrySetResult(response);
                }
                catch (Exception e)
                {
                    tcs.TrySetResult(new HttpTileResponse { StatusCode = 0, Error = e.GetType().Name });
                }
                finally
                {
                    request.Dispose();
                }
            };
        }
    }
}
