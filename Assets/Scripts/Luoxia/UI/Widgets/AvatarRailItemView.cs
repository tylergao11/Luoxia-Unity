using Luoxia.UI.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Luoxia.UI.Widgets
{
    public sealed class AvatarRailItemModel
    {
        public DialogueTarget Target;
        public string DisplayName;
        public Sprite Portrait;
        public bool Selected;
        public bool HasNotification;
        public bool CanInspect;
        public string SubjectEntityId;
    }

    public sealed class AvatarRailItemView : ListItemView<AvatarRailItemModel>, IPointerClickHandler
    {
        private static readonly Color EmptySlotColor = new Color(0.22f, 0.2f, 0.24f, 1f);

        [SerializeField] private Image portraitImage;
        [SerializeField] private Image selectedFrame;
        [SerializeField] private Image notificationDot;
        [SerializeField] private Text nameText;
        [SerializeField] private Button selectButton;

        private System.Action<DialogueTarget> _onSelected;
        private System.Action<string> _onInspect;

        public void SetSelectHandler(System.Action<DialogueTarget> onSelected)
        {
            _onSelected = onSelected;
        }

        public void SetInspectHandler(System.Action<string> onInspect)
        {
            _onInspect = onInspect;
        }

        private void Awake()
        {
            if (selectButton != null)
            {
                selectButton.onClick.AddListener(HandleClick);
            }
        }

        private void OnDestroy()
        {
            if (selectButton != null)
            {
                selectButton.onClick.RemoveListener(HandleClick);
            }
        }

        protected override void OnBind(AvatarRailItemModel model, int index)
        {
            if (nameText != null)
            {
                var name = model.DisplayName ?? string.Empty;
                nameText.text = name;
                nameText.gameObject.SetActive(!string.IsNullOrEmpty(name));
            }

            if (portraitImage != null)
            {
                if (model.Portrait != null)
                {
                    portraitImage.sprite = model.Portrait;
                    portraitImage.color = Color.white;
                    portraitImage.enabled = true;
                }
                else
                {
                    portraitImage.sprite = null;
                    portraitImage.color = EmptySlotColor;
                    portraitImage.enabled = true;
                }

                portraitImage.raycastTarget = false;
            }

            if (selectedFrame != null)
            {
                selectedFrame.enabled = model.Selected;
            }

            if (notificationDot != null)
            {
                notificationDot.enabled = model.HasNotification;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!HasModel)
            {
                return;
            }

            // Double-click / right-click inspect when dossier lore exists.
            if ((eventData.clickCount >= 2 || eventData.button == PointerEventData.InputButton.Right) &&
                Model.CanInspect &&
                !string.IsNullOrEmpty(Model.SubjectEntityId))
            {
                _onInspect?.Invoke(Model.SubjectEntityId);
            }
        }

        private void HandleClick()
        {
            if (!HasModel)
            {
                return;
            }

            _onSelected?.Invoke(Model.Target);
        }
    }
}
