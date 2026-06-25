using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Mcp.Tools.Read;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;
using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// 1-to-1 parity with the macOS FindImagesWithPackagesShapingTests + PartialMatchScoringTests
/// (integration) + discover_image_packages coverage. Pins the rich output shape (imageIds / coverage /
/// candidatesToProbe / allDiscovered / partialMatches) and the type-filter behaviour.
/// </summary>
public class ImageDiscoveryToolTests
{
    private static readonly McpToolContext Ctx = McpToolContext.ForExternal("test", Guid.Empty);

    private static JsonValue Args(string json) => JsonValue.Parse(json);
    private static JsonElement Json(ToolResult r) => JsonDocument.Parse(Assert.IsType<DataResult>(r).Json).RootElement;
    private static string[] Arr(JsonElement e, string prop) => e.GetProperty(prop).EnumerateArray().Select(x => x.GetString()!).ToArray();

    private static FindImagesWithPackagesTool MakeTool(
        string[]? search = null,
        (string Id, string[] Types)[]? catalogue = null,
        string[]? discovered = null,
        PartialMatch[]? partial = null)
        => new(
            _ => search ?? Array.Empty<string>(),
            _ => Task.FromResult<IReadOnlyList<CatalogueImage>>(
                (catalogue ?? Array.Empty<(string, string[])>())
                    .Select(c => new CatalogueImage(c.Id, c.Types)).ToList()),
            () => discovered ?? Array.Empty<string>(),
            (_, _, _) => partial ?? Array.Empty<PartialMatch>());

    // ── imageIDs (regression-pin) ──────────────────────────────────────────────

    [Fact]
    public async Task MatchesPassThrough()
    {
        var doc = Json(await MakeTool(search: new[] { "a:1", "b:1" }).InvokeAsync(JsonValue.Null, Ctx, default));
        Assert.Equal(new[] { "a:1", "b:1" }.ToHashSet(), Arr(doc, "imageIds").ToHashSet());
        Assert.True(doc.GetProperty("unfiltered").GetBoolean()); // empty args → unfiltered, NOT an error
    }

    // ── candidatesToProbe ──────────────────────────────────────────────────────

    [Fact]
    public async Task EmptyCacheSurfacesAllCatalogueAsCandidates()
    {
        var doc = Json(await MakeTool(
            catalogue: new[] { ("img:a", new[] { "headless" }), ("img:b", new[] { "headless" }), ("img:c", new[] { "notebook" }) },
            discovered: Array.Empty<string>()).InvokeAsync(JsonValue.Null, Ctx, default));
        Assert.Equal(new[] { "img:a", "img:b", "img:c" }.ToHashSet(), Arr(doc, "candidatesToProbe").ToHashSet());
        Assert.Empty(Arr(doc, "imageIds"));
    }

    [Fact]
    public async Task CandidatesExcludeAlreadyDiscovered()
    {
        var doc = Json(await MakeTool(
            catalogue: new[] { ("img:a", new[] { "headless" }), ("img:b", new[] { "headless" }), ("img:c", new[] { "headless" }) },
            discovered: new[] { "img:a" }).InvokeAsync(JsonValue.Null, Ctx, default));
        Assert.Equal(new[] { "img:b", "img:c" }.ToHashSet(), Arr(doc, "candidatesToProbe").ToHashSet());
        Assert.DoesNotContain("img:a", Arr(doc, "candidatesToProbe"));
    }

    [Fact]
    public async Task CandidatesExcludeAlreadyMatched()
    {
        var doc = Json(await MakeTool(
            search: new[] { "img:a" },
            catalogue: new[] { ("img:a", new[] { "headless" }), ("img:b", new[] { "headless" }) },
            discovered: new[] { "img:a" }).InvokeAsync(JsonValue.Null, Ctx, default));
        Assert.Equal(new[] { "img:a" }, Arr(doc, "imageIds"));
        Assert.Equal(new[] { "img:b" }, Arr(doc, "candidatesToProbe")); // no duplicate of the match
    }

    [Fact]
    public async Task CandidatesAreSortedForStableOrder()
    {
        var doc = Json(await MakeTool(
            catalogue: new[] { ("z:1", new[] { "headless" }), ("a:1", new[] { "headless" }), ("m:1", new[] { "headless" }) })
            .InvokeAsync(JsonValue.Null, Ctx, default));
        Assert.Equal(new[] { "a:1", "m:1", "z:1" }, Arr(doc, "candidatesToProbe"));
    }

