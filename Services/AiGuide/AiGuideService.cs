using System.Text;

namespace CanfarDesktop.Services.AiGuide;

/// <summary>
/// Owns the AI Guide state: per-tool description overrides and user-authored instruction tools.
/// SQLite-backed (via <see cref="AiGuideStore"/>), mirrored in memory for synchronous reads.
/// Mirror access is guarded so the MCP serve loop can read a <see cref="Snapshot"/> while the UI
/// thread edits.
///
/// Design notes:
///  * A built-in tool's description is the single source of truth and is NEVER stored. An override
///    is a sparse delta; "reset" soft-deletes the row.
///  * Guide tools are read-only: the agent CALLS one and a generic handler in the MCP bridge returns
///    the stored text (no execution). Name uniqueness among LIVE guides is enforced here, not by a
///    DB constraint, so a deleted name can be reused.
/// </summary>
public sealed class AiGuideService
{
    // Validation caps — generous enough for a re-tuning paragraph, bounded so a pathological value
    // can't bloat the wire manifest or a tool-call response.
    public const int MaxDescriptionChars = 600;
    public const int MaxBodyChars = 4000;

    private readonly AiGuideStore _store;
    private readonly object _gate = new();

    private Dictionary<string, string> _overrides = new();
    private List<AiGuideToolEntry> _guides = new();

    /// <summary>Names a guide may not shadow. Defaults to every name the catalog knows so collision
    /// protection holds even before the MCP host starts; the host refines it to the precise live router
    /// set once the agent tools are composed.</summary>
    public IReadOnlySet<string> KnownToolNames { get; set; } = AiGuideCatalog.MappedToolNames;

    public AiGuideService(AiGuideStore store)
    {
        _store = store;
        Reload();
    }

    // MARK: - Read / merge

    /// <summary>Reload both in-memory mirrors from the DB (live rows only).</summary>
    public void Reload()
    {
        var overrides = _store.LoadOverrides();
        var guides = _store.LoadGuides();
        lock (_gate)
        {
            _overrides = overrides;
            _guides = guides;
        }
    }

    public string EffectiveDescription(string toolName, string defaultDescription)
    {
        lock (_gate)
            return _overrides.TryGetValue(toolName, out var d) ? d : defaultDescription;
    }

    public bool IsOverridden(string toolName)
    {
        lock (_gate) return _overrides.ContainsKey(toolName);
    }

    /// <summary>Merge the live built-in manifest with stored overrides into UI rows.</summary>
    public IReadOnlyList<AiGuideTool> RowsForTools(IEnumerable<AiGuideToolInput> tools)
    {
        lock (_gate)
        {
            return tools.Select(t =>
            {
                var has = _overrides.TryGetValue(t.Name, out var ov);
                return new AiGuideTool(
                    Name: t.Name,
                    DefaultDescription: t.DefaultDescription,
                    EffectiveDescription: has ? ov! : t.DefaultDescription,
                    IsOverridden: has,
                    Category: t.Category);
            }).ToList();
        }
    }

    /// <summary>Immutable snapshot for the MCP bridge (built under the lock).</summary>
    public AiGuideSnapshot Snapshot()
    {
        lock (_gate)
            return new AiGuideSnapshot(
                new Dictionary<string, string>(_overrides),
                _guides.ToArray());
    }

    // MARK: - Tool description overrides

    /// <summary>Set (or, with empty text, clear) the override for a built-in tool. Trims whitespace;
    /// enforces the description cap.</summary>
    public void SetOverride(string toolName, string description)
    {
        var trimmed = (description ?? string.Empty).Trim();
        if (trimmed.Length > MaxDescriptionChars)
            throw AiGuideValidationException.TooLong("Description", MaxDescriptionChars);
        if (trimmed.Length == 0) { ClearOverride(toolName); return; }

        // Hold the gate across the DB write + mirror update so a concurrent Snapshot() can never observe
        // a mirror that disagrees with the DB. The store has its own lock; the order is always
        // service -> store (Snapshot only takes the service gate), so there's no lock-order inversion.
        lock (_gate)
        {
            _store.UpsertOverride(toolName, trimmed);
            _overrides[toolName] = trimmed;
        }
    }

