namespace CanfarDesktop.Mcp.Tools;

/// <summary>
/// Helper for the tool bases' "respond on timeout even if the handler ignores the token" pattern. When
/// a tool exceeds its budget we return a typed timeout immediately; the orphaned handler keeps running
/// (a backend call with no CancellationToken can't be interrupted), so we observe its eventual
/// result/exception to avoid an unobserved-task fault and dispose its linked CTS.
/// </summary>
internal static class ToolTimeout
{
    public static void ObserveInBackground(Task task, CancellationTokenSource cts)
        => _ = task.ContinueWith(
            static (t, state) =>
            {
                _ = t.Exception; // observe
                ((CancellationTokenSource)state!).Dispose();
            },
            cts,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
