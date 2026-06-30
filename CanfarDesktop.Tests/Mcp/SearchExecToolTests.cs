using System.Text;
using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Read;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class SearchExecToolTests
{
    private static readonly McpToolContext Ctx = McpToolContext.ForExternal("Claude/1.0", Guid.Empty);

    private static JsonObject Data(ToolResult r) =>
        (JsonObject)JsonValue.Parse(Encoding.UTF8.GetString(Assert.IsType<DataResult>(r).Json));

    private static string Str(JsonValue? v) => ((JsonString)v!).Value;
    private static long Int(JsonValue? v) => ((JsonInt)v!).Value;
    private static double Num(JsonValue? v) => v switch
    {
        JsonDouble d => d.Value,
        JsonInt i => i.Value,
        _ => throw new InvalidCastException($"not a number: {v?.GetType().Name}")
    };

    // ---- resolve_target ----

    [Fact]
    public async Task ResolveTarget_ReturnsCoordinates()
    {
        string? capturedTarget = null, capturedService = null;
        var tool = new ResolveTargetTool((t, s, _) =>
        {
            capturedTarget = t;
            capturedService = s;
            return Task.FromResult<ResolverResult?>(new ResolverResult
            {
                Target = "M31", RA = 10.6847, Dec = 41.269, CoordSys = "ICRS",
                ObjectType = "Galaxy", Service = "SIMBAD"
            });
        });

        var data = Data(await tool.InvokeAsync(JsonValue.Parse("""{"target":"M31","service":"SIMBAD"}"""), Ctx, default));

        Assert.Equal("M31", capturedTarget);
        Assert.Equal("SIMBAD", capturedService);
        Assert.Equal("M31", Str(data["target"]));
        Assert.Equal(10.6847, Num(data["ra"]), 4);
        Assert.Equal(41.269, Num(data["dec"]), 4);
        Assert.Equal("Galaxy", Str(data["objectType"]));
    }

    [Fact]
    public async Task ResolveTarget_DefaultsServiceToAll()
    {
        string? capturedService = null;
        var tool = new ResolveTargetTool((t, s, _) =>
        {
            capturedService = s;
            return Task.FromResult<ResolverResult?>(new ResolverResult { Target = t, RA = 1, Dec = 2 });
        });

        await tool.InvokeAsync(JsonValue.Parse("""{"target":"NGC 224"}"""), Ctx, default);

        Assert.Equal("ALL", capturedService);
    }

    [Fact]
    public async Task ResolveTarget_Unresolved_TargetNotResolved()
    {
        var tool = new ResolveTargetTool((_, _, _) => Task.FromResult<ResolverResult?>(null));
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"target":"ZZZ-not-a-target"}"""), Ctx, default);
        Assert.IsType<TargetNotResolved>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task ResolveTarget_EmptyTarget_InvalidArgument()
    {
        var tool = new ResolveTargetTool((_, _, _) => Task.FromResult<ResolverResult?>(new ResolverResult()));
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"target":"  "}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    // ---- search_observations ----

    private static SearchResults SampleResults(int rowCount)
    {
        var results = new SearchResults { Columns = { "observationID", "collection" } };
        for (var i = 0; i < rowCount; i++)
        {
            var row = new SearchResultRow();
            row.Values["observationID"] = $"obs{i}";
            row.Values["collection"] = "CFHT";
            results.Rows.Add(row);
        }
        return results;
    }

    [Fact]
    public async Task SearchObservations_PassesAdqlThrough()
    {
        string? capturedAdql = null;
        var tool = new SearchObservationsTool(
            (adql, _, _) => { capturedAdql = adql; return Task.FromResult(SampleResults(2)); },
            (_, _, _) => Task.FromResult<ResolverResult?>(null));

        var data = Data(await tool.InvokeAsync(JsonValue.Parse("""{"adql":"SELECT 1 FROM caom2.Plane"}"""), Ctx, default));

        Assert.Equal("SELECT 1 FROM caom2.Plane", capturedAdql);
        Assert.Equal("SELECT 1 FROM caom2.Plane", Str(data["adql"]));
        var columns = ((JsonArray)data["columns"]!).Items.Select(Str).ToList();
        Assert.Equal(new[] { "observationID", "collection" }, columns);
        Assert.Equal(2, Int(data["returnedRows"]));
        Assert.False(((JsonBool)data["truncated"]!).Value); // 2 rows, well under the cap

        var firstRow = ((JsonArray)((JsonArray)data["rows"]!).Items[0]).Items.Select(Str).ToList();
        Assert.Equal(new[] { "obs0", "CFHT" }, firstRow);
    }

    [Fact]
    public async Task SearchObservations_BuildsConeFromRaDec()
    {
        string? capturedAdql = null;
        var tool = new SearchObservationsTool(
            (adql, _, _) => { capturedAdql = adql; return Task.FromResult(SampleResults(1)); },
            (_, _, _) => Task.FromResult<ResolverResult?>(null));

        await tool.InvokeAsync(JsonValue.Parse("""{"ra":10.68,"dec":41.27,"radius":0.1}"""), Ctx, default);

        Assert.Contains("CIRCLE('ICRS', 10.68, 41.27, 0.1)", capturedAdql);
        Assert.Contains("INTERSECTS(", capturedAdql);
    }

    [Fact]
    public async Task SearchObservations_ResolvesTargetForCone()
    {
        string? capturedAdql = null;
        string? resolvedTarget = null;
        var tool = new SearchObservationsTool(
            (adql, _, _) => { capturedAdql = adql; return Task.FromResult(SampleResults(1)); },
            (t, _, _) =>
            {
                resolvedTarget = t;
                return Task.FromResult<ResolverResult?>(new ResolverResult { Target = t, RA = 10.68, Dec = 41.27 });
            });

        await tool.InvokeAsync(JsonValue.Parse("""{"target":"M31"}"""), Ctx, default);

        Assert.Equal("M31", resolvedTarget);
        Assert.Contains("CIRCLE('ICRS', 10.68, 41.27,", capturedAdql);
    }

    [Fact]
    public async Task SearchObservations_TargetUnresolved_TargetNotResolved()
    {
        var tool = new SearchObservationsTool(
            (_, _, _) => Task.FromResult(SampleResults(0)),
            (_, _, _) => Task.FromResult<ResolverResult?>(null));

        var result = await tool.InvokeAsync(JsonValue.Parse("""{"target":"ZZZ"}"""), Ctx, default);
        Assert.IsType<TargetNotResolved>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task SearchObservations_NoAdqlNoSpatial_InvalidArgument()
    {
        var tool = new SearchObservationsTool(
            (_, _, _) => Task.FromResult(SampleResults(0)),
            (_, _, _) => Task.FromResult<ResolverResult?>(null));

        var result = await tool.InvokeAsync(JsonValue.Parse("""{"maxRows":5}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task SearchObservations_CapsMaxRowsAt1000()
    {
        int capturedMax = -1;
        var tool = new SearchObservationsTool(
            (_, max, _) => { capturedMax = max; return Task.FromResult(SampleResults(0)); },
            (_, _, _) => Task.FromResult<ResolverResult?>(null));

        await tool.InvokeAsync(JsonValue.Parse("""{"adql":"SELECT 1","maxRows":99999}"""), Ctx, default);

        // Rows are capped at 1000; the backend is asked for cap+1 (over-fetch one row to detect truncation).
        Assert.Equal(SearchObservationsTool.MaxRowsCap + 1, capturedMax);
        Assert.Equal(1001, capturedMax);
    }

    [Fact]
    public async Task SearchObservations_TrimsToCap_AndFlagsTruncated()
    {
        // Backend returns more than maxRows → output is capped AND truncated is set, so the caller knows
        // the sample is incomplete (the silent-truncation trap a survey scientist would otherwise hit).
        var tool = new SearchObservationsTool(
            (_, _, _) => Task.FromResult(SampleResults(5)),
            (_, _, _) => Task.FromResult<ResolverResult?>(null));

        var data = Data(await tool.InvokeAsync(JsonValue.Parse("""{"adql":"SELECT 1","maxRows":3}"""), Ctx, default));

        Assert.Equal(3, Int(data["returnedRows"]));
        Assert.True(((JsonBool)data["truncated"]!).Value);
        Assert.Equal(3, ((JsonArray)data["rows"]!).Items.Count);
    }
}