    /// <summary>Reset a tool to its built-in description (soft-delete the override row).</summary>
    public void ClearOverride(string toolName)
    {
        lock (_gate)
        {
            if (!_overrides.ContainsKey(toolName)) return;
            _store.SoftDeleteOverride(toolName);
            _overrides.Remove(toolName);
        }
    }

    // MARK: - Custom guide tools

    /// <summary>Create a new guide tool. Returns the stored entry (with its assigned slug + id).
    /// Throws <see cref="AiGuideValidationException"/> on validation failure.</summary>
    public AiGuideToolEntry AddGuide(string name, string description, string? body)
    {
        // The whole operation runs under the gate (validate -> assign order -> write -> reload) so two
        // concurrent adds can't collide on a name or an orderIndex, and readers never see a half-applied
        // state. The gate is recursive, so the nested ValidatedName/Reload re-acquisitions are fine.
        lock (_gate)
        {
            var slug = ValidatedName(name, excluding: null);
            var desc = ValidatedDescription(description);
            var bod = ValidatedBody(body);

            var entry = new AiGuideToolEntry(Guid.NewGuid(), slug, desc, bod);
            _store.InsertGuide(entry, _guides.Count);
            Reload();
            return entry;
        }
    }

    /// <summary>Update an existing guide. Re-validates the (possibly changed) name.</summary>
    public void UpdateGuide(Guid id, string name, string description, string? body)
    {
        lock (_gate)
        {
            var slug = ValidatedName(name, excluding: id);
            var desc = ValidatedDescription(description);
            var bod = ValidatedBody(body);

            _store.UpdateGuide(id, slug, desc, bod);
            Reload();
        }
    }

    /// <summary>Soft-delete a guide tool.</summary>
    public void DeleteGuide(Guid id)
    {
        lock (_gate)
        {
            _store.SoftDeleteGuide(id);
            Reload();
        }
    }

    // MARK: - Validation

    private string ValidatedName(string raw, Guid? excluding)
    {
        var slug = Slug(raw);
        if (slug.Length == 0) throw AiGuideValidationException.NameEmpty();
        if (KnownToolNames.Contains(slug)) throw AiGuideValidationException.NameCollidesWithTool();
        lock (_gate)
        {
            if (_guides.Any(g => g.Name == slug && g.Id != excluding))
                throw AiGuideValidationException.NameTaken();
        }
        return slug;
    }

    private static string ValidatedDescription(string raw)
    {
        var trimmed = (raw ?? string.Empty).Trim();
        // A guide must carry a one-liner (it's shown in tools/list and is the call fallback when there's
        // no body). macOS enforces this in the edit sheet; we enforce it at the service for safety.
        if (trimmed.Length == 0) throw AiGuideValidationException.DescriptionEmpty();
        if (trimmed.Length > MaxDescriptionChars)
            throw AiGuideValidationException.TooLong("Description", MaxDescriptionChars);
        return trimmed;
    }

    private static string? ValidatedBody(string? raw)
    {
        if (raw is null) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > MaxBodyChars)
            throw AiGuideValidationException.TooLong("Instructions", MaxBodyChars);
        return trimmed;
    }

    /// <summary>
    /// Turn a display name into a valid MCP tool name: lowercase ASCII alphanumerics, with
    /// spaces/dashes/dots/underscores collapsed to a single <c>_</c>, trimmed of leading/trailing
    /// underscores. Non-ASCII letters are dropped (the agent-facing name must be wire-safe).
    /// </summary>
    public static string Slug(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.ToLowerInvariant())
        {
            if (ch <= 127 && char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch is ' ' or '-' or '_' or '.') sb.Append('_');
        }
        var outp = sb.ToString();
        while (outp.Contains("__")) outp = outp.Replace("__", "_");
        return outp.Trim('_');
    }
}
