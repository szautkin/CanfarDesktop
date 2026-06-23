using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Helpers.ImageDiscovery;

/// <summary>
/// Pure faceting helpers for the image-discovery filter UI (mirrors the macOS
/// <c>ImageDiscoveryModel.availableValues(for:)</c> + type ordering). No WinUI dependency, so the
/// filter logic is unit-testable headlessly.
/// </summary>
public static class FacetEngine
{
    /// <summary>
    /// The values in <paramref name="category"/> that would still yield ≥1 result given the OTHER
    /// active constraints in <paramref name="query"/>. Drives checkbox enable/disable: a value not
    /// in this set would collapse the results to empty, so the UI greys it out (unless already
    /// ticked). Faceting is computed over the <paramref name="discovered"/> manifests — NOT the full
    /// catalogue — so Capabilities (absent from <c>AllPackages</c>) are still facetable.
    /// </summary>
    public static IReadOnlyCollection<string> AvailableValues(
        PackageQuery query, PackageQuery.Category category, IEnumerable<ImageManifest> discovered)
    {
        var scoped = query.Dropping(category);
        var result = new HashSet<string>();
        foreach (var m in discovered)
        {
            if (!scoped.Matches(m)) continue;
            switch (category)
            {
                case PackageQuery.Category.OsFamily:
                    if (m.OsFamily != "unknown") result.Add(m.OsFamily);
                    break;
                case PackageQuery.Category.OsVersion:
                    if (m.OsVersion != "unknown") result.Add(m.OsVersion);
                    break;
                case PackageQuery.Category.Python:
                    foreach (var p in m.PythonPackages) result.Add(p.Name);
                    break;
                case PackageQuery.Category.R:
                    foreach (var p in m.RPackages) result.Add(p.Name);
                    break;
                case PackageQuery.Category.Dpkg:
                    foreach (var p in m.DpkgPackages) result.Add(p.Name);
                    break;
                case PackageQuery.Category.Rpm:
                    foreach (var p in m.RpmPackages) result.Add(p.Name);
                    break;
                case PackageQuery.Category.Apk:
                    foreach (var p in m.ApkPackages) result.Add(p.Name);
                    break;
                case PackageQuery.Category.Capabilities:
                    foreach (var c in m.Capabilities) result.Add(c);
                    break;
            }
        }
        return result;
    }

    // The order the macOS session-type picker presents known types, before any extras.
    private static readonly string[] CanonicalTypeOrder =
        { "notebook", "desktop", "carta", "headless", "contributed", "firefly", "desktop-app" };

    /// <summary>
    /// Session types in the macOS canonical order (the ones present, in canonical order), with any
    /// unknown extras appended alphabetically. Mirrors <c>ImageDiscoveryModel.availableTypes</c>.
    /// </summary>
    public static IReadOnlyList<string> OrderedTypes(IEnumerable<string> present)
    {
        var set = new HashSet<string>(present);
        var ordered = new List<string>();
        foreach (var t in CanonicalTypeOrder)
            if (set.Remove(t)) ordered.Add(t);
        ordered.AddRange(set.OrderBy(x => x, StringComparer.Ordinal));
        return ordered;
    }
}
