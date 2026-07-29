using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Luoxia.Contracts;
using Luoxia.Session;
using UnityEngine;

namespace Luoxia.Net
{
    /// <summary>
    /// Orchestrates transport + replica + single-flight commands.
    /// Does not open sessions; expects gateway-provided session_id + optional initial view.
    /// </summary>
    public sealed class BridgeSessionClient
    {
        private readonly IBridgeTransport _transport;
        private readonly ISessionReplica _replica;
        private readonly ICommandGate _gate;
        private readonly ClientEnvelopeFactory _factory;

        private string _sessionId;
        private int _clientSequence;

        public BridgeSessionClient(
            IBridgeTransport transport,
            ISessionReplica replica,
            ICommandGate gate,
            ClientEnvelopeFactory factory = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _replica = replica ?? throw new ArgumentNullException(nameof(replica));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _factory = factory ?? new ClientEnvelopeFactory();
        }

        public string SessionId => _sessionId;
        public ClientEnvelopeFactory Factory => _factory;

        public void AttachSession(string sessionId, SessionViewDto initialView = null, int initialServerSequence = 0)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                throw new ArgumentException("sessionId required", nameof(sessionId));
            }

            _sessionId = sessionId;
            _clientSequence = 0;
            _factory.SetNextSequence(0);

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
                _clientSequence++,
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
            var envelope = _factory.CreateResync(_sessionId, _clientSequence++, view.basis_token);
            return await SendAndApplyViewsAsync(envelope, ct).ConfigureAwait(true);
        }

        public async Task<bool> SendMutatingAsync(string commandId, string envelopeJson, CancellationToken ct = default)
        {
            if (!_gate.TryBegin(commandId, envelopeJson))
            {
                return false;
            }

            try
            {
                await SendAndApplyViewsAsync(envelopeJson, ct).ConfigureAwait(true);
                _gate.Complete(commandId);
                return true;
            }
            catch (Exception ex)
            {
                _gate.Fail(commandId, ex.Message);
                Debug.LogError($"[Bridge] command failed: {ex.Message}");
                return false;
            }
        }

        public async Task<SessionViewDto> SendAndApplyViewsAsync(string clientEnvelopeJson, CancellationToken ct = default)
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

                    var result = BridgeJson.TryExtractCommandResult(env);
                    if (result != null)
                    {
                        Debug.Log($"[Bridge] command.result ok={result.ok} code={result.code} id={result.command_id}");
                    }
                    else
                    {
                        Debug.Log($"[Bridge] server message type={BridgeJson.MessageType(env)} seq={env.sequence}");
                    }
                }
            }

            return lastView;
        }

        private void EnsureSession()
        {
            if (string.IsNullOrEmpty(_sessionId))
            {
                throw new InvalidOperationException("AttachSession first");
            }
        }
    }
}
