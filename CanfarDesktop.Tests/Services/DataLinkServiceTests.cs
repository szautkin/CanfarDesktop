using System.Reflection;
using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Services;

namespace CanfarDesktop.Tests.Services;

public class DataLinkServiceTests
{
    // Use reflection to test the private static ParseVOTable method
    private static DataLinkResult ParseVOTable(string xml)
    {
        var method = typeof(DataLinkService).GetMethod("ParseVOTable",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (DataLinkResult)method!.Invoke(null, [xml])!;
    }

    [Fact]
    public void ParseVOTable_ExtractsThumbnailAndPreview()
    {
        var xml = """
            <VOTABLE>
            <RESOURCE>
            <TABLE>
            <FIELD name="access_url"/>
            <FIELD name="semantics"/>
            <FIELD name="content_type"/>
            <FIELD name="error_message"/>
            <DATA><TABLEDATA>
            <TR><TD>https://example.com/thumb.jpg</TD><TD>#thumbnail</TD><TD>image/jpeg</TD><TD></TD></TR>
            <TR><TD>https://example.com/preview.png</TD><TD>#preview</TD><TD>image/png</TD><TD></TD></TR>
            <TR><TD>https://example.com/data.fits</TD><TD>#this</TD><TD>application/fits</TD><TD></TD></TR>
            </TABLEDATA></DATA>
            </TABLE>
            </RESOURCE>
            </VOTABLE>
            """;

        var result = ParseVOTable(xml);

        Assert.Single(result.Thumbnails);
        Assert.Equal("https://example.com/thumb.jpg", result.Thumbnails[0]);
        Assert.Single(result.Previews);
        Assert.Equal("https://example.com/preview.png", result.Previews[0]);
    }

    [Fact]
    public void ParseVOTable_SkipsErrorRows()
    {
        var xml = """
            <VOTABLE>
            <RESOURCE><TABLE>
            <FIELD name="access_url"/>
            <FIELD name="semantics"/>
            <FIELD name="content_type"/>
            <FIELD name="error_message"/>
            <DATA><TABLEDATA>
            <TR><TD>https://example.com/thumb.jpg</TD><TD>#thumbnail</TD><TD>image/jpeg</TD><TD>Authorization required</TD></TR>
            <TR><TD>https://example.com/ok.jpg</TD><TD>#thumbnail</TD><TD>image/jpeg</TD><TD></TD></TR>
            </TABLEDATA></DATA>
            </TABLE></RESOURCE>
            </VOTABLE>
            """;

        var result = ParseVOTable(xml);

        Assert.Single(result.Thumbnails);
        Assert.Equal("https://example.com/ok.jpg", result.Thumbnails[0]);
    }

    [Fact]
    public void ParseVOTable_PreviewMustBeImage()
    {
        var xml = """
            <VOTABLE>
            <RESOURCE><TABLE>
            <FIELD name="access_url"/>
            <FIELD name="semantics"/>
            <FIELD name="content_type"/>
            <FIELD name="error_message"/>
            <DATA><TABLEDATA>
            <TR><TD>https://example.com/data.fits</TD><TD>#preview</TD><TD>application/fits</TD><TD></TD></TR>
            <TR><TD>https://example.com/img.png</TD><TD>#preview</TD><TD>image/png</TD><TD></TD></TR>
            </TABLEDATA></DATA>
            </TABLE></RESOURCE>
            </VOTABLE>
            """;

        var result = ParseVOTable(xml);

        Assert.Single(result.Previews);
        Assert.Equal("https://example.com/img.png", result.Previews[0]);
    }

    [Fact]
    public void ParseVOTable_EmptyXml_ReturnsEmptyResult()
    {
        var result = ParseVOTable("<VOTABLE></VOTABLE>");
        Assert.Empty(result.Thumbnails);
        Assert.Empty(result.Previews);
    }

    [Fact]
    public void ParseVOTable_MissingFields_ReturnsEmptyResult()
    {
        var xml = """
            <VOTABLE>
            <RESOURCE><TABLE>
            <FIELD name="id"/>
            <FIELD name="name"/>
            <DATA><TABLEDATA>
            <TR><TD>1</TD><TD>test</TD></TR>
            </TABLEDATA></DATA>
            </TABLE></RESOURCE>
            </VOTABLE>
            """;

        var result = ParseVOTable(xml);
        Assert.Empty(result.Thumbnails);
        Assert.Empty(result.Previews);
    }
}
