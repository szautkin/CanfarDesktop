using System.Text.RegularExpressions;
using CanfarDesktop.Tests.Helpers;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// The MCP tools the app's sources declare, read from the sources themselves.
///
/// <para>The live catalogue cannot be built in a unit test — it wants a whole service provider — so the
/// guards that need "every tool" used to carry a hand-copied list. A hand-copied list only catches what
/// somebody remembered to copy: 29 tools reached the server without reaching the AI Guide's categories,
/// and two were written and tested and never reached the server at all. Reading the declarations keeps
/// the list honest without anybody maintaining it.</para>
/// </summary>
internal static class McpToolSources
{
    /// <param name="Tool">The name agents call it by.</param>
    /// <param name="Class">The class that declares it; null for an alias, which wraps another tool in place.</param>
    /// <param name="File">Where it is declared, for a failure message somebody can act on.</param>
    public sealed record Declaration(string Tool, string? Class, string File);

    // A tool's name is the first argument of its descriptor — or of the alias that re-exposes another
    // tool under the macOS name.
    private static readonly Regex Named = new(
        @"(?<how>WithStaticSchema|ToolDescriptor|AliasedTool)\s*\(\s*""(?<tool>[a-z][a-z0-9_]*)""",
        RegexOptions.Compiled);

    private static readonly Regex Class = new(@"\bclass\s+(?<name>\w+)", RegexOptions.Compiled);

    private static readonly Lazy<IReadOnlyList<Declaration>> Cached = new(Scan);

    /// <summary>Every declared tool, once per name.</summary>
    public static IReadOnlyList<Declaration> All => Cached.Value;

    /// <summary>The app's own C# sources — not these tests, which construct tools of their own.</summary>
    public static IEnumerable<string> AppSources()
    {
        var root = RepoFiles.Root();
        return RepoFiles.Sources("*.cs").Where(f =>
            !Path.GetRelativePath(root, f).StartsWith("CanfarDesktop.Tests", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<Declaration> Scan()
    {
        var mcp = RepoFiles.PathTo("Mcp") + Path.DirectorySeparatorChar;
        var found = new List<Declaration>();

        foreach (var file in AppSources().Where(f => f.StartsWith(mcp, StringComparison.OrdinalIgnoreCase)))
        {
            var text = File.ReadAllText(file);
            var classes = Class.Matches(text);

            foreach (Match m in Named.Matches(text))
            {
                // The class a descriptor belongs to is the last one opened before it.
                var owner = m.Groups["how"].Value == "AliasedTool"
                    ? null
                    : classes.LastOrDefault(c => c.Index < m.Index)?.Groups["name"].Value;
                found.Add(new Declaration(m.Groups["tool"].Value, owner, Path.GetFileName(file)));
            }
        }

        return found.DistinctBy(d => d.Tool).ToList();
    }
}
