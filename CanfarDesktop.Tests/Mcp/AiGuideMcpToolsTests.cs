using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;
using CanfarDesktop.Services.AiGuide;
using CanfarDesktop.Services.Database;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// The agent-facing AI Guide management tools: validation at plan time, and appliers that re-tune the
/// live <see cref="AiGuideService"/> (override descriptions; add/update/delete guide tools).
/// </summary>
public class AiGuideMcpToolsTests : IDisposable
{
    private readonly AppDatabase _db = new(filePath: null);
    private readonly AiGuideService _service;

    public AiGuideMcpToolsTests() => _service = new AiGuideService(new AiGuideStore(_db, deviceId: "t"));
    public void Dispose() => _db.Dispose();

    private static JsonValue Args(string json) => JsonValue.Parse(json);

    private static McpToolContext Ctx()
        => McpToolContext.ForExternal("c1", Guid.NewGuid(), new InMemoryProposalStore(), new ProposalBudget());

    private static PendingProposal ProposalWith<T>(string kind, T payload)
        => PendingProposal.Create("tool", kind, "summary",
            JsonSerializer.SerializeToUtf8Bytes(payload, McpJson.Options), OperationOrigin.External("c1"));

    // ── Verb classes ──────────────────────────────────────────────────────────
    [Fact]
    public void VerbClasses_AreCorrect()
    {
        Assert.Equal(McpVerbClass.SemanticWrite, new SetToolDescriptionTool().VerbClass);
        Assert.Equal(McpVerbClass.SemanticWrite, new AddGuideToolTool().VerbClass);
        Assert.Equal(McpVerbClass.Destructive, new DeleteGuideToolTool().VerbClass);
        Assert.Equal(McpVerbClass.Read, new ListGuideToolsTool(() => _service.Snapshot().Guides).VerbClass);
    }

    // ── set_tool_description ────────────────────────────────────────────────────
    [Fact]
    public async Task SetToolDescription_BuildsProposal()
    {
        var result = await new SetToolDescriptionTool().InvokeAsync(
            Args("""{"toolName":"search_observations","description":"Find images."}"""), Ctx(), default);
        var p = Assert.IsType<ProposedResult>(result).Proposal;
        Assert.Equal("set_tool_description", p.Kind);
        var payload = JsonSerializer.Deserialize<SetToolDescriptionPayload>(p.Payload, McpJson.Options)!;
        Assert.Equal("search_observations", payload.ToolName);
        Assert.Equal("Find images.", payload.Description);
    }

    [Theory]
    [InlineData("""{"toolName":"","description":"x"}""")]
    [InlineData("""{"toolName":"t","description":"  "}""")]
    public async Task SetToolDescription_MissingFields_Invalid(string json)
    {
        var result = await new SetToolDescriptionTool().InvokeAsync(Args(json), Ctx(), default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task SetToolDescription_TooLong_Invalid()
    {
        var json = $$"""{"toolName":"t","description":"{{new string('x', AiGuideService.MaxDescriptionChars + 1)}}"}""";
        var result = await new SetToolDescriptionTool().InvokeAsync(Args(json), Ctx(), default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    // ── add_guide_tool ──────────────────────────────────────────────────────────
    [Fact]
    public async Task AddGuideTool_BuildsProposal()
    {
        var result = await new AddGuideToolTool().InvokeAsync(
            Args("""{"name":"Survey Tips","description":"How to survey","body":"step 1"}"""), Ctx(), default);
        var p = Assert.IsType<ProposedResult>(result).Proposal;
        var payload = JsonSerializer.Deserialize<AddGuideToolPayload>(p.Payload, McpJson.Options)!;
        Assert.Equal("Survey Tips", payload.Name);
        Assert.Equal("How to survey", payload.Description);
        Assert.Equal("step 1", payload.Body);
    }

    [Fact]
    public async Task AddGuideTool_NameWithNoAsciiAlnum_Invalid()
    {
        var result = await new AddGuideToolTool().InvokeAsync(
            Args("""{"name":"界面","description":"d"}"""), Ctx(), default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task DeleteGuideTool_BadId_Invalid()
    {
        var result = await new DeleteGuideToolTool().InvokeAsync(Args("""{"id":"not-a-guid"}"""), Ctx(), default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    // ── Appliers re-tune the live service ───────────────────────────────────────
    [Fact]
    public async Task SetAndClearDescriptionAppliers_UpdateService()
    {
        var set = new SetToolDescriptionApplier(p => { _service.SetOverride(p.ToolName, p.Description); return Task.CompletedTask; });
        await set.ApplyAsync(ProposalWith("set_tool_description", new SetToolDescriptionPayload("get_session", "mine")));
        Assert.True(_service.IsOverridden("get_session"));
        Assert.Equal("mine", _service.EffectiveDescription("get_session", "default"));

        var clear = new ClearToolDescriptionApplier(p => { _service.ClearOverride(p.ToolName); return Task.CompletedTask; });
        await clear.ApplyAsync(ProposalWith("clear_tool_description", new ClearToolDescriptionPayload("get_session")));
        Assert.False(_service.IsOverridden("get_session"));
    }

    [Fact]
    public async Task GuideAppliers_AddUpdateDelete_RoundTrip()
    {
        var add = new AddGuideToolApplier(p => { _service.AddGuide(p.Name, p.Description, p.Body); return Task.CompletedTask; });
        await add.ApplyAsync(ProposalWith("add_guide_tool", new AddGuideToolPayload("My Tool", "desc", "body")));

        var guide = Assert.Single(_service.Snapshot().Guides);
        Assert.Equal("my_tool", guide.Name);

        var update = new UpdateGuideToolApplier(p => { _service.UpdateGuide(Guid.Parse(p.Id), p.Name, p.Description, p.Body); return Task.CompletedTask; });
        await update.ApplyAsync(ProposalWith("update_guide_tool",
            new UpdateGuideToolPayload(guide.Id.ToString(), "Renamed", "desc2", null)));
        Assert.Equal("renamed", _service.Snapshot().Guides.Single().Name);

        var del = new DeleteGuideToolApplier(p => { _service.DeleteGuide(Guid.Parse(p.Id)); return Task.CompletedTask; });
        await del.ApplyAsync(ProposalWith("delete_guide_tool", new DeleteGuideToolPayload(guide.Id.ToString())));
        Assert.Empty(_service.Snapshot().Guides);
    }

    // ── list_guide_tools ────────────────────────────────────────────────────────
    [Fact]
    public async Task ListGuideTools_ReturnsGuides()
    {
        _service.AddGuide("Alpha", "first", "body");
        _service.AddGuide("Beta", "second", null);

        var result = await new ListGuideToolsTool(() => _service.Snapshot().Guides)
            .InvokeAsync(JsonValue.Null, Ctx(), default);
        var doc = JsonDocument.Parse(Assert.IsType<DataResult>(result).Json).RootElement;

        Assert.Equal(2, doc.GetProperty("count").GetInt32());
        var names = doc.GetProperty("guides").EnumerateArray().Select(g => g.GetProperty("name").GetString()).ToList();
        Assert.Contains("alpha", names);
        Assert.Contains("beta", names);
        var alpha = doc.GetProperty("guides").EnumerateArray().First(g => g.GetProperty("name").GetString() == "alpha");
        Assert.True(alpha.GetProperty("hasBody").GetBoolean());
    }
}
