using Xunit;
using CanfarDesktop.Mcp;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>The wired MCP approval gate: allow-all by default, opt-in allow-list lockdown, revocation,
/// and an always-permitted internal self-test client. Identity is attribution-only, so these guard
/// visibility/lockdown behaviour, not authentication.</summary>
public class McpClientApprovalStoreTests
{
    private sealed class FakeStorage : IMcpApprovalStorage
    {
        public bool RequireApproval { get; set; }
        public HashSet<string> Approved = new(StringComparer.Ordinal);
        public int Saves { get; private set; }
        public HashSet<string> LoadApproved() => new(Approved, StringComparer.Ordinal);
        public void SaveApproved(IReadOnlyCollection<string> approved) { Approved = new(approved, StringComparer.Ordinal); Saves++; }
    }

    [Fact]
    public async Task AllowAll_PermitsAnyClient_AndRecordsSeen()
    {
        var store = new McpClientApprovalStore(new FakeStorage());
        Assert.True(await store.PermitAsync("claude-ai/1.0"));
        var seen = Assert.Single(store.SeenClients());
        Assert.Equal("claude-ai/1.0", seen.ClientId);
        Assert.Equal(1, seen.ConnectCount);
    }

    [Fact]
    public async Task RequireApproval_DeniesUnknown_ThenAllowsAfterApprove_AndPersists()
    {
        var fake = new FakeStorage { RequireApproval = true };
        var store = new McpClientApprovalStore(fake);

        Assert.False(await store.PermitAsync("evil/1.0")); // not on the allow-list → denied
        store.Approve("evil/1.0");
        Assert.True(await store.PermitAsync("evil/1.0"));
        Assert.Contains("evil/1.0", fake.Approved);        // persisted via storage
        Assert.True(fake.Saves >= 1);
    }

    [Fact]
    public async Task Revoke_RemovesApproval_AndPersists()
    {
        var fake = new FakeStorage { RequireApproval = true };
        fake.Approved.Add("claude/1.0");
        var store = new McpClientApprovalStore(fake); // loads the pre-approved client

        Assert.True(await store.PermitAsync("claude/1.0"));
        store.Revoke("claude/1.0");
        Assert.False(await store.PermitAsync("claude/1.0"));
        Assert.DoesNotContain("claude/1.0", fake.Approved);
    }

    [Theory]
    [InlineData("verbinal-selftest")]
    [InlineData("verbinal-selftest/1.0")]
    public async Task SelfTestClient_AlwaysPermitted_EvenUnderLockdown_AndNotListed(string id)
    {
        var store = new McpClientApprovalStore(new FakeStorage { RequireApproval = true });
        Assert.True(await store.PermitAsync(id)); // the app's own probe is internal
        Assert.Empty(store.SeenClients());        // never surfaced as an external client
    }

    [Fact]
    public async Task SeenClients_CountsRepeatConnects_MostRecentFirst()
    {
        var store = new McpClientApprovalStore(new FakeStorage());
        await store.PermitAsync("a/1.0");
        await store.PermitAsync("a/1.0");
        await store.PermitAsync("b/1.0");
        Assert.Equal(2, store.SeenClients().Single(c => c.ClientId == "a/1.0").ConnectCount);
    }

    [Fact]
    public void IsInternalClient_MatchesNameOrNameSlash_NotLookalikes()
    {
        Assert.True(McpClientApprovalStore.IsInternalClient("verbinal-selftest"));
        Assert.True(McpClientApprovalStore.IsInternalClient("verbinal-selftest/1.0"));
        Assert.False(McpClientApprovalStore.IsInternalClient("verbinal-selftest-evil/1.0"));
        Assert.False(McpClientApprovalStore.IsInternalClient("claude/1.0"));
    }

    [Fact]
    public void RequireApproval_RoundTripsThroughStorage()
    {
        var fake = new FakeStorage();
        var store = new McpClientApprovalStore(fake) { RequireApproval = true };
        Assert.True(fake.RequireApproval);
    }
}
