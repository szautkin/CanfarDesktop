using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Services.Fits;

/// <summary>
/// One header-and-data unit as it lies in the file: its header, parsed and as the file wrote it, and
/// where its data starts and how long it is.
/// </summary>
/// <param name="Index">0 for the primary HDU, then each extension in turn.</param>
/// <param name="RawCards">Every card before END, 80 characters each, exactly as in the file.</param>
/// <param name="DataBytes">The data's length before the padding to a whole block.</param>
public sealed record FitsHduLayout(int Index, FitsHeader Header, IReadOnlyList<string> RawCards, long DataStart, long DataBytes)
{
    /// <summary>"[SCI,1]", "[2]" — how the FITS world names an extension; "[0]" for the primary.</summary>
    public string Label
    {
        get
        {
            var name = Header.GetString("EXTNAME");
            if (string.IsNullOrWhiteSpace(name)) return $"[{Index}]";
            return Header.Contains("EXTVER") ? $"[{name},{Header.GetInt("EXTVER")}]" : $"[{name}]";
        }
    }
}

/// <summary>
/// Where every HDU of a FITS file lies, read from its headers alone — no pixel is read. What a cut
/// needs to reach straight for the rows it copies, and nothing more.
///
/// <para>Uses <see cref="FitsParser"/>'s own header reading and data-size rule, so the two cannot
/// disagree about where one HDU ends and the next begins.</para>
/// </summary>
public static class FitsLayout
{
    /// <summary>The HDUs of a seekable FITS stream, from its start.</summary>
    public static IReadOnlyList<FitsHduLayout> Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek) throw new ArgumentException("A FITS layout is read from a stream that can seek.", nameof(stream));

        var hdus = new List<FitsHduLayout>();
        stream.Position = 0;
        while (stream.Position < stream.Length)
        {
            var raw = new List<string>();
            var header = FitsParser.ReadHeader(stream, raw);
            if (header is null) break;

            var dataStart = stream.Position;
            var dataBytes = FitsParser.DataSize(header);
            hdus.Add(new FitsHduLayout(hdus.Count, header, raw, dataStart, dataBytes));
            stream.Position = dataStart + FitsParser.AlignToBlock(dataBytes);
        }
        return hdus;
    }
}
