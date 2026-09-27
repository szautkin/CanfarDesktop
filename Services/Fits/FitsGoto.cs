using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Services.Fits;

/// <summary>Why a sky position is not on an image.</summary>
public enum FitsGotoMiss
{
    /// <summary>It is on the image.</summary>
    None,

    /// <summary>The image has no coordinates to place anything with.</summary>
    NoWcs,

    /// <summary>The image's coordinates cannot reach it — the far side of the sky from its projection.</summary>
    CannotPlace,

    /// <summary>It falls at a pixel, just not one of this image's.</summary>
    OffImage,
}

/// <summary>
/// Where a sky position lands on an image: its display pixel, whether that is on the image, and —
/// when it is not — why: as a <see cref="FitsGotoMiss"/> for the status bar to put in the person's
/// language, and in words an agent can pass on.
/// </summary>
public sealed record FitsGotoTarget(bool OnImage, double X, double Y, string? Why, FitsGotoMiss Miss = FitsGotoMiss.None);

/// <summary>
/// Go To's one decision, shared by the viewer's Go To and <c>fits_goto_coordinate</c>.
///
/// <para>They used to decide separately. The viewer refused a position off the image with a status
/// message, while the tool asked only whether the WCS could place it at all and reported
/// <c>moved: true</c> — the crosshair left where it was.</para>
/// </summary>
public static class FitsGoto
{
    public static FitsGotoTarget Resolve(WcsInfo? wcs, int width, int height, double ra, double dec)
    {
        if (wcs is not { IsValid: true })
            return new(false, double.NaN, double.NaN, "the image has no usable WCS", FitsGotoMiss.NoWcs);

        if (PixelConvention.DisplayOfSky(wcs, height, ra, dec) is not { } p)
            return new(false, double.NaN, double.NaN, "this image's WCS cannot place that position", FitsGotoMiss.CannotPlace);

        return PixelConvention.IsOnImage(p.X, p.Y, width, height)
            ? new(true, p.X, p.Y, null)
            : new(false, p.X, p.Y, $"outside the image: it falls at pixel ({p.X:0}, {p.Y:0}) of a {width}×{height} image", FitsGotoMiss.OffImage);
    }
}
