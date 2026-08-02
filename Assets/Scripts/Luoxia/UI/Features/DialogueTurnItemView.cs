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
        private const float MinHeight = 132f;
        private const float HeightChrome = 68f;

        [SerializeField] private GameObject playerRoot;
        [SerializeField] private GameObject otherRoot;
        [SerializeField] private Text playerNameText;
        [SerializeField] private Text playerBodyText;
        [SerializeField] private Text otherNameText;
        [SerializeField] private Text otherBodyText;
        [SerializeField] private Image playerPortrait;
        [SerializeField] private Image otherPortrait;
        [SerializeField] private LayoutElement layoutElement;

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
                ApplyDynamicHeight(playerBodyText, body);
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
                ApplyDynamicHeight(otherBodyText, body);
            }
        }

        private void ApplyDynamicHeight(Text bodyText, string body)
        {
            var le = layoutElement != null ? layoutElement : GetComponent<LayoutElement>();
            if (le == null || bodyText == null)
            {
                return;
            }

            bodyText.text = body ?? string.Empty;
            Canvas.ForceUpdateCanvases();
            var width = bodyText.rectTransform.rect.width;
            if (width < 8f)
            {
                width = 600f;
            }

            var settings = bodyText.GetGenerationSettings(new Vector2(width, 0f));
            settings.generateOutOfBounds = true;
            settings.horizontalOverflow = HorizontalWrapMode.Wrap;
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            var generator = new TextGenerator();
            generator.Populate(bodyText.text, settings);
            var bodyH = generator.GetPreferredHeight(bodyText.text, settings);
            le.minHeight = MinHeight;
            le.preferredHeight = Mathf.Max(MinHeight, HeightChrome + bodyH);
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
