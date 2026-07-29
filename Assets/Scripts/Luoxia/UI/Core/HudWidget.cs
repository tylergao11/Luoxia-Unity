using Luoxia.Contracts;

namespace Luoxia.UI.Core
{
    /// <summary>
    /// Top/side HUD widget that only paints a slice of SessionView.
    /// </summary>
    public abstract class HudWidget : LuoxiaView
    {
        public sealed override void OnSessionView(SessionViewDto view)
        {
            if (view == null)
            {
                return;
            }

            Paint(view);
        }

        protected abstract void Paint(SessionViewDto view);
    }
}
