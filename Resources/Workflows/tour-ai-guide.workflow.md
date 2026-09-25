# Tour: AI Guide
> A detailed walk through the AI Guide — every tool an agent can use, how to rewrite what the agent is told about one, your own guide tools, and how agent changes are reviewed — with the app pointing at each control.
Tags: tour, ai, agents, mcp
Time: ~8 min

## Steps

- [ ] **How this tour moves** — Put a few hints up per step, then poll list_events for
      `hintsDismissed`: it arrives when the last hint goes, whether it faded on its timer, was
      closed, or the page changed. Hints fade by default; if the person asks for a slower guide,
      pass `untilClosed` to point_at_ui so each waits for its close button.
      Tool: list_events, point_at_ui
- [ ] **Every tool, in one place** — Open AI Guide. Point at `SearchBox` and type a word they
      care about — "cube", say — then `ClearSearchButton`. Every control they have seen in the app
      has a tool behind it here.
      Tool: navigate_to, search_tools, list_ui_targets, point_at_ui
      View: aiGuide
- [ ] **Two views** — Point at `TilesRadio` and `EverythingRadio`. The tiles are named by
      category; point at one, and say clicking it opens that category, closed again with
      `CloseFocusButton`.
      Tool: list_ui_targets, point_at_ui
      View: aiGuide
- [ ] **Change what the agent is told** — Each tool has a built-in description, and a person can
      write their own in its place — the agent reads theirs from then on, and it can be reset.
      Show one tool's description and offer to rewrite it together.
      Tool: man, set_tool_description, clear_tool_description
      View: aiGuide
- [ ] **Your own guide tools** — Point at `AddGuideButton`: a guide is a tool that only returns
      text they wrote — a naming convention, a project rule — which the agent can call like any
      other.
      Tool: list_guide_tools, add_guide_tool, update_guide_tool, delete_guide_tool, point_at_ui
      View: aiGuide
- [ ] **Who decides** — Point at `ProposalsButton` and `Settings`. With auto-apply off, an agent's
      changes wait as proposals to approve or reject; with it on they happen at once. Point at
      `ExpandButton` too: what the app and the agent are doing, as it happens.
      Tool: get_current_view, list_pending_proposals, get_proposal_state, point_at_ui
      View: aiGuide
- [ ] **Finish** — Offer the full tour, or one of the per-app tours, whichever they have not seen.
      Tool: list_workflows, use_workflow
