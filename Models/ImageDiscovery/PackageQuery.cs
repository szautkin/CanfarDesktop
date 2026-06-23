namespace CanfarDesktop.Models.ImageDiscovery;

/// <summary>
/// Search criteria for finding images that contain a set of required packages/capabilities.
/// Name-only, intersection: an image matches when its manifest contains every name in every
/// populated constraint set. <see cref="Score"/> additionally reports partial coverage for
/// near-miss images.
/// </summary>
public class PackageQuery
{
    public HashSet<string> OsFamilies { get; init; } = new();
    public HashSet<string> OsVersions { get; init; } = new();
    public HashSet<string> Dpkg { get; init; } = new();
    public HashSet<string> Rpm { get; init; } = new();
    public HashSet<string> Apk { get; init; } = new();
    public HashSet<string> Python { get; init; } = new();
    public HashSet<string> R { get; init; } = new();
    public HashSet<string> Capabilities { get; init; } = new();

    public bool IsEmpty =>
        OsFamilies.Count == 0 && OsVersions.Count == 0 && Dpkg.Count == 0 && Rpm.Count == 0
        && Apk.Count == 0 && Python.Count == 0 && R.Count == 0 && Capabilities.Count == 0;

    /// <summary>True when the manifest satisfies every populated constraint.</summary>
    public bool Matches(ImageManifest m)
    {
        if (OsFamilies.Count > 0 && !OsFamilies.Contains(m.OsFamily)) return false;
        if (OsVersions.Count > 0 && !OsVersions.Contains(m.OsVersion)) return false;
        if (Dpkg.Count > 0 && !Dpkg.IsSubsetOf(m.DpkgPackages.Select(p => p.Name))) return false;
        if (Rpm.Count > 0 && !Rpm.IsSubsetOf(m.RpmPackages.Select(p => p.Name))) return false;
        if (Apk.Count > 0 && !Apk.IsSubsetOf(m.ApkPackages.Select(p => p.Name))) return false;
        if (Python.Count > 0 && !Python.IsSubsetOf(m.PythonPackages.Select(p => p.Name))) return false;
        if (Capabilities.Count > 0 && !Capabilities.IsSubsetOf(m.Capabilities)) return false;
        if (R.Count > 0 && !R.IsSubsetOf(m.RPackages.Select(p => p.Name))) return false;
        return true;
    }

    /// <summary>
    /// Fraction (0..1) of individual constraints the manifest satisfies, plus the identifiers of
    /// the unsatisfied ones. An empty query trivially scores 1.0.
    /// </summary>
    public (double Score, IReadOnlyList<string> Missing) Score(ImageManifest m)
    {
        var satisfied = 0;
        var total = 0;
        var missing = new List<string>();

        if (OsFamilies.Count > 0)
        {
            total++;
            if (OsFamilies.Contains(m.OsFamily)) satisfied++; else missing.Add("osFamily");
        }
        if (OsVersions.Count > 0)
        {
            total++;
            if (OsVersions.Contains(m.OsVersion)) satisfied++; else missing.Add("osVersion");
        }

        void ScoreSet(HashSet<string> requested, IEnumerable<string> available, string label)
        {
            var have = available.ToHashSet();
            total += requested.Count;
            foreach (var name in requested)
            {
                if (have.Contains(name)) satisfied++;
                else missing.Add($"{label}:{name}");
            }
        }

        ScoreSet(Dpkg, m.DpkgPackages.Select(p => p.Name), "dpkg");
        ScoreSet(Rpm, m.RpmPackages.Select(p => p.Name), "rpm");
        ScoreSet(Apk, m.ApkPackages.Select(p => p.Name), "apk");
        ScoreSet(Python, m.PythonPackages.Select(p => p.Name), "python");
        ScoreSet(R, m.RPackages.Select(p => p.Name), "r");
        ScoreSet(Capabilities, m.Capabilities, "capability");

        return total == 0 ? (1.0, Array.Empty<string>()) : ((double)satisfied / total, missing);
    }
}
