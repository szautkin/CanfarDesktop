namespace CanfarDesktop.Services.AiGuide;

/// <summary>
/// A user-authored "instruction tool": named, read-only guidance the AI agent discovers in
/// <c>tools/list</c> and can CALL to receive <see cref="Body"/> (there is no execution — a generic
/// handler in the MCP bridge returns the stored text). <see cref="Name"/> is the agent-facing tool
/// name (a sanitized slug — see <see cref="AiGuideService.Slug"/>).
/// </summary>
public sealed record AiGuideToolEntry(Guid Id, string Name, string Description, string? Body)
{
    /// <summary>What a call to this guide returns: the body if present, else the description
    /// (a one-liner can stand alone as its own answer).</summary>
    public string CallPayload =>
        string.IsNullOrWhiteSpace(Body) ? Description : Body!;
}

/// <summary>
/// One built-in tool fed to the merge. Keeps <see cref="AiGuideService"/> decoupled from the
/// router/manifest types — the UI layer projects the live manifest into these before asking the
/// service to merge in overrides.
/// </summary>
public sealed record AiGuideToolInput(string Name, string DefaultDescription, string Category);

/// <summary>
/// A derived row for the AI Guide UI: a built-in tool's default description merged with any user
/// override. Never persisted (computed per render).
/// </summary>
public sealed record AiGuideTool(
    string Name,
    string DefaultDescription,
    string EffectiveDescription,
    bool IsOverridden,
    string Category);

/// <summary>
/// Immutable snapshot the MCP bridge reads to (a) substitute descriptions in <c>tools/list</c> and
/// (b) list + answer user guide tools. Built from <see cref="AiGuideService"/> state under a lock and
/// captured by the bridge's resolver delegates, so it crosses to the MCP thread without a race.
/// </summary>
public sealed class AiGuideSnapshot
{
    public IReadOnlyDictionary<string, string> Overrides { get; }
    public IReadOnlyList<AiGuideToolEntry> Guides { get; }

    public static readonly AiGuideSnapshot Empty =
        new(new Dictionary<string, string>(), Array.Empty<AiGuideToolEntry>());

    public AiGuideSnapshot(IReadOnlyDictionary<string, string> overrides, IReadOnlyList<AiGuideToolEntry> guides)
    {
        Overrides = overrides;
        Guides = guides;
    }

    /// <summary>Effective description for a built-in tool: the override if present, else the
    /// caller's built-in default.</summary>
    public string DescriptionForTool(string name, string defaultDescription)
        => Overrides.TryGetValue(name, out var d) ? d : defaultDescription;

    /// <summary>The payload a guide-tool call returns, or <c>null</c> if <paramref name="name"/>
    /// isn't a live guide.</summary>
    public string? GuideBody(string name)
    {
        foreach (var g in Guides)
            if (g.Name == name) return g.CallPayload;
        return null;
    }
}

/// <summary>The kind of validation failure surfaced by the AI Guide edit surface.</summary>
public enum AiGuideErrorKind
{
    TooLong,
    NameEmpty,
    NameTaken,
    NameCollidesWithTool,
}

/// <summary>User-actionable validation failure raised by <see cref="AiGuideService"/> edits.</summary>
public sealed class AiGuideValidationException : Exception
{
    public AiGuideErrorKind Kind { get; }

    private AiGuideValidationException(AiGuideErrorKind kind, string message) : base(message)
        => Kind = kind;

    public static AiGuideValidationException TooLong(string field, int limit)
        => new(AiGuideErrorKind.TooLong, $"{field} exceeds the {limit}-character limit.");

    public static AiGuideValidationException NameEmpty()
        => new(AiGuideErrorKind.NameEmpty, "Enter a name using letters, numbers, spaces, or underscores.");

    public static AiGuideValidationException NameTaken()
        => new(AiGuideErrorKind.NameTaken, "You already have a guide tool with this name.");

    public static AiGuideValidationException NameCollidesWithTool()
        => new(AiGuideErrorKind.NameCollidesWithTool, "That name is already used by a built-in tool. Choose another.");
}
