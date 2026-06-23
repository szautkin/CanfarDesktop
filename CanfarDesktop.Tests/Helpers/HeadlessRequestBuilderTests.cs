using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Tests.Helpers;

public class HeadlessRequestBuilderTests
{
    private static bool Has(List<KeyValuePair<string, string>> pairs, string key, string value)
        => pairs.Any(kv => kv.Key == key && kv.Value == value);

    private static bool HasKey(List<KeyValuePair<string, string>> pairs, string key)
        => pairs.Any(kv => kv.Key == key);

    [Fact]
    public void SingleReplica_WireShape()
    {
        var p = new SessionLaunchParams
        {
            Name = "smoke-job",
            Image = "images.canfar.net/skaha/terminal:1.1.2",
            Cmd = "echo hello",
            Args = "--verbose",
            Cores = 2,
            Ram = 8,
            Gpus = 0,
            Env = new() { new("FOO", "bar") },
            Replicas = 1,
        };

        var pairs = HeadlessRequestBuilder.BuildFormPairs(p, 0, 1);

        Assert.True(Has(pairs, "type", "headless"));
        Assert.True(Has(pairs, "name", "smoke-job"));
        Assert.True(Has(pairs, "image", "images.canfar.net/skaha/terminal:1.1.2"));
        Assert.True(Has(pairs, "cmd", "echo hello"));
        Assert.True(Has(pairs, "args", "--verbose"));
        Assert.True(Has(pairs, "cores", "2"));
        Assert.True(Has(pairs, "ram", "8"));
        Assert.False(HasKey(pairs, "gpus"));                 // gpus=0 omitted
        Assert.True(Has(pairs, "env", "FOO=bar"));
        Assert.True(Has(pairs, "env", "REPLICA_ID=1"));
        Assert.True(Has(pairs, "env", "REPLICA_COUNT=1"));
        Assert.False(HasKey(pairs, "replicas"));             // not sent for count == 1
    }

    [Fact]
    public void MultiReplica_NamesAndReplicaVars()
    {
        var p = new SessionLaunchParams { Name = "batch", Image = "img", Cmd = "true", Replicas = 3 };
        var second = HeadlessRequestBuilder.BuildFormPairs(p, 1, 3);

        Assert.True(Has(second, "name", "batch-2"));
        Assert.True(Has(second, "env", "REPLICA_ID=2"));
        Assert.True(Has(second, "env", "REPLICA_COUNT=3"));
        Assert.True(Has(second, "replicas", "3"));
    }

    [Fact]
    public void EmptyCmdAndArgs_Omitted()
    {
        var p = new SessionLaunchParams { Name = "no-cmd", Image = "img", Cmd = "", Args = null };
        var pairs = HeadlessRequestBuilder.BuildFormPairs(p, 0, 1);

        Assert.False(HasKey(pairs, "cmd"));
        Assert.False(HasKey(pairs, "args"));
    }
}
