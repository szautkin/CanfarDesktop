using Xunit;
using CanfarDesktop.Services.AiGuide;

namespace CanfarDesktop.Tests.Services;

/// <summary>
/// Guards the AI Guide category mapping: every tool the live <c>McpToolCatalog.Build</c> exposes must
/// land in a real category (never silently in "Other"), and the category set stays well-formed.
/// </summary>
public class AiGuideCatalogTests
{
    /// <summary>The full live Windows MCP tool surface (from McpToolCatalog.Build). Kept here so adding
    /// a tool without categorizing it trips this test — the same "never silently dropped" guard macOS has.</summary>
    private static readonly string[] LiveToolNames =
    {
        // Foundational
        "describe_app", "get_auth_state", "get_current_view", "get_service_health", "get_platform_load",
        // Search & Archive
        "search_observations", "vizier_cone_search", "resolve_target", "get_observation_caom2",
        "get_data_links", "get_preview_image", "list_recent_searches",
        // Search UI steering (form, facets, results table, exports, side panel)
        "get_search_form", "set_search_form", "get_search_constraints", "set_search_constraints",
        "reset_search_form", "run_search", "set_adql_query", "execute_adql_query",
        "get_search_results", "set_search_results_view", "export_search_results",
        "load_recent_search", "remove_recent_search", "clear_recent_searches",
        // Saved Queries
        "list_saved_queries", "get_saved_query", "save_query", "delete_saved_query", "run_saved_query",
        // Research & Notes
        "list_downloaded_observations", "get_downloaded_observation", "get_observation_notes",
        "update_observation_note", "bulk_update_observation_notes", "export_research_bundle",
        // Downloads
        "download_observation", "download_observations_bulk", "delete_downloaded_observation",
        "clear_research_archive",
        // FITS Viewer
        "get_fits_header", "get_fits_wcs", "open_fits_file", "set_fits_view", "get_fits_view",
        "probe_fits_pixel", "fits_goto_coordinate", "blink_fits_tabs", "switch_fits_tab",
        "list_fits_bookmarks", "save_fits_bookmark",
        "delete_fits_bookmark",
        // Cube Viewer
        "open_cube", "set_cube_view", "get_cube_view", "probe_cube_spectrum", "show_cube_spectrum",
        "set_cube_transfer", "get_cube_channel_profile", "switch_cube_tab", "list_recent_cubes",
        "export_cube_figure",
        // Notebook
        "list_notebooks", "get_notebook", "get_cell_output", "get_kernel_state", "open_notebook",
        "create_notebook", "save_notebook", "edit_cell", "add_cell", "delete_cell", "change_cell_type",
        "move_cell", "run_cell", "run_all_cells", "clear_cell_outputs", "start_kernel",
        "interrupt_kernel", "restart_kernel", "create_analysis_notebook",
        // Storage (VOSpace) — including the macOS-name aliases (G5 wire parity)
        "list_vospace_path", "get_vospace_node", "read_vospace_file", "download_vospace_file", "get_storage_quota",
        "upload_text_to_vospace", "upload_file_to_vospace", "create_vospace_folder", "set_vospace_acl", "delete_vospace_node",
        "upload_to_vospace", "download_from_vospace", "vospace_mkdir", "clear_user_site",
        // Sessions
        "list_sessions", "get_session", "list_session_types", "list_session_images", "list_recent_launches",
        "launch_session", "delete_session", "delete_sessions_bulk", "renew_session",
        // Headless / Batch
        "list_headless_jobs", "get_headless_job_logs", "get_headless_job_events", "launch_headless_job",
        // Image Discovery
        "find_images_with_packages", "discover_image_packages",
        // AI Compute
        "run_code", "run_code_output", "start_compute", "stop_compute",
        // View & Navigation
        "navigate_to", "set_search_focus", "close_active_tab", "list_open_tabs",
        // Agent Control
        "list_pending_proposals", "get_proposal_state", "withdraw_proposal", "list_events",
        // AI Guide management
        "list_guide_tools", "set_tool_description", "clear_tool_description",
        "add_guide_tool", "update_guide_tool", "delete_guide_tool",
    };

    [Fact]
    public void EveryLiveTool_MapsToARealCategory()
    {
        var uncategorized = LiveToolNames
            .Where(n => AiGuideCatalog.CategoryIdForTool(n) == AiGuideCatalog.Other.Id)
            .ToList();
        Assert.True(uncategorized.Count == 0, $"Uncategorized tools: {string.Join(", ", uncategorized)}");
    }

    [Fact]
    public void EveryMappedCategory_IsADefinedCategory()
    {
        // Every category id a tool resolves to must be a real, defined category.
        foreach (var name in LiveToolNames)
        {
            var cat = AiGuideCatalog.CategoryForTool(name);
            Assert.NotEqual(AiGuideCatalog.Other.Id, cat.Id);
            Assert.Contains(AiGuideCatalog.Categories, c => c.Id == cat.Id);
        }
    }

    [Fact]
    public void UnknownTool_FallsBackToOther()
    {
        Assert.Equal("other", AiGuideCatalog.CategoryIdForTool("totally_made_up_tool"));
        Assert.Equal(AiGuideCatalog.Other, AiGuideCatalog.CategoryForTool("totally_made_up_tool"));
    }

    [Fact]
    public void Categories_AreWellFormed()
    {
        var ids = AiGuideCatalog.Categories.Select(c => c.Id).ToList();
        Assert.Equal(ids.Distinct().Count(), ids.Count);              // unique ids
        Assert.DoesNotContain("other", ids);                          // Other is separate
        Assert.Equal("foundational", ids.First());                    // ordered render
        Assert.Equal("guide", ids.Last());
        Assert.All(AiGuideCatalog.Categories, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Title));
            Assert.False(string.IsNullOrWhiteSpace(c.Summary));
        });
    }

    [Fact]
    public void AllCategories_IncludesOtherLast()
    {
        Assert.Equal(AiGuideCatalog.Categories.Count + 1, AiGuideCatalog.AllCategories.Count);
        Assert.Equal(AiGuideCatalog.Other, AiGuideCatalog.AllCategories[^1]);
    }

    [Fact]
    public void CategoryById_UnknownReturnsOther()
        => Assert.Equal(AiGuideCatalog.Other, AiGuideCatalog.CategoryById("nope"));
}
