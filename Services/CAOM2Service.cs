using System.Net.Http.Headers;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models.Caom2;

namespace CanfarDesktop.Services;

/// <summary>
/// Fetches CAOM2 observation documents from caom2ops/meta?ID=caom:{collection}/{observationID}.
/// Maps 401/403 → <see cref="Caom2Status.AuthRequired"/> and 404 → <see cref="Caom2Status.NotFound"/>
/// so the detail viewer can surface a polite "sign in to view" rather than a generic error.
/// Results are cached in a bounded LRU keyed by observation URI.
/// </summary>
public class CAOM2Service : ICAOM2Service
{
    private readonly HttpClient _httpClient;
    private readonly ApiEndpoints _endpoints;

    private readonly object _gate = new();
    private readonly Dictionary<string, CAOM2Observation> _cache = new();
    private readonly LinkedList<string> _order = new();
    private const int CacheCapacity = 100;

    public CAOM2Service(HttpClient httpClient, ApiEndpoints endpoints)
    {
        _httpClient = httpClient;
        _endpoints = endpoints;
    }

    public async Task<Caom2Result> GetByPublisherIdAsync(string publisherID, CancellationToken cancellationToken = default)
    {
        var observationUri = Caom2Uri.ToObservationUri(publisherID);
        if (observationUri is null)
            return new Caom2Result(Caom2Status.InvalidId, null, $"Cannot derive an observation URI from: {publisherID}");

        if (TryGetCached(observationUri, out var cached))
            return new Caom2Result(Caom2Status.Success, cached, null);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(60)); // caom2ops/meta can take 30-50s under load

            using var request = new HttpRequestMessage(HttpMethod.Get, _endpoints.Caom2MetaUrl(observationUri));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));

            using var response = await _httpClient.SendAsync(request, cts.Token);
            switch ((int)response.StatusCode)
            {
                case 200:
                    var xml = await response.Content.ReadAsStringAsync(cts.Token);
                    try
                    {
                        var observation = CAOM2Parser.Parse(xml);
                        Insert(observationUri, observation);
                        return new Caom2Result(Caom2Status.Success, observation, null);
                    }
                    catch (Caom2ParseException ex)
                    {
                        return new Caom2Result(Caom2Status.Parse, null, ex.Message);
                    }
                case 401 or 403:
                    return new Caom2Result(Caom2Status.AuthRequired, null, "This observation requires CADC sign-in.");
                case 404:
                    return new Caom2Result(Caom2Status.NotFound, null, "Observation not found.");
                default:
                    return new Caom2Result(Caom2Status.ServerError, null,
                        $"Metadata server returned HTTP {(int)response.StatusCode}.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return new Caom2Result(Caom2Status.Transport, null, ex.Message);
        }
    }

    private bool TryGetCached(string key, out CAOM2Observation observation)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out observation!))
            {
                _order.Remove(key);
                _order.AddLast(key);
                return true;
            }
            observation = null!;
            return false;
        }
    }

    private void Insert(string key, CAOM2Observation value)
    {
        lock (_gate)
        {
            _cache[key] = value;
            _order.Remove(key);
            _order.AddLast(key);
            while (_order.Count > CacheCapacity && _order.First is { } oldest)
            {
                _order.RemoveFirst();
                _cache.Remove(oldest.Value);
            }
        }
    }
}
