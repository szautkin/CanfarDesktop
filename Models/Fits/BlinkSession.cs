using CanfarDesktop.ViewModels;

namespace CanfarDesktop.Models.Fits;

/// <summary>
/// State for a blink comparison session between two FITS tabs.
/// </summary>
public class BlinkSession
{
    public required FitsViewerTabItem TabA { get; init; }
    public required FitsViewerTabItem TabB { get; init; }
    public double ReferenceRa { get; set; }
    public double ReferenceDec { get; set; }
    public int IntervalMs { get; set; } = 500;
    public bool ShowingA { get; set; } = true;
    public bool IsPaused { get; set; }
}
