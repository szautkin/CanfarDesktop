using Microsoft.UI.Xaml;

namespace CanfarDesktop.Helpers;

/// <summary>Maps the user's saved theme string ("System"/"Light"/"Dark") to an
/// <see cref="ElementTheme"/> and applies it to a window root. Thin UI glue.</summary>
public static class ThemeApplier
{
    public static ElementTheme ToElementTheme(string? theme) => theme?.Trim().ToLowerInvariant() switch
    {
        "light" => ElementTheme.Light,
        "dark" => ElementTheme.Dark,
        _ => ElementTheme.Default, // "System" or unknown follows the OS
    };

    /// <summary>Apply the saved theme to the given window root element (no-op when null).</summary>
    public static void Apply(FrameworkElement? root, string? theme)
    {
        if (root is not null) root.RequestedTheme = ToElementTheme(theme);
    }
}
