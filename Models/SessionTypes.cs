namespace CanfarDesktop.Models;

/// <summary>
/// The session types, in one place. The launch form's tabs, the MCP tools that launch and list them and
/// the images card's filter read them from here, rather than each from a copy of its own.
/// </summary>
public static class SessionTypes
{
    /// <summary>What the launch form's Standard tab starts, in its order.</summary>
    public static readonly IReadOnlyList<string> Interactive = ["notebook", "desktop", "carta", "contributed", "firefly"];

    /// <summary>Everything a launch tab starts: the interactive types, and a batch job from Advanced.</summary>
    public static readonly IReadOnlyList<string> Launchable = [.. Interactive, "headless"];

    /// <summary>An application published inside a desktop session — not a session anyone starts on its own.</summary>
    public const string DesktopApp = "desktop-app";

    /// <summary>The type a filter shows this one under: desktop-app with desktop, one idea behind one button.</summary>
    public static string Group(string type)
        => string.Equals(type, DesktopApp, StringComparison.OrdinalIgnoreCase) ? "desktop" : type;

    /// <summary>A type as a person reads it: notebook → Notebook.</summary>
    public static string Label(string type)
        => string.IsNullOrEmpty(type) ? type : char.ToUpperInvariant(type[0]) + type[1..];
}
