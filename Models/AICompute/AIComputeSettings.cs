namespace CanfarDesktop.Models.AICompute;

/// <summary>
/// User-configurable settings for the agent <c>run_code</c> tool: the compute container image (an empty
/// image DISABLES run_code), the instance size, and registry credentials to pull a private compute image
/// (secret stored in the Windows PasswordVault under a SEPARATE resource from Image Discovery). Mirrors
/// the macOS AIComputeSettings.
/// </summary>
public record AIComputeSettings
{
    public const string DefaultRegistryHost = "images.canfar.net";

    /// <summary>The compute container image (a <c>verbinal-execution</c> watcher). Empty ⇒ run_code disabled.</summary>
    public string Image { get; init; } = string.Empty;
    public int Cores { get; init; } = 1;
    public int Ram { get; init; } = 1;
    public string RegistryHost { get; init; } = DefaultRegistryHost;
    /// <summary>Registry repository/project (e.g. "project"); used to prefix a short compute image name.</summary>
    public string RegistryRepository { get; init; } = string.Empty;
    public string RegistryUsername { get; init; } = string.Empty;
    /// <summary>True when a secret is stored for the current (host, username) — value lives in PasswordVault.</summary>
    public bool HasSecret { get; init; }

    /// <summary>run_code/start_compute are enabled only when a compute image is configured.</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(Image);

    /// <summary>True when nothing user-configured is set (UI shows/hides the Reset affordance).</summary>
    public bool IsAllDefaults =>
        string.IsNullOrEmpty(Image)
        && Cores == 1 && Ram == 1
        && string.IsNullOrEmpty(RegistryUsername)
        && string.IsNullOrEmpty(RegistryRepository)
        && !HasSecret
        && (RegistryHost == DefaultRegistryHost || RegistryHost.Length == 0);
}
