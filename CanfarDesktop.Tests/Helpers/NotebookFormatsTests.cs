using Xunit;
using CanfarDesktop.Helpers.Notebook;
using CanfarDesktop.Models.Notebook;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// The files this app opens as a notebook. Ported from the Linux build, whose two bugs these pin: a
/// script arriving as ONE cell, and Ctrl+S replacing that script with nbformat JSON.
/// </summary>
public class NotebookFormatsTests
{
    private static string Body(NotebookCell cell) => string.Concat(cell.Source);

    private static NotebookDocument Document(params (string Kind, string Source)[] cells) => new()
    {
        Cells = cells.Select(c => new NotebookCell
        {
            CellType = c.Kind,
            Source = NotebookFormats.SourceLines(c.Source),
        }).ToList(),
    };

    // ── Which format ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("a.ipynb", NotebookFormat.Ipynb)]
    [InlineData("a.py", NotebookFormat.PercentPython)]
    [InlineData("a.PY", NotebookFormat.PercentPython)]
    [InlineData("a.md", NotebookFormat.Markdown)]
    [InlineData("a.markdown", NotebookFormat.Markdown)]
    [InlineData("a.txt", NotebookFormat.PlainText)]
    [InlineData("a.log", NotebookFormat.PlainText)]
    public void TheExtensionDecidesTheFormat(string path, NotebookFormat expected)
        => Assert.Equal(expected, NotebookFormats.ForPath(path));

    /// <summary>
    /// A document, not a notebook. `.html` in particular is what a notebook is converted TO — the
    /// conversion is one way. Refusing by name lets the message say so; falling through to Ipynb
    /// produced "invalid notebook JSON", which describes the parser's disappointment rather than the
    /// user's problem.
    /// </summary>
    [Theory]
    [InlineData("report.html")]
    [InlineData("paper.pdf")]
    [InlineData("notes.docx")]
    public void AnExportFormatIsRefusedByNameRatherThanParsed(string path)
    {
        Assert.Equal(NotebookFormat.Unsupported, NotebookFormats.ForPath(path));
        Assert.Contains("not a notebook", NotebookFormats.UnsupportedReason(path));
        Assert.Contains(".ipynb", NotebookFormats.UnsupportedReason(path));
    }

    /// <summary>A file someone saved from here with no extension is far more likely a notebook than a script.</summary>
    [Fact]
    public void SomethingUnrecognisedIsAssumedToBeANotebook()
        => Assert.Equal(NotebookFormat.Ipynb, NotebookFormats.ForPath("untitled"));

    /// <summary>
    /// The kind comes from the format, not from a second reading of the extension. On Linux the MCP
    /// layer kept its own copy and it drifted within a day.
    /// </summary>
    [Theory]
    [InlineData(NotebookFormat.Ipynb, "notebook")]
    [InlineData(NotebookFormat.PercentPython, "python")]
    [InlineData(NotebookFormat.Markdown, "markdown")]
    [InlineData(NotebookFormat.PlainText, "text")]
    [InlineData(NotebookFormat.Unsupported, "other")]
    public void EveryFormatReportsItsOwnKind(NotebookFormat format, string kind)
        => Assert.Equal(kind, format.Kind());

    [Fact]
    public void ThePickerOffersEverythingTheLoaderTakes()
    {
        // One list, so the Open dialog cannot offer less than the loader accepts — which is how .txt
        // came to be openable everywhere except through the dialog.
        Assert.All(NotebookFormats.OpenableExtensions,
            ext => Assert.NotEqual(NotebookFormat.Unsupported, NotebookFormats.ForPath("a" + ext)));
    }

    // ── Percent Python ──────────────────────────────────────────────────────────────────────────

    /// <summary>The bug this fixes: a 500-line script was one block you could only run all at once.</summary>
    [Fact]
    public void AScriptIsSplitOnItsMarkers()
    {
        var cells = NotebookFormats.SplitPercent("""
            # %%
            import numpy as np

            # %%
            print(np.pi)
            """);

        Assert.Equal(2, cells.Count);
        Assert.Equal("import numpy as np", Body(cells[0]).Trim());
        Assert.Equal("print(np.pi)", Body(cells[1]).Trim());
    }

