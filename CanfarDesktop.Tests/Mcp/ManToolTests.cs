using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Builtin;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// The map's last step. list_apps, describe_app and search_tools all answer with names and summaries
/// rather than schemas — that IS the saving — so an agent that has found the tool it wants still had to
/// guess its arguments or call it wrong once to be told them.
/// </summary>
public class ManToolTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);
    private static McpToolContext Ctx() => McpToolContext.ForExternal("c1", Guid.NewGuid());

    private static T Payload<T>(ToolResult result)
    {
        var data = Assert.IsType<DataResult>(result);
        return JsonSerializer.Deserialize<T>(data.Json, McpJson.Options)!;
    }

    private static ToolDescriptor Descriptor(string name, string description = "does a thing")
        => ToolDescriptor.WithStaticSchema(name, description,
            """{"type":"object","properties":{"id":{"type":"string"}},"required":["id"],"additionalProperties":false}""");

    private static ManTool Tool(params string[] names)
        => new(() => names.Select(n => Descriptor(n)).ToList());

    // ── The page ─────────────────────────────────────────────────────────────

    /// <summary>The schema comes back VERBATIM — a paraphrase is the thing you would have to call the tool to check.</summary>
    [Fact]
    public async Task Man_ReturnsTheToolsOwnDescriptionAndSchema()
    {
        var tool = Tool("annotate_fits", "open_cube");

        var output = Payload<ManTool.Output>(
            await tool.InvokeAsync(Args("""{"tool":"annotate_fits"}"""), Ctx(), default));

        Assert.True(output.Found);
        Assert.Equal("annotate_fits", output.Tool);
        Assert.Equal("does a thing", output.Description);
        Assert.NotNull(output.InputSchema);
        Assert.Contains("additionalProperties", output.InputSchema!.ToJsonString());
        Assert.Empty(output.DidYouMean);
    }

    /// <summary>The entry says which area the tool lives in, so the next step can be describe_app.</summary>
    [Fact]
    public async Task Man_NamesTheAreaTheToolBelongsTo()
    {
        var output = Payload<ManTool.Output>(
            await Tool("annotate_fits").InvokeAsync(Args("""{"tool":"annotate_fits"}"""), Ctx(), default));

        Assert.False(string.IsNullOrWhiteSpace(output.App));
    }

    /// <summary>An agent reads a name off a heading as often as off a listing.</summary>
    [Fact]
    public async Task Man_MatchesTheNameWhateverTheCase()
    {
        var output = Payload<ManTool.Output>(
            await Tool("annotate_fits").InvokeAsync(Args("""{"tool":"Annotate_FITS"}"""), Ctx(), default));

        Assert.True(output.Found);
        Assert.Equal("annotate_fits", output.Tool);   // the canonical name is what comes back
    }

    // ── Not a name ───────────────────────────────────────────────────────────

    /// <summary>A typo and a half-remembered name are the two ways here, and both want to see what exists.</summary>
    [Fact]
    public async Task Man_UnknownName_OffersTheNearestOnes()
    {
        var tool = Tool("annotate_fits", "annotate_cube", "open_cube", "list_sessions");

        var output = Payload<ManTool.Output>(
            await tool.InvokeAsync(Args("""{"tool":"annotate"}"""), Ctx(), default));

        Assert.False(output.Found);
        Assert.Contains("annotate_fits", output.DidYouMean);
        Assert.Contains("annotate_cube", output.DidYouMean);
        Assert.DoesNotContain("list_sessions", output.DidYouMean);
        Assert.Contains("did you mean", output.Message);
    }

    /// <summary>The same guess made two ways round still finds the tool.</summary>
    [Fact]
    public async Task Man_SharedWord_IsANearMiss()
    {
        var output = Payload<ManTool.Output>(
            await Tool("fits_goto_coordinate").InvokeAsync(Args("""{"tool":"goto_fits"}"""), Ctx(), default));

        Assert.False(output.Found);
        Assert.Contains("fits_goto_coordinate", output.DidYouMean);
    }

    /// <summary>Nothing near it points at the tool that searches by meaning instead of by name.</summary>
    [Fact]
    public async Task Man_NothingNear_PointsAtSearchTools()
    {
        var output = Payload<ManTool.Output>(
            await Tool("list_sessions").InvokeAsync(Args("""{"tool":"zzzzz"}"""), Ctx(), default));

        Assert.False(output.Found);
        Assert.Empty(output.DidYouMean);
        Assert.Contains("search_tools", output.Message);
    }

    /// <summary>A short fragment must not drag in every tool that happens to contain those letters.</summary>
    [Fact]
    public async Task Man_ShortWords_AreNotTreatedAsSharedWords()
    {
        var tool = Tool("get_fits_view", "set_cube_view");

        var output = Payload<ManTool.Output>(
            await tool.InvokeAsync(Args("""{"tool":"to"}"""), Ctx(), default));

        Assert.False(output.Found);
        Assert.Empty(output.DidYouMean);
    }

    [Fact]
    public async Task Man_EmptyName_IsRefused()
    {
        var failure = Assert.IsType<FailedResult>(
            await Tool("a_tool").InvokeAsync(Args("""{"tool":"   "}"""), Ctx(), default));

        Assert.Contains("tool is required", failure.Reason.Description);
    }

    [Fact]
    public void Man_DescriptionSaysWhatTheOtherMapToolsDoNotGiveYou()
    {
        var description = Tool("a_tool").Descriptor.Description;

        Assert.Contains("search_tools", description);
        Assert.Contains("schema", description);
    }
}
