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
        => new(CanLocateOnSky: SkyOf(MarkById(id)) is not null, CanExportFigure: true);

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

        if (MarkById(id) is not { } mark) return;

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

            case MarkCommand.Delete:
                Marks.Delete(id);
                break;
        }
    }

    private Annotation? MarkById(string id) => Marks.Marks.FirstOrDefault(m => m.Id == id);

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
    /// The mark's position on the clipboard, in both the forms this app prints.
    ///
    /// Sexagesimal AND degrees, because the first is what goes into a message to a colleague and the
    /// second is what goes into a script, and which one is wanted is not knowable from here.
    /// </summary>
    private void CopyMarkCoordinates(Annotation mark)
    {
        var text = SkyOf(mark) is { } sky
            ? $"{WcsInfo.FormatRa(sky.Ra)} {WcsInfo.FormatDec(sky.Dec)}  ({sky.Ra:0.######}, {sky.Dec:0.######})"
            : $"{mark.Anchor.X:0.##}, {mark.Anchor.Y:0.##} px";

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

        var said = await Controls.MarkExportPrompt.RunAsync(
            "fits", target, System.IO.Path.GetFileNameWithoutExtension(target));

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
        ShowMarkMenuFor(id, at);
        return true;
    }

    /// <summary>One way to put the menu on screen, so the pointer and the keyboard cannot differ.</summary>
    private void ShowMarkMenuFor(string id, Point at)
    {
        var menu = Controls.MarkContextMenu.Build(
            MarkCommands.For(CommandContextFor(id)),
            command => InvokeMarkCommand(command, id));

        Controls.MarkContextMenu.ShowAt(menu, ImageCanvas, at.X, at.Y);
    }

    /// <summary>
    /// The keyboard's way of asking — the Menu key, or Shift+F10.
    ///
    /// <para>Only the keyboard's. A right-click is answered in the pointer handler instead, because
    /// right-click on this canvas already means "place the crosshair" and that is worth keeping: the
    /// menu takes the press only when it lands on a mark. That handler marks the press handled, so a
    /// mouse context request never reaches here — and a keyboard one carries no position, which is
    /// exactly how the two are told apart.</para>
    /// </summary>
    private void OnCanvasContextRequested(
        UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs args)
    {
        if (args.TryGetPosition(ImageCanvas, out _)) return;
        if (Marks.SelectedId is not { } id || MarkById(id) is not { } mark) return;

        // No pointer to anchor to, so the menu goes on the mark itself.
        var at = Surface.Project(mark.Anchor) is { } point
            ? new Point(point.X, point.Y)
            : new Point(ImageCanvas.ActualWidth / 2, ImageCanvas.ActualHeight / 2);

        ShowMarkMenuFor(id, at);
        args.Handled = true;
    }
}
