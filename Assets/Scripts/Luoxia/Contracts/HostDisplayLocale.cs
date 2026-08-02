namespace Luoxia.Contracts
{
    /// <summary>
    /// Single Host source for display locale. Must match the locale sent on
    /// dialogue.start / provision player_name — set once from Bootstrap / EditorPrefs.
    /// </summary>
    public static class HostDisplayLocale
    {
        public const string MissingPlaceholder = "[缺失本地化]";

        private static string _preferred = string.Empty;

        public static string Preferred => _preferred;

        public static void SetPreferred(string locale)
        {
            if (string.IsNullOrWhiteSpace(locale))
            {
                throw new System.ArgumentException("Host display locale required", nameof(locale));
            }

            _preferred = locale.Trim();
        }
    }
}
