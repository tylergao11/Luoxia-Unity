using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Luoxia.Contracts;
using Luoxia.Session;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Luoxia.Net
{
    /// <summary>
    /// Orchestrates transport + replica + presentation router + single-flight commands.
    /// Does not open sessions; expects gateway-provided session_id + optional initial view.
    /// Client sequence is owned exclusively by ClientEnvelopeFactory.
    /// ProtocolError.recoverability is the sole Host recovery switch (retry|resync|reconnect|fatal).
    /// </summary>
    public sealed class BridgeSessionClient
    {
        private readonly IBridgeTransport _transport;
        private readonly ISessionReplica _replica;
        private readonly ICommandGate _gate;
        private readonly ClientEnvelopeFactory _factory;
        private readonly PresentationRouter _presentation;

        private string _sessionId;
        private int _applyDepth;

        private string _lastMutatingCommandId;
        private string _lastMutatingEnvelopeJson;
        private string _idempotentRetryUsedForCommandId;

        private string _deferredRecoverability;
        private string _deferredProtocolMessage;
        /// <summary>When true, SendMutatingAsync must Fail the gate (retry exhausted / reconnect / fatal path).</summary>
        private bool _blockCommandComplete;

        public BridgeSessionClient(
            IBridgeTransport transport,
            ISessionReplica replica,
            ICommandGate gate,
            ClientEnvelopeFactory factory = null,
            PresentationRouter presentation = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _replica = replica ?? throw new ArgumentNullException(nameof(replica));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _factory = factory ?? new ClientEnvelopeFactory();
            _presentation = presentation ?? new PresentationRouter();

            _presentation.DialogueReplyReceived += HandleDialogueReply;
            _presentation.CommandResultReceived += HandleCommandResult;
            _presentation.ProtocolErrorReceived += HandleProtocolError;
            _presentation.UnknownMessageReceived += HandleUnknown;
        }

        /// <summary>
        /// Drop presentation subscriptions before Host replaces this bridge
        /// (reconnect / in-Play reprovision). Safe to call more than once.
        /// </summary>
        public void DetachPresentation()
        {
            if (_presentation == null)
            {
                return;
            }

            _presentation.DialogueReplyReceived -= HandleDialogueReply;
            _presentation.CommandResultReceived -= HandleCommandResult;
            _presentation.ProtocolErrorReceived -= HandleProtocolError;
            _presentation.UnknownMessageReceived -= HandleUnknown;
        }

        public string SessionId => _sessionId;
        public ClientEnvelopeFactory Factory => _factory;
        public IPresentationBus Presentation => _presentation;

        public event Action<string> UserVisibleError;
        /// <summary>recoverability=fatal — UI terminal page; only path back is provision/open.</summary>
        public event Action<string> ProtocolFatal;
        /// <summary>recoverability=reconnect — Host rebuilds session connection then resyncs.</summary>
        public event Action ReconnectRequested;

        public void AttachSession(string sessionId, SessionViewDto initialView = null, int initialServerSequence = 0)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                throw new ArgumentException("sessionId required", nameof(sessionId));
            }

            _sessionId = sessionId;
            _factory.SetNextSequence(0);
            _deferredRecoverability = null;
            _deferredProtocolMessage = null;
            _blockCommandComplete = false;

            if (initialView != null)
            {
                _replica.Bootstrap(initialView, initialServerSequence);
            }
        }

        public async Task<SessionViewDto> SendReadyAsync(CancellationToken ct = default)
        {
            EnsureSession();
            var envelope = _factory.CreateReady(
                _sessionId,
                ClientEnvelopeFactory.PlaceholderBuildDigest());
            return await SendAndApplyViewsAsync(envelope, ct).ConfigureAwait(true);
        }

        public async Task<SessionViewDto> ResyncAsync(CancellationToken ct = default)
        {
            EnsureSession();
            var view = _replica.CurrentView;
            if (view == null || string.IsNullOrEmpty(view.basis_token))
            {
                throw new InvalidOperationException("resync requires basis_token from a prior SessionView");
            }

            _replica.EnterResynchronizing();
            var envelope = _factory.CreateResync(_sessionId, view.basis_token);
            return await SendAndApplyViewsAsync(envelope, ct).ConfigureAwait(true);
        }

        public async Task<bool> SendMutatingAsync(string commandId, string envelopeJson, CancellationToken ct = default)
        {
            if (!_gate.TryBegin(commandId, envelopeJson))
            {
                return false;
            }

            _lastMutatingCommandId = commandId;
            _lastMutatingEnvelopeJson = envelopeJson;
            _blockCommandComplete = false;

            try
            {
                await SendAndApplyViewsAsync(envelopeJson, ct).ConfigureAwait(true);

                if (_replica.State == SessionReplicaState.Fatal)
                {
                    _gate.Fail(commandId, _replica.FatalReason ?? "protocol.fatal");
                    return false;
                }

                if (_blockCommandComplete || !string.IsNullOrEmpty(_deferredRecoverability))
                {
                    _gate.Fail(commandId, _deferredProtocolMessage ?? "protocol.recovery");
                    return false;
                }

                _gate.Complete(commandId);
                return true;
            }
            catch (Exception ex)
            {
                var reason = ex.GetBaseException().Message;
                _gate.Fail(commandId, reason);
                Debug.LogError($"[Bridge] command failed: {reason}");
                UserVisibleError?.Invoke(reason);
                return false;
            }
        }

        public async Task<SessionViewDto> SendAndApplyViewsAsync(string clientEnvelopeJson, CancellationToken ct = default)
        {
            _applyDepth++;
            try
            {
                var payloads = await _transport.SendClientEnvelopeAsync(clientEnvelopeJson, ct).ConfigureAwait(true);
                SessionViewDto lastView = null;

                for (var p = 0; p < payloads.Length; p++)
                {
                    var batch = BridgeJson.DeserializeServerBatch(payloads[p]);
                    for (var i = 0; i < batch.Count; i++)
                    {
                        var env = batch[i];
                        if (env == null)
                        {
                            continue;
                        }

                        var view = BridgeJson.TryExtractSessionView(env);
                        if (view != null)
                        {
                            _replica.ApplyFullView(view, env.sequence);
                            lastView = view;
                            continue;
                        }

                        // Engine sequences every envelope. Gap/Fatal/Resync → do not dispatch.
                        if (!_replica.AcknowledgeServerSequence(env.sequence))
                        {
                            Debug.LogWarning(
                                $"[Bridge] skip dispatch type={BridgeJson.MessageType(env)} seq={env.sequence} state={_replica.State}");
                            continue;
                        }

                        if (!_presentation.TryDispatchNonView(env))
                        {
                            Debug.Log($"[Bridge] unhandled server message type={BridgeJson.MessageType(env)} seq={env.sequence}");
                        }
                    }
                }

                if (_applyDepth == 1 &&
                    _replica.State == SessionReplicaState.Resynchronizing &&
                    _replica.CurrentView != null &&
                    !string.IsNullOrEmpty(_replica.CurrentView.basis_token) &&
                    string.IsNullOrEmpty(_deferredRecoverability))
                {
                    Debug.LogWarning("[Bridge] sequence gap detected — issuing client.resync");
                    return await ResyncAsync(ct).ConfigureAwait(true);
                }

                if (_applyDepth == 1 && !string.IsNullOrEmpty(_deferredRecoverability))
                {
                    return await ExecuteDeferredRecoverabilityAsync(lastView, ct).ConfigureAwait(true);
                }

                return lastView;
            }
            finally
            {
                _applyDepth--;
            }
        }

        private async Task<SessionViewDto> ExecuteDeferredRecoverabilityAsync(
            SessionViewDto lastView,
            CancellationToken ct)
        {
            var kind = _deferredRecoverability;
            var message = _deferredProtocolMessage ?? "protocol.error";
            _deferredRecoverability = null;
            _deferredProtocolMessage = null;

            switch (kind)
            {
                case "retry":
                    return await ExecuteRetryOnceAsync(message, lastView, ct).ConfigureAwait(true);
                case "resync":
                    Debug.Log("[Bridge] recoverability=resync — silent session.resync_request");
                    return await ResyncAsync(ct).ConfigureAwait(true);
                case "reconnect":
                    Debug.Log("[Bridge] recoverability=reconnect — Host must rebuild connection then resync");
                    _blockCommandComplete = true;
                    ReconnectRequested?.Invoke();
                    return lastView;
                case "fatal":
                    ApplyFatal(message);
                    return lastView;
                default:
                    ApplyFatal("unknown recoverability: " + kind + " — " + message);
                    return lastView;
            }
        }

        private async Task<SessionViewDto> ExecuteRetryOnceAsync(
            string message,
            SessionViewDto lastView,
            CancellationToken ct)
        {
            var commandId = _lastMutatingCommandId;
            var envelope = _lastMutatingEnvelopeJson;
            if (string.IsNullOrEmpty(commandId) || string.IsNullOrEmpty(envelope))
            {
                Debug.LogWarning("[Bridge] recoverability=retry but no mutating command to resend");
                _blockCommandComplete = true;
                UserVisibleError?.Invoke(message);
                return lastView;
            }

            if (string.Equals(_idempotentRetryUsedForCommandId, commandId, StringComparison.Ordinal))
            {
                Debug.LogWarning("[Bridge] recoverability=retry already used once — user-visible prompt");
                _blockCommandComplete = true;
                _deferredProtocolMessage = message;
                UserVisibleError?.Invoke(message);
                return lastView;
            }

            _idempotentRetryUsedForCommandId = commandId;
            Debug.Log($"[Bridge] recoverability=retry — idempotent resend command_id={commandId}");
            try
            {
                var view = await SendAndApplyViewsAsync(envelope, ct).ConfigureAwait(true);
                if (!string.IsNullOrEmpty(_deferredRecoverability)
                    || _replica.State == SessionReplicaState.Fatal
                    || _blockCommandComplete)
                {
                    var failMsg = _deferredProtocolMessage ?? _replica.FatalReason ?? message;
                    _blockCommandComplete = true;
                    _deferredProtocolMessage = failMsg;
                    _deferredRecoverability = null;
                    UserVisibleError?.Invoke(failMsg);
                }

                return view;
            }
            catch (Exception ex)
            {
                var reason = ex.GetBaseException().Message;
                Debug.LogError($"[Bridge] retry resend failed: {reason}");
                _blockCommandComplete = true;
                _deferredProtocolMessage = reason;
                UserVisibleError?.Invoke(reason);
                return lastView;
            }
        }

        private void HandleDialogueReply(DialogueReplyDto reply)
        {
            _replica.ApplyDialogueReply(reply);
        }

        private void HandleCommandResult(CommandResultDto result)
        {
            if (result == null)
            {
                return;
            }

            Debug.Log($"[Bridge] command.result status={result.status} code={result.code} id={result.command_id}");
            if (result.IsRejected)
            {
                var msg = result.ResolveMessage();
                if (string.IsNullOrEmpty(msg) || msg == HostDisplayLocale.MissingPlaceholder)
                {
                    msg = string.IsNullOrEmpty(result.code) ? "command rejected" : result.code;
                }

                UserVisibleError?.Invoke(msg);
            }
        }

        private void HandleProtocolError(ProtocolErrorDto error)
        {
            var msg = error != null
                ? $"{error.code}: {error.message}"
                : "protocol.error";
            Debug.LogError($"[Bridge] {msg} recoverability={error?.recoverability}");

            // Sole Host recovery switch — never branch on error.code strings here.
            var recoverability = error?.recoverability;
            if (string.IsNullOrEmpty(recoverability))
            {
                ApplyFatal(msg);
                return;
            }

            switch (recoverability)
            {
                case "retry":
                case "resync":
                case "reconnect":
                    _deferredRecoverability = recoverability;
                    _deferredProtocolMessage = msg;
                    break;
                case "fatal":
                    ApplyFatal(msg);
                    break;
                default:
                    ApplyFatal("unknown recoverability '" + recoverability + "': " + msg);
                    break;
            }
        }

        private void ApplyFatal(string message)
        {
            _replica.MarkFatal(message);
            ProtocolFatal?.Invoke(message);
        }

        private void HandleUnknown(string type)
        {
            var msg = "unknown/unparseable server message type=" + (type ?? "null");
            Debug.LogError("[Bridge] " + msg);
            ApplyFatal(msg);
        }

        private void EnsureSession()
        {
            if (string.IsNullOrEmpty(_sessionId))
            {
                throw new InvalidOperationException("AttachSession first");
            }
        }

        /// <summary>
        /// SHA-256 hex of UTF-8 payload. Used for stage.outcome_proposal evidence_digest MVP.
        /// </summary>
        public static string Sha256HexUtf8(string payload)
        {
            if (payload == null)
            {
                payload = string.Empty;
            }

            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var sb = new StringBuilder(bytes.Length * 2);
                for (var i = 0; i < bytes.Length; i++)
                {
                    sb.Append(bytes[i].ToString("x2"));
                }

                return sb.ToString();
            }
        }

        public static string EvidenceDigestForOutcome(string stageInstanceId, int stageRevision, string outcomeType, JObject outcome)
        {
            var body = new JObject
            {
                ["stage_instance_id"] = stageInstanceId ?? string.Empty,
                ["stage_revision"] = stageRevision,
                ["outcome_type"] = outcomeType ?? string.Empty,
                ["outcome"] = outcome ?? new JObject()
            };
            return Sha256HexUtf8(body.ToString(Newtonsoft.Json.Formatting.None));
        }
    }
}
