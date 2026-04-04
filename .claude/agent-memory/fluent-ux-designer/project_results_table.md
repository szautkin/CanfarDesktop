---
name: Results table architecture and audit
description: StackPanel-based results table in SearchPage: design issues, severity rankings, and agreed improvements (2026-04-01)
type: project
---

Results table is built in code-behind (RenderResultsPage / BuildRow in SearchPage.xaml.cs). No XAML template — all controls are instantiated imperatively.

**Architecture:**
- Outer: ScrollViewer (HorizontalScrollBarVisibility="Auto", VerticalScrollBarVisibility="Auto") containing a StackPanel (ResultsPanel, Spacing=0).
- Header row: Border wrapping a horizontal StackPanel of TextBlocks (FontSize=12, SemiBold). Background = CardBackgroundFillColorSecondaryBrush.
- Data rows: Border wrapping horizontal StackPanel. Alternating rows: odd-index rows get CardBackgroundFillColorDefaultBrush, even-index rows get no background (transparent). MinHeight=28.
- Action cells: download (E896) and preview (E8B9) are ghost Buttons (Background=null, BorderThickness=0, Width=column width, Tag="action").
- Row hover: PointerEntered sets Opacity=0.7, PointerExited resets to 1.0.
- Row click: Tapped handler; skipped if OriginalSource or ancestor has Tag="action". Opens ShowRowDetail ContentDialog.
- Column widths: fixed integers from ViewModel.GetColumnWidth(key). 120px default per-key.
- Pagination: four navigation buttons (first/prev/next/last) + PageStatusText (CaptionTextBlockStyle) + PageNumberText (BodyTextBlockStyle) + RowsPerPageCombo.
- Toolbar: StatusMessage TextBlock + Columns button + CSV button + TSV button (flat Buttons, Padding="8,4").
- Export: FileSavePicker.

**Critical issues (accessibility):**
1. Header row scrolls with data — no sticky header. With many rows the user loses column context.
2. Hover feedback via Opacity is wrong: opacity changes affect ALL child elements including text, making text harder to read on hover. The correct pattern is a background highlight using SubtleFillColorSecondaryBrush or LayerOnAcrylicFillColorDefaultBrush.
3. Row Tapped handler has no keyboard equivalent. Tab + Space/Enter cannot open row detail. Keyboard users cannot access any per-row action via the row itself.
4. Action buttons (download, preview) have no AutomationProperties.Name — Narrator reads nothing.
5. Hardcoded TextFillColorPrimaryBrush retrieved via Application.Current.Resources cast — bypasses High Contrast theme. Should be set via implicit TextBlock style or {ThemeResource TextFillColorPrimaryBrush} in XAML.
6. Opacity=0.6 on "No results found." TextBlock: in High Contrast mode opacity is ignored and the text may still be readable, but this is a semantic issue — use TextFillColorSecondaryBrush token instead of raw opacity.

**Platform consistency issues:**
7. Toolbar is a bare StackPanel of flat Buttons with no visual grouping. The Fluent pattern for table toolbars is a CommandBar (or at minimum a Border with SubtleFillColorDefaultBrush background, 4px padding, divider below).
8. CSV / TSV export buttons have no icons. Every action button in a command surface should carry a Fluent System Icon glyph.
9. PageStatusText uses CaptionTextBlockStyle — correct. PageNumberText uses BodyTextBlockStyle — also fine. But they live in the same StackPanel as navigation buttons with no visual separator between status info and nav controls. A 1px vertical divider (DividerStrokeColorDefaultBrush) between the status group and the nav group would clarify the two concerns.
10. RowsPerPageCombo has no AutomationProperties.Name — Narrator reads the current value but not what it controls.

**Polish issues:**
11. Alternating row colors: CardBackgroundFillColorDefaultBrush vs transparent. The contrast between these two is very subtle (by design). For a data-dense astronomy table this is acceptable, but the hover state at Opacity=0.7 on the lighter row will be lighter than the alternate row background — visually confusing.
12. MinHeight=28 with Padding=(4,5,4,5) = 38px effective row height. This is near the minimum 4px-grid-aligned compact density row (36px). For astronomical data with many columns this is appropriate. Do not inflate further.
13. Column selector is a ContentDialog (ColumnSelectorDialog). Adequate but introduces full modal interruption. A Flyout anchored to the Columns button is the lighter-weight Fluent alternative for non-destructive configuration.
14. No sort indicators on header cells. No sort state in ViewModel.
15. No column filtering (per-column text input). The CADC web portal provides this via TanStack.
16. Column widths fixed at 120px. No resize handles.
17. No multi-select. Single click opens detail.
18. Preview flyout opens on PointerEntered — this fires on keyboard focus too and is an anti-pattern. Flyout should open on explicit activation (button click or Enter). PointerEntered should not open transient UI.

**Achievable improvements (no DataGrid package, StackPanel approach retained):**
- P0 sticky header: move header Border outside ScrollViewer (in its own Grid row above it). The column widths must stay in sync — since they are fixed integers from ViewModel.GetColumnWidth, this is straightforward.
- P0 hover background: replace Opacity=0.7 with a background brush swap (SubtleFillColorSecondaryBrush on enter, restore original on exit).
- P0 keyboard row activation: make each data row a Button (Style="{StaticResource ListViewItemRevealStyle}" or transparent style) instead of a bare Border, so Tab+Space/Enter fires the detail dialog.
- P0 action button Narrator labels: add AutomationProperties.Name="Download observation" and AutomationProperties.Name="Preview observation" to dlBtn and previewBtn.
- P1 toolbar upgrade: wrap toolbar StackPanel in a Border with SubtleFillColorSecondaryBrush background, 8px vertical padding, 4px horizontal padding, 1px bottom divider. Add FontIcons to CSV (E8E6) and TSV (E8E6 or E9F5) buttons.
- P1 column selector as Flyout: replace ContentDialog with a Flyout anchored to Columns button. Keeps user in context.
- P1 sort by column: add SortKey + SortAscending to ViewModel. Header TextBlocks become Buttons. Clicking a header sets sort state and calls RenderResultsPage. Show sort indicator glyph (E70D asc / E70E desc) after header label.
- P2 preview flyout trigger: change from PointerEntered to Click/Tapped only. Remove PointerEntered handler.
- P2 pagination grouping: add a vertical 1px divider between status text group and nav button group.
- P2 RowsPerPageCombo: add AutomationProperties.Name="Rows per page".

**Why sticky header is P0:**
CADC results regularly return 20-50+ columns. With pagination at 25 rows/page the user scrolls vertically only a small amount, but with many visible columns the horizontal scroll is the primary axis. The header must remain visible as the user scrolls horizontally — which the current ScrollViewer wrapping both header and data already handles (horizontal scroll moves both together). The vertical sticky header problem is therefore most acute when the user scrolls down through 50+ rows per page. Given the default of 25 rows, this is a moderate issue, but correctness dictates the header should not scroll away.
