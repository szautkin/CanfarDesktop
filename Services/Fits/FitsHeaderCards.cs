using System.Globalization;
using System.Text;
using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Services.Fits;

/// <summary>
/// A FITS header as its 80-character cards, for writing one: a card that is not changed goes out byte
/// for byte as it came in, and only a card that is set is formatted anew.
///
/// <para>Re-writing every card from its parsed value would lose what the parse does not keep — a long
/// string continued over CONTINUE cards, a HISTORY line's leading spaces, a number written to the
/// precision its pipeline chose — in a file whose whole point is that nothing in it changed but what
/// had to.</para>
///
/// <para>New values are written in FITS's fixed format (Pence et al. 2010, §4.2): a number or logical
/// right-justified to column 30, a string quoted from column 11 and at least eight characters long.</para>
/// </summary>
public sealed class FitsHeaderCards
{
    private const int CardSize = FitsParser.CardSize;
    private const int HistoryWidth = CardSize - 8;

    private readonly List<string> _cards;

    /// <param name="cards">The cards before END, 80 characters each — as <see cref="FitsParser.ReadHeader"/> gives them.</param>
    public FitsHeaderCards(IEnumerable<string> cards)
    {
        _cards = cards.Select(c => c.Length == CardSize ? c : c.PadRight(CardSize)[..CardSize]).ToList();
    }

    public IReadOnlyList<string> Cards => _cards;

    /// <summary>A card's keyword: its first eight characters, trimmed.</summary>
    public static string KeywordOf(string card) => (card.Length >= 8 ? card[..8] : card).TrimEnd();

    public bool Contains(string keyword) => IndexOf(keyword) >= 0;

    public int IndexOf(string keyword) => _cards.FindIndex(c => KeywordOf(c) == keyword);

    /// <summary>The header as the parser reads it, for looking values up.</summary>
    public FitsHeader Parse()
    {
        var header = new FitsHeader();
        foreach (var card in _cards)
        {
            var parsed = FitsParser.ParseCard(Encoding.ASCII.GetBytes(card));
            if (!string.IsNullOrWhiteSpace(parsed.Keyword)) header.Add(parsed);
        }
        return header;
    }

    public void Set(string keyword, long value, string? comment = null)
        => Set(keyword, value.ToString(CultureInfo.InvariantCulture), comment);

    public void Set(string keyword, double value, string? comment = null)
        => Set(keyword, FormatReal(value), comment);

    public void Set(string keyword, bool value, string? comment = null)
        => Set(keyword, value ? "T" : "F", comment);

    public void SetString(string keyword, string value, string? comment = null)
        => SetFormatted(keyword, FormatString(value), comment);

    /// <summary>
    /// Replace the card in place — keeping its comment unless given a new one — or, when there is none,
    /// add it at the end. <paramref name="value"/> is a number or logical as FITS writes it.
    /// </summary>
    public void Set(string keyword, string value, string? comment = null)
        => SetFormatted(keyword, value.PadLeft(20), comment);

    public void Remove(string keyword) => _cards.RemoveAll(c => KeywordOf(c) == keyword);

    public void RemoveWhere(Func<string, bool> keyword) => _cards.RemoveAll(c => keyword(KeywordOf(c)));

    /// <summary>Add a card at the end, as it is.</summary>
    public void Add(string card) => _cards.Add(card.PadRight(CardSize)[..CardSize]);

    /// <summary>HISTORY cards at the end, the text wrapped at word boundaries to fit.</summary>
    public void AddHistory(string text)
    {
        foreach (var line in Wrap(Ascii(text), HistoryWidth))
            _cards.Add(("HISTORY " + line).PadRight(CardSize));
    }

    /// <summary>The header as written: its cards, END, and spaces to a whole block.</summary>
    public byte[] ToBytes()
    {
        var text = new StringBuilder(string.Concat(_cards)).Append("END".PadRight(CardSize));
        var length = (int)FitsParser.AlignToBlock(text.Length);
        return Encoding.ASCII.GetBytes(text.ToString().PadRight(length));
    }

    /// <summary>One card: keyword, "= ", the value as given, and " / comment" when there is room.</summary>
    public static string FormatCard(string keyword, string formattedValue, string? comment)
    {
        var card = $"{keyword,-8}= {formattedValue}";
        if (!string.IsNullOrEmpty(comment)) card += " / " + comment;
        card = Ascii(card);
        return card.Length > CardSize ? card[..CardSize] : card.PadRight(CardSize);
    }

    /// <summary>A header is printable ASCII only (§4.1.1): anything else — an accented file name — becomes '?'.</summary>
    public static string Ascii(string text)
        => string.Create(text.Length, text, (span, source) =>
        {
            for (var i = 0; i < source.Length; i++) span[i] = source[i] is >= ' ' and <= '~' ? source[i] : '?';
        });

    /// <summary>A real number as FITS reads it back exactly: round-trip digits, with a point or an exponent.</summary>
    public static string FormatReal(double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('E') || !double.IsFinite(value) ? text : text + ".0";
    }

    /// <summary>A string value: quoted, quotes doubled, at least eight characters inside.</summary>
    public static string FormatString(string value) => $"'{value.Replace("'", "''").PadRight(8)}'";

    private void SetFormatted(string keyword, string formattedValue, string? comment)
    {
        var at = IndexOf(keyword);
        if (at < 0)
        {
            _cards.Add(FormatCard(keyword, formattedValue, comment));
            return;
        }

        comment ??= FitsParser.ParseCard(Encoding.ASCII.GetBytes(_cards[at])).Comment;
        _cards[at] = FormatCard(keyword, formattedValue, comment);
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        var line = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            for (var rest = word; rest.Length > 0;)
            {
                var room = width - (line.Length == 0 ? 0 : line.Length + 1);
                if (rest.Length <= room)
                {
                    if (line.Length > 0) line.Append(' ');
                    line.Append(rest);
                    rest = string.Empty;
                }
                else if (line.Length > 0)
                {
                    yield return line.ToString();
                    line.Clear();
                }
                else
                {
                    // A word longer than a card — a path — is broken where it must be.
                    yield return rest[..width];
                    rest = rest[width..];
                }
            }
        }
        if (line.Length > 0) yield return line.ToString();
    }
}
