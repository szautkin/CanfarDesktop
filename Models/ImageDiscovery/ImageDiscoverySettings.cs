using System.Text;

namespace CanfarDesktop.Models.ImageDiscovery;

/// <summary>
/// User-configurable defaults for image discovery:
///   • <see cref="InspectorImage"/> — the headless host image the syft inspector runs in
///     (must ship bash + python3 + curl/wget and be pullable for the user's Skaha account);
///   • <see cref="RegistryHost"/> + <see cref="Username"/> + a secret (stored in PasswordVault,
///     never in this record) — used to mint the <c>x-skaha-registry-auth</c> header so Skaha can
///     pull private-namespace images.
/// </summary>
public record ImageDiscoverySettings
{
    public const string DefaultInspectorImage = "images.canfar.net/skaha/terminal:1.1.2";
    public const string DefaultRegistryHost = "images.canfar.net";

    public string RegistryHost { get; init; } = DefaultRegistryHost;
    /// <summary>Registry repository/project (e.g. "skaha"); used to prefix a short inspector image name.</summary>
    public string RegistryRepository { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    /// <summary>True when a secret is stored for the current (host, username) — the value lives in PasswordVault.</summary>
    public bool HasSecret { get; init; }
    public string InspectorImage { get; init; } = DefaultInspectorImage;

    /// <summary>True when nothing user-configured is meaningfully set (UI shows/hides the Reset affordance).</summary>
    public bool IsAllDefaults =>
        string.IsNullOrEmpty(Username)
        && !HasSecret
        && InspectorImage == DefaultInspectorImage
        && string.IsNullOrEmpty(RegistryRepository)
        && (RegistryHost == DefaultRegistryHost || RegistryHost.Length == 0);

    /// <summary>Build the <c>x-skaha-registry-auth</c> value: base64(username:secret).</summary>
    public static string BuildAuthHeader(string username, string secret)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{secret}"));
}
