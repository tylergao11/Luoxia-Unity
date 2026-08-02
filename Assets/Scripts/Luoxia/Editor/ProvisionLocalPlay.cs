#if UNITY_EDITOR
using System;
using System.IO;
using System.Threading.Tasks;
using Luoxia.App;
using Luoxia.Contracts;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;

namespace Luoxia.Editor
{
    /// <summary>
    /// Loopback local-play bootstrap: calls the Deployment provision server,
    /// writes LuoxiaClientBootstrap fields, and switches to EngineWithInitialView.
    /// Does not open worlds through Engine client-envelope. Pack selection is
    /// Deployment-owned — this Host never names or branches on pack_id.
    /// </summary>
    internal static class ProvisionLocalPlay
    {
        private const string PrefPort = "Luoxia.Provision.Port";
        private const string PrefSecret = "Luoxia.Provision.Secret";
        private const string PrefEngineBaseUrl = "Luoxia.Engine.BaseUrl";
        private const string PrefPlayerName = "Luoxia.Provision.PlayerName";
        private const string PrefPlayerLocale = "Luoxia.Provision.PlayerLocale";
        private const string DefaultEngineBaseUrl = "http://127.0.0.1:8000";
        private const string DefaultPlayerLocale = "zh-CN";
        private const string DefaultPlayerName = "试玩者";
        private static readonly string[] DeploymentEnvCandidates =
        {
            @"C:\Ai\Luoxia-Deployment\.env.local",
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Luoxia-Deployment", ".env.local")),
        };

        [MenuItem("Luoxia/Play/Configure Local Provision")]
        public static void Configure()
        {
            ProvisionSettingsWindow.Open();
        }

        [MenuItem("Luoxia/Play/Provision Local")]
        public static void Provision()
        {
            TryHydratePrefsFromDeploymentEnv();
            if (!TryReadSettings(out var port, out var secret, out var engineBaseUrl, out var locale, out var playerName))
            {
                EditorUtility.DisplayDialog(
                    "Luoxia Provision",
                    "缺少本地 Provision 配置。请先打开 Luoxia/Play/Configure Local Provision，点「从 Deployment .env.local 加载」后 Save。",
                    "OK");
                ProvisionSettingsWindow.Open();
                return;
            }

            var bootstrap = UnityEngine.Object.FindObjectOfType<LuoxiaClientBootstrap>();
            if (bootstrap == null)
            {
                EditorUtility.DisplayDialog(
                    "Luoxia Provision",
                    "No LuoxiaClientBootstrap in the open scene.",
                    "OK");
                return;
            }

            _ = RunProvisionAsync(bootstrap, port, secret, engineBaseUrl, locale, playerName);
        }

        private static async Task RunProvisionAsync(
            LuoxiaClientBootstrap bootstrap,
            int port,
            string secret,
            string engineBaseUrl,
            string locale,
            string playerName)
        {
            var url = $"http://127.0.0.1:{port}/provision/new-play";
            var body = BridgeJson.Serialize(new SimplePlayerNameBody
            {
                locale = locale,
                text = playerName
            });

            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            request.SetRequestHeader("x-luoxia-provision-secret", secret);
            request.timeout = 600;

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                var detail = string.IsNullOrEmpty(request.downloadHandler?.text)
                    ? request.error
                    : request.downloadHandler.text;
                Debug.LogError($"[Luoxia] provision failed: {detail}");
                if (ProvisionFaultPresentation.TryParseFaultBody(detail, out var ambiguous)
                    && ProvisionFaultPresentation.IsModelDispatchAmbiguous(ambiguous.Code))
                {
                    // Terminal: abandoned world. Offer only a brand-new Provision Local — never poll/auto-resend.
                    ProvisionFaultPresentation.ShowAmbiguousEditorDialog(ambiguous, Provision);
                    return;
                }

                if (ProvisionFaultPresentation.IsModelDispatchAmbiguous(detail))
                {
                    ProvisionFaultPresentation.ShowAmbiguousEditorDialog(
                        new ProvisionFaultPresentation.ProvisionFault(
                            ProvisionFaultPresentation.ModelDispatchAmbiguousCode,
                            null,
                            null,
                            null,
                            detail),
                        Provision);
                    return;
                }

                EditorUtility.DisplayDialog("Luoxia Provision", $"Provision failed:\n{detail}", "OK");
                return;
            }

