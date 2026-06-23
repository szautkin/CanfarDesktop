namespace CanfarDesktop.Helpers;

/// <summary>
/// Normalises a publisher ID to the canonical CAOM2 observation URI
/// (<c>caom:{collection}/{observationID}</c>) expected by the metadata service.
/// Accepts the shapes TAP / search return:
///   • Observation form:   <c>ivo://cadc.nrc.ca/JWST?jw01147</c>
///   • Plane form:         <c>ivo://cadc.nrc.ca/CFHT?729989/729989p</c> (productID stripped)
///   • Mirror collections: <c>ivo://cadc.nrc.ca/JWST/mirror?jw01147</c> (mirror segment dropped)
///   • Already-canonical:  <c>caom:CFHT/22803</c>
/// Returns null if the input is not a recognisable publisher URI.
/// </summary>
public static class Caom2Uri
{
    public static string? ToObservationUri(string? publisherID)
    {
        var trimmed = publisherID?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return null;

        // Already-canonical caom:Collection/ObservationID form.
        if (trimmed.StartsWith("caom:", StringComparison.OrdinalIgnoreCase))
        {
            var body = trimmed["caom:".Length..];
            var parts = body.Split('/', 2);
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0) return null;
            var observationID = parts[1].Split('/', 2)[0]; // strip trailing /productID
            if (observationID.Length == 0) return null;
            return $"caom:{parts[0]}/{observationID}";
        }

        // ivo://authority/collection[/mirror]?observationID[/productID]
        if (!trimmed.StartsWith("ivo://", StringComparison.OrdinalIgnoreCase)) return null;
        var rest = trimmed["ivo://".Length..];

        var qIdx = rest.IndexOf('?');
        var pathPart = qIdx >= 0 ? rest[..qIdx] : rest;
        var query = qIdx >= 0 ? rest[(qIdx + 1)..] : string.Empty;

        // First path segment is the authority/host; the rest is the path.
        var pathSegments = pathPart.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var afterHost = pathSegments
            .Skip(1)
            .Where(p => !string.Equals(p, "mirror", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (afterHost.Length == 0 || afterHost[0].Length == 0) return null;
        var collection = afterHost[0];

        var observation = query.Split('/', 2)[0];
        if (observation.Length == 0) return null;
        return $"caom:{collection}/{observation}";
    }
}
