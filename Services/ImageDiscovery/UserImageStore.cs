using System.Text.Json;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services.ImageDiscovery;

public interface IUserImageStore
{
    /// <summary>The images the user added by hand, newest first.</summary>
    IReadOnlyList<RegistryImage> All();

    /// <summary>Add one. Returns false when it is already there — an answer, not a failure.</summary>
    bool Add(RegistryImage image);

    /// <summary>Remove one by its full reference. False when it was not in the list.</summary>
    bool Remove(string id);

    /// <summary>Raised when the list changes, so every widget reading it can catch up.</summary>
    event Action? Changed;
}

/// <summary>
/// The images someone added from the registry by hand.
///
/// Small, and read by three surfaces — the images card, the package search, and the launch form — which
/// is exactly why it is one store rather than three copies. Register it as a SINGLETON: three widgets
/// each with their own idea of the list is a list that disagrees with itself the moment one of them
/// adds to it.
///
/// The images are stored, not the search. A search result is a moment; the list is a decision.
/// </summary>
public sealed class UserImageStore : IUserImageStore
{
    private const string FileName = "user_images.json";
    private const int SchemaVersion = 1;

    /// <summary>
    /// A ceiling on a hand-curated list. Nobody adds a thousand images one at a time; a number this
    /// size is a bug or a paste, and either way the launch form's dropdown stops being usable long
    /// before the file stops being small.
    /// </summary>
    private const int MaxImages = 200;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string? _filePath;
    private readonly object _gate = new();

    public event Action? Changed;

    public UserImageStore()
    {
        try
        {
            _filePath = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, FileName);
        }
        catch
        {
            // Unpackaged — the list lives for the session, as the other local stores do.
        }
    }

    /// <summary>A store at an explicit path, for tests.</summary>
    public UserImageStore(string filePath) => _filePath = filePath;

    public IReadOnlyList<RegistryImage> All()
    {
        lock (_gate) return Load();
    }

    public bool Add(RegistryImage image)
    {
        if (string.IsNullOrWhiteSpace(image.Id)) return false;

        lock (_gate)
        {
            var images = Load();
            if (images.Any(i => string.Equals(i.Id, image.Id, StringComparison.OrdinalIgnoreCase))) return false;

            // Newest first: the last thing added is the thing being looked for.
            images.Insert(0, image with { AddedAt = image.AddedAt ?? IsoTime.Now() });
            if (images.Count > MaxImages) images.RemoveRange(MaxImages, images.Count - MaxImages);

            Save(images);
        }

        Changed?.Invoke();
        return true;
    }

    public bool Remove(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;

        bool removed;
        lock (_gate)
        {
            var images = Load();
            removed = images.RemoveAll(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) Save(images);
        }

        if (removed) Changed?.Invoke();
        return removed;
    }

    private List<RegistryImage> Load()
        => DiskPersistence.Read(_filePath, SchemaVersion, () => new List<RegistryImage>(), Json).Value;

    private void Save(List<RegistryImage> images) => DiskPersistence.Write(_filePath, images, SchemaVersion, Json);
}