            if (!TryParseProvisionResponse(request.downloadHandler.text, out var parsed, out var error))
            {
                Debug.LogError($"[Luoxia] provision response invalid: {error}");
                if (ProvisionFaultPresentation.TryParseFaultBody(request.downloadHandler.text, out var ambiguousOkBody)
                    && ProvisionFaultPresentation.IsModelDispatchAmbiguous(ambiguousOkBody.Code))
                {
                    ProvisionFaultPresentation.ShowAmbiguousEditorDialog(ambiguousOkBody, Provision);
                    return;
                }

                EditorUtility.DisplayDialog("Luoxia Provision", $"Invalid provision response:\n{error}", "OK");
                return;
            }

            Assign(bootstrap, "mode", LuoxiaClientBootstrap.SessionSourceMode.EngineWithInitialView);
            Assign(bootstrap, "engineBaseUrl", engineBaseUrl);
            Assign(bootstrap, "sessionId", parsed.SessionId);
            Assign(bootstrap, "worldId", parsed.WorldId);
            Assign(bootstrap, "playerLocale", locale);
            Assign(bootstrap, "initialServerEnvelopesJson", parsed.ServerEnvelopesJson);
            Assign(bootstrap, "sendClientReadyOnStart", true);

            EditorUtility.SetDirty(bootstrap);
            EditorSceneManager.MarkSceneDirty(bootstrap.gameObject.scene);
            Debug.Log(
                $"[Luoxia] provisioned local play session={parsed.SessionId} world={parsed.WorldId} binding={parsed.ControlBindingId}");
            EditorUtility.DisplayDialog(
                "Luoxia Provision",
                $"Session ready.\nsession_id={parsed.SessionId}\nworld_id={parsed.WorldId}\nEnter Play Mode against {engineBaseUrl}.",
                "OK");
        }

        private static bool TryReadSettings(
            out int port,
            out string secret,
            out string engineBaseUrl,
            out string locale,
            out string playerName)
        {
            port = EditorPrefs.GetInt(PrefPort, 0);
            secret = EditorPrefs.GetString(PrefSecret, string.Empty);
            engineBaseUrl = EditorPrefs.GetString(PrefEngineBaseUrl, string.Empty);
            locale = EditorPrefs.GetString(PrefPlayerLocale, string.Empty);
            playerName = EditorPrefs.GetString(PrefPlayerName, string.Empty);
            return port is >= 1 and <= 65535
                && !string.IsNullOrWhiteSpace(secret)
                && !string.IsNullOrWhiteSpace(engineBaseUrl)
                && !string.IsNullOrWhiteSpace(locale)
                && !string.IsNullOrWhiteSpace(playerName);
        }

        /// <summary>
        /// Fills empty EditorPrefs from Deployment .env.local (loopback only).
        /// Does not invent secrets; requires the env file to exist.
        /// </summary>
        private static bool TryHydratePrefsFromDeploymentEnv()
        {
            if (!TryReadDeploymentEnv(out var port, out var secret))
            {
                return false;
            }

            if (EditorPrefs.GetInt(PrefPort, 0) is < 1 or > 65535)
            {
                EditorPrefs.SetInt(PrefPort, port);
            }

            if (string.IsNullOrWhiteSpace(EditorPrefs.GetString(PrefSecret, string.Empty)))
            {
                EditorPrefs.SetString(PrefSecret, secret);
            }

            if (string.IsNullOrWhiteSpace(EditorPrefs.GetString(PrefEngineBaseUrl, string.Empty)))
            {
                EditorPrefs.SetString(PrefEngineBaseUrl, DefaultEngineBaseUrl);
            }

            if (string.IsNullOrWhiteSpace(EditorPrefs.GetString(PrefPlayerLocale, string.Empty)))
            {
                EditorPrefs.SetString(PrefPlayerLocale, DefaultPlayerLocale);
            }

            if (string.IsNullOrWhiteSpace(EditorPrefs.GetString(PrefPlayerName, string.Empty)))
            {
                EditorPrefs.SetString(PrefPlayerName, DefaultPlayerName);
            }

            return true;
        }

