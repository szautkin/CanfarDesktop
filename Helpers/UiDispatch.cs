using Microsoft.UI.Dispatching;

namespace CanfarDesktop.Helpers;

/// <summary>
/// Run work on the UI thread and await its result from a thread that is not the UI thread.
///
/// Every MCP action has the same shape: an MCP connection thread needs something done to a XAML
/// object, which may only be touched on the dispatcher's thread, and needs the answer back. Written
/// out per action that is six lines of TaskCompletionSource each; the ones that were written out by
/// hand differed from each other in where the try/catch sat, which is the kind of difference that is
/// invisible until an exception picks the wrong side of it.
///
/// <paramref name="fallback"/> is returned when the dispatch itself cannot be queued — the window is
/// closing, and there is no thread left to answer on.
/// </summary>
public static class UiDispatch
{
    public static Task<T> OnUi<T>(DispatcherQueue queue, Func<T> work, T fallback)
    {
        var tcs = new TaskCompletionSource<T>();
        if (!queue.TryEnqueue(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }))
            tcs.SetResult(fallback);
        return tcs.Task;
    }

    /// <summary>
    /// As <see cref="OnUi{T}"/>, for work that awaits. The try/catch is INSIDE the enqueued lambda —
    /// around the await, not around the enqueue — so a fault raised after the first suspension point
    /// still reaches the caller instead of being lost on the dispatcher thread.
    /// </summary>
    public static Task<T> OnUiAsync<T>(DispatcherQueue queue, Func<Task<T>> work, T fallback)
    {
        var tcs = new TaskCompletionSource<T>();
        if (!queue.TryEnqueue(async () =>
        {
            try { tcs.SetResult(await work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }))
            tcs.SetResult(fallback);
        return tcs.Task;
    }
}
