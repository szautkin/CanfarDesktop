using System.Text;
using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class VoSpaceWriteToolsTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);

    private static (McpToolContext ctx, InMemoryProposalStore store) Context()
    {
        var store = new InMemoryProposalStore();
        return (McpToolContext.ForExternal("c1", Guid.NewGuid(), store, new ProposalBudget()), store);
    }

    [Fact]
    public async Task UploadText_BuildsProposal()
    {
        var (ctx, _) = Context();
        var result = await new UploadTextToVoSpaceTool().InvokeAsync(
            Args("""{"path":"/home/u/run.py","content":"print(1)"}"""), ctx, default);
        var payload = JsonSerializer.Deserialize<UploadTextPayload>(Assert.IsType<ProposedResult>(result).Proposal.Payload, McpJson.Options)!;
        Assert.Equal("/home/u/run.py", payload.Path);
        Assert.Equal("print(1)", payload.Content);
    }

    [Fact]
    public async Task UploadText_OverSizeLimit_InvalidArgument()
    {
        var (ctx, store) = Context();
        var big = new string('x', UploadTextToVoSpaceTool.MaxContentBytes + 1);
        var result = await new UploadTextToVoSpaceTool().InvokeAsync(
            Args(JsonSerializer.Serialize(new { path = "/p", content = big })), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Empty(store.List());
    }

    [Fact]
    public async Task CreateFolder_BuildsProposal()
    {
        var (ctx, _) = Context();
        var result = await new CreateVoSpaceFolderTool().InvokeAsync(Args("""{"path":"/home/u","name":"data"}"""), ctx, default);
        var payload = JsonSerializer.Deserialize<CreateFolderPayload>(Assert.IsType<ProposedResult>(result).Proposal.Payload, McpJson.Options)!;
        Assert.Equal("data", payload.Name);
    }

    [Fact]
    public void VerbClasses()
    {
        Assert.Equal(McpVerbClass.SemanticWrite, new UploadTextToVoSpaceTool().VerbClass);
        Assert.Equal(McpVerbClass.SemanticWrite, new UploadFileToVoSpaceTool().VerbClass);
        Assert.Equal(McpVerbClass.SemanticWrite, new CreateVoSpaceFolderTool().VerbClass);
        Assert.Equal(McpVerbClass.Destructive, new DeleteVoSpaceNodeTool().VerbClass);
        Assert.Equal(McpVerbClass.ViewState,
            new DownloadVoSpaceFileTool((_, _) => Task.FromResult<Stream>(new MemoryStream())).VerbClass);
    }

    // ── upload_file_to_vospace ────────────────────────────────────────────────

    [Fact]
    public async Task UploadFile_BuildsProposal_WithRootedPath()
    {
        var (ctx, _) = Context();
        var result = await new UploadFileToVoSpaceTool().InvokeAsync(
            Args("""{"localPath":"C:\\data\\cube.fits","vospacePath":"/home/u/cube.fits"}"""), ctx, default);
        var payload = JsonSerializer.Deserialize<UploadFilePayload>(Assert.IsType<ProposedResult>(result).Proposal.Payload, McpJson.Options)!;
        Assert.Equal("/home/u/cube.fits", payload.VospacePath);
        Assert.EndsWith("cube.fits", payload.LocalPath);
    }

    [Fact]
    public async Task UploadFile_NonRootedPath_InvalidArgument()
    {
        var (ctx, store) = Context();
        var result = await new UploadFileToVoSpaceTool().InvokeAsync(
            Args("""{"localPath":"relative.fits","vospacePath":"/home/u/x.fits"}"""), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Empty(store.List());
    }

    [Fact]
    public async Task UploadFileApplier_DecodesAndInvokes()
    {
        string? local = null, remote = null;
        var ap = new UploadFileToVoSpaceApplier(p => { local = p.LocalPath; remote = p.VospacePath; return Task.CompletedTask; });
        await ap.ApplyAsync(Proposal("upload_file_to_vospace", new UploadFilePayload("C:\\a\\b.fits", "/home/u/b.fits", null)));
        Assert.Equal("C:\\a\\b.fits", local);
        Assert.Equal("/home/u/b.fits", remote);
    }

    // ── download_vospace_file ─────────────────────────────────────────────────

    [Fact]
    public async Task DownloadFile_StreamsToLocalPath()
    {
        var ctx = McpToolContext.ForExternal("c1", Guid.Empty);
        var bytes = Encoding.UTF8.GetBytes("hello vospace");
        var dest = Path.Combine(Path.GetTempPath(), $"verbinal_dl_{Guid.NewGuid():N}.bin");
        try
        {
            var tool = new DownloadVoSpaceFileTool((_, _) => Task.FromResult<Stream>(new MemoryStream(bytes)));
            var result = await tool.InvokeAsync(
                Args(JsonSerializer.Serialize(new { path = "/home/u/x.bin", localPath = dest })), ctx, default);
            var doc = JsonDocument.Parse(Assert.IsType<DataResult>(result).Json).RootElement;
            Assert.Equal(bytes.Length, doc.GetProperty("bytesWritten").GetInt64());
            Assert.Equal("hello vospace", File.ReadAllText(dest));
        }
        finally
        {
            if (File.Exists(dest)) File.Delete(dest);
        }
    }

    [Fact]
    public async Task DownloadFile_NonRootedPath_InvalidArgument()
    {
        var ctx = McpToolContext.ForExternal("c1", Guid.Empty);
        var tool = new DownloadVoSpaceFileTool((_, _) => Task.FromResult<Stream>(new MemoryStream()));
        var result = await tool.InvokeAsync(Args("""{"path":"/x","localPath":"relative.bin"}"""), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task DeleteNode_MissingPath_InvalidArgument()
    {
        var (ctx, _) = Context();
        var result = await new DeleteVoSpaceNodeTool().InvokeAsync(Args("""{"path":"  "}"""), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task Appliers_DecodeAndInvoke()
    {
        string? uploaded = null, created = null, deleted = null;
        var up = new UploadTextToVoSpaceApplier(p => { uploaded = p.Path; return Task.CompletedTask; });
        var mk = new CreateVoSpaceFolderApplier(p => { created = p.Name; return Task.CompletedTask; });
        var rm = new DeleteVoSpaceNodeApplier(p => { deleted = p.Path; return Task.CompletedTask; });

        await up.ApplyAsync(Proposal("upload_text_to_vospace", new UploadTextPayload("/p", "c", null)));
        await mk.ApplyAsync(Proposal("create_vospace_folder", new CreateFolderPayload("/parent", "f")));
        await rm.ApplyAsync(Proposal("delete_vospace_node", new DeleteNodePayload("/gone")));

        Assert.Equal("/p", uploaded);
        Assert.Equal("f", created);
        Assert.Equal("/gone", deleted);
    }

    private static PendingProposal Proposal<T>(string kind, T payload)
        => PendingProposal.Create("t", kind, "s", JsonSerializer.SerializeToUtf8Bytes(payload, McpJson.Options), OperationOrigin.External("c1"));

    // ── ProposalPayload.Decode (the one shared decode path for every applier) ──

    [Fact]
    public void Decode_ValidPayload_RoundTrips()
    {
        var decoded = ProposalPayload.Decode<UploadTextPayload>(
            Proposal("upload_text_to_vospace", new UploadTextPayload("/p", "c", null)));
        Assert.Equal("/p", decoded.Path);
    }

    [Fact]
    public void Decode_EmptyPayload_ThrowsWithKind()
    {
        var proposal = PendingProposal.Create("t", "my_kind", "s",
            JsonSerializer.SerializeToUtf8Bytes((UploadTextPayload?)null, McpJson.Options), OperationOrigin.External("c1"));
        var ex = Assert.Throws<ProposalApplyException>(() => ProposalPayload.Decode<UploadTextPayload>(proposal));
        Assert.Contains("my_kind", ex.Message);
    }
}
