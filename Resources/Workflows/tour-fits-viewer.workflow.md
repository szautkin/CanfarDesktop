# Tour: FITS viewer
> A detailed walk through the 2D FITS viewer on a real image — display and stretch, extensions and headers, WCS and the crosshair, saved coordinates, tabs and blink, marks, and figure export — with the app pointing at each control.
Tags: tour, FITS, imaging, WCS, marks
Time: ~15 min

## Steps

- [ ] **How this tour moves** — Put a few hints up per step, then poll list_events for
      `hintsDismissed`: it arrives when the last hint goes, whether it faded on its timer, was
      closed, or the page changed. Hints fade by default; if the person asks for a slower guide,
      pass `untilClosed` to point_at_ui so each waits for its close button.
      Tool: list_events, point_at_ui
- [ ] **Have an image open** — Use one they already have: a downloaded observation, or a local
      file. With nothing open, point at `OpenImageButton` instead. A large file answers
      `loading: true`; poll get_fits_view until it is loaded rather than opening it twice.
      Tool: list_downloaded_observations, open_fits_file, get_fits_view
      View: fitsViewer
      Note: Offer a download only with their yes, and prefer something small.
- [ ] **Display** — Point at `StretchCombo`, `ColormapCombo`, `MinCutSlider` and `MaxCutSlider`,
      then `Reset stretch and view` and `ZoomPresetCombo`. Change the stretch to asinh while they
      watch — faint structure appearing is the whole argument for a stretch.
      Tool: set_fits_view, point_at_ui
      View: fitsViewer
- [ ] **Extensions and header** — Point at `HduList` (a mosaic has one extension per chip) and
      `Toggle FITS header panel`, then `HeaderFilter` and `ImageInfoList`. Say a mark belongs to the
      extension it was drawn on.
      Tool: set_fits_view, get_fits_header, point_at_ui
      View: fitsViewer
- [ ] **On the sky** — Point at `NorthUpToggle`, `Crosshair actions` and `ImageCanvas`. Put the
      crosshair on a source by its coordinates and read its pixel value; from the crosshair menu,
      "Search here" sends the position to Search.
      Tool: fits_goto_coordinate, get_fits_wcs, probe_fits_pixel, point_at_ui
      View: fitsViewer
- [ ] **Saved coordinates** — Point at `Toggle saved coordinates panel`, then `CoordLabelBox`,
      `ManualRaBox` and `ManualDecBox`: named positions to come back to.
      Tool: save_fits_bookmark, list_fits_bookmarks, delete_fits_bookmark, point_at_ui
      View: fitsViewer
- [ ] **Tabs and comparison** — Point at `TabViewControl` and `AddButton`, then `SyncZoomToggle`,
      `LinkedCrosshairToggle` and `Blink comparison`: two images of one field, aligned on the sky,
      flipped back and forth to show what moved or changed.
      Tool: list_open_tabs, switch_fits_tab, blink_fits_tabs, point_at_ui
      View: fitsViewer
- [ ] **Marks** — Open the marks panel, then point at `DrawToggle`, `KindCombo`, `ColourButton`,
      `Bold`, `FontSizeBox`, `Stroke` and `MarkList`. Draw one mark with them; say a right-click on
      a mark opens its menu — rename, copy coordinates, centre, search here, export.
      Tool: set_fits_view, annotate_fits, list_fits_annotations, select_annotation, point_at_ui
      View: fitsViewer
- [ ] **Look after the marks** — Point at `Rename this mark`, `Delete this mark`, `ClearButton`
      and `ExportButton`: marks export as JSON with provenance, or as a DS9 region file.
      Tool: update_annotation, remove_annotation, export_annotations, point_at_ui
      View: fitsViewer
- [ ] **A figure** — Point at `SelectAreaToggle` and `ExportFigureButton`: a region, at up to four
      times its size, as PNG or PDF, with its marks and a header and footer if they want them.
      Tool: export_fits_figure, point_at_ui
      View: fitsViewer
- [ ] **A narrow window** — When the window is too narrow for the toolbar, `ToolbarBackButton` and
      `ToolbarForwardButton` appear at its ends; point at whichever is showing.
      Tool: list_ui_targets, point_at_ui
      View: fitsViewer
