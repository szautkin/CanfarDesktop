# Changelog

## [1.4.1] - Unreleased

Fixes found by testing the published 1.4.0 from the Microsoft Store, and an assistant that can walk you through Settings.

### Added
- **Settings, shown by your assistant** — `open_settings` opens Settings at a section and `close_settings` closes it, so an assistant can show you where something is set and point at it. `point_at_ui` and `list_ui_targets` now work inside any open dialog — the window behind a dialog cannot be clicked, so that is not where they point — and a hint goes when its section changes or the dialog closes. Nothing is set for you: settings stay yours to change, and some (the MCP server and auto-apply, service endpoints, the compute image, sign-ins) only you should
- **Cutouts from the archive** — download only part of a file, cut on CADC's side by its SODA service: a few MB of a 1.6 GB MegaPipe tile, at full resolution. In an observation's Files tab every file has a **Cutout…** button — live where CADC can cut that file, greyed with the reason where it cannot — and Research has one too, for the observation you are looking at; it opens an editor: a circle or box by RA, Dec and size (degrees or sexagesimal, ″ ′ °), or drawn on the file's footprint — drag to move, drag its dot to resize, drag elsewhere to draw — and, for a cube, a wavelength range. It starts from your last search's target and wavelengths when they fall on the file, checks as you type (off the file, partly off, a band the file does not have), and estimates the size. The cutout downloads like any download — in the status bar, carrying on if you move away — and is kept in Research as the observation's record, marked **Cutout** with the region, and opening with a note that it is not the complete observation. A mark on an image opened from Research has **Cut out…** in its menu. Search's **Spatial cutout** and **Spectral cutout** boxes now do what they say: a row's download is cut to the search's circle and wavelengths. For assistants: `get_cutout_options`, `download_cutout` (checked before it is queued, with the reason when it is refused) and `show_cutout_editor`
- **Cutouts from a file you have downloaded** — once an observation's file is on your computer, cut pieces of it there: at once, offline, as often as you like, and for files CADC will not cut at all, such as its HST mirror's. The same **Cutout…** button and the same editor: when both ways are open to a file, **Cut by** chooses between CADC (only the part is downloaded) and this computer (cut from the downloaded file), this computer first; the region, the checks and Research's record are the same either way, and the size shown for a local cut is exact. The pixels are copied, not converted — same BITPIX and scaling — and the header is the file's own with its size, every reference pixel and pixel sections put right for the cut and a HISTORY line saying where it came from; SIP distortion stays valid. The cut says where it sits in the file it came from (LTV/LTM), so DS9 and IRAF show the file's own pixel numbers on it — how a MegaPipe catalogue, in tile pixels, is matched to it — and every HDU carries a fresh CHECKSUM and DATASUM that astropy and fitsverify accept. A multi-extension file keeps its shape: an HST frame's SCI, ERR and DQ of the chips the region falls on, a MegaPrime frame's CCDs, each cut by its own sky coordinates, the others named in the primary header as left out — or only the images you tick under **Images** (one, two or all); a region in the gap between chips is refused as on none of them. A cube is cut to the box on every plane, or with a wavelength range to the planes it reaches — on a wavelength, frequency, energy or wavenumber axis, or a velocity axis with its line's rest frequency — each plane keeping its own frequency. An fpack `.fz` compressed the CFHT way (RICE_1, 16-bit) is cut to a plain `.fits`, decompressing only the tiles the region touches. A file that cannot be cut says why — no sky coordinates, only approximate ones, galactic or B1950, compressed another way (with the funpack advice), cut short. A weight map comes along if you want it: the observation's other files in the same folder as the downloaded one — a MegaPipe tile's `.weight` map — are offered under **Also cut**, and each is cut with the very same pixel box into a file beside the cutout, named by the same key, so pixel for pixel the two go together. That they lie on the same pixels is checked, not assumed — the same size, and the same sky at every point of a grid across the image to a hundredth of a pixel, however each header writes its coordinates; one that does not, or cannot be read, is shown greyed with why. Research keeps the cutout as a cutout of the observation, cut on this computer; **Remove file** removes its weight map with it, and **Cut out again** makes both anew from the downloaded files. A cutout in Research — CADC's or this computer's — has **Original observation**, on its detail and its right-click menu: the complete observation it was cut from, selected in Research when Research already has it (with its file or without), otherwise found in Search by its observation ID, with the form cleared so nothing from the last search narrows it away, and the cutout's own row highlighted. For assistants: `show_research_observation` shows a record in Research as a click does, and with `original` does what that button does Download on an observation now fetches the same file it holds, not whichever DataLink lists first. For assistants: `cutBy` (`soda` or `local`), `extensions` and `companions` on `download_cutout` and `show_cutout_editor`, and `get_cutout_options` lists each way a file can be cut, the images a local cut can choose among, the files it can take along, and why a way cannot cut
- **Copy an observation, or any result** — right-click any cell of the search results to copy its value, the observation's details, or the row — or, with several rows chosen, all of them — as tab-separated text that pastes into a spreadsheet; Ctrl+C copies the chosen rows. Research and the observation view have **Copy details**, and an observation in Research's list has it on its right-click menu. The details read the same wherever they are copied from: ID, collection, publisher ID, target, the position as the search box, Simbad and DS9 read it with the degrees beside it, instrument, filter, date, calibration level, proposal, release, and for a cutout its region and how it was cut. Every copy says so in the status bar, and one the clipboard refuses is not claimed. For assistants: `copy_to_clipboard` copies text, or an observation's details — the same text Copy details copies
- **Keep an observation without its file** — **Save to Research** in an observation's view keeps it in Research — its details and a place for notes — without downloading anything; Download there fetches the file when you want it. It never replaces an observation already in Research. For assistants: `save_observation_to_research`
- **Remove a file, keep the observation** — **Remove file** in Research deletes the file from this computer, after asking, and keeps the observation with its notes; Download brings it back — for a cutout, the same cutout. For assistants: `remove_downloaded_file`, which always waits for your approval

