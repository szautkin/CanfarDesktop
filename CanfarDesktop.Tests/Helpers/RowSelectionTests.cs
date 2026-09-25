using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// Which rows are highlighted, which one a detail request acts on, and where a Shift-click measures
/// from. These lived in the page, so the only way to check them was to click — and the interesting
/// rules are the ones nobody clicks through: deselecting the primary row, Shift-clicking twice, and
/// a page that gets shorter under a filter.
/// </summary>
public class RowSelectionTests
{
    // ── A plain click ────────────────────────────────────────────────────────

    [Fact]
    public void SetPrimary_SelectsExactlyThatRow()
    {
        var s = new RowSelection();
        s.SetPrimary(3);

        Assert.Equal([3], s.Selected);
        Assert.Equal(3, s.Primary);
        Assert.Equal(3, s.Anchor);
    }

    [Fact]
    public void SetPrimary_ReplacesAnyEarlierSelection()
    {
        var s = new RowSelection();
        s.SetPrimary(1);
        s.Toggle(2);
        s.SetPrimary(7);

        Assert.Equal([7], s.Selected);
    }

    // ── Ctrl-click ───────────────────────────────────────────────────────────

    [Fact]
    public void Toggle_AddsAndRemoves()
    {
        var s = new RowSelection();
        s.SetPrimary(1);
        s.Toggle(4);

        Assert.Equal([1, 4], s.Selected);

        s.Toggle(4);
        Assert.Equal([1], s.Selected);
    }

    /// <summary>
    /// Deselecting the primary row must hand the role on — leaving it pointing at a row that is no
    /// longer highlighted would make "show me this one" mean a row nobody can see selected.
    /// </summary>
    [Fact]
    public void Toggle_RemovingThePrimary_HandsTheRoleToTheLowestRemaining()
    {
        var s = new RowSelection();
        s.SetPrimary(5);
        s.Toggle(2);
        s.Toggle(9);
        Assert.Equal(9, s.Primary);

        s.Toggle(9);

        Assert.Equal([2, 5], s.Selected);
        Assert.Equal(2, s.Primary);
    }

    [Fact]
    public void Toggle_RemovingTheLastRow_LeavesNoPrimary()
    {
        var s = new RowSelection();
        s.SetPrimary(3);
        s.Toggle(3);

        Assert.Empty(s.Selected);
        Assert.Null(s.Primary);
    }

    // ── Shift-click ──────────────────────────────────────────────────────────

    [Fact]
    public void SelectRange_TakesEverythingBetweenTheAnchorAndTheClick()
    {
        var s = new RowSelection();
        s.SetPrimary(2);
        s.SelectRange(5);

        Assert.Equal([2, 3, 4, 5], s.Selected);
    }

    /// <summary>Dragging the range backwards is the same range.</summary>
    [Fact]
    public void SelectRange_WorksUpwards()
    {
        var s = new RowSelection();
        s.SetPrimary(6);
        s.SelectRange(3);

        Assert.Equal([3, 4, 5, 6], s.Selected);
    }

    /// <summary>
    /// The rule that makes repeated Shift-clicks usable: the anchor does not move, so the range is
    /// re-measured from the same place rather than creeping.
    /// </summary>
    [Fact]
    public void SelectRange_Twice_ReMeasuresFromTheSameAnchor()
    {
        var s = new RowSelection();
        s.SetPrimary(2);
        s.SelectRange(8);
        s.SelectRange(5);

        Assert.Equal([2, 3, 4, 5], s.Selected);   // 2..5, not 2..8 plus 5..8
        Assert.Equal(2, s.Anchor);
    }

    /// <summary>The row you clicked is what a detail request means, not the top of the range.</summary>
    [Fact]
    public void SelectRange_MakesTheClickedRowThePrimary()
    {
        var s = new RowSelection();
        s.SetPrimary(6);
        s.SelectRange(2);

        Assert.Equal(2, s.Primary);
    }

    /// <summary>A range from nowhere is not a range.</summary>
    [Fact]
    public void SelectRange_WithNoAnchor_BehavesAsAPlainClick()
    {
        var s = new RowSelection();
        s.SelectRange(4);

        Assert.Equal([4], s.Selected);
        Assert.Equal(4, s.Primary);
        Assert.Equal(4, s.Anchor);
    }

    /// <summary>Ctrl-click moves the anchor, so the next Shift-click extends from what you just touched.</summary>
    [Fact]
    public void Toggle_MovesTheAnchor()
    {
        var s = new RowSelection();
        s.SetPrimary(1);
        s.Toggle(6);
        s.SelectRange(8);

        Assert.Equal([6, 7, 8], s.Selected);
    }

    [Fact]
    public void SelectRange_OntoItself_IsOneRow()
    {
        var s = new RowSelection();
        s.SetPrimary(4);
        s.SelectRange(4);

        Assert.Equal([4], s.Selected);
    }

    // ── A page that changed underneath ───────────────────────────────────────

    /// <summary>
    /// Turning a page or re-filtering builds a different list, so an index kept from the old one would
    /// point at a different observation. Anything past the end is dropped.
    /// </summary>
    [Fact]
    public void ClampTo_DropsRowsThePageNoLongerHas()
    {
        var s = new RowSelection();
        s.SetPrimary(1);
        s.SelectRange(9);

        Assert.True(s.ClampTo(5));
        Assert.Equal([1, 2, 3, 4], s.Selected);
    }

    [Fact]
    public void ClampTo_MovesThePrimaryWhenItIsTheOneDropped()
    {
        var s = new RowSelection();
        s.SetPrimary(2);
        s.Toggle(8);
        Assert.Equal(8, s.Primary);

        s.ClampTo(5);

        Assert.Equal([2], s.Selected);
        Assert.Equal(2, s.Primary);
    }

    [Fact]
    public void ClampTo_ClearsAnAnchorPastTheEnd()
    {
        var s = new RowSelection();
        s.SetPrimary(9);

        s.ClampTo(3);

        Assert.Null(s.Anchor);
        Assert.Null(s.Primary);
        Assert.Empty(s.Selected);
    }

    /// <summary>Nothing to drop reports nothing dropped, so a caller can skip a re-render.</summary>
    [Fact]
    public void ClampTo_ReportsWhetherAnythingChanged()
    {
        var s = new RowSelection();
        s.SetPrimary(1);

        Assert.False(s.ClampTo(10));   // a 10-row page still has row 1
        Assert.True(s.ClampTo(1));     // a 1-row page has only row 0, so row 1 goes
        Assert.Empty(s.Selected);
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        var s = new RowSelection();
        s.SetPrimary(2);
        s.SelectRange(6);
        s.Clear();

        Assert.Empty(s.Selected);
        Assert.Null(s.Primary);
        Assert.Null(s.Anchor);
    }

    /// <summary>Ascending, whatever order they were clicked in — it is a set of rows, not a history.</summary>
    [Fact]
    public void Selected_IsAscending()
    {
        var s = new RowSelection();
        s.SetPrimary(7);
        s.Toggle(2);
        s.Toggle(5);

        Assert.Equal([2, 5, 7], s.Selected);
    }
}
