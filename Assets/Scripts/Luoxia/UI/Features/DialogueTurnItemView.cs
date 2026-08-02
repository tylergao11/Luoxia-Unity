using Luoxia.Contracts;
using Luoxia.UI.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Features
{
    public sealed class DialogueTurnItemModel
    {
        public DialogueTurnViewDto Turn;
        public bool IsPlayer;
        public string SpeakerName;
        public Sprite Portrait;
    }

    public sealed class DialogueTurnItemView : ListItemView<DialogueTurnItemModel>
    {
        [SerializeField] private GameObject playerRoot;
        [SerializeField] private GameObject otherRoot;
        [SerializeField] private Text playerNameText;
        [SerializeField] private Text playerBodyText;
        [SerializeField] private Text otherNameText;
        [SerializeField] private Text otherBodyText;
        [SerializeField] private Image playerPortrait;
        [SerializeField] private Image otherPortrait;

        protected override void OnBind(DialogueTurnItemModel model, int index)
        {
            if (playerRoot != null)
            {
                playerRoot.SetActive(model.IsPlayer);
            }

            if (otherRoot != null)
            {
                otherRoot.SetActive(!model.IsPlayer);
            }

            var name = model.SpeakerName ?? string.Empty;
            var body = model.Turn != null ? model.Turn.text : string.Empty;

            if (model.IsPlayer)
            {
                if (playerNameText != null)
                {
                    playerNameText.text = name;
                }

                if (playerBodyText != null)
                {
                    playerBodyText.text = body;
                }

                ApplyPortraitOrKeepChrome(playerPortrait, model.Portrait);
            }
            else
            {
                if (otherNameText != null)
                {
                    otherNameText.text = name;
                }

                if (otherBodyText != null)
                {
                    otherBodyText.text = body;
                }

                ApplyPortraitOrKeepChrome(otherPortrait, model.Portrait);
            }
        }

        private static void ApplyPortraitOrKeepChrome(Image image, Sprite portrait)
        {
            if (image == null)
            {
                return;
            }

            if (portrait != null)
            {
                image.sprite = portrait;
                image.color = Color.white;
                image.enabled = true;
            }
            // No portrait: keep frame chrome already on the Image — do not assign null.
        }
    }
}
