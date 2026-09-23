using System.Text.Json;
using System.Text.Json.Serialization;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services.Fits;

public interface IAnnotationStore : CanfarDesktop.Helpers.IMarkStore
{
    /// <summary>Add one mark. Throws <see cref="ArgumentException"/> with the reason if it cannot be drawn.</summary>
    IReadOnlyList<Annotation> Add(string target, Annotation annotation);

    /// <summary>Replace one mark by id. Null when there is no such id.</summary>
    IReadOnlyList<Annotation>? Update(string target, Annotation annotation);

    /// <summary>Remove one mark by id. False when there was no such id.</summary>
    bool Remove(string target, string id);

    /// <summary>Every target that has marks.</summary>
    IReadOnlyList<string> Targets();

    /// <summary>
    /// Move the marks stored under <paramref name="from"/> to <paramref name="to"/>, when
    /// <paramref name="to"/> has none of its own. Returns how many moved.
    ///
    /// How marks written before extensions were tracked — keyed by bare path — reach the extension
    /// they belong to: the first image extension, the first time the file is opened.
    /// </summary>
    int Adopt(string from, string to);
}

/// <summary>
/// Annotations on disk, keyed by the file they were drawn on.
///
/// Same shape as <see cref="CoordinateStoreService"/> and for the same reasons: JSON in the app's local
/// folder, and a read that cannot fail loudly. A viewer must open whether or not this file is readable
/// — losing annotations is a disappointment, and refusing to show an image because of them would be a
/// bug.
///
/// Keyed by target path so marks come back with the image, and so two FITS files never show each
/// other's.
///
/// Writes go through <see cref="DiskPersistence"/> rather than File.WriteAllText: it writes atomically
/// and quarantines a corrupt file instead of silently dropping it, which matters more here than for a
/// bookmark list — these are drawings someone made.
/// </summary>
public sealed class AnnotationStore : IAnnotationStore
{
    private const string FileName = "annotations.json";
    /// <summary>
    /// 2 since marks are keyed per extension (<c>path#hdu</c>). The stored shape is the same, but an
    /// older build reading these keys would find no marks for a multi-extension file and start writing
    /// bare-path ones beside them. DiskPersistence refuses to load or overwrite a file from a newer
    /// schema, so the bump is what keeps an older build's hands off. Version 1 files still load.
    /// </summary>
    private const int SchemaVersion = 2;

    /// <summary>
    /// Marks kept for one target. A file nobody has drawn on is not stored at all, so this cap is about
    /// one pathological target rather than about disk.
    /// </summary>
    private const int MaxPerTarget = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string? _filePath;
    private readonly object _gate = new();

    /// <summary>The real store, in the app's local folder.</summary>
    public AnnotationStore()
    {
        try
        {
            _filePath = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, FileName);
        }
        catch
        {
            // Unpackaged — no local folder. Marks live for the session and are not persisted, which is
            // the same bargain the coordinate store makes.
        }
    }

    /// <summary>A store at an explicit path, for tests.</summary>
    public AnnotationStore(string filePath) => _filePath = filePath;

    private sealed class Contents
    {
        /// <summary>Target path → its marks.</summary>
        public Dictionary<string, List<Annotation>> Targets { get; set; } = new(StringComparer.Ordinal);
    }

    private Contents LoadAll()
        => DiskPersistence.Read(_filePath, SchemaVersion, () => new Contents(), JsonOptions).Value;

    private void WriteAll(Contents contents)
    {
        if (_filePath is null) return;

        // DiskPersistence writes atomically but does not create the folder: the app's local folder
        // always exists, so nothing had needed it to.
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // A failed write is raised rather than logged. Reading is the path that must never fail — a
        // viewer has to open whether or not this file is readable — but a SAVE that quietly did nothing
        // loses a drawing somebody made, and they find out the next time they open the file.
        if (!DiskPersistence.Write(_filePath, contents, SchemaVersion, JsonOptions))
            throw new InvalidOperationException($"could not save annotations to {_filePath}");
    }

    public IReadOnlyList<Annotation> LoadFor(string target)
    {
        lock (_gate)
            return LoadAll().Targets.TryGetValue(target, out var marks) ? marks : [];
    }

    public IReadOnlyList<Annotation> SaveFor(string target, IReadOnlyList<Annotation> annotations)
    {
        lock (_gate)
        {
            // Read-modify-write of the whole file: the sets are small, and a partial write that dropped
            // another file's annotations would be a silent loss of someone's work.
            var all = LoadAll();
            var kept = annotations.Take(MaxPerTarget).ToList();

            if (kept.Count == 0) all.Targets.Remove(target);
            else all.Targets[target] = kept;

            WriteAll(all);
            return kept;
        }
    }

    public IReadOnlyList<Annotation> Add(string target, Annotation annotation)
    {
        if (annotation.Validate() is { } wrong)
            throw new ArgumentException(wrong, nameof(annotation));

        lock (_gate)
        {
            var current = LoadFor(target).ToList();
            current.Add(annotation);
            return SaveFor(target, current);
        }
    }

    public IReadOnlyList<Annotation>? Update(string target, Annotation annotation)
    {
        if (annotation.Validate() is { } wrong)
            throw new ArgumentException(wrong, nameof(annotation));

        lock (_gate)
        {
            var current = LoadFor(target).ToList();
            var at = current.FindIndex(a => a.Id == annotation.Id);
            if (at < 0) return null;

            current[at] = annotation;
            return SaveFor(target, current);
        }
    }

    public bool Remove(string target, string id)
    {
        lock (_gate)
        {
            var current = LoadFor(target).ToList();
            if (current.RemoveAll(a => a.Id == id) == 0) return false;

            SaveFor(target, current);
            return true;
        }
    }

    public IReadOnlyList<string> Targets()
    {
        lock (_gate)
            return LoadAll().Targets.Keys.ToList();
    }

    public int Adopt(string from, string to)
    {
        lock (_gate)
        {
            if (string.Equals(from, to, StringComparison.Ordinal)) return 0;

            var all = LoadAll();
            if (!all.Targets.TryGetValue(from, out var legacy) || legacy.Count == 0) return 0;

            // Never merged into marks that are already there: those were drawn on this extension
            // deliberately, and folding a whole file's old marks in with them would put back exactly
            // the mixing this move exists to undo.
            if (all.Targets.TryGetValue(to, out var existing) && existing.Count > 0) return 0;

            // One write, so a crash cannot leave the marks under both keys or under neither.
            all.Targets[to] = legacy;
            all.Targets.Remove(from);
            WriteAll(all);
            return legacy.Count;
        }
    }
}
