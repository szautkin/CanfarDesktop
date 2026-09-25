using System.Text.Json;
using System.Text.Json.Serialization;
using CanfarDesktop.Helpers;
using CanfarDesktop.Mcp.Tools;

namespace CanfarDesktop.Mcp.Tools.Proposals;

/// <summary>
/// Where the pending-write queue survives a restart.
///
/// An interface rather than a file path on the store, so the store keeps one job — deciding what is
/// pending — and stays testable without touching a disk.
/// </summary>
public interface IProposalJournal
{
    /// <summary>The pending proposals as they were last written. Empty when there is nothing to restore.</summary>
    IReadOnlyList<PendingProposal> Read();

    /// <summary>Replace the journal with this pending set. Called after every mutation; never throws.</summary>
    void Write(IReadOnlyList<PendingProposal> pending);
}

/// <summary>A journal that keeps nothing — the default, and what every test gets unless it asks otherwise.</summary>
public sealed class NullProposalJournal : IProposalJournal
{
    public static readonly NullProposalJournal Instance = new();
    public IReadOnlyList<PendingProposal> Read() => [];
    public void Write(IReadOnlyList<PendingProposal> pending) { }
}

/// <summary>
/// The pending queue on disk.
///
/// <para>An app restart used to destroy it in silence: proposals awaiting human review vanished, and
/// one the user had already approved was voided. They are rehydrated under their ORIGINAL ids, so an
/// agent that was handed a proposal id before the restart can still ask about it.</para>
///
/// <para>Only PENDING proposals are journalled. Resolved ones are deliberately not restored — their
/// tombstone exists to stop an id being applied twice and expires on its own TTL, and persisting
/// tombstones would make that window outlive the run it was measured for.</para>
/// </summary>
public sealed class JsonProposalJournal : IProposalJournal
{
    private const string FileName = "mcp_proposals.json";
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// The on-disk shape, which is deliberately NOT <see cref="PendingProposal"/>.
    ///
    /// <c>OperationOrigin</c> is a polymorphic record — <c>UserOrigin</c> or
    /// <c>ExternalOrigin(clientId)</c> — and a plain serializer writes the runtime type's properties
    /// and then cannot read them back into the abstract one. Flattening it to a nullable client id
    /// (null = the user) makes the round trip total, and keeps the file format free to differ from the
    /// domain model, which is the point of having one.
    /// </summary>
    private sealed record Entry(
        Guid Id, string ToolName, string Kind, string Summary, byte[] Payload,
        DateTimeOffset CreatedAt, string? OriginClientId, Guid? RequestId)
    {
        public static Entry From(PendingProposal p) => new(
            p.Id, p.ToolName, p.Kind, p.Summary, p.Payload, p.CreatedAt,
            p.Origin is ExternalOrigin e ? e.ClientId : null, p.RequestId);

        public PendingProposal ToProposal() => new(
            Id, ToolName, Kind, Summary, Payload, CreatedAt,
            OriginClientId is null ? OperationOrigin.User : OperationOrigin.External(OriginClientId),
            RequestId);
    }

    private readonly string? _filePath;
    private readonly object _gate = new();

    public JsonProposalJournal()
    {
        try
        {
            // Beside the other local stores, so clearing application data clears this too.
            _filePath = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, FileName);
        }
        catch
        {
            // Unpackaged — the queue lives for the session, as the other local stores do.
        }
    }

    /// <summary>A journal at an explicit path, for tests.</summary>
    public JsonProposalJournal(string filePath) => _filePath = filePath;

    public IReadOnlyList<PendingProposal> Read()
    {
        lock (_gate)
        {
            var entries = DiskPersistence.Read<List<Entry>>(_filePath, SchemaVersion, () => [], Json).Value;
            return entries.Select(e => e.ToProposal()).ToList();
        }
    }

    public void Write(IReadOnlyList<PendingProposal> pending)
    {
        lock (_gate)
        {
            // Best effort by design: a queue that cannot be written is a queue that lives for this run,
            // which is where it was before. Failing the enqueue instead would turn a disk problem into
            // a refused tool call.
            try { DiskPersistence.Write(_filePath, pending.Select(Entry.From).ToList(), SchemaVersion, Json); }
            catch { /* the in-memory queue is still correct */ }
        }
    }
}
