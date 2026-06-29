using Xunit;
using CanfarDesktop.Services.AiGuide;
using CanfarDesktop.Services.Database;
using CanfarDesktop.ViewModels;

namespace CanfarDesktop.Tests.ViewModels;

/// <summary>
/// Tests the AI Guide dashboard ViewModel's pure logic (grouping, override edit/reset, filtering)
/// over a real in-memory <see cref="AiGuideService"/> and a fake tool-input provider.
/// </summary>
public class AiGuideViewModelTests : IDisposable
{
    private readonly AppDatabase _db = new(filePath: null);
    private readonly AiGuideService _service;

    private static readonly IReadOnlyList<AiGuideToolInput> Inputs = new[]
    {
        new AiGuideToolInput("get_auth_state", "Auth state.", "foundational"),
        new AiGuideToolInput("search_observations", "Search CADC.", "search"),
        new AiGuideToolInput("list_sessions", "List sessions.", "sessions"),
    };

    public AiGuideViewModelTests()
        => _service = new AiGuideService(new AiGuideStore(_db, deviceId: "test"));

    public void Dispose() => _db.Dispose();

    private AiGuideViewModel NewVm() => new(_service, () => Inputs);

    [Fact]
    public void Load_GroupsByCategory_InCatalogOrder()
    {
        var vm = NewVm();
        vm.Load();

        Assert.Equal(3, vm.ToolCount);
        Assert.Equal(new[] { "foundational", "search", "sessions" },
            vm.Categories.Select(c => c.Id).ToArray()); // catalog order, not input order
        Assert.Equal(3, vm.CategoryCount);
        Assert.Equal(0, vm.OverriddenCount);
    }

    [Fact]
    public void SaveOverride_PersistsAndUpdatesRowAndStats()
    {
        var vm = NewVm();
        vm.Load();
        var row = vm.Categories.SelectMany(c => c.Tools).Single(t => t.Name == "search_observations");

        row.EditText = "Find images by sky position.";
        vm.SaveOverride(row);

        Assert.True(row.IsOverridden);
        Assert.Equal("Find images by sky position.", row.EffectiveDescription);
        Assert.False(row.IsExpanded);
        Assert.Equal(1, vm.OverriddenCount);
        Assert.True(_service.IsOverridden("search_observations"));
        Assert.Contains(vm.Categories, c => c.Id == "search" && c.HasOverrides);
    }

    [Fact]
    public void SaveOverride_TooLong_SetsErrorAndDoesNotPersist()
    {
        var vm = NewVm();
        vm.Load();
        var row = vm.Categories.SelectMany(c => c.Tools).First();

        row.EditText = new string('x', AiGuideService.MaxDescriptionChars + 1);
        vm.SaveOverride(row);

        Assert.True(row.HasError);
        Assert.False(row.IsOverridden);
        Assert.False(_service.IsOverridden(row.Name));
    }

    [Fact]
    public void ResetOverride_ClearsIt()
    {
        var vm = NewVm();
        vm.Load();
        var row = vm.Categories.SelectMany(c => c.Tools).Single(t => t.Name == "list_sessions");
        row.EditText = "mine";
        vm.SaveOverride(row);
        Assert.True(row.IsOverridden);

        vm.ResetOverride(row);
        Assert.False(row.IsOverridden);
        Assert.Equal("List sessions.", row.EffectiveDescription);
        Assert.Equal(0, vm.OverriddenCount);
    }

    [Fact]
    public void Search_PopulatesFlatResults()
    {
        var vm = NewVm();
        vm.Load();
        Assert.False(vm.IsSearching);

        vm.SearchText = "auth";
        Assert.True(vm.IsSearching);
        var hit = Assert.Single(vm.SearchResults);
        Assert.Equal("get_auth_state", hit.Name);

        vm.SearchText = ""; // cleared → flat results gone, sections return
        Assert.False(vm.IsSearching);
        Assert.Empty(vm.SearchResults);
    }

    [Fact]
    public void ShowTiles_DefaultsTrue_AndPairsWithEverything()
    {
        var vm = NewVm();
        Assert.True(vm.ShowTiles);       // launchpad is the default mode (1:1 with macOS)
        Assert.False(vm.ShowEverything);

        vm.ShowEverything = true;
        Assert.False(vm.ShowTiles);
        Assert.True(vm.ShowEverything);
    }

    [Fact]
    public void OpenCategory_ThenClose_TogglesFocusPanel()
    {
        var vm = NewVm();
        vm.Load();
        var cat = vm.Categories.First();

        vm.OpenCategory(cat);
        Assert.True(vm.IsFocusOpen);
        Assert.Same(cat, vm.FocusedCategory);

        vm.CloseFocus();
        Assert.False(vm.IsFocusOpen);
        Assert.Null(vm.FocusedCategory);
    }

    [Fact]
    public void Search_SupersedesOpenFocusPanel()
    {
        var vm = NewVm();
        vm.Load();
        vm.OpenCategory(vm.Categories.First());
        Assert.True(vm.IsFocusOpen);

        vm.SearchText = "auth";
        Assert.False(vm.IsFocusOpen); // overlay closes when search starts
    }

    [Fact]
    public void Guides_LoadAndDelete()
    {
        var entry = _service.AddGuide("Survey Tips", "How to survey", "Body.");
        var vm = NewVm();
        vm.Load();

        Assert.True(vm.HasGuides);
        Assert.Contains(vm.Guides, g => g.Name == "survey_tips");

        vm.DeleteGuide(entry.Id);
        Assert.False(vm.HasGuides);
        Assert.Empty(vm.Guides);
    }
}
