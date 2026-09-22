# Take the tour of Verbinal
> First time here? Be shown around every part of the app, one screen at a time, with the app itself pointing at the controls as they are explained.
Tags: onboarding, tour, getting started
Time: ~10 min

## Steps

- [ ] **Say what the tour will do** — Tell them it is nine screens, that the app will point at
      controls as you describe them, and that they close each hint when they have read it — the tour
      waits for them rather than running on a timer.
      Note: Run this as a conversation, not a slideshow. If they ask a question at any screen, answer
      it and carry on from there.
- [ ] **How the pointing works** — Before the first screen, explain the one thing they need to know:
      each hint has a close button, hovering one stops it fading, and closing the last one moves the
      tour on. Waiting is done by polling list_events for a `hintsDismissed` event — take a token
      from list_events first so you know what is new.
      Tool: list_events
- [ ] **Landing — where everything starts** — The home screen with a tile per area. Point at a tile
      or two and say the app is nine areas around one archive, and that anything they can do here an
      agent can do for them.
      Tool: navigate_to, list_ui_targets, point_at_ui
      View: landing
- [ ] **Search — find data in the CADC archive** — Resolve a target by name or enter coordinates,
      narrow by instrument, filter, proposal or date, then search. Point at the search button and the
      constraints. Mention saved queries and CSV/TSV export, and that raw ADQL is there when the form
      is not enough.
      Tool: navigate_to, list_ui_targets, point_at_ui
      View: search
- [ ] **Portal — run something on CANFAR** — Pick a session type, project and container image, then
      launch a notebook, desktop or CARTA session. Point at Launch and at the platform load bars, and
      say the counters track pending, running, completed and failed.
      Tool: navigate_to, list_ui_targets, point_at_ui
      View: portal
- [ ] **Storage — your files on VOSpace and ARC** — Browse it like any filesystem: upload, download,
      make folders, sort, set sharing. Point at Upload and at the sort controls, and say a FITS image
      or a cube opens straight into its viewer from here.
      Tool: navigate_to, list_ui_targets, point_at_ui
      View: storage
- [ ] **Research — the archive you have downloaded** — Observations you pull down collect here with
      their notes and provenance, and the whole set exports as a research bundle. Point at the list
      and at the filter. Say it is empty until they download something, which is normal on day one.
      Tool: navigate_to, list_ui_targets, point_at_ui
      View: research
- [ ] **FITS viewer — look at an image** — Stretch and colormap, WCS readout, pixel probing, blink
      comparison across tabs, and marks you can draw on the sky and export as JSON or a DS9 region
      file. Open a file first if one is available, because the viewer has little to show without one.
      Tool: navigate_to, open_fits_file, list_ui_targets, point_at_ui
      View: fitsViewer
      Note: With nothing loaded there is no toolbar to point at. If they have no FITS file yet, say
      so plainly and describe the viewer instead of pointing at an empty screen.
- [ ] **Cube viewer — spectral cubes in 3D** — Volume rendering with an opacity curve you can shape,
      or slice mode one channel at a time; click a spaxel for its spectrum. Point at the slice and
      volume toggles. Say marks here live on a channel rather than on the whole cube.
      Tool: navigate_to, open_cube, list_ui_targets, point_at_ui
      View: cubeViewer
- [ ] **Notebook — a real editor, not a web view** — Code and markdown cells against a live kernel
      you can interrupt or restart, with plots inline. Point at Run all cells and at the kernel
      controls.
      Tool: navigate_to, list_ui_targets, point_at_ui
      View: notebook
- [ ] **Workflows — protocols you tick off** — Step-by-step research protocols in plain markdown,
      including this tour. Point at the built-in list and say they can write their own, import one,
      or publish one to VOSpace for collaborators.
      Tool: navigate_to, list_ui_targets, point_at_ui
      View: workflows
- [ ] **AI Guide — what the agent can do** — Every tool an agent can use, grouped and searchable,
      with descriptions they can rewrite to change how an agent reads them. Point at the filter box.
      This is the screen that explains the rest: every button they were just shown has a tool behind
      it.
      Tool: navigate_to, list_ui_targets, point_at_ui
      View: aiGuide
- [ ] **Finish on what they came for** — Ask what they actually want to do first. Offer to run it
      with them, or to point them at the workflow that fits — there are built-ins for archival
      imaging, cube kinematics, spectroscopy, photometry, cross-matching and batch reprocessing.
      Tool: list_workflows, use_workflow
