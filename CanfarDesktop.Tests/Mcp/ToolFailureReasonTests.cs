using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Write;

namespace CanfarDesktop.Tests.Mcp;

public class ToolFailureReasonTests
{
    [Fact]
    public void AMessageTheAppWrites_ArrivesWhole()
    {
        // QA D2: cut off at 200 characters, mid-sentence, before the remedy.
        var description = new InvalidArgument(CutoutOptions.NoneCanBeCut).Description;

        Assert.Equal($"Invalid argument: {CutoutOptions.NoneCanBeCut}", description);
        Assert.EndsWith("which can then be cut locally", description);
    }

    [Fact]
    public void AMessageAtTheLimit_IsNotCut()
    {
        var detail = new string('x', ToolFailureReason.MaxDescription);
        Assert.Equal(detail, new NotImplemented(detail).Description);
    }

    [Fact]
    public void ALongerOne_IsCutAtTheLimit_AndSaysSo()
    {
        var description = new BackendError(new string('x', 10_000)).Description;

        Assert.Equal(ToolFailureReason.MaxDescription, description.Length);
        Assert.EndsWith("x…", description);
    }
}
