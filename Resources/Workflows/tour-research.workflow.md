# Tour: Research
> A detailed walk through the local research archive — the observations you have downloaded, their notes and provenance, opening them in a viewer, and exporting the whole set as a bundle — with the app pointing at each control.
Tags: tour, research, archive, provenance
Time: ~8 min

## Steps

- [ ] **How this tour moves** — Put a few hints up per step, then poll list_events for
      `hintsDismissed`: it arrives when the last hint goes, whether it faded on its timer, was
      closed, or the page changed. Hints fade by default; if the person asks for a slower guide,
      pass `untilClosed` to point_at_ui so each waits for its close button.
      Tool: list_events, point_at_ui
- [ ] **What is here** — Open Research and point at `FileList`: every observation downloaded from
      Search, with its collection, instrument and size. If it is empty, say so and offer to find
      something small in Search rather than touring an empty list.
      Tool: navigate_to, list_downloaded_observations, list_ui_targets, point_at_ui
      View: research
- [ ] **Finding one again** — Point at `FilterBox`: it narrows by target, collection or
      instrument as you type.
      Tool: point_at_ui
      View: research
- [ ] **One observation** — Ask them to pick one. The detail beside the list is what was recorded
      when it was downloaded: identifiers, position, instrument, and the provenance that says
      exactly where it came from.
      Tool: get_downloaded_observation
      View: research
- [ ] **Notes** — Every observation carries a note — why it was kept, what was wrong with it.
      Offer to write one with them; notes travel with the export.
      Tool: get_observation_notes, update_observation_note, bulk_update_observation_notes
      View: research
- [ ] **Open it** — An image opens in the FITS viewer and a cube in the Cube viewer, straight from
      its id. Open the one they picked, then come back.
      Tool: open_fits_file, open_cube, navigate_to
- [ ] **Export the lot** — Point at `ExportButton`: the archive and the searches behind it as one
      bundle, provenance attached — something a collaborator, or an assistant, can pick up cold.
      Tool: export_research_bundle, point_at_ui
      View: research
- [ ] **Files on this PC** — Point at `Toggle file explorer`: a panel for local files that opens
      FITS images, cubes and notebooks in the right place.
      Tool: point_at_ui
      View: research
- [ ] **Housekeeping** — Removing an observation, or clearing the archive, deletes the local files.
      Point at nothing destructive unless asked, and never do it for them without an explicit yes.
      Tool: delete_downloaded_observation, clear_research_archive
      Note: A tour that tidies away somebody's data has not been a tour.
