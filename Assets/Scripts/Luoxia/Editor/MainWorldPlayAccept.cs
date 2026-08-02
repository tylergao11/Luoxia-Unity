#if UNITY_EDITOR
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Luoxia.App;
using Luoxia.Net;
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
    /// Provision HTTP + parse owned by runtime <see cref="ProvisionGatewayClient"/>.
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

            if (!ProvisionGatewayClient.TryLoadLocalSettings(out var settings, out var loadError))
            {
                Fail(loadError ?? "missing Luoxia-Deployment/.env.local", interactive, artifactDir);
                return;
            }

            var engineBase = settings.EngineBaseUrl;
            var locale = settings.PlayerLocale;
            var playerName = settings.PlayerName;

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

            Debug.Log(
                "[PlayAccept] provisioning via http://127.0.0.1:" + settings.Port + "/provision/new-play");
            var outcome = await ProvisionGatewayClient.ProvisionNewPlayAsync(settings, locale, playerName)
                .ConfigureAwait(true);
            if (!outcome.Ok)
            {
                if (outcome.IsAmbiguous
                    || ProvisionFaultPresentation.IsModelDispatchAmbiguous(outcome.Code)
                    || ProvisionFaultPresentation.IsModelDispatchAmbiguous(outcome.RawBody)
                    || ProvisionFaultPresentation.IsModelDispatchAmbiguous(outcome.Message))
                {
                    if (ProvisionFaultPresentation.TryParseFaultBody(
                            outcome.RawBody ?? outcome.Message,
                            out var fault))
                    {
                        FailAmbiguous(fault, interactive, artifactDir);
                    }
                    else
                    {
                        FailAmbiguous(
                            new ProvisionFaultPresentation.ProvisionFault(
                                ProvisionFaultPresentation.ModelDispatchAmbiguousCode,
                                outcome.Message,
                                null,
                                null,
                                outcome.RawBody ?? outcome.Message),
                            interactive,
                            artifactDir);
                    }

                    return;
                }

                Fail("provision failed: " + outcome.FormatDetail(), interactive, artifactDir);
                return;
            }

            var provision = outcome.Success;

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

            ProvisionLocalPlay.ApplyBootstrapFields(bootstrap, engineBase, locale, provision);

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
    }
}
#endif
