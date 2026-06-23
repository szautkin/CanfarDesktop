using System.Text;

namespace CanfarDesktop.Models.ImageDiscovery;

/// <summary>One installed system/R package (name + version).</summary>
public record ImagePackage(string Name, string Version);

/// <summary>One Python package, with its source (pip/conda/system) and conda env.</summary>
public record PythonPackage(string Name, string Version, string Source, string Env);

/// <summary>A conda environment with its own pip snapshot.</summary>
public record CondaEnv(string Name, string Prefix, IReadOnlyList<PythonPackage> Packages);

/// <summary>
/// Structured snapshot of what's installed inside a Skaha container image, produced by the
/// in-container probe and parsed by <c>ManifestParser</c>. Optional fields default so manifests
/// written by older/newer probe versions still deserialize (forward/backward compatible).
/// </summary>
public record ImageManifest
{
    public int SchemaVersion { get; init; } = 1;
    public string ImageID { get; init; } = string.Empty;
    public string ContentHash { get; init; } = "sha256:none";
    public DateTimeOffset CapturedAt { get; init; }
    public string OsFamily { get; init; } = "unknown";
    public string OsVersion { get; init; } = "unknown";
    public string Kernel { get; init; } = "unknown";

    public IReadOnlyList<ImagePackage> DpkgPackages { get; init; } = [];
    public IReadOnlyList<ImagePackage> RpmPackages { get; init; } = [];
    public IReadOnlyList<ImagePackage> ApkPackages { get; init; } = [];
    public IReadOnlyList<PythonPackage> PythonPackages { get; init; } = [];
    public IReadOnlyList<ImagePackage> RPackages { get; init; } = [];
    public IReadOnlyList<CondaEnv> CondaEnvs { get; init; } = [];

    public string? ProbeNotes { get; init; }
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public string PythonVersion { get; init; } = "unknown";
    public string OsRelease { get; init; } = "unknown";
    public IReadOnlyList<string> Shells { get; init; } = [];

    /// <summary>
    /// Convert an image id like <c>images.canfar.net/skaha/astroml:24.07</c> into a filesystem-safe
    /// stub (<c>images.canfar.net_skaha_astroml_24.07</c>) for cache filenames.
    /// </summary>
    public static string Sanitize(string imageID)
    {
        var sb = new StringBuilder(imageID.Length);
        foreach (var ch in imageID)
            sb.Append(ch is '/' or ':' or '\\' or '?' or '*' or '<' or '>' or '|' or '"' ? '_' : ch);
        return sb.ToString();
    }

    /// <summary>Empty manifest with explanatory notes (parser failure-case template).</summary>
    public static ImageManifest Empty(string imageID, string notes) => new()
    {
        ImageID = imageID,
        ProbeNotes = notes,
    };
}

/// <summary>Canonical behavioural capability keys the probe tests for.</summary>
public static class ImageCapability
{
    public const string Fitsio = "fitsio";
    public const string PhotutilsIterativePsf = "photutils-iterative-psf";
    public const string Gpu = "gpu";
    public const string Python3 = "python3";
    public const string Conda = "conda";
    public const string Rscript = "rscript";

    public static readonly string[] All = { Fitsio, PhotutilsIterativePsf, Gpu, Python3, Conda, Rscript };
}
