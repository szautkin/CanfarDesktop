using Microsoft.Extensions.DependencyInjection;
using CanfarDesktop.Models;
using CanfarDesktop.Services;
using CanfarDesktop.Services.Database;
using CanfarDesktop.Services.ImageDiscovery;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Builtin;
using CanfarDesktop.Mcp.Tools.Read;

namespace CanfarDesktop.Mcp;

/// <summary>
/// Builds the live MCP tool set by binding each pure tool to the app's real services (the tools take
/// injected delegates so they stay testable; this is the one place those delegates are wired to the
/// running DI graph). All tools are read-only (AgentSafe) — no tool here mutates state.
///
/// Deferred for a follow-up (need path/stream semantics an external agent can't readily drive):
/// the FITS header/WCS tools (operate on a local file path) and the VOSpace list/read tools.
/// Their omission is intentional, not a silent cap.
/// </summary>
public static class McpToolCatalog
{
    public static IReadOnlyList<IMcpTool> Build(IServiceProvider sp, string appVersion)
    {
        var auth = sp.GetRequiredService<IAuthService>();
        var observations = sp.GetRequiredService<ObservationStore>();
        var notes = sp.GetRequiredService<ObservationNoteStore>();
        var searchStore = sp.GetRequiredService<ISearchStoreService>();
        var tap = sp.GetRequiredService<ITAPService>();
        var sessions = sp.GetRequiredService<ISessionService>();
        var imageCatalog = sp.GetRequiredService<IImageService>();
        var recentLaunches = sp.GetRequiredService<IRecentLaunchService>();
        var discovery = sp.GetRequiredService<ImageDiscoveryCoordinator>();
        var caom2 = sp.GetRequiredService<ICAOM2Service>();
        var dataLink = sp.GetRequiredService<DataLinkService>();

        return new IMcpTool[]
        {
            // Foundational
            new DescribeAppTool(appVersion),
            new GetAuthStateTool(() => new AuthSnapshot(auth.IsAuthenticated, auth.CurrentUsername)),

            // Research (downloaded observations + notes)
            new ListDownloadedObservationsTool(() => observations.Observations),
            new GetDownloadedObservationTool(() => observations.Observations),
            new GetObservationNotesTool(() => notes.All()),

            // Saved search state
            new ListSavedQueriesTool(() => searchStore.LoadSavedQueries()),
            new ListRecentSearchesTool(() => searchStore.LoadRecentSearches()),

            // Live observation search (TAP / ADQL + name resolution)
            new SearchObservationsTool(
                (adql, max) => tap.ExecuteQueryAsync(adql, max),
                (target, service) => tap.ResolveTargetAsync(target, service)),
            new ResolveTargetTool((target, service) => tap.ResolveTargetAsync(target, service)),

            // Skaha sessions / headless jobs
            new ListSessionsTool(async () => (IReadOnlyList<Session>)await sessions.GetSessionsAsync()),
            new GetSessionTool(id => sessions.GetSessionAsync(id)),
            new ListHeadlessJobsTool(async () => (IReadOnlyList<Session>)await sessions.GetSessionsAsync()),
            new GetHeadlessJobLogsTool(id => sessions.GetSessionLogsAsync(id)),
            new GetHeadlessJobEventsTool(id => sessions.GetSessionEventsAsync(id)),

            // Image catalog + recent launches + package discovery
            new ListSessionImagesTool(() => imageCatalog.GetImagesAsync()),
            new ListRecentLaunchesTool(() => recentLaunches.Load()),
            new FindImagesWithPackagesTool(query => discovery.Search(query)),

            // CAOM2 metadata + DataLink (download/preview URLs)
            new GetObservationCaom2Tool(id => caom2.GetByPublisherIdAsync(id)),
            new GetDataLinksTool(id => dataLink.GetLinksAsync(id)),
        };
    }
}
