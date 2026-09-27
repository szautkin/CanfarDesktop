using System.Text.Json;
using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// Settings, opened at a section so an agent can guide the person through it with point_at_ui. The
/// tools show and close; nothing in them sets anything.
/// </summary>
public class SettingsViewToolsTests
{
    private static McpToolContext Ctx() => McpToolContext.ForExternal("c1", Guid.NewGuid());

    private static T Payload<T>(ToolResult result)
        => JsonSerializer.Deserialize<T>(Assert.IsType<DataResult>(result).Json, McpJson.Options)!;

    private static (OpenSettingsTool Tool, List<string?> Asked) Open()
    {
        var asked = new List<string?>();
        return (new OpenSettingsTool(section =>
        {
            asked.Add(section);
            return Task.FromResult(new SettingsShown(true, section ?? "general"));
        }), asked);
    }

    /// <summary>By its id, or by the title the person sees in the list.</summary>
    [Theory]
    [InlineData("agent", "agent")]
    [InlineData("AI agent", "agent")]
    [InlineData("  Compute ", "compute")]
    [InlineData("Image discovery", "discovery")]
    public async Task ASectionIsNamedByIdOrTitle(string asked, string opened)
    {
        var (tool, calls) = Open();

        var result = Payload<OpenSettingsTool.Result>(
            await tool.InvokeAsync(JsonValue.Parse($$"""{"section":"{{asked}}"}"""), Ctx(), default));

        Assert.Equal(opened, Assert.Single(calls));
        Assert.True(result.Open);
        Assert.Equal(opened, result.Section);
    }

    /// <summary>No section: the one showing if Settings is open, or General — the dialog decides.</summary>
    [Fact]
    public async Task WithoutASectionTheDialogChooses()
    {
        var (tool, calls) = Open();

        await tool.InvokeAsync(JsonValue.Parse("{}"), Ctx(), default);

        Assert.Null(Assert.Single(calls));
    }

    [Fact]
    public async Task ASectionThereIsNotIsRefusedWithTheOnesThereAre()
    {
        var (tool, calls) = Open();

        var result = await tool.InvokeAsync(JsonValue.Parse("""{"section":"privacy"}"""), Ctx(), default);

        var reason = Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Contains("general, portal, agent", reason.Description);
        Assert.Empty(calls);
    }

    /// <summary>So an agent can choose a section without opening each one to look.</summary>
    [Fact]
    public async Task TheReplySaysWhatEverySectionHolds()
    {
        var (tool, _) = Open();

        var result = Payload<OpenSettingsTool.Result>(await tool.InvokeAsync(JsonValue.Parse("{}"), Ctx(), default));

        Assert.Equal(SettingsSections.All.Select(s => s.Id), result.Sections.Select(s => s.Section));
        Assert.All(result.Sections, s => Assert.False(string.IsNullOrWhiteSpace(s.Holds)));
    }

    [Fact]
    public void TheSchemaOffersExactlyTheSections()
    {
        var schema = new OpenSettingsTool(_ => Task.FromResult(new SettingsShown(true, null))).Descriptor.InputSchema;
        var offered = Assert.IsType<JsonArray>(schema["properties"]?["section"]?["enum"]).Items
            .Select(i => Assert.IsType<JsonString>(i).Value);

        Assert.Equal(SettingsSections.All.Select(s => s.Id), offered);
    }

    /// <summary>What the dialog reports back — "another dialog is open", say — reaches the agent as it is.</summary>
    [Fact]
    public async Task WhyItDidNotOpenIsPassedOn()
    {
        var tool = new OpenSettingsTool(_ => Task.FromResult(new SettingsShown(false, null, "another dialog is open")));

        var result = Payload<OpenSettingsTool.Result>(await tool.InvokeAsync(JsonValue.Parse("{}"), Ctx(), default));

        Assert.False(result.Open);
        Assert.Equal("another dialog is open", result.Message);
    }

    [Fact]
    public async Task CloseClosesIt()
    {
        var closed = false;
        var tool = new CloseSettingsTool(() => { closed = true; return Task.FromResult(new SettingsShown(false, null)); });

        var result = Payload<SettingsShown>(await tool.InvokeAsync(JsonValue.Parse("{}"), Ctx(), default));

        Assert.True(closed);
        Assert.False(result.Open);
    }

    /// <summary>Showing is not setting: both are live view changes an agent may make, like navigate_to.</summary>
    [Fact]
    public void BothShowAndNeitherSets()
    {
        IMcpTool[] tools =
        [
            new OpenSettingsTool(_ => Task.FromResult(new SettingsShown(true, null))),
            new CloseSettingsTool(() => Task.FromResult(new SettingsShown(false, null))),
        ];

        Assert.All(tools, t =>
        {
            Assert.Equal(McpVerbClass.ViewState, t.VerbClass);
            Assert.True(t.AgentSafe);
        });
    }
}
