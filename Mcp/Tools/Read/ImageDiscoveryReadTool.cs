using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Mcp.Tools.Read;

/// <summary>One catalogue image: its id + the Skaha session types it supports (drives the type filter).</summary>
public sealed record CatalogueImage(string Id, IReadOnlyList<string> Types);

/// <summary>
/// <c>find_images_with_packages</c> — search the user's local image-content cache for images that contain
/// ALL listed packages / capabilities (intersection). 1-to-1 with the macOS tool: returns strict matches
/// plus the coverage of the catalogue, a next-step shortlist of unprobed candidates, every probed image,
/// and a ranked near-miss list when the strict AND-match is empty. Free — no Skaha jobs run.
/// </summary>
public sealed class FindImagesWithPackagesTool : JsonReadTool<FindImagesWithPackagesTool.Args, FindImagesWithPackagesTool.Output>
{
    private const int CandidatesCap = 10;
    private const double PartialMinScore = 0.5;
    private const int PartialLimit = 5;

    private readonly Func<PackageQuery, IReadOnlyList<string>> _search;
    private readonly Func<CancellationToken, Task<IReadOnlyList<CatalogueImage>>> _catalogue;
    private readonly Func<IReadOnlyList<string>> _discoveredIds;
    private readonly Func<PackageQuery, double, int, IReadOnlyList<PartialMatch>> _searchPartial;

