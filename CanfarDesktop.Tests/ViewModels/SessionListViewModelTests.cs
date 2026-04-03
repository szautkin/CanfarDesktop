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

    private static Session MakeSession(string id, string type, string status = "Running") => new()
    {
        Id = id,
        SessionType = type,
        SessionName = $"session-{id}",
        Status = status,
        ContainerImage = $"images.canfar.net/test/{type}:latest"
    };

    #region LoadSessionsAsync – headless filtering

    [Fact]
    public async Task LoadSessions_ExcludesHeadlessFromSessions()
    {
        var (vm, mock) = CreateViewModel();
        mock.GetSessionsAsync().Returns(new List<Session>
        {
            MakeSession("1", "notebook"),
            MakeSession("2", "headless"),
            MakeSession("3", "desktop"),
        });

        await vm.LoadSessionsCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Sessions.Count);
        Assert.All(vm.Sessions, s => Assert.NotEqual("headless", s.SessionType));
    }

    [Fact]
    public async Task LoadSessions_OnlyIncludesNonHeadless()
    {
        var (vm, mock) = CreateViewModel();
        mock.GetSessionsAsync().Returns(new List<Session>
        {
            MakeSession("1", "notebook"),
            MakeSession("2", "headless"),
            MakeSession("3", "headless"),
        });

        await vm.LoadSessionsCommand.ExecuteAsync(null);

        Assert.Single(vm.Sessions);
        Assert.Equal("notebook", vm.Sessions[0].SessionType);
    }

    [Fact]
    public async Task LoadSessions_HeadlessFilterIsCaseInsensitive()
    {
        var (vm, mock) = CreateViewModel();
        mock.GetSessionsAsync().Returns(new List<Session>
        {
            MakeSession("1", "Headless"),
            MakeSession("2", "HEADLESS"),
            MakeSession("3", "notebook"),
        });

        await vm.LoadSessionsCommand.ExecuteAsync(null);

        Assert.Single(vm.Sessions);
        Assert.Equal("notebook", vm.Sessions[0].SessionType);
    }

    [Fact]
    public async Task LoadSessions_NoHeadless_AllSessionsIncluded()
    {
        var (vm, mock) = CreateViewModel();
        mock.GetSessionsAsync().Returns(new List<Session>
        {
            MakeSession("1", "notebook"),
            MakeSession("2", "desktop"),
        });

        await vm.LoadSessionsCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Sessions.Count);
    }

    [Fact]
    public async Task LoadSessions_AllHeadless_SessionsIsEmpty()
    {
        var (vm, mock) = CreateViewModel();
        mock.GetSessionsAsync().Returns(new List<Session>
        {
            MakeSession("1", "headless"),
            MakeSession("2", "headless"),
        });

        await vm.LoadSessionsCommand.ExecuteAsync(null);

        Assert.Empty(vm.Sessions);
    }

    #endregion

    #region LoadSessionsAsync – state management

    [Fact]
    public async Task LoadSessions_FiresSessionsRefreshed()
    {
        var (vm, _) = CreateViewModel();
        var fired = false;
        vm.SessionsRefreshed += (_, _) => fired = true;

        await vm.LoadSessionsCommand.ExecuteAsync(null);

        Assert.True(fired);
    }

    [Fact]
    public async Task LoadSessions_ClearsExistingSessions()
    {
        var (vm, mock) = CreateViewModel();
        mock.GetSessionsAsync().Returns(
            new List<Session> { MakeSession("1", "notebook"), MakeSession("2", "desktop") },
            new List<Session> { MakeSession("3", "carta") });

        await vm.LoadSessionsCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Sessions.Count);

        await vm.LoadSessionsCommand.ExecuteAsync(null);
        Assert.Single(vm.Sessions);
        Assert.Equal("3", vm.Sessions[0].Id);
    }

    [Fact]
    public async Task LoadSessions_HttpError_SetsHasErrorAndMessage()
    {
        var (vm, mock) = CreateViewModel();
        mock.GetSessionsAsync().ThrowsAsync(new HttpRequestException("Network unreachable"));

        await vm.LoadSessionsCommand.ExecuteAsync(null);

        Assert.True(vm.HasError);
        Assert.Contains("Network unreachable", vm.ErrorMessage);
    }

    [Fact]
    public async Task LoadSessions_UnexpectedError_SetsHasErrorAndMessage()
    {
        var (vm, mock) = CreateViewModel();
        mock.GetSessionsAsync().ThrowsAsync(new InvalidOperationException("oops"));

        await vm.LoadSessionsCommand.ExecuteAsync(null);

        Assert.True(vm.HasError);
        Assert.Contains("oops", vm.ErrorMessage);
    }

    [Fact]
    public async Task LoadSessions_IsLoadingFalseAfterCompletion()
    {
        var (vm, _) = CreateViewModel();

        await vm.LoadSessionsCommand.ExecuteAsync(null);

        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task LoadSessions_IsLoadingFalseAfterError()
    {
        var (vm, mock) = CreateViewModel();
        mock.GetSessionsAsync().ThrowsAsync(new HttpRequestException("fail"));

        await vm.LoadSessionsCommand.ExecuteAsync(null);

        Assert.False(vm.IsLoading);
    }

    #endregion

    #region HasPendingSessions

    [Fact]
    public async Task HasPendingSessions_TrueWhenPendingExists()
    {
        var (vm, mock) = CreateViewModel();
        mock.GetSessionsAsync().Returns(new List<Session>
        {
            MakeSession("1", "notebook", "Running"),
            MakeSession("2", "desktop", "Pending"),
        });

        await vm.LoadSessionsCommand.ExecuteAsync(null);

        Assert.True(vm.HasPendingSessions());
    }

    [Fact]
    public async Task HasPendingSessions_TrueWhenTerminatingExists()
    {
        var (vm, mock) = CreateViewModel();
        mock.GetSessionsAsync().Returns(new List<Session>
        {
            MakeSession("1", "notebook", "Terminating"),
        });

        await vm.LoadSessionsCommand.ExecuteAsync(null);

        Assert.True(vm.HasPendingSessions());
    }

    [Fact]
    public async Task HasPendingSessions_FalseWhenAllRunning()
    {
        var (vm, mock) = CreateViewModel();
        mock.GetSessionsAsync().Returns(new List<Session>
        {
            MakeSession("1", "notebook", "Running"),
            MakeSession("2", "desktop", "Running"),
        });

        await vm.LoadSessionsCommand.ExecuteAsync(null);

        Assert.False(vm.HasPendingSessions());
    }

    [Fact]
    public async Task HasPendingSessions_IgnoresHeadlessPending()
    {
        var (vm, mock) = CreateViewModel();
        mock.GetSessionsAsync().Returns(new List<Session>
        {
            MakeSession("1", "notebook", "Running"),
            MakeSession("2", "headless", "Pending"),
        });

        await vm.LoadSessionsCommand.ExecuteAsync(null);

        // Headless sessions are filtered out of Sessions, so HasPendingSessions
        // only checks visible sessions
        Assert.False(vm.HasPendingSessions());
    }

    #endregion

    #region TryRenewSessionAsync

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

    #endregion

    #region Service delegation

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

    #endregion
}
