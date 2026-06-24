using System.Text;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;

namespace CanfarDesktop.Tests.Mcp;

public class ProposalCoreTests
{
    private static PendingProposal Proposal(string kind = "save_query", OperationOrigin? origin = null)
        => PendingProposal.Create("tool", kind, $"summary {kind}", Encoding.UTF8.GetBytes("{}"),
            origin ?? OperationOrigin.External("c1"));

    // ── InMemoryProposalStore ─────────────────────────────────────────────────

    [Fact]
    public void Enqueue_List_PreservesFifoOrder()
    {
        var store = new InMemoryProposalStore();
        var a = store.Enqueue(Proposal("a"));
        var b = store.Enqueue(Proposal("b"));

        var list = store.List();
        Assert.Equal(new[] { a.Id, b.Id }, list.Select(p => p.Id));
        Assert.Equal(ProposalState.Pending, store.State(a.Id));
    }

    [Fact]
    public void List_FiltersByOrigin()
    {
        var store = new InMemoryProposalStore();
        store.Enqueue(Proposal("a", OperationOrigin.External("c1")));
        store.Enqueue(Proposal("b", OperationOrigin.External("c2")));

        Assert.Single(store.List(OperationOrigin.External("c1")));
        Assert.Equal(2, store.List().Count);
    }

    [Fact]
    public void MarkApplied_RemovesFromPending_LeavesTombstone()
    {
        var store = new InMemoryProposalStore();
        var p = store.Enqueue(Proposal());

        Assert.True(store.MarkApplied(p.Id));
        Assert.Empty(store.List());
        Assert.Equal(ProposalState.Applied, store.State(p.Id)); // tombstone answers within TTL
        Assert.False(store.MarkApplied(p.Id));                   // already resolved
    }

    [Fact]
    public void Withdraw_And_Reject_Tracked()
    {
        var store = new InMemoryProposalStore();
        var w = store.Enqueue(Proposal("w"));
        var r = store.Enqueue(Proposal("r"));

        Assert.True(store.Withdraw(w.Id));
        Assert.True(store.MarkRejected(r.Id));
        Assert.Equal(ProposalState.Withdrawn, store.State(w.Id));
        Assert.Equal(ProposalState.Rejected, store.State(r.Id));
    }

    [Fact]
    public void State_UnknownId_IsUnknown()
        => Assert.Equal(ProposalState.Unknown, new InMemoryProposalStore().State(Guid.NewGuid()));

    [Fact]
    public void Tombstone_ExpiresAfterTtl()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = now;
        var store = new InMemoryProposalStore(() => clock);
        var p = store.Enqueue(Proposal());
        store.MarkApplied(p.Id);

        clock = now.AddMinutes(4);
        Assert.Equal(ProposalState.Applied, store.State(p.Id)); // within 5-min TTL
        clock = now.AddMinutes(6);
        Assert.Equal(ProposalState.Unknown, store.State(p.Id)); // tombstone GC'd
    }

    // ── ProposalBudget ────────────────────────────────────────────────────────

    [Fact]
    public void Budget_AcceptsUpToLimit_ThenRefuses()
    {
        var budget = new ProposalBudget(limit: 2);
        var origin = OperationOrigin.External("c1");

        Assert.True(budget.TryAccept(origin));
        Assert.True(budget.TryAccept(origin));
        Assert.False(budget.TryAccept(origin));
        Assert.Equal(0, budget.Remaining(origin));
    }

    [Fact]
    public void Budget_PerOrigin_AndReset()
    {
        var budget = new ProposalBudget(limit: 1);
        var a = OperationOrigin.External("a");
        var b = OperationOrigin.External("b");

        Assert.True(budget.TryAccept(a));
        Assert.True(budget.TryAccept(b)); // separate bucket
        Assert.False(budget.TryAccept(a));

        budget.Reset(a);
        Assert.True(budget.TryAccept(a)); // reset frees the bucket
    }

    [Fact]
    public void Budget_RejectsNonPositiveLimit()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new ProposalBudget(0));

    // ── ProposalApplierRegistry ───────────────────────────────────────────────

    private sealed class FakeApplier : IProposalApplier
    {
        public string Kind { get; }
        public FakeApplier(string kind) => Kind = kind;
        public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public void Registry_RegistersAndLooksUp()
    {
        var registry = new ProposalApplierRegistry();
        registry.Register(new[] { new FakeApplier("save_query"), (IProposalApplier)new FakeApplier("delete_saved_query") });

        Assert.NotNull(registry.ApplierFor("save_query"));
        Assert.Null(registry.ApplierFor("missing"));
        Assert.Equal(new[] { "delete_saved_query", "save_query" }, registry.RegisteredKinds());
    }

    // ── ToolResult / AutoAppliedAck ───────────────────────────────────────────

    [Fact]
    public void ToolResult_Proposed_WrapsProposal()
    {
        var p = Proposal();
        var result = ToolResult.Proposed(p);
        Assert.Same(p, Assert.IsType<ProposedResult>(result).Proposal);
    }

    [Fact]
    public void AutoAppliedAck_FromProposal()
    {
        var p = Proposal("save_query");
        var ack = AutoAppliedAck.From(p);
        Assert.True(ack.Applied);
        Assert.Equal(p.Id, ack.ProposalId);
        Assert.Equal("save_query", ack.Kind);
    }
}
