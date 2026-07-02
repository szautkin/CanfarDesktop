using Windows.Networking.Connectivity;

namespace CanfarDesktop.Services;

/// <summary>
/// Tracks internet connectivity via the OS network-status events, so the shell can show an offline
/// hint and views can offer "network changed — retry". 1-to-1 with the macOS NetworkPathMonitor.
/// </summary>
public sealed class NetworkMonitor : IDisposable
{
    private readonly NetworkStatusChangedEventHandler _handler;

    public bool IsOnline { get; private set; }

    /// <summary>Raised after every connectivity change (thread-pool thread — marshal for UI).</summary>
    public event Action? StatusChanged;

    public NetworkMonitor()
    {
        IsOnline = Evaluate();
        _handler = _ =>
        {
            IsOnline = Evaluate();
            StatusChanged?.Invoke();
        };
        NetworkInformation.NetworkStatusChanged += _handler;
    }

    private static bool Evaluate()
    {
        try
        {
            return NetworkInformation.GetInternetConnectionProfile()
                ?.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
        }
        catch
        {
            // Connectivity probing must never take the app down; assume online and let
            // requests surface real failures.
            return true;
        }
    }

    public void Dispose() => NetworkInformation.NetworkStatusChanged -= _handler;
}
