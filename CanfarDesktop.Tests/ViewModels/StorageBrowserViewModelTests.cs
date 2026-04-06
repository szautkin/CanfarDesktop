using Xunit;
using NSubstitute;
using CanfarDesktop.Models;
using CanfarDesktop.Services;
using CanfarDesktop.ViewModels;

namespace CanfarDesktop.Tests.ViewModels;

public class StorageBrowserViewModelTests
{
    private static (StorageBrowserViewModel vm, IStorageService mock) CreateVm(string username = "testuser")
    {
        var mock = Substitute.For<IStorageService>();
        mock.ListNodesAsync(Arg.Any<string>(), Arg.Any<int?>())
            .Returns(Task.FromResult(new List<VoSpaceNode>()));
        var vm = new StorageBrowserViewModel(mock);
        vm.SetUsername(username);
        return (vm, mock);
    }

    // ── Path navigation ─────────────────────────────────────────────────────

    [Fact]
    public async Task NavigateTo_SetsCurrentPath()
    {
        var (vm, _) = CreateVm();
        await vm.NavigateToAsync("subfolder/deep");
        Assert.Equal("subfolder/deep", vm.CurrentPath);
    }

    [Fact]
    public async Task NavigateTo_Root_EmptyPath()
    {
        var (vm, _) = CreateVm();
        await vm.NavigateToAsync("");
        Assert.Equal("", vm.CurrentPath);
    }

    [Fact]
    public async Task NavigateTo_CallsListNodes()
    {
        var (vm, mock) = CreateVm();
        await vm.NavigateToAsync("data");
        await mock.Received(1).ListNodesAsync(Arg.Any<string>(), Arg.Any<int?>());
    }

    // ── GoUp ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GoUp_FromSubfolder_NavigatesToParent()
    {
        var (vm, _) = CreateVm();
        await vm.NavigateToAsync("a/b/c");
        await vm.GoUpCommand.ExecuteAsync(null);
        Assert.Equal("a/b", vm.CurrentPath);
    }

    [Fact]
    public async Task GoUp_FromTopLevel_NavigatesToRoot()
    {
        var (vm, _) = CreateVm();
        await vm.NavigateToAsync("myfolder");
        await vm.GoUpCommand.ExecuteAsync(null);
        Assert.Equal("", vm.CurrentPath);
    }

    [Fact]
    public async Task GoUp_AtRoot_StaysAtRoot()
    {
        var (vm, _) = CreateVm();
        await vm.NavigateToAsync("");
        await vm.GoUpCommand.ExecuteAsync(null);
        Assert.Equal("", vm.CurrentPath);
    }

    // ── Breadcrumbs ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Breadcrumbs_AtRoot_ContainsHome()
    {
        var (vm, _) = CreateVm();
        await vm.NavigateToAsync("");
        Assert.Contains("Home", vm.BreadcrumbParts);
    }

    [Fact]
    public async Task Breadcrumbs_InSubfolder_ContainsAllParts()
    {
        var (vm, _) = CreateVm();
        await vm.NavigateToAsync("data/images/fits");
        Assert.Contains("Home", vm.BreadcrumbParts);
        Assert.Contains("data", vm.BreadcrumbParts);
        Assert.Contains("images", vm.BreadcrumbParts);
        Assert.Contains("fits", vm.BreadcrumbParts);
    }

    // ── Upload path construction ────────────────────────────────────────────

    [Fact]
    public async Task Upload_AtRoot_ConstructsCorrectPath()
    {
        var (vm, mock) = CreateVm("alice");
        await vm.NavigateToAsync("");
        using var stream = new MemoryStream();
        await vm.UploadAsync("test.fits", stream);
        await mock.Received(1).UploadFileAsync("alice/test.fits", Arg.Any<Stream>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Upload_InSubfolder_ConstructsCorrectPath()
    {
        var (vm, mock) = CreateVm("alice");
        await vm.NavigateToAsync("data/raw");
        using var stream = new MemoryStream();
        await vm.UploadAsync("obs.fits", stream);
        await mock.Received(1).UploadFileAsync("alice/data/raw/obs.fits", Arg.Any<Stream>(), Arg.Any<string?>());
    }

    // ── CreateFolder path ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateFolder_AtRoot_UsesUsername()
    {
        var (vm, mock) = CreateVm("bob");
        await vm.NavigateToAsync("");
        await vm.CreateFolderCommand.ExecuteAsync("newdir");
        await mock.Received(1).CreateFolderAsync("bob", "newdir");
    }

    [Fact]
    public async Task CreateFolder_InSubfolder_IncludesPath()
    {
        var (vm, mock) = CreateVm("bob");
        await vm.NavigateToAsync("projects");
        await vm.CreateFolderCommand.ExecuteAsync("analysis");
        await mock.Received(1).CreateFolderAsync("bob/projects", "analysis");
    }

    [Fact]
    public async Task CreateFolder_EmptyName_DoesNothing()
    {
        var (vm, mock) = CreateVm();
        await vm.CreateFolderCommand.ExecuteAsync("");
        await mock.DidNotReceive().CreateFolderAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    // ── Error handling ──────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_OnFailure_SetsError()
    {
        var (vm, mock) = CreateVm();
        mock.UploadFileAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string?>())
            .Returns(Task.FromException(new HttpRequestException("Network error")));

        using var stream = new MemoryStream();
        await vm.UploadAsync("file.txt", stream);

        Assert.True(vm.HasError);
        Assert.Contains("Network error", vm.ErrorMessage);
    }

    // ── State ───────────────────────────────────────────────────────────────

    [Fact]
    public void InitialState_EmptyPath()
    {
        var (vm, _) = CreateVm();
        Assert.Equal("", vm.CurrentPath);
        Assert.False(vm.IsLoading);
        Assert.False(vm.HasError);
    }
}
