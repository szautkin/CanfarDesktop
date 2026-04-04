namespace CanfarDesktop.Services.Notebook;

/// <summary>
/// Kernel lifecycle states. Used by the UI to show status indicators.
/// </summary>
public enum KernelState
{
    Dead,
    Starting,
    Idle,
    Busy,
    Error
}
