using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace CanfarDesktop.Services.ImageDiscovery;

public enum RegistryTestKind
{
    Success,
    Unauthorized,
    MissingConfiguration,
    InvalidChallenge,
    NetworkError,
}

public record RegistryTestResult(RegistryTestKind Kind, string Message);

/// <summary>
/// Verifies registry credentials via the Docker Registry V2 token-auth dance: ping <c>/v2/</c> to
/// discover the Bearer realm, then request a token with Basic auth. Lets the user confirm their
/// Harbor CLI secret BEFORE a probe job fails 5 minutes later with ImagePullBackOff. Works against
/// any OCI-compliant registry (Harbor, Docker Hub, Quay, GHCR). The <see cref="HttpClient"/> is
/// injected so it can be exercised with a mock handler in tests — it must NOT be an auth'd client
/// (the registry gets Basic auth, never the CADC bearer token).
/// </summary>
public static class RegistryCredentialTest
{
    public static async Task<RegistryTestResult> PerformAsync(string host, string user, string secret, HttpClient client)
    {
        if (string.IsNullOrEmpty(host)) return new(RegistryTestKind.MissingConfiguration, "Registry host is empty.");
        if (string.IsNullOrEmpty(user)) return new(RegistryTestKind.MissingConfiguration, "Username is empty.");
        if (string.IsNullOrEmpty(secret))
            return new(RegistryTestKind.MissingConfiguration, "No secret stored — save your Harbor CLI secret first.");

        // Step 1: ping /v2/ to discover the auth realm.
        HttpResponseMessage ping;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            ping = await client.GetAsync($"https://{host}/v2/", cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return new(RegistryTestKind.NetworkError, ex.Message);
        }

        using (ping)
        {
            if ((int)ping.StatusCode is >= 200 and < 300)
                return new(RegistryTestKind.Success, "Registry is publicly accessible — no credentials needed for image pulls.");

            if (ping.StatusCode != HttpStatusCode.Unauthorized)
                return new(RegistryTestKind.NetworkError, $"Unexpected HTTP {(int)ping.StatusCode} from {host}/v2/.");

            var challenge = ExtractChallenge(ping);
            if (challenge is null)
                return new(RegistryTestKind.NetworkError, $"Registry returned 401 without a WWW-Authenticate challenge from {host}/v2/.");

            var parsed = ParseBearerChallenge(challenge);
            if (parsed is null)
                return new(RegistryTestKind.InvalidChallenge, $"Could not parse WWW-Authenticate: {challenge}");
            if (string.IsNullOrEmpty(parsed.Value.Realm) || !Uri.TryCreate(parsed.Value.Realm, UriKind.Absolute, out var realmUri))
                return new(RegistryTestKind.InvalidChallenge, "Bearer challenge missing or malformed realm.");

            // Step 2: GET <realm>?service=<service> with Basic auth.
            var tokenUrl = parsed.Value.Service is { Length: > 0 } svc ? AppendQuery(realmUri, "service", svc) : realmUri;
            var request = new HttpRequestMessage(HttpMethod.Get, tokenUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{secret}")));

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var token = await client.SendAsync(request, cts.Token);
                var code = (int)token.StatusCode;
                if (code is 401 or 403)
                    return new(RegistryTestKind.Unauthorized, "Registry rejected the credentials. Use your Harbor CLI secret (not your CADC password); it may have expired.");
                if (code is >= 200 and < 300)
                    return new(RegistryTestKind.Success, "Credentials valid. The registry issued an auth token successfully.");
                return new(RegistryTestKind.NetworkError, $"Token endpoint returned HTTP {code}.");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                return new(RegistryTestKind.NetworkError, ex.Message);
            }
        }
    }

    /// <summary>
    /// Parse a Docker Registry V2 <c>Bearer realm="x", service="y"</c> challenge into its realm +
    /// service. Tolerates single/double quotes, extra params, and stray whitespace; returns null
    /// when the scheme isn't Bearer.
    /// </summary>
    public static (string? Realm, string? Service)? ParseBearerChallenge(string challenge)
    {
        var trimmed = challenge.Trim();
        if (!(trimmed.Equals("bearer", StringComparison.OrdinalIgnoreCase)
              || trimmed.StartsWith("bearer ", StringComparison.OrdinalIgnoreCase)))
            return null;

        var afterScheme = trimmed[6..].Trim();
        string? realm = null, service = null;
        foreach (var part in afterScheme.Split(','))
        {
            var kv = part.Trim();
            var eq = kv.IndexOf('=');
            if (eq < 0) continue;
            var key = kv[..eq].Trim().ToLowerInvariant();
            var value = kv[(eq + 1)..].Trim();
            if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                value = value[1..^1];
            if (key == "realm") realm = value;
            else if (key == "service") service = value;
        }
        return (realm, service);
    }

    private static string? ExtractChallenge(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("WWW-Authenticate", out var raw))
        {
            var joined = string.Join(", ", raw);
            if (!string.IsNullOrWhiteSpace(joined)) return joined;
        }
        if (response.Headers.WwwAuthenticate.Count > 0)
            return string.Join(", ", response.Headers.WwwAuthenticate.Select(h => h.ToString()));
        return null;
    }

    private static Uri AppendQuery(Uri uri, string key, string value)
    {
        var separator = string.IsNullOrEmpty(uri.Query) ? "?" : "&";
        return new Uri($"{uri}{separator}{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
    }
}