    /// <summary>An ordinary script with no markers is one code cell — which is also the old behaviour.</summary>
    [Fact]
    public void AScriptWithNoMarkersIsOneCodeCell()
    {
        var cells = NotebookFormats.SplitPercent("import numpy\nprint(1)\n");

        Assert.Single(cells);
        Assert.Equal("code", cells[0].CellType);
    }

    [Theory]
    [InlineData("# %%", "code")]
    [InlineData("#%%", "code")]
    [InlineData("# %% Load the data", "code")]     // jupytext's titled form, and what people type
    [InlineData("# %% [markdown]", "markdown")]
    [InlineData("# %% [MARKDOWN]", "markdown")]
    [InlineData("# %% [raw]", "raw")]
    [InlineData("  # %%", "code")]                  // indented
    public void EveryFormOfTheMarkerIsRecognised(string line, string kind)
        => Assert.Equal(kind, NotebookFormats.PercentMarker(line));

    [Theory]
    [InlineData("# %%%")]        // not a marker
    [InlineData("import numpy")]
    [InlineData("## %%")]
    [InlineData("")]
    public void SomethingThatIsNotAMarkerIsNotOne(string line)
        => Assert.Null(NotebookFormats.PercentMarker(line));

    [Fact]
    public void AMarkdownCellLosesItsCommentPrefixOnTheWayIn()
    {
        var cells = NotebookFormats.SplitPercent("""
            # %% [markdown]
            # # Heading
            # Some prose.

            # %%
            x = 1
            """);

        Assert.Equal("markdown", cells[0].CellType);
        Assert.Equal("# Heading\nSome prose.", Body(cells[0]).Trim());
    }

    /// <summary>
    /// The round trip is the point: open a script, save it, and it is still a script — with the same
    /// cells in it.
    /// </summary>
    [Fact]
    public void AScriptSurvivesBeingOpenedAndSaved()
    {
        const string original = "# %%\nimport numpy as np\n\n# %% [markdown]\n# Some prose.\n\n# %%\nprint(1)\n";

        var cells = NotebookFormats.SplitPercent(original);
        var written = NotebookFormats.ToPercent(new NotebookDocument { Cells = cells });
        var again = NotebookFormats.SplitPercent(written);

        Assert.Equal(cells.Count, again.Count);
        Assert.Equal(cells.Select(c => c.CellType), again.Select(c => c.CellType));
        Assert.Equal(cells.Select(c => Body(c).Trim()), again.Select(c => Body(c).Trim()));
    }

    /// <summary>A commented markdown cell keeps the file valid Python — that is the whole convention.</summary>
    [Fact]
    public void MarkdownIsCommentedOutOnTheWayBack()
    {
        var written = NotebookFormats.ToPercent(Document(("markdown", "Heading\n\nProse.")));

        Assert.Contains("# %% [markdown]", written);
        Assert.Contains("# Heading", written);
        Assert.Contains("\n#\n", written);   // a blank line becomes a bare #, not "# "
    }

    // ── Markdown ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FencedPythonBecomesACodeCellAndTheProseAroundItStaysProse()
    {
        var cells = NotebookFormats.SplitMarkdown("""
            # Analysis

            Some prose.

            ```python
            import numpy as np
            ```

            After.
            """);

        Assert.Equal(3, cells.Count);
        Assert.Equal("markdown", cells[0].CellType);
        Assert.Equal("code", cells[1].CellType);
        Assert.Equal("import numpy as np", Body(cells[1]).Trim());
        Assert.Equal("markdown", cells[2].CellType);
    }

    /// <summary>
    /// The kernel is Python. A shell or JSON block in a document is illustration, and turning it into a
    /// code cell would offer to run text that was never meant to be run.
    /// </summary>
    [Theory]
    [InlineData("bash")]
    [InlineData("json")]
    [InlineData("")]
    public void AFenceInAnotherLanguageStaysProse(string language)
    {
        var cells = NotebookFormats.SplitMarkdown($"Text.\n\n```{language}\nrm -rf /\n```\n");

        Assert.All(cells, c => Assert.Equal("markdown", c.CellType));
    }

