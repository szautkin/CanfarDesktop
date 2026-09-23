using Xunit;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// Every surface that does slow, user-visible work registers it.
///
/// The status bar is only as complete as this list. A view model that runs a network operation without
/// a task is a view model the app cannot report on — which is the state the whole registry was built to
/// end, and it comes back the moment somebody adds a page and forgets.
///
/// Listed by hand rather than inferred, so ADDING a surface to the app is a deliberate decision about
/// whether it belongs here rather than an omission nobody notices.
/// </summary>
public class SlowSurfaceCoverageTests
{
    /// <summary>
    /// The sources that must call <c>TaskRegistry.Begin</c>.
    ///
    /// Not everything slow is here yet, and that is a statement about the app rather than about the
    /// test: FITS downloads, notebook kernels and the registry browser still report only locally. When
    /// one of those is registered, it is added here — and then it cannot quietly stop.
    /// </summary>
    private static readonly string[] Registered =
    [
        // The probe pipeline, stage by stage. The reason stages exist at all.
        "Services/ImageDiscovery/ImageDiscoveryCoordinator.cs",

        // Launching a session — the operation most likely to outlive the dialog that started it.
        "ViewModels/SessionLaunchViewModel.cs",

        // Deleting and renewing an existing session.
        "ViewModels/SessionListViewModel.cs",

        // Uploads, downloads, deletes and new folders against VOSpace.
        "ViewModels/StorageBrowserViewModel.cs",
    ];

    [Fact]
    public void EverySlowSurfaceRegistersItsWork()
    {
        var missing = Registered
            .Where(rel => !File.ReadAllText(RepoFiles.PathTo(rel)).Contains("TaskRegistry.Begin"))
            .ToList();

        Assert.True(missing.Count == 0,
            $"these do slow work without registering it, so the status bar cannot report on them: " +
            string.Join(", ", missing));
    }

    /// <summary>
    /// The two surfaces that poll CANFAR and raise notifications from what they see.
    ///
    /// Every notification either raises is a side effect of a poll, so a fixed interval here is a fixed
    /// notification delay. One of these going back to a constant would put that delay back without
    /// anything else changing visibly.
    /// </summary>
    private static readonly string[] Pollers =
    [
        "ViewModels/SessionListViewModel.cs",
        "Views/Controls/BatchJobsControl.xaml.cs",
    ];

    [Fact]
    public void EverySurfaceThatPollsAndNotifiesUsesTheAdaptiveCadence()
    {
        var fixedInterval = Pollers
            .Where(rel => !File.ReadAllText(RepoFiles.PathTo(rel)).Contains("PollCadence"))
            .ToList();

        Assert.True(fixedInterval.Count == 0,
            "these poll on a fixed interval, so their notifications are that late: " +
            string.Join(", ", fixedInterval));
    }

    /// <summary>A path in the list that no longer exists would make the guard above pass by accident.</summary>
    [Fact]
    public void EveryListedSourceIsStillThere()
    {
        var gone = Registered.Concat(Pollers)
            .Where(rel => !File.Exists(RepoFiles.PathTo(rel)))
            .ToList();

        Assert.True(gone.Count == 0, $"listed but no longer in the tree: {string.Join(", ", gone)}");
    }
}
