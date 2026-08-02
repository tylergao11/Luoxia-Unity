#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Luoxia.App;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Luoxia.Editor
{
    /// <summary>
    /// Editor launcher: Provision → inject LuoxiaClientBootstrap → marker → Play →
    /// wait for <see cref="PlayAcceptRuntimeDriver"/> report → Exit.
    /// </summary>
    public static class MainWorldPlayAccept
    {
        private const string MenuPath = "Luoxia/UI/Play Accept Main World (send+confirm+map)";
        private const string ScenePath = "Assets/Scenes/MainWorld.unity";
        private const string ArtifactRelativeDir = "Artifacts/play-accept";
        private const string ReportFileName = "report.txt";
        private const string ExitCodeFileName = "exit-code.txt";
        private const string SessionKeyWatching = "Luoxia.PlayAccept.Watching";
        private const string SessionKeyStartedUtc = "Luoxia.PlayAccept.StartedUtc";
        // Day1 budget drain (capacity+2 probes) + day2 dialogue needs headroom beyond 15m.
        private const double MaxWaitSeconds = 2400d;
        private const string DefaultEngineBaseUrl = "http://127.0.0.1:8000";
        private const string DefaultPlayerLocale = "zh-CN";
        private const string DefaultPlayerName = "试玩者";

        [InitializeOnLoadMethod]
        private static void ResumeAfterReload()
        {
            if (!SessionState.GetBool(SessionKeyWatching, false))
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                if (!SessionState.GetBool(SessionKeyWatching, false))
                {
                    return;
                }

                EditorApplication.update -= WatchForRuntimeReport;
                EditorApplication.update += WatchForRuntimeReport;
            };
        }

        [MenuItem(MenuPath)]
        public static void RunFromMenu()
        {
            _ = RunAsync(interactive: true);
        }

        public static void RunFromBatch()
        {
            _ = RunAsync(interactive: false);
        }

        private static async Task RunAsync(bool interactive)
        {
            SessionState.SetBool(SessionKeyWatching, false);
            EditorApplication.update -= WatchForRuntimeReport;

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var artifactDir = Path.Combine(projectRoot, ArtifactRelativeDir);
            Directory.CreateDirectory(artifactDir);
            ClearArtifactOutputs(artifactDir);

            var envPath = ResolveDeploymentEnvPath();
            if (string.IsNullOrWhiteSpace(envPath) || !File.Exists(envPath))
            {
                Fail("missing Luoxia-Deployment/.env.local", interactive, artifactDir);
                return;
            }

            Dictionary<string, string> env;
            try
            {
                env = ParseEnvFile(envPath);
            }
            catch (Exception ex)
            {
                Fail("failed to parse .env.local: " + ex.Message, interactive, artifactDir);
                return;
            }

            if (!env.TryGetValue("LUOXIA_PROVISION_PORT", out var portText) ||
                !int.TryParse(portText, out var provisionPort) ||
                provisionPort is < 1 or > 65535)
            {
                Fail("missing/invalid LUOXIA_PROVISION_PORT", interactive, artifactDir);
                return;
            }

            if (!env.TryGetValue("LUOXIA_PROVISION_SHARED_SECRET", out var secret) ||
                string.IsNullOrWhiteSpace(secret))
            {
                Fail("missing LUOXIA_PROVISION_SHARED_SECRET", interactive, artifactDir);
                return;
            }

            var engineBase = env.TryGetValue("LUOXIA_ENGINE_BASE_URL", out var ebu) && !string.IsNullOrWhiteSpace(ebu)
                ? ebu.TrimEnd('/')
                : DefaultEngineBaseUrl;
            var playerName = env.TryGetValue("LUOXIA_PLAYER_DISPLAY_NAME", out var pn) && !string.IsNullOrWhiteSpace(pn)
                ? pn.Trim()
                : DefaultPlayerName;
            var locale = env.TryGetValue("LUOXIA_PLAYER_LOCALE", out var loc) && !string.IsNullOrWhiteSpace(loc)
                ? loc.Trim()
                : DefaultPlayerLocale;

            if (!File.Exists(ScenePath))
            {
                Fail("missing scene: " + ScenePath, interactive, artifactDir);
                return;
            }

            try
            {
                using var health = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var healthResponse = await health.GetAsync(engineBase + "/api/health").ConfigureAwait(true);
                if (!healthResponse.IsSuccessStatusCode)
                {
                    Fail("engine health failed: HTTP " + (int)healthResponse.StatusCode, interactive, artifactDir);
                    return;
                }
            }
            catch (Exception ex)
            {
                Fail("engine unreachable at " + engineBase + ": " + ex.Message, interactive, artifactDir);
                return;
            }

            Debug.Log("[PlayAccept] provisioning via http://127.0.0.1:" + provisionPort + "/provision/new-play");
            ProvisionResult provision;
            try
            {
                provision = await ProvisionNewPlayAsync(provisionPort, secret, locale, playerName)
                    .ConfigureAwait(true);
            }
            catch (ProvisionAmbiguousException ambiguous)
            {
                FailAmbiguous(ambiguous.Fault, interactive, artifactDir);
                return;
            }
            catch (Exception ex)
            {
                if (ProvisionFaultPresentation.TryParseFaultBody(ex.Message, out var fault)
                    && ProvisionFaultPresentation.IsModelDispatchAmbiguous(fault.Code))
                {
                    FailAmbiguous(fault, interactive, artifactDir);
                    return;
                }

                Fail("provision failed: " + ex.Message, interactive, artifactDir);
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Fail("user cancelled saving open scenes", interactive, artifactDir);
                return;
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (SceneManager.GetActiveScene().path != ScenePath)
            {
                Fail("failed to open " + ScenePath, interactive, artifactDir);
                return;
            }

            var bootstrap = UnityEngine.Object.FindObjectOfType<LuoxiaClientBootstrap>();
            if (bootstrap == null)
            {
                Fail("LuoxiaClientBootstrap missing in " + ScenePath, interactive, artifactDir);
                return;
            }

            Assign(bootstrap, "mode", LuoxiaClientBootstrap.SessionSourceMode.EngineWithInitialView);
            Assign(bootstrap, "engineBaseUrl", engineBase);
            Assign(bootstrap, "sessionId", provision.SessionId);
            Assign(bootstrap, "worldId", provision.WorldId);
            Assign(bootstrap, "playerLocale", locale);
            Assign(bootstrap, "initialServerEnvelopesJson", provision.ServerEnvelopesJson);
            Assign(bootstrap, "sendClientReadyOnStart", true);
            EditorUtility.SetDirty(bootstrap);

            var markerPath = Path.Combine(projectRoot, PlayAcceptRuntimeDriver.MarkerFileName);
            File.WriteAllText(
                markerPath,
                "session_id=" + provision.SessionId + "\n" +
                "world_id=" + provision.WorldId + "\n" +
                "engine_base_url=" + engineBase + "\n" +
                "started_utc=" + DateTime.UtcNow.ToString("o") + "\n",
                new UTF8Encoding(false));

            SessionState.SetBool(SessionKeyWatching, true);
            SessionState.SetString(SessionKeyStartedUtc, DateTime.UtcNow.ToString("o"));
            EditorApplication.update -= WatchForRuntimeReport;
            EditorApplication.update += WatchForRuntimeReport;

            Debug.Log(
                "[PlayAccept] injected session=" + provision.SessionId +
                " world=" + provision.WorldId + "; entering Play Mode");
            EditorApplication.isPlaying = true;
        }

        private static void WatchForRuntimeReport()
        {
            if (!SessionState.GetBool(SessionKeyWatching, false))
            {
                EditorApplication.update -= WatchForRuntimeReport;
                return;
            }

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var artifactDir = Path.Combine(projectRoot, ArtifactRelativeDir);
            var reportPath = Path.Combine(artifactDir, ReportFileName);
            var exitPath = Path.Combine(artifactDir, ExitCodeFileName);

            if (DateTime.TryParse(
                    SessionState.GetString(SessionKeyStartedUtc, string.Empty),
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var started) &&
                (DateTime.UtcNow - started).TotalSeconds > MaxWaitSeconds)
            {
                SessionState.SetBool(SessionKeyWatching, false);
                EditorApplication.update -= WatchForRuntimeReport;
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.isPlaying = false;
                }

                Fail("timed out waiting for runtime PlayAccept report", interactive: false, artifactDir);
                return;
            }

            if (!File.Exists(reportPath) || !File.Exists(exitPath))
            {
                return;
            }

            SessionState.SetBool(SessionKeyWatching, false);
            EditorApplication.update -= WatchForRuntimeReport;

            var exitText = File.ReadAllText(exitPath).Trim();
            var exitCode = 1;
            if (int.TryParse(exitText, out var parsed))
            {
                exitCode = parsed;
            }

            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }

            Debug.Log("[PlayAccept] runtime report ready; exit=" + exitCode + "\n" + File.ReadAllText(reportPath));
            EditorApplication.Exit(exitCode);
        }

        private static void ClearArtifactOutputs(string artifactDir)
        {
            foreach (var name in new[] { ReportFileName, ExitCodeFileName, "stamps.txt", "png-index.txt" })
            {
                var path = Path.Combine(artifactDir, name);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            foreach (var png in Directory.GetFiles(artifactDir, "*.png"))
            {
                File.Delete(png);
            }

            foreach (var stamp in Directory.GetFiles(artifactDir, "*.stamp.txt"))
            {
                File.Delete(stamp);
            }
        }

        private static async Task<ProvisionResult> ProvisionNewPlayAsync(
            int port,
            string secret,
            string locale,
            string playerName)
        {
            var url = "http://127.0.0.1:" + port + "/provision/new-play";
            var body = "{\"locale\":" + JsonString(locale) + ",\"text\":" + JsonString(playerName) + "}";
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(600) };
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation("x-luoxia-provision-secret", secret);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request).ConfigureAwait(true);
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
            if (!response.IsSuccessStatusCode)
            {
                if (ProvisionFaultPresentation.TryParseFaultBody(text, out var fault)
                    && ProvisionFaultPresentation.IsModelDispatchAmbiguous(fault.Code))
                {
                    throw new ProvisionAmbiguousException(fault);
                }

                if (ProvisionFaultPresentation.IsModelDispatchAmbiguous(text))
                {
                    throw new ProvisionAmbiguousException(
                        new ProvisionFaultPresentation.ProvisionFault(
                            ProvisionFaultPresentation.ModelDispatchAmbiguousCode,
                            null,
                            null,
                            null,
                            text));
                }

                throw new InvalidOperationException("HTTP " + (int)response.StatusCode + " " + text);
            }

            var root = JObject.Parse(text);
            var sessionId = root.Value<string>("session_id");
            var worldId = root.Value<string>("world_id");
            var envelopes = root["server_envelopes"] as JArray;
            if (string.IsNullOrWhiteSpace(sessionId) ||
                string.IsNullOrWhiteSpace(worldId) ||
                envelopes == null ||
                envelopes.Count < 1)
            {
                if (ProvisionFaultPresentation.TryParseFaultBody(text, out var fault)
                    && ProvisionFaultPresentation.IsModelDispatchAmbiguous(fault.Code))
                {
                    throw new ProvisionAmbiguousException(fault);
                }

                throw new InvalidOperationException("provision response missing session/world/envelopes");
            }

            return new ProvisionResult(
                sessionId,
                worldId,
                envelopes.ToString(Newtonsoft.Json.Formatting.None));
        }

        private static string JsonString(string value) =>
            "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static void Assign(UnityEngine.Object target, string fieldName, object value)
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
                case SerializedPropertyType.Boolean:
                    prop.boolValue = value is bool b && b;
                    break;
                case SerializedPropertyType.Enum:
                    prop.enumValueIndex = value is Enum e
                        ? Convert.ToInt32(e)
                        : Convert.ToInt32(value);
                    break;
                default:
                    Debug.LogWarning("Unsupported assign type for " + fieldName + ": " + prop.propertyType);
                    return;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string ResolveDeploymentEnvPath()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var candidates = new[]
            {
                Path.GetFullPath(Path.Combine(projectRoot, "..", "Luoxia-Deployment", ".env.local")),
                @"C:\Ai\Luoxia-Deployment\.env.local",
            };
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static Dictionary<string, string> ParseEnvFile(string path)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var idx = line.IndexOf('=');
                if (idx <= 0)
                {
                    continue;
                }

                var key = line.Substring(0, idx).Trim();
                var value = line.Substring(idx + 1).Trim();
                if ((value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal)) ||
                    (value.StartsWith("'", StringComparison.Ordinal) && value.EndsWith("'", StringComparison.Ordinal)))
                {
                    value = value.Substring(1, value.Length - 2);
                }

                map[key] = value;
            }

            return map;
        }

        private static void Fail(string reason, bool interactive, string artifactDir)
        {
            SessionState.SetBool(SessionKeyWatching, false);
            EditorApplication.update -= WatchForRuntimeReport;
            WriteFailure(artifactDir, "Play Accept FAILED\n" + reason + "\n");
            Debug.LogError("[PlayAccept] FAIL: " + reason);
            if (!interactive)
            {
                EditorApplication.Exit(1);
            }
        }

        private static void FailAmbiguous(
            ProvisionFaultPresentation.ProvisionFault fault,
            bool interactive,
            string artifactDir)
        {
            SessionState.SetBool(SessionKeyWatching, false);
            EditorApplication.update -= WatchForRuntimeReport;
            var report = ProvisionFaultPresentation.FormatPlayAcceptReport(fault);
            WriteFailure(artifactDir, report);
            Debug.LogError(
                "[PlayAccept] FAIL terminal model_dispatch_ambiguous — "
                + ProvisionFaultPresentation.PlayerCopy + "\n"
                + ProvisionFaultPresentation.FormatDetailLines(fault));
            if (interactive)
            {
                EditorUtility.DisplayDialog(
                    "开局未完成",
                    ProvisionFaultPresentation.PlayerCopy
                    + "\n\n"
                    + ProvisionFaultPresentation.FormatDetailLines(fault)
                    + "\n\n报告已写入 Artifacts/play-accept/report.txt。"
                    + "\n重试 = 重新跑 Play Accept（全新 provision），不可恢复本局。",
                    "OK");
            }
            else
            {
                EditorApplication.Exit(1);
            }
        }

        private static void WriteFailure(string artifactDir, string reportBody)
        {
            Directory.CreateDirectory(artifactDir);
            File.WriteAllText(
                Path.Combine(artifactDir, ReportFileName),
                reportBody,
                new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(artifactDir, ExitCodeFileName), "1\n", new UTF8Encoding(false));
        }

        private sealed class ProvisionAmbiguousException : Exception
        {
            public ProvisionAmbiguousException(ProvisionFaultPresentation.ProvisionFault fault)
                : base(ProvisionFaultPresentation.PlayerCopy)
            {
                Fault = fault;
            }

            public ProvisionFaultPresentation.ProvisionFault Fault { get; }
        }

        private readonly struct ProvisionResult
        {
            public ProvisionResult(string sessionId, string worldId, string serverEnvelopesJson)
            {
                SessionId = sessionId;
                WorldId = worldId;
                ServerEnvelopesJson = serverEnvelopesJson;
            }

            public string SessionId { get; }
            public string WorldId { get; }
            public string ServerEnvelopesJson { get; }
        }
    }
}
#endif
