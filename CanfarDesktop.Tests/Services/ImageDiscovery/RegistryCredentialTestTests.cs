using System.Net;
using System.Text;
using Xunit;
using CanfarDesktop.Services.ImageDiscovery;
using CanfarDesktop.Tests.Helpers;

namespace CanfarDesktop.Tests.Services.ImageDiscovery;

public class RegistryCredentialTestTests
{
    // ---- ParseBearerChallenge --------------------------------------------------------------

    [Fact]
    public void ParseBearerChallenge_ExtractsRealmAndService()
    {
        var parsed = RegistryCredentialTest.ParseBearerChallenge(
            "Bearer realm=\"https://auth.example/token\",service=\"registry.example\"");
        Assert.NotNull(parsed);
        Assert.Equal("https://auth.example/token", parsed!.Value.Realm);
        Assert.Equal("registry.example", parsed.Value.Service);
    }

    [Fact]
    public void ParseBearerChallenge_ToleratesSpacesAndExtraParams()
    {
        var parsed = RegistryCredentialTest.ParseBearerChallenge(
            "Bearer realm=\"https://auth.example/token\", service=\"reg\", scope=\"repository:x:pull\"");
        Assert.NotNull(parsed);
        Assert.Equal("https://auth.example/token", parsed!.Value.Realm);
        Assert.Equal("reg", parsed.Value.Service);
    }

    [Fact]
    public void ParseBearerChallenge_HandlesSingleQuotes()
    {
        var parsed = RegistryCredentialTest.ParseBearerChallenge("Bearer realm='https://a/token', service='svc'");
        Assert.Equal("https://a/token", parsed!.Value.Realm);
        Assert.Equal("svc", parsed.Value.Service);
    }

    [Fact]
    public void ParseBearerChallenge_IsSchemeCaseInsensitive()
    {
        var parsed = RegistryCredentialTest.ParseBearerChallenge("bearer realm=\"https://a/token\"");
        Assert.Equal("https://a/token", parsed!.Value.Realm);
        Assert.Null(parsed.Value.Service);
    }

    [Theory]
    [InlineData("Basic realm=\"x\"")]
    [InlineData("Negotiate")]
    [InlineData("BearerToken realm=\"x\"")] // not the Bearer scheme
    public void ParseBearerChallenge_ReturnsNullForNonBearer(string challenge)
        => Assert.Null(RegistryCredentialTest.ParseBearerChallenge(challenge));

    // ---- PerformAsync: configuration guards ------------------------------------------------

    [Theory]
    [InlineData("", "alice", "s3cr3t")]
    [InlineData("images.canfar.net", "", "s3cr3t")]
    [InlineData("images.canfar.net", "alice", "")]
    public async Task PerformAsync_MissingConfiguration(string host, string user, string secret)
    {
        var client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK));
        var result = await RegistryCredentialTest.PerformAsync(host, user, secret, client);
        Assert.Equal(RegistryTestKind.MissingConfiguration, result.Kind);
    }

    // ---- PerformAsync: the token-auth dance ------------------------------------------------

    [Fact]
    public async Task PerformAsync_PublicRegistry_IsSuccess()
    {
        var client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK));
        var result = await RegistryCredentialTest.PerformAsync("images.canfar.net", "alice", "s3cr3t", client);
        Assert.Equal(RegistryTestKind.Success, result.Kind);
    }

    [Fact]
    public async Task PerformAsync_ValidCredentials_SendsBasicAuthToRealmAndSucceeds()
    {
        HttpRequestMessage? tokenRequest = null;
        var client = new HttpClient(new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/v2/")
                return Task.FromResult(Challenge());
            tokenRequest = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"token\":\"abc\"}"),
            });
        }));

        var result = await RegistryCredentialTest.PerformAsync("images.canfar.net", "alice", "s3cr3t", client);

        Assert.Equal(RegistryTestKind.Success, result.Kind);
        Assert.NotNull(tokenRequest);
        Assert.Equal("https://auth.example/token", tokenRequest!.RequestUri!.GetLeftPart(UriPartial.Path));
        Assert.Contains("service=registry.example", tokenRequest.RequestUri!.Query);
        Assert.Equal("Basic", tokenRequest.Headers.Authorization!.Scheme);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(tokenRequest.Headers.Authorization!.Parameter!));
        Assert.Equal("alice:s3cr3t", decoded);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task PerformAsync_RegistryRejectsToken_IsUnauthorized(HttpStatusCode tokenStatus)
    {
        var client = new HttpClient(new MockHttpMessageHandler(req =>
            Task.FromResult(req.RequestUri!.AbsolutePath == "/v2/"
                ? Challenge()
                : new HttpResponseMessage(tokenStatus))));

        var result = await RegistryCredentialTest.PerformAsync("images.canfar.net", "alice", "s3cr3t", client);
        Assert.Equal(RegistryTestKind.Unauthorized, result.Kind);
    }

    [Fact]
    public async Task PerformAsync_401WithoutChallenge_IsNetworkError()
    {
        var client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.Unauthorized));
        var result = await RegistryCredentialTest.PerformAsync("images.canfar.net", "alice", "s3cr3t", client);
        Assert.Equal(RegistryTestKind.NetworkError, result.Kind);
    }

    [Fact]
    public async Task PerformAsync_NonBearerChallenge_IsInvalidChallenge()
    {
        var client = new HttpClient(new MockHttpMessageHandler(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            r.Headers.TryAddWithoutValidation("WWW-Authenticate", "Basic realm=\"x\"");
            return Task.FromResult(r);
        }));
        var result = await RegistryCredentialTest.PerformAsync("images.canfar.net", "alice", "s3cr3t", client);
        Assert.Equal(RegistryTestKind.InvalidChallenge, result.Kind);
    }

    [Fact]
    public async Task PerformAsync_UnexpectedStatus_IsNetworkError()
    {
        var client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.InternalServerError));
        var result = await RegistryCredentialTest.PerformAsync("images.canfar.net", "alice", "s3cr3t", client);
        Assert.Equal(RegistryTestKind.NetworkError, result.Kind);
    }

    [Fact]
    public async Task PerformAsync_NetworkException_IsNetworkError()
    {
        var client = new HttpClient(new MockHttpMessageHandler(
            _ => throw new HttpRequestException("connection refused")));
        var result = await RegistryCredentialTest.PerformAsync("images.canfar.net", "alice", "s3cr3t", client);
        Assert.Equal(RegistryTestKind.NetworkError, result.Kind);
    }

    private static HttpResponseMessage Challenge()
    {
        var r = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        r.Headers.TryAddWithoutValidation(
            "WWW-Authenticate", "Bearer realm=\"https://auth.example/token\",service=\"registry.example\"");
        return r;
    }
}
