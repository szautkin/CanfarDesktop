using System.Text;
using CanfarDesktop.Models.Notebook;

namespace CanfarDesktop.Helpers.Notebook;

/// <summary>A file this app can open as a notebook.</summary>
public enum NotebookFormat
{
    /// <summary>nbformat JSON — the native format.</summary>
    Ipynb,

    /// <summary>A Python script with <c># %%</c> cell markers (jupytext "percent").</summary>
    PercentPython,

    /// <summary>Markdown, where fenced Python blocks are code cells.</summary>
    Markdown,

    /// <summary>
    /// Plain text: one cell, written back as it came.
    ///
    /// Not a notebook format anywhere, and it does not pretend to be. It is here because observing
    /// notes, instrument logs and READMEs live next to the data as <c>.txt</c>, and being able to open
    /// one and add a code cell under it is the whole point of a notebook.
    /// </summary>
    PlainText,

    /// <summary>
    /// A file this app will not open as a notebook. Named rather than left to fail as malformed JSON,
    /// so the refusal can say what the file is and what to do with it instead.
    /// </summary>
    Unsupported,
}

/// <summary>
/// The files this app opens as a notebook, and how they map to cells.
///
/// <c>.ipynb</c> is the native format and always was. <c>.py</c> and <c>.md</c> could be opened too —
/// and each arrived as ONE cell holding the whole file, so a 500-line script was a single block you
/// could only run all at once. That is not what any other tool does, and it is not what the file means.
///
/// Two problems, and the second is worse than the first.
///
/// <b>Cells were never found.</b> Every editor that runs Python interactively — Jupyter via jupytext,
/// VS Code, Spyder, PyCharm — splits a <c>.py</c> on <c># %%</c> marker lines. The convention is
/// twenty years old between them, and it is what a scientist's script already contains.
///
/// <b>Saving destroyed the file.</b> The save path writes nbformat JSON to whatever path it is handed.
/// Open <c>analysis.py</c>, press Ctrl+S, and the script is replaced by a JSON document. Same for a
/// <c>.md</c>. The file could be one you opened to read.
///
/// So format is a value, decided once from the path, and each format can both read cells and write
/// them back. Everything here is a pure function over strings: no filesystem, no XAML, all testable.
/// </summary>
public static class NotebookFormats
{
    /// <summary>
    /// The format of the file at <paramref name="path"/>, from its extension.
    ///
    /// Anything unrecognised is <see cref="NotebookFormat.Ipynb"/>: that is what the loader has always
    /// assumed, and a file with no extension that someone saved from here is far more likely to be a
    /// notebook than a script.
    /// </summary>
    public static NotebookFormat ForPath(string? path)
        => (Path.GetExtension(path ?? string.Empty) ?? string.Empty).ToLowerInvariant() switch
        {
            ".py" => NotebookFormat.PercentPython,
            ".md" or ".markdown" => NotebookFormat.Markdown,
            ".txt" or ".text" or ".log" => NotebookFormat.PlainText,

            // Export formats and documents, not notebooks. `.html` in particular is what a notebook is
            // converted TO — the conversion is one way, and no tool reads it back. Refusing by name lets
            // the message say so; falling through to Ipynb produced "invalid notebook JSON", which
            // describes the parser's disappointment rather than the user's problem.
            ".html" or ".htm" or ".pdf" or ".docx" or ".odt" or ".rtf" or ".tex" => NotebookFormat.Unsupported,

            _ => NotebookFormat.Ipynb,
        };

    /// <summary>
    /// The word <c>list_notebooks</c> reports for this format.
    ///
    /// Derived from the format rather than from a second reading of the extension. On the Linux build
    /// the MCP layer had its own copy of that mapping and it drifted within a day: <c>.txt</c> became
    /// openable and the copy still answered "other", so the tool reported a file as unsupported while
    /// the editor opened it happily.
    /// </summary>
    public static string Kind(this NotebookFormat format) => format switch
    {
        NotebookFormat.Ipynb => "notebook",
        NotebookFormat.PercentPython => "python",
        NotebookFormat.Markdown => "markdown",
        NotebookFormat.PlainText => "text",
        _ => "other",
    };

    /// <summary>Why a file cannot be opened, in words that say what to do instead.</summary>
    public static string UnsupportedReason(string? path)
        => $"'{Path.GetFileName(path ?? string.Empty)}' is a document, not a notebook. This editor opens "
         + ".ipynb notebooks, .py scripts (split on # %%), .md documents (fenced python becomes code "
         + "cells) and .txt notes.";

