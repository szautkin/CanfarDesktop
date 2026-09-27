using Windows.ApplicationModel.DataTransfer;

namespace CanfarDesktop.Helpers;

/// <summary>
/// Text onto the clipboard: the one way the app puts it there, and says it did.
///
/// <para>This was the same three lines in six places — the FITS viewer, a mark's menu, Storage, Remote
/// Compute, the MCP settings and the connect wizard — each saying "copied" its own way or not at all,
/// and each able to throw when another application held the clipboard. Now a copy that works is said
/// in the status bar (<see cref="Copied"/>), wherever it was made, and one that fails is not claimed.</para>
/// </summary>
public static class ClipboardText
{
    /// <summary>Raised after a copy worked, with a short account of what was copied — for the status bar.</summary>
    public static event Action<string>? Copied;

    /// <summary>
    /// Put <paramref name="text"/> on the clipboard. False when the clipboard would not take it — held by
    /// another application for a moment, as Windows' clipboard sometimes is.
    /// </summary>
    /// <param name="what">How to say what was copied ("3 rows"); the text's first line when not given.</param>
    public static bool Copy(string text, string? what = null)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return false;
        }

        try { Copied?.Invoke(what ?? FirstLine(text)); }
        catch { /* saying so must not undo the copy */ }
        return true;
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', 2)[0].TrimEnd('\r');
        return line.Length <= 60 ? line : line[..57] + "…";
    }
}
