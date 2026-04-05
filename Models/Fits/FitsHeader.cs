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
}
