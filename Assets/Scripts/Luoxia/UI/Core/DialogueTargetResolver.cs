using Luoxia.Contracts;

namespace Luoxia.UI.Core
{
    /// <summary>
    /// Effective dialogue reply target: explicit selection, else focused dialogue non-player.
    /// Shared by DialogueFeaturePanel (display) and PlayerIntentRouter (send).
    /// </summary>
    public static class DialogueTargetResolver
    {
        public static bool TryResolveEffective(
            SessionViewDto view,
            DialogueTarget? selection,
            out DialogueTarget target)
        {
            if (selection.HasValue)
            {
                target = selection.Value;
                return true;
            }

            target = default;
            var dialogue = FindFocusedDialogue(view, null);
            if (dialogue?.participants == null)
            {
                return false;
            }

            var playerId = view?.player_entity_id;
            for (var i = 0; i < dialogue.participants.Count; i++)
            {
                var p = dialogue.participants[i];
                if (p == null)
                {
                    continue;
                }

                if (p.KindEnum == DialogueParticipantKind.System)
                {
                    target = DialogueTarget.System(string.Empty);
                    return true;
                }

                if (p.KindEnum == DialogueParticipantKind.Entity &&
                    !string.IsNullOrEmpty(p.entity_id) &&
                    p.entity_id != playerId)
                {
                    var name = LoreQuery.ResolveSubjectDisplayName(view, p.entity_id);
                    target = DialogueTarget.Entity(p.entity_id, name);
                    return true;
                }
            }

            return false;
        }

        public static DialogueViewDto FindFocusedDialogue(SessionViewDto view, DialogueTarget? selection)
        {
            if (view?.dialogues == null || view.dialogues.Count == 0)
            {
                return null;
            }

            for (var i = 0; i < view.dialogues.Count; i++)
            {
                var d = view.dialogues[i];
                if (d == null || !d.IsActive)
                {
                    continue;
                }

                if (!selection.HasValue)
                {
                    return d;
                }

                if (MatchesSelection(d, selection.Value))
                {
                    return d;
                }
            }

            return null;
        }

        private static bool MatchesSelection(DialogueViewDto dialogue, DialogueTarget target)
        {
            if (dialogue.participants == null)
            {
                return false;
            }

            for (var i = 0; i < dialogue.participants.Count; i++)
            {
                var p = dialogue.participants[i];
                if (target.kind == DialogueParticipantKind.System &&
                    p.KindEnum == DialogueParticipantKind.System)
                {
                    return true;
                }

                if (target.kind == DialogueParticipantKind.Entity &&
                    p.KindEnum == DialogueParticipantKind.Entity &&
                    p.entity_id == target.entityId)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
