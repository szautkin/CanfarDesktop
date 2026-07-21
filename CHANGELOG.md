# Changelog

## [1.3.3] - 2026-07-20

Patch: full MCP UI coverage for the Search page and the FITS and Cube viewers, plus QA fixes (masked-cube spectra, service-health honesty, UI-busy tool hangs, workflow CRLF preservation).

### Added
- **Search MCP: 100% UI coverage** — an agent can now drive everything a user can on the CADC Archive Search page. New tools: `get_search_form` / `set_search_form` (every field of the four constraint columns + Max Records, with the same debounced target resolution as typing), `get_search_constraints` / `set_search_constraints` (the Additional Constraints data-train facets — band, collection, instrument, filter, cal. level, data type, obs. type — with the UI's cascade narrowing and dropped-value reporting), `reset_search_form`, `run_search` (the Search button: form → ADQL → Results tab + Recent Searches entry), `set_adql_query` / `execute_adql_query` (the ADQL Editor tab), `get_search_results` / `set_search_results_view` (the results table: pagination, rows per page, column sort, per-column filters, column visibility, display units, and Apply-to-ADQL), `export_search_results` (CSV/TSV to a local file, no picker), `load_recent_search` and `run_saved_query` (the side-panel buttons), plus `remove_recent_search` / `clear_recent_searches` as Destructive proposals (always queued for user approval)
- **FITS Viewer MCP: 100% UI coverage** — an agent can now drive everything a user can. New tools: `blink_fits_tabs` (the WCS-aligned blink comparison: start with a partner tab, fade interval, pause/resume, stop) and `switch_fits_tab`. Extended: `open_fits_file` accepts a local file path (like `open_cube`); `set_fits_view` gains HDU/extension switching, pixel crosshair placement and pixel centering (both work without WCS), the sync-zoom and linked-crosshair toggles, and the header/bookmarks panel controls; `get_fits_view` now mirrors the full UI state (local path, HDU list + selection, pixel unit + data range, pixel scale / north angle / parity / approximate-WCS flag, blink status, panel visibility, crosshair pixel, status message); `list_open_tabs` lists each FITS tab with its index/name/active flag
- **Cube Viewer MCP: 100% UI coverage** — an agent can now drive everything a user can. New tools: `set_cube_transfer` (the opacity-curve editor: set/reset control points), `show_cube_spectrum` (the visible spaxel click — opens the on-screen spectrum panel, not just data), `get_cube_channel_profile` (the scrubber waveform: per-channel means + spectral axis), `switch_cube_tab` and `list_recent_cubes`. Extended: `set_cube_view` gains slice zoom/center/reset and the Min-Max / 99% window presets; `export_cube_figure` gains the export dialog's style options (font, text color, text scale, annotations, transparent background); `get_cube_view` now mirrors the full UI state (info-panel stats, native vs render dims, slice view, spectrum panel, transfer curve); `list_open_tabs` lists each cube tab with its index/name/active flag

### Fixed
- **Additional Constraints empty on first run** — with no cached data train, the facet lists stayed empty until the next launch (the network refresh was fire-and-forget). The MCP constraints tools now load the data train synchronously (cache first, then network) via the new `SearchViewModel.EnsureDataTrainAsync`
- **`probe_cube_spectrum` on real (masked) cubes** (QA F10) — blanked voxels (NaN/Inf) crashed the JSON serializer; they now arrive as `null` flux entries with a `blankedChannels` count. Coordinates are native cube pixels (they were checked against the GPU-downsampled volume, so valid pixels read as out-of-range), the spectral axis maps strided channels to true world values, and "no cube" / "out of range" / "UI busy" are distinct typed errors instead of one null
- **Spectral channel labels on downsampled cubes** — the slice label, hover chip, exported figure plate, and `get_cube_view` showed the world value of the *rendered* channel index instead of the native channel it represents
- **`probe_fits_pixel` on blanked pixels** — a NaN pixel value crashed the JSON serializer; `value` is now omitted for blanked pixels
- **`get_service_health` honesty** (QA F3) — a 404/5xx answer was reported as a healthy service (CADC auth was probed at its base URL, which always 404s). The auth probe now targets `/whoami`, each entry carries `ok` alongside `reachable`, the summary adds `healthyCount`, and the Settings connection test shows the failing HTTP status instead of "OK"
- **MCP tool hangs when the UI thread is busy** (QA F5) — UI-marshaled tool calls now fail after 30 s with a descriptive "UI busy" error instead of hanging behind a saturated dispatcher queue
- **Cube notebook template without `spectral-cube`** (QA F11) — the generated cell guards the import and prints the exact `%pip install` fix instead of dying with a bare `ModuleNotFoundError`
- **Workflow step toggle reformatted files** — flipping a `[ ]`/`[x]` marker rewrote every CRLF line ending as LF, violating the "only one byte changes" contract on Windows-authored workflow files; the marker is now flipped in place

## [1.3.2] - 2026-07-16

Patch: reliability fixes for packaged (Microsoft Store) installs — notebook kernel startup and the AI-assistant connection — plus a landing-page layout fix.

### Fixed
- **Notebook kernel start on packaged installs** — `python.exe` couldn't see the execution harness ("file not found": MSIX write virtualization sandboxes AppData writes); the harness now lives in the package's LocalCache, which both the app and Python see
- **Kernel restart race** — rapid interrupt/restart can no longer overlap two starts (one could delete the other's in-use harness file); unexpected kernel exits now log stderr for diagnosis
- **AI-assistant connection on packaged installs** — the MCP bridge is copied to the package LocalCache (a real path Claude Desktop can launch, surviving app updates), and the Store package now actually ships the bridge exe
- **Claude Desktop config writes** — Store-installed Claude configs are written directly; a traditional install's config (which MSIX would silently sandbox) now gets an honest guided step instead: the merged JSON is copied to the clipboard with the exact file path shown (EN + FR)
- **Landing page on small windows** — vertical scrollbar when the window is too short (the second tile row was unreachable); tiles reflow to fewer columns on narrow windows

### Changed
- **Windows App SDK 2.2** (from 1.8) plus updated runtime components (SQLite, MVVM Toolkit, DI/HTTP extensions)
- Packaging: publish profiles restored so Release/Store packaging (ReadyToRun) builds again

## [1.3.1] - 2026-07-07

Patch: FITS-viewer accuracy fixes and new tools, plus folder-sharing and search fixes.

### Added
- **Image Info panel** — at-a-glance summary above the raw header (dimensions, WCS solution + precision, pixel scale, field of view, sky centre, orientation, instrument/filter/date/exposure)
- **Extension (HDU) selector** in the FITS-viewer left panel for multi-extension files
- **Viewer choice** — files with a third axis get a dropdown to open in the 2D FITS or 3D Cube viewer (spectral cubes default to Cube, detector stacks to FITS)
- **Multi-plane cube support** (e.g. HST WFPC2) that previously failed with a "header size" error

### Changed
- **Mouse-wheel zoom** by default; Ctrl+wheel pans up/down, Shift+wheel left/right
- **"Search here"** from the FITS viewer opens the search form with coordinates pre-filled

### Fixed
- **SIP distortion** now applied — crosshair, Go To, and cross-tab sync land correctly on wide-field/TESS data (was off by arcminutes near the edges); readout uses the exact sub-pixel position
- **Blink comparison** frames the shared field so both images are comparable (the second image no longer renders as a tiny square)
- **Folder sharing** — VOSpace folders can be made public or shared with a group (`set_vospace_acl` 400 on containers)
- Crosshair stays on its star through panel resizes; hides when a linked position is off-image; warns when a synced image has only an approximate WCS
- `set_vospace_acl` no longer leaves a doomed proposal queued on a deterministic backend failure

## [1.3.0] - 2026-07-06

First release of the Workflows / AI-assistant generation.

### Added
- **Workflows** — research-protocols tile with markdown-checklist protocols, local + VOSpace storage, and Canada-first templates
- **AI assistant** — guided connect wizard to pair an AI agent (Claude Desktop) over MCP, plus an **AI Guide** for tuning how the agent sees each tool
- **Cube viewer** — 3D spectral-cube volume renderer with spectrum probing and figure export
- **Editable service endpoints** in Settings (all CANFAR/CADC hosts) with a connection self-test
- **In-app fpack (RICE_1) decompression** — `.fits.fz` files open directly
- **Full French localization** across the app

### Fixed
- Expired-session handling: a mid-session 401 now always leads back to sign-in
- Numerous macOS-parity, 4K-layout, notebook, and MCP reliability fixes

## [1.1.0] - 2026-04-05

### Added
- **Multi-tab FITS Viewer** — open multiple FITS files in tabs with shared toolbar
- **Saved Coordinates** — bookmark RA/Dec positions, manual Go To, persistent bookmarks panel
- **Linked Crosshair** — place crosshair in one tab, see it in all others at the same sky position
- **Sync Zoom** — match angular extent across tabs with different pixel scales
- **Blink Comparison** — smooth fade overlay between two images for transient detection
- **North Up** — orient images with celestial North up using WCS rotation
- **Back Navigation** — navigate between modules without losing context
- **Download Progress** — real-time byte counter and progress bar during FITS downloads
- **VOSpace to Viewer** — right-click .fits in Storage to open directly in FITS Viewer
- **Cell Copy/Paste** — C/V command keys in Notebook (static clipboard across tabs)
- **WorldCoordinate Record** — immutable RA/Dec type with value equality and formatting
- **ViewportMath Helper** — testable coordinate transforms for center-origin rotation
- **BlinkAligner** — pure math for WCS-aligned blink overlay
- **Feature Documentation** — 7 doc pages with screenshots in docs/ folder
- 48 new unit tests (529 total)

### Changed
- Landing tiles are now Buttons (keyboard-accessible, focus ring, Narrator support)
- Landing tile backgrounds use CardBackgroundFillColorDefaultBrush (visible on dark wallpapers)
- Login button uses AccentButtonStyle (blue, prominent)
- FITS toolbar consolidated: crosshair actions in dropdown, icon-only toggles
- Zoom slider removed (editable combo + scroll wheel sufficient)
- Settings moved from Notebook toolbar to app title bar
- Status bar text upgraded from Tertiary to Secondary brush
- Crosshair tooltip appears on hover only (not permanently visible)
- Crosshair colors use theme-aware SystemFillColorAttentionBrush
- About dialog uses semantic text tokens (no hardcoded opacity)
- Storage sort headers are keyboard-focusable Buttons
- NotebookTabHostViewModel registered as Singleton (was Transient)
- FitsTabHostViewModel registered as Singleton

### Fixed
- ContentDialog crash when downloading from row detail dialog
- Image loss when switching FITS tabs (Unloaded event was destroying image)
- Lambda event handler leaks in FitsTabHost (stored and cleaned up on close)
- CancellationTokenSource never disposed in FitsViewerViewModel.RenderAsync
- DispatcherTimer.Tick not unsubscribed in StopBlink
- CurrentHeader not notified on HDU change
- HasTabs never raised PropertyChanged
- Blink flyout Click handlers accumulated without cleanup
- Sync zoom only propagated from scroll wheel (not slider/combo)
- Crosshair drift on tab switch (floating-point re-derivation)
- async void methods without try/catch in MainWindow

### Security
- Removed user code content from kernel execution logs
- VOSpace folder name validates against path traversal (../, /, \)
- Kernel harness written to LocalAppData with random filename (not predictable %TEMP%)
- FITS parser: 512 MB data cap, 1000-block header limit, NAXIS capped at 999
- Notebook cell count capped at 10,000
- Executable extension blocklist on UseShellExecute (.exe, .bat, .cmd, .ps1, etc.)
- FitsParser integer overflow guard for images >32K pixels
- FitsRenderer cancellation-aware (checks every 64 rows)
- .claude/ agent memory removed from repository

## [1.0.5] - 2026-04-04

### Added
- FITS Viewer with native C# parser, stretch/colormap, WCS crosshair
- Jupiter Notebook engine (local Python execution, multi-tab)
- Local file browser side panel
- Search-to-FITS download pipeline with DataLink
- Research module for downloaded observations

## [1.0.0] - 2026-03-15

### Added
- Initial release: Portal, Search, Storage modules
- CANFAR session management
- CADC TAP/ADQL archive search
- VOSpace storage browser
