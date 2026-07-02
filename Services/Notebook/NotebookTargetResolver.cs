namespace CanfarDesktop.Services.Notebook;

/// <summary>How an MCP notebook selector resolved against the open notebooks.</summary>
public enum NotebookTargetKind
{
    /// <summary>No selector given — the caller should target the active notebook.</summary>
    UseActive,
    /// <summary>The selector matched an open notebook at <c>Index</c>.</summary>
    Resolved,
    /// <summary>A selector was given but no open notebook matched it.</summary>
    NotFound,
}

/// <summary>
/// Pure resolution of an MCP notebook selector (a notebook id or a file path) to an open-notebook
/// index. Lets the agent target a specific open notebook without stealing the user's active tab.
/// Matching order: exact id, then exact full path, then filename — all ordinal-ignore-case.
/// </summary>
public static class NotebookTargetResolver
{
    public readonly record struct Candidate(string Id, string? Path);

    public static (NotebookTargetKind Kind, int Index) Resolve(
        IReadOnlyList<Candidate> candidates, string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector)) return (NotebookTargetKind.UseActive, -1);
        var sel = selector.Trim();

        for (var i = 0; i < candidates.Count; i++)
            if (string.Equals(candidates[i].Id, sel, StringComparison.OrdinalIgnoreCase))
                return (NotebookTargetKind.Resolved, i);

        for (var i = 0; i < candidates.Count; i++)
            if (candidates[i].Path is { Length: > 0 } p && string.Equals(p, sel, StringComparison.OrdinalIgnoreCase))
                return (NotebookTargetKind.Resolved, i);

        for (var i = 0; i < candidates.Count; i++)
            if (candidates[i].Path is { Length: > 0 } p
                && string.Equals(System.IO.Path.GetFileName(p), sel, StringComparison.OrdinalIgnoreCase))
                return (NotebookTargetKind.Resolved, i);

        return (NotebookTargetKind.NotFound, -1);
    }
}
