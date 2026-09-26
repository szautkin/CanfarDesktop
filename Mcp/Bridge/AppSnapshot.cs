using CanfarDesktop.Helpers;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Mcp.Bridge;

/// <summary>
/// What the app last said about itself — its server info and its tools — so the bridge can answer for
/// it while it is away. Kept in a file beside the bridge when given one, so it outlasts the bridge
/// itself: a client started before the app still gets the tools it had last time.
///
/// <para>A memory, not a source of truth: whatever it holds is replaced by the app's own answer as soon
/// as the app is there. A file that cannot be read or written is no memory, never an error.</para>
/// </summary>
public sealed class AppSnapshot(string? path = null)
{
    /// <summary>The name of the file kept beside the bridge.</summary>
    public const string FileName = "app-snapshot.json";

    public const string ServerInfoKey = "serverInfo";
    public const string ToolsKey = "tools";

    private readonly object _gate = new();
    private Dictionary<string, JsonValue>? _known;

    /// <summary>What the app last said under <paramref name="key"/>, or null when it has not said it.</summary>
    public JsonValue? this[string key]
    {
        get
        {
            lock (_gate) return Known().GetValueOrDefault(key);
        }
    }

    /// <summary>Keep <paramref name="value"/> under <paramref name="key"/>; the file is written only when it changes.</summary>
    public void Remember(string key, JsonValue value)
    {
        lock (_gate)
        {
            var known = Known();
            if (known.TryGetValue(key, out var old) && old.ToJsonString() == value.ToJsonString()) return;
            known[key] = value;
            if (path is null) return;
            try { AtomicFile.WriteAllText(path, new JsonObject(known).ToJsonString()); }
            catch { /* a read-only or full disk: remembered for this run only */ }
        }
    }

    private Dictionary<string, JsonValue> Known()
    {
        if (_known is not null) return _known;
        _known = [];
        if (path is null || !File.Exists(path)) return _known;
        try
        {
            if (JsonValue.Parse(File.ReadAllText(path)) is JsonObject saved)
                _known = new Dictionary<string, JsonValue>(saved.Members);
        }
        catch { /* half-written by something else, or not ours: start afresh */ }
        return _known;
    }
}
