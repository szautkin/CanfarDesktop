# Tour: Storage
> A detailed walk through CANFAR storage — browsing VOSpace, folders, uploads and downloads, sorting, opening files in place, and what an agent can do with permissions and quota — with the app pointing at each control.
Tags: tour, storage, VOSpace
Time: ~8 min

## Steps

- [ ] **How this tour moves** — Put a few hints up per step, then poll list_events for
      `hintsDismissed`: it arrives when the last hint goes, whether it faded on its timer, was
      closed, or the page changed. Hints fade by default; if the person asks for a slower guide,
      pass `untilClosed` to point_at_ui so each waits for its close button.
      Tool: list_events, point_at_ui
- [ ] **Where you are** — Open Storage and point at `PathBreadcrumb` (click any level to jump
      back), `Navigate up` and `Refresh folder`. show_storage_folder opens it at any folder in their
      home, so the tour can go where their files are.
      Tool: navigate_to, show_storage_folder, list_vospace_path, list_ui_targets, point_at_ui
      View: storage
- [ ] **What is here** — Point at `FileList` and the sort buttons: `Sort by name`, `Sort by size`,
      `Sort by date`. Size is how to find what is using the quota.
      Tool: list_vospace_path, get_vospace_node, point_at_ui
      View: storage
- [ ] **Bring files in** — Point at `Upload files`, and say files can also be dropped onto the
      page. Offer to upload something small with them rather than for them.
      Tool: upload_file_to_vospace, upload_text_to_vospace, point_at_ui
      View: storage
- [ ] **Take files out** — Point at `Download selected item`. A FITS image or cube opens straight
      into its viewer; a text file can be read without downloading it.
      Tool: download_vospace_file, read_vospace_file, point_at_ui
      View: storage
- [ ] **Organise** — Point at `New folder` and `Delete selected item`. Deleting in VOSpace is
      permanent — point, never press.
      Tool: create_vospace_folder, point_at_ui
      View: storage
- [ ] **Quota and sharing** — The storage bar on the Portal shows how much of the quota is used.
      An agent can also set who may read or write a folder; do that only when they ask, and say who
      will be able to see it.
      Tool: get_storage_quota, set_vospace_acl
- [ ] **Finish** — Workflows can be published here for collaborators, under a workflows folder in
      their space. Offer the Workflows tour.
      Tool: list_workflows
