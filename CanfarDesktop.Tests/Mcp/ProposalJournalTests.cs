using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// An app restart used to destroy the review queue in silence: proposals awaiting a human vanished,
/// and one already approved was voided.
/// </summary>
public class ProposalJournalTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "verbinal-proposal-journal-" + Guid.NewGuid().ToString("N")[..8]);

    private string Path_ => System.IO.Path.Combine(_dir, "mcp_proposals.json");

    public ProposalJournalTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static PendingProposal Proposal(string summary)
        => PendingProposal.Create("delete_session", "session.delete", summary, [1, 2, 3], OperationOrigin.External("claude"));

    // ── Surviving a restart ──────────────────────────────────────────────────

    /// <summary>
    /// The whole point: what was pending is still pending, in the same order, under the SAME ids — an
    /// agent handed a proposal id before the restart can still ask about it.
    /// </summary>
    [Fact]
    public void PendingProposals_SurviveARestart_UnderTheirOriginalIds()
    {
        var a = Proposal("delete session a");
        var b = Proposal("delete session b");

        var first = new InMemoryProposalStore(journal: new JsonProposalJournal(Path_));
        first.Enqueue(a);
        first.Enqueue(b);

        // A new process, same journal.
        var second = new InMemoryProposalStore(journal: new JsonProposalJournal(Path_));

        Assert.Equal([a.Id, b.Id], second.List().Select(p => p.Id).ToArray());
        Assert.Equal(ProposalState.Pending, second.State(a.Id));
        Assert.Equal("delete session a", second.Get(a.Id)!.Summary);
        Assert.Equal([1, 2, 3], second.Get(a.Id)!.Payload);
    }

    /// <summary>A resolved proposal is gone from the queue, and stays gone across a restart.</summary>
    [Fact]
    public void ResolvedProposals_AreNotRestored()
    {
        var a = Proposal("a");
        var b = Proposal("b");

        var first = new InMemoryProposalStore(journal: new JsonProposalJournal(Path_));
        first.Enqueue(a);
        first.Enqueue(b);
        first.MarkApplied(a.Id);

        var second = new InMemoryProposalStore(journal: new JsonProposalJournal(Path_));

        Assert.Equal([b.Id], second.List().Select(p => p.Id).ToArray());

        // Deliberately Unknown rather than Applied: the tombstone stops an id being applied twice
        // within a run and expires on a TTL measured for that run. Persisting it would outlive that.
        Assert.Equal(ProposalState.Unknown, second.State(a.Id));
    }

    [Fact]
    public void WithdrawnAndRejected_AlsoLeaveTheJournal()
    {
        var a = Proposal("a");
        var b = Proposal("b");
        var c = Proposal("c");

        var first = new InMemoryProposalStore(journal: new JsonProposalJournal(Path_));
        first.Enqueue(a);
        first.Enqueue(b);
        first.Enqueue(c);
        first.MarkRejected(a.Id);
        first.Withdraw(b.Id);

        Assert.Equal([c.Id], new InMemoryProposalStore(journal: new JsonProposalJournal(Path_))
            .List().Select(p => p.Id).ToArray());
    }

    /// <summary>Nothing written yet is an empty queue, not a failure.</summary>
    [Fact]
    public void NoJournalFileYet_StartsEmpty()
        => Assert.Empty(new InMemoryProposalStore(journal: new JsonProposalJournal(Path_)).List());

    /// <summary>A corrupt journal is quarantined by DiskPersistence, not fatal to starting the server.</summary>
    [Fact]
    public void CorruptJournal_StartsEmptyRatherThanThrowing()
    {
        File.WriteAllText(Path_, "{ not json at all");

        var store = new InMemoryProposalStore(journal: new JsonProposalJournal(Path_));

        Assert.Empty(store.List());
        Assert.True(File.Exists(Path_ + ".corrupt"), "the unreadable file should be quarantined, not dropped");
    }

    // ── The default is still to keep nothing ─────────────────────────────────

    /// <summary>
    /// Persistence is the host's decision. A store built without a journal writes nothing, which is
    /// what every test gets — a store that wrote to the user's data directory would leak between runs.
    /// </summary>
    [Fact]
    public void WithoutAJournal_NothingIsWritten()
    {
        var store = new InMemoryProposalStore();
        store.Enqueue(Proposal("a"));

        Assert.Empty(Directory.GetFiles(_dir));
        Assert.Single(store.List());
    }

    [Fact]
    public void NullJournal_ReadsEmptyAndSwallowsWrites()
    {
        var journal = NullProposalJournal.Instance;
        journal.Write([Proposal("a")]);
        Assert.Empty(journal.Read());
    }

    // ── Origin filtering survives too ────────────────────────────────────────

    [Fact]
    public void RestoredProposals_KeepTheirOrigin()
    {
        var external = PendingProposal.Create("t", "k", "ext", [1], OperationOrigin.External("claude"));
        var local = PendingProposal.Create("t", "k", "loc", [1], OperationOrigin.User);

        var first = new InMemoryProposalStore(journal: new JsonProposalJournal(Path_));
        first.Enqueue(external);
        first.Enqueue(local);

        var second = new InMemoryProposalStore(journal: new JsonProposalJournal(Path_));

        Assert.Equal([external.Id], second.List(OperationOrigin.External("claude")).Select(p => p.Id).ToArray());
        Assert.Equal([local.Id], second.List(OperationOrigin.User).Select(p => p.Id).ToArray());
    }
}
