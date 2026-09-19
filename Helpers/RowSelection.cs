namespace CanfarDesktop.Helpers;

/// <summary>
/// Which rows of a results page are selected, and which one a detail request means.
///
/// <para>Three pieces of state that have to move together: the selected set, the PRIMARY row (what
/// "show me this one" and an agent's <c>show_search_row_detail</c> act on), and the ANCHOR a
/// Shift-click measures its range from. Keeping them in the page meant the rules could only be
/// checked by clicking, and the interesting ones are the rules nobody clicks through — what happens
/// when you deselect the primary row, or Shift-click twice.</para>
///
/// <para>Page-relative indices throughout: turning a page or re-filtering builds a different list, so
/// <see cref="ClampTo"/> drops anything the shorter page no longer has rather than letting it point
/// at the wrong observation.</para>
/// </summary>
public sealed class RowSelection
{
    private readonly HashSet<int> _selected = [];

    /// <summary>The selected rows, ascending.</summary>
    public IReadOnlyList<int> Selected => _selected.Order().ToList();

    public int Count => _selected.Count;

    public bool Contains(int index) => _selected.Contains(index);

    /// <summary>The row a detail request means when none is named. Always also in <see cref="Selected"/>.</summary>
    public int? Primary { get; private set; }

    /// <summary>Where the next Shift-click measures from: the last row picked WITHOUT Shift.</summary>
    public int? Anchor { get; private set; }

    /// <summary>A plain click: this row, and only this row.</summary>
    public void SetPrimary(int index)
    {
        _selected.Clear();
        _selected.Add(index);
        Primary = index;
        Anchor = index;
    }

    /// <summary>
    /// A Ctrl-click: add the row, or take it out again.
    ///
    /// Deselecting the primary row hands the role to the lowest still-selected one rather than
    /// leaving it pointing at a row that is no longer highlighted.
    /// </summary>
    public void Toggle(int index)
    {
        if (_selected.Add(index))
        {
            Primary = index;
        }
        else
        {
            _selected.Remove(index);
            if (Primary == index) Primary = _selected.Count > 0 ? _selected.Min() : null;
        }

        // Ctrl-click moves the anchor: the next Shift-click extends from what you just touched.
        Anchor = index;
    }

    /// <summary>
    /// A Shift-click: everything between the anchor and <paramref name="index"/>, inclusive.
    ///
    /// <para>The anchor does NOT move, so shift-clicking twice re-measures from the same place rather
    /// than creeping — pick row 2, Shift-click 8, then Shift-click 5, and you get 2..5, not 2..8 plus
    /// 5..8.</para>
    ///
    /// <para>With no anchor — Shift-clicking as the first thing you do — it behaves as a plain click,
    /// because a range from nowhere is not a range.</para>
    /// </summary>
    public void SelectRange(int index)
    {
        if (Anchor is not int anchor)
        {
            SetPrimary(index);
            return;
        }

        _selected.Clear();
        for (var i = Math.Min(anchor, index); i <= Math.Max(anchor, index); i++) _selected.Add(i);

        // The row you clicked is the one a detail request means, not the top of the range.
        Primary = index;
    }

    /// <summary>Drop everything — a new result set has nothing to do with the old highlighted row.</summary>
    public void Clear()
    {
        _selected.Clear();
        Primary = null;
        Anchor = null;
    }

    /// <summary>
    /// Forget anything past the end of a page that just got shorter (a filter, or the last page).
    /// Returns true when something was dropped, so a caller can tell whether to re-render.
    /// </summary>
    public bool ClampTo(int rowCount)
    {
        var dropped = _selected.RemoveWhere(i => i < 0 || i >= rowCount) > 0;

        if (Primary is int p && (p < 0 || p >= rowCount))
        {
            Primary = _selected.Count > 0 ? _selected.Min() : null;
            dropped = true;
        }
        if (Anchor is int a && (a < 0 || a >= rowCount))
        {
            Anchor = null;
            dropped = true;
        }
        return dropped;
    }
}
