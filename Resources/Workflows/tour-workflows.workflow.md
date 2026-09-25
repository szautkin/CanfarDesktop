# Tour: Workflows
> A detailed walk through Workflows — built-in protocols, your own working copies, ticking steps off, editing and writing your own, and sharing them through VOSpace — with the app pointing at each control.
Tags: tour, workflows, protocols
Time: ~8 min

## Steps

- [ ] **How this tour moves** — Put a few hints up per step, then poll list_events for
      `hintsDismissed`: it arrives when the last hint goes, whether it faded on its timer, was
      closed, or the page changed. Hints fade by default; if the person asks for a slower guide,
      pass `untilClosed` to point_at_ui so each waits for its close button.
      Tool: list_events, point_at_ui
- [ ] **Three lists** — Open Workflows. Point at `BuiltInList` (protocols that ship with the app,
      these tours included), `LocalList` (their own copies) and `VosList` with `VosRefreshButton`
      (workflows shared through VOSpace).
      Tool: navigate_to, list_workflows, list_ui_targets, point_at_ui
      View: workflows
- [ ] **Read one** — Ask them to pick a built-in. Point at `DetailPane`: steps in plain language,
      each naming the tools an agent would use and the screen it happens on.
      Tool: get_workflow, point_at_ui
      View: workflows
- [ ] **Use it** — Point at `UseButton`: a built-in is read-only, so using it makes a local copy
      that remembers progress. `ProgressBarCtl` shows how far along it is.
      Tool: use_workflow, set_workflow_step, point_at_ui
      View: workflows
- [ ] **Change it** — On a local copy, point at `EditButton`, `EditorBox` and `SaveEditButton` —
      it is markdown — and at `DuplicateButton` to branch one.
      Tool: update_workflow, point_at_ui
      View: workflows
- [ ] **Write or bring one** — Point at `NewButton` and `ImportButton` (a .workflow.md from a
      colleague).
      Tool: save_workflow, point_at_ui
      View: workflows
- [ ] **Share it** — Point at `PublishButton` (to VOSpace, for collaborators) and
      `CopyPromptButton` (the workflow as a prompt to paste into any assistant).
      Tool: point_at_ui
      View: workflows
- [ ] **Finish** — `DeleteButton` removes a local copy; built-ins cannot be deleted. Offer to start
      the workflow that fits what they came to do.
      Tool: use_workflow, delete_workflow
