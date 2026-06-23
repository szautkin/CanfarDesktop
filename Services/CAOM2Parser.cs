using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using CanfarDesktop.Models.Caom2;

namespace CanfarDesktop.Services;

/// <summary>Thrown when a CAOM2 document is malformed or missing a required field.</summary>
public class Caom2ParseException : Exception
{
    public Caom2ParseException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Tolerant CAOM-2 XML reader built on LINQ-to-XML. Element matching uses the local name
/// (<see cref="XName.LocalName"/>) so the document namespace prefix (caom2:, vodml:, …) and
/// schema-version drift (v2.4 / v2.5) don't matter. Unknown elements are ignored rather than
/// errored, so additive schema changes won't make observations unviewable.
/// </summary>
public static class CAOM2Parser
{
    public static CAOM2Observation Parse(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (XmlException ex)
        {
            throw new Caom2ParseException("Malformed CAOM2 XML", ex);
        }

        var root = doc.Root ?? throw new Caom2ParseException("CAOM2 document has no root element");
        return ParseObservation(root);
    }

    private static CAOM2Observation ParseObservation(XElement el)
    {
        var collection = TextChild(el, "collection")
            ?? throw new Caom2ParseException("CAOM2 document missing required field: collection");
        var obsID = TextChild(el, "observationID")
            ?? throw new Caom2ParseException("CAOM2 document missing required field: observationID");

        return new CAOM2Observation
        {
            Collection = collection,
            ObservationID = obsID,
            ObservationType = TextChild(el, "type"),
            Intent = TextChild(el, "intent"),
            SequenceNumber = TextChild(el, "sequenceNumber"),
            MetaRelease = DateChild(el, "metaRelease"),
            Algorithm = TextChild(Child(el, "algorithm"), "name"),
            Proposal = Child(el, "proposal") is { } p ? ParseProposal(p) : null,
            Target = Child(el, "target") is { } t ? ParseTarget(t) : null,
            Telescope = Child(el, "telescope") is { } te ? ParseTelescope(te) : null,
            Instrument = Child(el, "instrument") is { } i ? ParseInstrument(i) : null,
            Environment = Child(el, "environment") is { } e ? ParseEnvironment(e) : null,
            Planes = Children(Child(el, "planes"), "plane").Select(ParsePlane).ToList(),
        };
    }

    private static Caom2Proposal ParseProposal(XElement el) => new()
    {
        Id = TextChild(el, "id"),
        Pi = TextChild(el, "pi"),
        Project = TextChild(el, "project"),
        Title = TextChild(el, "title"),
        Keywords = KeywordList(Child(el, "keywords")),
    };

    private static Caom2Target ParseTarget(XElement el) => new()
    {
        Name = TextChild(el, "name"),
        Type = TextChild(el, "type"),
        Standard = BoolChild(el, "standard"),
        Redshift = DoubleChild(el, "redshift"),
        Moving = BoolChild(el, "moving"),
        Keywords = KeywordList(Child(el, "keywords")),
    };

    private static Caom2Telescope ParseTelescope(XElement el)
    {
        var x = DoubleChild(el, "geoLocationX");
        var y = DoubleChild(el, "geoLocationY");
        var z = DoubleChild(el, "geoLocationZ");
        var geo = x is { } gx && y is { } gy && z is { } gz ? new Caom2GeoLocation(gx, gy, gz) : null;
        return new Caom2Telescope
        {
            Name = TextChild(el, "name"),
            GeoLocation = geo,
            Keywords = KeywordList(Child(el, "keywords")),
        };
    }

    private static Caom2Instrument ParseInstrument(XElement el) => new()
    {
        Name = TextChild(el, "name"),
        Keywords = KeywordList(Child(el, "keywords")),
    };

    private static Caom2Environment ParseEnvironment(XElement el) => new()
    {
        Seeing = DoubleChild(el, "seeing"),
        Humidity = DoubleChild(el, "humidity"),
        Elevation = DoubleChild(el, "elevation"),
        Tau = DoubleChild(el, "tau"),
        WavelengthTau = DoubleChild(el, "wavelengthTau"),
        AmbientTemp = DoubleChild(el, "ambientTemp"),
        Photometric = BoolChild(el, "photometric"),
    };

