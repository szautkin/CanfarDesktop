# Tour: Notebook
> A detailed walk through the native notebook editor — opening and creating notebooks, cells, the kernel, running and outputs, and saving — with the app pointing at each control.
Tags: tour, notebook, python, jupyter
Time: ~10 min

## Steps

- [ ] **How this tour moves** — Put a few hints up per step, then poll list_events for
      `hintsDismissed`: it arrives when the last hint goes, whether it faded on its timer, was
      closed, or the page changed. Hints fade by default; if the person asks for a slower guide,
      pass `untilClosed` to point_at_ui so each waits for its close button.
      Tool: list_events, point_at_ui
- [ ] **Start somewhere** — Open Notebook. Point at `New notebook` and `Open notebook` — it opens
      .ipynb, and also Python, markdown and text files — and at the recent list when there is one.
      Tool: navigate_to, list_notebooks, list_ui_targets, point_at_ui
      View: notebook
- [ ] **A notebook with a purpose** — Offer a starter notebook against something they have
      downloaded: it opens the file and plots it, ready to change.
      Tool: create_analysis_notebook, create_notebook, open_notebook
      View: notebook
- [ ] **Cells** — Point at `Add code cell`, `Add markdown cell`, `Move cell up`, `Move cell down`
      and `Delete cell`. Add a markdown cell with them saying what the notebook is for.
      Tool: add_cell, edit_cell, move_cell, change_cell_type, point_at_ui
      View: notebook
- [ ] **The kernel** — Point at `Interrupt kernel` (stop a runaway cell) and `Restart kernel` (a
      clean slate). Say the kernel is real Python on this machine, not a web view.
      Tool: get_kernel_state, start_kernel, point_at_ui
      View: notebook
- [ ] **Run** — Point at `Run cell` and `Run all cells`, then run it: text and plots appear under
      each cell. `Clear all outputs` empties them before sharing.
      Tool: run_cell, run_all_cells, get_cell_output, get_cell_image, point_at_ui
      View: notebook
- [ ] **Keep it** — Point at `Save` and `Save as`. Several notebooks can be open at once, one per
      tab.
      Tool: save_notebook, list_open_notebooks, point_at_ui
      View: notebook
- [ ] **Finish** — Suggest where the notebook goes next: VOSpace for collaborators, or a workflow
      that uses it.
      Tool: list_workflows
