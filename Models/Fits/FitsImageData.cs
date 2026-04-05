namespace CanfarDesktop.Models.Fits;

/// <summary>
/// Raw image pixel data extracted from a FITS HDU.
/// Pixels are physical values (BZERO + BSCALE * stored).
/// </summary>
public class FitsImageData
{
    public required float[] Pixels { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public float Min { get; init; }
    public float Max { get; init; }
    public WcsInfo? Wcs { get; init; }
}
