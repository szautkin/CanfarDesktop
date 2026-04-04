using Xunit;
using CanfarDesktop.Helpers.Notebook;

namespace CanfarDesktop.Tests.Helpers.Notebook;

public class AnsiParserTests
{
    [Fact]
    public void Parse_PlainText_SingleSpan()
    {
        var spans = AnsiParser.Parse("Hello, world!");

        Assert.Single(spans);
        Assert.Equal("Hello, world!", spans[0].Text);
        Assert.Null(spans[0].Foreground);
        Assert.False(spans[0].IsBold);
    }

    [Fact]
    public void Parse_RedText_HasRedForeground()
    {
        var spans = AnsiParser.Parse("\x1b[31mError\x1b[0m");

        Assert.Single(spans); // No text after reset = no trailing span
        Assert.Equal("Error", spans[0].Text);
        Assert.NotNull(spans[0].Foreground);
        Assert.Equal(205, spans[0].Foreground!.Value.R); // Red
    }

    [Fact]
    public void Parse_BoldText_HasBoldFlag()
    {
        var spans = AnsiParser.Parse("\x1b[1mBold text\x1b[0m normal");

        Assert.Equal(2, spans.Count);
        Assert.True(spans[0].IsBold);
        Assert.Equal("Bold text", spans[0].Text);
        Assert.False(spans[1].IsBold);
        Assert.Equal(" normal", spans[1].Text);
    }

    [Fact]
    public void Parse_MultipleColors_ProducesMultipleSpans()
    {
        var text = "\x1b[32mGreen\x1b[0m and \x1b[34mBlue\x1b[0m";
        var spans = AnsiParser.Parse(text);

        Assert.True(spans.Count >= 3);
        Assert.Equal("Green", spans[0].Text);
        Assert.Equal(13, spans[0].Foreground!.Value.R); // Green color (R=13, G=188, B=121)
        Assert.Equal(" and ", spans[1].Text);
        Assert.Null(spans[1].Foreground);
        Assert.Equal("Blue", spans[2].Text);
        Assert.Equal(36, spans[2].Foreground!.Value.R); // Blue color R
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmpty()
    {
        Assert.Empty(AnsiParser.Parse(""));
        Assert.Empty(AnsiParser.Parse(null!));
    }

    [Fact]
    public void Parse_NoEscapes_SingleSpan()
    {
        var spans = AnsiParser.Parse("just plain text\nwith newlines\n");
        Assert.Single(spans);
        Assert.Equal("just plain text\nwith newlines\n", spans[0].Text);
    }

    [Fact]
    public void Parse_PythonTraceback_HandlesTypicalFormat()
    {
        // Typical Python traceback with ANSI codes
        var text = "\x1b[0;31mNameError\x1b[0m: name 'x' is not defined";
        var spans = AnsiParser.Parse(text);

        Assert.True(spans.Count >= 2);
        Assert.Equal("NameError", spans[0].Text);
        Assert.NotNull(spans[0].Foreground); // Red
        Assert.Contains("name 'x' is not defined", spans[1].Text);
    }

    [Fact]
    public void Parse_CombinedBoldAndColor()
    {
        var text = "\x1b[1;31mBold Red\x1b[0m";
        var spans = AnsiParser.Parse(text);

        Assert.Equal("Bold Red", spans[0].Text);
        Assert.True(spans[0].IsBold);
        Assert.NotNull(spans[0].Foreground); // Red
    }

    [Fact]
    public void Strip_RemovesAllEscapes()
    {
        var text = "\x1b[1;31mError:\x1b[0m something went \x1b[32mwrong\x1b[0m";
        var plain = AnsiParser.Strip(text);

        Assert.Equal("Error: something went wrong", plain);
        Assert.False(plain.Contains('\x1b'));
    }

    [Fact]
    public void Strip_PlainText_Unchanged()
    {
        Assert.Equal("hello", AnsiParser.Strip("hello"));
    }

    [Fact]
    public void Parse_DefaultForeground39_ResetsColor()
    {
        var text = "\x1b[31mRed\x1b[39mDefault";
        var spans = AnsiParser.Parse(text);

        Assert.Equal("Red", spans[0].Text);
        Assert.NotNull(spans[0].Foreground);
        Assert.Equal("Default", spans[1].Text);
        Assert.Null(spans[1].Foreground); // Reset to default
    }
}
