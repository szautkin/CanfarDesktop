using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;
using CanfarDesktop.Services.Notebook;

namespace CanfarDesktop.Tests.Mcp;

public class NotebookToolsTests
{
    private static readonly McpToolContext Ctx = McpToolContext.ForExternal("c1", Guid.Empty);
    private static JsonValue Args(string json) => JsonValue.Parse(json);
    private static JsonElement Json(ToolResult r) => JsonDocument.Parse(Assert.IsType<DataResult>(r).Json).RootElement;

    private static NotebookState SampleState() => new(
        Loaded: true, Title: "nb.ipynb", FilePath: "/x/nb.ipynb", FileMode: "Notebook", IsDirty: false,
        KernelState: "Idle", KernelName: "Python 3", SelectedIndex: 0, CellCount: 1,
        Cells: new[] { new NotebookCellInfo(0, "code", "print(1)", false, 1, 1) });

    // capture the command a mutation tool builds
    private static (T tool, Func<NotebookCommand?> seen) Mut<T>(Func<Func<NotebookCommand, Task<NotebookState?>>, T> make)
    {
        NotebookCommand? captured = null;
        var tool = make(cmd => { captured = cmd; return Task.FromResult<NotebookState?>(SampleState()); });
        return (tool, () => captured);
    }

    // ── read tools ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListNotebooks_ReturnsRefs()
    {
        var tool = new ListNotebooksTool(() => Task.FromResult<IReadOnlyList<NotebookRef>>(
            new[] { new NotebookRef("/x/nb.ipynb", "nb.ipynb", DateTime.UnixEpoch) }));
        var doc = Json(await tool.InvokeAsync(Args("""{}"""), Ctx, default));
        Assert.Equal(1, doc.GetProperty("count").GetInt32());
        Assert.Equal("nb.ipynb", doc.GetProperty("notebooks")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetNotebook_ReturnsState()
    {
        var tool = new GetNotebookTool(() => Task.FromResult<NotebookState?>(SampleState()));
        var doc = Json(await tool.InvokeAsync(Args("""{}"""), Ctx, default));
        Assert.Equal("Idle", doc.GetProperty("kernelState").GetString());
        Assert.Equal(1, doc.GetProperty("cellCount").GetInt32());
        Assert.Equal("print(1)", doc.GetProperty("cells")[0].GetProperty("source").GetString());
    }

    [Fact]
    public async Task GetCellOutput_InvokesClosure_AndValidates()
    {
        int? seen = null;
        var tool = new GetCellOutputTool(i =>
        {
            seen = i;
            return Task.FromResult<NotebookCellOutputs?>(new(i, "code", 2,
                new[] { new NotebookOutputInfo("stream", "hello", false, false, "", "", false, false, false) }));
        });
        var doc = Json(await tool.InvokeAsync(Args("""{"index":3}"""), Ctx, default));
        Assert.Equal(3, seen);
        Assert.Equal("hello", doc.GetProperty("outputs")[0].GetProperty("text").GetString());

        var bad = await tool.InvokeAsync(Args("""{}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(bad).Reason);
    }

    [Fact]
    public async Task GetKernelState_ReturnsInfo()
    {
        var tool = new GetKernelStateTool(() => Task.FromResult(new NotebookKernelInfo("Busy", "Busy", "Python 3")));
        var doc = Json(await tool.InvokeAsync(Args("""{}"""), Ctx, default));
        Assert.Equal("Busy", doc.GetProperty("state").GetString());
    }

    // ── mutation tools → NotebookCommand mapping ────────────────────────────────

    [Fact]
    public async Task OpenNotebook_MapsPath()
    {
        var (tool, seen) = Mut(a => new OpenNotebookTool(a));
        await tool.InvokeAsync(Args("""{"path":"C:\\nb\\a.ipynb"}"""), Ctx, default);
        Assert.Equal(NotebookOp.Open, seen()!.Op);
        Assert.Equal("C:\\nb\\a.ipynb", seen()!.Path);
    }

    [Fact]
    public async Task CreateNotebook_MapsCreate()
    {
        var (tool, seen) = Mut(a => new CreateNotebookTool(a));
        await tool.InvokeAsync(Args("""{}"""), Ctx, default);
        Assert.Equal(NotebookOp.Create, seen()!.Op);
    }

    [Fact]
    public async Task SaveNotebook_MapsOptionalPath()
    {
        var (tool, seen) = Mut(a => new SaveNotebookTool(a));
        await tool.InvokeAsync(Args("""{"path":"C:\\nb\\a.ipynb"}"""), Ctx, default);
        Assert.Equal(NotebookOp.Save, seen()!.Op);
        Assert.Equal("C:\\nb\\a.ipynb", seen()!.Path);

        await tool.InvokeAsync(Args("""{}"""), Ctx, default);
        Assert.Null(seen()!.Path);
    }

    [Fact]
    public async Task EditCell_MapsIndexSource_AndValidates()
    {
        var (tool, seen) = Mut(a => new EditCellTool(a));
        await tool.InvokeAsync(Args("""{"index":2,"source":"x=1"}"""), Ctx, default);
        Assert.Equal(NotebookOp.EditCell, seen()!.Op);
        Assert.Equal(2, seen()!.Index);
        Assert.Equal("x=1", seen()!.Source);

        var bad = await tool.InvokeAsync(Args("""{"index":2}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(bad).Reason);
    }

    [Fact]
    public async Task AddCell_DefaultsToCode_AndMapsFields()
    {
        var (tool, seen) = Mut(a => new AddCellTool(a));
        await tool.InvokeAsync(Args("""{"index":1,"type":"markdown","source":"# hi"}"""), Ctx, default);
        Assert.Equal(NotebookOp.AddCell, seen()!.Op);
        Assert.Equal(1, seen()!.Index);
        Assert.Equal("markdown", seen()!.CellType);
        Assert.Equal("# hi", seen()!.Source);

        await tool.InvokeAsync(Args("""{}"""), Ctx, default);
        Assert.Equal("code", seen()!.CellType); // default
        Assert.Null(seen()!.Index);
    }

    [Fact]
    public async Task DeleteCell_MapsIndex()
    {
        var (tool, seen) = Mut(a => new DeleteCellTool(a));
        await tool.InvokeAsync(Args("""{"index":4}"""), Ctx, default);
        Assert.Equal(NotebookOp.DeleteCell, seen()!.Op);
        Assert.Equal(4, seen()!.Index);
    }

    [Fact]
    public async Task ChangeCellType_MapsType_AndRejectsBad()
    {
        var (tool, seen) = Mut(a => new ChangeCellTypeTool(a));
        await tool.InvokeAsync(Args("""{"index":0,"type":"markdown"}"""), Ctx, default);
        Assert.Equal(NotebookOp.ChangeCellType, seen()!.Op);
        Assert.Equal("markdown", seen()!.CellType);

        var bad = await tool.InvokeAsync(Args("""{"index":0,"type":"raw"}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(bad).Reason);
    }

    [Fact]
    public async Task MoveCell_MapsFromTo()
    {
        var (tool, seen) = Mut(a => new MoveCellTool(a));
        await tool.InvokeAsync(Args("""{"from":3,"to":1}"""), Ctx, default);
        Assert.Equal(NotebookOp.MoveCell, seen()!.Op);
        Assert.Equal(3, seen()!.Index);
        Assert.Equal(1, seen()!.ToIndex);
    }

    [Fact]
    public async Task RunCell_MapsIndex()
    {
        var (tool, seen) = Mut(a => new RunCellTool(a));
        await tool.InvokeAsync(Args("""{"index":5}"""), Ctx, default);
        Assert.Equal(NotebookOp.RunCell, seen()!.Op);
        Assert.Equal(5, seen()!.Index);
    }

    [Fact]
    public async Task KernelAndRunAll_MapOps()
    {
        Assert.Equal(NotebookOp.RunAll, (await Capture(a => new RunAllCellsTool(a), "{}")).Op);
        Assert.Equal(NotebookOp.ClearOutputs, (await Capture(a => new ClearCellOutputsTool(a), "{}")).Op);
        Assert.Equal(NotebookOp.StartKernel, (await Capture(a => new StartKernelTool(a), "{}")).Op);
        Assert.Equal(NotebookOp.InterruptKernel, (await Capture(a => new InterruptKernelTool(a), "{}")).Op);
        Assert.Equal(NotebookOp.RestartKernel, (await Capture(a => new RestartKernelTool(a), "{}")).Op);
    }

    [Fact]
    public async Task Mutator_NoNotebookOpen_TargetNotResolved()
    {
        var tool = new EditCellTool(_ => Task.FromResult<NotebookState?>(null));
        var r = await tool.InvokeAsync(Args("""{"index":0,"source":"x=1"}"""), Ctx, default);
        Assert.IsType<TargetNotResolved>(Assert.IsType<FailedResult>(r).Reason);
    }

    [Fact]
    public void MutationTools_AreViewStateVerbs()
    {
        Func<NotebookCommand, Task<NotebookState?>> noop = _ => Task.FromResult<NotebookState?>(null);
        Assert.Equal(McpVerbClass.ViewState, new OpenNotebookTool(noop).VerbClass);
        Assert.Equal(McpVerbClass.ViewState, new RunCellTool(noop).VerbClass);
        Assert.Equal(McpVerbClass.ViewState, new RestartKernelTool(noop).VerbClass);
    }

    private static async Task<NotebookCommand> Capture<T>(Func<Func<NotebookCommand, Task<NotebookState?>>, T> make, string json)
        where T : IMcpTool
    {
        NotebookCommand? captured = null;
        var tool = make(cmd => { captured = cmd; return Task.FromResult<NotebookState?>(SampleState()); });
        await tool.InvokeAsync(Args(json), Ctx, default);
        return captured!;
    }
}
