namespace CanfarDesktop.Mcp;

/// <summary>
/// Bridges the live UI's current navigation state to the <c>get_current_view</c> MCP tool. The window
/// registers a provider once; the tool reads it on demand from the MCP connection thread. The provider
/// only reads enum-sized nav state (no UI mutation), so cross-thread reads are benign. Defaults to the
/// landing view until a provider is registered.
/// </summary>
public sealed class AppViewStateService
{
    /// <summary>Per-mode view context: which mode, plus optional Search focus and open FITS paths.</summary>
    public sealed record ModeView(
        string Mode,
        string ModeTitle,
        double? SearchFocusRA,
        double? SearchFocusDec,
        IReadOnlyList<string> OpenFitsPaths);

    private static readonly ModeView Default = new("landing", "Home", null, null, Array.Empty<string>());

    private volatile Func<ModeView>? _provider;

    public void SetProvider(Func<ModeView> provider) => _provider = provider;

    public ModeView Capture()
    {
        try { return _provider?.Invoke() ?? Default; }
        catch { return Default; }
    }
}
