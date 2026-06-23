using System.Text.Json;

namespace CanfarDesktop.Helpers;

/// <summary>Outcome of <see cref="DiskPersistence.Read{T}"/>.</summary>
public record DiskReadResult<T>(T Value, bool WasCorrupt, bool WasNewerVersion, bool WasLegacy);

/// <summary>
/// Versioned, resilient JSON persistence. Wraps a value in a <c>{ schemaVersion, value }</c>
/// envelope and:
///   • refuses to load (and to clobber) a file written by a NEWER schema version,
///   • quarantines a corrupt file (renames to <c>.corrupt</c>) instead of silently dropping data,
///   • still reads a LEGACY bare value (no envelope) so existing users' files are not lost,
///   • writes atomically (temp + replace) and reports write success.
/// </summary>
public static class DiskPersistence
{
    public static DiskReadResult<T> Read<T>(string? path, int currentSchemaVersion, Func<T> empty, JsonSerializerOptions? options = null)
    {
        if (path is null || !File.Exists(path)) return new(empty(), false, false, false);

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch
        {
            // Transient read failure (e.g. file locked) — don't quarantine, just report empty.
            return new(empty(), false, false, false);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object
                && TryGetProperty(root, "schemaVersion", out var sv)
                && TryGetProperty(root, "value", out var val))
            {
                if (sv.TryGetInt32(out var version) && version > currentSchemaVersion)
                    return new(empty(), false, true, false); // refuse newer; leave the file intact

                var value = val.Deserialize<T>(options) ?? empty();
                return new(value, false, false, false);
            }

            // Legacy bare value (pre-envelope).
            var legacy = JsonSerializer.Deserialize<T>(json, options) ?? empty();
            return new(legacy, false, false, true);
        }
        catch
        {
            Quarantine(path);
            return new(empty(), true, false, false);
        }
    }

    /// <summary>Write the value in a versioned envelope. Returns false on failure or if the
    /// existing file was written by a newer schema version (which must not be clobbered).</summary>
    public static bool Write<T>(string? path, T value, int currentSchemaVersion, JsonSerializerOptions? options = null)
    {
        if (path is null) return false;

        if (File.Exists(path) && PeekVersion(path) is { } existing && existing > currentSchemaVersion)
            return false; // don't clobber a newer-schema file

        try
        {
            var json = JsonSerializer.Serialize(new Envelope<T>(currentSchemaVersion, value), options);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DiskPersistence write failed for {path}: {ex.Message}");
            return false;
        }
    }

    private static int? PeekVersion(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && TryGetProperty(doc.RootElement, "schemaVersion", out var sv)
                && sv.TryGetInt32(out var v))
                return v;
        }
        catch
        {
            // Unreadable/corrupt — treat as no version (safe to overwrite).
        }
        return null;
    }

    /// <summary>Case-insensitive property lookup (envelope keys may be Pascal or camel case).</summary>
    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static void Quarantine(string path)
    {
        try
        {
            var dest = path + ".corrupt";
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(path, dest);
        }
        catch
        {
            // Best effort — never throw from a load path.
        }
    }

    private sealed record Envelope<T>(int SchemaVersion, T Value);
}
