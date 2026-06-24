using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Mcp.Tools.Read;

/// <summary>
/// <c>find_images_with_packages</c> — find discovered container images that contain ALL of the given
/// packages/constraints (intersection). Sits on the image-discovery cache via an injected search.
/// </summary>
public sealed class FindImagesWithPackagesTool : JsonReadTool<FindImagesWithPackagesTool.Args, FindImagesWithPackagesTool.Output>
{
    private readonly Func<PackageQuery, IReadOnlyList<string>> _search;

    public FindImagesWithPackagesTool(Func<PackageQuery, IReadOnlyList<string>> search) => _search = search;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "find_images_with_packages",
        "Find already-inspected container images that contain ALL of the given packages (Python/R/dpkg/" +
        "rpm/apk), OS families, or capabilities. Returns matching image ids.",
        """
        {"type":"object","properties":{
          "python":{"type":"array","items":{"type":"string"}},
          "r":{"type":"array","items":{"type":"string"}},
          "dpkg":{"type":"array","items":{"type":"string"}},
          "rpm":{"type":"array","items":{"type":"string"}},
          "apk":{"type":"array","items":{"type":"string"}},
          "osFamily":{"type":"array","items":{"type":"string"}},
          "capabilities":{"type":"array","items":{"type":"string"}}
        },"additionalProperties":false}
        """);

    protected override Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var query = new PackageQuery();
        Fill(query.Python, args.Python);
        Fill(query.R, args.R);
        Fill(query.Dpkg, args.Dpkg);
        Fill(query.Rpm, args.Rpm);
        Fill(query.Apk, args.Apk);
        Fill(query.OsFamilies, args.OsFamily);
        Fill(query.Capabilities, args.Capabilities);

        if (query.IsEmpty)
            throw new McpToolException(new InvalidArgument("Specify at least one package, OS family, or capability."));

        var ids = _search(query);
        return Task.FromResult(new Output(ids.Count, ids));
    }

    private static void Fill(HashSet<string> target, string[]? values)
    {
        if (values is null) return;
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) target.Add(v);
    }

    public sealed record Args
    {
        public string[]? Python { get; init; }
        public string[]? R { get; init; }
        public string[]? Dpkg { get; init; }
        public string[]? Rpm { get; init; }
        public string[]? Apk { get; init; }
        public string[]? OsFamily { get; init; }
        public string[]? Capabilities { get; init; }
    }

    public sealed record Output(int Count, IReadOnlyList<string> ImageIds);
}
