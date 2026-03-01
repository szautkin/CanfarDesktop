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

    private static double ParseRamGB(string ramString)
    {
        if (string.IsNullOrEmpty(ramString)) return 0;
        var numeric = ramString.TrimEnd('G', 'g', 'B', 'b', ' ');
        return double.TryParse(numeric, out var value) ? value : 0;
    }
}
