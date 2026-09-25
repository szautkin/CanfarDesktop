using Xunit;
using NSubstitute;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Services;
using CanfarDesktop.ViewModels;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// Whether the work the app actually does turns up in the registry.
///
/// The registry is only worth a strip of chrome if the operations register with it. An upload that
/// reports itself only through a label on a page you have since navigated away from is exactly the state
/// the status bar was built to end, so these check the operations, not the bar.
/// </summary>
[Collection("TaskRegistry")]
public class TaskRegistrationTests : IDisposable
{
    public TaskRegistrationTests() => TaskRegistry.ResetForTests();

    public void Dispose() => TaskRegistry.ResetForTests();

    private static (StorageBrowserViewModel vm, IStorageService service) Storage()
    {
        var service = Substitute.For<IStorageService>();
        service.ListNodesAsync(Arg.Any<string>(), Arg.Any<int?>())
            .Returns(Task.FromResult(new List<VoSpaceNode>()));

        var vm = new StorageBrowserViewModel(service);
        vm.SetUsername("testuser");
        return (vm, service);
    }

    // ── Storage ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnUploadIsRegisteredAndRecordedAsDone()
    {
        var (vm, _) = Storage();

        await vm.UploadAsync("cube.fits", new MemoryStream([1, 2, 3]));

        var only = Assert.Single(TaskRegistry.Snapshot());
        Assert.Equal("Upload cube.fits", only.Label);
        Assert.Equal(TaskKind.Storage, only.Kind);
        Assert.Equal(TaskProgress.Succeeded, only.Progress);
    }

    /// <summary>
    /// The failure that matters. The page's own error text goes away with the page; this does not, so
    /// the reason a 4 GB upload was refused is still readable ten minutes later.
    /// </summary>
    [Fact]
    public async Task AFailedUploadKeepsTheServicesReason()
    {
        var (vm, service) = Storage();
        service.UploadFileAsync(Arg.Any<string>(), Arg.Any<Stream>())
            .Returns<Task>(_ => throw new HttpRequestException("413 Payload Too Large"));

        await vm.UploadAsync("cube.fits", new MemoryStream([1, 2, 3]));

        var only = Assert.Single(TaskRegistry.Snapshot());
        Assert.Equal(TaskProgress.Failed, only.Progress);
        Assert.Contains("413", only.Message);
    }

    [Fact]
    public async Task ADeleteIsRegistered()
    {
        var (vm, _) = Storage();
        vm.SelectedNode = new VoSpaceNode { Name = "old.fits", Type = VoSpaceNodeType.DataNode };

        await vm.DeleteSelectedAsync();

        var only = Assert.Single(TaskRegistry.Snapshot());
        Assert.Equal("Delete old.fits", only.Label);
        Assert.Equal(TaskProgress.Succeeded, only.Progress);
    }

    [Fact]
    public async Task ANewFolderIsRegistered()
    {
        var (vm, _) = Storage();

        await vm.CreateFolderAsync("results");

        Assert.Equal("New folder results", Assert.Single(TaskRegistry.Snapshot()).Label);
    }

    [Fact]
    public async Task ADownloadIsRegistered()
    {
        var (vm, service) = Storage();
        service.DownloadFileAsync(Arg.Any<string>()).Returns(Task.FromResult<Stream>(new MemoryStream()));
        vm.SelectedNode = new VoSpaceNode { Name = "cube.fits", Type = VoSpaceNodeType.DataNode };

        await vm.DownloadSelectedAsync();

        Assert.Equal("Download cube.fits", Assert.Single(TaskRegistry.Snapshot()).Label);
    }

    /// <summary>Nothing selected is not work, and inventing a task for it would be noise.</summary>
    [Fact]
    public async Task NothingSelectedRegistersNothing()
    {
        var (vm, _) = Storage();

        await vm.DeleteSelectedAsync();

        Assert.Empty(TaskRegistry.Snapshot());
    }

    // ── Sessions ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeletingASessionIsRegistered()
    {
        var service = Substitute.For<ISessionService>();
        service.DeleteSessionAsync("abc").Returns(Task.FromResult(true));
        service.GetSessionsAsync().Returns(Task.FromResult(new List<Session>()));

        await new SessionListViewModel(service).DeleteSessionCommand.ExecuteAsync("abc");

        var only = Assert.Single(TaskRegistry.Snapshot());
        Assert.Equal("Delete session abc", only.Label);
        Assert.Equal(TaskProgress.Succeeded, only.Progress);
    }

    /// <summary>
    /// A refusal with no exception behind it: the one shape that used to leave nothing at all — no
    /// error, no toast, and a session still sitting in the list.
    /// </summary>
    [Fact]
    public async Task ASessionDeleteTheServiceRefusesIsRecordedAsFailed()
    {
        var service = Substitute.For<ISessionService>();
        service.DeleteSessionAsync("abc").Returns(Task.FromResult(false));

        await new SessionListViewModel(service).DeleteSessionCommand.ExecuteAsync("abc");

        var only = Assert.Single(TaskRegistry.Snapshot());
        Assert.Equal(TaskProgress.Failed, only.Progress);
        Assert.Contains("refused", only.Message);
    }

    [Fact]
    public async Task RenewingASessionIsRegistered()
    {
        var service = Substitute.For<ISessionService>();
        service.GetSessionsAsync().Returns(Task.FromResult(new List<Session>()));

        await new SessionListViewModel(service).TryRenewSessionAsync("abc");

        Assert.Equal("Renew session abc", Assert.Single(TaskRegistry.Snapshot()).Label);
    }

    [Fact]
    public async Task AFailedRenewKeepsItsReason()
    {
        var service = Substitute.For<ISessionService>();
        service.RenewSessionAsync("abc").Returns<Task>(_ => throw new HttpRequestException("403 Forbidden"));

        await new SessionListViewModel(service).TryRenewSessionAsync("abc");

        var only = Assert.Single(TaskRegistry.Snapshot());
        Assert.Equal(TaskProgress.Failed, only.Progress);
        Assert.Contains("403", only.Message);
    }
}
