using Luoxia.Contracts;
using Luoxia.UI.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Widgets
{
    /// <summary>
    /// Mid-screen narrative layer driven by RenderNode scene/portrait.
    /// Asset bytes resolved later via content_hash; until then uses fallback sprites.
    /// </summary>
    public sealed class ScenePortraitLayer : HudWidget
    {
        [SerializeField] private Image sceneImage;
        [SerializeField] private Image portraitImage;
        [SerializeField] private Sprite fallbackScene;
        [SerializeField] private Sprite fallbackPortrait;

        protected override void Paint(SessionViewDto view)
        {
            var hasScene = false;
            var hasPortrait = false;

            if (view.render_nodes != null)
            {
                for (var i = 0; i < view.render_nodes.Count; i++)
                {
                    var node = view.render_nodes[i];
                    if (node.KindEnum == RenderNodeKind.Scene && !hasScene)
                    {
                        hasScene = true;
                        // TODO: resolve node.asset.content_hash via asset store
                    }
                    else if (node.KindEnum == RenderNodeKind.Portrait && !hasPortrait)
                    {
                        hasPortrait = true;
                    }
                }
            }

            if (sceneImage != null)
            {
                sceneImage.sprite = fallbackScene;
                sceneImage.enabled = sceneImage.sprite != null;
            }

            if (portraitImage != null)
            {
                portraitImage.sprite = fallbackPortrait;
                portraitImage.enabled = portraitImage.sprite != null;
            }
        }
    }
}
