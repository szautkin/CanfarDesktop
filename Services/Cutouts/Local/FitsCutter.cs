using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Services.Cutouts.Local;

/// <summary>
/// Writes a <see cref="LocalCutPlan"/>: each HDU's header, then the rows of its box, read straight from
/// where they lie in the file and written as they are. A 1.6 GB tile is never loaded — only a row of
/// it at a time — and not a pixel is converted, so the cutout's pixels are the file's pixels.
/// </summary>
public static class FitsCutter
{
    /// <param name="file">The whole FITS file, seekable — as <see cref="FitsContainer.OpenFits"/> opens it.</param>
    /// <param name="progress">Bytes written so far, of the plan's total.</param>
    public static void Write(Stream file, LocalCutPlan plan, Stream destination,
                             IProgress<(long Done, long? Total)>? progress = null, CancellationToken ct = default)
    {
        var total = plan.Bytes;
        long done = 0;

        foreach (var hdu in plan.Hdus)
        {
            var header = hdu.Header.ToBytes();
            destination.Write(header);
            done += header.Length;

            if (hdu.Data is { } cut)
            {
                done += CopyBox(file, cut, destination, written =>
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report((done + written, total));
                });

                // Data is padded to a whole block with zeros (§3.3.2).
                var padding = FitsParser.AlignToBlock(cut.DataBytes) - cut.DataBytes;
                destination.Write(new byte[padding]);
                done += padding;
            }
            progress?.Report((done, total));
        }
    }

    /// <summary>
    /// The box's rows, in FITS order — the first axis fastest — for every combination of the other
    /// axes' ranges. Returns the bytes written; tells <paramref name="rowDone"/> after each row.
    /// </summary>
    private static long CopyBox(Stream file, ImageCut cut, Stream destination, Action<long> rowDone)
    {
        using var pixels = cut.Image.Encoding.Open(file, cut.Image.Hdu);
        var axes = cut.Axes;
        var lengths = Enumerable.Range(1, axes.Count).Select(n => (long)cut.Image.Header.GetInt($"NAXIS{n}")).ToArray();
        var row = new byte[axes[0].Length * pixels.BytesPerPixel];

        // One index per axis after the first, each running over its range; the second fastest.
        var at = axes.Skip(1).Select(a => a.Start).ToArray();
        long written = 0;
        while (true)
        {
            long rowIndex = 0, stride = 1;
            for (var k = 0; k < at.Length; k++)
            {
                rowIndex += at[k] * stride;
                stride *= lengths[k + 1];
            }

            pixels.ReadRow(rowIndex, axes[0].Start, row);
            destination.Write(row);
            written += row.Length;
            rowDone(written);

            var axis = 0;
            for (; axis < at.Length; axis++)
            {
                if (++at[axis] < axes[axis + 1].Start + axes[axis + 1].Length) break;
                at[axis] = axes[axis + 1].Start;
            }
            if (axis == at.Length) return written;
        }
    }
}
