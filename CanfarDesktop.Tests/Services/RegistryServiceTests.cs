using System.Net;
using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Services.ImageDiscovery;
using CanfarDesktop.Tests.Helpers;

namespace CanfarDesktop.Tests.Services;

/// <summary>
/// Searching the registry behind the platform. The interesting parts are the URL Harbor needs and the
/// bounds on how much of a shared service one keystroke may ask for.
/// </summary>
public class RegistryServiceTests
{
    private static RegistryService Service(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        => new(new HttpClient(new MockHttpMessageHandler(handler)));

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    /// <summary>A search answering with one repository, then one artifact with two tags.</summary>
    private static Func<HttpRequestMessage, Task<HttpResponseMessage>> OneRepository(List<string>? seen = null)
        => request =>
        {
            var url = request.RequestUri!.ToString();
            seen?.Add(url);

            if (url.Contains("/search?"))
                return Task.FromResult(Json("""
                    {"repository":[{"project_name":"skaha","repository_name":"skaha/astroml"}]}
                    """));

            return Task.FromResult(Json("""
                [{"tags":[{"name":"latest"},{"name":"24.04"}],"labels":[{"name":"notebook"},{"name":"gpu"}]}]
                """));
        };

    [Fact]
    public async Task ATaggedArtifactBecomesOneLaunchableReferencePerTag()
    {
        var images = await Service(OneRepository()).SearchAsync("images.canfar.net", "astroml", RegistryAuth.None);

        Assert.Equal(2, images.Count);
        Assert.Contains(images, i => i.Id == "images.canfar.net/skaha/astroml:latest");
        Assert.Contains(images, i => i.Id == "images.canfar.net/skaha/astroml:24.04");
    }

    /// <summary>
    /// Only labels that NAME a session type become types. An image labelled "gpu" is not launchable as a
    /// "gpu" session, and offering it as one produces a launch the platform refuses.
    /// </summary>
    [Fact]
    public async Task OnlySessionTypeLabelsBecomeTypes()
    {
        var images = await Service(OneRepository()).SearchAsync("images.canfar.net", "astroml", RegistryAuth.None);

        Assert.Equal(["notebook"], images[0].Types);
    }

    /// <summary>
    /// Harbor addresses a nested repository as ONE path segment, so its slash has to survive the proxy's
    /// decode and arrive still encoded — hence encoding the already-encoded form.
    /// </summary>
    [Fact]
    public void ANestedRepositoryIsDoubleEncoded()
    {
        var url = RegistryService.ArtifactsUrl("images.canfar.net", "skaha", "skaha/base/astro");

        Assert.Contains("/projects/skaha/repositories/base%252Fastro/artifacts", url);
        Assert.Contains("with_label=true", url);   // the labels are the whole reason for Harbor's API
    }

    [Fact]
    public async Task AnUntaggedArtifactIsSkipped()
    {
        // Addressable only by digest, which is not what the launch form takes — listing it would offer
        // something that cannot be launched.
        var service = Service(request => Task.FromResult(
            request.RequestUri!.ToString().Contains("/search?")
                ? Json("""{"repository":[{"project_name":"p","repository_name":"p/r"}]}""")
                : Json("""[{"tags":[],"labels":[{"name":"notebook"}]}]""")));

        Assert.Empty(await service.SearchAsync("host", "r", RegistryAuth.None));
    }

    /// <summary>
    /// A search across projects will routinely include one the user cannot see. Losing the other
    /// twenty-three to it would be the wrong answer.
    /// </summary>
    [Fact]
    public async Task ARepositoryThatCannotBeReadIsSkippedRatherThanFatal()
    {
        var service = Service(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("/search?"))
                return Task.FromResult(Json("""
                    {"repository":[
                      {"project_name":"a","repository_name":"a/one"},
                      {"project_name":"b","repository_name":"b/two"}]}
                    """));

            if (url.Contains("/projects/a/"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));

            return Task.FromResult(Json("""[{"tags":[{"name":"v1"}],"labels":[]}]"""));
        });

