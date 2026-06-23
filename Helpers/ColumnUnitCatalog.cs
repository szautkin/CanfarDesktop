namespace CanfarDesktop.Helpers;

/// <summary>One selectable unit in a column's unit menu (stable id + user-facing label).</summary>
public record UnitChoice(string Id, string Label);

/// <summary>
/// Which result columns have a switchable display unit, the ordered choices, and the default.
/// 1-to-1 with the macOS <c>CellFormatterRegistry.sets</c>. Keys are the cleaned column ids
/// (<see cref="CellFormatter.CleanKey"/>).
/// </summary>
public static class ColumnUnitCatalog
{
    private static readonly Dictionary<string, (IReadOnlyList<UnitChoice> Choices, string Default)> Sets = Build();

    public static bool HasMenu(string columnKey) => Sets.ContainsKey(CellFormatter.CleanKey(columnKey));

    public static IReadOnlyList<UnitChoice> AvailableUnits(string columnKey)
        => Sets.TryGetValue(CellFormatter.CleanKey(columnKey), out var s) ? s.Choices : Array.Empty<UnitChoice>();

    public static string? DefaultUnitId(string columnKey)
        => Sets.TryGetValue(CellFormatter.CleanKey(columnKey), out var s) ? s.Default : null;

    private static Dictionary<string, (IReadOnlyList<UnitChoice>, string)> Build()
    {
        var spectral = (IReadOnlyList<UnitChoice>)UnitConverter.SpectralUnitChoices
            .Select(c => new UnitChoice(c.Id, c.Label)).ToList();

        UnitChoice[] coord(string sexId, string sexLabel) =>
            new[] { new UnitChoice(sexId, sexLabel), new UnitChoice("degrees", "Degrees") };

        return new Dictionary<string, (IReadOnlyList<UnitChoice>, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["ra(j20000)"] = (coord("hms", "H:M:S"), "hms"),
            ["dec(j20000)"] = (coord("dms", "D:M:S"), "dms"),
            ["minwavelength"] = (spectral, "m"),
            ["maxwavelength"] = (spectral, "m"),
            ["restframeenergy"] = (spectral, "m"),
            ["inttime"] = (new UnitChoice[]
            {
                new("seconds", "Seconds"), new("minutes", "Minutes"), new("hours", "Hours"), new("days", "Days"),
            }, "seconds"),
            ["pixelscale"] = (new UnitChoice[]
            {
                new("milliarcseconds", "Milliarcseconds"), new("arcseconds", "Arcseconds"),
                new("arcminutes", "Arcminutes"), new("degrees", "Degrees"),
            }, "arcseconds"),
            // image quality: same as pixel scale but NO degrees option (macOS imageQualitySet)
            ["positionresolution"] = (new UnitChoice[]
            {
                new("milliarcseconds", "Milliarcseconds"), new("arcseconds", "Arcseconds"), new("arcminutes", "Arcminutes"),
            }, "arcseconds"),
            ["fieldofview"] = (new UnitChoice[]
            {
                new("sq_arcsec", "Sq. arcsec"), new("sq_arcmin", "Sq. arcmin"), new("sq_deg", "Sq. deg"),
            }, "sq_deg"),
            ["startdate"] = (new UnitChoice[] { new("calendar", "Calendar"), new("mjd", "MJD") }, "calendar"),
            ["enddate"] = (new UnitChoice[] { new("calendar", "Calendar"), new("mjd", "MJD") }, "calendar"),
        };
    }
}
