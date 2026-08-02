using System;
using System.Collections.Generic;
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
                Debug.LogWarning(
                    $"[SessionReplica] sequence gap expected={_expectedServerSequence} got={serverSequence}");
                EnterResynchronizing();
                return;
            }

            view.lore ??= new List<LoreViewDto>();
            view.render_nodes ??= new List<RenderNodeDto>();
            view.dialogues ??= new List<DialogueViewDto>();
            view.event_cards ??= new List<EventCardViewDto>();

            ReplaceView(view, serverSequence + 1);
            SetState(SessionReplicaState.Synchronized);
        }

        public bool AcknowledgeServerSequence(int serverSequence)
        {
            if (_state == SessionReplicaState.Fatal ||
                _state == SessionReplicaState.Resynchronizing)
            {
                return false;
            }

            if (_state == SessionReplicaState.Synchronized &&
                serverSequence != _expectedServerSequence)
            {
                Debug.LogWarning(
                    $"[SessionReplica] sequence gap on non-view expected={_expectedServerSequence} got={serverSequence}");
                EnterResynchronizing();
                return false;
            }

            if (_state == SessionReplicaState.Synchronized ||
                _state == SessionReplicaState.Synchronizing)
            {
                _expectedServerSequence = serverSequence + 1;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Low-latency dialogue.reply merge. Final set remains SessionView.dialogues
        /// (dedupe by turn_id). Does not advance server sequence baseline.
        /// </summary>
        public void ApplyDialogueReply(DialogueReplyDto reply)
        {
            if (_state == SessionReplicaState.Fatal ||
                _state == SessionReplicaState.Resynchronizing ||
                reply?.turn == null ||
                _currentView == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(reply.dialogue_id))
            {
                return;
            }

            _currentView.dialogues ??= new List<DialogueViewDto>();
            DialogueViewDto dialogue = null;
            for (var i = 0; i < _currentView.dialogues.Count; i++)
            {
                var d = _currentView.dialogues[i];
                if (d != null && d.dialogue_id == reply.dialogue_id)
                {
                    dialogue = d;
                    break;
                }
            }

            if (dialogue == null)
            {
                Debug.Log($"[SessionReplica] dialogue.reply for unknown dialogue_id={reply.dialogue_id}; waiting SessionView");
                return;
            }

            dialogue.turns ??= new List<DialogueTurnViewDto>();
            var turnId = reply.turn.turn_id;
            if (!string.IsNullOrEmpty(turnId))
            {
                for (var i = 0; i < dialogue.turns.Count; i++)
                {
                    if (dialogue.turns[i] != null && dialogue.turns[i].turn_id == turnId)
                    {
                        return;
                    }
                }
            }

            dialogue.turns.Add(reply.turn);
            ViewChanged?.Invoke(_currentView);
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

        /// <summary>Clear Fatal so an explicit ready/resync retry can re-enter Synchronized.</summary>
        public void ClearFatalForRetry()
        {
            if (_state != SessionReplicaState.Fatal)
            {
                return;
            }

            _fatalReason = null;
            SetState(SessionReplicaState.Resynchronizing);
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
