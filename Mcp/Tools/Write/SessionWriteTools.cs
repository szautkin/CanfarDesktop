using System.Text.Json;
using System.Text.Json.Serialization;
using CanfarDesktop.Mcp.Tools.Proposals;

namespace CanfarDesktop.Mcp.Tools.Write;

// ─────────────────────────────────────────────────────────────────────────────
// Skaha session lifecycle write tools + appliers. The tool validates + proposes;
// the applier decodes the payload and invokes the injected SessionService action,
// so the appliers stay pure/testable. launch/renew = SemanticWrite, delete = Destructive.
// ─────────────────────────────────────────────────────────────────────────────

public sealed record LaunchSessionPayload(string Type, string Image, string? Name, int? Cores, int? Ram, int? Gpus);
public sealed record LaunchHeadlessPayload(string Image, string? Name, int? Cores, int? Ram, int? Gpus, string? Args, int? Replicas);
public sealed record DeleteSessionPayload(string Id);
public sealed record RenewSessionPayload(string Id);

internal static class SessionWriteHelpers
{
    public static readonly string[] InteractiveTypes = { "notebook", "desktop", "carta", "contributed", "firefly" };

    public static void RequireResources(int? cores, int? ram, int? gpus)
    {
        if (cores is < 1) throw new McpToolException(new InvalidArgument("cores must be >= 1"));
        if (ram is < 1) throw new McpToolException(new InvalidArgument("ram (GB) must be >= 1"));
        if (gpus is < 0) throw new McpToolException(new InvalidArgument("gpus must be >= 0"));
    }
}