    [Fact]
    public async Task CandidatesCappedAtTen()
    {
        var catalogue = Enumerable.Range(1, 15).Select(i => ($"img:{i:D2}", new[] { "headless" })).ToArray();
        var doc = Json(await MakeTool(catalogue: catalogue).InvokeAsync(JsonValue.Null, Ctx, default));
        Assert.Equal(10, Arr(doc, "candidatesToProbe").Length);
        Assert.Equal("img:01", Arr(doc, "candidatesToProbe")[0]);
    }

    // ── type filter ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TypeFilterScopesCandidatesToMatchingTypes()
    {
        var doc = Json(await MakeTool(
            catalogue: new[] { ("head:a", new[] { "headless" }), ("note:a", new[] { "notebook" }), ("head:b", new[] { "headless" }) })
            .InvokeAsync(Args("""{"type":"headless"}"""), Ctx, default));
        Assert.Equal(new[] { "head:a", "head:b" }.ToHashSet(), Arr(doc, "candidatesToProbe").ToHashSet());
    }

    [Fact]
    public async Task TypeFilterScopesAllDiscoveredAndCoverage()
    {
        var doc = Json(await MakeTool(
            catalogue: new[] { ("head:a", new[] { "headless" }), ("head:b", new[] { "headless" }), ("note:a", new[] { "notebook" }) },
            discovered: new[] { "head:a", "note:a" }).InvokeAsync(Args("""{"type":"headless"}"""), Ctx, default));
        Assert.Equal(new[] { "head:a" }, Arr(doc, "allDiscovered"));
        Assert.Equal(2, doc.GetProperty("coverage").GetProperty("total").GetInt32());
        Assert.Equal(1, doc.GetProperty("coverage").GetProperty("discovered").GetInt32());
    }

    [Fact]
    public async Task TypeFilterIsCaseInsensitive()
    {
        var doc = Json(await MakeTool(catalogue: new[] { ("head:a", new[] { "Headless" }) })
            .InvokeAsync(Args("""{"type":"headless"}"""), Ctx, default));
        Assert.Equal(new[] { "head:a" }, Arr(doc, "candidatesToProbe"));
    }

    // ── allDiscovered ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AllDiscoveredReturnsEveryProbedImage()
    {
        var doc = Json(await MakeTool(
            search: new[] { "a:1" },
            catalogue: new[] { ("a:1", new[] { "headless" }), ("b:1", new[] { "headless" }) },
            discovered: new[] { "a:1", "b:1" }).InvokeAsync(JsonValue.Null, Ctx, default));
        Assert.Equal(new[] { "a:1" }, Arr(doc, "imageIds"));
        Assert.Equal(new[] { "a:1", "b:1" }.ToHashSet(), Arr(doc, "allDiscovered").ToHashSet());
    }

    [Fact]
    public async Task EmptyCatalogueDoesNotCrash()
    {
        var doc = Json(await MakeTool().InvokeAsync(JsonValue.Null, Ctx, default));
        Assert.Equal(0, doc.GetProperty("coverage").GetProperty("total").GetInt32());
        Assert.Empty(Arr(doc, "candidatesToProbe"));
        Assert.Empty(Arr(doc, "allDiscovered"));
    }

    // ── partialMatches ─────────────────────────────────────────────────────────

    [Fact]
    public async Task StrictEmptyQueryNonEmpty_PopulatesPartials()
    {
        var partials = new[]
        {
            new PartialMatch("img:near", 0.83, new[] { "python:fitsio" }),
            new PartialMatch("img:other", 0.66, new[] { "python:fitsio", "python:photutils" }),
        };
        var tool = MakeTool(
            search: Array.Empty<string>(),
            catalogue: new[] { ("img:near", new[] { "headless" }), ("img:other", new[] { "headless" }) },
            discovered: new[] { "img:near", "img:other" },
            partial: partials);
        var doc = Json(await tool.InvokeAsync(Args("""{"python":["astropy","scipy","astroquery","numpy","fitsio","python3"]}"""), Ctx, default));

        Assert.Empty(Arr(doc, "imageIds"));
        var pm = doc.GetProperty("partialMatches");
        Assert.Equal(2, pm.GetArrayLength());
        Assert.Equal("img:near", pm[0].GetProperty("imageId").GetString());
        Assert.Equal(0.83, pm[0].GetProperty("score").GetDouble(), 4);
        Assert.Equal(new[] { "python:fitsio" }, pm[0].GetProperty("missing").EnumerateArray().Select(x => x.GetString()).ToArray());
    }

