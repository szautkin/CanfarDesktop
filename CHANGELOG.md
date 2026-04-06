# Changelog

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
