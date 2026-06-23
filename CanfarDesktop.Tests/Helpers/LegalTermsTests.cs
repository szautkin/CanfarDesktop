using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

public class LegalTermsTests
{
    [Fact]
    public void IsAccepted_NullOrOlderVersion_ReturnsFalse()
    {
        Assert.False(LegalTerms.IsAccepted(null));
        Assert.False(LegalTerms.IsAccepted(LegalTerms.CurrentVersion - 1));
    }

    [Fact]
    public void IsAccepted_CurrentOrNewerVersion_ReturnsTrue()
    {
        Assert.True(LegalTerms.IsAccepted(LegalTerms.CurrentVersion));
        Assert.True(LegalTerms.IsAccepted(LegalTerms.CurrentVersion + 1));
    }

    [Fact]
    public void IsFrench_DetectsFrenchOnly()
    {
        Assert.True(LegalTerms.IsFrench("fr"));
        Assert.False(LegalTerms.IsFrench("en"));
        Assert.False(LegalTerms.IsFrench(null));
    }

    [Fact]
    public void Body_NotEmpty_ForBothLanguages()
    {
        Assert.False(string.IsNullOrWhiteSpace(LegalTerms.Body(french: false)));
        Assert.False(string.IsNullOrWhiteSpace(LegalTerms.Body(french: true)));
    }
}
