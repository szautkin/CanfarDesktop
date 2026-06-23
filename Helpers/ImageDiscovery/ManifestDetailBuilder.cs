using System.Text.Json;
using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Helpers.ImageDiscovery;

/// <summary>One name+version row in a manifest-detail ecosystem section.</summary>
public record PackageLine(string Name, string Version);

/// <summary>One collapsible ecosystem section in the manifest-detail panel (Python/Conda/R/dpkg/…).</summary>
public record EcosystemSection(string Title, int Count, IReadOnlyList<PackageLine> Packages);

/// <summary>The fully-built per-image detail (mirrors the macOS ManifestDetailSheet layout).</summary>
public record ManifestDetail(
    string OsLine,
    string KernelLine,
    string PythonVersion,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<EcosystemSection> Sections,
    string? ProbeNotes);

/// <summary>
/// Turns an <see cref="ImageManifest"/> into ordered, collapsible ecosystem sections for the inline
/// detail panel: Python (grouped by env, system first), then each Conda env, then R / apt-dpkg / rpm
/// / apk. Pure + unit-testable. 1-to-1 with the macOS ManifestDetailSheet section order.
/// </summary>
public static class ManifestDetailBuilder
{
    public static ManifestDetail Build(ImageManifest m)
    {
        var sections = new List<EcosystemSection>();

        // Python, grouped by env — the system interpreter first, then named envs alphabetically.
        foreach (var grp in m.PythonPackages
                     .GroupBy(p => p.Env)
                     .OrderBy(g => string.IsNullOrEmpty(g.Key) || g.Key == "system" ? string.Empty : g.Key, StringComparer.Ordinal))
        {
            var env = grp.Key;
            var title = string.IsNullOrEmpty(env) || env == "system" ? "Python" : $"Python · {env}";
            sections.Add(new EcosystemSection(title, grp.Count(), Lines(grp.Select(p => new PackageLine(p.Name, p.Version)))));
        }

        foreach (var env in m.CondaEnvs)
            sections.Add(new EcosystemSection($"Conda · {env.Name}", env.Packages.Count,
                Lines(env.Packages.Select(p => new PackageLine(p.Name, p.Version)))));

        AddPkgSection(sections, "R", m.RPackages);
        AddPkgSection(sections, "System (apt / dpkg)", m.DpkgPackages);
        AddPkgSection(sections, "System (rpm)", m.RpmPackages);
        AddPkgSection(sections, "System (apk)", m.ApkPackages);

        var osLine = $"{m.OsFamily} {m.OsVersion}".Trim();
        var kernelLine = m.Kernel == "unknown" ? string.Empty : m.Kernel;
        return new ManifestDetail(osLine, kernelLine, m.PythonVersion, m.Capabilities, sections, m.ProbeNotes);
    }

    /// <summary>Pretty JSON of the manifest (matches the on-disk cache shape) for "Copy as JSON".</summary>
    public static string ToJson(ImageManifest m)
        => JsonSerializer.Serialize(m, new JsonSerializerOptions { WriteIndented = true });

    private static void AddPkgSection(List<EcosystemSection> sections, string title, IReadOnlyList<ImagePackage> pkgs)
    {
        if (pkgs.Count == 0) return;
        sections.Add(new EcosystemSection(title, pkgs.Count, Lines(pkgs.Select(p => new PackageLine(p.Name, p.Version)))));
    }

    private static IReadOnlyList<PackageLine> Lines(IEnumerable<PackageLine> lines)
        => lines.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
}