    [Fact]
    public void AnUnterminatedFenceKeepsItsTextRatherThanDroppingIt()
    {
        var cells = NotebookFormats.SplitMarkdown("Intro.\n\n```python\nx = 1\n");

        Assert.Contains(cells, c => c.CellType == "code" && Body(c).Contains("x = 1"));
    }

    [Fact]
    public void AMarkdownFileSurvivesBeingOpenedAndSaved()
    {
        const string original = "# Title\n\nProse.\n\n```python\nx = 1\n```\n";

        var cells = NotebookFormats.SplitMarkdown(original);
        var written = NotebookFormats.ToMarkdown(new NotebookDocument { Cells = cells });
        var again = NotebookFormats.SplitMarkdown(written);

        Assert.Equal(cells.Select(c => c.CellType), again.Select(c => c.CellType));
        Assert.Equal(cells.Select(c => Body(c).Trim()), again.Select(c => Body(c).Trim()));
    }

    // ── Plain text ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// No parsing at all. Inventing a convention — splitting on blank lines, say — would take a file
    /// someone wrote as prose and cut it into pieces they did not ask for.
    /// </summary>
    [Fact]
    public void PlainTextIsOneCellAndIsNotCutUp()
    {
        var cells = NotebookFormats.SplitPlainText("Observing notes.\n\nSeeing was 1.2\".\n\nCloud after 03:00.\n");

        Assert.Single(cells);
        Assert.Equal("markdown", cells[0].CellType);
        Assert.Contains("Cloud after", Body(cells[0]));
    }

    /// <summary>
    /// A notes file that grew some analysis reopens with its cells intact, and the code is visibly code
    /// rather than run together with the prose.
    /// </summary>
    [Fact]
    public void CodeInATextFileKeepsAMarkerSoItComesBackAsCode()
    {
        var written = NotebookFormats.ToPlainText(Document(("markdown", "Notes."), ("code", "x = 1")));

        Assert.Contains("# %%", written);
        Assert.Contains("x = 1", written);
    }

    // ── Both directions ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Exactly one trailing newline. Writing a blank line at the end grew the file by one line on every
    /// save — and these are files people keep in git, where a diff that appears whenever the notebook is
    /// opened and closed is a diff that trains people to ignore diffs.
    /// </summary>
    [Theory]
    [InlineData(NotebookFormat.PercentPython)]
    [InlineData(NotebookFormat.Markdown)]
    [InlineData(NotebookFormat.PlainText)]
    public void EveryWrittenFileEndsWithExactlyOneNewline(NotebookFormat format)
    {
        var written = NotebookFormats.Serialize(format, Document(("code", "x = 1"), ("markdown", "Prose.")))!;

        Assert.EndsWith("\n", written);
        Assert.DoesNotContain("\n\n\n", written[^3..] + "|");
        Assert.False(written.EndsWith("\n\n", StringComparison.Ordinal));
    }

    /// <summary>Saving an .ipynb is the parser's job, not this module's — it says so by answering null.</summary>
    [Fact]
    public void TheNativeFormatIsNotThisModulesToWrite()
        => Assert.Null(NotebookFormats.Serialize(NotebookFormat.Ipynb, Document(("code", "x = 1"))));

    [Fact]
    public void WindowsLineEndingsAreReadTheSameAsUnixOnes()
    {
        var unix = NotebookFormats.SplitPercent("# %%\nx = 1\n# %%\ny = 2\n");
        var windows = NotebookFormats.SplitPercent("# %%\r\nx = 1\r\n# %%\r\ny = 2\r\n");

        Assert.Equal(unix.Count, windows.Count);
        Assert.Equal(unix.Select(c => Body(c).Trim()), windows.Select(c => Body(c).Trim()));
    }

    /// <summary>
    /// nbformat stores source as a list of lines that each keep their newline. A cell built from a
    /// script has to be the same shape as one loaded from JSON, or everything downstream sees two kinds.
    /// </summary>
    [Fact]
    public void CellSourceIsStoredTheWayNbformatStoresIt()
    {
        var lines = NotebookFormats.SourceLines("one\ntwo\nthree");

        Assert.Equal(["one\n", "two\n", "three"], lines);
        Assert.Empty(NotebookFormats.SourceLines(""));
    }
}
