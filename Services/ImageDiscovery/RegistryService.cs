using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services.ImageDiscovery;

/// <summary>What to talk to, and as whom.</summary>
/// <param name="Basic">
/// <c>base64(username:secret)</c>, ready for a Basic header — the same value the discovery settings
/// already mint for <c>x-skaha-registry-auth</c>, so someone who configured discovery has already
/// configured this. Null is allowed: a public project answers without credentials, and letting someone
/// try a search before going to find their CLI secret is the difference between a feature they use and
/// one they bounce off.
/// </param>
public readonly record struct RegistryAuth(string? Basic)
{
    public static RegistryAuth None => new((string?)null);

    /// <summary>Credentials typed in for one search, never stored.</summary>
    public static RegistryAuth FromCredentials(string? username, string? secret)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(secret)) return None;

        return new RegistryAuth(Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username.Trim()}:{secret}")));
    }
}

public interface IRegistryService
{
    /// <summary>Images in <paramref name="host"/> whose repository matches <paramref name="term"/>.</summary>
    Task<IReadOnlyList<RegistryImage>> SearchAsync(string host, string term, RegistryAuth auth, CancellationToken ct = default);
}

/// <summary>
/// Searching the container registry behind the platform.
///
/// Skaha's <c>/v1/image</c> is a curated list; the registry behind it holds far more. This is how
/// someone reaches an image the platform has not listed — a colleague's build, a tag Skaha has not
/// picked up — and it runs ONLY when they ask. Nothing here is called on a timer or at start-up:
/// enumerating a Harbor instance to populate a dashboard card would be a great deal of traffic to
/// answer a question nobody asked.
///
/// Harbor's own API rather than the OCI distribution API, for one reason: LABELS. <c>/v2/_catalog</c>
/// and <c>/v2/&lt;name&gt;/tags/list</c> will enumerate a registry anywhere, but neither reports labels
/// without pulling each image's config blob — and the labels are what say whether an image is a
/// notebook or a CARTA session. Without them an added image cannot be typed, cannot be filtered in the
/// widget, and cannot be offered on the Standard launch tab.
/// </summary>
public sealed class RegistryService : IRegistryService
{
    /// <summary>
    /// How many repositories one search will open. Harbor's search happily returns hundreds for a short
    /// term; past the first couple of dozen the user is not reading results, they are re-typing their
    /// search — so the rest is traffic spent on a list nobody scrolls.
    /// </summary>
    public const int MaxRepositories = 24;

    /// <summary>
    /// How many of those are read at once. An unbounded fan-out would be this app deciding, on one
    /// keystroke, to open fifty connections to a shared service.
    /// </summary>
    public const int ConcurrentRepositoryReads = 4;

    /// <summary>Newest first, which is Harbor's default: a repository with two hundred tags is a build history.</summary>
    private const int ArtifactsPerRepository = 10;

    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(20);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _client;

    public RegistryService(HttpClient client) => _client = client;

