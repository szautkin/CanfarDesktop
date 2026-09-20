namespace CanfarDesktop.Models.Fits;

using System.Globalization;

/// <summary>
/// Parsed FITS header. Provides typed accessors over the raw card dictionary.
/// </summary>
public class FitsHeader
{
    private readonly Dictionary<string, FitsCard> _cards = new(StringComparer.Ordinal);
    private readonly List<FitsCard> _orderedCards = [];

    public IReadOnlyDictionary<string, FitsCard> Cards => _cards;
    public IReadOnlyList<FitsCard> OrderedCards => _orderedCards;

    public void Add(FitsCard card)
    {
        _cards[card.Keyword] = card;
        _orderedCards.Add(card);
    }

    public string? GetString(string key) =>
        _cards.TryGetValue(key, out var c) ? c.Value.Trim().Trim('\'').Trim() : null;

    public int GetInt(string key, int fallback = 0) =>
        _cards.TryGetValue(key, out var c) && int.TryParse(c.Value.Trim(), out var v) ? v : fallback;

    public double GetDouble(string key, double fallback = 0.0) =>
        _cards.TryGetValue(key, out var c) && double.TryParse(c.Value.Trim(),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public bool GetBool(string key, bool fallback = false) =>
        _cards.TryGetValue(key, out var c) ? c.Value.Trim() == "T" : fallback;

    public bool Contains(string key) => _cards.ContainsKey(key);

    // Standard image keywords
    public int BitPix => GetInt("BITPIX");
    public int NAxis => GetInt("NAXIS");
    public int NAxis1 => GetInt("NAXIS1");
    public int NAxis2 => GetInt("NAXIS2");
    public double BScale => GetDouble("BSCALE", 1.0);
    public double BZero => GetDouble("BZERO", 0.0);

    // ── The IMAGE, as opposed to what the HDU is stored as ──────────────────────────────────────

    /// <summary>
    /// Whether this HDU holds a tile-compressed image.
    ///
    /// The FITS tile-compression convention keeps the image inside a BINARY TABLE, so NAXISn describes
    /// the TABLE — for an fpack'd MegaCam frame, "8 bytes wide by 4644 rows" — and ZNAXISn describes
    /// the picture, 2112 by 4644. Asking for NAXIS1 and getting 8 is not a bug in the file; it is the
    /// right answer to the wrong question.
    /// </summary>
    public bool IsTileCompressed
        => GetString("ZIMAGE")?.StartsWith("T", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>How many axes the IMAGE has, whichever way the HDU stores it.</summary>
    public int ImageAxes => IsTileCompressed ? GetInt("ZNAXIS") : NAxis;

    /// <summary>
    /// The IMAGE's length along one 1-based axis, whichever way the HDU stores it.
    ///
    /// Every caller that means the picture should ask this rather than NAXISn, or it gets the table's
    /// shape on a compressed file. The HDU list did, and showed every extension of a .fits.fz as
    /// "8x4644".
    /// </summary>
    public int ImageAxis(int axis) => GetInt((IsTileCompressed ? "ZNAXIS" : "NAXIS") + axis);

}
