using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Views.FitsViewer;

/// <summary>
/// Dragging out the part of the image a figure is of.
///
/// Ctrl and drag. Not a mode you enter and leave, because the thing it competes with is panning, and a
/// modifier you hold is a decision that lasts exactly as long as the gesture — nobody arrives at the
/// image later wondering why it will not pan.
///
/// Releasing opens the export dialog on the region just drawn, which is the whole point: the gesture
/// and the figure are one action rather than a selection followed by a menu.
/// </summary>
public sealed partial class FitsViewerPage
{
    private bool _regionDragging;
    private Windows.Foundation.Point _regionStart;

    /// <summary>The rubber band's current corners, in canvas points.</summary>
    private (double X1, double Y1, double X2, double Y2) _regionBand;

    /// <summary>
    /// Whether this press starts a region drag. Asked before the marks and the pan, because Ctrl is an
    /// explicit statement about what the drag is for, and a modifier that loses to whatever else claims
    /// the press first is a modifier nobody can rely on.
    /// </summary>
    private bool TryBeginRegionDrag(Windows.Foundation.Point at)
    {
        if (ViewModel.ImageData is null) return false;

        var ctrl = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (!ctrl) return false;

        _regionDragging = true;
        _regionStart = at;
        _regionBand = (at.X, at.Y, at.X, at.Y);
        DrawRegionBand();
        return true;
    }

    private bool ContinueRegionDrag(Windows.Foundation.Point at)
    {
        if (!_regionDragging) return false;

        _regionBand = (_regionStart.X, _regionStart.Y, at.X, at.Y);
        DrawRegionBand();
        return true;
    }

    /// <summary>
    /// Finish the drag. A band too small to be meant is dropped rather than exported: a Ctrl-click that
    /// moved three pixels is a click, and answering it with a figure of nine pixels is not helpful.
    /// </summary>
    private bool EndRegionDrag()
    {
        if (!_regionDragging) return false;

        _regionDragging = false;
        RegionRect.Visibility = Visibility.Collapsed;

        var (x1, y1, x2, y2) = _regionBand;
        if (Math.Abs(x2 - x1) < MinimumRegionPixels || Math.Abs(y2 - y1) < MinimumRegionPixels) return true;

        var a = e_PointToImage(new Windows.Foundation.Point(x1, y1));
        var b = e_PointToImage(new Windows.Foundation.Point(x2, y2));
        var region = FitsRegion.FromCorners(a.X, a.Y, b.X, b.Y);

        // Dispatched so the press finishes being handled before a modal opens over the canvas.
        DispatcherQueue.TryEnqueue(() => _ = ShowExportDialogAsync(region));
        return true;
    }

    /// <summary>Below this the drag was a click that wobbled.</summary>
    private const double MinimumRegionPixels = 8;

    private void DrawRegionBand()
    {
        var (x1, y1, x2, y2) = _regionBand;

        Canvas.SetLeft(RegionRect, Math.Min(x1, x2));
        Canvas.SetTop(RegionRect, Math.Min(y1, y2));
        RegionRect.Width = Math.Abs(x2 - x1);
        RegionRect.Height = Math.Abs(y2 - y1);
        RegionRect.Visibility = Visibility.Visible;
    }

    /// <summary>Export the view on screen — the toolbar's Export button.</summary>
    public Task ExportCurrentViewAsync() => ShowExportDialogAsync(CurrentViewRegion());

    private async Task ShowExportDialogAsync(FitsRegion region)
    {
        if (ViewModel.ImageData is null || XamlRoot is null) return;
        if (region.ClampTo(ViewModel.ImageData.Width, ViewModel.ImageData.Height) is not { } area) return;

        var dialog = new FitsExportDialog { XamlRoot = XamlRoot };
        dialog.Initialize(this, area, ExportBaseName());
        await dialog.ShowAsync();
    }
}
