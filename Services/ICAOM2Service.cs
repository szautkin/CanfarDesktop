using CanfarDesktop.Models.Caom2;

namespace CanfarDesktop.Services;

/// <summary>Outcome of a CAOM2 metadata fetch.</summary>
public enum Caom2Status
{
    Success,
    AuthRequired,   // 401/403 — proprietary collection needs CADC sign-in
    NotFound,       // 404
    InvalidId,      // publisher ID could not be normalised to a CAOM2 URI
    ServerError,    // other non-2xx
    Transport,      // network/timeout failure
    Parse,          // malformed CAOM2 document
}

/// <summary>Result of <see cref="ICAOM2Service.GetByPublisherIdAsync"/>.</summary>
public record Caom2Result(Caom2Status Status, CAOM2Observation? Observation, string? Message)
{
    public bool IsSuccess => Status == Caom2Status.Success && Observation is not null;
}

/// <summary>Fetches a single CAOM2 observation document from CADC's caom2ops/meta endpoint.</summary>
public interface ICAOM2Service
{
    Task<Caom2Result> GetByPublisherIdAsync(string publisherID, CancellationToken cancellationToken = default);
}
