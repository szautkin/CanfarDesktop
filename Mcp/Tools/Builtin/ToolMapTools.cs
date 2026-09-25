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
        "Find a tool by what it DOES, when you do not know which area it lives in. Matches the words of " +
        "the query — most of them must appear — against tool names and descriptions, best matches first, " +
        "and reports each hit with its area, so the next step is " +
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

        // The query is its words. Matched as one phrase, "cube spectrum" found nothing although
        // probe_cube_spectrum carries both — and agents write several words. Most of them must
        // appear (all of one or two, two of three, three of four…), so one shared word is not a hit.
        var words = Words(query);
        var needed = words.Count - words.Count / 3;

        var matches = _tools()
            .Select(t => new
            {
                Tool = t,
                Category = AiGuideCatalog.CategoryForTool(t.Name),
                Found = words.Count(w => t.Name.Contains(w, StringComparison.OrdinalIgnoreCase)
                                         || t.Description.Contains(w, StringComparison.OrdinalIgnoreCase)),
                // A name match beats a description match: someone typing "annotate" wants annotate_fits
                // before every tool whose prose happens to mention annotations.
                InName = words.Count(w => t.Name.Contains(w, StringComparison.OrdinalIgnoreCase)),
            })
            .Where(x => x.Found >= needed)
            .Where(x => app.Length == 0 || string.Equals(x.Category.Id, app, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Found)
            .ThenByDescending(x => x.InName)
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

    /// <summary>Letters, digits and underscores — so a tool's own name, run_code, is one word.</summary>
    private static readonly System.Text.RegularExpressions.Regex WordPattern = new(@"[\p{L}\p{N}_]+");

    /// <summary>Words that say nothing about what a tool does.</summary>
    private static readonly HashSet<string> Filler = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "of", "on", "in", "at", "to", "for", "and", "or", "with", "by", "from", "into",
        "is", "are", "be", "it", "its", "my", "me", "how", "do", "can", "what", "this", "that", "some", "please",
    };

    /// <summary>
    /// The words of a query that say what is wanted. A query of nothing but filler is taken as written,
    /// so "a" still finds what "a" finds.
    /// </summary>
    private static List<string> Words(string query)
    {
        var words = WordPattern.Matches(query).Select(m => m.Value)
            .Where(w => w.Length > 1 && !Filler.Contains(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return words.Count > 0 ? words : [query];
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

/// <summary>
/// <c>man</c> — one tool's full entry: its description and its complete argument schema.
///
/// The map's last step. <c>list_apps</c> gives the areas, <c>describe_app</c> gives an area's tools,
/// <c>search_tools</c> finds one by what it does — and each of those answers with a NAME and a summary,
/// because listing every schema is the cost the map exists to avoid. So an agent that has found the
/// tool it wants still has to guess its arguments, or call it wrong once to be told them.
///
/// Named after the thing it is: you have the name, you want the page.
/// </summary>
public sealed class ManTool : JsonReadTool<ManTool.Args, ManTool.Output>
{
    /// <summary>How many near-misses to offer when the name is not one. Enough to recognise, few enough to read.</summary>
    private const int MaxSuggestions = 5;

    private readonly Func<IReadOnlyList<ToolDescriptor>> _tools;

    public ManTool(Func<IReadOnlyList<ToolDescriptor>> tools) => _tools = tools;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "man",
        "Read one tool's full entry — its description and its complete argument schema — by name. " +
        "list_apps, describe_app and search_tools all answer with names and summaries rather than " +
        "schemas, so this is how you get the arguments for the one you picked without calling it wrong " +
        "first. An unknown name answers with the closest ones rather than just refusing.",
        """
        {"type":"object","properties":{
          "tool":{"type":"string","minLength":1,"description":"The tool's exact name, e.g. \"annotate_fits\"."}
        },"required":["tool"],"additionalProperties":false}
        """);

    protected override Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var wanted = (args.Tool ?? string.Empty).Trim();
        if (wanted.Length == 0) throw new McpToolException(new InvalidArgument("tool is required"));

        var all = _tools();

        // Case-insensitively, because an agent reads a name off a heading as often as off a listing,
        // and being refused for the case teaches it nothing it could not have guessed.
        var found = all.FirstOrDefault(t => string.Equals(t.Name, wanted, StringComparison.OrdinalIgnoreCase));
        if (found is not null)
        {
            return Task.FromResult(new Output(
                true,
                found.Name,
                AiGuideCatalog.CategoryIdForTool(found.Name),
                found.Description,
                found.InputSchema,
                Array.Empty<string>(),
                null));
        }

        // Not a name. Offer the ones it is nearest to — a typo and a half-remembered name are the two
        // ways to get here, and both are answered by showing what does exist.
        var near = all
            .Select(t => t.Name)
            .Where(n => n.Contains(wanted, StringComparison.OrdinalIgnoreCase)
                     || wanted.Contains(n, StringComparison.OrdinalIgnoreCase)
                     || SharesAWord(n, wanted))
            .OrderBy(n => n, StringComparer.Ordinal)
            .Take(MaxSuggestions)
            .ToList();

        return Task.FromResult(new Output(
            false, wanted, null, null, null, near,
            near.Count > 0
                ? $"no tool named \"{wanted}\"; did you mean one of these?"
                : $"no tool named \"{wanted}\". Use search_tools to find one by what it does."));
    }

    /// <summary>
    /// Whether two tool names share an underscore-separated word — "fits_goto" and "goto_fits" are the
    /// same guess made two ways, and the shared word is what says so.
    /// </summary>
    private static bool SharesAWord(string name, string wanted)
    {
        var wantedWords = wanted.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (wantedWords.Length == 0) return false;

        var nameWords = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return wantedWords.Any(w => w.Length >= 3
            && nameWords.Any(n => string.Equals(n, w, StringComparison.OrdinalIgnoreCase)));
    }

    public sealed record Args { public string? Tool { get; init; } }

    /// <summary>
    /// <c>inputSchema</c> is the tool's own schema verbatim, not a summary of it: a paraphrase is the
    /// thing an agent would then have to call the tool to check.
    /// </summary>
    public sealed record Output(
        bool Found,
        string Tool,
        string? App,
        string? Description,
        Wire.JsonValue? InputSchema,
        IReadOnlyList<string> DidYouMean,
        string? Message);
}