    /// <summary>Every openable extension, for a file picker — one list, so the dialog cannot offer less than the loader takes.</summary>
    public static IReadOnlyList<string> OpenableExtensions => [".ipynb", ".py", ".md", ".markdown", ".txt", ".text", ".log"];

    // ── Percent-format Python ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether a line opens a new percent cell, and what kind it declares. Null when it is not a marker.
    ///
    /// <c># %%</c>, <c>#%%</c>, <c># %% [markdown]</c>, <c># %% [raw]</c>, and the form carrying a title
    /// — <c># %% Load the data</c> — which jupytext writes and which people write by hand more often
    /// than the bare marker.
    /// </summary>
    internal static string? PercentMarker(string line)
    {
        var trimmed = line.TrimStart();

        var rest = trimmed.StartsWith("# %%", StringComparison.Ordinal) ? trimmed[4..]
                 : trimmed.StartsWith("#%%", StringComparison.Ordinal) ? trimmed[3..]
                 : null;

        if (rest is null) return null;

        // `# %%%` is not a marker; the character after must end it or separate it.
        if (rest.StartsWith('%')) return null;

        var lowered = rest.ToLowerInvariant();
        if (lowered.Contains("[markdown]")) return "markdown";
        if (lowered.Contains("[raw]")) return "raw";
        return "code";
    }

    /// <summary>
    /// Split a percent-format Python file into cells. A file with no markers is one code cell — both the
    /// old behaviour and the right answer for an ordinary script.
    /// </summary>
    public static List<NotebookCell> SplitPercent(string source)
    {
        var cells = new List<NotebookCell>();
        var buffer = new List<string>();
        var kind = "code";

        void Push()
        {
            var text = string.Join("\n", buffer);
            buffer.Clear();
            if (string.IsNullOrWhiteSpace(text)) return;

            var body = text.Trim('\n');
            cells.Add(Cell(kind, kind == "markdown" ? Uncomment(body) : body));
        }

        foreach (var line in Lines(source))
        {
            if (PercentMarker(line) is { } next)
            {
                Push();
                kind = next;
                continue;
            }
            buffer.Add(line);
        }
        Push();

        if (cells.Count == 0) cells.Add(Cell("code", string.Empty));
        return cells;
    }

    /// <summary>Write a document back as a percent-format Python file.</summary>
    public static string ToPercent(NotebookDocument document)
    {
        var text = new StringBuilder();

        foreach (var cell in document.Cells)
        {
            var body = string.Concat(cell.Source);
            switch (cell.CellType)
            {
                case "markdown":
                    text.Append("# %% [markdown]\n").Append(Comment(body));
                    break;
                case "raw":
                    text.Append("# %% [raw]\n").Append(Comment(body));
                    break;
                default:
                    text.Append("# %%\n").Append(body);
                    break;
            }
            text.Append("\n\n");
        }

        return EndWithOneNewline(text.ToString());
    }

    /// <summary>Strip the comment prefix jupytext puts on every line of a markdown cell.</summary>
    private static string Uncomment(string text)
        => string.Join("\n", Lines(text).Select(l =>
            l.StartsWith("# ", StringComparison.Ordinal) ? l[2..]
            : l.StartsWith('#') ? l[1..]
            : l));

    /// <summary>Comment out a markdown cell's text so the file stays valid Python.</summary>
    private static string Comment(string text)
        => string.Join("\n", Lines(text).Select(l => l.Length == 0 ? "#" : "# " + l));

    // ── Markdown ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The language tags on a fenced block that mean "this is a code cell".
    ///
    /// The kernel is Python, so only Python blocks become runnable cells. A shell or JSON block in a
    /// document is illustration, and turning it into a code cell would offer to run text that was never
    /// meant to be run.
    /// </summary>
    private static readonly string[] CodeFenceLanguages = ["python", "python3", "py", "ipython", "ipython3"];