        private static bool TryReadDeploymentEnv(out int port, out string secret)
        {
            port = 0;
            secret = null;
            for (var i = 0; i < DeploymentEnvCandidates.Length; i++)
            {
                var path = DeploymentEnvCandidates[i];
                if (!File.Exists(path))
                {
                    continue;
                }

                string portText = null;
                string secretText = null;
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
                }

                if (!string.IsNullOrWhiteSpace(portText)
                    && int.TryParse(portText, out var parsedPort)
                    && parsedPort is >= 1 and <= 65535
                    && !string.IsNullOrWhiteSpace(secretText))
                {
                    port = parsedPort;
                    secret = secretText.Trim();
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseProvisionResponse(
            string json,
            out ProvisionParsed parsed,
            out string error)
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

                parsed = new ProvisionParsed(
                    sessionId,
                    worldId,
                    bindingId,
                    envelopes.ToString(Newtonsoft.Json.Formatting.None));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void Assign(UnityEngine.Object target, string fieldName, object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning($"Missing field {target.GetType().Name}.{fieldName}");
                return;
            }

            switch (prop.propertyType)
            {
                case SerializedPropertyType.String:
                    prop.stringValue = value as string;
                    break;
                case SerializedPropertyType.Enum:
                    if (value is Enum)
                    {
                        prop.enumValueIndex = Convert.ToInt32(value);
                    }

                    break;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = value is bool flag && flag;
                    break;
                default:
                    Debug.LogWarning($"Unhandled property type {prop.propertyType} for {fieldName}");
                    break;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private readonly struct ProvisionParsed
        {
            public ProvisionParsed(
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

        [Serializable]
        private sealed class SimplePlayerNameBody
        {
            public string locale;
            public string text;
        }

        private sealed class ProvisionSettingsWindow : EditorWindow
        {
            private int _port;
            private string _secret = string.Empty;
            private string _engineBaseUrl = string.Empty;
            private string _locale = string.Empty;
            private string _playerName = string.Empty;

            public static void Open()
            {
                TryHydratePrefsFromDeploymentEnv();
                var window = GetWindow<ProvisionSettingsWindow>(true, "Luoxia Local Provision", true);
                window.minSize = new Vector2(420f, 260f);
                window._port = EditorPrefs.GetInt(PrefPort, 8010);
                window._secret = EditorPrefs.GetString(PrefSecret, string.Empty);
                window._engineBaseUrl = EditorPrefs.GetString(PrefEngineBaseUrl, DefaultEngineBaseUrl);
                window._locale = EditorPrefs.GetString(PrefPlayerLocale, DefaultPlayerLocale);
                window._playerName = EditorPrefs.GetString(PrefPlayerName, DefaultPlayerName);
                window.Show();
            }

            private void OnGUI()
            {
                EditorGUILayout.LabelField(
                    "Loopback provision。可从 Deployment .env.local 加载端口与密钥。",
                    EditorStyles.wordWrappedLabel);
                _port = EditorGUILayout.IntField("Provision Port", _port);
                _secret = EditorGUILayout.PasswordField("Shared Secret", _secret ?? string.Empty);
                _engineBaseUrl = EditorGUILayout.TextField("Engine Base URL", _engineBaseUrl ?? string.Empty);
                _locale = EditorGUILayout.TextField("Player Locale", _locale ?? string.Empty);
                _playerName = EditorGUILayout.TextField("Player Name", _playerName ?? string.Empty);

                if (GUILayout.Button("从 Deployment .env.local 加载"))
                {
                    if (!TryReadDeploymentEnv(out var port, out var secret))
                    {
                        EditorUtility.DisplayDialog(
                            "Luoxia Provision",
                            "未找到 C:\\Ai\\Luoxia-Deployment\\.env.local 或缺少 LUOXIA_PROVISION_PORT / SHARED_SECRET。",
                            "OK");
                    }
                    else
                    {
                        _port = port;
                        _secret = secret;
                        if (string.IsNullOrWhiteSpace(_engineBaseUrl))
                        {
                            _engineBaseUrl = DefaultEngineBaseUrl;
                        }

                        if (string.IsNullOrWhiteSpace(_locale))
                        {
                            _locale = DefaultPlayerLocale;
                        }

                        if (string.IsNullOrWhiteSpace(_playerName))
                        {
                            _playerName = DefaultPlayerName;
                        }
                    }
                }

                if (!GUILayout.Button("Save"))
                {
                    return;
                }

                if (_port is < 1 or > 65535
                    || string.IsNullOrWhiteSpace(_secret)
                    || string.IsNullOrWhiteSpace(_engineBaseUrl)
                    || string.IsNullOrWhiteSpace(_locale)
                    || string.IsNullOrWhiteSpace(_playerName))
                {
                    EditorUtility.DisplayDialog(
                        "Luoxia Provision",
                        "Port, secret, engine base URL, locale, and player name are all required.",
                        "OK");
                    return;
                }

                EditorPrefs.SetInt(PrefPort, _port);
                EditorPrefs.SetString(PrefSecret, _secret.Trim());
                EditorPrefs.SetString(PrefEngineBaseUrl, _engineBaseUrl.Trim());
                EditorPrefs.SetString(PrefPlayerLocale, _locale.Trim());
                EditorPrefs.SetString(PrefPlayerName, _playerName.Trim());
                Close();
            }
        }
    }
}
#endif
