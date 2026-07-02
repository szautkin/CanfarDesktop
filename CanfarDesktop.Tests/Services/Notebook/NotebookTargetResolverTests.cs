using CanfarDesktop.Services.Notebook;
using Xunit;

namespace CanfarDesktop.Tests.Services.Notebook;

public class NotebookTargetResolverTests
{
    private static NotebookTargetResolver.Candidate C(string id, string? path = null) => new(id, path);

    [Fact]
    public void EmptySelector_UsesActive()
    {
        var (kind, index) = NotebookTargetResolver.Resolve(new[] { C("aa"), C("bb") }, null);
        Assert.Equal(NotebookTargetKind.UseActive, kind);
        Assert.Equal(-1, index);

        Assert.Equal(NotebookTargetKind.UseActive, NotebookTargetResolver.Resolve(new[] { C("aa") }, "   ").Kind);
    }

    [Fact]
    public void MatchesById_CaseInsensitive()
    {
        var (kind, index) = NotebookTargetResolver.Resolve(new[] { C("aa"), C("bb"), C("cc") }, "BB");
        Assert.Equal(NotebookTargetKind.Resolved, kind);
        Assert.Equal(1, index);
    }

    [Fact]
    public void MatchesByFullPath()
    {
        var cands = new[] { C("aa", @"C:\nb\one.ipynb"), C("bb", @"C:\nb\two.ipynb") };
        var (kind, index) = NotebookTargetResolver.Resolve(cands, @"c:\nb\two.ipynb");
        Assert.Equal(NotebookTargetKind.Resolved, kind);
        Assert.Equal(1, index);
    }

    [Fact]
    public void MatchesByFilename_WhenNoFullPathMatch()
    {
        var cands = new[] { C("aa", @"C:\nb\one.ipynb"), C("bb", @"C:\nb\two.ipynb") };
        var (kind, index) = NotebookTargetResolver.Resolve(cands, "one.ipynb");
        Assert.Equal(NotebookTargetKind.Resolved, kind);
        Assert.Equal(0, index);
    }

    [Fact]
    public void IdBeatsPath_WhenBothCouldMatch()
    {
        // A selector equal to one notebook's id must resolve by id even if it could be a filename elsewhere.
        var cands = new[] { C("two.ipynb", @"C:\a.ipynb"), C("bb", @"C:\two.ipynb") };
        var (kind, index) = NotebookTargetResolver.Resolve(cands, "two.ipynb");
        Assert.Equal(NotebookTargetKind.Resolved, kind);
        Assert.Equal(0, index); // matched the id, not the path
    }

    [Fact]
    public void UnknownSelector_IsNotFound()
    {
        var (kind, index) = NotebookTargetResolver.Resolve(new[] { C("aa", @"C:\x.ipynb") }, "nope");
        Assert.Equal(NotebookTargetKind.NotFound, kind);
        Assert.Equal(-1, index);
    }

    [Fact]
    public void NoCandidates_WithSelector_IsNotFound()
    {
        var (kind, _) = NotebookTargetResolver.Resolve(System.Array.Empty<NotebookTargetResolver.Candidate>(), "aa");
        Assert.Equal(NotebookTargetKind.NotFound, kind);
    }
}
