using Xunit;

namespace CanfarDesktop.Tests.Helpers.Notebook;

/// <summary>
/// Tests for magic command detection patterns used by the kernel harness.
/// These test the C# side patterns; the actual magic execution is in Python.
/// </summary>
public class MagicCommandTests
{
    // Helper: mirrors the Python _is_magic_line logic
    private static bool IsMagicLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith("!") ||
               trimmed.StartsWith("%pip ") ||
               trimmed.StartsWith("%conda ") ||
               trimmed.StartsWith("%matplotlib");
    }

    private static bool IsAllMagic(string code)
    {
        var lines = code.Split('\n');
        return lines.Where(l => !string.IsNullOrWhiteSpace(l)).All(IsMagicLine);
    }

    private static bool HasAnyMagic(string code)
    {
        var lines = code.Split('\n');
        return lines.Where(l => !string.IsNullOrWhiteSpace(l)).Any(IsMagicLine);
    }

    [Theory]
    [InlineData("!pip install numpy", true)]
    [InlineData("%pip install numpy", true)]
    [InlineData("!git clone repo", true)]
    [InlineData("%matplotlib inline", true)]
    [InlineData("%conda install scipy", true)]
    [InlineData("import numpy", false)]
    [InlineData("print('hello')", false)]
    [InlineData("x = 1  # not magic", false)]
    [InlineData("  !pip install numpy", true)] // indented
    [InlineData("", false)]
    public void IsMagicLine_DetectsCorrectly(string line, bool expected)
    {
        Assert.Equal(expected, IsMagicLine(line));
    }

    [Fact]
    public void IsAllMagic_SingleShellCommand()
    {
        Assert.True(IsAllMagic("!pip install numpy"));
    }

    [Fact]
    public void IsAllMagic_MultipleShellCommands()
    {
        Assert.True(IsAllMagic("!git clone repo\n!pip install pkg1\n!pip install pkg2"));
    }

    [Fact]
    public void IsAllMagic_WithEmptyLines()
    {
        Assert.True(IsAllMagic("!pip install numpy\n\n!pip install matplotlib"));
    }

    [Fact]
    public void IsAllMagic_MixedMagicAndCode_ReturnsFalse()
    {
        Assert.False(IsAllMagic("!pip install numpy\nimport numpy"));
    }

    [Fact]
    public void IsAllMagic_PureCode_ReturnsFalse()
    {
        Assert.False(IsAllMagic("import numpy\nprint(numpy.__version__)"));
    }

    [Fact]
    public void HasAnyMagic_MixedCell_ReturnsTrue()
    {
        Assert.True(HasAnyMagic("!pip install numpy\nimport numpy\nprint('done')"));
    }

    [Fact]
    public void HasAnyMagic_PureCode_ReturnsFalse()
    {
        Assert.False(HasAnyMagic("import numpy\nprint('done')"));
    }

    [Fact]
    public void HasAnyMagic_MagicInMiddle()
    {
        Assert.True(HasAnyMagic("import os\n!pip install numpy\nprint('done')"));
    }

    [Theory]
    [InlineData("!pip install numpy\n!pip install matplotlib", true)]
    [InlineData("%pip install numpy\n%pip install matplotlib", true)]
    [InlineData("!git clone repo\n!pip install ipympl\n!pip install lightkurve", true)]
    public void IsAllMagic_VariousCombinations(string code, bool expected)
    {
        Assert.Equal(expected, IsAllMagic(code));
    }
}
