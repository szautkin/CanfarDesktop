using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Services.Export;

namespace CanfarDesktop.Tests.Services;

public class SearchExportBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);

    private static List<SavedQuery> SampleSaved() => new()
    {
        new SavedQuery { Name = "My Query", Adql = "SELECT * FROM caom2.Observation", SavedAt = DateTime.UtcNow },
    };

    private static List<RecentSearch> SampleRecent() => new()
    {
        new RecentSearch { Summary = "M31 search", Adql = "SELECT TOP 10 * FROM x", ResultCount = 42, SearchedAt = DateTime.UtcNow },
    };

    [Fact]
    public void Build_WritesJsonCountsAndFencedSql()
    {
        var output = SearchExportBuilder.Build(SampleSaved(), SampleRecent(), new ExportOptions(), Now);

        Assert.True(output.JsonFiles.ContainsKey("saved_queries.json"));
        Assert.True(output.JsonFiles.ContainsKey("recent_searches.json"));
        Assert.Equal(1, output.ItemCounts["saved_queries"]);
        Assert.Equal(1, output.ItemCounts["recent_searches"]);

        var md = output.MarkdownFiles["queries.md"];
        Assert.Contains("### My Query", md);
        Assert.Contains("```sql", md);
        Assert.Contains("SELECT * FROM caom2.Observation", md);
        Assert.Contains("## Recent Searches", md);
        Assert.Contains("M31 search", md);
    }

    [Fact]
    public void Build_IncludeSearchHistoryFalse_OmitsRecent()
    {
        var output = SearchExportBuilder.Build(SampleSaved(), SampleRecent(), new ExportOptions { IncludeSearchHistory = false }, Now);

        Assert.False(output.JsonFiles.ContainsKey("recent_searches.json"));
        Assert.False(output.ItemCounts.ContainsKey("recent_searches"));
        Assert.DoesNotContain("## Recent Searches", output.MarkdownFiles["queries.md"]);
    }

    [Fact]
    public void Build_Empty_ShowsEmptyMessage()
    {
        var output = SearchExportBuilder.Build(new List<SavedQuery>(), new List<RecentSearch>(), new ExportOptions(), Now);
        Assert.Contains("No saved queries or recent searches yet", output.MarkdownFiles["queries.md"]);
    }
}
