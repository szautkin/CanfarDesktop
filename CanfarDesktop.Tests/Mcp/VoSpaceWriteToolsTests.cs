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
        Assert.Equal(McpVerbClass.SemanticWrite, new SetVoSpaceAclTool().VerbClass);
        Assert.Equal(McpVerbClass.Destructive, new DeleteVoSpaceNodeTool().VerbClass);
        Assert.Equal(McpVerbClass.ViewState,
            new DownloadVoSpaceFileTool((_, _) => Task.FromResult<Stream>(new MemoryStream())).VerbClass);
    }

    // ── set_vospace_acl (SCI-12-2 write side) ─────────────────────────────────

    [Fact]
    public async Task SetAcl_BuildsProposal_PreservesNullVsEmptyDistinction_AndExplicitSummary()
    {
        var (ctx, _) = Context();
        // groupRead set, groupWrite OMITTED (null => leave unchanged), isPublic false.
        var result = await new SetVoSpaceAclTool().InvokeAsync(
            Args("""{"path":"projects/team/data","groupRead":["ivo://cadc.nrc.ca/gms?TeamA"],"isPublic":false}"""), ctx, default);
        var proposed = Assert.IsType<ProposedResult>(result);
        var payload = JsonSerializer.Deserialize<SetAclPayload>(proposed.Proposal.Payload, McpJson.Options)!;
        Assert.Equal("projects/team/data", payload.Path);
        Assert.Equal(new[] { "ivo://cadc.nrc.ca/gms?TeamA" }, payload.GroupRead);
        Assert.Null(payload.GroupWrite);   // omitted => unchanged, NOT revoked
        Assert.False(payload.IsPublic);
        // The proposal the user reviews must spell out the resulting ACL.
        Assert.Contains("read: ivo://cadc.nrc.ca/gms?TeamA", proposed.Proposal.Summary);
        Assert.Contains("public: no", proposed.Proposal.Summary);
    }

    [Fact]
    public async Task SetAcl_EmptyList_IsRevoke_NotNull()
    {
        var (ctx, _) = Context();
        var result = await new SetVoSpaceAclTool().InvokeAsync(Args("""{"path":"home/u/x","groupRead":[]}"""), ctx, default);
        var proposed = Assert.IsType<ProposedResult>(result);
        var payload = JsonSerializer.Deserialize<SetAclPayload>(proposed.Proposal.Payload, McpJson.Options)!;
        Assert.NotNull(payload.GroupRead);   // empty list (revoke), NOT null (unchanged)
        Assert.Empty(payload.GroupRead!);
        Assert.Contains("read: revoke all groups", proposed.Proposal.Summary);
    }

    [Fact]
    public async Task SetAcl_NoDimensions_InvalidArgument()
    {
        var (ctx, store) = Context();
        var result = await new SetVoSpaceAclTool().InvokeAsync(Args("""{"path":"home/u/x"}"""), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Empty(store.List());
    }

    [Fact]
    public async Task SetAclApplier_DecodesAndInvokes()
    {
        SetAclPayload? seen = null;
        var ap = new SetVoSpaceAclApplier(p => { seen = p; return Task.CompletedTask; });
        await ap.ApplyAsync(Proposal("set_vospace_acl",
            new SetAclPayload("home/u/x", new[] { "ivo://cadc.nrc.ca/gms?A" }, System.Array.Empty<string>(), true)));
        Assert.Equal("home/u/x", seen!.Path);
        Assert.Equal(new[] { "ivo://cadc.nrc.ca/gms?A" }, seen.GroupRead);
        Assert.Empty(seen.GroupWrite!);
        Assert.True(seen.IsPublic);
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

    // ── clear_user_site ───────────────────────────────────────────────────────

    [Fact]
    public async Task ClearUserSite_BuildsDestructiveProposal_WithVerbatimSummary()
    {
        var (ctx, _) = Context();
        var tool = new ClearUserSiteTool();
        Assert.Equal(McpVerbClass.Destructive, tool.VerbClass);

        var result = await tool.InvokeAsync(Args("{}"), ctx, default);
        var proposal = Assert.IsType<ProposedResult>(result).Proposal;
        Assert.Equal("clear_user_site", proposal.Kind);
        Assert.Equal("Wipe user-site Python packages from VOSpace (~/.local/lib/python3.*/site-packages)", proposal.Summary);
    }

    [Fact]
    public async Task ClearUserSiteApplier_DeletesSitePackagesForEveryPythonDir_OnlyPythonDirs()
    {
        string? listedPath = null;
        var deleted = new List<string>();
        var ap = new ClearUserSiteApplier(
            () => "szautkin",
            (path, _) =>
            {
                listedPath = path;
                return Task.FromResult(new List<CanfarDesktop.Models.VoSpaceNode>
                {
                    new() { Name = "python3.11" },
                    new() { Name = "python3.12" },
                    new() { Name = "R" }, // non-python dirs are left alone
                });
            },
            (path, _) => { deleted.Add(path); return Task.CompletedTask; });

        await ap.ApplyAsync(Proposal("clear_user_site", new ClearUserSitePayload()));

        Assert.Equal("szautkin/.local/lib", listedPath);
        Assert.Equal(new[]
        {
            "szautkin/.local/lib/python3.11/site-packages",
            "szautkin/.local/lib/python3.12/site-packages",
        }, deleted);
    }

    [Fact]
    public async Task ClearUserSiteApplier_MissingLocalLib_IsSuccess()
    {
        var deletes = 0;
        var ap = new ClearUserSiteApplier(
            () => "u",
            (_, _) => throw new HttpRequestException("404", null, System.Net.HttpStatusCode.NotFound), // no .local/lib
            (_, _) => { deletes++; return Task.CompletedTask; });

        await ap.ApplyAsync(Proposal("clear_user_site", new ClearUserSitePayload())); // must not throw
        Assert.Equal(0, deletes);
    }

    [Fact]
    public async Task ClearUserSiteApplier_NonNotFoundListFailure_Propagates()
    {
        // Auth expiry / 503 / timeouts must NOT be reported as a successful destructive apply.
        var ap = new ClearUserSiteApplier(
            () => "u",
            (_, _) => throw new HttpRequestException("503", null, System.Net.HttpStatusCode.ServiceUnavailable),
            (_, _) => Task.CompletedTask);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => ap.ApplyAsync(Proposal("clear_user_site", new ClearUserSitePayload())));
    }

    [Fact]
    public async Task ClearUserSiteApplier_PerDirDeleteFailures_AreBestEffort()
    {
        var attempted = new List<string>();
        var ap = new ClearUserSiteApplier(
            () => "u",
            (_, _) => Task.FromResult(new List<CanfarDesktop.Models.VoSpaceNode>
            {
                new() { Name = "python3.10" },
                new() { Name = "python3.11" },
            }),
            (path, _) =>
            {
                attempted.Add(path);
                throw new HttpRequestException("404"); // missing site-packages under this version
            });

        await ap.ApplyAsync(Proposal("clear_user_site", new ClearUserSitePayload())); // must not throw
        Assert.Equal(2, attempted.Count); // every python dir is still attempted
    }

    [Fact]
    public async Task ClearUserSiteApplier_NotAuthenticated_Throws()
    {
        var ap = new ClearUserSiteApplier(
            () => "  ",
            (_, _) => Task.FromResult(new List<CanfarDesktop.Models.VoSpaceNode>()),
            (_, _) => Task.CompletedTask);

        var ex = await Assert.ThrowsAsync<ProposalApplyException>(
            () => ap.ApplyAsync(Proposal("clear_user_site", new ClearUserSitePayload())));
        Assert.Contains("not authenticated", ex.Message);
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
