using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Cutouts;
using CanfarDesktop.Services;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// What a person can do on the new screens, an agent can: show a Research record, or a cutout's
/// original observation; copy an observation's details; stop a search. Each tool takes the one delegate
/// it needs, so the screen here is a lambda.
/// </summary>
public class ResearchUiToolsTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);
    private static McpToolContext Ctx() => McpToolContext.ForExternal("c1", Guid.NewGuid());
    private static T Payload<T>(ToolResult result)
        => JsonSerializer.Deserialize<T>(Assert.IsType<DataResult>(result).Json, McpJson.Options)!;
    private static string Failure(ToolResult result) => Assert.IsType<FailedResult>(result).Reason.Description;

    private static readonly DownloadedObservation Tile = new()
    {
        Id = "r1",
        PublisherID = "ivo://cadc.nrc.ca/CFHTMEGAPIPE?G006.010.684+41.269/G006.010.684+41.269.I",
        Collection = "CFHTMEGAPIPE",
        ObservationID = "G006.010.684+41.269",
        TargetName = "M31",
        Cutout = new CutoutSpec { ArtifactId = "cadc:CFHTSG/G006.010.684+41.269.I.fits", Region = SkyRegion.Circle(10.68, 41.27, 0.01), CutBy = CutoutMethod.Local },
    };

    // ── show_research_observation ───────────────────────────────────────────

    [Fact]
    public async Task ShowResearchObservation_ShowsTheRecordAsked_OrItsOriginal()
    {
        var asked = new List<(string Id, bool Original)>();
        var tool = new ShowResearchObservationTool((id, original) =>
        {
            asked.Add((id, original));
            return Task.FromResult(new ResearchShown(true, original ? "search" : "research", original ? null : id,
                original ? "G006.010.684+41.269" : null));
        });

        var shown = Payload<ResearchShown>(await tool.InvokeAsync(Args("""{"id":" r1 "}"""), Ctx(), default));
        var original = Payload<ResearchShown>(await tool.InvokeAsync(Args("""{"id":"r1","original":true}"""), Ctx(), default));

        Assert.Equal([("r1", false), ("r1", true)], asked);
        Assert.Equal(("research", "r1"), (shown.Where, shown.Id));
        Assert.Equal(("search", "G006.010.684+41.269"), (original.Where, original.ObservationId));
        Assert.Contains("id is required", Failure(await tool.InvokeAsync(Args("""{"id":""}"""), Ctx(), default)));
    }

    // ── copy_to_clipboard ───────────────────────────────────────────────────

    private static (CopyToClipboardTool Tool, List<(string Text, string? What)> Copied) Copier(bool clipboardFree = true)
    {
        var copied = new List<(string, string?)>();
        var tool = new CopyToClipboardTool(id => id is "r1" or "G006.010.684+41.269" ? Tile : null,
            (text, what) => { if (clipboardFree) copied.Add((text, what)); return Task.FromResult(clipboardFree); });
        return (tool, copied);
    }

    /// <summary>By an observation's id, the same details Copy details copies — not text of the agent's own.</summary>
    [Fact]
    public async Task CopyToClipboard_AnObservation_IsTheSameDetailsCopyDetailsCopies()
    {
        var (tool, copied) = Copier();

        var result = Payload<ClipboardCopied>(await tool.InvokeAsync(Args("""{"observationId":"G006.010.684+41.269"}"""), Ctx(), default));

        var (text, what) = Assert.Single(copied);
        Assert.Equal(ObservationSummary.Text(Tile), text);
        Assert.Equal("the observation's details", what);
        Assert.Equal((true, text.Length), (result.Copied, result.Characters));
    }

    [Fact]
    public async Task CopyToClipboard_Text_IsCopiedAsGiven()
    {
        var (tool, copied) = Copier();

        await tool.InvokeAsync(Args("""{"text":"00h42m44.3s +41°16′09″"}"""), Ctx(), default);

        Assert.Equal(("00h42m44.3s +41°16′09″", (string?)null), Assert.Single(copied));
    }

    [Theory]
    [InlineData("""{}""", "one of the two")]
    [InlineData("""{"text":"x","observationId":"r1"}""", "one of the two")]
    [InlineData("""{"observationId":"nope"}""", "list_downloaded_observations")]
    [InlineData("""{"text":""}""", "nothing to copy")]
    public async Task CopyToClipboard_RefusesWhatCannotBeCopied(string args, string why)
    {
        var (tool, copied) = Copier();

        Assert.Contains(why, Failure(await tool.InvokeAsync(Args(args), Ctx(), default)));
        Assert.Empty(copied);
    }

    /// <summary>A clipboard another program holds is not claimed as copied.</summary>
    [Fact]
    public async Task CopyToClipboard_ThatTheClipboardRefuses_SaysSo()
    {
        var (tool, _) = Copier(clipboardFree: false);

        var result = Payload<ClipboardCopied>(await tool.InvokeAsync(Args("""{"text":"x"}"""), Ctx(), default));

        Assert.False(result.Copied);
        Assert.Contains("in use", result.Message);
    }

    // ── cancel_search ───────────────────────────────────────────────────────

    [Fact]
    public async Task CancelSearch_StopsTheRunningSearch_OrSaysNoneIs()
    {
        var running = true;
        var tool = new CancelSearchTool(() =>
        {
            var outcome = running ? new SearchCancelOutcome(true) : new SearchCancelOutcome(false, "no search is running");
            running = false;
            return Task.FromResult(outcome);
        });

        Assert.True(Payload<SearchCancelOutcome>(await tool.InvokeAsync(Args("{}"), Ctx(), default)).Cancelled);
        Assert.Equal("no search is running", Payload<SearchCancelOutcome>(await tool.InvokeAsync(Args("{}"), Ctx(), default)).Message);
    }

    /// <summary>They change what the person sees, or has copied — never what is stored — so none is a proposal.</summary>
    [Fact]
    public void TheyAreViewState()
    {
        IMcpTool[] tools =
        [
            new ShowResearchObservationTool((_, _) => Task.FromResult(ResearchShown.Refused(""))),
            new CopyToClipboardTool(_ => null, (_, _) => Task.FromResult(true)),
            new CancelSearchTool(() => Task.FromResult(new SearchCancelOutcome(false))),
        ];
        Assert.All(tools, t => Assert.Equal(McpVerbClass.ViewState, t.VerbClass));
    }
}
