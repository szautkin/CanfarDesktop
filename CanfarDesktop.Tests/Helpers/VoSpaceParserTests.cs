using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Tests.Helpers;

public class VoSpaceParserTests
{
    private const string SampleXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <vos:node xmlns:vos="http://www.ivoa.net/xml/VOSpace/v2.0"
                  xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                  uri="vos://cadc.nrc.ca~arc/home/testuser"
                  xsi:type="vos:ContainerNode">
          <vos:properties>
            <vos:property uri="ivo://ivoa.net/vospace/core#quota">200000000000</vos:property>
            <vos:property uri="ivo://ivoa.net/vospace/core#length">54185426217</vos:property>
          </vos:properties>
          <vos:nodes>
            <vos:node uri="vos://cadc.nrc.ca~arc/home/testuser/data" xsi:type="vos:ContainerNode">
              <vos:properties>
                <vos:property uri="ivo://ivoa.net/vospace/core#date">2026-03-15T10:30:00.000</vos:property>
              </vos:properties>
            </vos:node>
            <vos:node uri="vos://cadc.nrc.ca~arc/home/testuser/results.fits" xsi:type="vos:DataNode">
              <vos:properties>
                <vos:property uri="ivo://ivoa.net/vospace/core#length">1048576</vos:property>
                <vos:property uri="ivo://ivoa.net/vospace/core#date">2026-03-20T14:00:00.000</vos:property>
                <vos:property uri="ivo://ivoa.net/vospace/core#type">application/fits</vos:property>
                <vos:property uri="ivo://ivoa.net/vospace/core#ispublic">true</vos:property>
              </vos:properties>
            </vos:node>
          </vos:nodes>
        </vos:node>
        """;

    [Fact]
    public void ParseNodeList_ReturnsTwoNodes()
    {
        var nodes = VoSpaceParser.ParseNodeList(SampleXml);
        Assert.Equal(2, nodes.Count);
    }

    [Fact]
    public void ParseNodeList_FolderNode_IsContainer()
    {
        var nodes = VoSpaceParser.ParseNodeList(SampleXml);
        var folder = nodes.First(n => n.Name == "data");
        Assert.Equal(VoSpaceNodeType.Container, folder.Type);
        Assert.True(folder.IsContainer);
    }

    [Fact]
    public void ParseNodeList_FileNode_IsDataNode()
    {
        var nodes = VoSpaceParser.ParseNodeList(SampleXml);
        var file = nodes.First(n => n.Name == "results.fits");
        Assert.Equal(VoSpaceNodeType.DataNode, file.Type);
        Assert.False(file.IsContainer);
    }

    [Fact]
    public void ParseNodeList_FileProperties_Parsed()
    {
        var nodes = VoSpaceParser.ParseNodeList(SampleXml);
        var file = nodes.First(n => n.Name == "results.fits");

        Assert.Equal(1048576L, file.SizeBytes);
        Assert.Equal("application/fits", file.ContentType);
        Assert.True(file.IsPublic);
        Assert.NotNull(file.LastModified);
    }

    [Fact]
    public void ParseNodeList_FolderDate_Parsed()
    {
        var nodes = VoSpaceParser.ParseNodeList(SampleXml);
        var folder = nodes.First(n => n.Name == "data");
        Assert.NotNull(folder.LastModified);
    }

    [Fact]
    public void ParseNodeList_EmptyXml_ReturnsEmpty()
    {
        Assert.Empty(VoSpaceParser.ParseNodeList(""));
        Assert.Empty(VoSpaceParser.ParseNodeList("   "));
    }

    [Fact]
    public void ParseNodeList_EmptyContainer_ReturnsEmpty()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <vos:node xmlns:vos="http://www.ivoa.net/xml/VOSpace/v2.0"
                      xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                      uri="vos://cadc.nrc.ca~arc/home/testuser/empty"
                      xsi:type="vos:ContainerNode">
              <vos:nodes/>
            </vos:node>
            """;
        Assert.Empty(VoSpaceParser.ParseNodeList(xml));
    }

    [Fact]
    public void ExtractPath_FullUri_ExtractsRelativePath()
    {
        Assert.Equal("folder/file.fits", VoSpaceParser.ExtractPath("vos://cadc.nrc.ca~arc/home/user/folder/file.fits"));
        Assert.Equal("data", VoSpaceParser.ExtractPath("vos://cadc.nrc.ca~arc/home/user/data"));
        Assert.Equal("", VoSpaceParser.ExtractPath("vos://cadc.nrc.ca~arc/home/user"));
    }

    [Fact]
    public void ParseNodeList_NodeNames_ExtractedFromUri()
    {
        var nodes = VoSpaceParser.ParseNodeList(SampleXml);
        Assert.Contains(nodes, n => n.Name == "data");
        Assert.Contains(nodes, n => n.Name == "results.fits");
    }

    [Fact]
    public void BuildContainerNodeXml_ContainsUri()
    {
        var xml = VoSpaceParser.BuildContainerNodeXml("vos://cadc.nrc.ca~arc/home/user/newfolder");
        Assert.Contains("vos://cadc.nrc.ca~arc/home/user/newfolder", xml);
        Assert.Contains("ContainerNode", xml);
    }

    [Fact]
    public void FormattedSize_OnNode()
    {
        var node = new VoSpaceNode { SizeBytes = 1048576 };
        Assert.Equal("1.0 MB", node.FormattedSize);
    }

    [Fact]
    public void Icon_FolderVsFile()
    {
        var folder = new VoSpaceNode { Type = VoSpaceNodeType.Container };
        var file = new VoSpaceNode { Type = VoSpaceNodeType.DataNode };
        Assert.NotEqual(folder.Icon, file.Icon);
    }

    [Fact]
    public void ParseNodeList_MalformedXml_ReturnsEmpty()
    {
        Assert.Empty(VoSpaceParser.ParseNodeList("<not valid <<>>"));
    }

    [Fact]
    public void ExtractPath_NoHomePart_ReturnsUri()
    {
        Assert.Equal("some-bare-path", VoSpaceParser.ExtractPath("some-bare-path"));
    }

    [Fact]
    public void BuildContainerNodeXml_ProducesValidXml()
    {
        var xml = VoSpaceParser.BuildContainerNodeXml("vos://cadc.nrc.ca~arc/home/user/test");
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        Assert.Contains("test", doc.Root?.Attribute("uri")?.Value ?? "");
    }

    [Fact]
    public void BuildContainerNodeXml_EscapesSpecialChars()
    {
        var xml = VoSpaceParser.BuildContainerNodeXml("vos://test/a&b<c");
        Assert.DoesNotContain("&b", xml); // should be escaped as &amp;b
        Assert.Contains("&amp;b", xml);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(0L, "0 B")]
    [InlineData(500L, "500 B")]
    [InlineData(2048L, "2.0 KB")]
    [InlineData(1073741824L, "1.00 GB")]
    public void VoSpaceNode_FormattedSize_Boundaries(long? size, string expected)
    {
        var node = new VoSpaceNode { SizeBytes = size };
        Assert.Equal(expected, node.FormattedSize);
    }

    [Theory]
    [InlineData("data.fits", ".fits")]
    [InlineData("script.py", ".py")]
    [InlineData("archive.tar.gz", ".gz")]
    [InlineData("noext", "")]
    public void VoSpaceNode_FileExtension(string name, string expected)
    {
        var node = new VoSpaceNode { Name = name, Type = VoSpaceNodeType.DataNode };
        Assert.Equal(expected, node.FileExtension);
    }

    [Fact]
    public void VoSpaceNode_Icon_FitsFile()
    {
        var node = new VoSpaceNode { Name = "image.fits", Type = VoSpaceNodeType.DataNode };
        Assert.Equal("\uE9D9", node.Icon);
    }

    [Fact]
    public void VoSpaceNode_Icon_PythonFile()
    {
        var node = new VoSpaceNode { Name = "script.py", Type = VoSpaceNodeType.DataNode };
        Assert.Equal("\uE943", node.Icon);
    }

    [Fact]
    public void VoSpaceNode_IsPublic_DefaultFalse()
    {
        var node = new VoSpaceNode();
        Assert.False(node.IsPublic);
    }
}
