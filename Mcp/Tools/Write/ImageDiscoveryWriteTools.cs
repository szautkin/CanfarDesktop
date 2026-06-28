using System.Text.Json;
using CanfarDesktop.Mcp.Tools.Proposals;

namespace CanfarDesktop.Mcp.Tools.Write;

// ─────────────────────────────────────────────────────────────────────────────
// Image-discovery write tool + applier. discover_image_packages schedules a probe
// job to enumerate one image's installed packages and cache the result, so a later
// find_images_with_packages can match it. 1-to-1 with the macOS DiscoverImagePackages
// tool: SemanticWrite (goes through the autonomy toggle alongside launch_session);
// the applier decodes the payload and invokes the injected coordinator action.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Proposal payload for a single-image discovery probe.</summary>
public sealed record DiscoverImagePackagesPayload(string Image, bool Force);

/// <summary><c>discover_image_packages</c> — probe one image's installed packages and cache them. SemanticWrite.</summary>
public sealed class DiscoverImagePackagesTool : JsonWriteTool<DiscoverImagePackagesTool.Args>
{
    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "discover_image_packages",
        "Run a probe job to enumerate the named image's installed packages (apt/rpm/apk + pip + conda + R) " +
        "and cache the result so it becomes queryable via find_images_with_packages. Cache-hit short-circuits " +
        "with no Skaha cost. Routing is automatic from the image's types: images that include headless run an " +
        "in-target probe; all other types launch a known-good headless host that introspects the target via a " +
        "static registry scan (the target image is never executed). A cache-miss runs one small Skaha job " +
        "(visible in list_headless_jobs; delete_session to cancel). Pass force=true to bypass the cache for a " +
        "known-fresh manifest (e.g. after an image rebuild). Queues for the user to apply; after it applies, " +
        "the image is matchable via find_images_with_packages.",
        """
        {"type":"object","required":["image"],"properties":{
          "image":{"type":"string","minLength":1},
          "force":{"type":"boolean"}
        },"additionalProperties":false}
        """);

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var image = (args.Image ?? string.Empty).Trim();
        if (image.Length == 0) throw new McpToolException(new InvalidArgument("image is required"));
        var force = args.Force ?? false;
        var summary = force
            ? $"Re-probe packages installed in '{image}'"
            : $"Discover packages installed in '{image}'";
        return Task.FromResult(ProposalPlan.Encoding(
            "discover_image_packages", summary, new DiscoverImagePackagesPayload(image, force)));
    }

    public sealed record Args
    {
        public string? Image { get; init; }
        public bool? Force { get; init; }
    }
}

/// <summary>Applies a <c>discover_image_packages</c> proposal via the injected coordinator action.</summary>
public sealed class DiscoverImagePackagesApplier : IProposalApplier
{
    private readonly Func<DiscoverImagePackagesPayload, Task> _apply;
    public DiscoverImagePackagesApplier(Func<DiscoverImagePackagesPayload, Task> apply) => _apply = apply;

    public string Kind => "discover_image_packages";

    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _apply(ProposalPayload.Decode<DiscoverImagePackagesPayload>(proposal));
}
