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
        void EnterResynchronizing();
        void MarkFatal(string reason);
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
