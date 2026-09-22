using Microsoft.UI.Xaml;
using Windows.Foundation;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Views.CubeViewer;

/// <summary>
/// What a cube means by a mark's menu.
///
/// <para>The same list as the FITS viewer, answered differently, which is the whole reason the list
/// is kept apart from what the entries do. A cube mark lives at a voxel, so there is no sky to search
/// and no flat figure to frame around it — those two entries are greyed and absent respectively, and
/// the other five mean here what they mean there.</para>
///
/// <para>"Centre" is the one that reads differently and is the same idea: bringing a cube mark into
/// view means going to its channel, because a mark on channel 200 is invisible on channel 40 no
/// matter where the camera points.</para>
/// </summary>
public sealed partial class CubeViewerPage : IMarkCommandHost
{
    /// <summary>
    /// A voxel has no sky position and a cube has no flat figure to frame, so both of those are off.
    ///
    /// Search stays in the menu greyed rather than hidden: it tells somebody who has used the FITS
    /// viewer that the command exists and why it cannot act here, which hiding it would not.
    /// </summary>
    public MarkCommands.Context CommandContextFor(string id)
        => new(CanLocateOnSky: false, CanExportFigure: false);

    /// <summary>What each entry does here. Every one of them is also an MCP tool.</summary>
    public void InvokeMarkCommand(MarkCommand command, string id)
    {
        // About the file rather than one mark — the panel's button raises it with no mark at all — so
        // it is answered before anything looks a mark up.
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
                // Selecting moves the channel with it, through the one Reveal both paths already use.
                Marks.Select(id);
                Reveal(mark);
                break;

            case MarkCommand.Delete:
                Marks.Delete(id);
                break;

            // SearchHere and ExportFigure never reach a cube's menu, so there is nothing to answer.
        }
    }

    /// <summary>The voxel, on the clipboard, through the format both viewers share.</summary>
    private void CopyMarkCoordinates(Annotation mark)
    {
        var text = MarkClipboard.Voxel(mark.Anchor.X, mark.Anchor.Y, mark.Anchor.Z);

        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            ShowStatus(Loc.T("Marks_CopiedCoordinates"));
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
            "cube", target, System.IO.Path.GetFileNameWithoutExtension(target));

        if (said is not null) ShowStatus(said);
    }

    /// <summary>
    /// A right-click or the Menu key over whichever view is showing.
    ///
    /// <para>One handler for the mouse AND the keyboard, and for the slice AND the volume. Neither
    /// press handler takes the right button — both return unless the left one is down — so the
    /// context request arrives here untouched, which the FITS canvas cannot say because right-click
    /// already means something there.</para>
    ///
    /// <para>With a position it is a click, and the mark is whichever one is under it. Without one
    /// the keyboard asked, and the mark is the one already picked out — which both canvases can
    /// receive because both are tab stops; neither could take focus on its own, and an element that
    /// never holds focus never sees the Menu key.</para>
    /// </summary>
    private void OnMarkContextRequested(
        UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs args)
    {
        var host = sender as FrameworkElement ?? SliceViewport;
        var surface = Surface;

        string? id;
        Point at;

        if (args.TryGetPosition(host, out var pointer))
        {
            id = AnnotationGeometry.AnnotationAt(Marks.Marks, surface, pointer.X, pointer.Y);
            at = pointer;
        }
        else
        {
            id = Marks.SelectedId;

            // No pointer to anchor to, so the menu goes on the mark itself.
            at = Marks.ById(id) is { } chosen && surface.Project(chosen.Anchor) is { } point
                ? new Point(point.X, point.Y)
                : new Point(host.ActualWidth / 2, host.ActualHeight / 2);
        }

        if (id is null) return;

        Marks.Select(id);
        Controls.MarkContextMenu.ShowFor(this, id, host, at.X, at.Y);
        args.Handled = true;
    }
}
