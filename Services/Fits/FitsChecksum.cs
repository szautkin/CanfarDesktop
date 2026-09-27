using System.Buffers.Binary;
using System.Globalization;

namespace CanfarDesktop.Services.Fits;

/// <summary>
/// The FITS checksum convention (Seaman, Pence &amp; Rots, 2012; FITS 4.0 appendix J): DATASUM, the
/// 32-bit ones'-complement sum of an HDU's data, and CHECKSUM, sixteen characters chosen so that the
/// whole HDU — header and data — sums to negative zero. What fitsverify, astropy and cfitsio check to
/// say a file is as it was written.
///
/// <para>Summed a piece at a time, as a cutout is written: its data streams out row by row, and its
/// header, which comes first, is sealed once the data's sum is known.</para>
/// </summary>
public sealed class FitsChecksum
{
    /// <summary>What CHECKSUM holds while the sum it is part of is taken.</summary>
    public const string Zero = "0000000000000000";

    private ulong _sum;
    private uint _partial;
    private int _partialBytes;

    /// <summary>The sum so far — bytes left over past the last whole word counted as that word, zero-filled, as FITS pads with zeros.</summary>
    public uint Value
    {
        get
        {
            var sum = _sum;
            if (_partialBytes > 0) sum += _partial << (8 * (4 - _partialBytes));
            return Fold(sum);
        }
    }

    /// <summary>Add bytes that follow the ones already added.</summary>
    public void Add(ReadOnlySpan<byte> bytes)
    {
        var i = 0;
        while (_partialBytes > 0 && i < bytes.Length)
        {
            _partial = (_partial << 8) | bytes[i++];
            if (++_partialBytes == 4) { _sum += _partial; _partial = 0; _partialBytes = 0; }
        }
        for (; i + 4 <= bytes.Length; i += 4)
        {
            _sum += BinaryPrimitives.ReadUInt32BigEndian(bytes[i..]);
            if (_sum >= 1UL << 62) _sum = Fold(_sum); // never near overflow, however long the data
        }
        for (; i < bytes.Length; i++)
        {
            _partial = (_partial << 8) | bytes[i];
            _partialBytes++;
        }
    }

    /// <summary>The ones'-complement sum of whole words, from a starting sum.</summary>
    public static uint Sum(ReadOnlySpan<byte> bytes, uint start = 0)
    {
        var sum = new FitsChecksum();
        sum.Add(bytes);
        return Add(sum.Value, start);
    }

    /// <summary>Two ones'-complement sums added: the end-around carry kept.</summary>
    public static uint Add(uint a, uint b) => Fold((ulong)a + b);

    /// <summary>
    /// A sum as CHECKSUM's sixteen characters: each byte spread over four printable characters that add
    /// back to it, clear of ASCII punctuation, then rotated one place so the encoding sums as the value
    /// does. Given the complement of an HDU's sum, it makes the HDU sum to negative zero.
    /// </summary>
    public static string Encode(uint value)
    {
        ReadOnlySpan<byte> excluded = [0x3a, 0x3b, 0x3c, 0x3d, 0x3e, 0x3f, 0x40, 0x5b, 0x5c, 0x5d, 0x5e, 0x5f, 0x60];
        Span<char> ascii = stackalloc char[16];
        Span<int> ch = stackalloc int[4];
        for (var i = 0; i < 4; i++)
        {
            var b = (int)((value >> (8 * (3 - i))) & 0xFF);
            ch.Fill(b / 4 + '0');
            ch[0] += b % 4;
            for (var moved = true; moved;)
            {
                moved = false;
                foreach (var x in excluded)
                    for (var j = 0; j < 4; j += 2)
                        if (ch[j] == x || ch[j + 1] == x) { ch[j]++; ch[j + 1]--; moved = true; }
            }
            for (var j = 0; j < 4; j++) ascii[4 * j + i] = (char)ch[j];
        }

        Span<char> rotated = stackalloc char[16];
        for (var i = 0; i < 16; i++) rotated[i] = ascii[(i + 15) % 16];
        return new string(rotated);
    }

    /// <summary>Put CHECKSUM and DATASUM in a header, their values to be sealed once the data is summed.</summary>
    public static void Reserve(FitsHeaderCards cards)
    {
        cards.Remove("CHECKSUM");
        cards.Remove("DATASUM");
        cards.SetString("CHECKSUM", Zero, "HDU checksum");
        cards.SetString("DATASUM", "0", "data unit checksum");
    }

    /// <summary>
    /// The header as written, with its DATASUM the data's sum and its CHECKSUM what makes the whole HDU
    /// sum to negative zero. The header's size does not change from the reserved one, so it can be
    /// written over the one written before the data.
    /// </summary>
    public static byte[] Seal(FitsHeaderCards cards, uint dataSum)
    {
        cards.SetString("DATASUM", dataSum.ToString(CultureInfo.InvariantCulture));
        cards.SetString("CHECKSUM", Zero);
        var sum = Sum(cards.ToBytes(), dataSum);
        cards.SetString("CHECKSUM", Encode(~sum));
        return cards.ToBytes();
    }

    private static uint Fold(ulong sum)
    {
        while (sum >> 32 != 0) sum = (sum & 0xFFFFFFFF) + (sum >> 32);
        return (uint)sum;
    }
}
