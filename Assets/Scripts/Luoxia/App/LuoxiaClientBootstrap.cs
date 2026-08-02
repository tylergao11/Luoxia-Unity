using System.Collections;
using System.Threading.Tasks;
using Luoxia.Assets;
using Luoxia.Contracts;
using Luoxia.Net;
using Luoxia.Session;
using Luoxia.UI.Core;
using Luoxia.UI.Screens;
using UnityEngine;

namespace Luoxia.App
{
    /// <summary>
    /// Composition root. Only Engine-backed sessions are allowed.
    /// Use menu Luoxia/Play/Provision Local before Enter Play, or fatal overlay「重新开局」in Play.
    /// </summary>
    public sealed class LuoxiaClientBootstrap : MonoBehaviour
    {
        public enum SessionSourceMode
        {
            /// <summary>Seed optional initial SessionView, then client.ready against Engine.</summary>
            EngineWithInitialView = 0,

            /// <summary>Attach session id only; fetch authoritative view via client.ready.</summary>
            EngineReadyOnly = 1
        }

        [Header("UI")]
        [SerializeField] private MainWorldScreen mainWorldScreen;

        [Header("Session source (Engine only)")]
        [SerializeField] private SessionSourceMode mode = SessionSourceMode.EngineWithInitialView;
        [SerializeField] private string engineBaseUrl = "http://127.0.0.1:8000";
        [SerializeField] private string sessionId;
        [SerializeField] private string worldId;
        [Tooltip("Host display locale — same value sent as dialogue.start locale / provision player_name locale.")]
        [SerializeField] private string playerLocale = "zh-CN";
        [Tooltip("Optional full ServerEnvelope JSON array or single envelope; used to seed SessionView.")]
        [SerializeField] [TextArea(4, 12)] private string initialServerEnvelopesJson;
        [SerializeField] private bool sendClientReadyOnStart = true;

        private SessionReplica _replica;
        private CommandGate _gate;
        private DialogueSelection _selection;
        private BridgeSessionClient _bridge;
        private ClientEnvelopeFactory _factory;
        private PresentationRouter _presentation;
        private PlayerIntentRouter _intents;
        private StreamingAssetsHashSpriteResolver _sprites;
        private HttpBridgeTransport _transport;
        /// <summary>fatal recoverability / abandoned world — overlay retry runs in-Play reprovision.</summary>
        private bool _terminalToProvisionOnly;
        private bool _reconnectInFlight;
        private bool _reprovisionInFlight;

        private const string ModelDispatchAmbiguousCode = ProvisionGatewayClient.ModelDispatchAmbiguousCode;
        private const string AmbiguousPlayerCopy =
            "开局未完成：世界导演未能就位，本次开局已作废。你可以重新开始一局。";
        private const string TerminalProvisionCopy =
            "会话已终止。点击「重新开局」将向 provision gateway 申请全新一局（无需退出 Play）。";

        private void Awake()
        {
            ApplyHostLocale();

            _replica = new SessionReplica();
            _gate = new CommandGate();
            _selection = new DialogueSelection();
            _factory = new ClientEnvelopeFactory();
            _presentation = new PresentationRouter();
            _sprites = new StreamingAssetsHashSpriteResolver();
            _sprites.EnsureIndexLoaded();
            ContentHashSpriteResolverLocator.SetShared(_sprites);

            if (mainWorldScreen == null)
            {
                mainWorldScreen = FindObjectOfType<MainWorldScreen>();
            }

            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(engineBaseUrl))
            {
                WireUiWithoutBridge();
                const string msg =
                    "禁止离线开局。请先启动 Engine + Provision，再执行菜单 Luoxia/Play/Provision Local，然后 Play；"
                    + "或在 Fatal overlay 点击「重新开局」。";
                Debug.LogError("[Bootstrap] " + msg);
                _terminalToProvisionOnly = true;
                mainWorldScreen?.ShowFatal("未 Provision", msg, terminalToProvision: true);
                return;
            }

            _transport = new HttpBridgeTransport(engineBaseUrl);
            _bridge = new BridgeSessionClient(_transport, _replica, _gate, _factory, _presentation);
            WireBridgeEvents();
            _intents = new PlayerIntentRouter(_replica, _gate, _selection, _bridge, _factory, this, worldId);
            WireUi();
            StartCoroutine(ConnectEngine());
        }

        private void OnDestroy()
        {
            UnwireBridgeEvents();
            _bridge?.DetachPresentation();
        }

