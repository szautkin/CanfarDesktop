namespace CanfarDesktop.Mcp.Tools.Proposals;

/// <summary>
/// Decides whether a just-enqueued write proposal may auto-apply. <see cref="McpVerbClass.Destructive"/>
/// writes (deletes, session teardown, etc.) NEVER auto-apply — even with the user's auto-apply setting ON
/// they always queue for explicit approval, so a prompt-injected or compromised agent can't silently
/// destroy data. Auto-apply only fast-paths reversible <see cref="McpVerbClass.SemanticWrite"/> proposals.
/// Pure + unit-testable.
/// </summary>
public static class AutoApplyPolicy
{
    public static bool ShouldAutoApply(bool autoApplyEnabled, McpVerbClass verb)
        => autoApplyEnabled && verb != McpVerbClass.Destructive;

    /// <summary>
    /// What this policy means for a tool of <paramref name="verb"/>, in the words an agent reads; null for
    /// a tool whose calls are not proposals. Said here, beside the rule, and appended to every tool's
    /// description rather than written into each: the tools used to say it by hand, and those that said
    /// "queues for the user to apply" were wrong whenever auto-apply was on (QA D9).
    /// </summary>
    public static string? Note(McpVerbClass verb) => verb switch
    {
        McpVerbClass.SemanticWrite =>
            "Applies at once when the person has \"Auto-apply agent writes\" on; otherwise it waits in Pending until they apply it.",
        McpVerbClass.Destructive =>
            "A destructive change: it always waits in Pending for the person to approve it, even with auto-apply on.",
        _ => null,
    };

    /// <summary><paramref name="description"/>, ending with the <see cref="Note"/> for <paramref name="verb"/>.</summary>
    public static string Described(string description, McpVerbClass verb)
        => Note(verb) is { } note ? $"{description.TrimEnd()} {note}" : description;
}
