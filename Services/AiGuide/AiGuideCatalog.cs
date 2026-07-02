namespace CanfarDesktop.Services.AiGuide;

/// <summary>One AI Guide category widget (UI grouping only â€” no logic, no MCP).</summary>
public sealed record AiGuideCategory(string Id, string Title, string Glyph, string Summary);

/// <summary>
/// Static grouping of the built-in MCP tools into logical categories for the AI Guide UI. Keyed by
/// tool name; any tool not listed falls into <see cref="Other"/> so a newly-added tool is never
/// silently dropped from the screen (it surfaces under "Other" â€” a visible nudge to slot it into a
/// category). Ports the macOS <c>AIGuideCatalog</c>, extended with the Windows-only tools (3D cube
/// viewer, 2D-FITS-viewer steering, and the native notebook engine). <see cref="AiGuideCategory.Glyph"/>
/// holds a Segoe Fluent Icons code point and is assigned by the UI layer.
/// </summary>
public static class AiGuideCatalog
{
    /// <summary>Ordered categories â€” the order the widgets render top-to-bottom.</summary>
    public static readonly IReadOnlyList<AiGuideCategory> Categories = new[]
    {
        new AiGuideCategory("foundational", "Foundational",     "î¥†", "App identity, auth, service health, platform load, and current view."),
        new AiGuideCategory("search",       "Search & Archive", "îœ¡", "Find observations in CADC, then fetch their metadata, links, and previews."),
        new AiGuideCategory("queries",      "Saved Queries",    "îœ´", "Save, recall, and edit reusable ADQL queries."),
        new AiGuideCategory("research",     "Research & Notes", "îœ‹", "Inspect downloaded observations and notes; export a research bundle."),
        new AiGuideCategory("downloads",    "Downloads",        "î¢–", "Pull observations into the local research archive."),
        new AiGuideCategory("fits",         "FITS Viewer",      "îŸ…", "Read FITS headers/WCS, open files, steer the 2D viewer, bookmark coordinates."),
        new AiGuideCategory("cube",         "Cube Viewer",      "ï…˜", "Open and steer the 3D spectral cube viewer; probe spectra; export figures."),
        new AiGuideCategory("notebook",     "Notebook",         "î²¥", "Drive the native notebook editor: cells, kernel, and execution."),
        new AiGuideCategory("storage",      "Storage (VOSpace)","î¢·", "Browse, read, upload, download, and tidy files in VOSpace/ARC."),
        new AiGuideCategory("sessions",     "Sessions",         "î¥·", "Launch and manage interactive compute sessions."),
        new AiGuideCategory("headless",     "Headless / Batch", "î–", "Submit batch jobs and follow their logs and events."),
        new AiGuideCategory("discovery",    "Image Discovery",  "îž¸", "Find container images by the packages they contain."),
        new AiGuideCategory("compute",      "AI Compute",       "î¥", "Run agent-authored code on a warm remote session."),
        new AiGuideCategory("workflows",    "Workflows",        "", "Read, follow, author, and check off step-by-step research protocols."),
        new AiGuideCategory("navigation",   "View & Navigation","îœ€", "Steer the app's views and focus the search field."),
        new AiGuideCategory("control",      "Agent Control",    "îœ“", "Inspect and withdraw the agent's pending proposals."),
        new AiGuideCategory("guide", "AI Guide", "î¢—", "Re-tune tool descriptions and add your own guide tools (agent-editable)."),
    };

    /// <summary>Fallback bucket for any tool not explicitly categorized.</summary>
    public static readonly AiGuideCategory Other = new("other", "Other", "îœ’", "Tools not yet sorted into a category.");

    /// <summary>All categories including the fallback, for iteration in the view.</summary>
    public static IReadOnlyList<AiGuideCategory> AllCategories { get; } = Categories.Append(Other).ToList();

