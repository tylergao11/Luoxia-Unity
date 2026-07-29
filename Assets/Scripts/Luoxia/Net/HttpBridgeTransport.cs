using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Luoxia.Net
{
    /// <summary>
    /// HTTP framing for Engine headless runtime:
    /// POST /api/client-envelope  Content-Type: application/json
    /// body: one ClientEnvelope
    /// response: ordered JSON array of ServerEnvelopes
    /// </summary>
    public sealed class HttpBridgeTransport : IBridgeTransport
    {
        private readonly string _endpointUrl;
        private readonly int _timeoutSeconds;

        public HttpBridgeTransport(string baseUrl, int timeoutSeconds = 60)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new ArgumentException("baseUrl required", nameof(baseUrl));
            }

            baseUrl = baseUrl.TrimEnd('/');
            _endpointUrl = baseUrl.EndsWith("/api/client-envelope", StringComparison.Ordinal)
                ? baseUrl
                : baseUrl + "/api/client-envelope";
            _timeoutSeconds = Mathf.Max(5, timeoutSeconds);
        }

        public string EndpointUrl => _endpointUrl;

        public async Task<string[]> SendClientEnvelopeAsync(string clientEnvelopeJson, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(clientEnvelopeJson))
            {
                throw new ArgumentException("envelope required", nameof(clientEnvelopeJson));
            }

            using var request = new UnityWebRequest(_endpointUrl, UnityWebRequest.kHttpVerbPOST);
            var body = Encoding.UTF8.GetBytes(clientEnvelopeJson);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = _timeoutSeconds;

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
            }

#if UNITY_2020_2_OR_NEWER
            var failed = request.result != UnityWebRequest.Result.Success;
#else
            var failed = request.isNetworkError || request.isHttpError;
#endif
            if (failed)
            {
                var detail = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                throw new BridgeTransportException(
                    $"HTTP {(long)request.responseCode} {request.error}: {detail}",
                    (long)request.responseCode);
            }

            var text = request.downloadHandler.text ?? "[]";
            // Caller parses; return as single raw payload entry for batch parse.
            return new[] { text };
        }
    }

    public sealed class BridgeTransportException : Exception
    {
        public long StatusCode { get; }

        public BridgeTransportException(string message, long statusCode) : base(message)
        {
            StatusCode = statusCode;
        }
    }
}
