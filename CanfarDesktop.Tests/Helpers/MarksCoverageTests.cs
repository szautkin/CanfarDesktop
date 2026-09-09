using Xunit;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// Structural guards on the marks UI.
///
/// These are about SHAPE rather than behaviour: that both viewers reach marks the same way, that
/// neither grows back the flyout that made naming one unreliable, and that simplifying what a person
/// is offered did not narrow what an agent can ask for. Each is a regression that would not fail any
/// other test, because each leaves every individual piece working.
/// </summary>
public class MarksCoverageTests
{
    /// <summary>
    /// Both viewers reach their marks through the one shared panel.
    ///
    /// The FITS canvas is flat and the cube's is not, but everything a person does to a mark that is
    /// not a gesture on the image is the same act in both. A second panel would be a second place to
    /// fix a bug in the list — and, worse, would teach that marks behave differently depending on what
    /// you have open.
    /// </summary>
    [Fact]
    public void BothViewersUseTheOneMarksPanel()
    {
        var root = RepoRoot();

        var without = new[]
            {
                "Views/CubeViewer/CubeViewerPage.xaml",
                "Views/FitsViewer/FitsViewerPage.xaml",
            }
            .Where(rel => !File.ReadAllText(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)))
                .Contains("MarksPanel"))
            .ToList();

        Assert.True(without.Count == 0,
            "these viewers have marks but no Marks panel, so there is no way to list or restyle them: " +
            string.Join(", ", without));
    }

    /// <summary>
    /// Naming a mark is an ordinary control, not a flyout.
    ///
    /// A flyout dismissed itself the moment the pointer went back to the image, committed on close so
    /// Escape saved instead of cancelling, and had nowhere to put a bin. Both viewers had their own
    /// copy of that, and this is what stops either growing it back.
    /// </summary>
    [Fact]
    public void NeitherViewerNamesAMarkInAFlyout()
    {
        var root = RepoRoot();

        var flyouts = new[]
            {
                "Views/CubeViewer/CubeViewerPage.Annotations.cs",
                "Views/FitsViewer/FitsViewerPage.Annotations.cs",
            }
            .Where(rel => File.ReadAllText(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)))
                .Contains("new Flyout"))
            .ToList();

        Assert.True(flyouts.Count == 0,
            "these name a mark in a flyout, which dismisses on pointer-out and saves on Escape: " +
            string.Join(", ", flyouts));
    }

    /// <summary>
    /// The shape picker offers two shapes; the MODEL and MCP still carry four.
    ///
    /// A callout was a small circle with a leader and every shape has a leader now, and a text was a
    /// label with nothing to point at — so neither is a choice worth making a person think about. But
    /// marks already on disk hold them and an agent can still ask for them, and dropping either from
    /// the tool surface would silently break both.
    /// </summary>
    [Fact]
    public void TheKindsTheShapePickerDroppedAreStillReachableByAnAgent()
    {
        var tools = File.ReadAllText(Path.Combine(RepoRoot(),
            "Mcp", "Tools", "Write", "AnnotationTools.cs"));

        Assert.Contains("\"callout\"", tools);
        Assert.Contains("\"text\"", tools);
    }

    /// <summary>Up from the test binary until the app's project file turns up.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CanfarDesktop.csproj")))
            dir = dir.Parent;

        Assert.True(dir is not null, "could not find the repository root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
