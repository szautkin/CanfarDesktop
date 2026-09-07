using CanfarDesktop.Helpers.ImageDiscovery;
using CanfarDesktop.Models;
using CanfarDesktop.Models.ImageDiscovery;
using CanfarDesktop.Services.ImageDiscovery;

namespace CanfarDesktop.Mcp.Tools.Read;

/// <summary>One image as an agent sees it, whether it came from the catalogue or the registry.</summary>
public sealed record ImageView(string Id, IReadOnlyList<string> Types, string? AddedAt = null)
{
    public static ImageView From(RegistryImage image) => new(image.Id, image.Types, image.AddedAt);
}

/// <summary>
/// <c>search_image_registry</c> — find an image the platform has not listed.
///
/// Skaha's catalogue is curated: it lists what it will launch. The registry behind it holds far more,
/// and someone who knows the image they want — a colleague's build, a tag Skaha has not picked up — has
/// no way to reach it from a list that does not contain it.
///
/// It runs only when asked. Nothing enumerates the registry on a timer.
/// </summary>
public sealed class SearchImageRegistryTool : JsonReadTool<SearchImageRegistryTool.Args, SearchImageRegistryTool.Output>
{
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<RegistryImage>>> _search;

    public SearchImageRegistryTool(Func<string, CancellationToken, Task<IReadOnlyList<RegistryImage>>> search)
        => _search = search;

    /// <summary>Up to two dozen repositories, four at a time — a search can honestly take a while.</summary>
    protected override TimeSpan Timeout => TimeSpan.FromSeconds(90);

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "search_image_registry",
        "Search the container registry BEHIND the platform for images the catalogue does not list — a " +
        "colleague's build, or a tag Skaha has not picked up. list_session_images is the curated " +
        "catalogue and is the right place to look first; this is for when what you want is not in it. " +
        "Results carry the session types the registry's labels declare, which is what decides whether " +
        "an image can be offered on a launch tab. Add one with add_registry_image to keep it.",
        """
        {"type":"object","properties":{
          "query":{"type":"string","description":"Repository name or fragment. An empty query is refused — it would mean the whole registry."}
        },"required":["query"],"additionalProperties":false}
        """);

    protected override async Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var query = (args.Query ?? string.Empty).Trim();
        if (query.Length == 0)
            throw new McpToolException(new InvalidArgument(
                "query is required — an empty search would be a request to download the whole registry"));

        IReadOnlyList<RegistryImage> found;
        try
        {
            found = await _search(query, ct);
        }
        catch (Exception ex)
        {
            throw new McpToolException(new BackendError(ex.Message));
        }

        return new Output(query, found.Count,
            found.Select(ImageView.From).ToList(),
            found.Count == 0 ? "nothing in the registry matched that" : null);
    }

    public sealed record Args { public string? Query { get; init; } }

    public sealed record Output(string Query, int Count, IReadOnlyList<ImageView> Images, string? Message);
}

/// <summary><c>list_my_images</c> — the images the user added by hand.</summary>
public sealed class ListMyImagesTool : JsonReadTool<EmptyArgs, ListMyImagesTool.Output>
{
    private readonly Func<IReadOnlyList<RegistryImage>> _images;

    public ListMyImagesTool(Func<IReadOnlyList<RegistryImage>> images) => _images = images;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_my_images",
        "The registry images the user has added to their own list, newest first. These appear in the " +
        "images card and on the launch form alongside the platform's catalogue.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<Output> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
    {
        var images = _images();
        return Task.FromResult(new Output(images.Count, images.Select(ImageView.From).ToList(),
            images.Count == 0 ? "the user has not added any images" : null));
    }

    public sealed record Output(int Count, IReadOnlyList<ImageView> Images, string? Message);
}

/// <summary>
/// <c>search_packages</c> — the vocabulary of what is installed across the images that have been probed.
///
/// The tool that was missing. "Which image is best for spectra on M51" used to end at a search for
/// "spectroscopy", which returns nothing and reads as "no image does that" — while nine images carry
/// <c>specutils</c>. This answers what the packages are actually CALLED, so the next search is one that
/// can match.
/// </summary>
public sealed class SearchPackagesTool : JsonReadTool<SearchPackagesTool.Args, SearchPackagesTool.Output>
{
    /// <summary>Names returned per ecosystem. Enough to choose from, few enough to read.</summary>
    private const int MaxPerEcosystem = 40;

    private readonly Func<AllPackages> _vocabulary;

