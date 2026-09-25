# Tour: Remote Compute
> A detailed walk through Remote Compute — what it is, how to set it up, starting and stopping its session, every run your assistant or you have sent, and running code yourself — with the app pointing at each control.
Tags: tour, compute, run_code, agents
Time: ~8 min

## Steps

- [ ] **How this tour moves** — Put a few hints up per step, then poll list_events for
      `hintsDismissed`: it arrives when the last hint goes, whether it faded on its timer, was
      closed, or the page changed. Hints fade by default; if the person asks for a slower guide,
      pass `untilClosed` to point_at_ui so each waits for its close button.
      Tool: list_events, point_at_ui
- [ ] **What it is** — Open Remote Compute. Say plainly what happens: code an assistant runs with
      run_code, and code they run here, goes to one session on their CANFAR account named
      verbinal-compute, using their cores, with no shell and no inbound network. Point at
      `StatusText` for where it stands.
      Tool: navigate_to, get_compute_state, list_ui_targets, point_at_ui
      View: remoteCompute
- [ ] **If it is not set up** — The screen shows three steps instead of the runs. Point at
      `SetupRepoLink` (the verbinal-execution image, built and pushed to their project) and
      `SetupSettingsButton` (Settings ▸ AI compute, where the image, cores and RAM go). Say that
      with auto-apply on an assistant's code runs without asking, and where to turn that off.
      Tool: get_compute_state, point_at_ui
      View: remoteCompute
      Note: Setting it up is their decision and their allocation. Explain; do not fill in Settings for them.
- [ ] **The session** — Point at `StartButton` and `StopButton`. A session takes a minute or two to
      come up; stopping deletes it and frees its cores, and code still running is lost, which is
      why Stop asks first. `OpenFolderButton` shows the request and result files in Storage — the
      same as show_storage_folder(".verbinal/exec").
      Tool: get_compute_state, start_compute, stop_compute, show_storage_folder, point_at_ui
      View: remoteCompute
      Note: Point at Start; start the session only when they ask.
- [ ] **What has run** — Point at `RunList`: every run, the assistant's and theirs, with its
      language and outcome. Put one on screen with show_compute_run and point at `CodeView` and
      `OutputView` — the exact code, and what it printed. `CopyCodeButton` and `RunAgainButton` sit
      above.
      Tool: list_compute_runs, show_compute_run, run_code_output, point_at_ui
      View: remoteCompute
- [ ] **Run something yourself** — Point at `ComputePivot`, then on the Run code tab at
      `LanguageCombo` and `LanguageNote` — Python or Bash, and from Bash anything the image has
      installed — then `TimeoutBox`, `SnippetBox` and `RunButton`. Offer a one-liner to try and put
      it in the box with set_compute_snippet: they read it and press Run, so the run is theirs, not
      the assistant's, and lands in the same list.
      Tool: set_compute_snippet, get_compute_view, point_at_ui
      View: remoteCompute
- [ ] **Finish** — Point at `RepoLink` for the watcher's source, and mention the batch-reprocessing
      workflow for work too big for a snippet.
      Tool: list_workflows
