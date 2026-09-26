using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;
using CanfarDesktop.Models.Cutouts;
using CanfarDesktop.Services.Cutouts;
using static CanfarDesktop.Tests.Services.Cutouts.SodaDescriptorParserTests;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// The agent's cutout tools. A cutout is checked when it is PROPOSED, by the same check the editor
/// shows, so an agent asking for a region off the image is told why at once rather than queuing a
/// download to fail.
/// </summary>
public class CutoutToolsTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);

    private static (McpToolContext Ctx, InMemoryProposalStore Store) Context()
    {
        var store = new InMemoryProposalStore();
        return (McpToolContext.ForExternal("c1", Guid.NewGuid(), store, new ProposalBudget()), store);
    }

    private static DownloadCutoutTool Tool(params SodaDescriptor[] files)
        => new((_, _) => Task.FromResult<IReadOnlyList<ICutoutSource>>(files.Select(f => new SodaCutoutSource(f)).ToList()));

    [Fact]
    public async Task ACircleOnTheImage_IsProposed_WithTheCheckedCutout()
    {
        var (ctx, _) = Context();
        var result = await Tool(MegaPipe()).InvokeAsync(Args(
            """{"publisherId":"ivo://cadc/MP","circle":{"ra":10.68,"dec":41.27,"radius":0.05}}"""), ctx, default);

        var proposal = Assert.IsType<ProposedResult>(result).Proposal;
        Assert.Equal("download_cutout", proposal.Kind);
        Assert.StartsWith("Download cutout of ivo://cadc/MP: r ", proposal.Summary);

        var payload = JsonSerializer.Deserialize<DownloadCutoutPayload>(proposal.Payload, McpJson.Options)!;
        Assert.Equal("cadc:CFHTSG/G006.010.684+41.269.R.fits", payload.Spec.ArtifactId); // the only file, taken without asking
        Assert.Equal(0.05, payload.Spec.Region!.Radius);
    }

    /// <summary>Refused at proposal time, with the reason, and nothing queued.</summary>
    [Fact]
    public async Task ARegionOffTheImage_IsRefused_BeforeAnythingIsQueued()
    {
        var (ctx, store) = Context();
        var result = await Tool(MegaPipe()).InvokeAsync(Args(
            """{"publisherId":"ivo://cadc/MP","circle":{"ra":20,"dec":41,"radius":0.1}}"""), ctx, default);

        var failed = Assert.IsType<FailedResult>(result);
        Assert.Contains("outside", Assert.IsType<InvalidArgument>(failed.Reason).Description);
        Assert.Empty(store.List());
    }

    [Fact]
    public async Task TwoRegionsAtOnce_AreRefused()
    {
        var (ctx, _) = Context();
        var result = await Tool(MegaPipe()).InvokeAsync(Args(
            """{"publisherId":"ivo://cadc/MP","circle":{"ra":10.68,"dec":41.27,"radius":0.05},"box":{"ra":10.68,"dec":41.27,"width":0.1,"height":0.1}}"""), ctx, default);

        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    /// <summary>With several files that can be cut, the agent names one; it is told their names rather than having one guessed.</summary>
    [Fact]
    public async Task WithSeveralFiles_TheAgentIsToldWhichThereAre()
    {
        var (ctx, _) = Context();
        var other = MegaPipe() with { ArtifactId = "cadc:CFHTSG/other.fits" };
        var result = await Tool(MegaPipe(), other).InvokeAsync(Args(
            """{"publisherId":"ivo://cadc/MP","circle":{"ra":10.68,"dec":41.27,"radius":0.05}}"""), ctx, default);

        var reason = Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason).Description;
        Assert.Contains("cadc:CFHTSG/other.fits", reason);
    }

    [Fact]
    public async Task AnObservationWithNothingToCut_SaysSo_AndPointsAtTheWholeFile()
    {
        var (ctx, _) = Context();
        var result = await Tool().InvokeAsync(Args("""{"publisherId":"ivo://cadc/X","circle":{"ra":1,"dec":1,"radius":0.1}}"""), ctx, default);

        Assert.Contains("download_observation", Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason).Description);
    }

    /// <summary>A cube's band, in metres, checked against the cube's own range.</summary>
    [Fact]
    public async Task ACubesBand_IsCarriedInMetres()
    {
        var (ctx, _) = Context();
        var result = await Tool(JcmtCube()).InvokeAsync(Args(
            """{"publisherId":"ivo://cadc/J","bandMin":8.66e-4,"bandMax":8.67e-4}"""), ctx, default);

        var payload = JsonSerializer.Deserialize<DownloadCutoutPayload>(Assert.IsType<ProposedResult>(result).Proposal.Payload, McpJson.Options)!;
        Assert.Equal(8.66e-4, payload.Spec.BandMin);
        Assert.Null(payload.Spec.Region);
    }

    [Fact]
    public void TheOptions_SuggestFromTheSearch_AndEstimateTheSize()
    {
        var options = CutoutOptions.From("ivo://cadc/MP", [new SodaCutoutSource(MegaPipe(), 1_663_807_680)],
            new CutoutHints(Ra: 10.68, Dec: 41.27, RadiusDeg: 0.05));

        var file = Assert.Single(options.Files);
        Assert.Equal(new[] { "CIRCLE", "ID", "POLYGON", "POS" }, file.Parameters);
        Assert.Equal(0.05, file.Suggested.Region!.Radius);
        Assert.InRange(file.SuggestedBytes!.Value, 8_000_000, 17_000_000);
        Assert.Null(options.Note);
    }

    [Fact]
    public void TheVerbClasses_AreAWriteAndAView()
    {
        Assert.Equal(McpVerbClass.SemanticWrite, Tool().VerbClass);
        Assert.Equal(McpVerbClass.ViewState, new ShowCutoutEditorTool(_ => Task.FromResult(CutoutEditorShown.Refused(""))).VerbClass);
    }

    [Fact]
    public async Task TheApplier_HandsOnTheCheckedCutout()
    {
        DownloadCutoutPayload? seen = null;
        var applier = new DownloadCutoutApplier((p, _) => { seen = p; return Task.CompletedTask; });
        var spec = new CutoutSpec { ArtifactId = "cadc:x", Region = SkyRegion.Circle(1, 2, 0.1) };
        var proposal = PendingProposal.Create("t", "download_cutout", "s",
            JsonSerializer.SerializeToUtf8Bytes(new DownloadCutoutPayload("ivo://cadc/X", spec), McpJson.Options),
            OperationOrigin.External("c1"));

        await applier.ApplyAsync(proposal);

        Assert.Equal(spec.Key, seen!.Spec.Key);
    }
}
