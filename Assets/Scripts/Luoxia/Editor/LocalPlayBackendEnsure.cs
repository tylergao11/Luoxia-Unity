#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Luoxia.Editor
{
    /// <summary>
    /// Ensures loopback Engine + Deployment provision processes are reachable.
    /// Starts them via existing npm scripts when health fails; never invents mock sessions.
    /// Does not kill already-running servers.
    /// </summary>
    internal static class LocalPlayBackendEnsure
    {
        internal const string EngineRoot = @"C:\Ai\Luoxia-Engine";
        internal const string DeploymentRoot = @"C:\Ai\Luoxia-Deployment";
        private const string ArtifactRelativeDir = "Artifacts/local-backends";
        private const int HealthPollTimeoutSeconds = 90;
        private const int HealthPollIntervalMs = 1000;
        private const int BuildTimeoutMs = 180_000;

        internal readonly struct EnsureResult
        {
            public EnsureResult(bool ok, string error)
            {
                Ok = ok;
                Error = error;
            }

            public bool Ok { get; }
            public string Error { get; }

            public static EnsureResult Success() => new EnsureResult(true, null);

            public static EnsureResult Fail(string error) => new EnsureResult(false, error);
        }

        internal static async Task<EnsureResult> EnsureEngineAsync(string engineBaseUrl)
        {
            if (!TryParseLoopbackBase(engineBaseUrl, out var baseUri, out var parseError))
            {
                return EnsureResult.Fail(parseError);
            }

            var healthUrl = baseUri.AbsoluteUri.TrimEnd('/') + "/api/health";
            if (await IsEngineHealthyAsync(healthUrl).ConfigureAwait(true))
            {
                Debug.Log("[Luoxia] Engine already healthy at " + healthUrl);
                return EnsureResult.Success();
            }

            if (!Directory.Exists(EngineRoot))
            {
                return EnsureResult.Fail("Engine root missing: " + EngineRoot);
            }

            var mainJs = Path.Combine(EngineRoot, "apps", "server", "dist", "main.js");
            if (!File.Exists(mainJs))
            {
                EditorUtility.DisplayProgressBar("Luoxia Local Play", "Building Engine (dist missing)…", 0.15f);
                if (!TryRunNpmBuild(EngineRoot, "engine-build", out var buildError))
                {
                    EditorUtility.ClearProgressBar();
                    return EnsureResult.Fail("Engine build failed: " + buildError);
                }

                if (!File.Exists(mainJs))
                {
                    EditorUtility.ClearProgressBar();
                    return EnsureResult.Fail("Engine dist still missing after build: " + mainJs);
                }
            }

            var deploymentModule = Path.Combine(DeploymentRoot, "dist", "deployment.js");
            if (!File.Exists(deploymentModule))
            {
                EditorUtility.DisplayProgressBar("Luoxia Local Play", "Building Deployment (Engine module missing)…", 0.25f);
                if (!TryRunNpmBuild(DeploymentRoot, "deployment-build", out var depBuildError))
                {
                    EditorUtility.ClearProgressBar();
                    return EnsureResult.Fail("Deployment build failed: " + depBuildError);
                }

                if (!File.Exists(deploymentModule))
                {
                    EditorUtility.ClearProgressBar();
                    return EnsureResult.Fail("Deployment module still missing after build: " + deploymentModule);
                }
            }

            var contractsDir = Path.Combine(EngineRoot, "contracts");
            if (!Directory.Exists(contractsDir))
            {
                EditorUtility.ClearProgressBar();
                return EnsureResult.Fail("Engine contracts directory missing: " + contractsDir);
            }

            var host = baseUri.Host;
            var port = baseUri.Port;
            var startArgs =
                "start -- --contracts=\"" + contractsDir + "\""
                + " --host=" + host
                + " --port=" + port
                + " --mode=runtime"
                + " --deployment-module=\"" + deploymentModule + "\"";

            EditorUtility.DisplayProgressBar("Luoxia Local Play", "Starting Engine…", 0.35f);
            if (!TrySpawnNpmWindow(EngineRoot, startArgs, "Luoxia Engine", out var startError))
            {
                EditorUtility.ClearProgressBar();
                return EnsureResult.Fail("Failed to start Engine: " + startError);
            }

            var waited = await WaitUntilAsync(
                    "Engine /api/health",
                    () => IsEngineHealthyAsync(healthUrl),
                    0.35f,
                    0.65f)
                .ConfigureAwait(true);
            if (!waited)
            {
                EditorUtility.ClearProgressBar();
                return EnsureResult.Fail(
                    "Engine did not become healthy within "
                    + HealthPollTimeoutSeconds
                    + "s at "
                    + healthUrl
                    + ". Check the Engine console window or "
                    + ArtifactRelativeDir
                    + ".");
            }

            Debug.Log("[Luoxia] Engine healthy at " + healthUrl);
            return EnsureResult.Success();
        }

        internal static async Task<EnsureResult> EnsureProvisionAsync(int port)
        {
            if (port is < 1 or > 65535)
            {
                return EnsureResult.Fail("Invalid provision port: " + port);
            }

            var probeUrl = "http://127.0.0.1:" + port + "/";
            if (await IsProvisionListeningAsync(probeUrl).ConfigureAwait(true))
            {
                Debug.Log("[Luoxia] Provision already listening at http://127.0.0.1:" + port);
                return EnsureResult.Success();
            }

            if (!Directory.Exists(DeploymentRoot))
            {
                return EnsureResult.Fail("Deployment root missing: " + DeploymentRoot);
            }

            var provisionJs = Path.Combine(DeploymentRoot, "dist", "provision-server.js");
            if (!File.Exists(provisionJs))
            {
                EditorUtility.DisplayProgressBar("Luoxia Local Play", "Building Deployment (provision dist missing)…", 0.7f);
                if (!TryRunNpmBuild(DeploymentRoot, "deployment-build", out var buildError))
                {
                    EditorUtility.ClearProgressBar();
                    return EnsureResult.Fail("Deployment build failed: " + buildError);
                }

                if (!File.Exists(provisionJs))
                {
                    EditorUtility.ClearProgressBar();
                    return EnsureResult.Fail("Provision dist still missing after build: " + provisionJs);
                }
            }

            EditorUtility.DisplayProgressBar("Luoxia Local Play", "Starting Deployment provision…", 0.8f);
            if (!TrySpawnNpmWindow(DeploymentRoot, "run start:provision", "Luoxia Provision", out var startError))
            {
                EditorUtility.ClearProgressBar();
                return EnsureResult.Fail("Failed to start provision: " + startError);
            }

            var waited = await WaitUntilAsync(
                    "Provision listen",
                    () => IsProvisionListeningAsync(probeUrl),
                    0.8f,
                    0.95f)
                .ConfigureAwait(true);
            if (!waited)
            {
                EditorUtility.ClearProgressBar();
                return EnsureResult.Fail(
                    "Provision did not become reachable within "
                    + HealthPollTimeoutSeconds
                    + "s at http://127.0.0.1:"
                    + port
                    + ". Provision has no /health route; liveness is any HTTP response (typically GET / → 404 not_found). "
                    + "Check the Provision console window or "
                    + ArtifactRelativeDir
                    + ".");
            }

            Debug.Log("[Luoxia] Provision listening at http://127.0.0.1:" + port);
            return EnsureResult.Success();
        }

        private static async Task<bool> WaitUntilAsync(
            string label,
            Func<Task<bool>> probe,
            float progressStart,
            float progressEnd)
        {
            var started = DateTime.UtcNow;
            var deadline = started.AddSeconds(HealthPollTimeoutSeconds);
            var attempt = 0;
            while (DateTime.UtcNow < deadline)
            {
                attempt++;
                var elapsed = (float)(DateTime.UtcNow - started).TotalSeconds;
                var t = Math.Min(1f, elapsed / HealthPollTimeoutSeconds);
                var progress = progressStart + (progressEnd - progressStart) * t;
                EditorUtility.DisplayProgressBar(
                    "Luoxia Local Play",
                    "Waiting for " + label + " (" + attempt + ")…",
                    progress);

                if (await probe().ConfigureAwait(true))
                {
                    return true;
                }

                await Task.Delay(HealthPollIntervalMs).ConfigureAwait(true);
            }

            return false;
        }

        private static async Task<bool> IsEngineHealthyAsync(string healthUrl)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                using var response = await client.GetAsync(healthUrl).ConfigureAwait(true);
                return response.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Provision gateway has no dedicated health route. Any HTTP response (including
        /// GET / → 404 {"status":"not_found"}) means the process is listening.
        /// </summary>
        private static async Task<bool> IsProvisionListeningAsync(string probeUrl)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                using var response = await client.GetAsync(probeUrl).ConfigureAwait(true);
                return response != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryParseLoopbackBase(string engineBaseUrl, out Uri baseUri, out string error)
        {
            baseUri = null;
            error = null;
            if (string.IsNullOrWhiteSpace(engineBaseUrl)
                || !Uri.TryCreate(engineBaseUrl.Trim(), UriKind.Absolute, out var parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                error = "Engine Base URL is not a valid absolute http(s) URL: " + engineBaseUrl;
                return false;
            }

            if (!string.Equals(parsed.Host, "127.0.0.1", StringComparison.Ordinal)
                && !string.Equals(parsed.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                error = "Local Play only starts Engine on loopback (127.0.0.1 / localhost). Got: " + parsed.Host;
                return false;
            }

            baseUri = parsed;
            return true;
        }

        private static bool TryRunNpmBuild(string workingDirectory, string logStem, out string error)
        {
            error = null;
            try
            {
                var artifactDir = EnsureArtifactDir();
                var logPath = Path.Combine(artifactDir, logStem + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".log");
                var psi = new ProcessStartInfo
                {
                    FileName = "npm.cmd",
                    Arguments = "run build",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    error = "Process.Start returned null for npm run build in " + workingDirectory;
                    return false;
                }

                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(BuildTimeoutMs))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception)
                    {
                        // Best-effort kill of stuck build only.
                    }

                    error = "npm run build timed out after " + BuildTimeoutMs + "ms in " + workingDirectory;
                    File.WriteAllText(logPath, stdout + Environment.NewLine + stderr + Environment.NewLine + error);
                    return false;
                }

                File.WriteAllText(logPath, stdout + Environment.NewLine + stderr);
                if (process.ExitCode != 0)
                {
                    error = "npm run build exit=" + process.ExitCode + " (log: " + logPath + ")";
                    return false;
                }

                Debug.Log("[Luoxia] npm run build ok in " + workingDirectory + " → " + logPath);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TrySpawnNpmWindow(
            string workingDirectory,
            string npmArgsAfterNpm,
            string windowTitle,
            out string error)
        {
            error = null;
            try
            {
                var artifactDir = EnsureArtifactDir();
                var stampPath = Path.Combine(
                    artifactDir,
                    SanitizeFileStem(windowTitle) + "-spawn-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".txt");
                File.WriteAllText(
                    stampPath,
                    "cwd=" + workingDirectory + "\n"
                    + "npm " + npmArgsAfterNpm + "\n"
                    + "started_utc=" + DateTime.UtcNow.ToString("o") + "\n");

                // Visible console so operators can see Engine/Provision startup faults.
                // /k keeps the window open after exit for diagnosis; we never kill it.
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/k title " + windowTitle + " && npm.cmd " + npmArgsAfterNpm,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true,
                };

                var process = Process.Start(psi);
                if (process == null)
                {
                    error = "Process.Start returned null when spawning " + windowTitle;
                    return false;
                }

                Debug.Log("[Luoxia] spawned " + windowTitle + " (stamp " + stampPath + ")");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string SanitizeFileStem(string title)
        {
            var chars = title.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_')
                {
                    chars[i] = '-';
                }
            }

            return new string(chars);
        }

        private static string EnsureArtifactDir()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            var dir = Path.Combine(projectRoot, ArtifactRelativeDir);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
#endif
