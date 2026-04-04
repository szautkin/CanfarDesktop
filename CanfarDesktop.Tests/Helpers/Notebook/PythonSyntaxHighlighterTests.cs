using Xunit;
using CanfarDesktop.Helpers.Notebook;

namespace CanfarDesktop.Tests.Helpers.Notebook;

public class PythonSyntaxHighlighterTests
{
    [Fact]
    public void Highlight_EmptyString_ReturnsSingleSpan()
    {
        var spans = PythonSyntaxHighlighter.Highlight("");
        Assert.Single(spans);
        Assert.Equal(TokenType.Plain, spans[0].Type);
    }

    [Fact]
    public void Highlight_Keywords_Detected()
    {
        var spans = PythonSyntaxHighlighter.Highlight("import numpy as np");

        Assert.Contains(spans, s => s.Type == TokenType.Keyword && s.Text == "import");
        Assert.Contains(spans, s => s.Type == TokenType.Keyword && s.Text == "as");
    }

    [Fact]
    public void Highlight_String_Detected()
    {
        var spans = PythonSyntaxHighlighter.Highlight("x = \"hello world\"");

        Assert.Contains(spans, s => s.Type == TokenType.String && s.Text.Contains("hello"));
    }

    [Fact]
    public void Highlight_Comment_Detected()
    {
        var spans = PythonSyntaxHighlighter.Highlight("x = 1 # a comment");

        Assert.Contains(spans, s => s.Type == TokenType.Comment && s.Text.Contains("a comment"));
    }

    [Fact]
    public void Highlight_Number_Detected()
    {
        var spans = PythonSyntaxHighlighter.Highlight("x = 42");

        Assert.Contains(spans, s => s.Type == TokenType.Number && s.Text == "42");
    }

    [Fact]
    public void Highlight_Builtin_Detected()
    {
        var spans = PythonSyntaxHighlighter.Highlight("print(len(x))");

        Assert.Contains(spans, s => s.Type == TokenType.Builtin && s.Text == "print");
        Assert.Contains(spans, s => s.Type == TokenType.Builtin && s.Text == "len");
    }

    [Fact]
    public void Highlight_Decorator_Detected()
    {
        var spans = PythonSyntaxHighlighter.Highlight("@property\ndef foo(): pass");

        Assert.Contains(spans, s => s.Type == TokenType.Decorator && s.Text == "@property");
    }

    [Fact]
    public void Highlight_StringInsideComment_CommentWins()
    {
        // The string inside a comment should be part of the comment, not a separate string
        var spans = PythonSyntaxHighlighter.Highlight("# don't parse \"this\"");

        // The whole line should be one comment span
        Assert.Single(spans);
        Assert.Equal(TokenType.Comment, spans[0].Type);
    }

    [Fact]
    public void Highlight_MultilineCode_AllTokenTypes()
    {
        var code = """
            import numpy as np
            x = np.array([1, 2, 3])
            # compute mean
            print(f"mean = {x.mean():.2f}")
            """;

        var spans = PythonSyntaxHighlighter.Highlight(code);

        Assert.Contains(spans, s => s.Type == TokenType.Keyword);
        Assert.Contains(spans, s => s.Type == TokenType.Number);
        Assert.Contains(spans, s => s.Type == TokenType.Comment);
        Assert.Contains(spans, s => s.Type == TokenType.Builtin);
        Assert.Contains(spans, s => s.Type == TokenType.String);
    }

    [Fact]
    public void Highlight_NoOverlap_SpansCoverFullText()
    {
        var code = "x = 42 # num";
        var spans = PythonSyntaxHighlighter.Highlight(code);

        // Concatenated spans should equal the original text
        var reconstructed = string.Join("", spans.Select(s => s.Text));
        Assert.Equal(code, reconstructed);
    }

    [Fact]
    public void Highlight_TripleQuotedString()
    {
        var code = "x = \"\"\"hello\nworld\"\"\"";
        var spans = PythonSyntaxHighlighter.Highlight(code);

        Assert.Contains(spans, s => s.Type == TokenType.String && s.Text.Contains("hello"));
    }

    [Fact]
    public void GetColor_LightAndDark_ReturnsDifferentColors()
    {
        var lightColor = PythonSyntaxHighlighter.GetColor(TokenType.Keyword, isDark: false);
        var darkColor = PythonSyntaxHighlighter.GetColor(TokenType.Keyword, isDark: true);

        Assert.NotEqual(lightColor, darkColor);
    }
}
