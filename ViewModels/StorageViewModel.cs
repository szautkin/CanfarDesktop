using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CanfarDesktop.Services;

namespace CanfarDesktop.ViewModels;

public partial class StorageViewModel : ObservableObject
{
    private readonly IStorageService _storageService;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private double _usedGB;

    [ObservableProperty]
    private double _quotaGB;

    [ObservableProperty]
    private double _usagePercent;

    [ObservableProperty]
    private bool _isWarning;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _hasData;

    public StorageViewModel(IStorageService storageService)
    {
        _storageService = storageService;
    }

    [RelayCommand]
    public async Task LoadQuotaAsync(string username)
    {
        if (string.IsNullOrEmpty(username)) return;

        IsLoading = true;
        HasError = false;

        try
        {
            var quota = await _storageService.GetQuotaAsync(username);
            if (quota is not null)
            {
                UsedGB = Math.Round(quota.UsedGB, 2);
                QuotaGB = Math.Round(quota.QuotaGB, 2);
                UsagePercent = Math.Round(quota.UsagePercent, 1);
                IsWarning = UsagePercent > 90;
                HasData = true;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load storage: {ex.Message}";
            HasError = true;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
