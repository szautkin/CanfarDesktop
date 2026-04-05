using System.Text.Json;
using Windows.Storage;
using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Services.Fits;

public interface ICoordinateStoreService
{
    List<SavedCoordinate> Load();
    void Save(SavedCoordinate coord);
    void Delete(SavedCoordinate coord);
    void SaveAll(IEnumerable<SavedCoordinate> coords);
}

public class CoordinateStoreService : ICoordinateStoreService
{
    private const int MaxCoordinates = 50;
    private const string FileName = "saved_coordinates.json";

    private readonly string? _filePath;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public CoordinateStoreService()
    {
        try
        {
            var folder = ApplicationData.Current.LocalFolder.Path;
            _filePath = Path.Combine(folder, FileName);
        }
        catch
        {
            // Unpackaged — no local folder
        }
    }

    public List<SavedCoordinate> Load()
    {
        if (_filePath is null || !File.Exists(_filePath)) return [];
        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<SavedCoordinate>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load saved coordinates: {ex.Message}");
            return [];
        }
    }

    public void Save(SavedCoordinate coord)
    {
        if (_filePath is null) return;
        var list = Load();
        list.Insert(0, coord);
        if (list.Count > MaxCoordinates)
            list.RemoveRange(MaxCoordinates, list.Count - MaxCoordinates);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(list, JsonOptions));
    }

    public void Delete(SavedCoordinate coord)
    {
        if (_filePath is null) return;
        var list = Load();
        list.RemoveAll(c => c.Id == coord.Id);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(list, JsonOptions));
    }

    public void SaveAll(IEnumerable<SavedCoordinate> coords)
    {
        if (_filePath is null) return;
        File.WriteAllText(_filePath, JsonSerializer.Serialize(coords.ToList(), JsonOptions));
    }
}
