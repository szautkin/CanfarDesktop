namespace CanfarDesktop.Models.Fits;

/// <summary>
/// Immutable RA/Dec coordinate pair in degrees.
/// Record equality is value-based — PropertyChanged fires only when coords actually change.
/// </summary>
public sealed record WorldCoordinate(double Ra, double Dec)
{
    public string FormattedRa => WcsInfo.FormatRa(Ra);
    public string FormattedDec => WcsInfo.FormatDec(Dec);
    public string Display => $"RA {FormattedRa}  Dec {FormattedDec}";
}
