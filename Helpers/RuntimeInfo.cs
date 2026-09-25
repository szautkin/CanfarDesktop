using System.Reflection;
using System.Runtime.InteropServices;

namespace CanfarDesktop.Helpers;

/// <summary>Where to send someone who wants the project, a bug report, or help.</summary>
public static class AppLinks
{
    /// <summary>
    /// The APP's home, not the service's.
    ///
    /// About used to point at canfar.net, which is the observatory this talks to — so the one link
    /// offered to somebody asking "what is this program?" answered a different question.
    /// </summary>
    public const string Website = "https://verbinal.com";

    /// <summary>Where a bug goes. This repository's tracker, not the sibling platform's.</summary>
    public const string Issues = "https://github.com/szautkin/CanfarDesktop/issues";

    public const string Support = "mailto:support@verbinal.com";
}

/// <summary>One fact about the machine, as a bug report would want it.</summary>
public sealed record RuntimeFact(string Name, string Value);

/// <summary>
/// What was actually running, read at RUN time.
///
/// The point is bug reports. A version block that names only things fixed at build time tells a reader
/// nothing that varies between the machines where a bug appears — the sibling app's About said
/// "Rust {version}", which was the app's own version wearing the toolchain's label, and named its UI
/// toolkit with no version at all.
///
/// So: the app, the framework, the OS build, the architecture, and whether this is a packaged install —
/// each read from its own source, and each rendered as text somebody can paste.
/// </summary>
public static class RuntimeInfo
{
    /// <summary>
    /// The facts, or a stand-in for each one that cannot be read.
    ///
    /// Nothing here throws and nothing here is omitted on failure: a missing line in a bug report reads
    /// as an answer ("no GPU?") rather than as a gap, so an unreadable value says so in words.
    /// </summary>
    public static IReadOnlyList<RuntimeFact> Facts() =>
    [
        new("App", AppVersion()),
        new("Framework", Framework()),
        new("Windows App SDK", WindowsAppSdk()),
        new("OS", OperatingSystem()),
        new("Architecture", Architecture()),
        new("Install", Packaged() ? "packaged (MSIX)" : "unpackaged"),
    ];

    /// <summary>
    /// The facts as one block of text, for the Copy button.
    ///
    /// A bug report gets pasted, not retyped, and a reader who has to retype six lines writes down
    /// four of them.
    /// </summary>
    public static string AsText(IReadOnlyList<RuntimeFact> facts)
        => string.Join(Environment.NewLine, facts.Select(f => $"{f.Name}: {f.Value}"));

    /// <summary>
    /// The app's own version — the packaged identity where there is one, the assembly's otherwise.
    ///
    /// Deliberately its own function, and deliberately not reused for anything else on this page: the
    /// whole failure being fixed here was one version standing in for another.
    /// </summary>
    public static string AppVersion()
    {
        try
        {
            var v = Windows.ApplicationModel.Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
        }
        catch
        {
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        }
    }

    /// <summary>The .NET that is running, e.g. ".NET 8.0.11".</summary>
    public static string Framework()
    {
        try { return RuntimeInformation.FrameworkDescription; }
        catch { return "unknown"; }
    }

    /// <summary>
    /// The Windows App SDK actually loaded.
    ///
    /// Found by assembly name rather than by referencing the type, so this file stays free of a WinUI
    /// dependency and can be tested without one. Its version is the single most useful line in a WinUI
    /// bug report, because a runtime mismatch is the most common cause of one.
    /// </summary>
    public static string WindowsAppSdk()
    {
        try
        {
            var winui = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Microsoft.WinUI");

            return winui?.GetName().Version?.ToString() ?? "not loaded";
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>The Windows build. The build number is what a bug is reproduced against.</summary>
    public static string OperatingSystem()
    {
        try { return RuntimeInformation.OSDescription.Trim(); }
        catch { return "unknown"; }
    }

    /// <summary>
    /// The process architecture, and the machine's when they differ.
    ///
    /// x64 on an Arm64 machine is emulation, which changes what a graphics or interop bug means — and
    /// it is invisible from anywhere else in a report.
    /// </summary>
    public static string Architecture()
    {
        try
        {
            var process = RuntimeInformation.ProcessArchitecture;
            var machine = RuntimeInformation.OSArchitecture;

            return process == machine ? process.ToString() : $"{process} on {machine}";
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// Whether this is an MSIX install.
    ///
    /// Half the paths in this app branch on it — resource loading, the settings store, where the MCP
    /// bridge is found — so a report that does not say which is a report missing its first question.
    /// </summary>
    public static bool Packaged()
    {
        try { return Windows.ApplicationModel.Package.Current is not null; }
        catch { return false; }
    }
}