    private static Caom2Plane ParsePlane(XElement el) => new()
    {
        ProductID = TextChild(el, "productID") ?? string.Empty,
        CreatorID = TextChild(el, "creatorID"),
        MetaRelease = DateChild(el, "metaRelease"),
        DataRelease = DateChild(el, "dataRelease"),
        DataProductType = TextChild(el, "dataProductType"),
        CalibrationLevel = IntChild(el, "calibrationLevel"),
        Provenance = Child(el, "provenance") is { } pv ? ParseProvenance(pv) : null,
        Metrics = Child(el, "metrics") is { } m ? ParseMetrics(m) : null,
        Quality = TextChild(Child(el, "quality"), "flag"),
        Position = Child(el, "position") is { } pos ? ParsePosition(pos) : null,
        Energy = Child(el, "energy") is { } en ? ParseEnergy(en) : null,
        Time = Child(el, "time") is { } ti ? ParseTime(ti) : null,
        Polarization = Child(el, "polarization") is { } pol ? ParsePolarization(pol) : null,
        Artifacts = Children(Child(el, "artifacts"), "artifact").Select(ParseArtifact).ToList(),
    };

    private static Caom2Provenance ParseProvenance(XElement el) => new()
    {
        Name = TextChild(el, "name"),
        Version = TextChild(el, "version"),
        Project = TextChild(el, "project"),
        Producer = TextChild(el, "producer"),
        RunID = TextChild(el, "runID"),
        Reference = TextChild(el, "reference"),
        LastExecuted = DateChild(el, "lastExecuted"),
        Keywords = KeywordList(Child(el, "keywords")),
        Inputs = Children(Child(el, "inputs"), "planeURI")
            .Select(n => n.Value.Trim())
            .Where(s => s.Length > 0)
            .ToList(),
    };

    private static Caom2Metrics ParseMetrics(XElement el) => new()
    {
        SourceNumberDensity = DoubleChild(el, "sourceNumberDensity"),
        Background = DoubleChild(el, "background"),
        BackgroundStddev = DoubleChild(el, "backgroundStddev"),
        FluxDensityLimit = DoubleChild(el, "fluxDensityLimit"),
        MagLimit = DoubleChild(el, "magLimit"),
        SampleSNR = DoubleChild(el, "sampleSNR"),
    };

    /// <summary>
    /// Position bounds — pulls polygon vertices when present. CAOM2 wraps the polygon under
    /// bounds/Polygon/points/vertex/{cval1,cval2}. Returns null if the whole section is empty.
    /// </summary>
    private static Caom2Position? ParsePosition(XElement el)
    {
        var polygon = new List<Caom2SkyVertex>();
        if (Child(el, "bounds") is { } bounds)
        {
            var polyContainer = Child(bounds, "Polygon") ?? bounds;
            if (Child(polyContainer, "points") is { } points)
                foreach (var vertex in Children(points, "vertex"))
                    if (ParseVertex(vertex) is { } v) polygon.Add(v);
            foreach (var vertex in Children(polyContainer, "vertex"))
                if (ParseVertex(vertex) is { } v) polygon.Add(v);
        }

        var dim = ParseDimension(Child(el, "dimension"));

        if (polygon.Count == 0
            && dim is null
            && DoubleChild(el, "resolution") is null
            && DoubleChild(el, "sampleSize") is null
            && BoolChild(el, "timeDependent") is null)
        {
            return null;
        }

        return new Caom2Position
        {
            Polygon = polygon,
            DimensionPixels = dim,
            ResolutionArcsec = DoubleChild(el, "resolution"),
            SampleSizeArcsec = DoubleChild(el, "sampleSize"),
            TimeDependent = BoolChild(el, "timeDependent"),
        };
    }

    private static Caom2PixelDimension? ParseDimension(XElement? dim)
    {
        if (dim is null) return null;
        return IntChild(dim, "naxis1") is { } a && IntChild(dim, "naxis2") is { } b
            ? new Caom2PixelDimension(a, b)
            : null;
    }

