#if UNITY_EDITOR
using System;
using System.Threading.Tasks;
using Luoxia.App;
using Luoxia.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Luoxia.Editor
{
    /// <summary>
    /// Loopback local-play bootstrap: calls the Deployment provision server,
    /// writes LuoxiaClientBootstrap fields, and switches to EngineWithInitialView.
    /// Does not open worlds through Engine client-envelope. Pack selection is
    /// Deployment-owned — this Host never names or branches on pack_id.
    /// HTTP + parse owned by runtime <see cref="ProvisionGatewayClient"/>.
    /// </summary>
    internal static class ProvisionLocalPlay
    {
        private const string PrefPort = "Luoxia.Provision.Port";
        private const string PrefSecret = "Luoxia.Provision.Secret";
        private const string PrefEngineBaseUrl = "Luoxia.Engine.BaseUrl";
        private const string PrefPlayerName = "Luoxia.Provision.PlayerName";
        private const string PrefPlayerLocale = "Luoxia.Provision.PlayerLocale";

        [MenuItem("Luoxia/Play/Configure Local Provision")]
        public static void Configure()
        {
            ProvisionSettingsWindow.Open();
        }

        [MenuItem("Luoxia/Play/Provision Local")]
        public static void Provision()
        {
            if (!TryPrepareLocalSettings(
                    out var port,
                    out var secret,
                    out var engineBaseUrl,
                    out var locale,
                    out var playerName))
            {
                return;
            }

            if (!TryFindBootstrap(out var bootstrap))
            {
                return;
            }

            _ = RunProvisionAsync(
                bootstrap,
                port,
                secret,
                engineBaseUrl,
                locale,
                playerName,
                enterPlayMode: false);
        }

        /// <summary>
        /// One-click DX: ensure Engine + provision → Provision Local assigns → Save → Play.
        /// Keeps Provision Local for provision-only; does not mock or invent sessions.
        /// </summary>
        [MenuItem("Luoxia/Play/Provision Local And Play")]
        public static void ProvisionAndPlay()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "Luoxia Provision",
                    "Already in Play Mode. Exit Play Mode before running Provision Local And Play.",
                    "OK");
                return;
            }

            if (!TryPrepareLocalSettings(
                    out var port,
                    out var secret,
                    out var engineBaseUrl,
                    out var locale,
                    out var playerName))
            {
                return;
            }

            if (!TryFindBootstrap(out var bootstrap))
            {
                return;
            }

            _ = RunProvisionAsync(
                bootstrap,
                port,
                secret,
                engineBaseUrl,
                locale,
                playerName,
                enterPlayMode: true);
        }

        private static async Task RunProvisionAsync(
            LuoxiaClientBootstrap bootstrap,
            int port,
            string secret,
            string engineBaseUrl,
            string locale,
            string playerName,
            bool enterPlayMode)
        {
            try
            {
                if (enterPlayMode)
                {
                    EditorUtility.DisplayProgressBar("Luoxia Local Play", "Ensuring Engine…", 0.1f);
                    var engineEnsure = await LocalPlayBackendEnsure.EnsureEngineAsync(engineBaseUrl)
                        .ConfigureAwait(true);
                    if (!engineEnsure.Ok)
                    {
                        EditorUtility.ClearProgressBar();
                        Debug.LogError("[Luoxia] Engine ensure failed: " + engineEnsure.Error);
                        EditorUtility.DisplayDialog(
                            "Luoxia Local Play",
                            "Engine 未能就绪：\n" + engineEnsure.Error,
                            "OK");
                        return;
                    }

                    EditorUtility.DisplayProgressBar("Luoxia Local Play", "Ensuring Provision…", 0.7f);
                    var provisionEnsure = await LocalPlayBackendEnsure.EnsureProvisionAsync(port)
                        .ConfigureAwait(true);
                    if (!provisionEnsure.Ok)
                    {
                        EditorUtility.ClearProgressBar();
                        Debug.LogError("[Luoxia] Provision ensure failed: " + provisionEnsure.Error);
                        EditorUtility.DisplayDialog(
                            "Luoxia Local Play",
                            "Provision 未能就绪：\n" + provisionEnsure.Error,
                            "OK");
                        return;
                    }

                    EditorUtility.DisplayProgressBar("Luoxia Local Play", "Calling /provision/new-play…", 0.92f);
                }

                var settings = new ProvisionLocalSettings(
                    port,
                    secret,
                    engineBaseUrl,
                    locale,
                    playerName,
                    envPath: null);

                var outcome = await ProvisionGatewayClient.ProvisionNewPlayAsync(settings, locale, playerName)
                    .ConfigureAwait(true);

                EditorUtility.ClearProgressBar();

                if (!outcome.Ok)
                {
                    PresentProvisionFailure(
                        outcome,
                        enterPlayMode ? (Action)ProvisionAndPlay : Provision);
                    return;
                }

                var parsed = outcome.Success;
                ApplyBootstrapFields(bootstrap, engineBaseUrl, locale, parsed);
                EditorSceneManager.MarkSceneDirty(bootstrap.gameObject.scene);
                Debug.Log(
                    "[Luoxia] provisioned local play session="
                    + parsed.SessionId
                    + " world="
                    + parsed.WorldId
                    + " binding="
                    + parsed.ControlBindingId);

                if (!enterPlayMode)
                {
                    EditorUtility.DisplayDialog(
                        "Luoxia Provision",
                        "Session ready.\nsession_id="
                        + parsed.SessionId
                        + "\nworld_id="
                        + parsed.WorldId
                        + "\nEnter Play Mode against "
                        + engineBaseUrl
                        + ".",
                        "OK");
                    return;
                }

                var scene = bootstrap.gameObject.scene;
                if (!scene.IsValid() || !EditorSceneManager.SaveScene(scene))
                {
                    EditorUtility.DisplayDialog(
                        "Luoxia Local Play",
                        "Provision succeeded but saving the open scene failed. Enter Play Mode manually after Save.",
                        "OK");
                    return;
                }

                Debug.Log(
                    "[Luoxia] scene saved; entering Play Mode against "
                    + engineBaseUrl
                    + " session="
                    + parsed.SessionId);
                EditorApplication.isPlaying = true;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static bool TryPrepareLocalSettings(
            out int port,
            out string secret,
            out string engineBaseUrl,
            out string locale,
            out string playerName)
        {
            TryHydratePrefsFromDeploymentEnv();
            if (!TryReadSettings(out port, out secret, out engineBaseUrl, out locale, out playerName))
            {
                EditorUtility.DisplayDialog(
                    "Luoxia Provision",
                    "缺少本地 Provision 配置。请先打开 Luoxia/Play/Configure Local Provision，点「从 Deployment .env.local 加载」后 Save。",
                    "OK");
                ProvisionSettingsWindow.Open();
                return false;
            }

            return true;
        }

        private static bool TryFindBootstrap(out LuoxiaClientBootstrap bootstrap)
        {
            bootstrap = UnityEngine.Object.FindObjectOfType<LuoxiaClientBootstrap>();
            if (bootstrap != null)
            {
                return true;
            }

            EditorUtility.DisplayDialog(
                "Luoxia Provision",
                "No LuoxiaClientBootstrap in the open scene.",
                "OK");
            return false;
        }

        internal static void ApplyBootstrapFields(
            LuoxiaClientBootstrap bootstrap,
            string engineBaseUrl,
            string locale,
            ProvisionNewPlaySuccess parsed)
        {
            Assign(bootstrap, "mode", LuoxiaClientBootstrap.SessionSourceMode.EngineWithInitialView);
            Assign(bootstrap, "engineBaseUrl", engineBaseUrl);
            Assign(bootstrap, "sessionId", parsed.SessionId);
            Assign(bootstrap, "worldId", parsed.WorldId);
            Assign(bootstrap, "playerLocale", locale);
            Assign(bootstrap, "initialServerEnvelopesJson", parsed.ServerEnvelopesJson);
            Assign(bootstrap, "sendClientReadyOnStart", true);
            EditorUtility.SetDirty(bootstrap);
        }

        internal static void PresentProvisionFailure(ProvisionNewPlayOutcome outcome, Action retry)
        {
            Debug.LogError("[Luoxia] provision failed: " + outcome.FormatDetail());
            if (outcome.IsAmbiguous
                || ProvisionFaultPresentation.IsModelDispatchAmbiguous(outcome.Code)
                || ProvisionFaultPresentation.IsModelDispatchAmbiguous(outcome.RawBody)
                || ProvisionFaultPresentation.IsModelDispatchAmbiguous(outcome.Message))
            {
                if (ProvisionFaultPresentation.TryParseFaultBody(outcome.RawBody ?? outcome.Message, out var ambiguous))
                {
                    ProvisionFaultPresentation.ShowAmbiguousEditorDialog(ambiguous, retry);
                }
                else
                {
                    ProvisionFaultPresentation.ShowAmbiguousEditorDialog(
                        new ProvisionFaultPresentation.ProvisionFault(
                            ProvisionFaultPresentation.ModelDispatchAmbiguousCode,
                            outcome.Message,
                            null,
                            null,
                            outcome.RawBody ?? outcome.Message),
                        retry);
                }

                return;
            }

            EditorUtility.DisplayDialog("Luoxia Provision", "Provision failed:\n" + outcome.FormatDetail(), "OK");
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
            if (!ProvisionGatewayClient.TryReadDeploymentEnv(
                    out var port,
                    out var secret,
                    out var engineBaseUrl,
                    out var locale,
                    out var playerName,
                    out _))
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
                EditorPrefs.SetString(
                    PrefEngineBaseUrl,
                    string.IsNullOrWhiteSpace(engineBaseUrl)
                        ? ProvisionGatewayClient.DefaultEngineBaseUrl
                        : engineBaseUrl);
            }

            if (string.IsNullOrWhiteSpace(EditorPrefs.GetString(PrefPlayerLocale, string.Empty)))
            {
                EditorPrefs.SetString(
                    PrefPlayerLocale,
                    string.IsNullOrWhiteSpace(locale)
                        ? ProvisionGatewayClient.DefaultPlayerLocale
                        : locale);
            }

            if (string.IsNullOrWhiteSpace(EditorPrefs.GetString(PrefPlayerName, string.Empty)))
            {
                EditorPrefs.SetString(
                    PrefPlayerName,
                    string.IsNullOrWhiteSpace(playerName)
                        ? ProvisionGatewayClient.DefaultPlayerName
                        : playerName);
            }

            return true;
        }

        internal static void Assign(UnityEngine.Object target, string fieldName, object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning("Missing field " + target.GetType().Name + "." + fieldName);
                return;
            }

            switch (prop.propertyType)
            {
                case SerializedPropertyType.String:
                    prop.stringValue = value as string ?? string.Empty;
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
                    Debug.LogWarning("Unhandled property type " + prop.propertyType + " for " + fieldName);
                    break;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
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
                window._engineBaseUrl = EditorPrefs.GetString(
                    PrefEngineBaseUrl,
                    ProvisionGatewayClient.DefaultEngineBaseUrl);
                window._locale = EditorPrefs.GetString(
                    PrefPlayerLocale,
                    ProvisionGatewayClient.DefaultPlayerLocale);
                window._playerName = EditorPrefs.GetString(
                    PrefPlayerName,
                    ProvisionGatewayClient.DefaultPlayerName);
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
                    if (!ProvisionGatewayClient.TryReadDeploymentEnv(
                            out var port,
                            out var secret,
                            out var engineBaseUrl,
                            out var locale,
                            out var playerName,
                            out _))
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
                            _engineBaseUrl = string.IsNullOrWhiteSpace(engineBaseUrl)
                                ? ProvisionGatewayClient.DefaultEngineBaseUrl
                                : engineBaseUrl;
                        }

                        if (string.IsNullOrWhiteSpace(_locale))
                        {
                            _locale = string.IsNullOrWhiteSpace(locale)
                                ? ProvisionGatewayClient.DefaultPlayerLocale
                                : locale;
                        }

                        if (string.IsNullOrWhiteSpace(_playerName))
                        {
                            _playerName = string.IsNullOrWhiteSpace(playerName)
                                ? ProvisionGatewayClient.DefaultPlayerName
                                : playerName;
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
