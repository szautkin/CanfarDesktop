using System.Text;
using System.Text.RegularExpressions;

namespace CanfarDesktop.Services.Workflows;

/// <summary>One step of a workflow: title, body, agent-tool hints, an optional app view deep-link,
/// an optional free-text note, and its check-off state.</summary>
public sealed record WorkflowStep(
    int Index,
    string Title,
    string Body,
    IReadOnlyList<string> Tools,
    string? View,
    string? Note,
    bool Done);

/// <summary>A parsed workflow document. <see cref="Warnings"/> carries tolerant-parse diagnostics
/// (never fatal) for the editor and template tests.</summary>
public sealed record WorkflowDoc(
    string Title,
    string Description,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<WorkflowStep> Steps,
    IReadOnlyList<string> Warnings)
{
    public IReadOnlyList<string> Tags =>
        Metadata.TryGetValue("Tags", out var t)
            ? t.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();

    public int DoneCount => Steps.Count(s => s.Done);
}

/// <summary>
/// The `.workflow.md` markdown-checklist dialect: pure parse / step-flip / skeleton, no I/O.
/// Format: first `# ` line = title, first `&gt; ` line = description, `Key: value` lines before the
/// first step = metadata, steps are `- [ ]` / `- [x]` items whose `**bold lead**` is the title,
/// with indented `Tool:` / `View:` / `Note:` attachment lines. Tolerant by design — anything
/// unrecognized becomes body text or a warning, never a failure.
/// </summary>
public static partial class WorkflowFormat
{
    public const string FileExtension = ".workflow.md";

    [GeneratedRegex(@"^\s*-\s*\[( |x|X)\]\s*(.*)$")]
    private static partial Regex StepStart();

    [GeneratedRegex(@"^\*\*(.+?)\*\*\s*(?:[—–-]\s*)?(.*)$")]
    private static partial Regex BoldLead();

    [GeneratedRegex(@"^([A-Za-z][A-Za-z ]{0,30}):\s*(.+)$")]
    private static partial Regex MetaLine();

