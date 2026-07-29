using Luoxia.UI.Core;
using UnityEngine;
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
    }

    public sealed class AvatarRailItemView : ListItemView<AvatarRailItemModel>
    {
        [SerializeField] private Image portraitImage;
        [SerializeField] private Image selectedFrame;
        [SerializeField] private Image notificationDot;
        [SerializeField] private Text nameText;
        [SerializeField] private Button selectButton;

        private System.Action<DialogueTarget> _onSelected;

        public void SetSelectHandler(System.Action<DialogueTarget> onSelected)
        {
            _onSelected = onSelected;
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
                nameText.text = model.DisplayName ?? string.Empty;
            }

            if (portraitImage != null)
            {
                portraitImage.sprite = model.Portrait;
                portraitImage.enabled = model.Portrait != null;
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
