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
                // day_cycle.day is the only advancing time fact. world_time.calendar_label
                // is a one-shot opening label under the closed clock.advance contract
                // (no label field), so it must not be shown as "current" time.
                var day = view.day_cycle != null ? view.day_cycle.day : 0;
                dayTimeText.text = day > 0 ? $"第{ToCjkNumeral(day)}日" : string.Empty;
            }
        }

        private static string ToCjkNumeral(int value)
        {
            if (value <= 0 || value > 99)
            {
                return value.ToString();
            }

            const string digits = "零一二三四五六七八九";
            if (value < 10)
            {
                return digits[value].ToString();
            }

            var tens = value / 10;
            var ones = value % 10;
            var prefix = tens == 1 ? "十" : $"{digits[tens]}十";
            return ones == 0 ? prefix : $"{prefix}{digits[ones]}";
        }
    }
}
