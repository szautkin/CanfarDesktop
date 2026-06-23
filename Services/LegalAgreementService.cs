using CanfarDesktop.Helpers;
using Windows.Storage;

namespace CanfarDesktop.Services;

/// <summary>
/// Persists Terms-of-Use acceptance (version + UTC timestamp) in LocalSettings.
/// Degrades gracefully when running unpackaged (no ApplicationData) by holding the
/// accepted version in memory for the session only.
/// </summary>
public class LegalAgreementService : ILegalAgreementService
{
    private const string VersionKey = "TermsAcceptedVersion";
    private const string TimestampKey = "TermsAcceptedUtc";

    private readonly ApplicationDataContainer? _localSettings;
    private int? _acceptedVersionFallback;

    public LegalAgreementService()
    {
        try { _localSettings = ApplicationData.Current.LocalSettings; }
        catch { _localSettings = null; }
    }

    public int CurrentVersion => LegalTerms.CurrentVersion;

    public bool HasAcceptedCurrent => LegalTerms.IsAccepted(ReadAcceptedVersion());

    public void Accept()
    {
        _acceptedVersionFallback = LegalTerms.CurrentVersion;
        if (_localSettings is null) return;
        _localSettings.Values[VersionKey] = LegalTerms.CurrentVersion;
        _localSettings.Values[TimestampKey] = DateTimeOffset.UtcNow.ToString("O");
    }

    private int? ReadAcceptedVersion()
    {
        if (_localSettings is not null
            && _localSettings.Values.TryGetValue(VersionKey, out var v)
            && v is int iv)
            return iv;
        return _acceptedVersionFallback;
    }
}
