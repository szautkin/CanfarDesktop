using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// What a mark's menu offers.
///
/// <para>One list, three renderings — the FITS canvas, the cube canvas, and the rows of the marks
/// panel. They are three ways of pointing at the same object, so a menu that differed between them
/// would be three menus to keep in step. This is the list; turning it into a flyout is separate, and
/// deliberately so, because the rules are here and the drawing is not.</para>
/// </summary>
public class MarkCommandsTests
{
    private static MarkCommands.Context Fits(bool sky = true) => new(CanLocateOnSky: sky, CanExportFigure: true);
    private static MarkCommands.Context Cube(bool sky = true) => new(CanLocateOnSky: sky, CanExportFigure: false);

    private static IEnumerable<MarkCommand> Commands(MarkCommands.Context c)
        => MarkCommands.For(c).Select(i => i.Command);

    [Fact]
    public void TheCommandsThatAlwaysApplyAreAlwaysThere()
    {
        foreach (var context in new[] { Fits(), Cube(), Fits(sky: false), Cube(sky: false) })
            Assert.All(
                new[] { MarkCommand.EditLabel, MarkCommand.CopyCoordinates, MarkCommand.Centre, MarkCommand.Delete },
                wanted => Assert.Contains(wanted, Commands(context)));
    }

    /// <summary>
    /// Framing a figure on one mark is a FITS idea. Left out of a cube's menu rather than shown
    /// greyed forever — a permanently dead entry teaches nothing.
    /// </summary>
    [Fact]
    public void OnlyTheFitsViewerOffersAFigureAroundTheMark()
    {
        Assert.Contains(MarkCommand.ExportFigure, Commands(Fits()));
        Assert.DoesNotContain(MarkCommand.ExportFigure, Commands(Cube()));
    }

    /// <summary>
    /// Without WCS there is nowhere on the sky to send a search. The command stays in the menu and is
    /// greyed: hiding it would leave a person wondering whether the app can do it at all.
    /// </summary>
    [Fact]
    public void SearchHereIsGreyedRatherThanHiddenWithoutAWcs()
    {
        var item = Assert.Single(MarkCommands.For(Fits(sky: false)).Where(i => i.Command == MarkCommand.SearchHere));

        Assert.False(item.Enabled);
        Assert.NotNull(item.DisabledReasonUid);
    }

    [Fact]
    public void SearchHereIsLiveWhenTheMarkHasASkyPosition()
    {
        var item = Assert.Single(MarkCommands.For(Fits()).Where(i => i.Command == MarkCommand.SearchHere));

        Assert.True(item.Enabled);
        Assert.Null(item.DisabledReasonUid);
    }

    /// <summary>A greyed item that does not say why reads as a broken menu.</summary>
    [Fact]
    public void EveryDisabledItemSaysWhy()
        => Assert.All(
            MarkCommands.For(Fits(sky: false)).Where(i => !i.Enabled),
            i => Assert.False(string.IsNullOrWhiteSpace(i.DisabledReasonUid)));

    /// <summary>Delete is the only destructive one, and it is last so a slipped click misses it.</summary>
    [Fact]
    public void DeleteIsTheOnlyDestructiveCommandAndItComesLast()
    {
        foreach (var context in new[] { Fits(), Cube() })
        {
            var items = MarkCommands.For(context);

            Assert.Equal(MarkCommand.Delete, items[^1].Command);
            Assert.True(items[^1].Destructive);
            Assert.Single(items.Where(i => i.Destructive));
        }
    }

    /// <summary>Every entry needs words and a glyph, or it renders as a blank row.</summary>
    [Fact]
    public void EveryItemHasAResourceKeyAndAGlyph()
        => Assert.All(MarkCommands.For(Fits()), i =>
        {
            Assert.False(string.IsNullOrWhiteSpace(i.Uid));
            Assert.StartsWith("Marks_Cmd", i.Uid);
            Assert.False(string.IsNullOrWhiteSpace(i.Glyph));
        });

    /// <summary>No command appears twice — a menu with two Deletes is a menu nobody trusts.</summary>
    [Fact]
    public void NoCommandIsOfferedTwice()
    {
        var items = MarkCommands.For(Fits());

        Assert.Equal(items.Count, items.Select(i => i.Command).Distinct().Count());
    }
}
