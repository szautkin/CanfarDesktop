using System.Globalization;
using System.Text;
using System.Text.Json;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services.Export;

/// <summary>
/// Pure builder for the Search module's export payload (saved_queries.json + recent_searches.json +
/// queries.md). ADQL is placed in fenced <c>sql</c> blocks so an LLM can parse/execute/rewrite the
/// user's queries. 1-to-1 with the macOS SearchExporter (adapted to the Windows RecentSearch shape).
/// </summary>
public static class SearchExportBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static ExportModuleOutput Build(
        IReadOnlyList<SavedQuery> saved,
        IReadOnlyList<RecentSearch> recent,
        ExportOptions options,
        DateTimeOffset now)
    {
        var output = new ExportModuleOutput();

        output.JsonFiles["saved_queries.json"] = JsonSerializer.Serialize(saved, JsonOptions);
        output.ItemCounts["saved_queries"] = saved.Count;

        var recentToWrite = options.IncludeSearchHistory ? recent : Array.Empty<RecentSearch>();
        if (options.IncludeSearchHistory)
        {
            output.JsonFiles["recent_searches.json"] = JsonSerializer.Serialize(recent, JsonOptions);
            output.ItemCounts["recent_searches"] = recent.Count;
        }

        output.MarkdownFiles["queries.md"] = RenderMarkdown(saved, recentToWrite, now);
        return output;
    }

    private static string RenderMarkdown(IReadOnlyList<SavedQuery> saved, IReadOnlyList<RecentSearch> recent, DateTimeOffset now)
    {
        var md = new StringBuilder();
        md.Append("# Search Queries\n\n");
        md.Append($"Exported {Iso(now)}\n\n");
        md.Append($"- {saved.Count} saved quer{(saved.Count == 1 ? "y" : "ies")}\n");
        md.Append($"- {recent.Count} recent search{(recent.Count == 1 ? "" : "es")}\n\n");
        md.Append("---\n\n");

        if (saved.Count > 0)
        {
            md.Append("## Saved ADQL Queries\n\n");
            foreach (var q in saved)
            {
                md.Append($"### {q.Name}\n\n");
                md.Append($"Saved {Iso(q.SavedAt)}\n\n");
                md.Append("```sql\n");
                md.Append(q.Adql);
                if (!q.Adql.EndsWith('\n')) md.Append('\n');
                md.Append("```\n\n");
            }
            md.Append("---\n\n");
        }

        if (recent.Count > 0)
        {
            md.Append("## Recent Searches\n\n");
            foreach (var s in recent)
            {
                md.Append($"### {(string.IsNullOrEmpty(s.Summary) ? "(search)" : s.Summary)}\n\n");
                md.Append($"- **Searched:** {Iso(s.SearchedAt)}\n");
                md.Append($"- **Results:** {s.ResultCount}\n\n");
                if (!string.IsNullOrWhiteSpace(s.Adql))
                {
                    md.Append("```sql\n");
                    md.Append(s.Adql);
                    if (!s.Adql.EndsWith('\n')) md.Append('\n');
                    md.Append("```\n\n");
                }
            }
        }

        if (saved.Count == 0 && recent.Count == 0)
            md.Append("_No saved queries or recent searches yet._\n");

        return md.ToString();
    }

    private static string Iso(DateTime dt)
    {
        var utc = dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt.ToUniversalTime();
        return utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    private static string Iso(DateTimeOffset dt) => dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
