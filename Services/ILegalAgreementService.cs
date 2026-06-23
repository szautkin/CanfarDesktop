namespace CanfarDesktop.Services;

/// <summary>
/// Tracks the user's acceptance of the versioned Terms of Use.
/// </summary>
public interface ILegalAgreementService
{
    /// <summary>The terms version the app currently requires acceptance of.</summary>
    int CurrentVersion { get; }

    /// <summary>True if the user has accepted the current (or a newer) terms version.</summary>
    bool HasAcceptedCurrent { get; }

    /// <summary>Record acceptance of the current terms version (with a UTC timestamp).</summary>
    void Accept();
}
