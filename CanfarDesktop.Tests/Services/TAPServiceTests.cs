using System.Reflection;
using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Services;

namespace CanfarDesktop.Tests.Services;

public class TAPServiceTests
{
    private static SearchResults ParseCsv(string csv)
    {
        var method = typeof(TAPService).GetMethod("ParseCsv",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (SearchResults)method!.Invoke(null, [csv, "test query"])!;
    }

    private static List<DataTrainRow> ParseDataTrainCsv(string csv)
    {
        var method = typeof(TAPService).GetMethod("ParseDataTrainCsv",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (List<DataTrainRow>)method!.Invoke(null, [csv])!;
    }

    [Fact]
    public void ParseCsv_ParsesHeadersAndRows()
    {
        var csv = "col1,col2,col3\nval1,val2,val3\nval4,val5,val6\n";
        var result = ParseCsv(csv);

        Assert.Equal(3, result.Columns.Count);
        Assert.Equal("col1", result.Columns[0]);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("val1", result.Rows[0].Get("col1"));
        Assert.Equal("val6", result.Rows[1].Get("col3"));
    }

    [Fact]
    public void ParseCsv_HandlesQuotedFields()
    {
        var csv = "name,description\nM31,\"Andromeda Galaxy, spiral\"\n";
        var result = ParseCsv(csv);

        Assert.Single(result.Rows);
        Assert.Equal("Andromeda Galaxy, spiral", result.Rows[0].Get("description"));
    }

    [Fact]
    public void ParseCsv_HandlesEscapedQuotes()
    {
        var csv = "name,note\ntest,\"says \"\"hello\"\"\"\n";
        var result = ParseCsv(csv);

        Assert.Equal("says \"hello\"", result.Rows[0].Get("note"));
    }

    [Fact]
    public void ParseCsv_SkipsMismatchedRows()
    {
        var csv = "a,b,c\n1,2,3\n1,2\n4,5,6\n";
        var result = ParseCsv(csv);

        Assert.Equal(2, result.Rows.Count); // middle row skipped
        Assert.Equal("1", result.Rows[0].Get("a"));
        Assert.Equal("4", result.Rows[1].Get("a"));
    }

    [Fact]
    public void ParseCsv_HandlesCRLF()
    {
        var csv = "a,b\r\n1,2\r\n3,4\r\n";
        var result = ParseCsv(csv);

        Assert.Equal(2, result.Rows.Count);
    }

    [Fact]
    public void ParseCsv_EmptyInput_ReturnsEmptyResult()
    {
        var result = ParseCsv("");
        Assert.Empty(result.Columns);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void ParseCsv_HeaderOnly_ReturnsNoRows()
    {
        var result = ParseCsv("a,b,c\n");
        Assert.Equal(3, result.Columns.Count);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void ParseCsv_StoresQuery()
    {
        var result = ParseCsv("a\n1\n");
        Assert.Equal("test query", result.Query);
    }

    // Data train CSV parser
    [Fact]
    public void ParseDataTrainCsv_ParsesDirectlyToDataTrainRows()
    {
        var csv = "energy_emBand,collection,instrument_name,energy_bandpassName,calibrationLevel,dataProductType,type\n" +
                  "Optical,CFHT,MegaCam,g,1,image,SCIENCE\n" +
                  "Radio,JCMT,SCUBA-2,850,2,image,SCIENCE\n";

        var rows = ParseDataTrainCsv(csv);

        Assert.Equal(2, rows.Count);

        Assert.Equal("Optical", rows[0].Band);
        Assert.Equal("CFHT", rows[0].Collection);
        Assert.Equal("MegaCam", rows[0].Instrument);
        Assert.Equal("g", rows[0].Filter);
        Assert.Equal("1", rows[0].CalibrationLevel);
        Assert.Equal("image", rows[0].DataProductType);
        Assert.Equal("SCIENCE", rows[0].ObservationType);

        Assert.Equal("Radio", rows[1].Band);
        Assert.Equal("JCMT", rows[1].Collection);
    }

    [Fact]
    public void ParseDataTrainCsv_SkipsRowsWithFewerThan7Fields()
    {
        var csv = "a,b,c,d,e,f,g\n1,2,3\n1,2,3,4,5,6,7\n";
        var rows = ParseDataTrainCsv(csv);
        Assert.Single(rows);
    }

    [Fact]
    public void ParseDataTrainCsv_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(ParseDataTrainCsv(""));
        Assert.Empty(ParseDataTrainCsv("header_only\n"));
    }

    [Fact]
    public void ParseDataTrainCsv_TrimsWhitespace()
    {
        var csv = "a,b,c,d,e,f,g\n  Optical , CFHT , Mega , g , 1 , image , SCI \n";
        var rows = ParseDataTrainCsv(csv);
        Assert.Equal("Optical", rows[0].Band);
        Assert.Equal("CFHT", rows[0].Collection);
        Assert.Equal("SCI", rows[0].ObservationType);
    }
}
