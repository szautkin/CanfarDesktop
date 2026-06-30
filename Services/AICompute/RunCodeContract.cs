namespace CanfarDesktop.Services.AICompute;

/// <summary>
/// The file-drop RPC contract shared with the external <c>verbinal-execution</c> watcher container.
/// These literals MUST match the watcher byte-for-byte (and the macOS <c>RunCodeContract</c>): the app
/// launches one reusable <c>contributed</c> session named <see cref="SessionName"/>, PUTs a request to
/// <see cref="InboxDir"/> on the shared /arc home, and polls <see cref="OutDir"/> for the result.
/// Pure — no I/O — so it is unit-testable.
/// </summary>
public static class RunCodeContract
{
    public const string SessionName = "verbinal-compute";
    public const string SessionType = "contributed";
    public const string InboxDir = ".verbinal/exec/inbox";
    public const string OutDir = ".verbinal/exec/out";

    /// <summary>Bounded read of the result file (the watcher caps output at this size).</summary>
    public const int MaxResultBytes = 1024 * 1024;

    public static readonly string[] Languages = { "python", "bash" };

    public const int DefaultTimeoutSeconds = 60;
    public const int MaxTimeoutSeconds = 900;
    public const int DefaultCores = 1;
    public const int DefaultRam = 1;
    public const int MaxCores = 64;
    public const int MaxRam = 256;

    private static readonly char[] UnsafeIdChars = { '/', ':', '\\', '?', '*', '<', '>', '|', '"' };

    public static int ClampTimeout(int seconds) => Math.Clamp(seconds <= 0 ? DefaultTimeoutSeconds : seconds, 1, MaxTimeoutSeconds);
    public static int ClampCores(int cores) => Math.Clamp(cores <= 0 ? DefaultCores : cores, 1, MaxCores);
    public static int ClampRam(int ram) => Math.Clamp(ram <= 0 ? DefaultRam : ram, 1, MaxRam);

    /// <summary>Normalize the requested language to a supported one (default <c>python</c>).</summary>
    public static string NormalizeLanguage(string? language)
    {
        var l = (language ?? string.Empty).Trim().ToLowerInvariant();
        return Array.IndexOf(Languages, l) >= 0 ? l : "python";
    }

    /// <summary>Replace filesystem-unsafe characters in an execution id so it is a valid file name.</summary>
    public static string SanitizeId(string id)
        => new(id.Select(c => Array.IndexOf(UnsafeIdChars, c) >= 0 ? '_' : c).ToArray());

    /// <summary>VOSpace path of the request file: <c>&lt;username&gt;/.verbinal/exec/inbox/&lt;id&gt;.json</c>.</summary>
    public static string InboxPath(string username, string id) => $"{username}/{InboxDir}/{SanitizeId(id)}.json";

    /// <summary>VOSpace path of the result file: <c>&lt;username&gt;/.verbinal/exec/out/&lt;id&gt;.json</c>.</summary>
    public static string OutPath(string username, string id) => $"{username}/{OutDir}/{SanitizeId(id)}.json";

    /// <summary>The inbox folder tree to create one level at a time (CreateFolderAsync rejects '/').</summary>
    public static IReadOnlyList<string> InboxTreeLevels => new[] { ".verbinal", ".verbinal/exec", ".verbinal/exec/inbox" };
    public static IReadOnlyList<string> OutTreeLevels => new[] { ".verbinal", ".verbinal/exec", ".verbinal/exec/out" };
}
