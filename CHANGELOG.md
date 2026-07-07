# Changelog

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
