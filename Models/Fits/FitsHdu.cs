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
    public bool HasImage => Header.NAxis >= 2 && Header.NAxis1 > 0 && Header.NAxis2 > 0;
}
