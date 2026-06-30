namespace CanfarDesktop.Mcp.Tools.Proposals;

/// <summary>
/// Decides whether a just-enqueued write proposal may auto-apply. <see cref="McpVerbClass.Destructive"/>
/// writes (deletes, etc.) NEVER auto-apply — even with the user's auto-apply setting ON they always queue
/// for explicit approval, so a prompt-injected or compromised agent can't silently delete data or spin up
/// paid compute. Auto-apply only fast-paths reversible <see cref="McpVerbClass.SemanticWrite"/> proposals.
/// Pure + unit-testable.
/// </summary>
public static class AutoApplyPolicy
{
    public static bool ShouldAutoApply(bool autoApplyEnabled, McpVerbClass verb)
        => autoApplyEnabled && verb != McpVerbClass.Destructive;
}
