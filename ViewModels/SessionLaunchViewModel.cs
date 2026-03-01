using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Services;

namespace CanfarDesktop.ViewModels;

public partial class SessionLaunchViewModel : ObservableObject
{
    private readonly ISessionService _sessionService;
    private readonly IImageService _imageService;

    private Dictionary<string, Dictionary<string, List<ParsedImage>>> _imagesByTypeAndProject = new();

    // Default images per type (from opencadc/science-portal constants.js)
    private static readonly Dictionary<string, string> DefaultImageNames = new()
    {
        ["notebook"] = "astroml:latest",
        ["desktop"] = "desktop:latest",
        ["carta"] = "carta:latest",
        ["contributed"] = "astroml-vscode:latest",
        ["firefly"] = "firefly:2025.2"
    };

    [ObservableProperty]
    private string _selectedType = "notebook";

    [ObservableProperty]
    private string _selectedProject = string.Empty;

    [ObservableProperty]
    private ParsedImage? _selectedImage;

    [ObservableProperty]
    private string _sessionName = string.Empty;

    [ObservableProperty]
    private string _resourceType = "flexible";

    [ObservableProperty]
    private int _cores = 2;

    [ObservableProperty]
    private int _ram = 8;

    [ObservableProperty]
    private int _gpus;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLaunching;

    [ObservableProperty]
    private string _launchStatus = string.Empty;

    [ObservableProperty]
    private bool _launchSuccess;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    // Advanced launch fields
    [ObservableProperty]
    private string _customImageUrl = string.Empty;

    [ObservableProperty]
    private string _repositoryHost = "images.canfar.net";

    [ObservableProperty]
    private string _repositoryUsername = string.Empty;

    [ObservableProperty]
    private string _repositorySecret = string.Empty;

    [ObservableProperty]
    private bool _useCustomImage;

    [ObservableProperty]
    private bool _isAtSessionLimit;

    [ObservableProperty]
    private string _sessionLimitMessage = string.Empty;

    private const int MaxConcurrentSessions = 3;
    private Func<string, int>? _sessionCounter;
    private Func<int>? _totalSessionCounter;

    public ObservableCollection<string> SessionTypes { get; } =
        ["notebook", "desktop", "carta", "contributed", "firefly"];

    public ObservableCollection<string> Projects { get; } = [];
    public ObservableCollection<ParsedImage> Images { get; } = [];
    public ObservableCollection<int> CoreOptions { get; } = [];
    public ObservableCollection<int> RamOptions { get; } = [];
    public ObservableCollection<int> GpuOptions { get; } = [];

    public SessionLaunchViewModel(ISessionService sessionService, IImageService imageService)
    {
        _sessionService = sessionService;
        _imageService = imageService;
    }

    public async Task LoadImagesAndContextAsync()
    {
        IsLoading = true;
        try
        {
            var rawImages = await _imageService.GetImagesAsync();
            _imagesByTypeAndProject = ImageParser.GroupByTypeAndProject(rawImages);

            var context = await _imageService.GetContextAsync();
            if (context is not null)
            {
                CoreOptions.Clear();
                foreach (var c in context.Cores.Options) CoreOptions.Add(c);
                Cores = context.Cores.Default;

                RamOptions.Clear();
                foreach (var r in context.MemoryGB.Options) RamOptions.Add(r);
                Ram = context.MemoryGB.Default;

                GpuOptions.Clear();
                if (!context.Gpus.Options.Contains(0))
                    GpuOptions.Add(0);
                foreach (var g in context.Gpus.Options) GpuOptions.Add(g);
            }

            UpdateProjects();
            GenerateSessionName();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load images: {ex.Message}";
            HasError = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedTypeChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            UpdateProjects();
            GenerateSessionName();
        }
    }

