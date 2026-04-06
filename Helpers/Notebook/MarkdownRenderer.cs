namespace CanfarDesktop.Helpers.Notebook;

using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

/// <summary>
/// Simple markdown → XAML renderer for notebook markdown cells.
/// Supports: headings, bold, italic, inline code, code blocks, lists, horizontal rules.
/// Not a full CommonMark parser — covers the 80% used in Jupyter notebooks.
/// </summary>
public static partial class MarkdownRenderer
{
    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"\*(.+?)\*")]
    private static partial Regex ItalicRegex();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex InlineCodeRegex();

    /// <summary>
    /// Render markdown source into XAML UIElements for display in a StackPanel.
    /// </summary>
    public static List<UIElement> Render(string markdown)
    {
        var elements = new List<UIElement>();
        if (string.IsNullOrWhiteSpace(markdown)) return elements;

        var lines = markdown.Split('\n');
        var inCodeBlock = false;
        var codeBlockLines = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            // Code block fence
            if (line.TrimStart().StartsWith("```"))
            {
                if (inCodeBlock)
                {
                    elements.Add(BuildCodeBlock(string.Join('\n', codeBlockLines)));
                    codeBlockLines.Clear();
                    inCodeBlock = false;
                }
                else
                {
                    inCodeBlock = true;
                }
                continue;
            }

            if (inCodeBlock)
            {
                codeBlockLines.Add(line);
                continue;
            }

            // Horizontal rule
            if (line.Trim() is "---" or "***" or "___")
            {
                elements.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(0, 8, 0, 8),
                    Background = ThemeHelper.GetBrush("DividerStrokeColorDefaultBrush"),
                });
                continue;
            }

            // Headings
            if (line.StartsWith("### "))
            {
                elements.Add(BuildHeading(line[4..], "BodyStrongTextBlockStyle"));
                continue;
            }
            if (line.StartsWith("## "))
            {
                elements.Add(BuildHeading(line[3..], "SubtitleTextBlockStyle"));
                continue;
            }
            if (line.StartsWith("# "))
            {
                elements.Add(BuildHeading(line[2..], "TitleTextBlockStyle"));
                continue;
            }

            // Unordered list
            if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("* "))
            {
                var indent = line.Length - line.TrimStart().Length;
                var text = line.TrimStart()[2..];
                elements.Add(BuildListItem(text, indent));
                continue;
            }

            // Ordered list
            if (line.TrimStart().Length > 2 && char.IsDigit(line.TrimStart()[0]) && line.TrimStart().Contains(". "))
            {
                var trimmed = line.TrimStart();
                var dotIdx = trimmed.IndexOf(". ");
                if (dotIdx > 0 && dotIdx < 4)
                {
                    var text = trimmed[(dotIdx + 2)..];
                    var number = trimmed[..dotIdx];
                    elements.Add(BuildListItem($"{number}. {text}", 0));
                    continue;
                }
            }

            // Empty line
            if (string.IsNullOrWhiteSpace(line))
            {
                elements.Add(new Border { Height = 8 });
                continue;
            }

            // Regular paragraph with inline formatting
            elements.Add(BuildParagraph(line));
        }

        // Close unclosed code block
        if (inCodeBlock && codeBlockLines.Count > 0)
            elements.Add(BuildCodeBlock(string.Join('\n', codeBlockLines)));

        return elements;
    }

    private static UIElement BuildHeading(string text, string styleKey)
    {
        return new TextBlock
        {
            Text = text.Trim(),
            Style = (Style)Application.Current.Resources[styleKey],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 4),
        };
    }

    private static UIElement BuildCodeBlock(string code)
    {
        return new Border
        {
            Background = ThemeHelper.GetBrush("SubtleFillColorSecondaryBrush"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 4, 0, 4),
            Child = new TextBlock
            {
                Text = code,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            }
        };
    }

    private static UIElement BuildListItem(string text, int indent)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(indent * 8 + 8, 2, 0, 2) };
        panel.Children.Add(new TextBlock { Text = "\u2022 ", Foreground = ThemeHelper.GetBrush("TextFillColorSecondaryBrush") });
        panel.Children.Add(BuildInlineTextBlock(text));
        return panel;
    }

    private static UIElement BuildParagraph(string text)
    {
        return BuildInlineTextBlock(text);
    }

    private static TextBlock BuildInlineTextBlock(string text)
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };

        // Process inline formatting: **bold**, *italic*, `code`
        var remaining = text;

        // Simple approach: split on formatting markers
        while (remaining.Length > 0)
        {
            // Find the next formatting marker
            var boldMatch = BoldRegex().Match(remaining);
            var italicMatch = ItalicRegex().Match(remaining);
            var codeMatch = InlineCodeRegex().Match(remaining);

            // Find earliest match
            Match? earliest = null;
            string type = "";
            foreach (var (m, t) in new[] { (boldMatch, "bold"), (italicMatch, "italic"), (codeMatch, "code") })
            {
                if (m.Success && (earliest is null || m.Index < earliest.Index))
                {
                    earliest = m;
                    type = t;
                }
            }

            if (earliest is null)
            {
                // No more formatting — add the rest as plain text
                if (remaining.Length > 0)
                    tb.Inlines.Add(new Run { Text = remaining });
                break;
            }

            // Add text before the match
            if (earliest.Index > 0)
                tb.Inlines.Add(new Run { Text = remaining[..earliest.Index] });

            // Add the formatted span
            var content = earliest.Groups[1].Value;
            switch (type)
            {
                case "bold":
                    tb.Inlines.Add(new Run { Text = content, FontWeight = FontWeights.Bold });
                    break;
                case "italic":
                    tb.Inlines.Add(new Run { Text = content, FontStyle = Windows.UI.Text.FontStyle.Italic });
                    break;
                case "code":
                    tb.Inlines.Add(new Run
                    {
                        Text = content,
                        FontFamily = new FontFamily("Consolas"),
                        // Can't set background on Run, but font change is enough
                    });
                    break;
            }

            remaining = remaining[(earliest.Index + earliest.Length)..];
        }

        return tb;
    }
}
