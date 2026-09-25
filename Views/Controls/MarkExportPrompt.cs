using Windows.Storage.Pickers;
using WinRT.Interop;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Views.Controls;

/// <summary>
/// Ask where the marks should go, then write them there.
///
/// <para>It writes nothing itself. It picks a path and hands it to <c>AppViewStateService</c>'s export
/// action — the very same call <c>export_annotations</c> makes, arriving at the same builder, the same
/// provenance lookup and the same atomic write. A person and an agent asking for the same thing get
/// the same file, and there is one implementation to be right about which is not the one with the
/// picker in it.</para>
///
/// <para>Both formats are offered rather than chosen: JSON carries everything and nothing reads it, a
/// DS9 region file carries less and every tool in the field opens it, and which one is wanted depends
/// entirely on what happens to the marks next.</para>
/// </summary>
public static class MarkExportPrompt
{
    /// <summary>
    /// Run the whole thing: picker, export, and a sentence about what happened.
    ///
    /// Returns null when the picker was dismissed — a cancelled save is not a result to report, and
    /// saying "cancelled" to somebody who just pressed Cancel is noise.
    /// </summary>
    public static async Task<string?> RunAsync(string viewer, string target, string suggestedName)
    {
        var picker = new FileSavePicker { SuggestedFileName = suggestedName + "-marks" };
        picker.FileTypeChoices.Add(Loc.T("Marks_FileTypeJson"), [".json"]);
        picker.FileTypeChoices.Add(Loc.T("Marks_FileTypeDs9"), [".reg"]);

        // A picker needs an owning window; a control has none of its own.
        var hwnd = Views.WindowHelper.ActiveWindows.Count > 0
            ? WindowNative.GetWindowHandle(Views.WindowHelper.ActiveWindows[0])
            : nint.Zero;
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file is null) return null;

        var viewState = App.Services.GetService(typeof(Mcp.AppViewStateService)) as Mcp.AppViewStateService;
        if (viewState is null) return Loc.F("Marks_ExportFailed", Loc.T("Marks_ViewerUnavailable"));

        var outcome = await viewState.ExportAnnotationsAsync(
            new Mcp.Tools.Write.AnnotationExportRequest(file.Path, viewer, target));

        return outcome.Exported
            ? Loc.F("Marks_ExportedCount", outcome.Marks, System.IO.Path.GetFileName(file.Path))
            : Loc.F("Marks_ExportFailed", outcome.Message ?? Loc.T("Marks_UnknownError"));
    }
}
