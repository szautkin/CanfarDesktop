namespace CanfarDesktop.Mcp.Listener;

/// <summary>
/// The sidecar file the packaged app writes the live pipe NAME to, at a NON-virtualized absolute path
/// (<c>%LOCALAPPDATA%\Verbinal\mcp.pipe-name</c>) so the unpackaged bridge process can read it. Atomic
/// (temp + replace). The directory is injectable so the path/serialization logic is unit-testable.
/// </summary>
public sealed class McpSidecar
{
    public string FilePath { get; }

    public McpSidecar(string? directory = null)
        => FilePath = Path.Combine(directory ?? DefaultDirectory(), McpConstants.SidecarFileName);

    public static string DefaultDirectory()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), McpConstants.SidecarFolderName);

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
