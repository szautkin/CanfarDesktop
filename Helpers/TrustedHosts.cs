namespace CanfarDesktop.Helpers;

/// <summary>
/// Allowlist of hosts the app's CADC session (bearer) token may be sent to.
///
/// The token must only ever leave the machine bound for a trusted CANFAR/CADC
/// host, and only over HTTPS. This guards against two leak vectors:
///   1. Attaching the token to an off-domain URL supplied by a server response
///      (e.g. a DataLink access_url or preview link pointing at a partner archive).
///   2. Sending the token over a downgraded (http) connection.
///
/// .NET's SocketsHttpHandler already clears the Authorization header on
/// auto-redirects and never follows HTTPS-&gt;HTTP, so the per-hop redirect case is
/// covered by the framework; this allowlist governs the initial token attach.
/// </summary>
public static class TrustedHosts
{
    // A host is trusted if it equals one of these suffixes or is a subdomain of it.
    private static readonly string[] TrustedSuffixes =
    {
        "canfar.net",
        "cadc-ccda.hia-iha.nrc-cnrc.gc.ca",
    };

    /// <summary>
    /// True if the bearer token may be attached to a request for this URI:
    /// it must be absolute, HTTPS, and target a trusted host.
    /// </summary>
    public static bool IsTrusted(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        return IsTrustedHost(uri.Host);
    }

    /// <summary>
    /// True if the host equals, or is a subdomain of, a trusted suffix (case-insensitive).
    /// Lookalikes such as "canfar.net.evil.com" or "evilcanfar.net" are rejected because a
    /// match requires a "." boundary immediately before the suffix.
    /// </summary>
    public static bool IsTrustedHost(string? host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        foreach (var suffix in TrustedSuffixes)
        {
            if (host.Equals(suffix, StringComparison.OrdinalIgnoreCase))
                return true;
            if (host.Length > suffix.Length + 1
                && host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && host[host.Length - suffix.Length - 1] == '.')
                return true;
        }
        return false;
    }
}
