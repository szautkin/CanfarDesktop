using Xunit;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// A tool that is written is a tool that is served.
///
/// <para>The Search page's "Remove from history" and "Clear All" each had a tool behind them — written,
/// unit-tested, given an applier — that nothing ever put on the server. Every test of the tool passed,
/// because every test built it by hand; an agent asked to clear the history had no way to. Every
/// control a person can use is meant to have a tool an agent can use, and a tool left out of the
/// catalogue breaks that as surely as one never written.</para>
/// </summary>
public class McpToolRegistrationTests
{
    [Fact]
    public void EveryDeclaredToolIsPutOnTheServer()
    {
        var app = string.Join("\n", McpToolSources.AppSources().Select(File.ReadAllText));

        var unserved = McpToolSources.All
            .Where(d => d.Class is not null && !app.Contains($"new {d.Class}("))
            .Select(d => $"{d.Tool} ({d.Class} in {d.File})")
            .ToList();

        Assert.True(unserved.Count == 0,
            "these tools are declared but nothing constructs them, so no agent can call them — add them " +
            "to McpToolCatalog: " + string.Join("; ", unserved));
    }

    /// <summary>The guard above would pass by accident if the scan found nothing.</summary>
    [Fact]
    public void TheScanFindsToolsOfEveryShape()
    {
        var tools = McpToolSources.All.Select(d => d.Tool).ToHashSet();

        Assert.True(tools.Count > 150, $"only {tools.Count} tools found — the scan has stopped matching");
        Assert.Contains("describe_app", tools);        // a read tool
        Assert.Contains("point_at_ui", tools);         // a view-state tool
        Assert.Contains("remove_recent_search", tools); // a proposal-backed write tool
        Assert.Contains("vospace_mkdir", tools);       // a macOS-name alias
    }
}