    private static readonly IReadOnlyDictionary<string, AiGuideCategory> ById =
        AllCategories.ToDictionary(c => c.Id, StringComparer.Ordinal);

    /// <summary>Look up a category by id, defaulting to <see cref="Other"/>.</summary>
    public static AiGuideCategory CategoryById(string id) => ById.TryGetValue(id, out var c) ? c : Other;

    /// <summary>
    /// Tool name â†’ category id. A superset of the macOS map: it keeps the macOS-only names (harmless â€”
    /// they simply never match a live Windows tool) and adds the Windows-only tools. The reserved
    /// AI-Compute names categorize ahead of Feature B landing them.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> CategoryByTool = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Foundational
        ["describe_app"] = "foundational",
        ["get_auth_state"] = "foundational",
        ["get_current_view"] = "foundational",
        ["get_service_health"] = "foundational",
        ["get_platform_load"] = "foundational",
        // Search & Archive
        ["search_observations"] = "search",
        ["vizier_cone_search"] = "search",
        ["resolve_target"] = "search",
        ["get_observation_caom2"] = "search",
        ["get_data_links"] = "search",
        ["get_preview_image"] = "search",
        ["list_recent_searches"] = "search",
        // Saved Queries
        ["list_saved_queries"] = "queries",
        ["get_saved_query"] = "queries",
        ["save_query"] = "queries",
        ["update_saved_query"] = "queries",
        ["delete_saved_query"] = "queries",
        // Research & Notes
        ["list_downloaded_observations"] = "research",
        ["get_downloaded_observation"] = "research",
        ["get_observation_notes"] = "research",
        ["update_observation_note"] = "research",
        ["bulk_update_observation_notes"] = "research",
        ["export_research_bundle"] = "research",
        // Downloads
        ["download_observation"] = "downloads",
        ["download_observations_bulk"] = "downloads",
        ["delete_downloaded_observation"] = "downloads",
        ["clear_research_archive"] = "downloads",
        // FITS Viewer
        ["get_fits_header"] = "fits",
        ["get_fits_wcs"] = "fits",
        ["open_fits_file"] = "fits",
        ["set_fits_view"] = "fits",
        ["get_fits_view"] = "fits",
        ["probe_fits_pixel"] = "fits",
        ["fits_goto_coordinate"] = "fits",
        ["list_fits_bookmarks"] = "fits",
        ["save_fits_bookmark"] = "fits",
        ["delete_fits_bookmark"] = "fits",
        // Cube Viewer
        ["open_cube"] = "cube",
        ["set_cube_view"] = "cube",
        ["get_cube_view"] = "cube",
        ["probe_cube_spectrum"] = "cube",
        ["export_cube_figure"] = "cube",
        // Notebook
        ["list_notebooks"] = "notebook",
        ["list_open_notebooks"] = "notebook",
        ["get_notebook"] = "notebook",
        ["get_cell_output"] = "notebook",
        ["get_kernel_state"] = "notebook",
        ["open_notebook"] = "notebook",
        ["create_notebook"] = "notebook",
        ["save_notebook"] = "notebook",
        ["edit_cell"] = "notebook",
        ["add_cell"] = "notebook",
        ["delete_cell"] = "notebook",
        ["change_cell_type"] = "notebook",
        ["move_cell"] = "notebook",
        ["run_cell"] = "notebook",
        ["run_all_cells"] = "notebook",
        ["clear_cell_outputs"] = "notebook",
        ["start_kernel"] = "notebook",
        ["interrupt_kernel"] = "notebook",
        ["restart_kernel"] = "notebook",
        ["create_analysis_notebook"] = "notebook",
        // Storage (VOSpace) â€” both macOS and Windows tool names
        ["list_vospace_path"] = "storage",
        ["get_vospace_node"] = "storage",
        ["read_vospace_file"] = "storage",
        ["upload_to_vospace"] = "storage",
        ["upload_text_to_vospace"] = "storage",
        ["upload_file_to_vospace"] = "storage",
        ["download_from_vospace"] = "storage",
        ["download_vospace_file"] = "storage",
        ["vospace_mkdir"] = "storage",
        ["create_vospace_folder"] = "storage",
        ["set_vospace_acl"] = "storage",
        ["delete_vospace_node"] = "storage",
        ["get_storage_quota"] = "storage",
        ["clear_user_site"] = "storage",
        // Sessions
        ["list_sessions"] = "sessions",
        ["get_session"] = "sessions",
        ["list_session_types"] = "sessions",
        ["list_session_images"] = "sessions",
        ["list_recent_launches"] = "sessions",
        ["launch_session"] = "sessions",
        ["delete_session"] = "sessions",
        ["delete_sessions_bulk"] = "sessions",
        ["renew_session"] = "sessions",
        // Headless / Batch
        ["list_headless_jobs"] = "headless",
        ["get_headless_job"] = "headless",
        ["get_headless_job_logs"] = "headless",
        ["get_headless_job_events"] = "headless",
        ["launch_headless_job"] = "headless",
        // Image Discovery
        ["find_images_with_packages"] = "discovery",
        ["discover_image_packages"] = "discovery",
        // AI Compute (Feature B â€” names reserved so they categorize once built)
        ["run_code"] = "compute",
        ["run_code_output"] = "compute",
        ["start_compute"] = "compute",
        ["stop_compute"] = "compute",
        // Workflows
        ["list_workflows"] = "workflows",
        ["get_workflow"] = "workflows",
        ["save_workflow"] = "workflows",
        ["update_workflow"] = "workflows",
        ["set_workflow_step"] = "workflows",
        ["use_workflow"] = "workflows",
        ["delete_workflow"] = "workflows",
        // View & Navigation
        ["set_search_focus"] = "navigation",
        ["navigate_to"] = "navigation",
        ["close_active_tab"] = "navigation",
        ["list_open_tabs"] = "navigation",
        // Agent Control
        ["list_pending_proposals"] = "control",
        ["get_proposal_state"] = "control",
        ["withdraw_proposal"] = "control",
        ["list_events"] = "control",
        // AI Guide management (agent re-tunes its own tool surface)
        ["list_guide_tools"] = "guide",
        ["set_tool_description"] = "guide",
        ["clear_tool_description"] = "guide",
        ["add_guide_tool"] = "guide",
        ["update_guide_tool"] = "guide",
        ["delete_guide_tool"] = "guide",
    };

    /// <summary>
    /// Every tool name the catalog knows (live built-ins + macOS-only + reserved AI-Compute names).
    /// Used as the default set a user guide name may not shadow, so collision protection holds even
    /// before the MCP host starts and refines it to the precise live router set.
    /// </summary>
    public static IReadOnlySet<string> MappedToolNames { get; } = CategoryByTool.Keys.ToHashSet(StringComparer.Ordinal);

    /// <summary>Category id for a tool name, defaulting to <see cref="Other"/>.</summary>
    public static string CategoryIdForTool(string name) => CategoryByTool.TryGetValue(name, out var id) ? id : Other.Id;

    /// <summary>Category for a tool name, defaulting to <see cref="Other"/>.</summary>
    public static AiGuideCategory CategoryForTool(string name) => CategoryById(CategoryIdForTool(name));
}

/// <summary>
/// AI Guide user-preference keys. Defined here so the landing tile and the Settings â–¸ MCP toggle
/// share one key (no drift). Showing/hiding the tile only toggles the launchpad shortcut â€” saved
/// description overrides and guide tools stay active in the MCP server regardless.
/// </summary>
public static class AiGuidePreferences
{
    /// <summary>Whether the AI Guide tile shows on the landing launchpad. OFF by default.</summary>
    public const string ShowLandingTileKey = "verbinal.aiGuide.showLandingTile";
}