    public SearchPackagesTool(Func<AllPackages> vocabulary) => _vocabulary = vocabulary;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "search_packages",
        "Find what a package is actually CALLED, across every image that has been probed. Use this " +
        "BEFORE find_images_with_packages when you are working from a subject rather than a package " +
        "name: searching for \"spectroscopy\" matches nothing while nine images carry specutils, and a " +
        "zero-hit search reads as \"no image does that\". Matches anywhere in the name, per ecosystem " +
        "(python, r, dpkg, rpm, apk).",
        """
        {"type":"object","properties":{
          "query":{"type":"string","description":"Part of a package name."},
          "ecosystem":{"type":"string","enum":["any","python","r","dpkg","rpm","apk"],"description":"Default any."}
        },"required":["query"],"additionalProperties":false}
        """);

    protected override Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var query = (args.Query ?? string.Empty).Trim();
        if (query.Length == 0) throw new McpToolException(new InvalidArgument("query is required"));

        var ecosystem = (args.Ecosystem ?? "any").Trim().ToLowerInvariant();
        if (ecosystem is not ("any" or "python" or "r" or "dpkg" or "rpm" or "apk"))
            throw new McpToolException(new InvalidArgument(
                $"ecosystem must be one of: any, python, r, dpkg, rpm, apk — got '{args.Ecosystem}'"));

        var all = _vocabulary();
        if (all.IsEmpty)
            return Task.FromResult(new Output(query, 0, [],
                "no images have been probed yet — run discover_image_packages on an image first"));

        var groups = new List<EcosystemMatches>();
        void Consider(string name, IEnumerable<string> names)
        {
            if (ecosystem is not "any" && ecosystem != name) return;

            var matched = names
                .Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n.Length)          // the shortest match is usually the package itself
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (matched.Count > 0)
                groups.Add(new EcosystemMatches(name, matched.Count, matched.Take(MaxPerEcosystem).ToList()));
        }

        Consider("python", all.Python);
        Consider("r", all.R);
        Consider("dpkg", all.Dpkg);
        Consider("rpm", all.Rpm);
        Consider("apk", all.Apk);

        var total = groups.Sum(g => g.Count);
        return Task.FromResult(new Output(query, total, groups,
            total == 0 ? "no probed image has a package with that in its name" : null));
    }

    public sealed record Args
    {
        public string? Query { get; init; }
        public string? Ecosystem { get; init; }
    }

    public sealed record EcosystemMatches(string Ecosystem, int Count, IReadOnlyList<string> Names);

    public sealed record Output(string Query, int Count, IReadOnlyList<EcosystemMatches> Matches, string? Message);
}

/// <summary>
/// <c>describe_image</c> — what is actually inside one image.
///
/// Orders what it returns and drops what it does not have: an answer listing five empty ecosystems to
/// find one populated section is an answer that has to be read twice.
/// </summary>
public sealed class DescribeImageTool : JsonReadTool<DescribeImageTool.Args, DescribeImageTool.Output>
{
    /// <summary>Packages listed per section. The median image here holds 624 of them.</summary>
    private const int MaxPerSection = 50;

    private readonly Func<string, ImageManifest?> _manifest;

    public DescribeImageTool(Func<string, ImageManifest?> manifest) => _manifest = manifest;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "describe_image",
        "What is inside a container image that has been probed: its OS, kernel, Python version, " +
        "capabilities, and its packages by ecosystem. Use it to choose between images " +
        "find_images_with_packages returned — the one carrying the newer astropy, or the one that also " +
        "has CASA. Only probed images can be described; discover_image_packages probes one.",
        """
        {"type":"object","properties":{
          "imageID":{"type":"string","description":"The full image reference, as list_session_images reports it."},
          "filter":{"type":"string","description":"Only list packages whose name contains this."}
        },"required":["imageID"],"additionalProperties":false}
        """);

    protected override Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        // The wire key is imageID, capital ID, to match the macOS reference. Not camelCased.
        var id = (args.ImageID ?? string.Empty).Trim();
        if (id.Length == 0) throw new McpToolException(new InvalidArgument("imageID is required"));

        var manifest = _manifest(id)
            ?? throw new McpToolException(new UnknownTarget(
                $"'{id}' has not been probed — run discover_image_packages on it first, or use " +
                "find_images_with_packages to find one that has been"));

        var detail = ManifestDetailBuilder.Build(manifest);
        var filter = (args.Filter ?? string.Empty).Trim();

        var sections = detail.Sections
            .Select(s =>
            {
                var packages = filter.Length == 0
                    ? s.Packages
                    : s.Packages.Where(p => p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

                return new SectionView(s.Title, packages.Count,
                    packages.Take(MaxPerSection).Select(p => new PackageView(p.Name, p.Version)).ToList());
            })
            // Empty sections are dropped rather than listed as empty: five zeroes around one real
            // section is an answer that has to be read twice to find the part that answers.
            .Where(s => s.Count > 0)
            .ToList();

        return Task.FromResult(new Output(
            manifest.ImageID,
            detail.OsLine,
            detail.KernelLine,
            detail.PythonVersion,
            detail.Capabilities,
            sections,
            detail.ProbeNotes,
            manifest.CapturedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            filter.Length > 0 && sections.Count == 0 ? $"nothing in this image matches '{filter}'" : null));
    }

    public sealed record Args
    {
        public string? ImageID { get; init; }
        public string? Filter { get; init; }
    }

    public sealed record PackageView(string Name, string Version);

    public sealed record SectionView(string Ecosystem, int Count, IReadOnlyList<PackageView> Packages);

    public sealed record Output(
        string ImageID,
        string Os,
        string Kernel,
        string PythonVersion,
        IReadOnlyList<string> Capabilities,
        IReadOnlyList<SectionView> Sections,
        string? ProbeNotes,
        string CapturedAt,
        string? Message);
}
