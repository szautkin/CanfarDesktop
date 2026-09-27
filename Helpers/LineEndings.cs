namespace CanfarDesktop.Helpers;

/// <summary>
/// Text with plain <c>\n</c> line breaks.
///
/// <para>A WinUI TextBox keeps its lines apart with a bare <c>\r</c>, and text from Windows arrives
/// with <c>\r\n</c>. Text leaving the app — code for a Linux interpreter, a query an agent reads back —
/// goes with <c>\n</c>, whichever it came in with.</para>
/// </summary>
public static class LineEndings
{
    public static string ToLf(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