    public static WorkflowDoc Parse(string text)
    {
        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var warnings = new List<string>();
        string? title = null;
        string description = string.Empty;
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var steps = new List<WorkflowStep>();

        // Mutable accumulator for the step being read.
        string? stepTitle = null;
        bool stepDone = false;
        var body = new List<string>();
        var tools = new List<string>();
        string? view = null, note = null;
        var inStep = false;

        void FlushStep()
        {
            if (!inStep) return;
            steps.Add(new WorkflowStep(
                steps.Count,
                (stepTitle ?? string.Empty).Trim().Length > 0 ? stepTitle!.Trim() : $"Step {steps.Count + 1}",
                string.Join("\n", body).Trim(),
                tools.ToList(), view, note, stepDone));
            stepTitle = null; stepDone = false; body.Clear(); tools.Clear(); view = null; note = null;
            inStep = false;
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();

            var step = StepStart().Match(line);
            if (step.Success)
            {
                FlushStep();
                inStep = true;
                stepDone = step.Groups[1].Value is "x" or "X";
                var content = step.Groups[2].Value.Trim();
                var bold = BoldLead().Match(content);
                if (bold.Success)
                {
                    stepTitle = bold.Groups[1].Value;
                    if (bold.Groups[2].Value.Trim() is { Length: > 0 } rest) body.Add(rest);
                }
                else
                {
                    stepTitle = content; // no bold lead — the whole line is the title
                }
                continue;
            }

            if (inStep)
            {
                var t = line.Trim();
                if (t.Length == 0) continue;
                if (t.StartsWith("#", StringComparison.Ordinal)) { FlushStep(); continue; } // heading ends the list item
                if (TryAttachment(t, "Tool", out var v) || TryAttachment(t, "Tools", out v))
                    tools.AddRange(v.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                else if (TryAttachment(t, "View", out v)) view = v.Trim();
                else if (TryAttachment(t, "Note", out v)) note = v.Trim();
                else body.Add(t);
                continue;
            }

            // Preamble (before the first step).
            if (line.StartsWith("# ", StringComparison.Ordinal) && !line.StartsWith("##", StringComparison.Ordinal))
            {
                if (title is null) title = line[2..].Trim();
                continue;
            }
            if (line.StartsWith("> ", StringComparison.Ordinal))
            {
                if (description.Length == 0) description = line[2..].Trim();
                continue;
            }
            if (line.StartsWith("#", StringComparison.Ordinal)) continue; // section headings ("## Steps") are ignored
            var meta = MetaLine().Match(line.Trim());
            if (meta.Success) metadata[meta.Groups[1].Value.Trim()] = meta.Groups[2].Value.Trim();
        }
        FlushStep();

        if (title is null)
        {
            title = "Untitled workflow";
            warnings.Add("No `# Title` line found — using \"Untitled workflow\".");
        }
        if (steps.Count == 0) warnings.Add("No steps found — add lines like `- [ ] **Step title** — what to do`.");

        return new WorkflowDoc(title, description, metadata, steps, warnings);
    }

    private static bool TryAttachment(string trimmed, string key, out string value)
    {
        if (trimmed.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
        {
            value = trimmed[(key.Length + 1)..];
            return true;
        }
        value = string.Empty;
        return false;
    }

    /// <summary>
    /// Flip the done-marker of the <paramref name="stepIndex"/>-th step (0-based, in step order),
    /// changing ONLY the `[ ]`/`[x]` characters — every other byte of the author's text is
    /// preserved (the file is the state, so rewrites must not reformat).
    /// Throws when the index doesn't exist.
    /// </summary>
    public static string WithStepDone(string text, int stepIndex, bool done)
    {
        // Scan lines IN PLACE (no split/rejoin): normalizing line endings here rewrote every CRLF in
        // a Windows-authored file as LF, breaking the only-one-byte-changes contract above.
        var probe = 0;
        var i = 0;
        while (i <= text.Length)
        {
            var end = i;
            while (end < text.Length && text[end] is not ('\n' or '\r')) end++;
            var line = text.AsSpan(i, end - i);
            if (StepStart().IsMatch(line) && probe++ == stepIndex)
            {
                var open = i + line.IndexOf('[');
                return string.Concat(text.AsSpan(0, open + 1), done ? "x" : " ", text.AsSpan(open + 2));
            }
            if (end >= text.Length) break;
            i = text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? end + 2 : end + 1;
        }
        throw new ArgumentOutOfRangeException(nameof(stepIndex), $"workflow has {probe} steps; step {stepIndex} does not exist.");
    }

    /// <summary>A starter document for the "New workflow" action.</summary>
    public static string Skeleton(string title) => new StringBuilder()
        .AppendLine($"# {title}")
        .AppendLine("> One-line description of what this protocol achieves.")
        .AppendLine("Tags: ")
        .AppendLine("Time: ~1 h")
        .AppendLine()
        .AppendLine("## Steps")
        .AppendLine()
        .AppendLine("- [ ] **First step** — What to do and why.")
        .AppendLine("      Tool: search_observations")
        .AppendLine("      View: search")
        .AppendLine("- [ ] **Second step** — ...")
        .ToString();

    /// <summary>
    /// Validate a parsed doc against the app's known view keys and agent tool names (both injected —
    /// the parser itself stays app-agnostic). Returns human-readable problems for the editor and
    /// the template tests.
    /// </summary>
    public static IReadOnlyList<string> Validate(
        WorkflowDoc doc, IReadOnlySet<string> knownViews, IReadOnlySet<string> knownTools)
    {
        var problems = new List<string>(doc.Warnings);
        foreach (var s in doc.Steps)
        {
            if (s.View is { Length: > 0 } v && !knownViews.Contains(v))
                problems.Add($"Step {s.Index + 1} (\"{s.Title}\"): unknown View \"{v}\".");
            foreach (var tool in s.Tools)
                if (!knownTools.Contains(tool))
                    problems.Add($"Step {s.Index + 1} (\"{s.Title}\"): unknown Tool \"{tool}\".");
        }
        return problems;
    }

    /// <summary>The NavigateByKey keys a `View:` attachment may use (mirrors MainWindow.NavigateByKey).</summary>
    public static readonly IReadOnlySet<string> KnownViews = new HashSet<string>(StringComparer.Ordinal)
    {
        "landing", "portal", "search", "research", "storage", "notebook", "fitsViewer", "aiGuide", "workflows",
    };
}
