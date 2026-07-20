namespace CanfarDesktop.Mcp.Listener;

/// <summary>
/// A diagnostic breadcrumb: the app writes the live pipe NAME here so a human (or external tooling)
/// can see what was advertised. The bridge does NOT need it — both sides compute the same
/// deterministic per-user pipe name (<see cref="McpPipeName"/>). The default directory is
/// <see cref="Helpers.PackagePaths.WritableInteropRoot"/>, where a packaged app's writes land at
/// their literal on-disk path (a write to the real %LOCALAPPDATA% would be silently CoW-sandboxed by
/// MSIX virtualization). Atomic (temp + replace). The directory is injectable so the
/// path/serialization logic is unit-testable.
/// </summary>
public sealed class McpSidecar
{
    public string FilePath { get; }

    public McpSidecar(string? directory = null)
        => FilePath = Path.Combine(directory ?? DefaultDirectory(), McpConstants.SidecarFileName);

    public static string DefaultDirectory()
        => Path.Combine(Helpers.PackagePaths.WritableInteropRoot(), McpConstants.SidecarFolderName);

    /// <summary>Atomically write the current pipe name (temp file + replace).</summary>
    public void Write(string pipeName)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, pipeName);
        if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
        else File.Move(tmp, FilePath);
    }

    /// <summary>The pipe name the app last advertised, or null when absent/unreadable.</summary>
    public string? Read()
    {
        try
        {
            return File.Exists(FilePath) ? File.ReadAllText(FilePath).Trim() is { Length: > 0 } s ? s : null : null;
        }
        catch
        {
            return null;
        }
    }

    public void Delete()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch { /* best effort */ }
    }
}
