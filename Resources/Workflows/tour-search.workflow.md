# Tour: Search
> A detailed walk through the CADC archive search — every group of fields on the form, the results grid, the observation detail, raw ADQL, and the searches and queries it remembers — with the app pointing at each control.
Tags: tour, search, CADC, ADQL
Time: ~15 min

## Steps

- [ ] **How this tour moves** — Put a few hints up per step, then poll list_events for
      `hintsDismissed`: it arrives when the last hint goes, whether it faded on its timer, was
      closed, or the page changed. Hints fade by default; if the person asks for a slower guide,
      pass `untilClosed` to point_at_ui so each waits for its close button.
      Tool: list_events, point_at_ui
      Note: Two to six hints a step reads well. More than that and nobody reads any of them.
- [ ] **Three tabs** — Open Search and point at `MainPivot`: the Form builds a query, Results shows
      it, and ADQL is the query itself. Say everything on the form ends up as ADQL against CAOM2 at
      CADC — the form is a way to write it without writing it.
      Tool: navigate_to, list_ui_targets, point_at_ui
      View: search
- [ ] **Where on the sky** — Point at `TargetBox` (a name or coordinates), `ResolverCombo` (which
      name service turns a name into RA/Dec — ALL tries each) and `RadiusBox` (the cone, in degrees).
      Fill in a target they care about, or M31, so the rest of the tour has something to find.
      `SpatialCutoutCheck` asks for cutouts rather than whole files; `PixelScaleBox` limits the
      resolution.
      Tool: set_search_form, resolve_target, point_at_ui
      View: search
- [ ] **Which observation, whose proposal** — Point at `ObservationIdBox`, `PiNameBox`,
      `ProposalIdBox`, `ProposalTitleBox` and `KeywordsBox`: the route when you know the programme
      rather than the sky. `DataReleaseBox` and `PublicOnlyCheck` keep out data still under its
      proprietary period; `IntentCombo` separates science frames from calibrations.
      Tool: get_search_form, point_at_ui
      View: search
- [ ] **When, and at what wavelength** — Point at `ObsDateBox`, `IntegrationTimeBox` and
      `TimeSpanBox`, then `SpectralCoverageBox`, `SpectralSamplingBox`, `ResolvingPowerBox`,
      `BandpassWidthBox` and `RestFrameEnergyBox`. Each carries its own unit, and the unit is part
      of the value — 500 nm and 500 Å are different searches. `SpectralCutoutCheck` asks for a
      spectral slice.
      Tool: get_search_form, set_search_form, point_at_ui
      View: search
- [ ] **Additional constraints** — Point at `ConstraintsExpander`, then into it: `BandList`,
      `CollectionList`, `InstrumentList`, `FilterList`, `CalLevelList`, `DataTypeList`,
      `ObsTypeList`. These narrow by what the archive actually holds; pointing at one opens the
      section for you.
      Tool: get_search_constraints, set_search_constraints, point_at_ui
      View: search
      Note: Calibration level is worth a sentence: 0 raw, 1 calibrated, 2 and up products.
- [ ] **Run it, or start over** — Point at `SearchButton` and `ResetButton`, then run the search.
      Say whether it was truncated: a result at the record limit is a sample, not the answer.
      Tool: run_search, reset_search_form, point_at_ui
      View: search
- [ ] **The results grid** — Point at `ColumnsButton` (about forty columns come back, a dozen
      show), `RowsPerPageCombo` and the page buttons (`FirstPageBtn`, `PrevPageBtn`, `NextPageBtn`,
      `LastPageBtn`). Sort by a column and filter one to show both are live, and that units can
      change per column.
      Tool: get_search_results, set_search_results_view, point_at_ui
      View: search
- [ ] **One observation up close** — Open a row's detail. Point at `DetailPivot` (Overview,
      Coverage, Files, Provenance, Raw) and `ViewOnCadcButton`. Look at the preview together before
      offering a download, and ask before downloading anything — say how large it is.
      Tool: show_search_row_detail, get_preview_image, get_data_links, get_observation_caom2, download_observation
      View: search
- [ ] **Take the results with you** — Point at `ExportCsvButton` and `ExportTsvButton`: the table
      as it is filtered, for a spreadsheet or a script.
      Tool: export_search_results, point_at_ui
      View: search
- [ ] **Raw ADQL** — Show the query the form built: point at `AdqlBox`, `ExecuteAdqlButton`, and
      `AdqlProblemsBar`, which says why a query would be refused before it is sent. The TAP schema
      says which tables and columns exist.
      Tool: set_adql_query, validate_adql_query, execute_adql_query, describe_tap_schema, point_at_ui
      View: search
- [ ] **What it remembers** — Point at `RecentSearchList` with `LoadRecentBtn`, `RemoveRecentBtn`
      and `ClearAllButton`; then `SaveQueryName` and `SaveQueryBtn`, and a saved query's
      `RunSavedBtn`, `LoadSavedBtn` and `DeleteSavedBtn`. Recent is automatic; saved is on purpose.
      Tool: list_recent_searches, load_recent_search, save_query, list_saved_queries, run_saved_query, point_at_ui
      View: search
- [ ] **Finish** — Ask what they want to find. Mention that an agent can also cross-match a field
      against VizieR catalogues, and point them at the cross-match or imaging workflows.
      Tool: vizier_cone_search, list_workflows
