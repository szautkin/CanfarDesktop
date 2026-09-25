namespace CanfarDesktop.Mcp;

/// <summary>
/// The screen an agent's tool call concerns: where "Agent is working in …" says it is, and where
/// follow-agent-activity takes the person.
///
/// <para>Keyed by tool name, which is also the proposal kind of an applied write, so one table covers
/// reads as they happen and writes as they apply. Null for tools that should not move the view:
/// foundational and meta tools (describe_app, get_current_view, …), the view-state tools that navigate
/// themselves (navigate_to, open_fits_file, show_compute_run, set_compute_snippet,
/// show_storage_folder), and the local FITS readers and preview fetch.</para>
///
/// <para>Every name returned is a screen navigate_to knows; a test holds it to that.</para>
/// </summary>
public static class AgentScreens
{
    public static string? For(string toolName) => toolName switch
    {
        "search_observations" or "resolve_target" or "list_saved_queries" or "get_saved_query"
            or "list_recent_searches" or "save_query" or "delete_saved_query"
            or "get_search_form" or "set_search_form" or "get_search_constraints" or "set_search_constraints"
            or "reset_search_form" or "run_search" or "set_adql_query" or "execute_adql_query"
            or "get_search_results" or "set_search_results_view" or "export_search_results"
            or "load_recent_search" or "run_saved_query"
            or "remove_recent_search" or "clear_recent_searches" => "search",
        "list_downloaded_observations" or "get_downloaded_observation" or "get_observation_notes"
            or "get_observation_caom2" or "get_data_links" or "update_observation_note"
            or "bulk_update_observation_notes" or "download_observation" or "delete_downloaded_observation" => "research",
        "list_sessions" or "get_session" or "list_session_types" or "list_headless_jobs"
            or "get_headless_job_logs" or "get_headless_job_events" or "list_session_images"
            or "list_recent_launches" or "find_images_with_packages" or "get_platform_load"
            or "launch_session" or "launch_headless_job" or "delete_session" or "renew_session" => "portal",
        "get_storage_quota" or "list_vospace_path" or "read_vospace_file"
            or "upload_text_to_vospace" or "create_vospace_folder" or "delete_vospace_node" => "storage",
        // Workflow tools double as proposal kinds — one mapping covers the "Agent is working in
        // Workflows" indicator AND follow-agent-activity navigation for reads and applied writes.
        "list_workflows" or "get_workflow" or "save_workflow" or "update_workflow"
            or "set_workflow_step" or "use_workflow" or "delete_workflow" => "workflows",
        // Code an agent runs on the person's account is shown where it is logged, as it runs — the
        // run lands in the Remote Compute list with its code and output in front of them, rather than
        // only in a history they have to go and look for.
        "run_code" or "run_code_output" or "start_compute" or "stop_compute"
            or "get_compute_state" or "list_compute_runs" or "get_compute_view" => "remoteCompute",
        _ => null,
    };
}
