namespace CanfarDesktop.Helpers;

/// <summary>What a person can ask for about one mark.</summary>
public enum MarkCommand
{
    /// <summary>Type its words, in the field over the mark itself.</summary>
    EditLabel,

    /// <summary>Its position, on the clipboard, in the form the rest of the app prints it.</summary>
    CopyCoordinates,

    /// <summary>Bring it into view.</summary>
    Centre,

    /// <summary>Point the Search form at its sky position.</summary>
    SearchHere,

    /// <summary>A publication figure framed on it.</summary>
    ExportFigure,

    /// <summary>The same region cut out of the archive's file with SODA — at full resolution, only that part.</summary>
    CutOut,

    /// <summary>Write the marks out as data — JSON with provenance, or a DS9 region file.</summary>
    ExportMarks,

    /// <summary>Remove it.</summary>
    Delete,
}

/// <summary>
/// One entry in a mark's menu: which command, the resource key for its words, the glyph beside them,
/// whether it is reachable right now, and whether it is the destructive one.
/// </summary>
/// <param name="Enabled">
/// False for a command that belongs in this viewer's menu but cannot act on THIS mark — shown greyed,
/// with <paramref name="DisabledReasonUid"/> as its tooltip. Hiding it instead would leave a person
/// wondering whether the app can do the thing at all; greying it says "yes, but not here".
/// </param>
public readonly record struct MarkCommandItem(
    MarkCommand Command,
    string Uid,
    string Glyph,
    bool Enabled = true,
    bool Destructive = false,
    string? DisabledReasonUid = null);

/// <summary>
/// What a mark's menu contains, given what the viewer showing it can actually do.
///
/// <para>One list, three renderings: the FITS canvas, the cube canvas, and the rows of the marks
/// panel. They are three ways of pointing at the same object, and a menu that differed between them
/// would be three menus to keep in step.</para>
///
/// <para>Free of any UI type on purpose — this is the decision, not the drawing, and the decision is
/// the part with rules in it. Every command here has an MCP tool behind it, because an agent is a
/// user of this app too and a menu item it cannot reach is a feature half the users do not have.</para>
/// </summary>
public static class MarkCommands
{
    /// <summary>What the viewer showing this mark is able to do with it.</summary>
    /// <param name="CanLocateOnSky">
    /// The mark has, or can be given, a sky position. False for an image with no WCS — the mark is
    /// still perfectly good, there is just nowhere on the sky to send a search.
    /// </param>
    /// <param name="CanExportFigure">
    /// This viewer can frame a publication figure on one mark. True of the FITS viewer. Not a cube
    /// concept, so the command is left out of a cube's menu rather than shown greyed forever.
    /// </param>
    /// <param name="CanCutOut">
    /// Null where the viewer does not offer cutouts at all. False where it does but not for this mark:
    /// the file is not an archive observation in Research, or the mark has no sky position — greyed,
    /// with the reason. True when the mark's region can be cut from the observation's file.
    /// </param>
    public readonly record struct Context(bool CanLocateOnSky, bool CanExportFigure, bool? CanCutOut = null);

    /// <summary>The menu, in order. The destructive command is last and marked as such.</summary>
    public static IReadOnlyList<MarkCommandItem> For(Context context)
    {
        var items = new List<MarkCommandItem>
        {
            new(MarkCommand.EditLabel, "Marks_CmdEditLabel", ""),
            new(MarkCommand.CopyCoordinates, "Marks_CmdCopyCoordinates", ""),
            new(MarkCommand.Centre, "Marks_CmdCentre", ""),
            new(MarkCommand.SearchHere, "Marks_CmdSearchHere", "",
                Enabled: context.CanLocateOnSky,
                DisabledReasonUid: context.CanLocateOnSky ? null : "Marks_CmdNeedsWcs"),
        };

        if (context.CanExportFigure)
            items.Add(new MarkCommandItem(MarkCommand.ExportFigure, "Marks_CmdExportFigure", ""));

        // The mark's region, cut out of the observation's own file — on CADC's side, or from its copy on
        // this computer (show_cutout_editor).
        if (context.CanCutOut is { } canCut)
            items.Add(new MarkCommandItem(MarkCommand.CutOut, "Marks_CmdCutOut", "",
                Enabled: canCut, DisabledReasonUid: canCut ? null : "Marks_CmdCutOutNeedsObservation"));

        // Writing the marks out belongs to every viewer that has marks, which is all of them.
        items.Add(new MarkCommandItem(MarkCommand.ExportMarks, "Marks_CmdExportMarks", ""));

        items.Add(new MarkCommandItem(MarkCommand.Delete, "Marks_CmdDelete", "", Destructive: true));
        return items;
    }
}

/// <summary>
/// A viewer that can answer a mark's menu.
///
/// <para>Three things point at a mark — the FITS canvas, the cube canvas and a row of the marks panel
/// — and all three want the same menu with the same meanings. This is the seam: the panel and the
/// canvases ask a host what the menu should say and tell it what was chosen, and neither of them
/// knows what any command does.</para>
///
/// <para>Without it the panel would have to reach back into a viewer it does not know about, and the
/// one menu would become two implementations that drift.</para>
/// </summary>
public interface IMarkCommandHost
{
    /// <summary>What this viewer can do with this particular mark.</summary>
    MarkCommands.Context CommandContextFor(string id);

    /// <summary>Do it. Unknown or no-longer-present marks are ignored, not thrown over.</summary>
    void InvokeMarkCommand(MarkCommand command, string id);
}
