---
name: Search page side panel
description: Architecture and UX gaps for Recent Searches and Saved Queries side panel in SearchPage.xaml
type: project
---

Side panel is a fixed 260px right column in SearchPage.xaml. Two card Borders stacked vertically in a ScrollViewer.

**Recent Searches card:**
- ListView SelectionMode="None". Two per-item buttons: Load (E74B) and Remove (E74D).
- E74B is wrong for "Load into form" — it is the back/navigation arrow. Correct glyph is E8A5 (Import).
- AutomationProperties.Name missing on all icon buttons — Narrator reads nothing meaningful.
- Each item shows: Summary (CaptionTextBlockStyle, ellipsized) + ResultCount (CaptionTextBlockStyle, TextFillColorTertiaryBrush).
- No timestamp shown.
- "Clear All" button in header row.

**Saved Queries card:**
- ListView SelectionMode="None". Three per-item buttons per item:
  - Run (E721, magnifier) — WRONG. Should be E768 (Play). Magnifier implies search-open, not execute.
  - Load into editor (E768, play) — WRONG. Play means execute. Should be E8DA (Code). Communicates "put into code editor."
  - Delete (E74D, trash) — correct, keep.
- Save row at top: TextBox name input + E74E (floppy disk) save button. Floppy is acceptable for save.
- AutomationProperties.Name missing on all three per-item buttons.

**Agreed icon assignments (2026-04-01 audit):**
- Recent Search Load: E8A5 (Import) — "Load into search form"
- Saved Query Run: E768 (Play) — "Run query"
- Saved Query Load: E8DA (Code) — "Load into ADQL editor"
- Saved Query Delete: E74D (Delete) — no change

**Run query progress feedback design (agreed):**
- On click: disable Run button, swap play FontIcon for 16px ProgressRing in button content.
- Also disable Load and Delete for that row while running (prevents race conditions).
- Add persistent CaptionTextBlockStyle status TextBlock at bottom of Saved Queries card bound to ViewModel.RunningQueryName + BoolToVisibility. Collapses when idle.
- On complete: re-enable buttons, auto-navigate MainPivot to Results tab (SelectedIndex=1), clear status. Same pattern as OnSearchClick.
- On error: show InfoBar Severity="Error" consistent with Search Form error handling. No success toast.

**Key gaps vs macOS (still open):**
- No timestamp on recent searches.
- No filter/search box for saved queries.
- No inline rename for either list.
- Touch targets on icon buttons: Padding="8" is now used (adequate, ~40px with FontSize="12"). Was previously Padding="4" (~18px). Verify rendered size.

**Search Form action bar layout (resolved 2026-04-01):**
- Action bar (Search, Reset, Max Records, ProgressRing, InfoBar) was inside the ScrollViewer, below the Additional Constraints Expander. When expanded, buttons were pushed ~200px down.
- Fix: PivotItem content is now a two-row Grid (Height="*" scroll row + Height="Auto" pinned row). ScrollViewer contains only the constraint columns and the Expander. Action bar and InfoBar live in the pinned row, outside the scroll area.
- Divider: 1px Border using DividerStrokeColorDefaultBrush, Margin="0,8,0,12" above the button row.
- ProgressRing uses VerticalAlignment="Bottom" to align baseline with the Search button when the NumberBox header label inflates row height.
- InfoBar also moved to pinned row — error feedback must be visible at the same focus point as the Search button.

**Why:** Fluent 2 pattern: primary actions are always reachable without scrolling. Pattern matches WinUI ContentDialog (pinned button row below scrollable content area). Web portal's top-placement of Search button is a web-specific workaround; the Grid layout row is the Windows-native superior alternative.
