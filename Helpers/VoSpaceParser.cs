using System.Xml.Linq;
using CanfarDesktop.Models;

namespace CanfarDesktop.Helpers;

/// <summary>
/// Pure static utility for parsing VOSpace XML responses into VoSpaceNode objects.
/// No UI dependencies, fully testable.
/// </summary>
public static class VoSpaceParser
{
    private static readonly XNamespace Vos = "http://www.ivoa.net/xml/VOSpace/v2.0";

    /// <summary>
    /// Parse a VOSpace container node listing into a list of child nodes.
    /// </summary>
    public static List<VoSpaceNode> ParseNodeList(string xml)
    {
        var nodes = new List<VoSpaceNode>();
        if (string.IsNullOrWhiteSpace(xml)) return nodes;

        try
        {
            var doc = XDocument.Parse(xml);
            var rootNode = doc.Root;
            if (rootNode is null) return nodes;

            // Find <nodes> element containing child <node> elements
            var nodesElement = rootNode.Element(Vos + "nodes")
                ?? rootNode.Descendants(Vos + "nodes").FirstOrDefault();

            if (nodesElement is null) return nodes;

            foreach (var nodeEl in nodesElement.Elements(Vos + "node"))
            {
                var node = ParseNode(nodeEl);
                if (node is not null)
                    nodes.Add(node);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"VOSpace XML parse error: {ex.Message}");
        }

        return nodes;
    }

    /// <summary>
    /// Parse a single node element into a VoSpaceNode.
    /// </summary>
    internal static VoSpaceNode? ParseNode(XElement nodeEl)
    {
        var uri = nodeEl.Attribute("uri")?.Value;
        if (string.IsNullOrEmpty(uri)) return null;

        // Extract name from URI: vos://cadc.nrc.ca~arc/home/user/folder/file.fits → file.fits
        var name = uri.Contains('/') ? uri[(uri.LastIndexOf('/') + 1)..] : uri;
        var path = ExtractPath(uri);

        // Determine type from xsi:type or element name
        var xsiType = nodeEl.Attribute(XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance") + "type")?.Value ?? "";
        var type = xsiType.Contains("ContainerNode") ? VoSpaceNodeType.Container
            : xsiType.Contains("LinkNode") ? VoSpaceNodeType.LinkNode
            : VoSpaceNodeType.DataNode;

        var node = new VoSpaceNode
        {
            Name = name,
            Path = path,
            Type = type
        };

        // Parse properties
        var propsEl = nodeEl.Element(Vos + "properties");
        if (propsEl is not null)
        {
            foreach (var prop in propsEl.Elements(Vos + "property"))
            {
                var propUri = prop.Attribute("uri")?.Value ?? "";
                var value = prop.Value.Trim();

                if (propUri.EndsWith("#length") && long.TryParse(value, out var size))
                    node.SizeBytes = size;
                else if (propUri.EndsWith("#date") && DateTime.TryParse(value, out var dt))
                    node.LastModified = dt;
                else if (propUri.EndsWith("#type"))
                    node.ContentType = value;
                else if (propUri.EndsWith("#ispublic"))
                    node.IsPublic = value.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
        }

        return node;
    }

    /// <summary>VOSpace roots that own a path: personal (<c>/home/&lt;user&gt;</c>) and group
    /// (<c>/projects/&lt;group&gt;</c>) trees on the ARC node service.</summary>
    private static readonly string[] ScopeRoots = { "/home/", "/projects/" };

    /// <summary>
    /// Extract the path relative to its owner segment from a VOSpace URI. Scope-aware: handles both
    /// personal (<c>/home/&lt;user&gt;/…</c>) and group (<c>/projects/&lt;group&gt;/…</c>) trees.
    /// e.g. "vos://cadc.nrc.ca~arc/home/user/folder" → "folder";
    ///      "vos://cadc.nrc.ca~arc/projects/grp/sub/x.fits" → "sub/x.fits".
    /// </summary>
    internal static string ExtractPath(string uri)
    {
        // Find the scope marker (/home/ or /projects/) and return everything below the owner segment
        // (the user or group). A /projects/ URI previously fell through and returned the raw URI.
        foreach (var scope in ScopeRoots)
        {
            var idx = uri.IndexOf(scope, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;

            var afterScope = uri[(idx + scope.Length)..]; // skip "/home/" or "/projects/"
            var slashIdx = afterScope.IndexOf('/');
            if (slashIdx < 0) return ""; // just /home/user or /projects/group, no sub-path
            return afterScope[(slashIdx + 1)..]; // everything after the owner segment
        }

        return uri;
    }

    /// <summary>
    /// Build VOSpace XML for creating a container (folder) node.
    /// </summary>
    public static string BuildContainerNodeXml(string nodeUri)
    {
        var escaped = System.Security.SecurityElement.Escape(nodeUri);
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <vos:node xmlns:vos="http://www.ivoa.net/xml/VOSpace/v2.0"
                      xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                      uri="{escaped}"
                      xsi:type="vos:ContainerNode">
              <vos:properties/>
              <vos:accepts/>
              <vos:provides/>
              <vos:capabilities/>
              <vos:nodes/>
            </vos:node>
            """;
    }
}
