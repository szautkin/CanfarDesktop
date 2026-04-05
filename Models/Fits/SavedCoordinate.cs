namespace CanfarDesktop.Models.Fits;

public class SavedCoordinate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "";
    public double Ra { get; set; }
    public double Dec { get; set; }
    public string? SourceFile { get; set; }
    public DateTime SavedAt { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string FormattedCoords => $"RA {WcsInfo.FormatRa(Ra)}  Dec {WcsInfo.FormatDec(Dec)}";
}
