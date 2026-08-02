using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Luoxia.Contracts;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Luoxia.Net
{
    /// <summary>
    /// Loopback Deployment provision gateway client (provision-gateway.v1).
    /// Opens a brand-new world+session via POST /provision/new-play.
    /// Pack selection is Deployment-owned — Host never names or branches on pack_id.
    /// </summary>
    public static class ProvisionGatewayClient
    {
        public const string ModelDispatchAmbiguousCode = "runtime.kernel.model_dispatch_ambiguous";
        public const string DefaultEngineBaseUrl = "http://127.0.0.1:8000";
        public const string DefaultPlayerLocale = "zh-CN";
        public const string DefaultPlayerName = "试玩者";

        private static readonly string[] DeploymentEnvCandidates =
        {
            @"C:\Ai\Luoxia-Deployment\.env.local",
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Luoxia-Deployment", ".env.local")),
        };

        /// <summary>
        /// Reads loopback provision settings from Deployment .env.local.
        /// Requires port + shared secret. Engine URL / player locale / name fall back to local-play defaults
        /// when the env file omits those keys (same resolution as Editor Play Accept).
        /// </summary>
        public static bool TryLoadLocalSettings(out ProvisionLocalSettings settings, out string error)
        {
            settings = default;
            error = null;

            if (!TryReadDeploymentEnv(
                    out var port,
                    out var secret,
                    out var engineBaseUrl,
                    out var locale,
                    out var playerName,
                    out var envPath))
            {
                error =
                    "未找到 Deployment .env.local，或缺少 LUOXIA_PROVISION_PORT / LUOXIA_PROVISION_SHARED_SECRET。"
                    + " 期望路径含 C:\\Ai\\Luoxia-Deployment\\.env.local。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(engineBaseUrl))
            {
                engineBaseUrl = DefaultEngineBaseUrl;
            }

            if (string.IsNullOrWhiteSpace(locale))
            {
                locale = DefaultPlayerLocale;
            }

            if (string.IsNullOrWhiteSpace(playerName))
            {
                playerName = DefaultPlayerName;
            }

            settings = new ProvisionLocalSettings(
                port,
                secret.Trim(),
                engineBaseUrl.Trim().TrimEnd('/'),
                locale.Trim(),
                playerName.Trim(),
                envPath);
            return true;
        }

        /// <summary>
        /// POST /provision/new-play. Never invents pack_id. On HTTP/parse failure returns Ok=false with detail.
        /// </summary>
        public static async Task<ProvisionNewPlayOutcome> ProvisionNewPlayAsync(
            ProvisionLocalSettings settings,
            string localeOverride = null,
            string playerNameOverride = null)
        {
            if (settings.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(settings.SharedSecret))
            {
                return ProvisionNewPlayOutcome.Fail(
                    "provision.settings.invalid",
                    "Provision port and shared secret are required.");
            }

            var locale = string.IsNullOrWhiteSpace(localeOverride) ? settings.PlayerLocale : localeOverride.Trim();
            var playerName = string.IsNullOrWhiteSpace(playerNameOverride)
                ? settings.PlayerName
                : playerNameOverride.Trim();
            if (string.IsNullOrWhiteSpace(locale) || string.IsNullOrWhiteSpace(playerName))
            {
                return ProvisionNewPlayOutcome.Fail(
                    "provision.player_name.invalid",
                    "Player locale and name are required for provision/new-play.");
            }

            var url = $"http://127.0.0.1:{settings.Port}/provision/new-play";
            var body = BridgeJson.Serialize(new SimplePlayerNameBody
            {
                locale = locale,
                text = playerName
            });

            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            request.SetRequestHeader("x-luoxia-provision-secret", settings.SharedSecret);
            request.timeout = 600;

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            var responseText = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
#if UNITY_2020_2_OR_NEWER
            var transportFailed = request.result != UnityWebRequest.Result.Success;
#else
            var transportFailed = request.isNetworkError || request.isHttpError;
#endif
            if (transportFailed)
            {
                var detail = string.IsNullOrEmpty(responseText) ? request.error : responseText;
                if (TryParseFault(detail, out var fault))
                {
                    return ProvisionNewPlayOutcome.Fail(fault.Code, fault.Message, detail, fault.IsAmbiguous);
                }

                return ProvisionNewPlayOutcome.Fail(
                    "provision.transport_failed",
                    detail ?? "provision transport failed",
                    detail);
            }

            if (!TryParseSuccess(responseText, out var parsed, out var parseError))
            {
                if (TryParseFault(responseText, out var fault))
                {
                    return ProvisionNewPlayOutcome.Fail(fault.Code, fault.Message, responseText, fault.IsAmbiguous);
                }

                return ProvisionNewPlayOutcome.Fail(
                    "provision.response_invalid",
                    parseError ?? "invalid provision response",
                    responseText);
            }

            return ProvisionNewPlayOutcome.Succeed(parsed);
        }

        public static bool TryReadDeploymentEnv(
            out int port,
            out string secret,
            out string engineBaseUrl,
            out string playerLocale,
            out string playerName,
            out string envPath)
        {
            port = 0;
            secret = null;
            engineBaseUrl = null;
            playerLocale = null;
            playerName = null;
            envPath = null;

            for (var i = 0; i < DeploymentEnvCandidates.Length; i++)
            {
                var path = DeploymentEnvCandidates[i];
                if (!File.Exists(path))
                {
                    continue;
                }

                string portText = null;
                string secretText = null;
                string engineText = null;
                string localeText = null;
                string nameText = null;
                foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var line = raw?.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var eq = line.IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }

                    var key = line.Substring(0, eq).Trim();
                    var value = line.Substring(eq + 1).Trim();
                    if (string.Equals(key, "LUOXIA_PROVISION_PORT", StringComparison.Ordinal))
                    {
                        portText = value;
                    }
                    else if (string.Equals(key, "LUOXIA_PROVISION_SHARED_SECRET", StringComparison.Ordinal))
                    {
                        secretText = value;
                    }
                    else if (string.Equals(key, "LUOXIA_ENGINE_BASE_URL", StringComparison.Ordinal))
                    {
                        engineText = value;
                    }
                    else if (string.Equals(key, "LUOXIA_PLAYER_LOCALE", StringComparison.Ordinal))
                    {
                        localeText = value;
                    }
                    else if (string.Equals(key, "LUOXIA_PLAYER_DISPLAY_NAME", StringComparison.Ordinal))
                    {
                        nameText = value;
                    }
                }

                if (!string.IsNullOrWhiteSpace(portText)
                    && int.TryParse(portText, out var parsedPort)
                    && parsedPort is >= 1 and <= 65535
                    && !string.IsNullOrWhiteSpace(secretText))
                {
                    port = parsedPort;
                    secret = secretText.Trim();
                    engineBaseUrl = string.IsNullOrWhiteSpace(engineText) ? null : engineText.Trim();
                    playerLocale = string.IsNullOrWhiteSpace(localeText) ? null : localeText.Trim();
                    playerName = string.IsNullOrWhiteSpace(nameText) ? null : nameText.Trim();
                    envPath = path;
                    return true;
                }
            }

            return false;
        }

        public static bool TryParseSuccess(string json, out ProvisionNewPlaySuccess parsed, out string error)
        {
            parsed = default;
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "empty body";
                return false;
            }

            try
            {
                var root = JObject.Parse(json);
                var sessionId = root.Value<string>("session_id");
                var worldId = root.Value<string>("world_id");
                var bindingId = root.Value<string>("control_binding_id");
                var envelopes = root["server_envelopes"] as JArray;
                if (string.IsNullOrWhiteSpace(sessionId)
                    || string.IsNullOrWhiteSpace(worldId)
                    || string.IsNullOrWhiteSpace(bindingId)
                    || envelopes == null
                    || envelopes.Count < 1)
                {
                    error = "missing session_id/world_id/control_binding_id/server_envelopes";
                    return false;
                }

                parsed = new ProvisionNewPlaySuccess(
                    sessionId.Trim(),
                    worldId.Trim(),
                    bindingId.Trim(),
                    envelopes.ToString(Newtonsoft.Json.Formatting.None));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TryParseFault(string body, out ProvisionFaultInfo fault)
        {
            fault = default;
            if (string.IsNullOrWhiteSpace(body))
            {
                return false;
            }

            var trimmed = body.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                try
                {
                    var root = JObject.Parse(trimmed);
                    var code = root.Value<string>("code");
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        code = root.Value<string>("failure_code");
                    }

                    var message = root.Value<string>("message");
                    var details = root["details"] as JObject;
                    if (details != null
                        && string.IsNullOrWhiteSpace(code)
                        && details.Value<string>("failure_code") is { Length: > 0 } nested)
                    {
                        code = nested;
                    }

                    if (string.IsNullOrWhiteSpace(code)
                        && trimmed.IndexOf(ModelDispatchAmbiguousCode, StringComparison.Ordinal) >= 0)
                    {
                        code = ModelDispatchAmbiguousCode;
                    }

                    if (string.IsNullOrWhiteSpace(code))
                    {
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(message))
                    {
                        message = trimmed;
                    }

                    fault = new ProvisionFaultInfo(
                        code.Trim(),
                        message.Trim(),
                        string.Equals(code.Trim(), ModelDispatchAmbiguousCode, StringComparison.Ordinal));
                    return true;
                }
                catch (Exception)
                {
                    // Fall through to plain-text ambiguous detection.
                }
            }

            if (trimmed.IndexOf(ModelDispatchAmbiguousCode, StringComparison.Ordinal) >= 0)
            {
                fault = new ProvisionFaultInfo(
                    ModelDispatchAmbiguousCode,
                    trimmed,
                    isAmbiguous: true);
                return true;
            }

            return false;
        }

        [Serializable]
        private sealed class SimplePlayerNameBody
        {
            public string locale;
            public string text;
        }
    }

    public readonly struct ProvisionLocalSettings
    {
        public ProvisionLocalSettings(
            int port,
            string sharedSecret,
            string engineBaseUrl,
            string playerLocale,
            string playerName,
            string envPath)
        {
            Port = port;
            SharedSecret = sharedSecret;
            EngineBaseUrl = engineBaseUrl;
            PlayerLocale = playerLocale;
            PlayerName = playerName;
            EnvPath = envPath;
        }

        public int Port { get; }
        public string SharedSecret { get; }
        public string EngineBaseUrl { get; }
        public string PlayerLocale { get; }
        public string PlayerName { get; }
        public string EnvPath { get; }
    }

    public readonly struct ProvisionNewPlaySuccess
    {
        public ProvisionNewPlaySuccess(
            string sessionId,
            string worldId,
            string controlBindingId,
            string serverEnvelopesJson)
        {
            SessionId = sessionId;
            WorldId = worldId;
            ControlBindingId = controlBindingId;
            ServerEnvelopesJson = serverEnvelopesJson;
        }

        public string SessionId { get; }
        public string WorldId { get; }
        public string ControlBindingId { get; }
        public string ServerEnvelopesJson { get; }
    }

    public readonly struct ProvisionFaultInfo
    {
        public ProvisionFaultInfo(string code, string message, bool isAmbiguous)
        {
            Code = code;
            Message = message;
            IsAmbiguous = isAmbiguous;
        }

        public string Code { get; }
        public string Message { get; }
        public bool IsAmbiguous { get; }
    }

    public readonly struct ProvisionNewPlayOutcome
    {
        private ProvisionNewPlayOutcome(
            bool ok,
            ProvisionNewPlaySuccess success,
            string code,
            string message,
            string rawBody,
            bool isAmbiguous)
        {
            Ok = ok;
            Success = success;
            Code = code;
            Message = message;
            RawBody = rawBody;
            IsAmbiguous = isAmbiguous;
        }

        public bool Ok { get; }
        public ProvisionNewPlaySuccess Success { get; }
        public string Code { get; }
        public string Message { get; }
        public string RawBody { get; }
        public bool IsAmbiguous { get; }

        public static ProvisionNewPlayOutcome Succeed(ProvisionNewPlaySuccess success) =>
            new ProvisionNewPlayOutcome(true, success, null, null, null, false);

        public static ProvisionNewPlayOutcome Fail(
            string code,
            string message,
            string rawBody = null,
            bool isAmbiguous = false) =>
            new ProvisionNewPlayOutcome(false, default, code, message, rawBody, isAmbiguous);

        public string FormatDetail()
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(Code))
            {
                sb.Append("code=").Append(Code);
            }

            if (!string.IsNullOrEmpty(Message))
            {
                if (sb.Length > 0)
                {
                    sb.Append('\n');
                }

                sb.Append(Message);
            }

            if (!string.IsNullOrEmpty(RawBody)
                && !string.Equals(RawBody, Message, StringComparison.Ordinal))
            {
                if (sb.Length > 0)
                {
                    sb.Append('\n');
                }

                sb.Append(RawBody);
            }

            return sb.Length > 0 ? sb.ToString() : "provision failed";
        }
    }
}