### Fixed
- **Public only in Search** — ticking it made every search fail with "Function [GETDATE] is not found in TapSchema": CADC's TAP service has no function for the current time. The release date is now compared with the time of the search, written out, as CADC's own search does
- **Stopping a search** — a search that took long could not be stopped: Search stayed greyed with a spinner until CADC answered. **Cancel** now appears beside the spinner, on the form and in the ADQL editor, and stops it; the results already shown stay, and a cancelled search is not kept as a recent one. A failed search now stays on the form, where its error is, instead of switching to the previous results; and an assistant's `run_search` is told when the person cancelled it; `cancel_search` is the button for assistants
- **Proprietary files, and the observation view's downloads** — DataLink, which every observation download goes through, was not signed in, so a proprietary observation's files were refused even to their owner; it now signs in like the rest of the app (the token only ever goes to CADC). The observation view had a download of its own that died with the view and showed progress only there; it now goes through the app's downloader like Search's and Research's, and a failed download shows CADC's own reason rather than a bare HTTP status
- **Clicking a search result** — five columns (collection, instrument, target, proposal, PI) were silent filter links, so clicking a row's target name narrowed the table instead of opening the observation, and every click rebuilt the whole page of rows before it did anything. A click anywhere on a row except Download and Preview now opens the observation at once, with its loading spinner while the metadata arrives; narrowing to a value is on those cells' right-click menu
- **The footprint sketch** — the observation view's Coverage tab stretched every footprint to fill its box, ignoring that a degree of RA is shorter than a degree of Dec away from the equator, and broke for a field across RA 0°; it is now drawn as the sky is, north up and east left, at its true shape
- **Positions on distorted images** — on images whose header gives only the forward SIP distortion (HST's calibrated frames among them), going from the sky to a pixel skipped the distortion: up to 6.6 pixels (0.26″) out across a WFC3 frame. Go To, marks pinned to the sky, sky-region figures and aligning tabs by sky position were affected; a position now lands on its own pixel
- **Go To off the image** — the assistant's `fits_goto_coordinate` reported a move for a position off the image while the viewer, rightly, stayed where it was; both now give the same answer, and say where the position falls
- **Viridis in the FITS viewer** — was a teal-to-orange approximation rather than viridis, and figures went out labelled VIRIDIS; the FITS viewer now uses the cube viewer's colour tables, so every colormap looks the same in both
- **Notebook cells** — a cell showed the value of its last line only when the whole cell was one expression; like Jupyter, a cell ending in an expression now shows it (`df.head()` after a few statements), and a trailing `;` hides it
- **Remote Compute after a reinstall** — a compute session still running on your account showed as "Not set up", with no way to see or stop it from the screen; it is now shown with Stop whether or not compute is set up in this install, and `get_compute_state` reports it too
- **Finding a tool** — `search_tools` matched the whole query as one phrase, so "cube spectrum" found nothing; it now matches the words
- **Service health** — `usableCount` left out the services that need sign-in even while you were signed in
- **Queries read back** — the ADQL `run_search` reported came back with bare carriage returns for line breaks
- **Your assistant after Verbinal restarts** — closing Verbinal left each assistant's MCP bridge stuck: it answered nothing, kept its file locked, and dropped the next request, so the assistant lost Verbinal until you reconnected it by hand. The bridge now answers "not running" at once while Verbinal is closed, picks the connection up again when it starts, and answers anything Verbinal closed in the middle of; a bridge started before Verbinal no longer keeps refusing once it is running
- **Very large images in the FITS viewer** — anything over 512 MB was refused whatever the machine had free, so a 20315 × 20475 MegaPipe tile (1.6 GB) would not open even with room to spare. The limit is now the memory actually free, and what is drawn is a picture of the image at most 64 million pixels (a 1.6 GB tile is shown at 40%, noted in the status bar), while its full pixels stay behind every readout, coordinate, mark and export. The file is converted as it is read rather than held twice, a stream that returns its bytes a few at a time no longer reads as truncated, and when an image will not fit the message says how much it needs and how much is free, and suggests a cutout
- **Large downloads your assistant starts** — a download taking longer than two and a half minutes (a 1.6 GB MegaPipe tile) was reported to the assistant as timed out while it carried on, kept every other change waiting behind it (Apply spun and then said "the apply queue is busy"), and stayed in Pending as if refused after it had landed in Research. It now carries on as a background job the assistant can follow, leaves Pending as applied when it lands, and shows as applying rather than offering Apply; a busy queue says which change it is waiting on; and a download that stops receiving data fails after a minute instead of holding the queue

## [1.4.0] - 2026-09-25

Marks on images and cubes, figure export from the FITS viewer, an AI assistant that can see the viewers and point at the interface, and a round of accuracy fixes found by measuring against Verbinal for Linux 1.4.4.

### Added
- **Marks** — draw boxes, circles, callouts and text on FITS images and cubes, pinned to the sky, to image pixels, or to a cube channel. A Marks panel lists, filters and styles them (colour, weight, label size, outline); grips resize a selected mark; every shape carries its label on a leader. Right-click or the Menu key opens a menu per mark: edit label, copy coordinates (in a form the Search box reads), centre, search here, export a figure around it, delete. Marks are stored per file — per extension, in a multi-extension FITS file — and export as JSON with provenance or as a DS9 region file
- **FITS figure export** — export the view or a selected region (the Select area button, or Ctrl-drag) as PNG or PDF at 1×, 2× or 4×, with header and footer, marks, dark or light theme and a transparent background. Cube figures carry their marks too
- **Container images** — search the image registry for images the platform's catalogue does not list, add them to your own list, and see what is installed inside an image. The images card filters by project and shows only what a launch tab can use
- **Notebooks** — `.py` (percent-format) and `.md` files open as cells and save back in their own format; outputs render SVG, Markdown, LaTeX and HTML
- **ADQL checked before it runs** — the ADQL editor validates the query against the service's TAP_SCHEMA as you type, lists each problem (click one to jump to it), and disables Execute until the query can run
- **Activity bar and job history** — a status bar shows what the app and an agent are working on; finished batch jobs, and why the failed ones failed, are remembered after CANFAR removes them
- **FITS viewer** — images open fitted to the window, the empty screen offers recently opened files, large files show loading progress, and the toolbar scrolls sideways (wheel or step buttons) when the window is too narrow for it
- **Accessibility** — every launch-form field, the image canvas and the new marks and export controls have names a screen reader announces
- **Search results** — Shift-click selects a range of rows, and neither it nor Ctrl-click opens a detail window over the rows being compared
- **Agent sound cues** — two short sounds when an AI agent starts and finishes work (Settings, on by default)
- **About** — the app's own links and a copyable runtime-info block for bug reports
- **Remote Compute** — a screen for the code an AI assistant runs on CANFAR with `run_code`, which until now left no trace in the app. It shows the `verbinal-compute` session's state, with Start and Stop; every run — the assistant's and your own — with its code, output and errors; and a box to run a Python or Bash snippet yourself. Until a compute image is set, it explains how to set one up, with links to the [verbinal-execution](https://github.com/szautkin/verbinal-execution) watcher image and to Settings. The Portal marks the session as the assistant's, the activity bar shows a run while it is out, and with "follow agent activity" on the app brings Remote Compute up when an assistant runs code, so the run appears in front of you as it happens. The AI Assistant wizard mentions it once your assistant is connected, as an optional next step rather than part of connecting. Six new tools match the screen: `get_compute_state` and `list_compute_runs` read it, `show_compute_run` puts a run on screen, `set_compute_snippet` fills the code box for the person to run, `get_compute_view` reads what it shows, and `show_storage_folder` opens Storage at a folder (the exec folder, or any in the person's home)
- **AI assistant (MCP): 31 new tools** — see what the viewers show (`get_fits_image`, `get_cube_image`, `get_cell_image`); draw and manage marks (`annotate_fits`, `annotate_cube`, `list_fits_annotations`, `list_cube_annotations`, `update_annotation`, `select_annotation`, `remove_annotation`, `clear_annotations`, `export_annotations`); export figures (`export_fits_figure`); point at a control on screen, including one inside a folded-shut section (`list_ui_targets`, `point_at_ui`), which also powers a guided onboarding tour workflow; close tabs (`close_tab`); search-page detail (`show_search_row_detail`, `show_observation_detail`, `validate_adql_query`, `describe_tap_schema`); container images (`search_image_registry`, `add_registry_image`, `remove_registry_image`, `list_my_images`, `describe_image`, `search_packages`); a map of the tool surface (`list_apps`, `search_tools`, `man`); and work that outlives the call that started it (`start_background_apply`, `get_job_status`). Changes waiting for your approval now survive an app restart
- **Any AI assistant** — `AGENTS.md`, in the repository and in the app's install folder, tells any MCP client (Codex, Copilot, Cursor, Gemini and the rest) how to connect, written for the agent to follow. The bridge it points to is now kept at its fixed location whenever the MCP server is on, not only after the connect wizard has run

### Fixed
- **WCS rotation on modern headers** — headers that describe rotation with a PC matrix (JWST among them) had it ignored, so positions drifted by up to 40–90″ toward the frame edge. Affects the crosshair, Go To, cross-tab sync, marks and the assistant's sky readouts
- **Sexagesimal rounding** — coordinates just under a whole minute printed as `23h59m60.00s`; the carry now happens everywhere a position is formatted
- **Downloads that produced nothing** — an empty archive response is no longer recorded as a successful download; a failed or cancelled download no longer truncates an existing file; JWST products fetch the science file rather than the first listed artifact; records fetched by publisher ID get their observation details
- **Service health** — services are checked through their IVOA availability document, so planned downtime shows as down with its note and a 4xx answer no longer gets a tick
- **VizieR** — two mirrors that no longer resolve are gone, so a cone search no longer starts with two certain failures; `vizier_cone_search` accepts `columns`
- **"Search here" from the FITS viewer** — filled the Search box with a coordinate form the search could not parse, so the search only worked until anything cleared the resolved position
- **Opening large files through the AI assistant** — `open_fits_file` and `open_cube` reported a failure when a big file simply took longer than the call could wait; they now answer "still loading", so an agent no longer retries and opens duplicates
- **Compressed FITS** — every extension of a `.fits.fz` file reported the compressed table's shape instead of the image's; opening a file that is already open switches to it instead of adding a duplicate tab
- **Small cubes** — a cube only a few pixels wide on the sky collapsed to a single voxel when downsampled
- **Cube viewer idle CPU** — a cube that was just open re-rendered every frame, which could delay other work in the app
- **Figure export** — the export dialog scrolls to its buttons on small windows, a 4× figure too large to render reports the scale it actually achieved, and a transparent-background PDF has a white page instead of a black one
- **Notifications from polling** — sessions and jobs are polled more often while something is changing, so a job that starts and fails quickly is still announced
- **Localization** — strings set from code showed their resource keys, and several new controls showed English in French
- **Portal from an assistant while signed out** — `navigate_to` opened an empty Portal without asking you to sign in
- **The MCP bridge in Store packages** — each package now builds its own, for its own architecture. The x86 packages of 1.3.2 and 1.3.3 carried a 64-bit bridge, so an assistant could not connect on 32-bit Windows
- **Connecting after an update** — an assistant already connected kept running the old bridge, because Verbinal could not replace its copy while it was in use; and opening the connect wizard then could point the assistant at the version-numbered install folder, which broke at the next update. The copy is now replaced even while it runs (the assistant picks the new one up when it next starts it), and an assistant is only ever pointed at that one fixed copy

### Changed
- **Cube viewer** — one control column instead of two overlapping panels; side panels slide in and out
- **Home screen** — the tiles run Portal, Remote Compute, Storage, then Search, Research, FITS Viewer, Cube Viewer, Notebook, Workflows, AI Guide and AI Assistant. Remote Compute is locked until you sign in, as Portal and Storage are, however it is opened — its tile, an assistant's `navigate_to`, a workflow step or the wizard — and its tools answer an assistant "sign in required" until then. Following an assistant never asks you to sign in: it just does not follow it onto a locked screen

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
