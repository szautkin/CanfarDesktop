namespace CanfarDesktop.Helpers;

/// <summary>
/// Thin wrapper over the MRT Core <see cref="Microsoft.Windows.ApplicationModel.Resources.ResourceLoader"/>
/// (WinAppSDK — not the UWP Windows.ApplicationModel.Resources one) for code-behind lookups into
/// Strings/&lt;locale&gt;/Resources.resw. XAML surfaces use x:Uid instead; code uses <see cref="T"/> /
/// <see cref="F"/> with plain keys (no dots).
///
/// Lookup failures return the key itself: unpackaged/dev runs may have no reachable resources.pri, and
/// the UI must never crash over a missing string. The language follows
/// <c>Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride</c>, applied at startup in App.
/// </summary>
public static class Loc
{
    private static Microsoft.Windows.ApplicationModel.Resources.ResourceLoader? _loader;

    /// <summary>
    /// How many times building the loader may fail before we stop trying.
    ///
    /// <para>It used to give up after ONE failure, for good, and that is how an app with perfectly
    /// good resources ended up showing raw keys everywhere. The first call happens during startup —
    /// the summary and guide hooks fire while services are still being built — and a loader that is
    /// not ready yet threw once and latched the whole session. Every string from code-behind then
    /// rendered as its key while XAML's own x:Uid lookups, which do not go through here, kept
    /// working; that split is exactly what made it look like a handful of missing strings rather than
    /// one broken loader.</para>
    ///
    /// <para>A few retries carry it past startup. Latching eventually still matters: a genuinely
    /// unpackaged run has nothing to find, and throwing on every label would be its own problem.</para>
    /// </summary>
    private const int MaxAttempts = 5;

    private static int _attempts;

    /// <summary>The loader, built on first need and retried a few times before giving up.</summary>
    private static Microsoft.Windows.ApplicationModel.Resources.ResourceLoader? Loader()
    {
        if (_loader is not null) return _loader;
        if (_attempts >= MaxAttempts) return null;

        _attempts++;

        // The default constructor is right for a packaged app. Unpackaged it cannot find the file on
        // its own, and the explicit path is the documented way to say where it is — so the second
        // attempt is not a retry of the same thing, it is the other way of asking.
        try { return _loader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader(); }
        catch { /* fall through to the explicit path */ }

        try
        {
            return _loader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader(
                Microsoft.Windows.ApplicationModel.Resources.ResourceLoader.GetDefaultResourceFilePath());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Localized string for <paramref name="key"/>, or the key itself if lookup fails.</summary>
    public static string T(string key)
    {
        try
        {
            // A missing key returns empty rather than throwing, so the key falls through as its own
            // fallback and only a broken loader costs an exception.
            var value = Loader()?.GetString(key);
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch
        {
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