    /// <summary>Split a Markdown file into markdown cells and fenced Python code cells.</summary>
    public static List<NotebookCell> SplitMarkdown(string source)
    {
        var cells = new List<NotebookCell>();
        var prose = new List<string>();
        var code = new List<string>();
        var inCode = false;

        void Flush(string kind, List<string> buffer)
        {
            var text = string.Join("\n", buffer);
            buffer.Clear();
            if (string.IsNullOrWhiteSpace(text)) return;

            cells.Add(Cell(kind, text.Trim('\n')));
        }

        foreach (var line in Lines(source))
        {
            var trimmed = line.TrimStart();

            if (!inCode)
            {
                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    var info = trimmed[3..].Trim().ToLowerInvariant();
                    var language = info.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

                    if (CodeFenceLanguages.Contains(language))
                    {
                        Flush("markdown", prose);
                        inCode = true;
                        continue;
                    }
                }
                prose.Add(line);
            }
            else
            {
                if (trimmed.TrimEnd() == "```")
                {
                    Flush("code", code);
                    inCode = false;
                    continue;
                }
                code.Add(line);
            }
        }

        // An unterminated fence: keep the text rather than dropping the tail.
        if (inCode) Flush("code", code);
        Flush("markdown", prose);

        if (cells.Count == 0) cells.Add(Cell("markdown", string.Empty));
        return cells;
    }

    /// <summary>Write a document back as Markdown, code cells as fenced Python blocks.</summary>
    public static string ToMarkdown(NotebookDocument document)
    {
        var text = new StringBuilder();

        foreach (var cell in document.Cells)
        {
            var body = string.Concat(cell.Source);
            if (string.IsNullOrWhiteSpace(body)) continue;

            if (cell.CellType == "code")
                text.Append("```python\n").Append(body.TrimEnd()).Append("\n```\n\n");
            else
                text.Append(body.TrimEnd()).Append("\n\n");
        }

        return EndWithOneNewline(text.ToString());
    }

    // ── Plain text ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Read a plain-text file as one markdown cell.
    ///
    /// Deliberately no parsing. A <c>.txt</c> has no cell convention to find, and inventing one —
    /// splitting on blank lines, say — would take a file someone wrote as prose and cut it into pieces
    /// they did not ask for.
    /// </summary>
    public static List<NotebookCell> SplitPlainText(string source)
        => [Cell("markdown", Normalise(source).Trim('\n'))];

    /// <summary>
    /// Write a document back as plain text. Code cells keep a <c># %%</c> marker so a notes file that
    /// grew some analysis can be reopened with its cells intact, and so the code is visibly code rather
    /// than silently run together with the prose.
    /// </summary>
    public static string ToPlainText(NotebookDocument document)
    {
        var text = new StringBuilder();

        foreach (var cell in document.Cells)
        {
            var body = string.Concat(cell.Source);
            if (string.IsNullOrWhiteSpace(body)) continue;

            if (cell.CellType == "code") text.Append("# %%\n");
            text.Append(body.TrimEnd()).Append("\n\n");
        }

        return EndWithOneNewline(text.ToString());
    }

    // ── Shared ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Split the cells of a non-JSON file according to its format.</summary>
    public static List<NotebookCell> Split(NotebookFormat format, string source) => format switch
    {
        NotebookFormat.PercentPython => SplitPercent(source),
        NotebookFormat.Markdown => SplitMarkdown(source),
        NotebookFormat.PlainText => SplitPlainText(source),
        _ => SplitPlainText(source),
    };

    /// <summary>Serialize a document in a non-JSON format. Null for <c>.ipynb</c>, which the parser writes.</summary>
    public static string? Serialize(NotebookFormat format, NotebookDocument document) => format switch
    {
        NotebookFormat.PercentPython => ToPercent(document),
        NotebookFormat.Markdown => ToMarkdown(document),
        NotebookFormat.PlainText => ToPlainText(document),
        _ => null,
    };

    private static NotebookCell Cell(string kind, string source) => new()
    {
        CellType = kind,
        // nbformat stores source as a list of lines each keeping its newline. Written that way here too,
        // so a cell built from a script and one loaded from JSON are the same shape downstream.
        Source = SourceLines(source),
    };

    /// <summary>A block of text as nbformat's line list: every line but the last keeps its newline.</summary>
    internal static List<string> SourceLines(string text)
    {
        if (text.Length == 0) return [];

        var lines = Lines(text).ToList();
        return lines.Select((line, index) => index == lines.Count - 1 ? line : line + "\n").ToList();
    }

    private static IEnumerable<string> Lines(string text) => Normalise(text).Split('\n');

    private static string Normalise(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>
    /// Exactly one trailing newline.
    ///
    /// Writing a blank line at the end grew the file by one line on every save. These are files people
    /// keep in git, and a diff that appears whenever the notebook is opened and closed is a diff that
    /// trains people to ignore diffs.
    /// </summary>
    private static string EndWithOneNewline(string text) => text.TrimEnd('\n', '\r') + "\n";
}
