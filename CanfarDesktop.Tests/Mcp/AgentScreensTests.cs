using Xunit;
using CanfarDesktop.Mcp;
using CanfarDesktop.Services.Workflows;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// Where "Agent is working in …" says an agent is, and where follow-agent-activity takes the person.
/// </summary>
public class AgentScreensTests
{
    /// <summary>
    /// Code an agent runs on somebody's account is shown where it is logged, as it runs. Before this the
    /// compute tools named no screen: the indicator said nothing about where the agent was, and the app
    /// stayed wherever the person happened to be while code ran on their account.
    /// </summary>
    [Theory]
    [InlineData("run_code")]
    [InlineData("run_code_output")]
    [InlineData("start_compute")]
    [InlineData("stop_compute")]
    [InlineData("get_compute_state")]
    [InlineData("list_compute_runs")]
    [InlineData("get_compute_view")]
    public void ComputeWorkHappensOnRemoteCompute(string tool)
        => Assert.Equal("remoteCompute", AgentScreens.For(tool));

    /// <summary>These open their own screen already; following them as well would navigate twice.</summary>
    [Theory]
    [InlineData("navigate_to")]
    [InlineData("open_fits_file")]
    [InlineData("show_compute_run")]
    [InlineData("set_compute_snippet")]
    [InlineData("show_storage_folder")]
    [InlineData("describe_app")]
    public void ToolsThatNavigateThemselvesOrConcernNoScreenMoveNothing(string tool)
        => Assert.Null(AgentScreens.For(tool));

    /// <summary>
    /// A screen name navigate_to does not know moves nothing and names nothing, silently — the indicator
    /// shows no screen and the person is not followed. Every tool the app declares is checked.
    /// </summary>
    [Fact]
    public void EveryScreenNamedIsOneTheAppHas()
    {
        var unknown = McpToolSources.All
            .Select(d => (d.Tool, Screen: AgentScreens.For(d.Tool)))
            .Where(x => x.Screen is not null && !WorkflowFormat.KnownViews.Contains(x.Screen))
            .Select(x => $"{x.Tool} → {x.Screen}")
            .ToList();

        Assert.True(unknown.Count == 0, "these name a screen the app does not have: " + string.Join(", ", unknown));
    }
}