    partial void OnSelectedProjectChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
            UpdateImages();
    }

    private void UpdateProjects()
    {
        Projects.Clear();
        if (_imagesByTypeAndProject.TryGetValue(SelectedType, out var projects))
        {
            foreach (var project in projects.Keys.OrderBy(p => p))
                Projects.Add(project);
        }
        if (Projects.Count > 0)
        {
            // Prefer the project that contains the default image for this type
            var defaultProject = FindProjectWithDefaultImage();
            SelectedProject = defaultProject ?? Projects[0];
        }
    }

    private string? FindProjectWithDefaultImage()
    {
        if (!DefaultImageNames.TryGetValue(SelectedType, out var defaultName))
            return null;
        if (!_imagesByTypeAndProject.TryGetValue(SelectedType, out var projects))
            return null;

        foreach (var (project, images) in projects)
        {
            if (images.Any(img =>
                img.Id.EndsWith(defaultName, StringComparison.OrdinalIgnoreCase) ||
                img.Label.Equals(defaultName, StringComparison.OrdinalIgnoreCase)))
                return project;
        }
        return null;
    }

    private void UpdateImages()
    {
        Images.Clear();
        if (string.IsNullOrEmpty(SelectedProject))
        {
            SelectedImage = null;
            return;
        }
        if (_imagesByTypeAndProject.TryGetValue(SelectedType, out var projects)
            && projects.TryGetValue(SelectedProject, out var images))
        {
            foreach (var img in images)
                Images.Add(img);
        }

        // Try to select the default image for this type
        SelectedImage = TrySelectDefaultImage() ?? Images.FirstOrDefault();
    }

    private ParsedImage? TrySelectDefaultImage()
    {
        if (!DefaultImageNames.TryGetValue(SelectedType, out var defaultName))
            return null;

        // Exact label match first, then partial
        return Images.FirstOrDefault(img =>
                img.Label.Equals(defaultName, StringComparison.OrdinalIgnoreCase))
            ?? Images.FirstOrDefault(img =>
                img.Id.EndsWith(defaultName, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private async Task LaunchAsync()
    {
        UpdateSessionLimit();
        if (IsAtSessionLimit)
        {
            ErrorMessage = SessionLimitMessage;
            HasError = true;
            return;
        }

        string imageToLaunch;

        if (UseCustomImage)
        {
            if (string.IsNullOrWhiteSpace(CustomImageUrl))
            {
                ErrorMessage = "Please provide a container image URL.";
                HasError = true;
                return;
            }
            imageToLaunch = $"{RepositoryHost}/{CustomImageUrl}";
        }
        else
        {
            if (SelectedImage is null)
            {
                ErrorMessage = "Please select a container image.";
                HasError = true;
                return;
            }
            imageToLaunch = SelectedImage.Id;
        }

        if (string.IsNullOrWhiteSpace(SessionName))
        {
            ErrorMessage = "Please provide a session name.";
            HasError = true;
            return;
        }

        IsLaunching = true;
        LaunchStatus = "Requesting session...";
        HasError = false;

        try
        {
            var launchParams = new SessionLaunchParams
            {
                Type = SelectedType,
                Name = SessionName,
                Image = imageToLaunch,
                Cores = ResourceType == "fixed" ? Cores : 0,
                Ram = ResourceType == "fixed" ? Ram : 0,
                Gpus = ResourceType == "fixed" ? Gpus : 0,
                RegistryUsername = UseCustomImage ? RepositoryUsername : null,
                RegistrySecret = UseCustomImage ? RepositorySecret : null
            };

            var sessionId = await _sessionService.LaunchSessionAsync(launchParams);
            if (sessionId is not null)
            {
                LaunchStatus = "Session launched successfully!";
                LaunchSuccess = true;
                GenerateSessionName();
            }
            else
            {
                LaunchStatus = "Failed to launch session.";
                HasError = true;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            LaunchStatus = "Launch failed.";
            HasError = true;
        }
        finally
        {
            IsLaunching = false;
        }
    }

    public void SetSessionCounter(Func<string, int> counter)
    {
        _sessionCounter = counter;
    }

    public void SetTotalSessionCounter(Func<int> counter)
    {
        _totalSessionCounter = counter;
    }

    public void UpdateSessionLimit()
    {
        var count = _totalSessionCounter?.Invoke() ?? 0;
        IsAtSessionLimit = count >= MaxConcurrentSessions;
        SessionLimitMessage = IsAtSessionLimit
            ? $"Session limit reached ({count}/{MaxConcurrentSessions}). Delete a session to launch a new one."
            : string.Empty;
    }

    public void GenerateSessionName()
    {
        var count = _sessionCounter?.Invoke(SelectedType) ?? 0;
        SessionName = $"{SelectedType}{count + 1}";
    }
}
