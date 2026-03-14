using System.Windows;

namespace Orbital
{
    /// <summary>
    /// Provides typed access to localized strings from the active Strings ResourceDictionary.
    /// Usage: Loc.Get("Str_Save")
    ///        string.Format(Loc.Get("Str_DeleteActionConfirm"), actionName)
    /// </summary>
    public static class Loc
    {
        /// <summary>
        /// Retrieves a localized string by resource key.
        /// Falls back to the key itself if not found — missing strings are immediately visible in UI.
        /// </summary>
        public static string Get(string key)
        {
            try
            {
                if (Application.Current?.TryFindResource(key) is string value)
                    return value;
            }
            catch { /* Defensive: resource system unavailable during shutdown */ }

            return key;
        }
    }
}
