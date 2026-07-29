using System;
using System.Threading;
using System.Threading.Tasks;

namespace Luoxia.Net
{
    /// <summary>
    /// Transport-agnostic Client Bridge. Engine HTTP is one implementation.
    /// Sends one ClientEnvelope JSON, receives zero-or-more ServerEnvelope JSON bodies.
    /// </summary>
    public interface IBridgeTransport
    {
        Task<string[]> SendClientEnvelopeAsync(string clientEnvelopeJson, CancellationToken ct);
    }

    /// <summary>
    /// Builds contract-valid ClientEnvelope payloads. Does not own world rules.
    /// </summary>
    public interface IClientEnvelopeFactory
    {
        string CreateReady(string sessionId, int sequence, string clientBuildDigest);
        string CreateResync(string sessionId, int sequence, string basisToken);
        string CreateDialogueStart(string sessionId, int sequence, string commandId, string basisToken, string recipientEntityIdOrSystem, string text);
        string CreateDialogueContinue(string sessionId, int sequence, string commandId, string basisToken, string dialogueId, string text);
        string CreateDialogueClose(string sessionId, int sequence, string commandId, string basisToken, string dialogueId);
        string CreateMapMove(string sessionId, int sequence, string commandId, string basisToken, string worldId, string destinationEntityId);
        string CreateEventCardTrigger(string sessionId, int sequence, string commandId, string basisToken, string eventCardId);
        string CreatePlayerDayEnd(string sessionId, int sequence, string commandId, string basisToken);
    }

    /// <summary>
    /// Single-flight world command gate: at most one pending mutating command.
    /// </summary>
    public interface ICommandGate
    {
        bool HasPending { get; }
        string PendingCommandId { get; }
        bool TryBegin(string commandId, string originalEnvelopeJson);
        void Complete(string commandId);
        void Fail(string commandId, string reason);
        event Action<string> CommandCompleted;
        event Action<string, string> CommandFailed;
    }
}
