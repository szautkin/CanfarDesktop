using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Services;
using CanfarDesktop.ViewModels;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CanfarDesktop.Tests.ViewModels;

public class SessionListViewModelTests
{
    private static (SessionListViewModel vm, ISessionService mock) CreateViewModel()
    {
        var mock = Substitute.For<ISessionService>();
        mock.GetSessionsAsync().Returns(new List<Session>());
        var vm = new SessionListViewModel(mock);
        return (vm, mock);
    }

    [Fact]
    public async Task TryRenewSessionAsync_Success_ReturnsTrueAndReloadsSessions()
    {
        var (vm, mock) = CreateViewModel();

        var (success, error) = await vm.TryRenewSessionAsync("test-123");

        Assert.True(success);
        Assert.Null(error);
        await mock.Received(1).RenewSessionAsync("test-123");
        await mock.Received(1).GetSessionsAsync();
    }

    [Fact]
    public async Task TryRenewSessionAsync_HttpFailure_ReturnsFalseWithMessage()
    {
        var (vm, mock) = CreateViewModel();
        mock.RenewSessionAsync("test-123")
            .ThrowsAsync(new HttpRequestException("Renew failed: 403 Forbidden - token expired"));

        var (success, error) = await vm.TryRenewSessionAsync("test-123");

        Assert.False(success);
        Assert.NotNull(error);
        Assert.Contains("403", error);
    }

    [Fact]
    public async Task TryRenewSessionAsync_UnexpectedError_ReturnsFalseWithMessage()
    {
        var (vm, mock) = CreateViewModel();
        mock.RenewSessionAsync("test-123")
            .ThrowsAsync(new InvalidOperationException("Something broke"));

        var (success, error) = await vm.TryRenewSessionAsync("test-123");

        Assert.False(success);
        Assert.NotNull(error);
        Assert.Contains("Something broke", error);
    }

    [Fact]
    public async Task RenewSessionCommand_DelegatesToTryRenew()
    {
        var (vm, mock) = CreateViewModel();

        await vm.RenewSessionCommand.ExecuteAsync("test-123");

        await mock.Received(1).RenewSessionAsync("test-123");
    }

    [Fact]
    public async Task GetSessionEventsAsync_DelegatesToService()
    {
        var (vm, mock) = CreateViewModel();
        mock.GetSessionEventsAsync("test-123").Returns("event data");

        var result = await vm.GetSessionEventsAsync("test-123");

        Assert.Equal("event data", result);
        await mock.Received(1).GetSessionEventsAsync("test-123");
    }

    [Fact]
    public async Task GetSessionLogsAsync_DelegatesToService()
    {
        var (vm, mock) = CreateViewModel();
        mock.GetSessionLogsAsync("test-123").Returns("log data");

        var result = await vm.GetSessionLogsAsync("test-123");

        Assert.Equal("log data", result);
        await mock.Received(1).GetSessionLogsAsync("test-123");
    }
}