    public async Task<IReadOnlyList<RegistryImage>> SearchAsync(
        string host, string term, RegistryAuth auth, CancellationToken ct = default)
    {
        host = (host ?? string.Empty).Trim().TrimEnd('/');
        if (host.Length == 0) throw new InvalidOperationException("No registry host configured.");

        term = (term ?? string.Empty).Trim();
        // An empty term is refused rather than treated as "match everything": that is the whole-registry
        // download this class exists to avoid.
        if (term.Length == 0) throw new InvalidOperationException("Type something to search for.");

        var found = await GetJsonAsync<SearchResponse>(
            $"https://{host}/api/v2.0/search?q={Uri.EscapeDataString(term)}", auth, ct);

        var repositories = (found?.Repository ?? []).Take(MaxRepositories).ToList();

        var images = new List<RegistryImage>();
        for (var i = 0; i < repositories.Count; i += ConcurrentRepositoryReads)
        {
            var chunk = repositories.Skip(i).Take(ConcurrentRepositoryReads);

            var reads = chunk.Select(async repo =>
            {
                try
                {
                    var artifacts = await GetJsonAsync<List<Artifact>>(
                        ArtifactsUrl(host, repo.ProjectName ?? "", repo.RepositoryName ?? ""), auth, ct);
                    return ToImages(host, repo.RepositoryName ?? "", artifacts ?? []);
                }
                catch
                {
                    // A repository that fails to read is skipped, not fatal: a search across projects
                    // will routinely include one the user cannot see, and losing the other twenty-three
                    // to it would be the wrong answer.
                    return [];
                }
            });

            foreach (var batch in await Task.WhenAll(reads)) images.AddRange(batch);
        }

        return images
            .GroupBy(i => i.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(i => i.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Harbor's artifacts URL for one repository.
    ///
    /// The repository segment is DOUBLE-encoded on purpose. Harbor addresses a nested repository
    /// (<c>skaha/base/astro</c>) as one path segment, so its <c>/</c> must survive the proxy's decode
    /// and arrive at Harbor still encoded — hence encoding the already-encoded form.
    /// </summary>
    internal static string ArtifactsUrl(string host, string project, string repositoryName)
    {
        var bare = repositoryName.StartsWith(project + "/", StringComparison.Ordinal)
            ? repositoryName[(project.Length + 1)..]
            : repositoryName;

        var encoded = Uri.EscapeDataString(Uri.EscapeDataString(bare));

        return $"https://{host}/api/v2.0/projects/{Uri.EscapeDataString(project)}/repositories/{encoded}"
             + $"/artifacts?with_label=true&page_size={ArtifactsPerRepository}&page=1";
    }

    /// <summary>
    /// One repository's artifacts as launchable image references. An UNTAGGED artifact is skipped: it
    /// can only be addressed by digest, which is not what the launch form takes, so listing it would
    /// offer something that cannot be launched.
    /// </summary>
    internal static List<RegistryImage> ToImages(string host, string repositoryName, IReadOnlyList<Artifact> artifacts)
    {
        var images = new List<RegistryImage>();

        foreach (var artifact in artifacts)
        {
            var labels = (artifact.Labels ?? []).Select(l => l.Name ?? "").ToList();
            foreach (var tag in artifact.Tags ?? [])
            {
                if (string.IsNullOrWhiteSpace(tag.Name)) continue;
                images.Add(RegistryImage.FromLabels($"{host}/{repositoryName}:{tag.Name}", labels));
            }
        }

        return images;
    }

    private async Task<T?> GetJsonAsync<T>(string url, RegistryAuth auth, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (auth.Basic is { Length: > 0 } basic)
            request.Headers.TryAddWithoutValidation("Authorization", "Basic " + basic);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CallTimeout);

        HttpResponseMessage response;
        try
        {
            response = await _client.SendAsync(request, timeout.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || ct.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Could not reach the registry: {ex.Message}", ex);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                // Named specifically because the fix is specific, and because the CADC password is the
                // wrong answer often enough to be worth saying.
                throw new InvalidOperationException(
                    "The registry rejected these credentials. Use your Harbor CLI secret, not your CADC password.");

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"The registry returned HTTP {(int)response.StatusCode}.");

            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            try
            {
                return JsonSerializer.Deserialize<T>(body, Json);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Could not read the registry's answer: {ex.Message}", ex);
            }
        }
    }

    // ── Harbor's wire shapes, only the fields used ──────────────────────────────────────────────

    internal sealed class SearchResponse
    {
        public List<SearchRepository>? Repository { get; set; }
    }

    internal sealed class SearchRepository
    {
        [JsonPropertyName("project_name")] public string? ProjectName { get; set; }

        /// <summary>Fully qualified as <c>project/name</c>, and <c>name</c> may itself contain <c>/</c>.</summary>
        [JsonPropertyName("repository_name")] public string? RepositoryName { get; set; }
    }

    internal sealed class Artifact
    {
        public List<ArtifactTag>? Tags { get; set; }
        public List<ArtifactLabel>? Labels { get; set; }
    }

    internal sealed class ArtifactTag
    {
        public string? Name { get; set; }
    }

    internal sealed class ArtifactLabel
    {
        public string? Name { get; set; }
    }
}
