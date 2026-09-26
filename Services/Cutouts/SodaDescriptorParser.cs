using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using CanfarDesktop.Models.Cutouts;

namespace CanfarDesktop.Services.Cutouts;

/// <summary>The cutout services a DataLink answer describes, and why any it describes were not read.</summary>
public sealed record SodaParse(IReadOnlyList<SodaDescriptor> Descriptors, IReadOnlyList<string> PassedOver);

/// <summary>
/// The cutout services a DataLink answer describes: one <c>RESOURCE type="meta" utype="adhoc:service"</c>
/// per file, SODA's endpoint among its PARAMs and the file's own parameters — with their limits — in
/// its <c>inputParams</c> GROUP.
///
/// <para>Apart from the row parsing in <see cref="DataLinkService"/>, because it is a different job:
/// rows are a table, a descriptor is a small document, and this reads it as one. Only the synchronous
/// service is taken — the app runs cutouts as ordinary downloads — and only over https, the same rule
/// every DataLink URL is held to.</para>
///
/// <para>A descriptor passed over is passed over with a reason (<see cref="SodaParse.PassedOver"/>). They
/// were dropped without one, so an answer the parser could not read looked exactly like CADC offering no
/// cutouts at all — which is what every observation said for a while (QA D8), with nothing to tell which.</para>
/// </summary>
public static class SodaDescriptorParser
{
    private const string SyncStandard = "ivo://ivoa.net/std/SODA#sync";

    public static IReadOnlyList<SodaDescriptor> Parse(string votableXml) => ParseWithReasons(votableXml).Descriptors;

    /// <summary>The cutout services read, and why each descriptor that was not read was passed over.</summary>
    public static SodaParse ParseWithReasons(string votableXml)
    {
        XDocument doc;
        try
        {
            // No DTDs and no resolver: the answer comes off the network.
            using var reader = XmlReader.Create(new StringReader(votableXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            doc = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            return new SodaParse([], [$"the DataLink answer is not well-formed XML: {ex.Message}"]);
        }

        var found = new List<SodaDescriptor>();
        var passedOver = new List<string>();
        var others = new List<string>();
        var services = doc.Descendants().Where(e => e.Name.LocalName == "RESOURCE"
                                                    && Attr(e, "type") == "meta" && Attr(e, "utype") == "adhoc:service").ToList();
        foreach (var resource in services)
        {
            var meta = Meta(resource);
            // Other services — SODA's asynchronous one, listed beside it — are not problems; they are
            // only worth naming when no synchronous one is there.
            if (!meta.TryGetValue("standardID", out var standard) || !standard.StartsWith(SyncStandard, StringComparison.OrdinalIgnoreCase))
            {
                others.Add(string.IsNullOrEmpty(standard) ? "one naming no standardID" : standard);
                continue;
            }

            var (descriptor, why) = Read(resource, meta);
            if (descriptor is not null) found.Add(descriptor);
            else passedOver.Add(why!);
        }

        if (services.Count == 0)
            passedOver.Add("the DataLink answer describes no service at all (no RESOURCE of type \"meta\" and utype \"adhoc:service\")");
        else if (found.Count == 0 && passedOver.Count == 0)
            passedOver.Add($"the DataLink answer describes no SODA synchronous service, only: {string.Join(", ", others.Distinct())}");
        return new SodaParse(found, passedOver);
    }

    /// <summary>A service descriptor's own PARAMs by name; the first of a name wins, so a malformed answer repeating one is still read.</summary>
    private static Dictionary<string, string> Meta(XElement resource)
        => resource.Elements().Where(e => e.Name.LocalName == "PARAM")
            .GroupBy(p => Attr(p, "name") ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => Attr(g.First(), "value") ?? "", StringComparer.OrdinalIgnoreCase);

    /// <summary>A SODA synchronous service's descriptor, or why it cannot be used.</summary>
    private static (SodaDescriptor? Descriptor, string? PassedOver) Read(XElement resource, Dictionary<string, string> meta)
    {
        static (SodaDescriptor?, string?) Over(string why) => (null, why);

        if (!meta.TryGetValue("accessURL", out var url) || url.Length == 0)
            return Over("the SODA service descriptor has no accessURL");
        if (!DataLinkService.IsHttpsUrl(url))
            return Over($"the SODA service's accessURL is not https, so it is not used: {url}");

        var inputs = resource.Elements().FirstOrDefault(e => e.Name.LocalName == "GROUP" && Attr(e, "name") == "inputParams");
        if (inputs is null)
            return Over("the SODA service descriptor has no inputParams group, so which file it cuts is unknown");

        var parameters = inputs.Elements().Where(e => e.Name.LocalName == "PARAM")
            .GroupBy(p => (Attr(p, "name") ?? "").ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First());
        if (!parameters.TryGetValue("ID", out var id))
            return Over("the SODA service descriptor has no ID parameter, so which file it cuts is unknown");
        if (string.IsNullOrWhiteSpace(Attr(id, "value")))
            return Over(Attr(id, "ref") is { Length: > 0 } column
                ? $"the SODA service names its file by reference to the table's column \"{column}\" rather than by value — a form this app does not read yet"
                : "the SODA service descriptor's ID parameter has no value, so which file it cuts is unknown");

        var circle = parameters.TryGetValue("CIRCLE", out var c) ? CircleFrom(Numbers(Limit(c, "MAX"))) : null;
        var polygon = parameters.TryGetValue("POLYGON", out var p) ? PolygonFrom(Numbers(Limit(p, "MAX"))) : null;
        var (bandMin, bandMax) = parameters.TryGetValue("BAND", out var b) ? Interval(b) : (null, null);
        var (timeMin, timeMax) = parameters.TryGetValue("TIME", out var t) ? Interval(t) : (null, null);
        var pol = parameters.TryGetValue("POL", out var pp)
            ? pp.Descendants().Where(e => e.Name.LocalName == "OPTION")
                .Select(o => Attr(o, "value") ?? "").Where(v => v.Length > 0).ToList()
            : [];

        return (new SodaDescriptor
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
        }, null);
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
