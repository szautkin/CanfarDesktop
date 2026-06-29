using CanfarDesktop.Services.AiGuide;

namespace CanfarDesktop.Mcp;

/// <summary>
/// Builds the AI Guide's view of the agent-safe tool surface: each built-in tool's name + default
/// description + category id. Reuses <see cref="McpToolCatalog"/> so the descriptions stay the single
/// source of truth, and works whether or not the MCP server is running (no router needed). The app
/// version never affects a descriptor string, so a placeholder is passed here.
/// </summary>
public sealed class AiGuideToolInventory
{
    private readonly IServiceProvider _services;

    public AiGuideToolInventory(IServiceProvider services) => _services = services;

    public IReadOnlyList<AiGuideToolInput> BuildInputs()
        => McpToolCatalog.Build(_services, appVersion: string.Empty)
            .Where(t => t.AgentSafe)
            .Select(t => new AiGuideToolInput(
                t.Descriptor.Name,
                t.Descriptor.Description,
                AiGuideCatalog.CategoryIdForTool(t.Descriptor.Name)))
            .OrderBy(i => i.Name, StringComparer.Ordinal)
            .ToList();
}
