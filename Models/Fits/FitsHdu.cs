namespace CanfarDesktop.Models.Fits;

/// <summary>
/// One Header Data Unit from a FITS file.
/// </summary>
public class FitsHdu
{
    public required FitsHeader Header { get; init; }
    public FitsImageData? ImageData { get; init; }
    public int Index { get; init; }
    public string Name => Header.GetString("EXTNAME") ?? $"HDU {Index}";
    public bool HasImage
        => Header.ImageAxes >= 2 && Header.ImageAxis(1) > 0 && Header.ImageAxis(2) > 0;
}
