namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// Just enough of cfitsio's fits_rcomp_short to make fpack files for tests: a tile's first pixel as a
/// big-endian int16, then blocks of folded 16-bit differences, each block Rice-coded at the split its
/// mean suggests (or marked all-zero). The app only ever decodes; this is how a test gets something to decode.
/// </summary>
internal static class RiceEncoder
{
    public static byte[] Encode(short[] pixels, int blockSize = 32)
    {
        var bits = new BitWriter();
        bits.Write((ushort)pixels[0], 16);
        var last = pixels[0];
        for (var start = 0; start < pixels.Length; start += blockSize)
        {
            var n = Math.Min(blockSize, pixels.Length - start);
            var folded = new int[n];
            long sum = 0;
            for (var j = 0; j < n; j++)
            {
                var d = unchecked((short)(pixels[start + j] - last)); // differences wrap at 16 bits, as cfitsio's
                last = pixels[start + j];
                folded[j] = d < 0 ? ~(d << 1) : d << 1;
                sum += folded[j];
            }

            if (sum == 0) { bits.Write(0, 4); continue; }  // every difference zero
            var fs = 0;
            while (fs < 13 && (1L << (fs + 1)) <= sum / n) fs++;
            bits.Write(fs + 1, 4);
            foreach (var v in folded)
            {
                for (var q = v >> fs; q > 0; q--) bits.Write(0, 1);
                bits.Write(1, 1);
                if (fs > 0) bits.Write(v & ((1 << fs) - 1), fs);
            }
        }
        return bits.ToArray();
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        private int _current;
        private int _used;

        public void Write(int value, int count)
        {
            for (var i = count - 1; i >= 0; i--)
            {
                _current = (_current << 1) | ((value >> i) & 1);
                if (++_used == 8) { _bytes.Add((byte)_current); _current = 0; _used = 0; }
            }
        }

        public byte[] ToArray()
        {
            if (_used > 0) { _bytes.Add((byte)(_current << (8 - _used))); _current = 0; _used = 0; }
            return [.. _bytes];
        }
    }
}
