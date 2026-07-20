using CanfarDesktop.Services.Workflows;
using Xunit;

namespace CanfarDesktop.Tests.Services.Workflows;

public class WorkflowFormatTests
{
    private const string Sample = """
        # M31 archival recon
        > Find the best MegaCam imaging of M31.
        Tags: imaging, CFHT
        Time: ~1 h

        ## Steps

        - [ ] **Resolve the target** — Confirm coordinates first.
              Tool: resolve_target
              View: search
        - [x] **Query the archive** — collection CFHT, instrument MegaCam.
              Continued body line here.
              Tool: search_observations, save_query
              Note: keep radius small
        - [ ] A step without a bold lead
        """;

    [Fact]
    public void Parse_FullSample_AllFields()
    {
        var doc = WorkflowFormat.Parse(Sample);

        Assert.Equal("M31 archival recon", doc.Title);
        Assert.Equal("Find the best MegaCam imaging of M31.", doc.Description);
        Assert.Equal(new[] { "imaging", "CFHT" }, doc.Tags);
        Assert.Equal("~1 h", doc.Metadata["Time"]);
        Assert.Empty(doc.Warnings);

        Assert.Equal(3, doc.Steps.Count);
        var s0 = doc.Steps[0];
        Assert.Equal("Resolve the target", s0.Title);
        Assert.Equal("Confirm coordinates first.", s0.Body);
        Assert.Equal(new[] { "resolve_target" }, s0.Tools);
        Assert.Equal("search", s0.View);
        Assert.False(s0.Done);

        var s1 = doc.Steps[1];
        Assert.True(s1.Done);
        Assert.Equal(new[] { "search_observations", "save_query" }, s1.Tools);
        Assert.Equal("keep radius small", s1.Note);
        Assert.Contains("Continued body line here.", s1.Body);

        Assert.Equal("A step without a bold lead", doc.Steps[2].Title);
        Assert.Equal(1, doc.DoneCount);
    }

    [Fact]
    public void Parse_MissingTitleAndSteps_WarnsButNeverThrows()
    {
        var doc = WorkflowFormat.Parse("just some text\nTags: a");
        Assert.Equal("Untitled workflow", doc.Title);
        Assert.Equal(2, doc.Warnings.Count); // no title + no steps
        Assert.Empty(doc.Steps);

        Assert.Equal("Untitled workflow", WorkflowFormat.Parse("").Title);
    }

    [Fact]
    public void WithStepDone_FlipsOnlyTheMarker_PreservingEveryOtherByte()
    {
        var flipped = WorkflowFormat.WithStepDone(Sample, 0, done: true);
        Assert.Contains("- [x] **Resolve the target**", flipped);
        // Everything except that one character is untouched.
        Assert.Equal(Sample.Replace("- [ ] **Resolve the target**", "- [x] **Resolve the target**"), flipped);

        var unflipped = WorkflowFormat.WithStepDone(Sample, 1, done: false);
        Assert.Contains("- [ ] **Query the archive**", unflipped);

        Assert.Throws<ArgumentOutOfRangeException>(() => WorkflowFormat.WithStepDone(Sample, 9, true));
    }

    [Fact]
    public void WithStepDone_PreservesLineEndings_CrlfAndLf()
    {
        // The byte-preservation contract must hold regardless of the file's line-ending flavour —
        // the old implementation rewrote every CRLF as LF (reformatting Windows-authored files).
        const string crlf = "# T\r\n\r\n## Steps\r\n\r\n- [ ] **A** — first\r\n- [ ] **B** — second\r\n";
        Assert.Equal(crlf.Replace("- [ ] **B**", "- [x] **B**"), WorkflowFormat.WithStepDone(crlf, 1, true));

        const string lf = "# T\n\n## Steps\n\n- [ ] **A** — first\n- [x] **B** — second\n";
        Assert.Equal(lf.Replace("- [x] **B**", "- [ ] **B**"), WorkflowFormat.WithStepDone(lf, 1, false));
    }

    [Fact]
    public void WithStepDone_RoundTrips_ThroughParse()
    {
        var doc = WorkflowFormat.Parse(WorkflowFormat.WithStepDone(Sample, 2, true));
        Assert.True(doc.Steps[2].Done);
        Assert.Equal(2, doc.DoneCount);
    }

    [Fact]
    public void Validate_FlagsUnknownViewAndTool()
    {
        var doc = WorkflowFormat.Parse("""
            # T
            - [ ] **A** — x
                  Tool: not_a_tool
                  View: mars
            """);
        var problems = WorkflowFormat.Validate(doc, WorkflowFormat.KnownViews, new HashSet<string> { "resolve_target" });
        Assert.Contains(problems, p => p.Contains("unknown View \"mars\""));
        Assert.Contains(problems, p => p.Contains("unknown Tool \"not_a_tool\""));
    }

    [Fact]
    public void Skeleton_ParsesCleanly()
    {
        var doc = WorkflowFormat.Parse(WorkflowFormat.Skeleton("My protocol"));
        Assert.Equal("My protocol", doc.Title);
        Assert.Empty(doc.Warnings);
        Assert.Equal(2, doc.Steps.Count);
    }
}
