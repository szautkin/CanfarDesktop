# Tour: Portal
> A detailed walk through the CANFAR science portal — running sessions, the three ways to launch one, batch jobs, platform load and quota, and the container images you can run — with the app pointing at each control.
Tags: tour, portal, sessions, batch, containers
Time: ~15 min

## Steps

- [ ] **How this tour moves** — Put a few hints up per step, then poll list_events for
      `hintsDismissed`: it arrives when the last hint goes, whether it faded on its timer, was
      closed, or the page changed. Hints fade by default; if the person asks for a slower guide,
      pass `untilClosed` to point_at_ui so each waits for its close button.
      Tool: list_events, point_at_ui
- [ ] **Your sessions** — Open the Portal. On a running session, point at `OpenButton`,
      `RenewButton` (sessions expire; this extends one), `EventsButton` and `DeleteButton`. With no
      sessions, say where they will appear and move on.
      Tool: navigate_to, list_sessions, get_session, list_ui_targets, point_at_ui
      View: portal
- [ ] **Launch, the usual way** — Point at `LaunchModes` (Standard, Advanced, Headless), then
      `TypeCombo`, `StdRegistryCombo`, `ProjectCombo`, `ImageCombo`, `StdNameBox`,
      `StdResTypeButtons` and `LaunchButton`. Each field has a help button beside it, such as
      `StdTypeHelpBtn`.
      Tool: list_session_types, list_session_images, point_at_ui
      View: portal
      Note: Launching uses their allocation. Point at Launch; press it only when they ask.
- [ ] **Launch, your own image** — Ask them to open the Advanced tab, then point at
      `AdvTypeCombo`, `RegistryHostCombo`, `CustomImageBox`, `RepoUsernameBox`, `AdvNameBox`,
      `AdvResTypeButtons` and `AdvancedLaunchButton`: a private image from any registry.
      Tool: list_ui_targets, point_at_ui
      View: portal
      Note: Never type a registry secret for them — point at the field and let them.
- [ ] **Batch jobs** — Ask them to open the Headless tab, then point at `JobNameBox`,
      `JobProjectCombo`, `JobImageCombo`, `JobCommandBox`, `JobArgsBox`, `JobReplicasBox`,
      `JobResTypeButtons` and `HeadlessLaunchButton`: the same container, no screen, many copies.
      Tool: launch_headless_job, point_at_ui
      View: portal
- [ ] **Jobs you have run** — Point at `PendingButton`, `RunningButton`, `CompletedButton` and
      `FailedButton`; each opens the jobs in that state, with their logs and events.
      Tool: list_headless_jobs, get_job_status, get_headless_job_logs, get_headless_job_events, point_at_ui
      View: portal
- [ ] **How busy, how full** — Point at `CpuBar` and `RamBar` (the platform right now) and
      `UsageBar` (their storage quota).
      Tool: get_platform_load, get_storage_quota, point_at_ui
      View: portal
- [ ] **What you can run** — Point at `TypeSelector`, `ProjectFilter`, `ImageList` and a row's
      `RowAction`: every image, and the software inside it. `Image discovery settings` controls how
      images are probed for their packages.
      Tool: list_my_images, describe_image, search_packages, find_images_with_packages, point_at_ui
      View: portal
- [ ] **Images from a registry** — An agent can search the registry behind the catalogue for a
      colleague's build and add it to their list.
      Tool: search_image_registry, add_registry_image, remove_registry_image
      View: portal
- [ ] **Finish** — Offer the batch-reprocessing workflow, or to launch the session they came for.
      Tool: list_workflows, launch_session
