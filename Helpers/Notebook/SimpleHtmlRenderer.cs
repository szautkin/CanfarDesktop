namespace CanfarDesktop.Helpers.Notebook;

using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

/// <summary>
/// Lightweight HTML → XAML renderer for notebook outputs.
/// Handles: tables, bold, italic, code, pre, links, br, p, headings.
/// NOT a full HTML parser — covers the patterns Jupyter/pandas produce.
/// </summary>
public static partial class SimpleHtmlRenderer
{
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"<table[^>]*>([\s\S]*?)</table>", RegexOptions.IgnoreCase)]
    private static partial Regex TableRegex();

    [GeneratedRegex(@"<tr[^>]*>([\s\S]*?)</tr>", RegexOptions.IgnoreCase)]
    private static partial Regex RowRegex();

    [GeneratedRegex(@"<t[hd][^>]*>([\s\S]*?)</t[hd]>", RegexOptions.IgnoreCase)]
    private static partial Regex CellRegex();

    [GeneratedRegex(@"<th[^>]*>([\s\S]*?)</th>", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderCellRegex();

    /// <summary>
    /// Render HTML string into XAML UIElements.
    /// </summary>
    public static List<UIElement> Render(string html)
    {
        var elements = new List<UIElement>();
        if (string.IsNullOrWhiteSpace(html)) return elements;

        // Decode common entities
        html = html.Replace("&amp;", "&").Replace("&lt;", "<")
                   .Replace("&gt;", ">").Replace("&nbsp;", " ")
                   .Replace("&quot;", "\"");

        var pos = 0;
        foreach (Match tableMatch in TableRegex().Matches(html))
        {
            // Text before the table
            if (tableMatch.Index > pos)
            {
                var before = html[pos..tableMatch.Index].Trim();
                if (!string.IsNullOrEmpty(before))
                    elements.Add(RenderInlineHtml(before));
            }

            elements.Add(RenderTable(tableMatch.Value));
            pos = tableMatch.Index + tableMatch.Length;
        }

        // Remaining text after last table
        if (pos < html.Length)
        {
            var remaining = html[pos..].Trim();
            if (!string.IsNullOrEmpty(remaining))
                elements.Add(RenderInlineHtml(remaining));
        }

        return elements;
    }

    private static UIElement RenderTable(string tableHtml)
    {
        var rows = RowRegex().Matches(tableHtml);
        if (rows.Count == 0)
            return new TextBlock { Text = StripTags(tableHtml), TextWrapping = TextWrapping.Wrap };

        // Determine column count from first row
        var firstRowCells = CellRegex().Matches(rows[0].Groups[1].Value);
        var colCount = Math.Max(1, firstRowCells.Count);

        var grid = new Grid
        {
            BorderBrush = ThemeHelper.GetBrush("DividerStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
        };

        for (var c = 0; c < colCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var rowIndex = 0;
        foreach (Match rowMatch in rows)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var cells = CellRegex().Matches(rowMatch.Groups[1].Value);
            var isHeader = HeaderCellRegex().IsMatch(rowMatch.Groups[1].Value);

            for (var c = 0; c < Math.Min(cells.Count, colCount); c++)
            {
                var cellText = StripTags(cells[c].Groups[1].Value).Trim();

                var tb = new TextBlock
                {
                    Text = cellText,
                    Padding = new Thickness(8, 4, 8, 4),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 300,
                    FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                var border = new Border
                {
                    Child = tb,
                    BorderBrush = ThemeHelper.GetBrush("DividerStrokeColorDefaultBrush"),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Background = isHeader
                        ? ThemeHelper.GetBrush("CardBackgroundFillColorSecondaryBrush")
                        : null,
                };

                Grid.SetRow(border, rowIndex);
                Grid.SetColumn(border, c);
                grid.Children.Add(border);
            }
            rowIndex++;
        }

        // Wrap in a ScrollViewer for wide tables
        return new ScrollViewer
        {
            Content = grid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 400,
            Margin = new Thickness(0, 4, 0, 4),
        };
    }

    private static UIElement RenderInlineHtml(string html)
    {
        // Handle <pre> blocks
        if (html.Contains("<pre", StringComparison.OrdinalIgnoreCase))
        {
            var preContent = Regex.Replace(html, @"</?pre[^>]*>", "", RegexOptions.IgnoreCase).Trim();
            return new Border
            {
                Background = ThemeHelper.GetBrush("SubtleFillColorSecondaryBrush"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Child = new TextBlock
                {
                    Text = StripTags(preContent),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                }
            };
        }

        // Inline formatted text
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };

        // Split on tags and build inlines
        var parts = TagRegex().Split(html);
        var tags = TagRegex().Matches(html);
        var bold = false;
        var italic = false;
        var code = false;

        var partIndex = 0;
        var tagIndex = 0;

        for (var i = 0; i < parts.Length + tags.Count; i++)
        {
            if (i % 2 == 0 && partIndex < parts.Length)
            {
                // Text content
                var text = parts[partIndex++]
                    .Replace("<br>", "\n").Replace("<br/>", "\n").Replace("<br />", "\n");
                if (!string.IsNullOrEmpty(text))
                {
                    var run = new Run { Text = text };
                    if (bold) run.FontWeight = FontWeights.Bold;
                    if (italic) run.FontStyle = Windows.UI.Text.FontStyle.Italic;
                    if (code) run.FontFamily = new FontFamily("Consolas");
                    tb.Inlines.Add(run);
                }
            }
            else if (tagIndex < tags.Count)
            {
                // Tag
                var tag = tags[tagIndex++].Value.ToLowerInvariant();
                if (tag.StartsWith("<b>") || tag.StartsWith("<strong")) bold = true;
                else if (tag.StartsWith("</b>") || tag.StartsWith("</strong")) bold = false;
                else if (tag.StartsWith("<i>") || tag.StartsWith("<em")) italic = true;
                else if (tag.StartsWith("</i>") || tag.StartsWith("</em")) italic = false;
                else if (tag.StartsWith("<code")) code = true;
                else if (tag.StartsWith("</code")) code = false;
                else if (tag.StartsWith("<br")) tb.Inlines.Add(new LineBreak());
            }
        }

        return tb;
    }

    /// <summary>Strip all HTML tags, returning plain text.</summary>
    public static string StripTags(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;
        var text = TagRegex().Replace(html, "");
        return text.Replace("&amp;", "&").Replace("&lt;", "<")
                   .Replace("&gt;", ">").Replace("&nbsp;", " ")
                   .Replace("&quot;", "\"").Trim();
    }
}
