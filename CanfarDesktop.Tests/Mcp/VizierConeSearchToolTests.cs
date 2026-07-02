using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Read;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class VizierConeSearchToolTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);

    private static McpToolContext Ctx() => McpToolContext.ForExternal("c1", Guid.NewGuid());

    private static VizierConeSearchTool Tool(
        Func<VizierConeSearchRequest, (IReadOnlyList<string>, IReadOnlyList<IReadOnlyList<string>>)> search,
        Action<VizierConeSearchRequest>? capture = null)
        => new((req, _) =>
        {
            capture?.Invoke(req);
            return Task.FromResult(search(req));
        });

    private static (IReadOnlyList<string>, IReadOnlyList<IReadOnlyList<string>>) Rows(int count)
        => (new[] { "Name" }, Enumerable.Range(0, count).Select(i => (IReadOnlyList<string>)new[] { $"V{i}" }).ToList());

    [Fact]
    public async Task Defaults_Raj2000Dej2000_MaxRec500_AndArcsecToDegrees()
    {
        VizierConeSearchRequest? seen = null;
        var result = await Tool(_ => Rows(2), req => seen = req).InvokeAsync(
            Args("""{"catalogue":"V/97/catalog","raDeg":298.4438,"decDeg":18.7792,"radiusArcsec":180}"""),
            Ctx(), default);

        Assert.IsType<DataResult>(result);
        Assert.Equal("V/97/catalog", seen!.Catalogue);
        Assert.Equal("RAJ2000", seen.RaColumn);
        Assert.Equal("DEJ2000", seen.DecColumn);
        Assert.Equal(500, seen.MaxRec);
        Assert.Equal(180.0 / 3600.0, seen.RadiusDeg, 12);
    }

    [Fact]
    public async Task Overrides_ColumnsAndMaxRec_PassThrough()
    {
        VizierConeSearchRequest? seen = null;
        await Tool(_ => Rows(0), req => seen = req).InvokeAsync(
            Args("""{"catalogue":"I/355/gaiadr3","raDeg":1,"decDeg":2,"radiusArcsec":60,"raColumn":"ra","decColumn":"dec","maxRec":1000}"""),
            Ctx(), default);

        Assert.Equal("ra", seen!.RaColumn);
        Assert.Equal("dec", seen.DecColumn);
        Assert.Equal(1000, seen.MaxRec);
    }

    [Fact]
    public async Task Output_CarriesHeadersRowsCount_AndTruncationHint()
    {
        var result = await Tool(_ => Rows(10)).InvokeAsync(
            Args("""{"catalogue":"V/97/catalog","raDeg":1,"decDeg":2,"radiusArcsec":60,"maxRec":10}"""),
            Ctx(), default);

        var doc = JsonDocument.Parse(Assert.IsType<DataResult>(result).Json).RootElement;
        Assert.Equal("V/97/catalog", doc.GetProperty("catalogue").GetString());
        Assert.Equal("Name", doc.GetProperty("headers")[0].GetString());
        Assert.Equal(10, doc.GetProperty("rowCount").GetInt32());
        Assert.Equal(10, doc.GetProperty("rows").GetArrayLength());
        Assert.True(doc.GetProperty("probablyTruncated").GetBoolean()); // hit the cap → probably more
    }

    [Fact]
    public async Task UnderTheCap_NotProbablyTruncated()
    {
        var result = await Tool(_ => Rows(3)).InvokeAsync(
            Args("""{"catalogue":"V/97/catalog","raDeg":1,"decDeg":2,"radiusArcsec":60,"maxRec":10}"""),
            Ctx(), default);

        var doc = JsonDocument.Parse(Assert.IsType<DataResult>(result).Json).RootElement;
        Assert.False(doc.GetProperty("probablyTruncated").GetBoolean());
    }

    [Theory]
    [InlineData("""{"raDeg":1,"decDeg":2,"radiusArcsec":60}""")]                              // no catalogue
    [InlineData("""{"catalogue":"  ","raDeg":1,"decDeg":2,"radiusArcsec":60}""")]             // blank catalogue
    [InlineData("""{"catalogue":"V/97","decDeg":2,"radiusArcsec":60}""")]                     // no raDeg
    [InlineData("""{"catalogue":"V/97","raDeg":1,"decDeg":2}""")]                             // no radius
    [InlineData("""{"catalogue":"V/97","raDeg":1,"decDeg":2,"radiusArcsec":-1}""")]           // negative radius
    [InlineData("""{"catalogue":"V/97","raDeg":1,"decDeg":2,"radiusArcsec":60,"maxRec":0}""")]    // maxRec < 1
    [InlineData("""{"catalogue":"V/97","raDeg":1,"decDeg":2,"radiusArcsec":60,"maxRec":5001}""")] // maxRec > cap
    public async Task InvalidArguments_AreRejected(string argsJson)
    {
        var result = await Tool(_ => Rows(0)).InvokeAsync(Args(argsJson), Ctx(), default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task BackendFailure_SurfacesAsPrefixedBackendError()
    {
        var tool = new VizierConeSearchTool((_, _) =>
            throw new HttpRequestException("exhausted all VizieR mirrors"));
        var result = await tool.InvokeAsync(
            Args("""{"catalogue":"V/97","raDeg":1,"decDeg":2,"radiusArcsec":60}"""), Ctx(), default);

        var reason = Assert.IsType<BackendError>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Contains("vizier_cone_search: exhausted all VizieR mirrors", reason.Description);
    }

    [Fact]
    public void Descriptor_NameAndReadDefaults()
    {
        var tool = Tool(_ => Rows(0));
        Assert.Equal("vizier_cone_search", tool.Descriptor.Name);
        Assert.Equal(McpVerbClass.Read, tool.VerbClass);
        Assert.True(tool.AgentSafe);
        Assert.Contains("Cone-search a VizieR catalogue at CDS", tool.Descriptor.Description);
    }
}
