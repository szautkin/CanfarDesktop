using System.Text;
using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Caom2;
using CanfarDesktop.Services;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Read;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class Caom2ReadToolTests
{
    private static readonly McpToolContext Ctx = McpToolContext.ForExternal("Claude/1.0", Guid.Empty);

    private static JsonObject Data(ToolResult r) => (JsonObject)JsonValue.Parse(Encoding.UTF8.GetString(Assert.IsType<DataResult>(r).Json));
    private static ToolFailureReason Reason(ToolResult r) => Assert.IsType<FailedResult>(r).Reason;

    // ── get_observation_caom2 ──────────────────────────────────────────────────

    [Fact]
    public async Task GetObservationCaom2_Success_ReturnsSummary()
    {
        var observation = new CAOM2Observation
        {
            Collection = "CFHT",
            ObservationID = "1234567p",
            Intent = "science",
            Algorithm = "exposure",
            Proposal = new Caom2Proposal { Id = "08AC01", Pi = "Smith", Title = "Deep survey" },
            Target = new Caom2Target { Name = "M31", Type = "field", Redshift = 0.0 },
            Telescope = new Caom2Telescope { Name = "CFHT 3.6m" },
            Instrument = new Caom2Instrument { Name = "MegaPrime" },
            Planes = new[]
            {
                new Caom2Plane { ProductID = "1234567p", DataProductType = "image", CalibrationLevel = 2, Quality = "good" },
            },
        };
        var tool = new GetObservationCaom2Tool(_ =>
            Task.FromResult(new Caom2Result(Caom2Status.Success, observation, null)));

        var data = Data(await tool.InvokeAsync(JsonValue.Parse("""{"publisherId":"ivo://cadc/CFHT?1234567p"}"""), Ctx, default));

        Assert.Equal("CFHT", ((JsonString)data["collection"]!).Value);
        Assert.Equal("1234567p", ((JsonString)data["observationId"]!).Value);
        Assert.Equal("M31", ((JsonString)data["targetName"]!).Value);
        Assert.Equal("MegaPrime", ((JsonString)data["instrumentName"]!).Value);
        Assert.Equal("08AC01", ((JsonString)data["proposalId"]!).Value);
        Assert.Equal(1, ((JsonInt)data["planeCount"]!).Value);
        var plane = (JsonObject)((JsonArray)data["planes"]!).Items[0];
        Assert.Equal("image", ((JsonString)plane["dataProductType"]!).Value);
        Assert.Equal(2, ((JsonInt)plane["calibrationLevel"]!).Value);
    }

    [Fact]
    public async Task GetObservationCaom2_AuthRequired_MapsToAuthRequired()
    {
        var tool = new GetObservationCaom2Tool(_ =>
            Task.FromResult(new Caom2Result(Caom2Status.AuthRequired, null, "This observation requires CADC sign-in.")));
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"publisherId":"ivo://cadc/X"}"""), Ctx, default);
        Assert.IsType<AuthRequired>(Reason(result));
    }

    [Fact]
    public async Task GetObservationCaom2_NotFound_MapsToUnknownTarget()
    {
        var tool = new GetObservationCaom2Tool(_ =>
            Task.FromResult(new Caom2Result(Caom2Status.NotFound, null, "Observation not found.")));
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"publisherId":"ivo://cadc/missing"}"""), Ctx, default);
        Assert.IsType<UnknownTarget>(Reason(result));
    }

    [Fact]
    public async Task GetObservationCaom2_InvalidId_MapsToInvalidArgument()
    {
        var tool = new GetObservationCaom2Tool(_ =>
            Task.FromResult(new Caom2Result(Caom2Status.InvalidId, null, "Cannot derive an observation URI.")));
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"publisherId":"garbage"}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Reason(result));
    }

    [Fact]
    public async Task GetObservationCaom2_ServerError_MapsToBackendError()
    {
        var tool = new GetObservationCaom2Tool(_ =>
            Task.FromResult(new Caom2Result(Caom2Status.ServerError, null, "HTTP 500.")));
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"publisherId":"ivo://cadc/X"}"""), Ctx, default);
        Assert.IsType<BackendError>(Reason(result));
    }

    [Fact]
    public async Task GetObservationCaom2_MissingPublisherId_InvalidArgument()
    {
        var tool = new GetObservationCaom2Tool(_ => Task.FromResult(new Caom2Result(Caom2Status.Success, null, null)));
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"publisherId":""}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Reason(result));
    }

    // ── get_data_links ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDataLinks_ReturnsLinkLists()
    {
        var dataLink = new DataLinkResult
        {
            DownloadUrl = "https://ws.cadc/download?id=X",
            DirectFiles =
            {
                new DataLinkFile { Url = "https://ws.cadc/files/1234567p.fits.fz", ContentType = "application/fits", Description = "science" },
            },
            Previews = { "https://ws.cadc/preview/1234567p.png" },
            Thumbnails = { "https://ws.cadc/thumb/1234567p.png" },
        };
        var tool = new GetDataLinksTool(_ => Task.FromResult(dataLink));

        var data = Data(await tool.InvokeAsync(JsonValue.Parse("""{"publisherId":"ivo://cadc/CFHT?1234567p"}"""), Ctx, default));

        Assert.Equal("https://ws.cadc/download?id=X", ((JsonString)data["downloadUrl"]!).Value);
        Assert.Equal(1, ((JsonInt)data["directFileCount"]!).Value);
        var file = (JsonObject)((JsonArray)data["directFiles"]!).Items[0];
        Assert.Equal("https://ws.cadc/files/1234567p.fits.fz", ((JsonString)file["url"]!).Value);
        Assert.Equal("1234567p.fits.fz", ((JsonString)file["filename"]!).Value);
        Assert.Equal(1, ((JsonInt)data["previewCount"]!).Value);
        Assert.Equal("https://ws.cadc/preview/1234567p.png", ((JsonString)((JsonArray)data["previews"]!).Items[0]).Value);
        Assert.Equal(1, ((JsonInt)data["thumbnailCount"]!).Value);
        Assert.Equal("https://ws.cadc/thumb/1234567p.png", ((JsonString)((JsonArray)data["thumbnails"]!).Items[0]).Value);
    }

    [Fact]
    public async Task GetDataLinks_Empty_ReturnsZeroCounts()
    {
        var tool = new GetDataLinksTool(_ => Task.FromResult(new DataLinkResult()));
        var data = Data(await tool.InvokeAsync(JsonValue.Parse("""{"publisherId":"ivo://cadc/X"}"""), Ctx, default));
        Assert.Equal(0, ((JsonInt)data["directFileCount"]!).Value);
        Assert.Equal(0, ((JsonInt)data["previewCount"]!).Value);
        Assert.Equal(0, ((JsonInt)data["thumbnailCount"]!).Value);
        // DownloadUrl is null on an empty result → omitted by WhenWritingNull.
        Assert.Null(data["downloadUrl"]);
    }

    [Fact]
    public async Task GetDataLinks_MissingPublisherId_InvalidArgument()
    {
        var tool = new GetDataLinksTool(_ => Task.FromResult(new DataLinkResult()));
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"publisherId":""}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Reason(result));
    }
}
