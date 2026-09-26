namespace CanfarDesktop.Helpers;

/// <summary>
/// Which image a set of marks belongs to: a file, and in a multi-extension FITS file, which extension.
///
/// <para>Marks used to be keyed by path alone. A CFHT MegaPrime frame has forty-one image extensions,
/// so every mark drawn on any of them went into one bucket: the list mixed chips, the cap was shared
/// across all of them, and a pixel mark drawn at (100, 100) on ccd00 reappeared at (100, 100) on
/// ccd39, pointing at nothing it was meant to. A sky mark was merely hidden when its position fell off
/// the chip on screen — still listed, still counted, still exported.</para>
///
/// <para>The key is <c>path#hdu</c>, and it stays a plain string so the store, the editor and the MCP
/// tools go on passing one value around exactly as before. A cube has one image by nature and keeps
/// its bare path; so do marks written before extensions were tracked, which move to the file's first
/// image extension the first time that file is opened.</para>
/// </summary>
public static class MarkTarget
{
    /// <summary>The key for marks on one extension of a file.</summary>
    public static string Key(string path, int hdu) => $"{Canonical(path)}#{hdu}";

    /// <summary>
    /// A file's path, written one way whoever wrote it: one kind of separator, no doubled separators,
    /// no "." or ".." steps. The same file opened from Research as <c>C:\Users\x\m31.fits</c> and by an
    /// agent as <c>C:/Users/x/m31.fits</c> — or with a <c>..\</c> in it — is one file, and its marks
    /// are one set; keyed as written, the agent's reopening found none of them. Case is left as it is:
    /// keys are compared ignoring it (<see cref="Comparer"/>), as the file system compares names.
    /// </summary>
    public static string Canonical(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path ?? string.Empty;
        var p = path.Trim();
        if (!IsWindowsRooted(p))
        {
            // A path this platform reads itself: made full, which also tidies it.
            try { return Path.IsPathRooted(p) || OperatingSystem.IsWindows() ? Path.GetFullPath(p) : p; }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return p; }
        }

        // A Windows path — read by its own rules wherever this runs, so a test on another system reads it
        // as the app does.
        var unc = p.StartsWith(@"\\", StringComparison.Ordinal) || p.StartsWith("//", StringComparison.Ordinal);
        var steps = new List<string>();
        foreach (var step in p.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (step == ".") continue;
            if (step == "..") { if (steps.Count > (unc ? 2 : 1)) steps.RemoveAt(steps.Count - 1); continue; }
            steps.Add(step);
        }
        var joined = string.Join('\\', steps);
        return unc ? @"\\" + joined : steps.Count == 1 ? joined + @"\" : joined;
    }

    /// <summary>A key, its path written the one way (<see cref="Canonical"/>).</summary>
    public static string CanonicalKey(string key)
    {
        var (path, hdu) = Parse(key);
        return hdu is { } h ? Key(path, h) : Canonical(path);
    }

    /// <summary>How keys are compared: as the file system compares names, ignoring case.</summary>
    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>Whether two keys name the same extension of the same file, however either is written.</summary>
    public static bool SameKey(string? a, string? b)
        => a is not null && b is not null && Comparer.Equals(CanonicalKey(a), CanonicalKey(b));

    private static bool IsWindowsRooted(string p)
        => (p.Length >= 3 && char.IsAsciiLetter(p[0]) && p[1] == ':' && p[2] is '\\' or '/')
           || p.StartsWith(@"\\", StringComparison.Ordinal) || p.StartsWith("//", StringComparison.Ordinal);

    /// <summary>
    /// A key's file and extension. The extension is null for a bare path — a cube, or a mark stored
    /// before extensions were tracked.
    ///
    /// <para>Split on the LAST <c>#</c>, and only when everything after it is digits. <c>#</c> is legal
    /// in a Windows file name, so <c>C:\odd#name.fits</c> is a path with no extension, not extension
    /// "name.fits" of a file called <c>C:\odd</c>.</para>
    /// </summary>
    public static (string Path, int? Hdu) Parse(string key)
    {
        if (string.IsNullOrEmpty(key)) return (key ?? string.Empty, null);

        var at = key.LastIndexOf('#');
        if (at <= 0 || at == key.Length - 1) return (key, null);

        var tail = key.AsSpan(at + 1);
        foreach (var c in tail)
            if (!char.IsAsciiDigit(c)) return (key, null);

        return int.TryParse(tail, out var hdu) ? (key[..at], hdu) : (key, null);
    }

    /// <summary>The file a key is about, whatever extension it names.</summary>
    public static string PathOf(string key) => Parse(key).Path;

    /// <summary>
    /// Whether two keys are about the same file. Case-insensitive, like the file system they name — a
    /// path typed by an agent and one read back from a dialog need not agree on case, nor on separators.
    /// </summary>
    public static bool SameFile(string? a, string? b)
        => a is not null && b is not null && Comparer.Equals(Canonical(PathOf(a)), Canonical(PathOf(b)));

    /// <summary>
    /// The key an MCP call means.
    /// </summary>
    /// <param name="given">What the caller named: nothing, a bare path, or a full key.</param>
    /// <param name="hdu">An extension the caller asked for explicitly, if any.</param>
    /// <param name="active">The key of what the viewer is showing, if anything.</param>
    /// <param name="perExtension">
    /// False for the cube viewer, whose files have one image and whose keys are bare paths.
    /// </param>
    /// <remarks>
    /// <para>Nothing named means what is on screen. A bare path naming the file on screen means the
    /// extension on screen — an agent that says "this file" should not have its mark land on a
    /// different chip from the one the person is looking at. A bare path to a file NOT on screen is
    /// kept bare, which is the legacy shape: it lands on that file's first image extension when it is
    /// next opened, which is the only sensible default when nobody said which.</para>
    /// </remarks>
    public static string? Resolve(string? given, int? hdu, string? active, bool perExtension)
    {
        var named = string.IsNullOrWhiteSpace(given) ? null : CanonicalKey(given.Trim());

        if (!perExtension) return named is null ? active : PathOf(named);

        if (hdu is { } asked)
        {
            var file = named ?? active;
            return file is null ? null : Key(PathOf(file), asked);
        }

        if (named is null) return active;
        if (Parse(named).Hdu is not null) return named;
        return SameFile(named, active) ? active : named;
    }
}
