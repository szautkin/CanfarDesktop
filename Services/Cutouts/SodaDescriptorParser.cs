using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using CanfarDesktop.Models.Cutouts;

namespace CanfarDesktop.Services.Cutouts;

/// <summary>
/// The cutout services a DataLink answer describes: one <c>RESOURCE type="meta" utype="adhoc:service"</c>
/// per file, SODA's endpoint among its PARAMs and the file's own parameters — with their limits — in
/// its <c>inputParams</c> GROUP.
///
/// <para>Apart from the row parsing in <see cref="DataLinkService"/>, because it is a different job:
/// rows are a table, a descriptor is a small document, and this reads it as one. Only the synchronous
/// service is taken — the app runs cutouts as ordinary downloads — and only over https, the same rule
/// every DataLink URL is held to.</para>
/// </summary>
public static class SodaDescriptorParser
{
    private const string SyncStandard = "ivo://ivoa.net/std/SODA#sync";

    public static IReadOnlyList<SodaDescriptor> Parse(string votableXml)
    {
        XDocument doc;
        try
        {
            // No DTDs and no resolver: the answer comes off the network.
            using var reader = XmlReader.Create(new StringReader(votableXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            doc = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return [];
        }

        var found = new List<SodaDescriptor>();
        foreach (var resource in doc.Descendants().Where(e => e.Name.LocalName == "RESOURCE"
                     && Attr(e, "type") == "meta" && Attr(e, "utype") == "adhoc:service"))
        {
            if (Read(resource) is { } descriptor) found.Add(descriptor);
        }
        return found;
    }

    private static SodaDescriptor? Read(XElement resource)
    {
        // First of a name wins: a malformed answer repeating one must not throw the whole parse away.
        var meta = resource.Elements().Where(e => e.Name.LocalName == "PARAM")
            .GroupBy(p => Attr(p, "name") ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => Attr(g.First(), "value") ?? "", StringComparer.OrdinalIgnoreCase);

        if (!meta.TryGetValue("standardID", out var standard)
            || !standard.StartsWith(SyncStandard, StringComparison.OrdinalIgnoreCase)) return null;
        if (!meta.TryGetValue("accessURL", out var url) || !DataLinkService.IsHttpsUrl(url)) return null;

        var inputs = resource.Elements().FirstOrDefault(e => e.Name.LocalName == "GROUP" && Attr(e, "name") == "inputParams");
        if (inputs is null) return null;

        var parameters = inputs.Elements().Where(e => e.Name.LocalName == "PARAM")
            .GroupBy(p => (Attr(p, "name") ?? "").ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First());
        if (!parameters.TryGetValue("ID", out var id) || string.IsNullOrWhiteSpace(Attr(id, "value"))) return null;

        var circle = parameters.TryGetValue("CIRCLE", out var c) ? CircleFrom(Numbers(Limit(c, "MAX"))) : null;
        var polygon = parameters.TryGetValue("POLYGON", out var p) ? PolygonFrom(Numbers(Limit(p, "MAX"))) : null;
        var (bandMin, bandMax) = parameters.TryGetValue("BAND", out var b) ? Interval(b) : (null, null);
        var (timeMin, timeMax) = parameters.TryGetValue("TIME", out var t) ? Interval(t) : (null, null);
        var pol = parameters.TryGetValue("POL", out var pp)
            ? pp.Descendants().Where(e => e.Name.LocalName == "OPTION")
                .Select(o => Attr(o, "value") ?? "").Where(v => v.Length > 0).ToList()
            : [];

        return new SodaDescriptor
        {
            AccessUrl = url,
            ArtifactId = Attr(id, "value")!,
            Parameters = parameters.Keys.Where(k => k.Length > 0).ToHashSet(),
            Footprint = polygon ?? circle,
            BoundingCircle = circle,
            BandMin = bandMin,
            BandMax = bandMax,
            TimeMin = timeMin,
            TimeMax = timeMax,
            PolStates = pol,
        };
    }

    private static string? Attr(XElement e, string name) => e.Attribute(name)?.Value;

    /// <summary>A PARAM's MIN or MAX value, from its VALUES child.</summary>
    private static string? Limit(XElement param, string which)
        => param.Descendants().FirstOrDefault(e => e.Name.LocalName == which) is { } limit ? Attr(limit, "value") : null;

    /// <summary>
    /// An interval's limits. CADC gives BAND as MIN and MAX, one number each; an interval written whole
    /// into MAX ("lo hi") is read too, as the SODA text allows either.
    /// </summary>
    private static (double? Min, double? Max) Interval(XElement param)
    {
        var min = Numbers(Limit(param, "MIN"));
        var max = Numbers(Limit(param, "MAX"));
        if (max.Count >= 2 && min.Count == 0) return (max[0], max[1]);
        return (min.Count > 0 ? min[0] : null, max.Count > 0 ? max[^1] : null);
    }

    private static List<double> Numbers(string? text)
    {
        var numbers = new List<double>();
        foreach (var part in (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v))
                return [];
            numbers.Add(v);
        }
        return numbers;
    }

    private static SkyRegion? CircleFrom(IReadOnlyList<double> n)
        => n.Count == 3 ? SkyRegion.Circle(n[0], n[1], n[2]) : null;

    private static SkyRegion? PolygonFrom(IReadOnlyList<double> n)
        => n.Count >= 6 && n.Count % 2 == 0
            ? SkyRegion.Polygon(Enumerable.Range(0, n.Count / 2).Select(i => new SkyPoint(n[2 * i], n[2 * i + 1])))
            : null;
}
