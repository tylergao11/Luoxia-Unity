using System;
using Luoxia.Contracts;

namespace Luoxia.Session
{
    public enum SessionReplicaState
    {
        Bootstrapped,
        Synchronizing,
        Synchronized,
        Resynchronizing,
        Fatal
    }

    /// <summary>
    /// Authoritative client mirror of the last full SessionView.
    /// Full view replace only; no speculative world merge.
    /// </summary>
    public interface ISessionReplica
    {
        SessionReplicaState State { get; }
        SessionViewDto CurrentView { get; }
        int ExpectedServerSequence { get; }
        string FatalReason { get; }

        event Action<SessionViewDto> ViewReplaced;
        event Action<SessionReplicaState, SessionReplicaState> StateChanged;

        void Bootstrap(SessionViewDto initialView, int initialServerSequence);
        void ApplyFullView(SessionViewDto view, int serverSequence);
        /// <summary>
        /// Advance the server sequence cursor for non-view envelopes
        /// (dialogue.reply, command.result, stage.*, …). Engine sequences every envelope.
        /// Returns false when Fatal, Resynchronizing, or a gap was detected (caller must not dispatch).
        /// </summary>
        bool AcknowledgeServerSequence(int serverSequence);
        void ApplyDialogueReply(DialogueReplyDto reply);
        void EnterResynchronizing();
        void MarkFatal(string reason);
        void ClearFatalForRetry();
    }

    /// <summary>
    /// Read-only surface for UI bindings. Views must not mutate session state.
    /// </summary>
    public interface ISessionViewSource
    {
        SessionViewDto CurrentView { get; }
        bool HasView { get; }
        event Action<SessionViewDto> ViewChanged;
    }
}
