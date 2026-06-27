using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;
using CanfarDesktop.Services.Export;

namespace CanfarDesktop.Tests.Mcp;

public class ExportBundleToolTests
{
    private static readonly McpToolContext Ctx = McpToolContext.ForExternal("c1", Guid.Empty);
    private static JsonValue Args(string json) => JsonValue.Parse(json);
    private static JsonElement Json(ToolResult r) => JsonDocument.Parse(Assert.IsType<DataResult>(r).Json).RootElement;

    [Fact]
    public void IsViewStateVerb()
        => Assert.Equal(McpVerbClass.ViewState,
            new ExportResearchBundleTool((_, _, _, _, _, _) => Task.FromResult(new ExportBundleResult("b", "z", null))).VerbClass);

    [Fact]
    public async Task Export_PassesOptions_ReturnsPaths()
    {
        (string dest, bool notes, bool hist, bool files, bool upload)? seen = null;
        var dir = Path.GetTempPath(); // an existing folder
        var tool = new ExportResearchBundleTool((dest, notes, hist, files, upload, ct) =>
        {
            seen = (dest, notes, hist, files, upload);
            return Task.FromResult(new ExportBundleResult(Path.Combine(dest, "bundle"), Path.Combine(dest, "bundle.zip"), "alice/Verbinal-Exports/bundle.zip"));
        });
        var doc = Json(await tool.InvokeAsync(
            Args(JsonSerializer.Serialize(new { destFolder = dir, includeNotes = true, includeSearchHistory = false, includeFiles = true, uploadToVospace = true })),
            Ctx, default));

        Assert.True(seen!.Value.notes);
        Assert.False(seen.Value.hist);
        Assert.True(seen.Value.files);
        Assert.True(seen.Value.upload);
        Assert.EndsWith("bundle.zip", doc.GetProperty("zipPath").GetString());
        Assert.Equal("alice/Verbinal-Exports/bundle.zip", doc.GetProperty("remotePath").GetString());
    }

    [Fact]
    public async Task Export_DefaultsNotesAndHistoryTrue()
    {
        (bool notes, bool hist, bool files, bool upload)? seen = null;
        var dir = Path.GetTempPath();
        var tool = new ExportResearchBundleTool((_, notes, hist, files, upload, _) =>
        {
            seen = (notes, hist, files, upload);
            return Task.FromResult(new ExportBundleResult("b", "z", null));
        });
        await tool.InvokeAsync(Args(JsonSerializer.Serialize(new { destFolder = dir })), Ctx, default);
        Assert.True(seen!.Value.notes);
        Assert.True(seen.Value.hist);
        Assert.False(seen.Value.files);
        Assert.False(seen.Value.upload);
    }

    [Fact]
    public async Task Export_NonRootedDest_InvalidArgument()
    {
        var tool = new ExportResearchBundleTool((_, _, _, _, _, _) => Task.FromResult(new ExportBundleResult("b", "z", null)));
        var r = await tool.InvokeAsync(Args("""{"destFolder":"relative"}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(r).Reason);
    }

    [Fact]
    public async Task Export_MissingFolder_InvalidArgument()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"verbinal_nope_{Guid.NewGuid():N}");
        var tool = new ExportResearchBundleTool((_, _, _, _, _, _) => Task.FromResult(new ExportBundleResult("b", "z", null)));
        var r = await tool.InvokeAsync(Args(JsonSerializer.Serialize(new { destFolder = missing })), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(r).Reason);
    }

    [Fact]
    public async Task Export_ExportException_BackendError()
    {
        var dir = Path.GetTempPath();
        var tool = new ExportResearchBundleTool((_, _, _, _, _, _) => throw new ExportException("disk full"));
        var r = await tool.InvokeAsync(Args(JsonSerializer.Serialize(new { destFolder = dir })), Ctx, default);
        Assert.IsType<BackendError>(Assert.IsType<FailedResult>(r).Reason);
    }
}
