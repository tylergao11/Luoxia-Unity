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
    /// Use menu Luoxia/Play/Provision Local before Enter Play.
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
        /// <summary>fatal recoverability / abandoned world — overlay retry only returns to provision/open.</summary>
        private bool _terminalToProvisionOnly;
        private bool _reconnectInFlight;

        private const string ModelDispatchAmbiguousCode = "runtime.kernel.model_dispatch_ambiguous";
        private const string AmbiguousPlayerCopy =
            "开局未完成：世界导演未能就位，本次开局已作废。你可以重新开始一局。";
        private const string TerminalProvisionCopy =
            "会话已终止。请退出 Play，执行菜单 Luoxia/Play/Provision Local 重新开局。";

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
                    "禁止离线开局。请先启动 Engine + Provision，再执行菜单 Luoxia/Play/Provision Local，然后 Play。";
                Debug.LogError("[Bootstrap] " + msg);
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
                locale = "zh-CN";
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
            if (_reconnectInFlight)
            {
                return;
            }

            StartCoroutine(ReconnectThenResync());
        }

        private void HandleFatalRetry()
        {
            if (_terminalToProvisionOnly)
            {
                mainWorldScreen?.ShowFatal(
                    "请重新开局",
                    TerminalProvisionCopy,
                    terminalToProvision: true);
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
                + "\n\n请退出 Play，执行 Luoxia/Play/Provision Local（全新 provision）。禁止对本局自动重试模型。";
            mainWorldScreen?.ShowFatal("开局未完成", detail, terminalToProvision: true);
            return true;
        }

        private IEnumerator ReconnectThenResync()
        {
            _reconnectInFlight = true;
            try
            {
                _replica?.ClearFatalForRetry();
                UnwireBridgeEvents();

                _transport = new HttpBridgeTransport(engineBaseUrl);
                _bridge = new BridgeSessionClient(_transport, _replica, _gate, _factory, _presentation);
                WireBridgeEvents();
                _intents = new PlayerIntentRouter(_replica, _gate, _selection, _bridge, _factory, this, worldId);
                WireUi();

                SessionViewDto seeded = null;
                var serverSeq = 0;
                if (mode == SessionSourceMode.EngineWithInitialView &&
                    !string.IsNullOrWhiteSpace(initialServerEnvelopesJson) &&
                    TryParseInitialView(initialServerEnvelopesJson, out seeded, out serverSeq))
                {
                    // Re-attach identity; authoritative view comes from resync/ready below.
                }

                _bridge.AttachSession(sessionId, seeded, serverSeq);

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
                    var msg = task.Exception?.GetBaseException().Message ?? "reconnect failed";
                    _terminalToProvisionOnly = true;
                    mainWorldScreen?.ShowFatal("重连失败", msg + "\n\n" + TerminalProvisionCopy, terminalToProvision: true);
                    yield break;
                }

                Debug.Log("[Bootstrap] recoverability=reconnect completed");
            }
            finally
            {
                _reconnectInFlight = false;
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
                const string msg = "sessionId required — run Luoxia/Play/Provision Local";
                Debug.LogError("[Bootstrap] " + msg);
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
