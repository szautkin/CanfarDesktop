namespace CanfarDesktop.Helpers;

/// <summary>
/// Splitting a download's filename into the two parts a <c>FileSavePicker</c> wants.
///
/// <para>The picker takes a <c>SuggestedFileName</c> and a set of <c>FileTypeChoices</c>, and appends
/// the selected choice's extension to the name. Handing it a COMPLETE filename and then offering only
/// <c>.fits</c> therefore doubled the extension on everything that was not literally a <c>.fits</c>:
/// an fpack artifact came back as <c>x.fits.fz.fits</c>.</para>
///
/// <para>So the caller needs the stem and the file's own extension separately, and the guarantee that
/// putting them back together gives exactly the name it started with.</para>
/// </summary>
public static class SaveFileName
{
    /// <summary>What a download with no usable extension is assumed to be.</summary>
    public const string DefaultExtension = ".fits";

    /// <summary>
    /// The stem and extension to hand the picker.
    ///
    /// <para>A name with no extension gets <see cref="DefaultExtension"/> — these are FITS downloads,
    /// and a publisher id resolves to a bare identifier like <c>1100689o</c>.</para>
    ///
    /// <para>Only the LAST extension is split off, so <c>x.fits.fz</c> is the stem <c>x.fits</c> plus
    /// <c>.fz</c>. That is what the picker needs to reassemble the name unchanged; treating
    /// <c>.fits.fz</c> as one extension would make the stem <c>x</c> and offer a file type the picker
    /// cannot round-trip.</para>
    /// </summary>
    public static (string Stem, string Extension) ForPicker(string? fileName)
    {
        var name = (fileName ?? string.Empty).Trim();
        if (name.Length == 0) return (string.Empty, DefaultExtension);

        var ext = Path.GetExtension(name);

        // A trailing dot ("x.") is an extension of "." — not something to offer as a file type.
        if (string.IsNullOrEmpty(ext) || ext == ".")
            return (name.TrimEnd('.'), DefaultExtension);

        return (Path.GetFileNameWithoutExtension(name), ext);
    }

    /// <summary>
    /// The name those two parts produce — what the picker will actually suggest.
    /// Exists so the round trip can be asserted rather than assumed.
    /// </summary>
    public static string Combine((string Stem, string Extension) parts)
        => parts.Stem + parts.Extension;
}
