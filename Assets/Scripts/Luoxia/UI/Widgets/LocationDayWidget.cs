using Luoxia.Contracts;
using Luoxia.UI.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Widgets
{
    /// <summary>
    /// Left-top: location title + day/time label.
    /// Location only from lore / location-slot render_nodes — never invent place names.
    /// </summary>
    public sealed class LocationDayWidget : HudWidget
    {
        [SerializeField] private Text locationText;
        [SerializeField] private Text dayTimeText;

        protected override void Paint(SessionViewDto view)
        {
            if (locationText != null)
            {
                var label = LoreQuery.ResolveLocationLabel(view);
                locationText.text = label;
                locationText.gameObject.SetActive(!string.IsNullOrEmpty(label));
            }

            if (dayTimeText != null)
            {
                var day = view.day_cycle != null ? view.day_cycle.day : 0;
                var label = view.world_time != null ? view.world_time.calendar_label : string.Empty;
                if (!string.IsNullOrEmpty(label))
                {
                    dayTimeText.text = label;
                }
                else if (day > 0)
                {
                    dayTimeText.text = $"D{day}";
                }
                else
                {
                    dayTimeText.text = string.Empty;
                }
            }
        }
    }
}
