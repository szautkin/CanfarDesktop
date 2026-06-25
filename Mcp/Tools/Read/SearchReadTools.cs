using CanfarDesktop.Models;

namespace CanfarDesktop.Mcp.Tools.Read;

/// <summary><c>list_saved_queries</c> — the user's saved ADQL queries.</summary>
public sealed class ListSavedQueriesTool : JsonReadTool<EmptyArgs, ListSavedQueriesTool.Output>
{
    private readonly Func<IReadOnlyList<SavedQuery>> _saved;

    public ListSavedQueriesTool(Func<IReadOnlyList<SavedQuery>> saved) => _saved = saved;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_saved_queries",
        "List the user's saved ADQL queries (name + the ADQL text, ready to run or rewrite).",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<Output> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
    {
        var items = _saved().Select(q => new QueryView(q.Name, q.Adql, q.SavedAt)).ToList();
        return Task.FromResult(new Output(items.Count, items));
    }

    public sealed record QueryView(string Name, string Adql, DateTime SavedAt);
    public sealed record Output(int Count, IReadOnlyList<QueryView> Queries);
}

/// <summary><c>get_saved_query</c> — one saved ADQL query by name.</summary>
public sealed class GetSavedQueryTool : JsonReadTool<GetSavedQueryTool.Args, GetSavedQueryTool.Output>
{
    private readonly Func<IReadOnlyList<SavedQuery>> _saved;

    public GetSavedQueryTool(Func<IReadOnlyList<SavedQuery>> saved) => _saved = saved;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_saved_query",
        "Get one saved ADQL query by its exact name (the full ADQL text, ready to run via search_observations).",
        """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"],"additionalProperties":false}""");

    protected override Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Name))
            throw new McpToolException(new InvalidArgument("name is required"));

        var match = _saved().FirstOrDefault(q => string.Equals(q.Name, args.Name, StringComparison.Ordinal));
        if (match is null)
            throw new McpToolException(new UnknownTarget($"no saved query named '{args.Name}'"));

        return Task.FromResult(new Output(match.Name, match.Adql, match.SavedAt));
    }

    public sealed record Args { public string Name { get; init; } = string.Empty; }
    public sealed record Output(string Name, string Adql, DateTime SavedAt);
}

/// <summary><c>list_recent_searches</c> — the user's recent search history.</summary>
public sealed class ListRecentSearchesTool : JsonReadTool<ListRecentSearchesTool.Args, ListRecentSearchesTool.Output>
{
    private readonly Func<IReadOnlyList<RecentSearch>> _recent;

    public ListRecentSearchesTool(Func<IReadOnlyList<RecentSearch>> recent) => _recent = recent;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_recent_searches",
        "List the user's recent searches (summary, ADQL, result count, when run). Newest first; optional limit.",
        """{"type":"object","properties":{"limit":{"type":"integer","minimum":1,"description":"Max entries to return"}},"additionalProperties":false}""");

    protected override Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        IEnumerable<RecentSearch> recent = _recent();
        if (args.Limit is > 0)
            recent = recent.Take(args.Limit.Value);

        var items = recent.Select(s => new SearchView(s.Summary, s.Adql, s.ResultCount, s.SearchedAt)).ToList();
        return Task.FromResult(new Output(items.Count, items));
    }

    public sealed record Args
    {
        public int? Limit { get; init; }
    }

    public sealed record SearchView(string Summary, string Adql, int ResultCount, DateTime SearchedAt);
    public sealed record Output(int Count, IReadOnlyList<SearchView> Searches);
}
