using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Views.Controls;

namespace CanfarDesktop.Views.Dialogs;

/// <summary>
/// The launch form, in a dialog. It used to take two thirds of the Portal while idle; it opens now from
/// Launch session on Active sessions, from "Use this image" with the image chosen, and for agents from
/// show_launch_form — as on Verbinal for Linux.
///
/// <para>One dialog for the Portal's life, holding the one form: what was typed is still there when it
/// opens again, and "Use this image" fills the form it will show. WinUI opens one dialog at a time, so a
/// launch closes this first to show its progress (<see cref="CloseAsync"/>).</para>
/// </summary>
public sealed partial class LaunchFormDialog : ContentDialog
{
    /// <summary>The showing, from the moment it is asked for until the dialog has closed.</summary>
    private Task _showing = Task.CompletedTask;

    public LaunchFormDialog(LaunchFormControl form)
    {
        InitializeComponent();
        FormHost.Content = form;
    }

    /// <summary>True from the moment it is asked to open until it has closed — not only once it is on screen.</summary>
    public bool IsOpen => !_showing.IsCompleted;

    /// <summary>Show it, unless it is showing already.</summary>
    public void Open(XamlRoot root)
    {
        if (IsOpen) return;
        XamlRoot = root;
        _showing = ShowAndWaitAsync();
    }

    private async Task ShowAndWaitAsync()
    {
        try { await ShowAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Launch form dialog error: {ex.Message}"); }
    }

    /// <summary>Close it and wait until it has — the next dialog cannot open before. False when it was not open.</summary>
    public async Task<bool> CloseAsync()
    {
        if (!IsOpen) return false;
        Hide();
        // Bounded: asked to close in the instant between being asked to open and opening, it may not hear it.
        await Task.WhenAny(_showing, Task.Delay(TimeSpan.FromSeconds(5)));
        return true;
    }
}
