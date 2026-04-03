using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CanfarDesktop.Services;

namespace CanfarDesktop.ViewModels;

public partial class PlatformLoadViewModel : ObservableObject
{
    private readonly IPlatformService _platformService;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private double _cpuUsed;

    [ObservableProperty]
    private double _cpuAvailable;

    [ObservableProperty]
    private double _cpuPercent;

    [ObservableProperty]
    private double _ramUsedGB;

    [ObservableProperty]
    private double _ramAvailableGB;

    [ObservableProperty]
    private double _ramPercent;

    [ObservableProperty]
    private string _lastUpdate = string.Empty;

    [ObservableProperty]
    private int _instancesTotal;

    [ObservableProperty]
    private int _instancesSessions;

    [ObservableProperty]
    private int _instancesDesktopApp;

    [ObservableProperty]
    private int _instancesHeadless;

    [ObservableProperty]
    private bool _hasInstances;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    public PlatformLoadViewModel(IPlatformService platformService)
    {
        _platformService = platformService;
    }

    [RelayCommand]
    public async Task LoadStatsAsync()
    {
        IsLoading = true;
        HasError = false;

        try
        {
            var stats = await _platformService.GetStatsAsync();
            if (stats is not null)
            {
                CpuUsed = stats.Cores.RequestedCPUCores;
                CpuAvailable = stats.Cores.CpuCoresAvailable;
                CpuPercent = CpuAvailable > 0 ? CpuUsed / CpuAvailable * 100 : 0;

                RamUsedGB = ParseRamGB(stats.Ram.RequestedRAM);
                RamAvailableGB = ParseRamGB(stats.Ram.RamAvailable);
                RamPercent = RamAvailableGB > 0 ? RamUsedGB / RamAvailableGB * 100 : 0;

                if (stats.Instances is not null)
                {
                    InstancesTotal = stats.Instances.Total;
                    InstancesSessions = stats.Instances.Session;
                    InstancesDesktopApp = stats.Instances.DesktopApp;
                    InstancesHeadless = stats.Instances.Headless;
                    HasInstances = true;
                }

                LastUpdate = DateTime.Now.ToString("HH:mm:ss");
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load stats: {ex.Message}";
            HasError = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    internal static double ParseRamGB(string ramString)
    {
        if (string.IsNullOrEmpty(ramString)) return 0;
        var s = ramString.Trim();

        if (TryStripSuffix(s, ["GB", "Gi", "G"], out var gb)) return gb;
        if (TryStripSuffix(s, ["TB", "Ti", "T"], out var tb)) return tb * 1024;
        if (TryStripSuffix(s, ["MB", "Mi", "M"], out var mb)) return mb / 1024;

        return double.TryParse(s, out var raw) ? raw : 0;
    }

    private static bool TryStripSuffix(string s, string[] suffixes, out double value)
    {
        foreach (var suffix in suffixes)
        {
            if (s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && double.TryParse(s[..^suffix.Length], out value))
                return true;
        }
        value = 0;
        return false;
    }
}
