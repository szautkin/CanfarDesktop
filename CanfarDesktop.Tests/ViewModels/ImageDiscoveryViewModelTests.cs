using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Models.ImageDiscovery;
using CanfarDesktop.Services.ImageDiscovery;
using CanfarDesktop.ViewModels.ImageDiscovery;

namespace CanfarDesktop.Tests.ViewModels;

public class ImageDiscoveryViewModelTests
{
    // ── Fakes (the VM only touches the store via the coordinator) ─────────────

    private sealed class NoHeadless : IHeadlessProbeLauncher
    {
        public Task<IReadOnlyList<string>> LaunchHeadlessAsync(SessionLaunchParams p, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<IReadOnlyList<HeadlessJobStatus>> GetHeadlessJobsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<HeadlessJobStatus>>(Array.Empty<HeadlessJobStatus>());
        public Task<string> GetLogsAsync(string id, CancellationToken ct) => Task.FromResult("");
        public Task<string> GetEventsAsync(string id, CancellationToken ct) => Task.FromResult("");
    }

    private sealed class NoVoSpace : IVoSpaceFileTransfer
    {
        public Task UploadFileAsync(string u, string r, string l, CancellationToken ct) => Task.CompletedTask;
        public Task<string> DownloadToTempAsync(string u, string p, CancellationToken ct) => throw new FileNotFoundException();
        public Task EnsureFolderAsync(string u, string p, string f, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoScripts : IProbeScriptProvider
    {
        public string HomeSubdirectory => ".verbinal";
        public string ProbeBody => "#"; public string InspectorBody => "#";
        public string ProbeUploadFileName => "probe-x.sh"; public string InspectorUploadFileName => "inspector-x.sh";
    }

    private static ImageManifest M(string id, string fam, string ver, string[]? py = null)
        => new()
        {
            ImageID = id,
            OsFamily = fam,
            OsVersion = ver,
            CapturedAt = DateTimeOffset.UnixEpoch,
            PythonPackages = (py ?? Array.Empty<string>()).Select(n => new PythonPackage(n, "1", "pip", "system")).ToArray(),
        };

    private static RawImage R(string id, params string[] types) => new() { Id = id, Types = types };

    private static ImageDiscoveryViewModel BuildVM(ImageManifest[] manifests, RawImage[] catalogue)
    {
        var dir = Path.Combine(Path.GetTempPath(), "vmtest-" + Guid.NewGuid().ToString("N"));
        var store = new JsonManifestStore(dir);
        foreach (var m in manifests) store.SetManifest(m);
        var coord = new ImageDiscoveryCoordinator(store, new NoHeadless(), new NoVoSpace(), new NoScripts(), () => "alice");
        var vm = new ImageDiscoveryViewModel(coord);
        vm.Load(catalogue);
        return vm;
    }

    private static FacetValueViewModel Val(ImageDiscoveryViewModel vm, PackageQuery.Category cat, string value)
        => vm.FilterSections.First(s => s.Category == cat).Values.First(v => v.Value == value);

    private static IEnumerable<string> FilteredIds(ImageDiscoveryViewModel vm)
        => vm.FilteredGroups.SelectMany(g => g.Images).Select(r => r.Id);

    // sample fixture: two discovered images on different distros + one undiscovered
    private static ImageDiscoveryViewModel Sample()
        => BuildVM(
            new[]
            {
                M("images.canfar.net/skaha/astroml:24.07", "ubuntu", "22.04", py: new[] { "numpy", "astropy" }),
                M("images.canfar.net/skaha/carta:5.0.3", "almalinux", "9", py: new[] { "numpy", "scipy" }),
            },
            new[]
            {
                R("images.canfar.net/skaha/astroml:24.07", "notebook"),
                R("images.canfar.net/skaha/carta:5.0.3", "carta"),
                R("images.canfar.net/cadc/vos:3.4", "notebook", "headless"),
            });

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_BuildsSectionsInPinnedOrder()
    {
        var vm = Sample();
        var cats = vm.FilterSections.Select(s => s.Category).ToList();

        Assert.Equal(PackageQuery.Category.OsFamily, cats[0]);
        Assert.True(cats.IndexOf(PackageQuery.Category.OsFamily) < cats.IndexOf(PackageQuery.Category.Python));
        Assert.Contains(PackageQuery.Category.Python, cats);
        // OS family section holds both distros.
        Assert.Equal(new[] { "almalinux", "ubuntu" },
            vm.FilterSections.First(s => s.Category == PackageQuery.Category.OsFamily).Values.Select(v => v.Value));
    }

    [Fact]
    public void EmptyQuery_ShowsAllCatalogueRows_IncludingUndiscovered()
    {
        var vm = Sample();
        Assert.Equal(3, FilteredIds(vm).Count()); // astroml + carta + vos(undiscovered)
        Assert.False(vm.HasActiveFilters);
    }

    [Fact]
    public void TogglingPackage_CollapsesToMatching_AndBuildsChip()
    {
        var vm = Sample();
        Val(vm, PackageQuery.Category.Python, "scipy").IsSelected = true;

        Assert.Equal(new[] { "images.canfar.net/skaha/carta:5.0.3" }, FilteredIds(vm)); // only carta has scipy
        Assert.True(vm.HasActiveFilters);
        var chip = Assert.Single(vm.ActiveChips);
        Assert.Equal(PackageQuery.Category.Python, chip.Category);
        Assert.Equal("scipy", chip.Value);
    }

    [Fact]
    public void Faceting_DisablesValuesThatWouldYieldZero()
    {
        var vm = Sample();
        Val(vm, PackageQuery.Category.OsFamily, "ubuntu").IsSelected = true;

        // scipy exists only on almalinux → with ubuntu selected it would yield nothing → disabled.
        Assert.False(Val(vm, PackageQuery.Category.Python, "scipy").IsEnabled);
        Assert.True(Val(vm, PackageQuery.Category.Python, "numpy").IsEnabled);  // numpy on ubuntu too
        Assert.True(Val(vm, PackageQuery.Category.Python, "astropy").IsEnabled);
    }

    [Fact]
    public void RemoveChip_UnticksCheckbox_AndRestoresResults()
    {
        var vm = Sample();
        Val(vm, PackageQuery.Category.Python, "scipy").IsSelected = true;
        var chip = vm.ActiveChips.Single();

        vm.RemoveChipCommand.Execute(chip);

        Assert.Empty(vm.ActiveChips);
        Assert.False(Val(vm, PackageQuery.Category.Python, "scipy").IsSelected);
        Assert.Equal(3, FilteredIds(vm).Count());
        Assert.False(vm.HasActiveFilters);
    }

    [Fact]
    public void ClearAllFilters_ResetsQueryTypeAndCheckboxes()
    {
        var vm = Sample();
        Val(vm, PackageQuery.Category.Python, "numpy").IsSelected = true;
        vm.SelectedSessionType = "carta";

        vm.ClearAllFiltersCommand.Execute(null);

        Assert.True(vm.Query.IsEmpty);
        Assert.Equal(ImageDiscoveryViewModel.AllTypesOption, vm.SelectedSessionType);
        Assert.False(vm.HasActiveFilters);
        Assert.Empty(vm.ActiveChips);
        Assert.Equal(3, FilteredIds(vm).Count());
    }

    [Fact]
    public void SessionTypeFilter_NarrowsByType_CacheOnlyNeverHidden()
    {
        var vm = BuildVM(
            new[] { M("r/skaha/astroml:1", "ubuntu", "22.04") },
            new[]
            {
                R("r/skaha/astroml:1", "notebook"),
                R("r/skaha/carta:5", "carta"),
                R("r/local/cacheonly:1"), // no declared types
            });

        vm.SelectedSessionType = "carta";

        var ids = FilteredIds(vm).ToList();
        Assert.Contains("r/skaha/carta:5", ids);
        Assert.Contains("r/local/cacheonly:1", ids); // empty-types image is never hidden by type filter
        Assert.DoesNotContain("r/skaha/astroml:1", ids); // notebook-only → hidden
    }

    [Fact]
    public void OsVersionSection_ScopedToSelectedFamily()
    {
        var vm = Sample();

        // Before any family selection, both versions are candidates.
        var before = vm.FilterSections.First(s => s.Category == PackageQuery.Category.OsVersion).Values.Select(v => v.Value);
        Assert.Equal(new[] { "22.04", "9" }, before.OrderBy(x => x));

        Val(vm, PackageQuery.Category.OsFamily, "ubuntu").IsSelected = true;

        var after = vm.FilterSections.First(s => s.Category == PackageQuery.Category.OsVersion).Values.Select(v => v.Value);
        Assert.Equal(new[] { "22.04" }, after); // only ubuntu's version remains
    }

    [Fact]
    public void DiscoveredSubtitle_CountsDiscoveredOfTotal()
    {
        var vm = Sample();
        Assert.Equal("Discovered 2 of 3 images", vm.DiscoveredSubtitle);
    }

    [Fact]
    public void SessionTypeOptions_AllFirstThenCanonicalOrder()
    {
        var vm = Sample();
        Assert.Equal(new[] { "All", "notebook", "carta", "headless" }, vm.SessionTypeOptions);
    }

    [Fact]
    public void FilterItems_FlattensHeadersAndValues_ForVirtualizedList()
    {
        var vm = Sample();
        // Each section header is followed by its value rows.
        Assert.Contains(vm.FilterItems, o => o is FacetSectionViewModel);
        Assert.Contains(vm.FilterItems, o => o is FacetValueViewModel f && f.Value == "numpy");
    }

    [Fact]
    public void PackageSearch_FiltersFilterItems()
    {
        var vm = Sample();
        vm.PackageSearchText = "scipy";

        var values = vm.FilterItems.OfType<FacetValueViewModel>().Select(v => v.Value).ToList();
        Assert.Contains("scipy", values);
        Assert.DoesNotContain("numpy", values);
        // Sections with no surviving values drop out entirely.
        Assert.DoesNotContain(vm.FilterItems.OfType<FacetSectionViewModel>(), s => s.Category == PackageQuery.Category.OsFamily);
    }
}
