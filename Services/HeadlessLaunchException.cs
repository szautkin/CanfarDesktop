namespace CanfarDesktop.Services;

/// <summary>
/// Thrown when a headless replica fan-out partially succeeds: at least one replica launched
/// before a later one failed. <see cref="LaunchedIds"/> are the live sessions (the caller can
/// roll them back via delete); <see cref="FailedAtIndex"/> is the 0-based replica that failed.
/// A failure on the very first replica surfaces the underlying error instead (no partial state).
/// </summary>
public class HeadlessLaunchException : Exception
{
    public IReadOnlyList<string> LaunchedIds { get; }
    public int FailedAtIndex { get; }

    public HeadlessLaunchException(IReadOnlyList<string> launchedIds, int failedAtIndex, string message)
        : base(message)
    {
        LaunchedIds = launchedIds;
        FailedAtIndex = failedAtIndex;
    }
}
