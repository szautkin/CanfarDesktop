using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>Where the Portal's cards go, wide and narrow — Verbinal for Linux's arrangement.</summary>
public class PortalLayoutTests
{
    public static TheoryData<string> Arrangements => new() { nameof(PortalLayout.Wide), nameof(PortalLayout.Narrow) };

    private static IReadOnlyDictionary<PortalCard, PortalCell> Named(string name)
        => name == nameof(PortalLayout.Wide) ? PortalLayout.Wide : PortalLayout.Narrow;

    [Theory]
    [MemberData(nameof(Arrangements))]
    public void EveryCard_HasAPlace(string arrangement)
        => Assert.Equal(Enum.GetValues<PortalCard>().Order(), Named(arrangement).Keys.Order());

    [Theory]
    [MemberData(nameof(Arrangements))]
    public void EveryPlace_IsInsideTheGrid_AndNoTwoOverlap(string arrangement)
    {
        var taken = new HashSet<(int, int)>();
        foreach (var (card, cell) in Named(arrangement))
        {
            Assert.InRange(cell.Column, 0, PortalLayout.Columns - 1);
            Assert.InRange(cell.Column + cell.ColumnSpan, 1, PortalLayout.Columns);
            for (var c = cell.Column; c < cell.Column + cell.ColumnSpan; c++)
                Assert.True(taken.Add((cell.Row, c)), $"{card} overlaps another card at row {cell.Row}, column {c}");
        }
    }

    [Fact]
    public void Wide_IsThreeStatusCards_ThenSessions_ThenImagesBesideRecentLaunches()
    {
        var wide = PortalLayout.Wide;
        Assert.Equal([PortalCard.PlatformLoad, PortalCard.Storage, PortalCard.BatchJobs],
            wide.Where(x => x.Value.Row == 0).OrderBy(x => x.Value.Column).Select(x => x.Key));
        Assert.Equal(new PortalCell(0, 1, 3), wide[PortalCard.Sessions]);
        Assert.Equal(new PortalCell(0, 2, 2), wide[PortalCard.Images]);
        Assert.Equal(new PortalCell(2, 2, 1), wide[PortalCard.RecentLaunches]);
        Assert.Equal(3, PortalLayout.Rows(wide));
    }

    [Fact]
    public void Narrow_IsOneColumn_InTheSameOrder()
    {
        Assert.All(PortalLayout.Narrow.Values, cell => Assert.Equal((0, PortalLayout.Columns), (cell.Column, cell.ColumnSpan)));
        Assert.Equal(Enum.GetValues<PortalCard>(), PortalLayout.Narrow.OrderBy(x => x.Value.Row).Select(x => x.Key));
    }

    [Theory]
    [InlineData(999.9, false)]
    [InlineData(1000, true)]
    [InlineData(1600, true)]
    [InlineData(400, false)]
    public void ItStacks_BelowTheBreakpoint(double width, bool wide)
        => Assert.Same(wide ? PortalLayout.Wide : PortalLayout.Narrow, PortalLayout.For(width));
}