        private void ApplyHostLocale()
        {
            var locale = string.IsNullOrWhiteSpace(playerLocale) ? null : playerLocale.Trim();
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(locale))
            {
                locale = UnityEditor.EditorPrefs.GetString("Luoxia.Provision.PlayerLocale", string.Empty);
            }
#endif
            if (string.IsNullOrWhiteSpace(locale))
            {
                Debug.LogError("[Bootstrap] playerLocale required (Bootstrap field or EditorPrefs Luoxia.Provision.PlayerLocale)");
                locale = ProvisionGatewayClient.DefaultPlayerLocale;
            }

            playerLocale = locale;
            HostDisplayLocale.SetPreferred(locale);
        }

        private void WireBridgeEvents()
        {
            if (_bridge == null)
            {
                return;
            }

            _bridge.UserVisibleError += HandleBridgeError;
            _bridge.ProtocolFatal += HandleProtocolFatal;
            _bridge.ReconnectRequested += HandleReconnectRequested;
        }

        private void UnwireBridgeEvents()
        {
            if (_bridge == null)
            {
                return;
            }

            _bridge.UserVisibleError -= HandleBridgeError;
            _bridge.ProtocolFatal -= HandleProtocolFatal;
            _bridge.ReconnectRequested -= HandleReconnectRequested;
        }

        private void WireUiWithoutBridge()
        {
            if (mainWorldScreen == null)
            {
                return;
            }

            _intents = new PlayerIntentRouter(_replica, _gate, _selection, null, _factory, this, worldId);
            mainWorldScreen.Configure(
                _replica,
                _intents,
                _selection,
                _presentation,
                _gate,
                _sprites,
                _replica,
                HandleFatalRetry);
        }

        private void WireUi()
        {
            if (mainWorldScreen == null)
            {
                Debug.LogError("[Bootstrap] MainWorldScreen missing");
                return;
            }

            mainWorldScreen.Configure(
                _replica,
                _intents,
                _selection,
                _bridge.Presentation,
                _gate,
                _sprites,
                _replica,
                HandleFatalRetry);
        }

        private void HandleBridgeError(string message)
        {
            // Provision-adjacent ambiguous may still surface as transport text before a Session exists.
            if (TryShowAmbiguousTerminal(message))
            {
                return;
            }

            mainWorldScreen?.ShowUserError(message);
        }

        private void HandleProtocolFatal(string message)
        {
            // recoverability=fatal only — do not branch on error.code strings.
            _terminalToProvisionOnly = true;
            _replica?.MarkFatal(message);
            mainWorldScreen?.ShowFatal("协议错误", message + "\n\n" + TerminalProvisionCopy, terminalToProvision: true);
        }

        private void HandleReconnectRequested()
        {
            if (_reconnectInFlight || _reprovisionInFlight)
            {
                return;
            }

            StartCoroutine(ReconnectThenResync());
        }

        private void HandleFatalRetry()
        {
            if (_terminalToProvisionOnly)
            {
                if (_reprovisionInFlight)
                {
                    return;
                }

                StartCoroutine(ReprovisionInPlay());
                return;
            }

            if (_bridge == null)
            {
                return;
            }

            StartCoroutine(RetryReadyOrResync());
        }

        private bool TryShowAmbiguousTerminal(string message)
        {
            // Provision gateway contract (not protocol.recoverability): abandoned world.
            if (string.IsNullOrEmpty(message)
                || message.IndexOf(ModelDispatchAmbiguousCode, System.StringComparison.Ordinal) < 0)
            {
                return false;
            }

            _terminalToProvisionOnly = true;
            _replica?.MarkFatal(AmbiguousPlayerCopy);
            var detail = AmbiguousPlayerCopy
                + "\n\ncode=" + ModelDispatchAmbiguousCode
                + "\n" + message
                + "\n\n点击「重新开局」向 provision gateway 申请全新一局。禁止对本局自动重试模型。";
            mainWorldScreen?.ShowFatal("开局未完成", detail, terminalToProvision: true);
            return true;
        }

        private IEnumerator ReprovisionInPlay()
        {
            _reprovisionInFlight = true;
            try
            {
                mainWorldScreen?.ShowFatal(
                    "重新开局",
                    "正在向 provision gateway 申请全新一局…",
                    terminalToProvision: true);

                if (!ProvisionGatewayClient.TryLoadLocalSettings(out var settings, out var loadError))
                {
                    mainWorldScreen?.ShowFatal(
                        "重新开局失败",
                        loadError + "\n\n" + TerminalProvisionCopy,
                        terminalToProvision: true);
                    yield break;
                }

                var locale = !string.IsNullOrWhiteSpace(playerLocale)
                    ? playerLocale.Trim()
                    : settings.PlayerLocale;
                var playerName = ResolvePlayerNameForProvision(settings);

                var task = ProvisionGatewayClient.ProvisionNewPlayAsync(settings, locale, playerName);
                while (!task.IsCompleted)
                {
                    yield return null;
                }

                if (task.IsFaulted)
                {
                    var msg = task.Exception?.GetBaseException().Message ?? "provision failed";
                    mainWorldScreen?.ShowFatal(
                        "重新开局失败",
                        msg + "\n\n" + TerminalProvisionCopy,
                        terminalToProvision: true);
                    yield break;
                }

                var outcome = task.Result;
                if (!outcome.Ok)
                {
                    var detail = outcome.IsAmbiguous
                        ? AmbiguousPlayerCopy + "\n\n" + outcome.FormatDetail()
                          + "\n\n点击「重新开局」再试一局。禁止对本局自动重试模型。"
                        : outcome.FormatDetail() + "\n\n" + TerminalProvisionCopy;
                    mainWorldScreen?.ShowFatal(
                        outcome.IsAmbiguous ? "开局未完成" : "重新开局失败",
                        detail,
                        terminalToProvision: true);
                    yield break;
                }

                engineBaseUrl = settings.EngineBaseUrl;
                playerLocale = locale;
                HostDisplayLocale.SetPreferred(locale);
                sessionId = outcome.Success.SessionId;
                worldId = outcome.Success.WorldId;
                initialServerEnvelopesJson = outcome.Success.ServerEnvelopesJson;
                mode = SessionSourceMode.EngineWithInitialView;
                sendClientReadyOnStart = true;
                _terminalToProvisionOnly = false;

                yield return RebuildSessionConnection(
                    seedFromInitialEnvelopes: true,
                    preferResyncWhenBasisPresent: false);

                Debug.Log(
                    "[Bootstrap] in-Play reprovision ok session=" + sessionId + " world=" + worldId);
            }
            finally
            {
                _reprovisionInFlight = false;
            }
        }

        private static string ResolvePlayerNameForProvision(ProvisionLocalSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.PlayerName))
            {
                return settings.PlayerName.Trim();
            }

