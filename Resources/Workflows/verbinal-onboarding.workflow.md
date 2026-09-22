# Take the tour of Verbinal
> First time here? Be shown around every part of the app, one screen at a time, with the app itself pointing at the controls — finding and keeping a real image and a real cube along the way, so nothing you are shown is an empty screen.
Tags: onboarding, tour, getting started
Time: ~15 min

## Steps

- [ ] **Say what the tour will do** — Ten screens, the app pointing at controls as you describe
      them, and they close each hint when they have read it — the tour waits for them, not the other
      way round. Say up front that partway through you will offer to download one small image and one
      small cube, so the viewer screens have something real in them.
      Note: A conversation, not a slideshow. If they ask something at any screen, answer it and carry
      on from there.
- [ ] **How the pointing works** — Each hint has a close button, hovering one stops it fading, and
      closing the last one is the tour's cue to move on. Take a token from list_events first so you
      know what is new, then poll it for a `hintsDismissed` event between screens.
      Tool: list_events
- [ ] **Landing — where everything starts** — The home screen, a tile per area. Point at a tile or
      two and say the app is ten areas around one archive, and that anything they can do an agent
      can do for them.
      Tool: navigate_to, list_ui_targets, point_at_ui
      View: landing
- [ ] **Search — find real data** — Do it for real rather than describing it: resolve M31, then cone
      search CADC around it. Point at the search button and at Additional Constraints while the
      results are on screen. Mention saved queries, CSV/TSV export, and raw ADQL underneath.
      Tool: resolve_target, set_search_form, run_search, navigate_to, list_ui_targets, point_at_ui
      View: search
      Note: M31 is a good demo target because it is bright, famous and well covered. Any target they
      name instead is better — it is their tour.
- [ ] **Ask before downloading anything** — Offer one image and one cube for the rest of the tour.
      Say roughly how large they are and where they will land, and take no for an answer: if they
      decline, describe the three data screens instead of pointing at empty ones, or use a file they
      already have.
      Note: This is their disk and their bandwidth. A tour that quietly pulls down a few hundred
      megabytes has helped itself to both.
- [ ] **Pick small on purpose** — Prefer the smallest product that still shows something: a
      calibrated image rather than a raw mosaic, and a modest cube. Look at previews before
      committing to a download so you are not fetching a bad frame.
      Tool: get_preview_image, get_data_links
      Note: Some CFHT MegaCam frames are several hundred MB; a JCMT ACSIS cube is often under a
      megabyte. Size is worth a glance before committing somebody to the wait.
- [ ] **Download the image and the cube** — Pull both into the local research archive. Say it is
      happening and roughly how long it will take: a large file takes a while, and silence reads as a
      hang.
      Tool: download_observation, download_observations_bulk
- [ ] **Research — the archive you just started** — The two files are here now with their notes and
      provenance. Point at the list and the filter, and say the whole set exports as a research
      bundle with that provenance attached.
      Tool: navigate_to, list_downloaded_observations, list_ui_targets, point_at_ui
      View: research
- [ ] **FITS viewer — look at the image you kept** — Open the downloaded image. Stretch and colormap,
      WCS readout, pixel probing, blink comparison across tabs. Point at the marks panel and say
      marks can be drawn on the sky, right-clicked for a menu, and exported as JSON with provenance
      or as a DS9 region file.
      Tool: open_fits_file, set_fits_view, list_ui_targets, point_at_ui
      View: fitsViewer
- [ ] **Cube viewer — the third dimension** — Open the downloaded cube. Volume rendering with an
      opacity curve you can shape, or slice mode one channel at a time; click a spaxel for its
      spectrum. Point at the slice and volume toggles, and say a mark here lives on a channel rather
      than on the whole cube.
      Tool: open_cube, set_cube_view, list_ui_targets, point_at_ui
      View: cubeViewer
- [ ] **Storage — where files live on CANFAR** — VOSpace and ARC, browsable like any filesystem:
      upload, download, folders, sorting, sharing permissions. Point at Upload and at the sort
      controls, and say a FITS image or a cube opens straight into its viewer from here.
      Tool: navigate_to, list_vospace_path, list_ui_targets, point_at_ui
      View: storage
- [ ] **Portal — run something on the platform** — Session type, project and container image, then
      launch a notebook, desktop or CARTA session. Point at Launch and at the load bars, and say the
      counters track pending, running, completed and failed.
      Tool: navigate_to, list_ui_targets, point_at_ui
      View: portal
- [ ] **Notebook — a real editor, not a web view** — Code and markdown cells against a live kernel
      you can interrupt or restart, plots inline. Point at Run all cells and the kernel controls, and
      offer them a starter notebook against the file they just downloaded.
      Tool: navigate_to, create_analysis_notebook, list_ui_targets, point_at_ui
      View: notebook
- [ ] **Workflows — protocols you tick off** — Step-by-step protocols in plain markdown, this tour
      included. Point at the built-in list and say they can write their own, import one, or publish
      one to VOSpace for collaborators.
      Tool: navigate_to, list_workflows, list_ui_targets, point_at_ui
      View: workflows
- [ ] **AI Guide — what the agent can do** — Every tool an agent can use, grouped and searchable,
      with descriptions they can rewrite to change how an agent reads them. Point at the filter box.
      This is the screen that explains the other nine: every control they were shown has a tool
      behind it.
      Tool: navigate_to, list_guide_tools, list_ui_targets, point_at_ui
      View: aiGuide
- [ ] **Finish on what they came for** — Ask what they actually want to do first. Offer to run it
      with them, or point them at the workflow that fits — there are built-ins for archival imaging,
      cube kinematics, spectroscopy, photometry, cross-matching and batch reprocessing. Mention they
      can delete the two demo downloads if they were only ever scaffolding.
      Tool: list_workflows, use_workflow, delete_downloaded_observation