    private static Caom2SkyVertex? ParseVertex(XElement el)
    {
        var ra = DoubleChild(el, "cval1") ?? DoubleChild(el, "coord1");
        var dec = DoubleChild(el, "cval2") ?? DoubleChild(el, "coord2");
        if (ra is { } r && dec is { } d && double.IsFinite(r) && double.IsFinite(d))
            return new Caom2SkyVertex(r, d);
        return null;
    }

    private static Caom2Energy ParseEnergy(XElement el)
    {
        var lower = DoubleChild(Child(el, "bounds"), "lower");
        var upper = DoubleChild(Child(el, "bounds"), "upper");
        if (lower is null || upper is null)
        {
            // Fallback: range axis under axis/range/{start,end}/val.
            var range = Child(Child(el, "axis"), "range");
            lower ??= DoubleChild(Child(range, "start"), "val");
            upper ??= DoubleChild(Child(range, "end"), "val");
        }
        return new Caom2Energy
        {
            LowerMetres = lower,
            UpperMetres = upper,
            ResolvingPower = DoubleChild(el, "resolvingPower"),
            BandpassName = TextChild(el, "bandpassName"),
            EmBand = TextChild(el, "emBand"),
            RestWavMetres = DoubleChild(el, "restwav"),
        };
    }

    private static Caom2Time ParseTime(XElement el) => new()
    {
        LowerMJD = DoubleChild(Child(el, "bounds"), "lower"),
        UpperMJD = DoubleChild(Child(el, "bounds"), "upper"),
        ExposureSeconds = DoubleChild(el, "exposure"),
    };

    private static Caom2Polarization ParsePolarization(XElement el) => new()
    {
        States = Children(Child(el, "states"), "state")
            .Select(n => n.Value.Trim())
            .Where(s => s.Length > 0)
            .ToList(),
    };

    private static Caom2Artifact ParseArtifact(XElement el) => new()
    {
        Uri = TextChild(el, "uri") ?? string.Empty,
        ProductType = TextChild(el, "productType"),
        ReleaseType = TextChild(el, "releaseType"),
        ContentLength = LongChild(el, "contentLength"),
        ContentType = TextChild(el, "contentType"),
        ContentChecksum = TextChild(el, "contentChecksum"),
    };

    // MARK: - Tree helpers (namespace-agnostic via local name)

    private static XElement? Child(XElement? parent, string name)
        => parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name);

    private static IEnumerable<XElement> Children(XElement? parent, string name)
        => parent?.Elements().Where(e => e.Name.LocalName == name) ?? Enumerable.Empty<XElement>();

    private static string? TextChild(XElement? parent, string name)
    {
        var value = Child(parent, name)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static double? DoubleChild(XElement? parent, string name)
        => TextChild(parent, name) is { } s
           && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static int? IntChild(XElement? parent, string name)
        => TextChild(parent, name) is { } s
           && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    private static long? LongChild(XElement? parent, string name)
        => TextChild(parent, name) is { } s
           && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : null;

    private static bool? BoolChild(XElement? parent, string name)
        => TextChild(parent, name)?.ToLowerInvariant() switch
        {
            "true" or "1" => true,
            "false" or "0" => false,
            _ => null,
        };

    private static DateTimeOffset? DateChild(XElement? parent, string name)
    {
        var raw = TextChild(parent, name);
        if (raw is null) return null;
        // Plain (no zone) → assume UTC; ISO-8601 with Z/offset handled too.
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return new DateTimeOffset(dt, TimeSpan.Zero);
        return null;
    }

    /// <summary>
    /// Keyword lists appear as &lt;keywords&gt;&lt;keyword&gt;...&lt;/keyword&gt;&lt;/keywords&gt; in some
    /// schema versions and as a single space/;-separated string in others. Handle both.
    /// </summary>
    private static IReadOnlyList<string> KeywordList(XElement? container)
    {
        if (container is null) return [];
        var elements = Children(container, "keyword")
            .Select(e => e.Value.Trim())
            .Where(s => s.Length > 0)
            .ToList();
        if (elements.Count > 0) return elements;

        var raw = container.Value.Trim();
        if (raw.Length == 0) return [];
        return raw.Split([' ', '\t', '\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries);
    }
}