    public FindImagesWithPackagesTool(
        Func<PackageQuery, IReadOnlyList<string>> search,
        Func<CancellationToken, Task<IReadOnlyList<CatalogueImage>>> catalogue,
        Func<IReadOnlyList<string>> discoveredIds,
        Func<PackageQuery, double, int, IReadOnlyList<PartialMatch>> searchPartial)
    {
        _search = search;
        _catalogue = catalogue;
        _discoveredIds = discoveredIds;
        _searchPartial = searchPartial;
    }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "find_images_with_packages",
        "Search the user's local image-content cache for images that contain ALL listed packages / " +
        "capabilities (intersection). Free — no Skaha jobs run. `capabilities` filters on behavioural flags " +
        "the probe detects beyond raw package names (fitsio, photutils-iterative-psf, gpu, python3, conda, " +
        "rscript). Optional `type` narrows to images launchable as that session type. Returns five " +
        "complementary fields: (1) imageIds — strict-match hits you can launch right now; (2) " +
        "candidatesToProbe — up to 10 unprobed catalogue images that fit the type filter, your next-step " +
        "shortlist when matches are empty (call discover_image_packages on one); (3) allDiscovered — every " +
        "image the user has probed (matched or not); (4) coverage — how many of the catalogue have been " +
        "probed at all; (5) partialMatches — ranked near-miss images with a score (0.0-1.0 fraction of " +
        "constraints satisfied) and missing list, populated ONLY when imageIds is empty AND you supplied " +
        "filters. When imageIds is empty but candidatesToProbe is non-empty, the answer is \"unknown, but " +
        "here's what to probe next.\"",
        """
        {"type":"object","properties":{
          "dpkg":{"type":"array","items":{"type":"string"}},
          "rpm":{"type":"array","items":{"type":"string"}},
          "apk":{"type":"array","items":{"type":"string"}},
          "python":{"type":"array","items":{"type":"string"}},
          "r":{"type":"array","items":{"type":"string"}},
          "osFamily":{"type":"array","items":{"type":"string"}},
          "osVersion":{"type":"array","items":{"type":"string"}},
          "capabilities":{"type":"array","items":{"type":"string","enum":["fitsio","photutils-iterative-psf","gpu","python3","conda","rscript"]}},
          "type":{"type":"string","enum":["notebook","desktop","carta","firefly","contributed","headless"]}
        },"additionalProperties":false}
        """);

    protected override async Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var query = new PackageQuery();
        Fill(query.Dpkg, args.Dpkg);
        Fill(query.Rpm, args.Rpm);
        Fill(query.Apk, args.Apk);
        Fill(query.Python, args.Python);
        Fill(query.R, args.R);
        Fill(query.OsFamilies, args.OsFamily);
        Fill(query.OsVersions, args.OsVersion);
        Fill(query.Capabilities, args.Capabilities);

        var matchedIds = _search(query);
        var catRows = await _catalogue(ct);
        var discovered = _discoveredIds().ToHashSet(StringComparer.Ordinal);

        var typeFilter = string.IsNullOrWhiteSpace(args.Type) ? null : args.Type.Trim().ToLowerInvariant();

        // Catalogue projection — apply the type filter once and reuse for candidates, coverage and scoping.
        var scopedCatalogueIds = catRows
            .Where(r => typeFilter is null
                || r.Types.Any(t => string.Equals(t, typeFilter, StringComparison.OrdinalIgnoreCase)))
            .Select(r => r.Id)
            .ToList();
        var scopedCatalogueSet = scopedCatalogueIds.ToHashSet(StringComparer.Ordinal);

        // Match set respects the type filter too — a headless ask shouldn't return a notebook match.
        var scopedMatches = matchedIds
            .Where(id => typeFilter is null || scopedCatalogueSet.Contains(id))
            .ToList();
        var scopedMatchSet = scopedMatches.ToHashSet(StringComparer.Ordinal);

        // Partial scoring runs only when the strict intersection is empty AND the user supplied constraints.
        IReadOnlyList<PartialMatchOut> partialMatches = Array.Empty<PartialMatchOut>();
        if (scopedMatches.Count == 0 && !query.IsEmpty)
        {
            partialMatches = _searchPartial(query, PartialMinScore, PartialLimit)
                .Where(p => typeFilter is null || scopedCatalogueSet.Contains(p.ImageID))
                .Select(p => new PartialMatchOut(p.ImageID, p.Score, p.Missing))
                .ToList();
        }

        // candidatesToProbe = scoped catalogue not yet probed and not already matched, sorted, capped.
        var candidates = scopedCatalogueIds
            .Where(id => !discovered.Contains(id) && !scopedMatchSet.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .Take(CandidatesCap)
            .ToList();

        var probedForType = scopedCatalogueSet.Where(discovered.Contains).ToList();

        var allDiscovered = (typeFilter is null ? (IEnumerable<string>)discovered : probedForType)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        var coverage = typeFilter is null
            ? new Coverage(catRows.Count, discovered.Count, scopedMatches.Count)
            : new Coverage(scopedCatalogueSet.Count, probedForType.Count, scopedMatches.Count);

        return new Output(scopedMatches, query.IsEmpty, scopedMatches.Count, coverage, candidates, allDiscovered, partialMatches);
    }

    private static void Fill(HashSet<string> target, string[]? values)
    {
        if (values is null) return;
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) target.Add(v);
    }

    public sealed record Args
    {
        public string[]? Dpkg { get; init; }
        public string[]? Rpm { get; init; }
        public string[]? Apk { get; init; }
        public string[]? Python { get; init; }
        public string[]? R { get; init; }
        public string[]? OsFamily { get; init; }
        public string[]? OsVersion { get; init; }
        public string[]? Capabilities { get; init; }
        public string? Type { get; init; }
    }

    /// <summary>How much of the catalogue the local cache has actually probed (type-scoped when a filter is set).</summary>
    public sealed record Coverage(int Total, int Discovered, int Matching);

    /// <summary>One ranked near-miss image: its id, fraction of constraints satisfied, and the unmet ones.</summary>
    public sealed record PartialMatchOut(string ImageId, double Score, IReadOnlyList<string> Missing);

    public sealed record Output(
        IReadOnlyList<string> ImageIds,
        bool Unfiltered,
        int Count,
        Coverage Coverage,
        IReadOnlyList<string> CandidatesToProbe,
        IReadOnlyList<string> AllDiscovered,
        IReadOnlyList<PartialMatchOut> PartialMatches);
}
