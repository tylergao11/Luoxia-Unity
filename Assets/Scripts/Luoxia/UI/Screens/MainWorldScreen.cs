using Luoxia.Contracts;
using Luoxia.Session;
using Luoxia.UI.Core;
using Luoxia.UI.Features;
using Luoxia.UI.Widgets;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Screens
{
    /// <summary>
    /// Main 2D world shell matching the design mock:
    /// shared HUD + portrait layer + bottom feature tabs (dialogue | event).
    /// Composition only — no world rules.
    /// </summary>
    public sealed class MainWorldScreen : LuoxiaView
    {
        [Header("Shared HUD")]
        [SerializeField] private LocationDayWidget locationDayWidget;
        [SerializeField] private EventBudgetWidget eventBudgetWidget;
        [SerializeField] private EventBadgeBar eventBadgeBar;
        [SerializeField] private AvatarRailWidget avatarRailWidget;
        [SerializeField] private ScenePortraitLayer scenePortraitLayer;
        [SerializeField] private Button mapButton;
        [SerializeField] private Button endDayButton;

        [Header("Feature tabs")]
        [SerializeField] private Button dialogueTabButton;
        [SerializeField] private Button eventTabButton;
        [SerializeField] private DialogueFeaturePanel dialoguePanel;
        [SerializeField] private EventFeaturePanel eventPanel;
        [SerializeField] private string defaultFeatureId = DialogueFeaturePanel.Id;

        private IPlayerIntentSink _intents;
        private IDialogueSelection _selection;
        private IFeaturePanel[] _panels;
        private string _activeFeatureId;

        public string ActiveFeatureId => _activeFeatureId;

        /// <summary>
        /// Wire pure C# services from app composition root (not Unity singletons).
        /// </summary>
        public void Configure(
            ISessionViewSource session,
            IPlayerIntentSink intents,
            IDialogueSelection selection)
        {
            _intents = intents;
            _selection = selection ?? new DialogueSelection();

            dialoguePanel?.Configure(_intents, _selection);
            eventPanel?.Configure(_intents);
            avatarRailWidget?.Configure(_selection, _intents);
            eventBadgeBar?.Configure(_intents, () => ActivateFeature(EventFeaturePanel.Id));

            _panels = CollectPanels();

            BindSession(session);
            BindChildren(session);

            ActivateFeature(string.IsNullOrEmpty(defaultFeatureId)
                ? DialogueFeaturePanel.Id
                : defaultFeatureId);
        }

        protected override void OnBound()
        {
            if (dialogueTabButton != null)
            {
                dialogueTabButton.onClick.AddListener(() => ActivateFeature(DialogueFeaturePanel.Id));
            }

            if (eventTabButton != null)
            {
                eventTabButton.onClick.AddListener(() => ActivateFeature(EventFeaturePanel.Id));
            }

            if (mapButton != null)
            {
                mapButton.onClick.AddListener(HandleMap);
            }

            if (endDayButton != null)
            {
                endDayButton.onClick.AddListener(HandleEndDay);
            }
        }

        protected override void OnUnbound()
        {
            if (dialogueTabButton != null)
            {
                dialogueTabButton.onClick.RemoveAllListeners();
            }

            if (eventTabButton != null)
            {
                eventTabButton.onClick.RemoveAllListeners();
            }

            if (mapButton != null)
            {
                mapButton.onClick.RemoveAllListeners();
            }

            if (endDayButton != null)
            {
                endDayButton.onClick.RemoveAllListeners();
            }
        }

        public override void OnSessionView(SessionViewDto view)
        {
            // Child widgets bind themselves; screen may react to phase later.
            if (view?.day_cycle != null && endDayButton != null)
            {
                endDayButton.interactable = view.day_cycle.PhaseEnum == DayPhase.Player;
            }
        }

        public void ActivateFeature(string featureId)
        {
            _activeFeatureId = featureId;
            if (_panels == null)
            {
                return;
            }

            for (var i = 0; i < _panels.Length; i++)
            {
                var panel = _panels[i];
                if (panel == null)
                {
                    continue;
                }

                var active = panel.FeatureId == featureId ||
                             (featureId == DialogueFeaturePanel.Id && panel is DialogueFeaturePanel) ||
                             (featureId == EventFeaturePanel.Id && panel is EventFeaturePanel);
                panel.SetActiveFeature(active);
            }
        }

        private void BindChildren(ISessionViewSource session)
        {
            BindChild(locationDayWidget, session);
            BindChild(eventBudgetWidget, session);
            BindChild(eventBadgeBar, session);
            BindChild(avatarRailWidget, session);
            BindChild(scenePortraitLayer, session);
            BindChild(dialoguePanel, session);
            BindChild(eventPanel, session);
        }

        private static void BindChild(ISessionViewBinder binder, ISessionViewSource session)
        {
            binder?.BindSession(session);
        }

        private IFeaturePanel[] CollectPanels()
        {
            return new IFeaturePanel[]
            {
                dialoguePanel,
                eventPanel
            };
        }

        private void HandleMap()
        {
            _intents?.TryOpenMap();
        }

        private void HandleEndDay()
        {
            _intents?.TryEndPlayerDay();
        }
    }
}
