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
    /// Sequence is allocated only via ClientEnvelopeFactory.AllocateSequence.
    /// </summary>
    public interface IClientEnvelopeFactory
    {
        int PeekNextSequence { get; }
        void SetNextSequence(int sequence);
        int AllocateSequence();

        string CreateReady(string sessionId, string clientBuildDigest);
        string CreateResync(string sessionId, string basisToken);
        string CreateDialogueStart(
            string sessionId,
            string commandId,
            string basisToken,
            string worldId,
            string recipientEntityIdOrSystem,
            string text,
            string interactionKind = ClientEnvelopeFactory.DefaultInteractionKind);
        string CreateDialogueContinue(
            string sessionId,
            string commandId,
            string basisToken,
            string dialogueId,
            string text,
            string interactionKind = ClientEnvelopeFactory.DefaultInteractionKind);
        string CreateDialogueClose(string sessionId, string commandId, string basisToken, string dialogueId);
        string CreateMapMove(
            string sessionId,
            string commandId,
            string basisToken,
            string worldId,
            string destinationEntityId);
        string CreateEventCardTrigger(
            string sessionId,
            string commandId,
            string basisToken,
            string eventCardId);
        string CreatePlayerDayEnd(string sessionId, string commandId, string basisToken);
        string CreateStageOutcomeProposal(
            string sessionId,
            string commandId,
            string basisToken,
            string stageInstanceId,
            int stageRevision,
            string outcomeType,
            Newtonsoft.Json.Linq.JObject outcome,
            string evidenceDigest);
    }

    /// <summary>
    /// Single-flight world command gate: at most one pending mutating command.
    /// </summary>
    public interface ICommandGate
    {
        bool HasPending { get; }
        string PendingCommandId { get; }
        string PendingEnvelopeJson { get; }
        bool TryBegin(string commandId, string originalEnvelopeJson);
        void Complete(string commandId);
        void Fail(string commandId, string reason);
        event Action PendingChanged;
        event Action<string> CommandCompleted;
        event Action<string, string> CommandFailed;
    }
}
