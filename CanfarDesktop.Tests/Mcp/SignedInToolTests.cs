using System.Text.RegularExpressions;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;
using CanfarDesktop.Models.AICompute;
using CanfarDesktop.Tests.Helpers;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// Remote compute is locked until the person signs in, like Portal and Storage — to an agent as to them.
/// Its history and state are answered from this machine, where nothing on the platform would refuse a
/// signed-out caller, so the lock has to be the app's own.
/// </summary>
public class SignedInToolTests
{
    private const string Detail = "sign in first";

    private static McpToolContext Ctx()
        => McpToolContext.ForExternal("c1", Guid.NewGuid(), new InMemoryProposalStore(), new ProposalBudget());

    private static ListComputeRunsTool History(Action read)
        => new(() => { read(); return Array.Empty<ComputeRun>(); });

    [Fact]
    public async Task SignedOutItSaysSoWithoutRunning()
    {
        var read = false;
        var tool = new SignedInTool(History(() => read = true), () => false, Detail);

        var result = await tool.InvokeAsync(JsonValue.Parse("{}"), Ctx(), default);

        var reason = Assert.IsType<AuthRequired>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Equal(Detail, reason.Detail);
        Assert.False(read);
    }

    [Fact]
    public async Task SignedInItIsTheToolItWraps()
    {
        var read = false;
        var tool = new SignedInTool(History(() => read = true), () => true, Detail);

        var result = await tool.InvokeAsync(JsonValue.Parse("{}"), Ctx(), default);

        Assert.IsNotType<FailedResult>(result);
        Assert.True(read);
    }

    /// <summary>Asked at each call, not when the tools are built: signing in or out takes effect at once.</summary>
    [Fact]
    public async Task SigningInOpensItWithoutRebuildingTheTools()
    {
        var signedIn = false;
        var tool = new SignedInTool(History(() => { }), () => signedIn, Detail);

        Assert.IsType<FailedResult>(await tool.InvokeAsync(JsonValue.Parse("{}"), Ctx(), default));
        signedIn = true;
        Assert.IsNotType<FailedResult>(await tool.InvokeAsync(JsonValue.Parse("{}"), Ctx(), default));
    }

    /// <summary>
    /// The same tool to everything around it: its name, schema and gating in the manifest, and a write
    /// still proposes the kind its applier applies.
    /// </summary>
    [Fact]
    public async Task AWrappedWriteKeepsItsNameVerbAndProposal()
    {
        var inner = new StopComputeTool();
        var tool = new SignedInTool(inner, () => true, Detail);

        Assert.Same(inner.Descriptor, tool.Descriptor);
        Assert.Equal(McpVerbClass.Destructive, tool.VerbClass);
        Assert.Equal(inner.AgentSafe, tool.AgentSafe);

        var proposed = Assert.IsType<ProposedResult>(await tool.InvokeAsync(JsonValue.Parse("{}"), Ctx(), default));
        Assert.Equal("stop_compute", proposed.Proposal.Kind);
    }

    /// <summary>Every compute tool is registered locked — one left out would answer a signed-out agent.</summary>
    [Theory]
    [InlineData(nameof(RunCodeTool))]
    [InlineData(nameof(RunCodeOutputTool))]
    [InlineData(nameof(StartComputeTool))]
    [InlineData(nameof(StopComputeTool))]
    [InlineData(nameof(GetComputeStateTool))]
    [InlineData(nameof(ListComputeRunsTool))]
    [InlineData(nameof(GetComputeViewTool))]
    public void TheCatalogRegistersEveryComputeToolLocked(string tool)
    {
        var catalog = File.ReadAllText(RepoFiles.PathTo("Mcp/McpToolCatalog.cs"));

        var registered = Regex.Matches(catalog, $@"\bnew {tool}\(").Count;
        var locked = Regex.Matches(catalog, $@"\bCompute\(new {tool}\(").Count;

        Assert.True(registered > 0, $"{tool} is not registered");
        Assert.Equal(registered, locked);
    }
}
