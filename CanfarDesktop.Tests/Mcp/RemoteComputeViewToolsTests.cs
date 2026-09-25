using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;
using CanfarDesktop.Models.AICompute;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// The Remote Compute screen and Storage-at-a-folder, for an agent: what a person can do there, an
/// agent can show them.
/// </summary>
public class RemoteComputeViewToolsTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);
    private static McpToolContext Ctx() => McpToolContext.ForExternal("c1", Guid.Empty);

    private static readonly ComputeScreenView Screen =
        new(true, "running", "run", null, new ComputeSnippetView("python", 60, ""));

    private static JsonElement Json(ToolResult result)
        => JsonDocument.Parse(Assert.IsType<DataResult>(result).Json).RootElement;

    [Fact]
    public async Task ShowComputeRun_WithNoIdAsksForTheNewest()
    {
        string? asked = "unset";
        var tool = new ShowComputeRunTool(id => { asked = id; return Task.FromResult(Screen); });

        await tool.InvokeAsync(Args("{}"), Ctx(), default);

        Assert.Null(asked);
    }

    [Fact]
    public async Task ShowComputeRun_PassesTheIdOnTrimmed()
    {
        string? asked = null;
        var tool = new ShowComputeRunTool(id => { asked = id; return Task.FromResult(Screen); });

        await tool.InvokeAsync(Args("""{"executionId":"  abc  "}"""), Ctx(), default);

        Assert.Equal("abc", asked);
    }

    /// <summary>It fills the box; it never runs, so there is nothing to approve and no run is created.</summary>
    [Fact]
    public async Task SetComputeSnippet_FillsTheBoxWithCleanValues()
    {
        ComputeSnippetRequest? got = null;
        var tool = new SetComputeSnippetTool(r => { got = r; return Task.FromResult(Screen); });

        var result = await tool.InvokeAsync(Args("""{"code":"echo hi","language":"BASH","timeoutSeconds":5000}"""), Ctx(), default);

        Assert.IsType<DataResult>(result);
        Assert.Equal("echo hi", got!.Code);
        Assert.Equal("bash", got.Language);
        Assert.Equal(900, got.TimeoutSeconds);   // clamped to what the watcher allows
    }

    [Fact]
    public async Task SetComputeSnippet_DefaultsToPythonForAMinute()
    {
        ComputeSnippetRequest? got = null;
        var tool = new SetComputeSnippetTool(r => { got = r; return Task.FromResult(Screen); });

        await tool.InvokeAsync(Args("""{"code":"print(1)"}"""), Ctx(), default);

        Assert.Equal("python", got!.Language);
        Assert.Equal(60, got.TimeoutSeconds);
    }

    [Fact]
    public async Task SetComputeSnippet_RefusesBlankCode()
    {
        var called = false;
        var tool = new SetComputeSnippetTool(_ => { called = true; return Task.FromResult(Screen); });

        var result = await tool.InvokeAsync(Args("""{"code":"   "}"""), Ctx(), default);

        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
        Assert.False(called);
    }

    [Fact]
    public async Task GetComputeView_ReportsTheScreen()
    {
        var run = ComputeRunDetail.From(new ComputeRun("r1", ComputeRunAuthor.User, "bash", "ls", 30, "2026-09-24T12:00:00Z"));
        var view = Screen with { Tab = "code", SelectedRun = run, Snippet = new ComputeSnippetView("bash", 30, "ls -la") };

        var doc = Json(await new GetComputeViewTool(() => Task.FromResult(view)).InvokeAsync(Args("{}"), Ctx(), default));

        Assert.Equal("code", doc.GetProperty("tab").GetString());
        Assert.Equal("user", doc.GetProperty("selectedRun").GetProperty("author").GetString());
        Assert.Equal("running", doc.GetProperty("selectedRun").GetProperty("status").GetString());   // still out
        Assert.Equal("ls -la", doc.GetProperty("snippet").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ShowStorageFolder_PassesTheFolderOn()
    {
        string? asked = null;
        var tool = new ShowStorageFolderTool(f => { asked = f; return Task.FromResult(new StorageFolderShown(true, f)); });

        await tool.InvokeAsync(Args("""{"folder":".verbinal/exec"}"""), Ctx(), default);

        Assert.Equal(".verbinal/exec", asked);
    }

    [Fact]
    public void TheScreenToolsAreViewStateAndTheReadIsARead()
    {
        Assert.Equal(McpVerbClass.ViewState, new ShowComputeRunTool(_ => Task.FromResult(Screen)).VerbClass);
        Assert.Equal(McpVerbClass.ViewState, new SetComputeSnippetTool(_ => Task.FromResult(Screen)).VerbClass);
        Assert.Equal(McpVerbClass.ViewState, new ShowStorageFolderTool(f => Task.FromResult(new StorageFolderShown(true, f))).VerbClass);
        Assert.Equal(McpVerbClass.Read, new GetComputeViewTool(() => Task.FromResult(Screen)).VerbClass);
    }
}
