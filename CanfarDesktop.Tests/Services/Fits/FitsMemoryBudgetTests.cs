using Xunit;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Tests.Services.Fits;

/// <summary>
/// The viewer refused every image over a fixed 512 MB, whatever the machine had free: a 1.6 GB tile
/// was refused on a workstation with room to spare, and the same number was all that stood between a
/// laptop and swapping itself to a halt. What is free now decides.
/// </summary>
public class FitsMemoryBudgetTests
{
    private const long GB = 1024L * 1024 * 1024;
    private const long MegaPipeBytes = 20315L * 20475 * sizeof(float);

    /// <summary>Everything that opened before still opens, however full the machine is.</summary>
    [Fact]
    public void UnderTheOldCap_AnImageAlwaysFits()
    {
        Assert.Null(FitsMemoryBudget.Refusal(8000, 8000, 8000L * 8000 * 4, availableBytes: 0));
        Assert.Equal(FitsMemoryBudget.Floor, FitsMemoryBudget.MaxImageBytes(availableBytes: 0));
    }

    [Fact]
    public void AMegaPipeTile_Opens_WhereThereIsRoomForIt()
        => Assert.Null(FitsMemoryBudget.Refusal(20315, 20475, MegaPipeBytes, availableBytes: 8 * GB));

    /// <summary>
    /// And where there is not, the refusal gives the numbers and a way on — not "too large", which
    /// says neither how large nor what to do.
    /// </summary>
    [Fact]
    public void AMegaPipeTile_IsRefused_OnALaptopWithLittleFree_AndSaysWhy()
    {
        var message = FitsMemoryBudget.Refusal(20315, 20475, MegaPipeBytes, availableBytes: (long)(1.5 * GB));

        Assert.NotNull(message);
        Assert.Contains(20315.ToString("N0"), message);
        Assert.Contains($"needs about {2.5:0.0} GB", message); // 1.5 GB of pixels and the headroom
        Assert.Contains($"{1.5:0.0} GB is free", message);
        Assert.Contains("cutout", message);
    }

    /// <summary>The pixels are not the only thing a large image needs, so the headroom is kept.</summary>
    [Fact]
    public void TheHeadroom_IsKeptBackFromWhatIsFree()
    {
        var justFits = FitsMemoryBudget.Headroom + MegaPipeBytes;

        Assert.Null(FitsMemoryBudget.Refusal(20315, 20475, MegaPipeBytes, justFits));
        Assert.NotNull(FitsMemoryBudget.Refusal(20315, 20475, MegaPipeBytes, justFits - 1));
    }

    [Fact]
    public void TheMachine_ReportsSomeFreeMemory()
        => Assert.True(FitsMemoryBudget.AvailableBytes() > 0);
}
