using System;
using UnityEngine;

namespace Luoxia.Net
{
    public sealed class CommandGate : ICommandGate
    {
        private string _pendingCommandId;
        private string _pendingEnvelopeJson;

        public bool HasPending => !string.IsNullOrEmpty(_pendingCommandId);
        public string PendingCommandId => _pendingCommandId;
        public string PendingEnvelopeJson => _pendingEnvelopeJson;

        public event Action PendingChanged;
        public event Action<string> CommandCompleted;
        public event Action<string, string> CommandFailed;

        public bool TryBegin(string commandId, string originalEnvelopeJson)
        {
            if (string.IsNullOrEmpty(commandId))
            {
                throw new ArgumentException("commandId required", nameof(commandId));
            }

            if (HasPending)
            {
                Debug.LogWarning($"[CommandGate] reject {commandId}; pending={_pendingCommandId}");
                return false;
            }

            _pendingCommandId = commandId;
            _pendingEnvelopeJson = originalEnvelopeJson;
            PendingChanged?.Invoke();
            return true;
        }

        public void Complete(string commandId)
        {
            if (!IsCurrent(commandId))
            {
                return;
            }

            _pendingCommandId = null;
            _pendingEnvelopeJson = null;
            PendingChanged?.Invoke();
            CommandCompleted?.Invoke(commandId);
        }

        public void Fail(string commandId, string reason)
        {
            if (!IsCurrent(commandId))
            {
                return;
            }

            _pendingCommandId = null;
            _pendingEnvelopeJson = null;
            PendingChanged?.Invoke();
            CommandFailed?.Invoke(commandId, reason ?? "failed");
        }

        private bool IsCurrent(string commandId) =>
            HasPending && string.Equals(_pendingCommandId, commandId, StringComparison.Ordinal);
    }
}
