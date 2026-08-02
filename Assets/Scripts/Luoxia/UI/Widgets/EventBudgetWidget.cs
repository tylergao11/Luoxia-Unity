using Luoxia.Contracts;
using Luoxia.UI.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Widgets
{
    /// <summary>
    /// AP / event budget bar. Values only from SessionView.event_budget.
    /// daily_capacity and card costs are pack-owned (ContentBundle.event_budget);
    /// Host never hardcodes capacity or spend tables — only projects the view.
    /// </summary>
    public sealed class EventBudgetWidget : HudWidget
    {
        [SerializeField] private Text budgetText;
        [SerializeField] private Slider budgetSlider;
        [SerializeField] private Image fillImage;
        [SerializeField] private string format = "AP {0}/{1}";

        protected override void Paint(SessionViewDto view)
        {
            var budget = view.event_budget;
            if (budget == null)
            {
                if (budgetText != null)
                {
                    // No SessionView.event_budget yet — never invent pack capacity.
                    budgetText.text = "—";
                }

                if (budgetSlider != null)
                {
                    budgetSlider.minValue = 0f;
                    budgetSlider.maxValue = 1f;
                    budgetSlider.value = 0f;
                }

                return;
            }

            if (budgetText != null)
            {
                budgetText.text = string.Format(format, budget.remaining, budget.capacity);
            }

            if (budgetSlider != null)
            {
                budgetSlider.minValue = 0f;
                budgetSlider.maxValue = Mathf.Max(1, budget.capacity);
                budgetSlider.value = budget.remaining;
            }
        }
    }
}