#if UNITY_EDITOR
            var fromPrefs = UnityEditor.EditorPrefs.GetString("Luoxia.Provision.PlayerName", string.Empty);
            if (!string.IsNullOrWhiteSpace(fromPrefs))
            {
                return fromPrefs.Trim();
            }
#endif
            return ProvisionGatewayClient.DefaultPlayerName;
        }

        private IEnumerator ReconnectThenResync()
        {
            _reconnectInFlight = true;
            try
            {
                yield return RebuildSessionConnection(
                    seedFromInitialEnvelopes: mode == SessionSourceMode.EngineWithInitialView,
                    preferResyncWhenBasisPresent: true);

                Debug.Log("[Bootstrap] recoverability=reconnect completed");
            }
            finally
            {
                _reconnectInFlight = false;
            }
        }

        /// <summary>
        /// Shared Host rebuild for recoverability=reconnect and in-Play reprovision:
        /// new transport + bridge (+ new replica/gate for brand-new session), seed, ready/resync.
        /// </summary>
        private IEnumerator RebuildSessionConnection(
            bool seedFromInitialEnvelopes,
            bool preferResyncWhenBasisPresent)
        {
            UnwireBridgeEvents();
            _bridge?.DetachPresentation();

            // Brand-new session identity (reprovision) must not keep the abandoned replica/gate.
            if (!preferResyncWhenBasisPresent)
            {
                _replica = new SessionReplica();
                _gate = new CommandGate();
                _selection?.Clear();
                _factory = new ClientEnvelopeFactory();
                _presentation = new PresentationRouter();
            }
            else
            {
                _replica?.ClearFatalForRetry();
            }

            _transport = new HttpBridgeTransport(engineBaseUrl);
            _bridge = new BridgeSessionClient(_transport, _replica, _gate, _factory, _presentation);
            WireBridgeEvents();
            _intents = new PlayerIntentRouter(_replica, _gate, _selection, _bridge, _factory, this, worldId);
            WireUi();

            SessionViewDto seeded = null;
            var serverSeq = 0;
            if (seedFromInitialEnvelopes &&
                !string.IsNullOrWhiteSpace(initialServerEnvelopesJson) &&
                TryParseInitialView(initialServerEnvelopesJson, out seeded, out serverSeq))
            {
                // Attach identity; authoritative view comes from ready/resync below when needed.
            }
            else if (seedFromInitialEnvelopes && !preferResyncWhenBasisPresent
                     && !string.IsNullOrWhiteSpace(initialServerEnvelopesJson))
            {
                _terminalToProvisionOnly = true;
                mainWorldScreen?.ShowFatal(
                    "初始 SessionView 无效",
                    "provision 返回的 server_envelopes 无法解析为 session.view。",
                    terminalToProvision: true);
                yield break;
            }

            _bridge.AttachSession(sessionId, seeded, serverSeq);

            Task<SessionViewDto> task;
            if (preferResyncWhenBasisPresent &&
                _replica != null &&
                _replica.HasView &&
                !string.IsNullOrEmpty(_replica.CurrentView?.basis_token))
            {
                task = _bridge.ResyncAsync();
            }
            else
            {
                task = _bridge.SendReadyAsync();
            }

            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                var msg = task.Exception?.GetBaseException().Message ?? "session rebuild failed";
                _terminalToProvisionOnly = true;
                if (!TryShowAmbiguousTerminal(msg))
                {
                    mainWorldScreen?.ShowFatal(
                        preferResyncWhenBasisPresent ? "重连失败" : "重新开局失败",
                        msg + "\n\n" + TerminalProvisionCopy,
                        terminalToProvision: true);
                }

                yield break;
            }

            if (task.Result != null)
            {
                Debug.Log($"[Bootstrap] rebuild ready/resync ok view_revision={task.Result.view_revision}");
            }
        }

        private IEnumerator RetryReadyOrResync()
        {
            _replica?.ClearFatalForRetry();
            Task<SessionViewDto> task;
            if (_replica != null &&
                _replica.HasView &&
                !string.IsNullOrEmpty(_replica.CurrentView?.basis_token))
            {
                task = _bridge.ResyncAsync();
            }
            else
            {
                task = _bridge.SendReadyAsync();
            }

            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                var msg = task.Exception?.GetBaseException().Message ?? "retry failed";
                if (!TryShowAmbiguousTerminal(msg))
                {
                    mainWorldScreen?.ShowFatal("重试失败", msg);
                }

                yield break;
            }

            Debug.Log("[Bootstrap] fatal retry completed");
        }

        private IEnumerator ConnectEngine()
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                const string msg = "sessionId required — run Luoxia/Play/Provision Local or overlay「重新开局」";
                Debug.LogError("[Bootstrap] " + msg);
                _terminalToProvisionOnly = true;
                mainWorldScreen?.ShowFatal("未 Provision", msg, terminalToProvision: true);
                yield break;
            }

            SessionViewDto seeded = null;
            var serverSeq = 0;
            if (mode == SessionSourceMode.EngineWithInitialView &&
                !string.IsNullOrWhiteSpace(initialServerEnvelopesJson))
            {
                if (TryParseInitialView(initialServerEnvelopesJson, out seeded, out serverSeq))
                {
                    Debug.Log($"[Bootstrap] seeded SessionView rev={seeded.view_revision} seq={serverSeq}");
                }
                else
                {
                    Debug.LogError("[Bootstrap] failed to parse initialServerEnvelopesJson");
                    _terminalToProvisionOnly = true;
                    mainWorldScreen?.ShowFatal(
                        "初始 SessionView 无效",
                        "initialServerEnvelopesJson 无法解析为 session.view，请重新 Provision。",
                        terminalToProvision: true);
                    yield break;
                }
            }

            _bridge.AttachSession(sessionId, seeded, serverSeq);

            if (!sendClientReadyOnStart)
            {
                yield break;
            }

            var task = _bridge.SendReadyAsync();
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                var msg = task.Exception?.GetBaseException().Message ?? "client.ready failed";
                Debug.LogError($"[Bootstrap] {msg}");
                if (!TryShowAmbiguousTerminal(msg))
                {
                    mainWorldScreen?.ShowFatal("连接 Engine 失败", msg);
                }

                yield break;
            }

            if (task.Result != null)
            {
                Debug.Log($"[Bootstrap] ready ok view_revision={task.Result.view_revision}");
            }
            else if (!_replica.HasView)
            {
                mainWorldScreen?.ShowFatal(
                    "无 SessionView",
                    "client.ready 未返回 session.view，请检查 Engine 与 session_id。");
            }
        }

        public static bool TryParseInitialView(string json, out SessionViewDto view, out int serverSequence)
        {
            view = null;
            serverSequence = 0;
            try
            {
                var batch = BridgeJson.DeserializeServerBatch(json);
                for (var i = 0; i < batch.Count; i++)
                {
                    var env = batch[i];
                    var extracted = BridgeJson.TryExtractSessionView(env);
                    if (extracted != null)
                    {
                        view = extracted;
                        serverSequence = env.sequence;
                        return true;
                    }
                }

                view = BridgeJson.Deserialize<SessionViewDto>(json);
                return view != null && !string.IsNullOrEmpty(view.session_id);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(ex.Message);
                return false;
            }
        }
    }
}
