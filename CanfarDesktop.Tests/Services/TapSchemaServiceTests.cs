using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Services;

namespace CanfarDesktop.Tests.Services;

/// <summary>
/// Assembling TAP_SCHEMA's three result sets into one schema, and the tools that read it.
///
/// The rows are the ones CADC actually returns (captured 2026-08-21), not invented ones: the
/// descriptions carry commas and embedded quotes, which is exactly where a hand-rolled CSV reader goes
/// wrong, and the captured calibrationLevel row has both.
/// </summary>
public class TapSchemaServiceTests
{
    private static SearchResults CapturedTables() => TAPService.ParseCsv(
        """
        table_name,description
        caom2.Observation,telescope observations
        caom2.Plane,"data products, with calibration level"
        """, null);

    private static SearchResults CapturedColumns() => TAPService.ParseCsv(
        """
        table_name,column_name,datatype,description,unit,ucd
        caom2.Plane,calibrationLevel,int,"IVOA ObsCore calibration level + extensions (-1,0,1,2,3,4)",,
        caom2.Plane,position_bounds,clob,coverage on the sky,,pos.outline;obs.field
        caom2.Plane,time_exposure,double,actual exposure time,d,time.duration;obs.exposure
        caom2.Observation,target_name,char,target name,,meta.id;src
        """, null);

    private static SearchResults CapturedKeys() => TAPService.ParseCsv(
        """
        from_table,target_table,from_column,target_column,description
        caom2.Plane,caom2.Observation,obsID,obsID,the standard way to join a plane to its observation
        """, null);

    [Fact]
    public void ColumnsLandOnTheirTables()
    {
        var schema = TapSchemaService.BuildSchema(CapturedTables(), CapturedColumns(), CapturedKeys());

        Assert.Equal(2, schema.Tables.Count);
        Assert.Equal(3, schema.Table("caom2.Plane")!.Columns.Count);
        Assert.Single(schema.Table("caom2.Observation")!.Columns);
    }

    /// <summary>The description with a comma and parentheses survives intact — the whole point of the capture.</summary>
    [Fact]
    public void ADescriptionWithCommasSurvives()
    {
        var schema = TapSchemaService.BuildSchema(CapturedTables(), CapturedColumns(), CapturedKeys());
        var calibration = schema.Table("caom2.Plane")!.Columns.Single(c => c.Name == "calibrationLevel");

        Assert.Equal("IVOA ObsCore calibration level + extensions (-1,0,1,2,3,4)", calibration.Description);
        Assert.Equal("data products, with calibration level", schema.Table("caom2.Plane")!.Description);
    }

    [Fact]
    public void UnitsAndUcdsAreCarried()
    {
        var schema = TapSchemaService.BuildSchema(CapturedTables(), CapturedColumns(), CapturedKeys());
        var exposure = schema.Table("caom2.Plane")!.Columns.Single(c => c.Name == "time_exposure");

        Assert.Equal("d", exposure.Unit);
        Assert.Equal("time.duration;obs.exposure", exposure.Ucd);
        // An omitted field costs that field, not the row.
        Assert.Equal(string.Empty, schema.Table("caom2.Plane")!.Columns.Single(c => c.Name == "calibrationLevel").Ucd);
    }

    [Fact]
    public void TableLookupIsCaseInsensitive()
    {
        var schema = TapSchemaService.BuildSchema(CapturedTables(), CapturedColumns(), CapturedKeys());

        Assert.NotNull(schema.Table("CAOM2.PLANE"));
        Assert.NotNull(schema.Table("caom2.plane"));
        Assert.Null(schema.Table("caom2.Planet"));
    }

    [Fact]
    public void DeclaredJoinsAreFoundFromEitherEnd()
    {
        var schema = TapSchemaService.BuildSchema(CapturedTables(), CapturedColumns(), CapturedKeys());

        Assert.Single(schema.KeysTouching("caom2.Plane"));
        Assert.Single(schema.KeysTouching("caom2.Observation"));
        Assert.Empty(schema.KeysTouching("caom2.Artifact"));
    }

    /// <summary>
    /// A column whose table the `tables` query did not list still belongs to something. Dropping it
    /// would hide it entirely — and the checker would then call a real column missing.
    /// </summary>
    [Fact]
    public void AColumnOfAnUnlistedTableStillAppears()
    {
        var columns = TAPService.ParseCsv(
            """
            table_name,column_name,datatype,description,unit,ucd
            caom2.Artifact,uri,char,artifact uri,,
            """, null);

        var schema = TapSchemaService.BuildSchema(CapturedTables(), columns, CapturedKeys());

        Assert.NotNull(schema.Table("caom2.Artifact"));
        Assert.Single(schema.Table("caom2.Artifact")!.Columns);
    }

    /// <summary>
    /// Fields are read by NAME, not position: TAP services differ on the case they echo back for
    /// TAP_SCHEMA columns, and reading by position would transpose two fields the day one returned
    /// them in another order.
    /// </summary>
    [Fact]
    public void FieldsAreReadByNameWhateverTheirCase()
    {
        var columns = TAPService.ParseCsv(
            """
            TABLE_NAME,COLUMN_NAME,DATATYPE,DESCRIPTION,UNIT,UCD
            caom2.Plane,energy_bandpassName,char,the filter,,em.filter
            """, null);

        var schema = TapSchemaService.BuildSchema(CapturedTables(), columns, CapturedKeys());
        var column = schema.Table("caom2.Plane")!.Columns.Single();

        Assert.Equal("energy_bandpassName", column.Name);
        Assert.Equal("em.filter", column.Ucd);
    }

    [Fact]
    public void AnEmptySchemaSaysSo()
    {
        var empty = TapSchemaService.BuildSchema(
            new SearchResults(), new SearchResults(), new SearchResults());

        Assert.True(empty.IsEmpty);
    }
}
