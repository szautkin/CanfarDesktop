# Tour: Cube viewer
> A detailed walk through the 3D cube viewer on a real spectral cube — volume and slice modes, display and opacity, channels and playback, spectra, marks on a channel, and export — with the app pointing at each control.
Tags: tour, cube, spectral-cube, 3D
Time: ~12 min

## Steps

- [ ] **How this tour moves** — Put a few hints up per step, then poll list_events for
      `hintsDismissed`: it arrives when the last hint goes, whether it faded on its timer, was
      closed, or the page changed. Hints fade by default; if the person asks for a slower guide,
      pass `untilClosed` to point_at_ui so each waits for its close button.
      Tool: list_events, point_at_ui
- [ ] **Have a cube open** — Reopen a recent cube or a downloaded one. A large cube answers
      `loading: true`; poll get_cube_view. If it says `downsampled`, say so: a big cube is rendered
      at a stride, and the native size is still what probing and export use.
      Tool: list_recent_cubes, open_cube, get_cube_view
      View: cubeViewer
      Note: A JCMT HARP cube is often under a megabyte — a good first cube.
- [ ] **Two ways to look** — Point at `VolumeModeButton` and `SliceModeButton`, and at
      `RenderPanel`: in volume mode drag to orbit and scroll to zoom. Turn on auto-orbit for a
      moment so they see it is three-dimensional, then turn it off.
      Tool: set_cube_view, point_at_ui
      View: cubeViewer
- [ ] **Display** — Point at `DisplayHeader`, then `StretchCombo`, `ColormapCombo`,
      `WindowLoSlider`, `WindowHiSlider`, `BackgroundCombo` and `RenderModeCombo` — emission builds
      up light along each ray, maximum intensity shows only the brightest voxel.
      Tool: set_cube_view, point_at_ui
      View: cubeViewer
- [ ] **Shaping the volume** — Point at `DensitySlider`, `SpectralSlider` (stretches the velocity
      axis) and `StepsSlider` (quality against speed), then `AutoOrbitToggle`, `CaptionsToggle` and
      `SlicePlaneToggle`. The opacity curve decides which intensities show at all.
      Tool: set_cube_view, set_cube_transfer, point_at_ui
      View: cubeViewer
- [ ] **Through the channels** — Point at `ChannelSlider` and `PlayButton` (Space toggles it).
      Play through the band once so they see where the emission is.
      Tool: set_cube_view, get_cube_channel_profile, point_at_ui
      View: cubeViewer
- [ ] **A spectrum** — In slice mode, clicking a spaxel gives its spectrum. Probe the brightest
      one and show the spectrum panel.
      Tool: probe_cube_spectrum, show_cube_spectrum
      View: cubeViewer
- [ ] **Marks on a channel** — Point at `MarksPanelToggle` and `MarksHeader`. A mark here lives on
      a voxel — a place AND a channel — rather than on the whole cube.
      Tool: annotate_cube, list_cube_annotations, point_at_ui
      View: cubeViewer
- [ ] **Panels and export** — Point at `PanelsToggle` (hide the panels to see the whole render)
      and `ExportButton`: a publication figure as PNG or PDF.
      Tool: export_cube_figure, get_cube_image, point_at_ui
      View: cubeViewer
- [ ] **Finish** — Offer the cube-kinematics workflow for moment maps and velocity fields, or a
      starter notebook against this cube.
      Tool: list_workflows, create_analysis_notebook
