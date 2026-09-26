using Microsoft.UI.Xaml;
using Windows.Foundation;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Views.FitsViewer;

/// <summary>
/// What this viewer means by a mark's menu.
///
/// <para>Six of its seven entries were already things the app could do and only an agent could ask
/// for: centring on a mark, searching its position, framing a figure on it, writing the marks out.
/// A person had no way to reach any of them, because none of them is a toolbar button — they are all
/// "do this to THAT mark", and until there was a menu there was nowhere to say which mark.</para>
///
/// <para>The list itself is <see cref="MarkCommands"/> and the drawing of it is
/// <see cref="Controls.MarkContextMenu"/>. This is only the part that knows what the entries mean
/// here, which is the part that differs between a flat image and a cube.</para>
/// </summary>
public sealed partial class FitsViewerPage : IMarkCommandHost
{
    /// <summary>
    /// What can be done with this mark on this image.
    ///
    /// A sky-anchored mark always has a position to search; a pixel-anchored one has one only if the
    /// image carries a WCS. Without it the mark is perfectly good — there is simply nowhere to send
    /// a search, and the entry is greyed rather than hidden so that is visible.
    /// </summary>
    public MarkCommands.Context CommandContextFor(string id)
        => new(CanLocateOnSky: SkyOf(Marks.ById(id)) is not null, CanExportFigure: true,
               CanCutOut: CutoutSource() is not null && Marks.ById(id) is { } m && SkyRegionOf(m) is not null);

    /// <summary>
    /// A mark's region to cut from the archive: which observation (and, for a cutout, which of its
    /// files) and the region on the sky. The host opens the cutout editor on it.
    /// </summary>
    public event Action<string, string?, Models.Cutouts.SkyRegion>? CutoutRequested;

    /// <summary>What each entry does here. Every one of them is also an MCP tool.</summary>
    public void InvokeMarkCommand(MarkCommand command, string id)
    {
        // Writing the marks out is about the file, not about one mark — the panel's button raises it
        // with no mark at all — so it is answered before anything looks a mark up.
        if (command == MarkCommand.ExportMarks)
        {
            _ = ExportMarksAsync();
            return;
        }

        if (Marks.ById(id) is not { } mark) return;

        switch (command)
        {
            case MarkCommand.EditLabel:
                Marks.Select(id);
                Marks.BeginLabelEdit(id);
                break;

            case MarkCommand.CopyCoordinates:
                CopyMarkCoordinates(mark);
                break;

            case MarkCommand.Centre:
                if (DisplayPixelOf(mark) is { } at) CenterOnImagePixel(at.X, at.Y);
                break;

            case MarkCommand.SearchHere:
                if (SkyOf(mark) is { } sky) SearchAtPositionRequested?.Invoke(sky.Ra, sky.Dec);
                break;

            case MarkCommand.ExportFigure:
                if (ViewModel.ImageData is { } image &&
                    RegionAround(mark, image, image.Wcs) is { } region)
                    DispatcherQueue.TryEnqueue(() => _ = ShowExportDialogAsync(region));
                break;

            case MarkCommand.CutOut:
                if (CutoutSource() is { } source && SkyRegionOf(mark) is { } skyRegion)
                    CutoutRequested?.Invoke(source.PublisherId, source.ArtifactId, skyRegion);
                break;

            case MarkCommand.Delete:
                Marks.Delete(id);
                break;
        }
    }

    // ── Cutting a mark's region from the archive ────────────────────────────────────────────────

