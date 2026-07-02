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
    private readonly IRecentLaunchService _recentLaunchService;

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
    public ObservableCollection<string> Repositories { get; } = [];

    public SessionLaunchViewModel(ISessionService sessionService, IImageService imageService, IRecentLaunchService recentLaunchService)
    {
        _sessionService = sessionService;
        _imageService = imageService;
        _recentLaunchService = recentLaunchService;
    }

    public async Task LoadImagesAndContextAsync()
    {
        IsLoading = true;
        try
        {
            var rawImages = await _imageService.GetImagesAsync();
            _imagesByTypeAndProject = ImageParser.GroupByTypeAndProject(rawImages);

            var repos = await _imageService.GetRepositoriesAsync();
            Repositories.Clear();
            RepositoryHost = string.Empty;
            foreach (var r in repos) Repositories.Add(r);
            if (Repositories.Count > 0)
                RepositoryHost = Repositories[0];

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
            UpdateHeadlessProjects();
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

    /// <summary>
    /// Pre-select an image in the launch form by its full id (used by the find-by-package "Use this
    /// image" action). Picks a session type the form supports, switches to its project, and selects
    /// the image; cascading type/project changes are applied first, then the exact image is restored.
    /// Falls back to the custom-image URL when the id isn't a launchable catalogue image. Returns true
    /// when it matched a catalogue image.
    /// </summary>
    public bool SelectImageById(string imageId)
    {
        if (string.IsNullOrWhiteSpace(imageId)) return false;

        foreach (var type in _imagesByTypeAndProject.Keys.Where(SessionTypes.Contains))
        {
            foreach (var (project, images) in _imagesByTypeAndProject[type])
            {
                if (images.All(img => img.Id != imageId)) continue;

                UseCustomImage = false;
                SelectedType = type;        // → UpdateProjects (resets SelectedProject)
                SelectedProject = project;  // → UpdateImages (populates Images, picks a default)
                SelectedImage = Images.FirstOrDefault(img => img.Id == imageId)
                                ?? images.First(img => img.Id == imageId);
                return true;
            }
        }

        // Not a launchable catalogue image (e.g. a private image only known to discovery) → custom URL.
        UseCustomImage = true;
        CustomImageUrl = imageId;
        return false;
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

            // Snapshot config before the async call — user may change the form while awaiting
            var recentLaunch = new Models.RecentLaunch
            {
                Name = SessionName,
                Type = SelectedType,
                Image = imageToLaunch,
                ImageLabel = UseCustomImage
                    ? CustomImageUrl
                    : SelectedImage?.Label ?? imageToLaunch,
                Project = UseCustomImage ? "" : SelectedProject,
                ResourceType = ResourceType,
                Cores = Cores,
                Ram = Ram,
                Gpus = Gpus,
                LaunchedAt = DateTime.Now
            };

            var sessionId = await _sessionService.LaunchSessionAsync(launchParams);
            if (sessionId is not null)
            {
                LaunchStatus = "Session launched successfully!";
                LaunchSuccess = true;
                GenerateSessionName();

                _recentLaunchService.Save(recentLaunch);
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

    // ── Headless launch (the "Headless" tab; macOS HeadlessLaunchTabView parity) ──
    // Separate project/image state so configuring a batch job never disturbs the
    // Standard tab's selections; images are scoped to type=headless.

    [ObservableProperty] private string _headlessSessionName = string.Empty;
    [ObservableProperty] private string _headlessSelectedProject = string.Empty;
    [ObservableProperty] private ParsedImage? _headlessSelectedImage;
    [ObservableProperty] private string _headlessCommand = string.Empty;
    [ObservableProperty] private string _headlessArgs = string.Empty;
    [ObservableProperty] private int _headlessReplicas = 1;
    // Own resource-type flag: the tab's RadioButtons must not clobber the shared ResourceType the
    // Standard/Advanced tabs read (their radios wouldn't reflect the change).
    [ObservableProperty] private string _headlessResourceType = "flexible";

    public ObservableCollection<string> HeadlessProjects { get; } = [];
    public ObservableCollection<ParsedImage> HeadlessImages { get; } = [];

    /// <summary>False when the account's catalogue has no type=headless images (shows a hint instead).</summary>
    public bool HasHeadlessImages => HeadlessProjects.Count > 0;

    private void UpdateHeadlessProjects()
    {
        HeadlessProjects.Clear();
        if (_imagesByTypeAndProject.TryGetValue("headless", out var projects))
            foreach (var project in projects.Keys.OrderBy(p => p))
                HeadlessProjects.Add(project);
        OnPropertyChanged(nameof(HasHeadlessImages));
        if (HeadlessProjects.Count > 0)
            HeadlessSelectedProject = HeadlessProjects[0];
        if (string.IsNullOrEmpty(HeadlessSessionName))
            GenerateHeadlessSessionName();
    }

    partial void OnHeadlessSelectedProjectChanged(string value)
    {
        if (!string.IsNullOrEmpty(value)) UpdateHeadlessImages();
    }

    private void UpdateHeadlessImages()
    {
        HeadlessImages.Clear();
        if (!string.IsNullOrEmpty(HeadlessSelectedProject)
            && _imagesByTypeAndProject.TryGetValue("headless", out var projects)
            && projects.TryGetValue(HeadlessSelectedProject, out var images))
        {
            foreach (var img in images) HeadlessImages.Add(img);
        }
        HeadlessSelectedImage = HeadlessImages.FirstOrDefault();
    }

    public void GenerateHeadlessSessionName()
    {
        var count = _sessionCounter?.Invoke("headless") ?? 0;
        HeadlessSessionName = $"headless{count + 1}";
    }

    [RelayCommand]
    private async Task LaunchHeadlessAsync()
    {
        if (HeadlessSelectedImage is null)
        {
            ErrorMessage = "Please select a container image.";
            HasError = true;
            return;
        }
        if (string.IsNullOrWhiteSpace(HeadlessCommand))
        {
            ErrorMessage = "Please provide the command the container should run.";
            HasError = true;
            return;
        }
        if (string.IsNullOrWhiteSpace(HeadlessSessionName))
            GenerateHeadlessSessionName();

        IsLaunching = true;
        LaunchStatus = "Submitting job...";
        HasError = false;

        try
        {
            var replicas = Math.Clamp(HeadlessReplicas, 1, 20);
            var launchParams = new SessionLaunchParams
            {
                Type = "headless",
                Name = HeadlessSessionName,
                Image = HeadlessSelectedImage.Id,
                Cmd = HeadlessCommand.Trim(),
                Args = string.IsNullOrWhiteSpace(HeadlessArgs) ? null : HeadlessArgs.Trim(),
                Replicas = replicas,
                Cores = HeadlessResourceType == "fixed" ? Cores : 0,
                Ram = HeadlessResourceType == "fixed" ? Ram : 0,
                Gpus = HeadlessResourceType == "fixed" ? Gpus : 0,
            };

            var recentLaunch = new Models.RecentLaunch
            {
                Name = HeadlessSessionName,
                Type = "headless",
                Image = HeadlessSelectedImage.Id,
                ImageLabel = HeadlessSelectedImage.Label,
                Project = HeadlessSelectedProject,
                ResourceType = HeadlessResourceType,
                Cores = Cores,
                Ram = Ram,
                Gpus = Gpus,
                Cmd = HeadlessCommand.Trim(),
                Args = string.IsNullOrWhiteSpace(HeadlessArgs) ? null : HeadlessArgs.Trim(),
                Replicas = replicas,
                LaunchedAt = DateTime.Now,
            };

            var ids = await _sessionService.LaunchHeadlessAsync(launchParams);
            if (ids.Count > 0)
            {
                LaunchStatus = ids.Count > 1
                    ? $"Launched {ids.Count} replicas."
                    : "Job launched successfully!";
                LaunchSuccess = true;
                _recentLaunchService.Save(recentLaunch);
                GenerateHeadlessSessionName();
            }
            else
            {
                LaunchStatus = "Failed to launch job.";
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

    public async Task<bool> RelaunchAsync(Models.RecentLaunch launch)
    {
        // Headless jobs aren't bound by the interactive-session cap (matches the launch tab).
        if (launch.Type != "headless")
        {
            UpdateSessionLimit();
            if (IsAtSessionLimit)
            {
                ErrorMessage = SessionLimitMessage;
                HasError = true;
                return false;
            }
        }

        IsLaunching = true;
        LaunchStatus = "Requesting session...";
        HasError = false;
        LaunchSuccess = false;

        try
        {
            var launchParams = new SessionLaunchParams
            {
                Type = launch.Type,
                Name = launch.Name,
                Image = launch.Image,
                Cores = launch.ResourceType == "fixed" ? launch.Cores : 0,
                Ram = launch.ResourceType == "fixed" ? launch.Ram : 0,
                Gpus = launch.ResourceType == "fixed" ? launch.Gpus : 0,
                Cmd = launch.Cmd,
                Args = launch.Args,
                Replicas = Math.Max(1, launch.Replicas),
            };

            var recentEntry = new Models.RecentLaunch
            {
                Name = launch.Name,
                Type = launch.Type,
                Image = launch.Image,
                ImageLabel = launch.ImageLabel,
                Project = launch.Project,
                ResourceType = launch.ResourceType,
                Cores = launch.Cores,
                Ram = launch.Ram,
                Gpus = launch.Gpus,
                Cmd = launch.Cmd,
                Args = launch.Args,
                Replicas = launch.Replicas,
                LaunchedAt = DateTime.Now
            };

            // Batch jobs relaunch through the headless endpoint with their saved command line;
            // routing them through the interactive call would drop cmd/args and mis-count them
            // against the interactive session cap.
            var launched = launch.Type == "headless"
                ? (await _sessionService.LaunchHeadlessAsync(launchParams)).Count > 0
                : await _sessionService.LaunchSessionAsync(launchParams) is not null;

            if (launched)
            {
                LaunchStatus = launch.Type == "headless" ? "Job launched successfully!" : "Session launched successfully!";
                LaunchSuccess = true;
                _recentLaunchService.Save(recentEntry);
                return true;
            }

            LaunchStatus = "Failed to launch session.";
            HasError = true;
            return false;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            LaunchStatus = "Launch failed.";
            HasError = true;
            return false;
        }
        finally
        {
            IsLaunching = false;
        }
    }

    public void ApplyRecentLaunch(Models.RecentLaunch launch)
    {
        SelectedType = launch.Type;
        ResourceType = launch.ResourceType;
        Cores = launch.Cores;
        Ram = launch.Ram;
        Gpus = launch.Gpus;

        // Try saved project first, then scan all projects
        UseCustomImage = false;
        if (_imagesByTypeAndProject.TryGetValue(launch.Type, out var projects))
        {
            // Prefer the saved project
            if (!string.IsNullOrEmpty(launch.Project)
                && projects.TryGetValue(launch.Project, out var projectImages))
            {
                var match = projectImages.FirstOrDefault(img => img.Id == launch.Image);
                if (match is not null)
                {
                    SelectedProject = launch.Project;
                    SelectedImage = match;
                    GenerateSessionName();
                    return;
                }
            }

            // Fall back to scanning all projects
            foreach (var (project, images) in projects)
            {
                var match = images.FirstOrDefault(img => img.Id == launch.Image);
                if (match is not null)
                {
                    SelectedProject = project;
                    SelectedImage = match;
                    GenerateSessionName();
                    return;
                }
            }
        }

        // Image not found in standard list — use as custom image
        UseCustomImage = true;
        CustomImageUrl = launch.Image;
        GenerateSessionName();
    }
}
