using System;
using Luoxia.Contracts;
using UnityEngine;

namespace Luoxia.Session
{
    public sealed class SessionReplica : ISessionReplica, ISessionViewSource
    {
        private SessionViewDto _currentView;
        private int _expectedServerSequence;
        private SessionReplicaState _state = SessionReplicaState.Bootstrapped;
        private string _fatalReason;

        public SessionReplicaState State => _state;
        public SessionViewDto CurrentView => _currentView;
        public int ExpectedServerSequence => _expectedServerSequence;
        public string FatalReason => _fatalReason;
        public bool HasView => _currentView != null;

        public event Action<SessionViewDto> ViewReplaced;
        public event Action<SessionReplicaState, SessionReplicaState> StateChanged;
        public event Action<SessionViewDto> ViewChanged;

        public void Bootstrap(SessionViewDto initialView, int initialServerSequence)
        {
            if (initialView == null)
            {
                throw new ArgumentNullException(nameof(initialView));
            }

            ReplaceView(initialView, initialServerSequence);
            SetState(SessionReplicaState.Synchronizing);
        }

        public void ApplyFullView(SessionViewDto view, int serverSequence)
        {
            if (_state == SessionReplicaState.Fatal)
            {
                return;
            }

            if (view == null)
            {
                MarkFatal("session.view is null");
                return;
            }

            if (_state == SessionReplicaState.Synchronized &&
                serverSequence != _expectedServerSequence)
            {
                // gap: caller should resync; do not invent partial merge
                Debug.LogWarning(
                    $"[SessionReplica] sequence gap expected={_expectedServerSequence} got={serverSequence}");
                EnterResynchronizing();
                return;
            }

            ReplaceView(view, serverSequence + 1);
            SetState(SessionReplicaState.Synchronized);
        }

        public void EnterResynchronizing()
        {
            if (_state == SessionReplicaState.Fatal)
            {
                return;
            }

            SetState(SessionReplicaState.Resynchronizing);
        }

        public void MarkFatal(string reason)
        {
            _fatalReason = reason ?? "unknown";
            SetState(SessionReplicaState.Fatal);
        }

        private void ReplaceView(SessionViewDto view, int nextExpectedSequence)
        {
            _currentView = view;
            _expectedServerSequence = nextExpectedSequence;
            ViewReplaced?.Invoke(view);
            ViewChanged?.Invoke(view);
        }

        private void SetState(SessionReplicaState next)
        {
            if (_state == next)
            {
                return;
            }

            var previous = _state;
            _state = next;
            StateChanged?.Invoke(previous, next);
        }
    }
}
