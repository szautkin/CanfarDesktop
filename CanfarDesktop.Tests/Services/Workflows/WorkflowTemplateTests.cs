using CanfarDesktop.Services.AiGuide;
using CanfarDesktop.Services.Workflows;
using Xunit;

namespace CanfarDesktop.Tests.Services.Workflows;

/// <summary>
/// Locks the shipped templates to the real app surface: every template must parse warning-free,
/// every `View:` must be a navigable key, and every `Tool:` must be a tool name the catalog knows
/// (AiGuideCatalog.MappedToolNames — the same universe the AI Guide uses). A template that names a
/// renamed/removed tool fails CI instead of shipping a broken protocol.
/// </summary>
public class WorkflowTemplateTests
{
    // The test assembly embeds the same template files (linked EmbeddedResource), and the linked
    // WorkflowStore loads from ITS OWN assembly — so the default provider finds them here too.
    private static readonly WorkflowStore Store = new(Path.GetTempPath());

    [Fact]
    public void EveryTemplate_IsEmbedded()
    {
        var builtins = Store.ListBuiltIn();
        Assert.Equal(17, builtins.Count);
        Assert.Contains(builtins, w => w.Id == "builtin:cfht-imaging-recon");
        Assert.Contains(builtins, w => w.Id == "builtin:variable-star-photometry");
        Assert.Contains(builtins, w => w.Id == "builtin:jcmt-cube-kinematics");
        Assert.Contains(builtins, w => w.Id == "builtin:dao-espadons-spectroscopy");
        Assert.Contains(builtins, w => w.Id == "builtin:vizier-cadc-crossmatch");
        Assert.Contains(builtins, w => w.Id == "builtin:proposal-due-diligence");
        Assert.Contains(builtins, w => w.Id == "builtin:canfar-batch-reprocessing");
        Assert.Contains(builtins, w => w.Id == "builtin:verbinal-onboarding");
    }

    /// <summary>
    /// A detailed tour for every screen the onboarding tour visits, and nothing else calling itself one.
    ///
    /// The onboarding tour and describe_app both send an agent to these by id; a screen that grew a
    /// tour, or lost one, without the other two knowing is an agent sent to a workflow that is not there.
    /// </summary>
    [Theory]
    [InlineData("search")]
    [InlineData("research")]
    [InlineData("fits-viewer")]
    [InlineData("cube-viewer")]
    [InlineData("storage")]
    [InlineData("portal")]
    [InlineData("notebook")]
    [InlineData("workflows")]
    [InlineData("ai-guide")]
    public void EveryScreenHasADetailedTour(string screen)
    {
        var tour = Assert.Single(Store.ListBuiltIn(), w => w.Id == $"builtin:tour-{screen}");
        Assert.Contains("tour", tour.Doc.Tags);
        Assert.StartsWith("Tour:", tour.Doc.Title);
    }

    [Fact]
    public void EveryTemplate_ParsesCleanly_WithRealViewsAndTools()
    {
        foreach (var wf in Store.ListBuiltIn())
        {
            var problems = WorkflowFormat.Validate(wf.Doc, WorkflowFormat.KnownViews, AiGuideCatalog.MappedToolNames);
            Assert.True(problems.Count == 0, $"{wf.Id}: {string.Join(" | ", problems)}");
            Assert.True(wf.Doc.Steps.Count >= 5, $"{wf.Id}: templates should have at least 5 steps");
            Assert.NotEqual(string.Empty, wf.Doc.Description);
            Assert.NotEmpty(wf.Doc.Tags);
            Assert.All(wf.Doc.Steps, s => Assert.False(s.Done, $"{wf.Id}: templates must ship with no steps checked"));
        }
    }
}
