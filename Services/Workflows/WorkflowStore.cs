using System.Reflection;
using System.Text;

namespace CanfarDesktop.Services.Workflows;

public enum WorkflowSource { BuiltIn, Local, VoSpace }

/// <summary>A workflow the store knows about: stable id + source + parsed doc.</summary>
public sealed record WorkflowInfo(string Id, WorkflowSource Source, WorkflowDoc Doc, string RawText);

/// <summary>
/// Owns the two synchronous workflow tiers: read-only built-in templates (embedded resources) and
/// the user's local working copies (<c>%LOCALAPPDATA%\Verbinal\Workflows\*.workflow.md</c> — the
/// ONLY tier where check-off state is written; the file is the state). VOSpace workflows are a
/// remote tier handled by the page/tools via StorageService — deliberately not here, so this class
/// stays synchronous, lock-simple, and unit-testable with a temp directory.
/// Thread-safe: MCP tool calls arrive off the UI thread. <see cref="Changed"/> is raised outside
/// the lock after every mutation (agent or user) so the page can pull-refresh.
/// </summary>
public sealed class WorkflowStore
{
    public const string BuiltInPrefix = "builtin:";
    public const string LocalPrefix = "local:";

    private readonly string _directory;
    private readonly Func<IReadOnlyList<(string Name, string Text)>> _builtins;
    private readonly object _gate = new();

    /// <summary>Raised after any local mutation (save/update/step/delete/use) — outside the lock.
    /// Carries the affected local id (null after a delete) so the page can follow agent activity
    /// to the specific workflow being worked on.</summary>
    public event Action<string?>? Changed;

    public WorkflowStore(string? directory = null, Func<IReadOnlyList<(string, string)>>? builtins = null)
    {
        _directory = directory ?? Path.Combine(
            Helpers.PackagePaths.RealLocalAppData(), "Verbinal", "Workflows");
        _builtins = builtins ?? LoadEmbeddedTemplates;
    }

    // ── Listing / reading ─────────────────────────────────────────────────────

    public IReadOnlyList<WorkflowInfo> ListBuiltIn()
        => _builtins().Select(b => new WorkflowInfo(
                BuiltInPrefix + b.Name, WorkflowSource.BuiltIn, WorkflowFormat.Parse(b.Text), b.Text))
            .OrderBy(w => w.Doc.Title, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<WorkflowInfo> ListLocal()
    {
        lock (_gate)
        {
            if (!Directory.Exists(_directory)) return Array.Empty<WorkflowInfo>();
            var list = new List<WorkflowInfo>();
            foreach (var file in Directory.GetFiles(_directory, "*" + WorkflowFormat.FileExtension))
            {
                string text;
                try { text = File.ReadAllText(file); }
                catch { continue; } // unreadable file — skip, don't break the list
                list.Add(new WorkflowInfo(LocalPrefix + SlugOf(file), WorkflowSource.Local, WorkflowFormat.Parse(text), text));
            }
            return list.OrderBy(w => w.Doc.Title, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public WorkflowInfo? Get(string id)
    {
        if (id.StartsWith(BuiltInPrefix, StringComparison.Ordinal))
            return ListBuiltIn().FirstOrDefault(w => w.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (!id.StartsWith(LocalPrefix, StringComparison.Ordinal)) return null;
        lock (_gate)
        {
            var path = PathOf(id);
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path);
            return new WorkflowInfo(id, WorkflowSource.Local, WorkflowFormat.Parse(text), text);
        }
    }

    // ── Mutations (local tier only) ───────────────────────────────────────────

    /// <summary>Create a new local workflow from raw text; the id is derived from the name and
    /// de-duplicated (-2, -3, ...). Returns the new id.</summary>
    public string SaveNew(string name, string text)
    {
        string id;
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            var slug = Slugify(name);
            var candidate = slug;
            for (var n = 2; File.Exists(Path.Combine(_directory, candidate + WorkflowFormat.FileExtension)); n++)
                candidate = $"{slug}-{n}";
            File.WriteAllText(Path.Combine(_directory, candidate + WorkflowFormat.FileExtension), text, new UTF8Encoding(false));
            id = LocalPrefix + candidate;
        }
        Changed?.Invoke(id);
        return id;
    }

    /// <summary>Replace a local workflow's full text (the editor / update_workflow).</summary>
    public void UpdateText(string id, string text)
    {
        lock (_gate)
        {
            var path = RequireLocalPath(id);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
        Changed?.Invoke(id);
    }

    /// <summary>Flip one step's done-marker in place — only the checkbox characters change.</summary>
    public void SetStepDone(string id, int stepIndex, bool done)
    {
        lock (_gate)
        {
            var path = RequireLocalPath(id);
            File.WriteAllText(path, WorkflowFormat.WithStepDone(File.ReadAllText(path), stepIndex, done), new UTF8Encoding(false));
        }
        Changed?.Invoke(id);
    }

    public void Delete(string id)
    {
        lock (_gate)
        {
            var path = RequireLocalPath(id);
            if (File.Exists(path)) File.Delete(path);
        }
        Changed?.Invoke(null);
    }

    /// <summary>Instantiate a source (built-in / local / raw remote text) as a local working copy —
    /// the only place progress can be tracked. Returns the new local id.</summary>
    public string UseWorkflow(string sourceId, string? newName = null)
    {
        var source = Get(sourceId)
            ?? throw new InvalidOperationException($"No workflow '{sourceId}'. Call list_workflows for ids.");
        return UseText(source.RawText, newName ?? source.Doc.Title);
    }

    /// <summary>Local-copy path for remote (VOSpace) text the caller fetched itself.</summary>
    public string UseText(string rawText, string name) => SaveNew(name, rawText);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string PathOf(string id) => Path.Combine(_directory, id[LocalPrefix.Length..] + WorkflowFormat.FileExtension);

    private string RequireLocalPath(string id)
        => id.StartsWith(LocalPrefix, StringComparison.Ordinal) && File.Exists(PathOf(id))
            ? PathOf(id)
            : throw new InvalidOperationException(
                id.StartsWith(BuiltInPrefix, StringComparison.Ordinal)
                    ? $"'{id}' is a read-only template — call use_workflow to make a local working copy first."
                    : $"No local workflow '{id}'. Call list_workflows for ids.");

    private static string SlugOf(string filePath)
    {
        var name = Path.GetFileName(filePath);
        return name.EndsWith(WorkflowFormat.FileExtension, StringComparison.OrdinalIgnoreCase)
            ? name[..^WorkflowFormat.FileExtension.Length] : Path.GetFileNameWithoutExtension(name);
    }

    internal static string Slugify(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name.Trim().ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : '-');
        var slug = string.Join("-", sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "workflow" : slug[..Math.Min(slug.Length, 60)];
    }

    private static IReadOnlyList<(string, string)> LoadEmbeddedTemplates()
    {
        var assembly = typeof(WorkflowStore).Assembly;
        var list = new List<(string, string)>();
        foreach (var res in assembly.GetManifestResourceNames())
        {
            if (!res.EndsWith(WorkflowFormat.FileExtension, StringComparison.OrdinalIgnoreCase)
                || !res.Contains(".Workflows.", StringComparison.OrdinalIgnoreCase)) continue;
            using var stream = assembly.GetManifestResourceStream(res);
            if (stream is null) continue;
            using var reader = new StreamReader(stream);
            // Resource name like "CanfarDesktop.Resources.Workflows.cfht-imaging-recon.workflow.md"
            var parts = res.Split('.');
            var slug = parts.Length >= 3 ? parts[^3] : "template";
            list.Add((slug, reader.ReadToEnd()));
        }
        return list;
    }
}
