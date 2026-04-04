namespace CanfarDesktop.Services.Notebook;

using System.Text.Json;

/// <summary>
/// Persists notebook settings to %LocalAppData%/CanfarDesktop/Notebook/settings.json.
/// Singleton service. All properties have sensible defaults.
/// </summary>
public class NotebookSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CanfarDesktop", "Notebook", "settings.json");

    // Editor
    public int FontSize { get; set; } = 13;
    public int TabSize { get; set; } = 4;
    public bool WordWrap { get; set; } = true;

    // Autosave
    public bool AutosaveEnabled { get; set; } = true;
    public int AutosaveIntervalSeconds { get; set; } = 30;

    // Execution
    public string? PythonPath { get; set; }
    public int ExecutionTimeoutSeconds { get; set; } = 60;

    // UI
    public bool ShowToolbar { get; set; } = true;

    public event Action? Changed;

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (dir is not null) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings save failed: {ex.Message}");
        }
        Changed?.Invoke();
    }

    public static NotebookSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new NotebookSettings();
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<NotebookSettings>(json) ?? new NotebookSettings();
        }
        catch
        {
            return new NotebookSettings();
        }
    }
}
