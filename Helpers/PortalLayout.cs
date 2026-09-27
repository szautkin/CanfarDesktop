namespace CanfarDesktop.Helpers;

/// <summary>The Portal's cards, in the one order both arrangements are written in.</summary>
public enum PortalCard { PlatformLoad, Storage, BatchJobs, Sessions, Images, RecentLaunches }

/// <summary>A card's place in the Portal's grid of three equal columns.</summary>
public readonly record struct PortalCell(int Column, int Row, int ColumnSpan);

/// <summary>
/// Where each Portal card goes: across three equal columns when there is room, one above another when
/// there is not — Verbinal for Linux's arrangement. Two tables of the same cards rather than two blocks
/// of placement code, so a card placed in one and forgotten in the other fails a test instead of
/// vanishing when the window is resized.
///
/// <para>Wide: platform load, storage and batch jobs across the top; active sessions the full width;
/// then CANFAR images over two columns beside recent launches. The launch form is not a card: it opens
/// from the Launch session button on Active sessions.</para>
/// </summary>
public static class PortalLayout
{
    public const int Columns = 3;

    /// <summary>
    /// Below this width, in effective pixels, the cards stack. Above the grid's own minimum — three
    /// columns of cards a little over 300 wide, and the spacing and padding between them — so the grid
    /// never has to be clipped between the two.
    /// </summary>
    public const double StackBelow = 1000;

    public static IReadOnlyDictionary<PortalCard, PortalCell> Wide { get; } = new Dictionary<PortalCard, PortalCell>
    {
        [PortalCard.PlatformLoad] = new(0, 0, 1),
        [PortalCard.Storage] = new(1, 0, 1),
        [PortalCard.BatchJobs] = new(2, 0, 1),
        [PortalCard.Sessions] = new(0, 1, Columns),
        [PortalCard.Images] = new(0, 2, 2),
        [PortalCard.RecentLaunches] = new(2, 2, 1),
    };

    /// <summary>One column, the cards in the order they are listed.</summary>
    public static IReadOnlyDictionary<PortalCard, PortalCell> Narrow { get; } =
        Enum.GetValues<PortalCard>().Select((card, row) => (card, row)).ToDictionary(x => x.card, x => new PortalCell(0, x.row, Columns));

    /// <summary>The arrangement for a Portal this wide.</summary>
    public static IReadOnlyDictionary<PortalCard, PortalCell> For(double width) => width < StackBelow ? Narrow : Wide;

    /// <summary>How many rows an arrangement uses.</summary>
    public static int Rows(IReadOnlyDictionary<PortalCard, PortalCell> layout) => layout.Values.Max(c => c.Row) + 1;
}