    [Fact]
    public async Task StrictMatchNonEmpty_SuppressesPartials()
    {
        var tool = MakeTool(
            search: new[] { "img:hit" },
            catalogue: new[] { ("img:hit", new[] { "headless" }) },
            discovered: new[] { "img:hit" },
            partial: new[] { new PartialMatch("img:fake", 0.5, Array.Empty<string>()) });
        var doc = Json(await tool.InvokeAsync(Args("""{"python":["astropy"]}"""), Ctx, default));

        Assert.Equal(new[] { "img:hit" }, Arr(doc, "imageIds"));
        Assert.Equal(0, doc.GetProperty("partialMatches").GetArrayLength());
    }

    [Fact]
    public async Task EmptyQuery_YieldsEmptyPartials_AndUnfiltered()
    {
        var doc = Json(await MakeTool().InvokeAsync(JsonValue.Null, Ctx, default));
        Assert.Equal(0, doc.GetProperty("partialMatches").GetArrayLength());
        Assert.True(doc.GetProperty("unfiltered").GetBoolean());
    }

    [Fact]
    public async Task PartialMatchesRespectTypeFilter()
    {
        var partials = new[]
        {
            new PartialMatch("img:notebook-only", 0.83, Array.Empty<string>()),
            new PartialMatch("img:headless", 0.66, Array.Empty<string>()),
        };
        var tool = MakeTool(
            search: Array.Empty<string>(),
            catalogue: new[] { ("img:notebook-only", new[] { "notebook" }), ("img:headless", new[] { "headless" }) },
            discovered: new[] { "img:notebook-only", "img:headless" },
            partial: partials);
        var doc = Json(await tool.InvokeAsync(Args("""{"python":["astropy"],"type":"headless"}"""), Ctx, default));

        var ids = doc.GetProperty("partialMatches").EnumerateArray().Select(p => p.GetProperty("imageId").GetString()).ToArray();
        Assert.Equal(new[] { "img:headless" }, ids);
    }

    // ── discover_image_packages (write) ────────────────────────────────────────

    private static (McpToolContext ctx, InMemoryProposalStore store) WriteContext()
    {
        var store = new InMemoryProposalStore();
        return (McpToolContext.ForExternal("c1", Guid.NewGuid(), store, new ProposalBudget()), store);
    }

    private static PendingProposal ProposalWith<T>(string kind, T payload)
        => PendingProposal.Create("tool", kind, "summary",
            JsonSerializer.SerializeToUtf8Bytes(payload, McpJson.Options), OperationOrigin.External("c1"));

    [Fact]
    public async Task DiscoverImagePackages_BuildsProposal_DefaultNotForce()
    {
        var (ctx, _) = WriteContext();
        var result = await new DiscoverImagePackagesTool().InvokeAsync(Args("""{"image":"img:a"}"""), ctx, default);

        var proposal = Assert.IsType<ProposedResult>(result).Proposal;
        Assert.Equal("discover_image_packages", proposal.Kind);
        Assert.Equal("Discover packages installed in 'img:a'", proposal.Summary);
        var payload = JsonSerializer.Deserialize<DiscoverImagePackagesPayload>(proposal.Payload, McpJson.Options)!;
        Assert.Equal("img:a", payload.Image);
        Assert.False(payload.Force);
    }

    [Fact]
    public async Task DiscoverImagePackages_Force_ChangesSummaryAndPayload()
    {
        var (ctx, _) = WriteContext();
        var result = await new DiscoverImagePackagesTool().InvokeAsync(Args("""{"image":"img:a","force":true}"""), ctx, default);
        var proposal = Assert.IsType<ProposedResult>(result).Proposal;
        Assert.Equal("Re-probe packages installed in 'img:a'", proposal.Summary);
        Assert.True(JsonSerializer.Deserialize<DiscoverImagePackagesPayload>(proposal.Payload, McpJson.Options)!.Force);
    }

    [Fact]
    public async Task DiscoverImagePackages_MissingImage_InvalidArgument()
    {
        var (ctx, _) = WriteContext();
        var result = await new DiscoverImagePackagesTool().InvokeAsync(Args("""{"image":"  "}"""), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public void DiscoverImagePackages_IsSemanticWrite() =>
        Assert.Equal(McpVerbClass.SemanticWrite, new DiscoverImagePackagesTool().VerbClass);

    [Fact]
    public async Task DiscoverImagePackagesApplier_DecodesAndInvokes()
    {
        DiscoverImagePackagesPayload? applied = null;
        var applier = new DiscoverImagePackagesApplier(p => { applied = p; return Task.CompletedTask; });

        Assert.Equal("discover_image_packages", applier.Kind);
        await applier.ApplyAsync(ProposalWith("discover_image_packages", new DiscoverImagePackagesPayload("img:x", true)));

        Assert.Equal("img:x", applied!.Image);
        Assert.True(applied.Force);
    }
}
