using Xunit;
using CanfarDesktop.Services.AiGuide;
using CanfarDesktop.Tests.Mcp;

namespace CanfarDesktop.Tests.Services;

/// <summary>
/// Guards the AI Guide category mapping: every tool the app declares must land in a real category
/// (never silently in "Other"), and the category set stays well-formed.
/// </summary>
public class AiGuideCatalogTests
{
    /// <summary>
    /// Read from the sources rather than copied by hand. The hand-copied list this replaces could only
    /// catch a tool somebody had remembered to copy into it, and 29 tools — every mark tool, figure
    /// export, the image registry — reached the server without it, and sat under "Other" in the live
    /// AI Guide.
    /// </summary>
    private static IEnumerable<string> LiveToolNames => McpToolSources.All.Select(d => d.Tool);

    [Fact]
    public void EveryLiveTool_MapsToARealCategory()
    {
        var uncategorized = LiveToolNames
            .Where(n => AiGuideCatalog.CategoryIdForTool(n) == AiGuideCatalog.Other.Id)
            .ToList();
        Assert.True(uncategorized.Count == 0, $"Uncategorized tools: {string.Join(", ", uncategorized)}");
    }

    [Fact]
    public void EveryMappedCategory_IsADefinedCategory()
    {
        // Every category id a tool resolves to must be a real, defined category.
        foreach (var name in LiveToolNames)
        {
            var cat = AiGuideCatalog.CategoryForTool(name);
            Assert.NotEqual(AiGuideCatalog.Other.Id, cat.Id);
            Assert.Contains(AiGuideCatalog.Categories, c => c.Id == cat.Id);
        }
    }

    [Fact]
    public void UnknownTool_FallsBackToOther()
    {
        Assert.Equal("other", AiGuideCatalog.CategoryIdForTool("totally_made_up_tool"));
        Assert.Equal(AiGuideCatalog.Other, AiGuideCatalog.CategoryForTool("totally_made_up_tool"));
    }

    [Fact]
    public void Categories_AreWellFormed()
    {
        var ids = AiGuideCatalog.Categories.Select(c => c.Id).ToList();
        Assert.Equal(ids.Distinct().Count(), ids.Count);              // unique ids
        Assert.DoesNotContain("other", ids);                          // Other is separate
        Assert.Equal("foundational", ids.First());                    // ordered render
        Assert.Equal("guide", ids.Last());
        Assert.All(AiGuideCatalog.Categories, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Title));
            Assert.False(string.IsNullOrWhiteSpace(c.Summary));
        });
    }

    [Fact]
    public void AllCategories_IncludesOtherLast()
    {
        Assert.Equal(AiGuideCatalog.Categories.Count + 1, AiGuideCatalog.AllCategories.Count);
        Assert.Equal(AiGuideCatalog.Other, AiGuideCatalog.AllCategories[^1]);
    }

    [Fact]
    public void CategoryById_UnknownReturnsOther()
        => Assert.Equal(AiGuideCatalog.Other, AiGuideCatalog.CategoryById("nope"));
}