        var images = await service.SearchAsync("host", "x", RegistryAuth.None);

        Assert.Single(images);
        Assert.Equal("host/b/two:v1", images[0].Id);
    }

    /// <summary>
    /// Harbor's search happily returns hundreds. Past the first couple of dozen the user is re-typing
    /// their search, not reading — so the rest is traffic spent on a list nobody scrolls.
    /// </summary>
    [Fact]
    public async Task OnlySoManyRepositoriesAreEverOpened()
    {
        var opened = 0;
        var many = string.Join(",", Enumerable.Range(0, 100)
            .Select(i => $$"""{"project_name":"p","repository_name":"p/r{{i}}"}"""));

        var service = Service(request =>
        {
            if (request.RequestUri!.ToString().Contains("/search?"))
                return Task.FromResult(Json($$"""{"repository":[{{many}}]}"""));

            Interlocked.Increment(ref opened);
            return Task.FromResult(Json("[]"));
        });

        await service.SearchAsync("host", "r", RegistryAuth.None);

        Assert.Equal(RegistryService.MaxRepositories, opened);
    }

    /// <summary>An empty term would be a request to download the whole registry — the thing this avoids.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnEmptyTermIsRefused(string term)
    {
        var service = Service(_ => Task.FromResult(Json("{}")));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SearchAsync("host", term, RegistryAuth.None));
        Assert.Contains("Type something", ex.Message);
    }

    [Fact]
    public async Task WithNoHostThereIsNothingToSearch()
    {
        var service = Service(_ => Task.FromResult(Json("{}")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SearchAsync("  ", "x", RegistryAuth.None));
    }

    /// <summary>
    /// The CADC password is the wrong answer here often enough that saying so is worth a sentence.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task RejectedCredentialsNameTheOnesThatWork(HttpStatusCode status)
    {
        var service = Service(_ => Task.FromResult(new HttpResponseMessage(status)));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SearchAsync("host", "x", RegistryAuth.None));

        Assert.Contains("Harbor CLI secret", ex.Message);
        Assert.Contains("not your CADC password", ex.Message);
    }

    [Fact]
    public async Task CredentialsGoOnTheRequestWhenThereAreSome()
    {
        HttpRequestMessage? seen = null;
        var service = Service(request =>
        {
            seen = request;
            return Task.FromResult(Json("""{"repository":[]}"""));
        });

        await service.SearchAsync("host", "x", RegistryAuth.FromCredentials("me", "secret"));

        var header = Assert.Single(seen!.Headers.GetValues("Authorization"));
        Assert.Equal("Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("me:secret")), header);
    }

    /// <summary>A public project answers without them, and being able to try before finding a secret matters.</summary>
    [Fact]
    public async Task WithoutCredentialsTheRequestCarriesNone()
    {
        HttpRequestMessage? seen = null;
        var service = Service(request =>
        {
            seen = request;
            return Task.FromResult(Json("""{"repository":[]}"""));
        });

        await service.SearchAsync("host", "x", RegistryAuth.None);

        Assert.False(seen!.Headers.Contains("Authorization"));
    }

    [Theory]
    [InlineData(null, "secret")]
    [InlineData("me", "")]
    public void HalfACredentialIsNoCredential(string? username, string secret)
        => Assert.Null(RegistryAuth.FromCredentials(username, secret).Basic);

    // ── The model ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AnImagesProjectIsReadFromItsReference()
    {
        Assert.Equal("skaha", new RegistryImage("images.canfar.net/skaha/astroml:latest", []).Project);
        Assert.Null(new RegistryImage("shortname:tag", []).Project);
    }

    [Fact]
    public void AnImageIsLaunchableAsTheTypesItsLabelsDeclare()
    {
        var image = RegistryImage.FromLabels("host/p/n:t", ["notebook", "GPU", "carta"]);

        Assert.True(image.IsLaunchableAs("notebook"));
        Assert.True(image.IsLaunchableAs("CARTA"));
        Assert.False(image.IsLaunchableAs("gpu"));
        Assert.False(image.IsLaunchableAs("headless"));
    }
}
