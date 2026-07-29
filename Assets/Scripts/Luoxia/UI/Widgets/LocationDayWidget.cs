using Luoxia.Contracts;
using Luoxia.UI.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Widgets
{
    /// <summary>
    /// Left-top: location title + day/time label (design: 青云宗·外门 / 第38天 午时).
    /// Location name may come from render_nodes text or a future dedicated field; day from day_cycle + world_time.
    /// </summary>
    public sealed class LocationDayWidget : HudWidget
    {
        [SerializeField] private Text locationText;
        [SerializeField] private Text dayTimeText;
        [SerializeField] private string locationFallback = "烟水渡";

        protected override void Paint(SessionViewDto view)
        {
            if (locationText != null)
            {
                locationText.text = ResolveLocationLabel(view);
            }

            if (dayTimeText != null)
            {
                var day = view.day_cycle != null ? view.day_cycle.day : 0;
                var label = view.world_time != null ? view.world_time.calendar_label : string.Empty;
                dayTimeText.text = string.IsNullOrEmpty(label)
                    ? $"第{day}天"
                    : label;
            }
        }

        private string ResolveLocationLabel(SessionViewDto view)
        {
            if (view.render_nodes == null)
            {
                return locationFallback;
            }

            for (var i = 0; i < view.render_nodes.Count; i++)
            {
                var node = view.render_nodes[i];
                if (node.KindEnum == RenderNodeKind.Text &&
                    node.slot_id != null &&
                    node.slot_id.IndexOf("location", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                    node.text != null)
                {
                    return node.text.Resolve();
                }
            }

            return locationFallback;
        }
    }
}
