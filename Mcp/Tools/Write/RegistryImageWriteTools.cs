using CanfarDesktop.Mcp.Tools.Proposals;

namespace CanfarDesktop.Mcp.Tools.Write;

/// <summary>Proposal payload for <c>add_registry_image</c>.</summary>
/// <param name="Types">
/// The session types the registry's labels declared. Carried on the proposal rather than looked up on
/// apply, because by then the search that produced them is over — and an image applied without its
/// types is one the launch form cannot offer on any tab.
/// </param>
public sealed record AddRegistryImagePayload(string ImageID, IReadOnlyList<string> Types);

/// <summary>Proposal payload for <c>remove_registry_image</c>.</summary>
public sealed record RemoveRegistryImagePayload(string ImageID);

/// <summary>
/// <c>add_registry_image</c> — keep an image found in the registry.
///
/// A proposal rather than a live change: it puts something in the user's own list, which then appears
/// in their images card and on their launch form. Small, but theirs.
/// </summary>
public sealed class AddRegistryImageTool : JsonWriteTool<AddRegistryImageTool.Args>
{
    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "add_registry_image",
        "Propose adding a registry image to the user's own list, where it joins the platform's " +
        "catalogue in the images card and on the launch form. Use the id exactly as " +
        "search_image_registry reported it — a reference the registry does not have is one that cannot " +
        "be launched. Pass the `types` it reported too: they decide which launch tab can offer it.",
        """
        {"type":"object","properties":{
          "imageID":{"type":"string","description":"Full reference: host/project/name:tag."},
          "types":{"type":"array","items":{"type":"string"},"description":"Session types from search_image_registry."}
        },"required":["imageID"],"additionalProperties":false}
        """);

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var id = (args.ImageID ?? string.Empty).Trim();
        if (id.Length == 0) throw new McpToolException(new InvalidArgument("imageID is required"));

        // A bare name with no tag would be stored, offered, and then refused by the platform at launch —
        // the worst moment to find out.
        if (!id.Contains(':'))
            throw new McpToolException(new InvalidArgument(
                $"'{id}' has no tag — an image reference must be host/project/name:tag"));

        var types = (args.Types ?? [])
            .Select(t => (t ?? string.Empty).Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return Task.FromResult(ProposalPlan.Encoding(
            "add_registry_image", $"Add image: {id}", new AddRegistryImagePayload(id, types)));
    }

    public sealed record Args
    {
        public string? ImageID { get; init; }
        public List<string>? Types { get; init; }
    }
}

/// <summary><c>remove_registry_image</c> — drop one from the user's list.</summary>
public sealed class RemoveRegistryImageTool : JsonWriteTool<RemoveRegistryImageTool.Args>
{
    /// <summary>
    /// Destructive, though it deletes nothing but a list entry: the image itself is untouched and can be
    /// found again. It is the user's curation, and taking something out of it without being asked is the
    /// kind of small liberty that stops a list being trusted.
    /// </summary>
    public override McpVerbClass VerbClass => McpVerbClass.Destructive;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "remove_registry_image",
        "Propose removing an image from the user's own list (see list_my_images). The image itself is " +
        "untouched — it can be found again with search_image_registry.",
        """{"type":"object","properties":{"imageID":{"type":"string"}},"required":["imageID"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var id = (args.ImageID ?? string.Empty).Trim();
        if (id.Length == 0) throw new McpToolException(new InvalidArgument("imageID is required"));

        return Task.FromResult(ProposalPlan.Encoding(
            "remove_registry_image", $"Remove image: {id}", new RemoveRegistryImagePayload(id)));
    }

    public sealed record Args { public string? ImageID { get; init; } }
}

/// <summary>Applies an <c>add_registry_image</c> proposal against the user's image list.</summary>
public sealed class AddRegistryImageApplier : IProposalApplier
{
    private readonly Func<AddRegistryImagePayload, Task> _add;

    public AddRegistryImageApplier(Func<AddRegistryImagePayload, Task> add) => _add = add;

    public string Kind => "add_registry_image";

    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _add(ProposalPayload.Decode<AddRegistryImagePayload>(proposal));
}

/// <summary>Applies a <c>remove_registry_image</c> proposal.</summary>
public sealed class RemoveRegistryImageApplier : IProposalApplier
{
    private readonly Func<RemoveRegistryImagePayload, Task> _remove;

    public RemoveRegistryImageApplier(Func<RemoveRegistryImagePayload, Task> remove) => _remove = remove;

    public string Kind => "remove_registry_image";

    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _remove(ProposalPayload.Decode<RemoveRegistryImagePayload>(proposal));
}
