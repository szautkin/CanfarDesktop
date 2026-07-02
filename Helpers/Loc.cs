namespace CanfarDesktop.Helpers;

/// <summary>
/// Thin wrapper over the MRT Core <see cref="Microsoft.Windows.ApplicationModel.Resources.ResourceLoader"/>
/// (WinAppSDK — not the UWP Windows.ApplicationModel.Resources one) for code-behind lookups into
/// Strings/&lt;locale&gt;/Resources.resw. XAML surfaces use x:Uid instead; code uses <see cref="T"/> /
/// <see cref="F"/> with plain keys (no dots).
///
/// Lookup failures return the key itself: unpackaged/dev runs have no resources.pri, and the UI must
/// never crash over a missing string. The language follows
/// <c>Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride</c>, applied at startup in App.
/// </summary>
public static class Loc
{
    private static Microsoft.Windows.ApplicationModel.Resources.ResourceLoader? _loader;
    private static bool _unavailable;

    /// <summary>Localized string for <paramref name="key"/>, or the key itself if lookup fails.</summary>
    public static string T(string key)
    {
        if (_unavailable) return key;
        try
        {
            _loader ??= new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
            var value = _loader.GetString(key);
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch
        {
            // No resources.pri (unpackaged/dev) or MRT init failure — fall back to keys for good.
            _unavailable = true;
            return key;
        }
    }

    /// <summary>Localized format string for <paramref name="key"/> applied to <paramref name="args"/>.</summary>
    public static string F(string key, params object?[] args)
    {
        var pattern = T(key);
        try { return string.Format(System.Globalization.CultureInfo.CurrentCulture, pattern, args); }
        catch (FormatException) { return pattern; } // key-as-fallback has no {0} slots
    }
}
