namespace CanfarDesktop.Helpers;

/// <summary>
/// A folder as the Storage screen addresses it: relative to the person's home.
///
/// <para>The screen browses one home. Agents meet VOSpace paths in two spellings — relative
/// (<c>.verbinal/exec</c>) from the screen and the compute contract, absolute (<c>/alice/data</c>)
/// from list_vospace_path — and both should land in the same place.</para>
/// </summary>
public static class StorageFolder
{
    /// <summary>
    /// The folder relative to <paramref name="username"/>'s home, or null when the path lies outside
    /// it — somebody else's home, a project area, or a path that climbs with <c>..</c>. Empty is the
    /// home itself.
    /// </summary>
    public static string? RelativeToHome(string? path, string username)
    {
        var p = (path ?? string.Empty).Trim().Replace('\\', '/');

        if (p.StartsWith('/'))
        {
            var rest = p.TrimStart('/');
            if (string.IsNullOrEmpty(username)) return null;
            if (rest == username || rest == username + "/") return string.Empty;
            if (!rest.StartsWith(username + "/", StringComparison.Ordinal)) return null;
            p = rest[(username.Length + 1)..];
        }

        var segments = p.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s == "..")) return null;
        return string.Join('/', segments.Where(s => s != "."));
    }
}