/// <summary><c>launch_session</c> — propose launching an interactive Skaha session. SemanticWrite.</summary>
public sealed class LaunchSessionTool : JsonWriteTool<LaunchSessionTool.Args>
{
    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "launch_session",
        "Propose launching an interactive Skaha session (notebook/desktop/carta/contributed/firefly) from " +
        "a container image (use an id from list_session_images — hand-typed image strings can be rejected), " +
        "with optional name + CPU/RAM(GB)/GPU. Queues for the user to apply; after it applies, find the new " +
        "session via list_sessions.",
        """{"type":"object","properties":{"type":{"type":"string","enum":["notebook","desktop","carta","contributed","firefly"]},"image":{"type":"string"},"name":{"type":"string"},"cores":{"type":"integer","minimum":1},"ram":{"type":"integer","minimum":1},"gpus":{"type":"integer","minimum":0}},"required":["type","image"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var type = (args.Type ?? string.Empty).Trim().ToLowerInvariant();
        if (!SessionWriteHelpers.InteractiveTypes.Contains(type))
            throw new McpToolException(new InvalidArgument($"type must be one of: {string.Join(", ", SessionWriteHelpers.InteractiveTypes)}"));
        var image = (args.Image ?? string.Empty).Trim();
        if (image.Length == 0) throw new McpToolException(new InvalidArgument("image is required"));
        SessionWriteHelpers.RequireResources(args.Cores, args.Ram, args.Gpus);

        var payload = new LaunchSessionPayload(type, image, args.Name?.Trim(), args.Cores, args.Ram, args.Gpus);
        return Task.FromResult(ProposalPlan.Encoding("launch_session", $"Launch {type} session: {payload.Name ?? image}", payload));
    }

    public sealed record Args
    {
        public string? Type { get; init; }
        public string? Image { get; init; }
        public string? Name { get; init; }
        public int? Cores { get; init; }
        public int? Ram { get; init; }
        public int? Gpus { get; init; }
    }
}

/// <summary><c>launch_headless_job</c> — propose launching one or more headless batch replicas. SemanticWrite.</summary>
public sealed class LaunchHeadlessJobTool : JsonWriteTool<LaunchHeadlessJobTool.Args>
{
    public const int MaxReplicas = 50;

    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "launch_headless_job",
        "Propose launching a headless (batch) Skaha job from a container image (use an id from " +
        "list_session_images — hand-typed image strings can be rejected), with an optional args string, " +
        "resources, and replica count (1-50). Queues for the user to apply; track it via list_headless_jobs.",
        """{"type":"object","properties":{"image":{"type":"string"},"name":{"type":"string"},"args":{"type":"string"},"cores":{"type":"integer","minimum":1},"ram":{"type":"integer","minimum":1},"gpus":{"type":"integer","minimum":0},"replicas":{"type":"integer","minimum":1,"maximum":50}},"required":["image"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var image = (args.Image ?? string.Empty).Trim();
        if (image.Length == 0) throw new McpToolException(new InvalidArgument("image is required"));
        if (args.Replicas is < 1 or > MaxReplicas)
            throw new McpToolException(new InvalidArgument($"replicas must be between 1 and {MaxReplicas}"));
        SessionWriteHelpers.RequireResources(args.Cores, args.Ram, args.Gpus);

        var replicas = args.Replicas ?? 1;
        var payload = new LaunchHeadlessPayload(image, args.Name?.Trim(), args.Cores, args.Ram, args.Gpus, args.CommandArgs?.Trim(), replicas);
        var count = replicas == 1 ? "job" : $"{replicas} replicas";
        return Task.FromResult(ProposalPlan.Encoding("launch_headless_job", $"Launch headless {count}: {payload.Name ?? image}", payload));
    }

    public sealed record Args
    {
        public string? Image { get; init; }
        public string? Name { get; init; }
        [JsonPropertyName("args")] public string? CommandArgs { get; init; }
        public int? Cores { get; init; }
        public int? Ram { get; init; }
        public int? Gpus { get; init; }
        public int? Replicas { get; init; }
    }
}

/// <summary><c>delete_session</c> — propose terminating a running Skaha session by id. Destructive.</summary>
public sealed class DeleteSessionTool : JsonWriteTool<DeleteSessionTool.Args>
{
    public override McpVerbClass VerbClass => McpVerbClass.Destructive;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "delete_session",
        "Propose terminating a running Skaha session (or headless job) by its id. Queues for the user to " +
        "apply (a destructive change). Get ids from list_sessions / list_headless_jobs.",
        """{"type":"object","properties":{"id":{"type":"string"}},"required":["id"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var id = (args.Id ?? string.Empty).Trim();
        if (id.Length == 0) throw new McpToolException(new InvalidArgument("id is required"));
        return Task.FromResult(ProposalPlan.Encoding("delete_session", $"Terminate session {id}", new DeleteSessionPayload(id)));
    }

    public sealed record Args { public string? Id { get; init; } }
}

/// <summary><c>renew_session</c> — propose extending a session's expiry by its id. SemanticWrite.</summary>
public sealed class RenewSessionTool : JsonWriteTool<RenewSessionTool.Args>
{
    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "renew_session",
        "Propose renewing (extending the expiry of) a running Skaha session by its id. Queues for the user to apply.",
        """{"type":"object","properties":{"id":{"type":"string"}},"required":["id"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var id = (args.Id ?? string.Empty).Trim();
        if (id.Length == 0) throw new McpToolException(new InvalidArgument("id is required"));
        return Task.FromResult(ProposalPlan.Encoding("renew_session", $"Renew session {id}", new RenewSessionPayload(id)));
    }

    public sealed record Args { public string? Id { get; init; } }
}

// ── Appliers (decode payload → injected SessionService action) ────────────────────────────────────

public sealed class LaunchSessionApplier : IProposalApplier
{
    private readonly Func<LaunchSessionPayload, Task> _launch;
    public LaunchSessionApplier(Func<LaunchSessionPayload, Task> launch) => _launch = launch;
    public string Kind => "launch_session";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _launch(ProposalPayload.Decode<LaunchSessionPayload>(proposal));
}

public sealed class LaunchHeadlessApplier : IProposalApplier
{
    private readonly Func<LaunchHeadlessPayload, Task> _launch;
    public LaunchHeadlessApplier(Func<LaunchHeadlessPayload, Task> launch) => _launch = launch;
    public string Kind => "launch_headless_job";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _launch(ProposalPayload.Decode<LaunchHeadlessPayload>(proposal));
}

public sealed class DeleteSessionApplier : IProposalApplier
{
    private readonly Func<DeleteSessionPayload, Task> _delete;
    public DeleteSessionApplier(Func<DeleteSessionPayload, Task> delete) => _delete = delete;
    public string Kind => "delete_session";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _delete(ProposalPayload.Decode<DeleteSessionPayload>(proposal));
}

public sealed class RenewSessionApplier : IProposalApplier
{
    private readonly Func<RenewSessionPayload, Task> _renew;
    public RenewSessionApplier(Func<RenewSessionPayload, Task> renew) => _renew = renew;
    public string Kind => "renew_session";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _renew(ProposalPayload.Decode<RenewSessionPayload>(proposal));
}
