using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Mcp.Tools.Read;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;
using CanfarDesktop.Models;
using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// The six image tools: the other door into the registry, the user's own list, the vocabulary of what
/// is installed, and what is inside one image.
/// </summary>
public class RegistryImageToolTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);
    private static McpToolContext Ctx() => McpToolContext.ForExternal("c1", Guid.NewGuid());

    private static McpToolContext WriteCtx(out InMemoryProposalStore store)
    {
        store = new InMemoryProposalStore();
        return McpToolContext.ForExternal("c1", Guid.NewGuid(), store, new ProposalBudget());
    }

    private static T Payload<T>(ToolResult result)
        => JsonSerializer.Deserialize<T>(Assert.IsType<DataResult>(result).Json, McpJson.Options)!;

    private static string Failure(ToolResult result) => Assert.IsType<FailedResult>(result).Reason.Description;

    // ── search_image_registry ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ARegistrySearchReportsWhatItFoundWithItsTypes()
    {
        var tool = new SearchImageRegistryTool((query, _) => Task.FromResult<IReadOnlyList<RegistryImage>>(
            [RegistryImage.FromLabels("host/skaha/astroml:latest", ["notebook", "gpu"])]));

        var output = Payload<SearchImageRegistryTool.Output>(
            await tool.InvokeAsync(Args("""{"query":"astroml"}"""), Ctx(), default));

        Assert.Equal(1, output.Count);
        Assert.Equal("host/skaha/astroml:latest", output.Images[0].Id);
        Assert.Equal(["notebook"], output.Images[0].Types);
    }

    /// <summary>An empty query would be a request to download the whole registry.</summary>
    [Fact]
    public async Task AnEmptyQueryIsRefusedWithTheReason()
    {
        var tool = new SearchImageRegistryTool((_, _) => Task.FromResult<IReadOnlyList<RegistryImage>>([]));

        var message = Failure(await tool.InvokeAsync(Args("""{"query":"  "}"""), Ctx(), default));
        Assert.Contains("whole registry", message);
    }

    [Fact]
    public async Task NoMatchesIsAnAnswerThatSaysSo()
    {
        var tool = new SearchImageRegistryTool((_, _) => Task.FromResult<IReadOnlyList<RegistryImage>>([]));

        var output = Payload<SearchImageRegistryTool.Output>(
            await tool.InvokeAsync(Args("""{"query":"nothing"}"""), Ctx(), default));

        Assert.Equal(0, output.Count);
        Assert.Contains("nothing in the registry matched", output.Message);
    }

    [Fact]
    public async Task ARegistryThatRefusesIsReportedAsABackendFailure()
    {
        var tool = new SearchImageRegistryTool((_, _) =>
            Task.FromException<IReadOnlyList<RegistryImage>>(
                new InvalidOperationException("The registry rejected these credentials.")));

        Assert.Contains("rejected these credentials",
            Failure(await tool.InvokeAsync(Args("""{"query":"x"}"""), Ctx(), default)));
    }

    // ── list_my_images ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheUsersOwnListComesBackAsItIs()
    {
        var tool = new ListMyImagesTool(() =>
            [new RegistryImage("host/p/n:1", ["notebook"], "2026-09-06T10:00:00Z")]);

        var output = Payload<ListMyImagesTool.Output>(await tool.InvokeAsync(Args("{}"), Ctx(), default));

        Assert.Equal(1, output.Count);
        Assert.Equal("2026-09-06T10:00:00Z", output.Images[0].AddedAt);
    }

    [Fact]
    public async Task AnEmptyListSaysSoRatherThanAnsweringNothing()
    {
        var tool = new ListMyImagesTool(() => []);
        var output = Payload<ListMyImagesTool.Output>(await tool.InvokeAsync(Args("{}"), Ctx(), default));

        Assert.Contains("has not added any images", output.Message);
    }

    // ── search_packages ─────────────────────────────────────────────────────────────────────────

    private static AllPackages Vocabulary()
    {
        var all = new AllPackages();
        all.Python.Add("specutils");
        all.Python.Add("astropy");
        all.Python.Add("specreduce");
        all.R.Add("spectral");
        all.Dpkg.Add("libspectre1");
        return all;
    }

    /// <summary>
    /// The tool that was missing. "Which image is best for spectra" used to end at a search for
    /// "spectroscopy" that matched nothing — which reads as "no image does that" while nine images carry
    /// specutils.
    /// </summary>
    [Fact]
    public async Task SearchingForPartOfANameFindsWhatItIsActuallyCalled()
    {
        var tool = new SearchPackagesTool(Vocabulary);

        var output = Payload<SearchPackagesTool.Output>(
            await tool.InvokeAsync(Args("""{"query":"spec"}"""), Ctx(), default));

        Assert.Equal(4, output.Count);

        var python = output.Matches.Single(m => m.Ecosystem == "python");
        // Shortest first: the shortest match is usually the package itself rather than a plugin for it.
        Assert.Equal("specutils", python.Names[0]);
    }

    [Fact]
    public async Task AnEcosystemNarrowsTheAnswer()
    {
        var tool = new SearchPackagesTool(Vocabulary);

        var output = Payload<SearchPackagesTool.Output>(
            await tool.InvokeAsync(Args("""{"query":"spec","ecosystem":"r"}"""), Ctx(), default));

        Assert.Equal("r", Assert.Single(output.Matches).Ecosystem);
    }

    [Fact]
    public async Task AnUnknownEcosystemNamesTheOnesThatExist()
    {
        var tool = new SearchPackagesTool(Vocabulary);

        var message = Failure(await tool.InvokeAsync(
            Args("""{"query":"spec","ecosystem":"cargo"}"""), Ctx(), default));

        Assert.Contains("any, python, r, dpkg, rpm, apk", message);
    }

    /// <summary>
    /// With nothing probed, "no matches" would be misleading — the answer is that there is nothing to
    /// match against yet, and what to do about it.
    /// </summary>
    [Fact]
    public async Task WithNothingProbedItSaysThatRatherThanNoMatches()
    {
        var tool = new SearchPackagesTool(() => new AllPackages());

        var output = Payload<SearchPackagesTool.Output>(
            await tool.InvokeAsync(Args("""{"query":"astropy"}"""), Ctx(), default));

        Assert.Equal(0, output.Count);
        Assert.Contains("discover_image_packages", output.Message);
    }

    // ── describe_image ──────────────────────────────────────────────────────────────────────────

    private static ImageManifest Manifest() => new()
    {
        ImageID = "host/skaha/astroml:latest",
        OsFamily = "ubuntu",
        OsVersion = "24.04",
        Kernel = "6.8.0",
        PythonVersion = "3.11.6",
        Capabilities = ["cuda"],
        PythonPackages = [new PythonPackage("astropy", "6.1.0", "pip", "system"),
                          new PythonPackage("specutils", "1.15.0", "pip", "system")],
        CapturedAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public async Task DescribingAnImageAnswersWhatIsInIt()
    {
        var tool = new DescribeImageTool(_ => Manifest());

        var output = Payload<DescribeImageTool.Output>(await tool.InvokeAsync(
            Args("""{"imageID":"host/skaha/astroml:latest"}"""), Ctx(), default));

        Assert.Equal("host/skaha/astroml:latest", output.ImageID);
        Assert.Contains("ubuntu", output.Os, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("3.11.6", output.PythonVersion);
        Assert.Contains("cuda", output.Capabilities);
        Assert.NotEmpty(output.Sections);
    }

    /// <summary>
    /// Empty ecosystems are dropped rather than listed as zeroes: five of them around one real section
    /// is an answer that has to be read twice to find the part that answers.
    /// </summary>
    [Fact]
    public async Task EmptyEcosystemsAreNotListed()
    {
        var tool = new DescribeImageTool(_ => Manifest());

        var output = Payload<DescribeImageTool.Output>(await tool.InvokeAsync(
            Args("""{"imageID":"host/skaha/astroml:latest"}"""), Ctx(), default));

        Assert.All(output.Sections, s => Assert.True(s.Count > 0));
    }

    [Fact]
    public async Task AFilterNarrowsToWhatWasAskedAbout()
    {
        var tool = new DescribeImageTool(_ => Manifest());

        var output = Payload<DescribeImageTool.Output>(await tool.InvokeAsync(
            Args("""{"imageID":"host/skaha/astroml:latest","filter":"specut"}"""), Ctx(), default));

        var names = output.Sections.SelectMany(s => s.Packages).Select(p => p.Name).ToList();
        Assert.Contains("specutils", names);
        Assert.DoesNotContain("astropy", names);
    }

    [Fact]
    public async Task AnImageNobodyHasProbedSaysHowToProbeIt()
    {
        var tool = new DescribeImageTool(_ => null);

        var message = Failure(await tool.InvokeAsync(
            Args("""{"imageID":"host/p/unknown:1"}"""), Ctx(), default));

        Assert.Contains("has not been probed", message);
        Assert.Contains("discover_image_packages", message);
    }

    /// <summary>The wire key is imageID with a capital ID, to match the macOS reference. Not camelCased.</summary>
    [Fact]
    public void TheImageIdKeepsItsCapitalIdOnTheWire()
    {
        var schema = new DescribeImageTool(_ => null).Descriptor.InputSchema.ToJsonString();
        Assert.Contains("\"imageID\"", schema);
    }

    // ── add / remove ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddingAnImageProposesItWithItsTypes()
    {
        var result = await new AddRegistryImageTool().InvokeAsync(
            Args("""{"imageID":"host/p/n:1","types":["notebook","GPU"]}"""), WriteCtx(out _), default);

        var proposal = Assert.IsType<ProposedResult>(result).Proposal;
        Assert.Equal("add_registry_image", proposal.Kind);

        var payload = JsonSerializer.Deserialize<AddRegistryImagePayload>(proposal.Payload, McpJson.Options)!;
        Assert.Equal("host/p/n:1", payload.ImageID);
        Assert.Equal(["notebook", "gpu"], payload.Types);
    }

    /// <summary>
    /// A reference with no tag would be stored, offered on the launch form, and refused by the platform
    /// at launch — the worst moment to find out.
    /// </summary>
    [Fact]
    public async Task AnImageWithNoTagIsRefusedBeforeItCanBeOffered()
    {
        var message = Failure(await new AddRegistryImageTool().InvokeAsync(
            Args("""{"imageID":"host/p/n"}"""), WriteCtx(out _), default));

        Assert.Contains("has no tag", message);
        Assert.Contains("host/project/name:tag", message);
    }

    [Fact]
    public async Task RemovingProposesItAndIsDestructive()
    {
        var tool = new RemoveRegistryImageTool();
        var result = await tool.InvokeAsync(Args("""{"imageID":"host/p/n:1"}"""), WriteCtx(out _), default);

        Assert.Equal(McpVerbClass.Destructive, tool.VerbClass);
        Assert.Equal("remove_registry_image", Assert.IsType<ProposedResult>(result).Proposal.Kind);
    }

    [Fact]
    public async Task TheAppliersDecodeAndInvoke()
    {
        AddRegistryImagePayload? added = null;
        RemoveRegistryImagePayload? removed = null;

        var addResult = await new AddRegistryImageTool().InvokeAsync(
            Args("""{"imageID":"host/p/n:1","types":["notebook"]}"""), WriteCtx(out _), default);
        await new AddRegistryImageApplier(p => { added = p; return Task.CompletedTask; })
            .ApplyAsync(Assert.IsType<ProposedResult>(addResult).Proposal);

        var removeResult = await new RemoveRegistryImageTool().InvokeAsync(
            Args("""{"imageID":"host/p/n:1"}"""), WriteCtx(out _), default);
        await new RemoveRegistryImageApplier(p => { removed = p; return Task.CompletedTask; })
            .ApplyAsync(Assert.IsType<ProposedResult>(removeResult).Proposal);

        Assert.Equal("host/p/n:1", added!.ImageID);
        Assert.Equal("host/p/n:1", removed!.ImageID);
    }

    [Fact]
    public void TheReadsAreReadsAndTheWritesArePropose()
    {
        Assert.Equal(McpVerbClass.Read, new ListMyImagesTool(() => []).VerbClass);
        Assert.Equal(McpVerbClass.Read, new SearchPackagesTool(() => new AllPackages()).VerbClass);
        Assert.Equal(McpVerbClass.Read, new DescribeImageTool(_ => null).VerbClass);
        Assert.Equal(McpVerbClass.SemanticWrite, new AddRegistryImageTool().VerbClass);
    }
}
