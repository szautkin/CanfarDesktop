using CanfarDesktop.Services.AiGuide;

namespace CanfarDesktop.Mcp.Tools.Builtin;

/// <summary>
/// Telling an agent what this app can do, without telling it all hundred-odd ways.
///
/// <c>tools/list</c> is every schema the server has, and most of it is irrelevant to any one task. An
/// agent reading all of it spends context before it starts, and chooses worse for having more to choose
/// between. These two are the map — <c>describe_app</c>, which already existed, is the third.
///
/// <b>They do not shrink tools/list.</b> A client reads that on connect and there is no way for a
/// server to say otherwise, so these help an agent CHOOSE, not an agent LOAD. Anyone expecting a token
/// reduction from this file alone will not find one.
///
/// The taxonomy is <see cref="AiGuideCatalog"/>, which the AI Guide window reads too. One table: an app
/// added there appears in both, or in neither.
/// </summary>
public sealed class ListAppsTool : JsonReadTool<EmptyArgs, ListAppsTool.Output>
{
    private readonly Func<IReadOnlyList<string>> _toolNames;

    public ListAppsTool(Func<IReadOnlyList<string>> toolNames) => _toolNames = toolNames;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_apps",
        "START HERE when you do not know which tool you need. Lists the app's areas — FITS Viewer, " +
        "Cube Viewer, Notebook, Storage, Search, Sessions and the rest — with what each is for and how " +
        "many tools it has. Then call describe_app with the one you want, to get just those tools and " +
        "their arguments instead of reading all of them.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<Output> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
    {
        var live = _toolNames();

        var counts = live
            .GroupBy(AiGuideCatalog.CategoryIdForTool, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        // An area with no live tools is left out. The taxonomy is a superset — it keeps names from the
        // other platforms so a tool is never silently uncategorised — and listing an area an agent
        // cannot then describe would be a map with roads that go nowhere.
        var apps = AiGuideCatalog.AllCategories
            .Where(c => counts.ContainsKey(c.Id))
            .Select(c => new AppView(c.Id, c.Title, c.Summary, counts[c.Id]))
            .ToList();

        return Task.FromResult(new Output(apps.Count, live.Count, apps));
    }

    public sealed record AppView(string Id, string Title, string Summary, int ToolCount);

    public sealed record Output(int AppCount, int ToolCount, IReadOnlyList<AppView> Apps);
}

/// <summary>
/// <c>search_tools</c> — for when the app is not obvious, which is the common case when a model knows
/// what it wants but not where it lives.
/// </summary>
public sealed class SearchToolsTool : JsonReadTool<SearchToolsTool.Args, SearchToolsTool.Output>
{
    /// <summary>Enough to choose from, few enough that the answer is smaller than the question it saved.</summary>
    private const int MaxMatches = 25;

    private readonly Func<IReadOnlyList<ToolDescriptor>> _tools;

    public SearchToolsTool(Func<IReadOnlyList<ToolDescriptor>> tools) => _tools = tools;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "search_tools",
        "Find a tool by what it DOES, when you do not know which area it lives in. Matches the query " +
        "against tool names and descriptions and reports each hit with its area, so the next step is " +
        "either calling it or describe_app on the area around it. Use list_apps instead when you want " +
        "the shape of the whole app rather than one capability.",
        """
        {"type":"object","properties":{
          "query":{"type":"string","description":"What you are trying to do, or part of a tool's name."},
          "app":{"type":"string","description":"Narrow to one area's id (from list_apps)."}
        },"required":["query"],"additionalProperties":false}
        """);

    protected override Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var query = (args.Query ?? string.Empty).Trim();
        if (query.Length == 0) throw new McpToolException(new InvalidArgument("query is required"));

        var app = (args.App ?? string.Empty).Trim();

        var matches = _tools()
            .Select(t => new
            {
                Tool = t,
                Category = AiGuideCatalog.CategoryForTool(t.Name),
                // A name match beats a description match: someone typing "annotate" wants annotate_fits
                // before it wants every tool whose prose happens to mention annotations.
                Rank = t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ? 0
                     : t.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1
                     : 2,
            })
            .Where(x => x.Rank < 2)
            .Where(x => app.Length == 0 || string.Equals(x.Category.Id, app, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Tool.Name, StringComparer.Ordinal)
            .ToList();

        var results = matches
            .Take(MaxMatches)
            .Select(x => new ToolMatch(x.Tool.Name, Summarize(x.Tool.Description), x.Category.Id, x.Category.Title))
            .ToList();

        return Task.FromResult(new Output(query, matches.Count, results,
            matches.Count == 0
                ? "nothing matched — try list_apps for the areas, or a plainer word"
                : matches.Count > MaxMatches ? $"showing the first {MaxMatches}" : null));
    }

    /// <summary>
    /// The first sentence of a tool's description. A search result is for CHOOSING between tools; the
    /// whole description is what describe_app is for, and repeating it here would make the map as long
    /// as the territory.
    /// </summary>
    private static string Summarize(string description)
    {
        var stop = description.IndexOf(". ", StringComparison.Ordinal);
        var first = stop > 0 ? description[..(stop + 1)] : description;
        return first.Length <= 200 ? first : first[..200].TrimEnd() + "…";
    }

    public sealed record Args
    {
        public string? Query { get; init; }
        public string? App { get; init; }
    }

    public sealed record ToolMatch(string Name, string Summary, string AppId, string App);

    public sealed record Output(string Query, int Count, IReadOnlyList<ToolMatch> Tools, string? Message);
}
