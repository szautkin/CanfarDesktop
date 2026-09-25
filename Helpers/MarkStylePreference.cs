using CanfarDesktop.Models;

namespace CanfarDesktop.Helpers;

/// <summary>
/// What the next mark will look like when nothing is selected.
///
/// An interface rather than a call into settings, so the thing that draws marks can be tested without a
/// packaged app behind it — and so a viewer cannot reach past it and remember a style somewhere else.
/// </summary>
public interface IMarkStylePreference
{
    MarkStyle Default { get; set; }
}

/// <summary>A preference that lives for the session. For tests, and for a viewer with no settings.</summary>
public sealed class InMemoryMarkStylePreference : IMarkStylePreference
{
    public MarkStyle Default { get; set; } = MarkStyle.UserDefault;
}
