using System;
using Luoxia.Contracts;
using Luoxia.Session;
// System.Serializable is used by DialogueTarget.

namespace Luoxia.UI.Core
{
    /// <summary>
    /// Marker for anything that can be shown/hidden by the screen shell.
    /// </summary>
    public interface IView
    {
        bool IsVisible { get; }
        void Show();
        void Hide();
    }

    /// <summary>
    /// View that rebinds when SessionView is replaced.
    /// </summary>
    public interface ISessionViewBinder : IView
    {
        void BindSession(ISessionViewSource source);
        void UnbindSession();
        void OnSessionView(SessionViewDto view);
    }

    /// <summary>
    /// Bottom feature panels (Dialogue / Event / future GoalPlan).
    /// </summary>
    public interface IFeaturePanel : ISessionViewBinder
    {
        string FeatureId { get; }
        void SetActiveFeature(bool active);
    }

    /// <summary>
    /// Recyclable list row. TModel is a contract DTO or lightweight row VM.
    /// </summary>
    public interface IListItemView<in TModel>
    {
        void Bind(TModel model, int index);
        void Unbind();
    }

    /// <summary>
    /// UI emits player intents; network layer maps intents to ClientMessages.
    /// Keeps MonoBehaviours free of envelope JSON.
    /// </summary>
    public interface IPlayerIntentSink
    {
        bool TrySelectDialogueTarget(DialogueTarget target);
        bool TrySendDialogueText(string text);
        bool TryCloseActiveDialogue();
        bool TryTriggerEventCard(string eventCardId);
        bool TryTriggerAllAvailableEventCards();
        bool TryMapMove(string destinationEntityId);
        bool TryEndPlayerDay();
        bool TryOpenMap();
        bool TrySubmitStageOutcome(
            string stageInstanceId,
            int stageRevision,
            string outcomeType,
            Newtonsoft.Json.Linq.JObject outcome = null);
    }

    [Serializable]
    public struct DialogueTarget
    {
        public DialogueParticipantKind kind;
        public string entityId;
        public string displayName;

        public static DialogueTarget System(string displayName = "System") => new DialogueTarget
        {
            kind = DialogueParticipantKind.System,
            entityId = null,
            displayName = displayName
        };

        public static DialogueTarget Entity(string entityId, string displayName) => new DialogueTarget
        {
            kind = DialogueParticipantKind.Entity,
            entityId = entityId,
            displayName = displayName
        };
    }

    /// <summary>
    /// Shared selection state for avatar rail + dialogue panel.
    /// </summary>
    public interface IDialogueSelection
    {
        DialogueTarget? Current { get; }
        event Action<DialogueTarget?> Changed;
        void Select(DialogueTarget target);
        void Clear();
    }
}
