using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Read;
using CanfarDesktop.Mcp.Wire;
using CanfarDesktop.Services;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>describe_tap_schema and validate_adql_query — reading the service's own schema, and checking against it.</summary>
public class TapSchemaToolsTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);
    private static McpToolContext Ctx() => McpToolContext.ForExternal("c1", Guid.NewGuid());

    private static T Payload<T>(ToolResult result)
        => JsonSerializer.Deserialize<T>(Assert.IsType<DataResult>(result).Json, McpJson.Options)!;

    private static string Failure(ToolResult result) => Assert.IsType<FailedResult>(result).Reason.Description;

    private static TapColumn Col(string name, string description = "", string unit = "", string ucd = "")
        => new(name, "char", description, unit, ucd);

    private static TapSchema Schema() => new()
    {
        Tables =
        [
            new TapTable("caom2.Observation", "telescope observations",
                [Col("obsID"), Col("observationID"), Col("collection", "the archive collection"), Col("target_name", "target name", ucd: "meta.id;src")]),
            new TapTable("caom2.Plane", "data products",
                [Col("obsID"), Col("calibrationLevel", "IVOA ObsCore calibration level"), Col("time_exposure", "actual exposure time", "d", "time.duration;obs.exposure")]),
        ],
        Keys =
        [
            new TapKey("caom2.Plane", "caom2.Observation", "obsID", "obsID", "the standard way to join"),
        ],
    };

    private static DescribeTapSchemaTool Describe() => new(_ => Task.FromResult(Schema()));
    private static ValidateAdqlQueryTool Validate() => new(_ => Task.FromResult(Schema()));

    // ── describe_tap_schema ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WithNoArgumentsItListsTheTablesAndTheJoinsWithoutFourHundredColumns()
    {
        var output = Payload<DescribeTapSchemaTool.Output>(await Describe().InvokeAsync(Args("{}"), Ctx(), default));

        Assert.Equal(2, output.Tables.Count);
        Assert.All(output.Tables, t => Assert.Null(t.Columns));   // the listing omits columns on purpose
        Assert.All(output.Tables, t => Assert.True(t.ColumnCount > 0));
        Assert.Single(output.Keys);
        Assert.Equal("the standard way to join", output.Keys[0].Description);
    }

    [Fact]
    public async Task OneTableComesBackWithItsColumnsAndOnlyItsOwnJoins()
    {
        var output = Payload<DescribeTapSchemaTool.Output>(
            await Describe().InvokeAsync(Args("""{"table":"caom2.Plane"}"""), Ctx(), default));

        var table = Assert.Single(output.Tables);
        Assert.Equal("caom2.Plane", table.Name);
        Assert.Equal(3, table.Columns!.Count);

        var exposure = table.Columns.Single(c => c.Name == "time_exposure");
        Assert.Equal("d", exposure.Unit);
        Assert.Equal("time.duration;obs.exposure", exposure.Ucd);
        Assert.Single(output.Keys);
    }

    [Fact]
    public async Task ATableNameIsMatchedWhateverItsCase()
    {
        var output = Payload<DescribeTapSchemaTool.Output>(
            await Describe().InvokeAsync(Args("""{"table":"CAOM2.PLANE"}"""), Ctx(), default));

        Assert.Equal("caom2.Plane", Assert.Single(output.Tables).Name);
    }

    [Fact]
    public async Task AnUnknownTableNamesTheTablesThatExist()
    {
        var message = Failure(await Describe().InvokeAsync(Args("""{"table":"caom2.Planet"}"""), Ctx(), default));

        Assert.Contains("caom2.Planet", message);
        Assert.Contains("caom2.Plane", message);
    }

    /// <summary>
    /// Search covers name, description AND ucd. The UCD is the point: it says what a number MEANS
    /// independent of what the column is called, so "exposure" finds the column whose name is
    /// time_exposure and the one whose only clue is time.duration;obs.exposure.
    /// </summary>
    [Theory]
    [InlineData("calibration", "calibrationLevel")]
    [InlineData("collection", "collection")]
    [InlineData("meta.id", "target_name")]
    public async Task SearchFindsAColumnByNameDescriptionOrUcd(string needle, string expected)
    {
        var output = Payload<DescribeTapSchemaTool.Output>(
            await Describe().InvokeAsync(Args($$"""{"search":"{{needle}}"}"""), Ctx(), default));

        var names = output.Tables.SelectMany(t => t.Columns ?? []).Select(c => c.Name).ToList();
        Assert.Contains(expected, names);
    }

    [Fact]
    public async Task AnEmptySchemaIsReportedRatherThanAnsweredWithNothing()
    {
        var tool = new DescribeTapSchemaTool(_ => Task.FromResult(new TapSchema()));
        Assert.Contains("no TAP_SCHEMA tables", Failure(await tool.InvokeAsync(Args("{}"), Ctx(), default)));
    }

    // ── validate_adql_query ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AQueryItCannotFaultComesBackValid()
    {
        var output = Payload<ValidateAdqlQueryTool.Output>(await Validate().InvokeAsync(
            Args("""{"adql":"SELECT o.observationID FROM caom2.Observation AS o"}"""), Ctx(), default));

        Assert.True(output.Valid);
        Assert.True(output.SchemaLoaded);
        Assert.Empty(output.Problems);
    }

    [Fact]
    public async Task TheAmbiguousQualifierCadcRejectsIsCaughtWithTheTextAndTheFix()
    {
        var output = Payload<ValidateAdqlQueryTool.Output>(await Validate().InvokeAsync(Args(
            """{"adql":"SELECT Observation.observationID FROM caom2.Observation JOIN caom2.Plane ON Plane.obsID=Observation.obsID"}"""),
            Ctx(), default));

        Assert.False(output.Valid);
        var problem = output.Problems[0];

        // The offending text is quoted back — an offset is something a caller has to count to.
        Assert.Equal("Plane.obsID", problem.Text);
        Assert.Contains("ambiguous", problem.Message);
        Assert.Equal("caom2.Plane.obsID", problem.Fix);
    }

    [Fact]
    public async Task AColumnTheTableDoesNotHaveIsReported()
    {
        var output = Payload<ValidateAdqlQueryTool.Output>(await Validate().InvokeAsync(
            Args("""{"adql":"SELECT o.collection_name FROM caom2.Observation AS o"}"""), Ctx(), default));

        Assert.False(output.Valid);
        Assert.Equal("o.collection_name", output.Problems[0].Text);
        Assert.Contains("no column", output.Problems[0].Message);
        // The ObsCore spelling of a CAOM2 column: the near miss is named rather than guessed at.
        Assert.Equal("o.collection", output.Problems[0].Fix);
    }

    [Fact]
    public async Task WithNoSchemaItSaysSoRatherThanCallingEveryTableUnknown()
    {
        var tool = new ValidateAdqlQueryTool(_ => Task.FromResult(new TapSchema()));
        var output = Payload<ValidateAdqlQueryTool.Output>(await tool.InvokeAsync(
            Args("""{"adql":"SELECT x.y FROM caom2.Nothing AS x"}"""), Ctx(), default));

        Assert.True(output.Valid);          // nothing could be SHOWN to be wrong
        Assert.False(output.SchemaLoaded);  // and the caller is told why
        Assert.Empty(output.Problems);
    }

    [Fact]
    public async Task AnEmptyQueryIsRefused()
        => Assert.Contains("adql is required", Failure(await Validate().InvokeAsync(Args("""{"adql":"   "}"""), Ctx(), default)));

    [Fact]
    public void BothToolsAreReadOnlyAndAgentSafe()
    {
        foreach (IMcpTool tool in new IMcpTool[] { Describe(), Validate() })
        {
            Assert.Equal(McpVerbClass.Read, tool.VerbClass);
            Assert.True(tool.AgentSafe);
        }
    }
}