    /// <summary>
    /// The archive observation this file is, from its Research record, and which of its files: the one
    /// it was downloaded as, or — when the file is itself a cutout — the file it was cut from, so a mark
    /// on a cutout cuts the same file again. Null for a file Research does not know as an observation.
    /// </summary>
    private (string PublisherId, string? ArtifactId)? CutoutSource()
    {
        if (string.IsNullOrEmpty(ViewModel.FilePath)) return null;
        try
        {
            var open = System.IO.Path.GetFullPath(ViewModel.FilePath);
            var store = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<Services.ObservationStore>(App.Services);
            var record = store.Observations.FirstOrDefault(o => !string.IsNullOrEmpty(o.LocalPath)
                && string.Equals(System.IO.Path.GetFullPath(o.LocalPath), open, StringComparison.OrdinalIgnoreCase));
            return record is null || string.IsNullOrEmpty(record.PublisherID)
                ? null
                : (record.PublisherID, record.Cutout?.ArtifactId ?? record.ArtifactId);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The mark as a region on the sky: a box for a rectangle, a circle for anything else, its size
    /// through the image's pixel scale. A rectangle on a rotated image becomes the box that is square
    /// to RA and Dec — SODA cuts on the sky, not along the image's axes.
    /// </summary>
    private Models.Cutouts.SkyRegion? SkyRegionOf(Annotation mark)
    {
        if (SkyOf(mark) is not { } sky || ViewModel.ImageData is not { } image
            || image.Wcs is not { IsValid: true } wcs || wcs.PixelScaleArcsec <= 0) return null;

        var surface = new FitsExportSurface(FitsRegion.WholeImage(image.Width, image.Height),
            image.Width, image.Height, wcs, image.Height);
        var degreesPerPixel = wcs.PixelScaleArcsec / 3600;
        var (halfW, halfH) = mark.Extent is { } extent
            ? (extent.HalfWidth * surface.UnitsToPixels(mark.Anchor) * degreesPerPixel,
               extent.HalfHeight * surface.UnitsToPixels(mark.Anchor) * degreesPerPixel)
            : (20 * degreesPerPixel, 20 * degreesPerPixel); // a bare point: the same 20 px a figure frames it with

        return mark.Kind == AnnotationKind.Rect
            ? Models.Cutouts.SkyRegion.Box(sky.Ra, sky.Dec, 2 * halfW, 2 * halfH)
            : Models.Cutouts.SkyRegion.Circle(sky.Ra, sky.Dec, Math.Max(halfW, halfH));
    }

    // ── Where a mark is ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The mark's sky position, whichever space it was pinned in, or null when there is none to be had.
    /// </summary>
    private (double Ra, double Dec)? SkyOf(Annotation? mark)
    {
        if (mark is null) return null;
        if (mark.Anchor.Space == AnchorSpace.Sky) return (mark.Anchor.X, mark.Anchor.Y);

        if (ViewModel.ImageData is not { } image || image.Wcs is not { IsValid: true } wcs) return null;

        return PixelConvention.SkyAtDisplay(wcs, image.Height, mark.Anchor.X, mark.Anchor.Y);
    }

    /// <summary>
    /// The mark's display pixel — the space the view centres on, and the space a pixel anchor is
    /// already in.
    /// </summary>
    private (double X, double Y)? DisplayPixelOf(Annotation mark)
    {
        if (mark.Anchor.Space != AnchorSpace.Sky) return (mark.Anchor.X, mark.Anchor.Y);

        if (ViewModel.ImageData is not { } image || image.Wcs is not { IsValid: true } wcs) return null;

        return PixelConvention.DisplayOfSky(wcs, image.Height, mark.Anchor.X, mark.Anchor.Y);
    }

    /// <summary>
    /// The mark's position on the clipboard, in the form our own Search box can read.
    ///
    /// See <see cref="MarkClipboard"/> for why that is the deciding constraint and not legibility.
    /// </summary>
    private void CopyMarkCoordinates(Annotation mark)
    {
        var text = SkyOf(mark) is { } sky
            ? MarkClipboard.Sky(sky.Ra, sky.Dec)
            : MarkClipboard.ImagePixel(mark.Anchor.X, mark.Anchor.Y);

        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            ViewModel.StatusMessage = Loc.T("Marks_CopiedCoordinates");
        }
        catch
        {
            // A clipboard that refuses is not worth interrupting anyone over.
        }
    }

    /// <summary>Write these marks out, through the same call <c>export_annotations</c> makes.</summary>
    private async Task ExportMarksAsync()
    {
        if (Target is not { } target) return;

        // Marks belong to one extension, so the suggested name says which: a folder of exports from
        // a forty-chip mosaic is unreadable when every file is called the same thing.
        var file = System.IO.Path.GetFileNameWithoutExtension(Helpers.MarkTarget.PathOf(target));
        var chip = ViewModel.Hdus?.FirstOrDefault(h => h.Index == ViewModel.SelectedHduIndex)
            ?.Header.GetString("EXTNAME")?.Trim();
        var suggested = string.IsNullOrEmpty(chip) || ViewModel.Hdus?.Count(h => h.HasImage) <= 1
            ? file
            : $"{file}-{chip}";

        var said = await Controls.MarkExportPrompt.RunAsync("fits", target, suggested);

        if (said is not null) ViewModel.StatusMessage = said;
    }

    // ── Asking for the menu ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Open the menu for the mark under a point, if there is one there. Says whether it did.
    ///
    /// The mark is picked out first: a menu acting on something that is not visibly chosen is how
    /// people delete the wrong thing.
    /// </summary>
    private bool TryShowMarkMenu(Point at)
    {
        if (MarkAt(at) is not { } id) return false;

        Marks.Select(id);
        Controls.MarkContextMenu.ShowFor(this, id, ImageCanvas, at.X, at.Y);
        return true;
    }

    /// <summary>
    /// The keyboard's way of asking — the Menu key, or Shift+F10.
    ///
    /// <para>Only the keyboard's. A right-click is answered in the pointer handler instead, because
    /// right-click on this canvas already means "place the crosshair" and that is worth keeping: the
    /// menu takes the press only when it lands on a mark. That handler marks the press handled, so a
    /// mouse context request never reaches here — and a keyboard one carries no position, which is
    /// exactly how the two are told apart.</para>
    ///
    /// <para>The canvas is a tab stop so that this can happen at all: a Grid cannot take focus by
    /// default, and an element that never holds focus never receives the Menu key. Without that it
    /// would be a handler that compiles, reads as keyboard support, and runs on no press anyone can
    /// make.</para>
    /// </summary>
    private void OnCanvasContextRequested(
        UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs args)
    {
        if (args.TryGetPosition(ImageCanvas, out _)) return;
        if (Marks.SelectedId is not { } id || Marks.ById(id) is not { } mark) return;


        // No pointer to anchor to, so the menu goes on the mark itself.
        var at = Surface.Project(mark.Anchor) is { } point
            ? new Point(point.X, point.Y)
            : new Point(ImageCanvas.ActualWidth / 2, ImageCanvas.ActualHeight / 2);

        Controls.MarkContextMenu.ShowFor(this, id, ImageCanvas, at.X, at.Y);
        args.Handled = true;
    }
}
