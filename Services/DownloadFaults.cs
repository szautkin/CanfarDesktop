namespace CanfarDesktop.Services;

/// <summary>
/// DataLink answered with faults and nothing else — the service refusing the request, not resolving
/// it to an observation with no artifacts. Carries the service's own text, because "invalid ID" and
/// "no data for this proposal" need different things from the person reading them.
/// </summary>
public sealed class DataLinkFaultException : Exception
{
    public string PublisherId { get; }
    public IReadOnlyList<string> Faults { get; }

    public DataLinkFaultException(string publisherId, IReadOnlyList<string> faults)
        : base(Describe(publisherId, faults))
    {
        PublisherId = publisherId;
        Faults = faults;
    }

    private static string Describe(string publisherId, IReadOnlyList<string> faults)
        => $"the archive refused {publisherId}: {string.Join("; ", faults)}";
}

/// <summary>
/// The transfer succeeded and produced nothing.
///
/// <c>caom2ops/pkg</c> answers HTTP 200 with an empty body and no content type for a publisher id it
/// cannot resolve, so the status check passed, a zero-byte file landed in the research library, and
/// the job reported "Downloaded … (0 bytes)" as succeeded. Zero bytes is refused here and the partial
/// file removed, which is the only point that can tell the difference.
/// </summary>
public sealed class EmptyDownloadException : Exception
{
    /// <summary>The shape of a real mirrored publisher id, shown because a wrong one is the usual cause.</summary>
    public const string MirroredIdShape = "ivo://cadc.nrc.ca/mirror/JWST?<observationID>/<productID>";

    public string Url { get; }

    public EmptyDownloadException(string url, bool fromUnresolvedId)
        : base(Describe(url, fromUnresolvedId))
        => Url = url;

    private static string Describe(string url, bool fromUnresolvedId) => fromUnresolvedId
        ? "the archive returned an empty response, which is what it answers for a publisher id it " +
          $"cannot resolve. A mirrored id looks like {MirroredIdShape}."
        : $"the artifact at {url} is empty (0 bytes); nothing was saved.";
}
