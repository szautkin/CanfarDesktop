using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Tests.Helpers;

public class BatchJobsHelperTests
{
    private static Session MakeHeadless(string id, string status) => new()
    {
        Id = id,
        SessionType = "headless",
        SessionName = $"batch-{id}",
        Status = status,
        ContainerImage = $"images.canfar.net/test/headless:latest"
    };

    [Fact]
    public void GroupByState_CountsPendingCorrectly()
    {
        var sessions = new[]
        {
            MakeHeadless("1", "Pending"),
            MakeHeadless("2", "Pending"),
            MakeHeadless("3", "Running"),
        };

        var result = BatchJobsHelper.GroupByState(sessions);

        Assert.Equal(2, result.Pending);
    }

    [Fact]
    public void GroupByState_CountsRunningCorrectly()
    {
        var sessions = new[]
        {
            MakeHeadless("1", "Running"),
            MakeHeadless("2", "Running"),
            MakeHeadless("3", "Running"),
            MakeHeadless("4", "Pending"),
        };

        var result = BatchJobsHelper.GroupByState(sessions);

        Assert.Equal(3, result.Running);
    }

    [Fact]
    public void GroupByState_MapsSucceededToCompleted()
    {
        var sessions = new[]
        {
            MakeHeadless("1", "Succeeded"),
            MakeHeadless("2", "Completed"),
        };

        var result = BatchJobsHelper.GroupByState(sessions);

        Assert.Equal(2, result.Completed);
    }

    [Fact]
    public void GroupByState_MapsErrorToFailed()
    {
        var sessions = new[]
        {
            MakeHeadless("1", "Failed"),
            MakeHeadless("2", "Error"),
        };

        var result = BatchJobsHelper.GroupByState(sessions);

        Assert.Equal(2, result.Failed);
    }

    [Fact]
    public void GroupByState_UnknownStatusIsNotCounted()
    {
        var sessions = new[]
        {
            MakeHeadless("1", "Terminating"),
            MakeHeadless("2", "Unknown"),
        };

        var result = BatchJobsHelper.GroupByState(sessions);

        Assert.Equal(0, result.Pending);
        Assert.Equal(0, result.Running);
        Assert.Equal(0, result.Completed);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public void GroupByState_EmptyCollection_ReturnsAllZeros()
    {
        var result = BatchJobsHelper.GroupByState([]);

        Assert.Equal(0, result.Pending);
        Assert.Equal(0, result.Running);
        Assert.Equal(0, result.Completed);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public void GroupByState_MixedStatuses_GroupsCorrectly()
    {
        var sessions = new[]
        {
            MakeHeadless("1", "Pending"),
            MakeHeadless("2", "Running"),
            MakeHeadless("3", "Running"),
            MakeHeadless("4", "Succeeded"),
            MakeHeadless("5", "Succeeded"),
            MakeHeadless("6", "Completed"),
            MakeHeadless("7", "Failed"),
            MakeHeadless("8", "Error"),
            MakeHeadless("9", "Terminating"),
        };

        var result = BatchJobsHelper.GroupByState(sessions);

        Assert.Equal(1, result.Pending);
        Assert.Equal(2, result.Running);
        Assert.Equal(3, result.Completed);
        Assert.Equal(2, result.Failed);
    }
}
