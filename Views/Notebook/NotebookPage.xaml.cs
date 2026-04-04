using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Input;
using CanfarDesktop.Services.Notebook;
using CanfarDesktop.ViewModels.Notebook;

namespace CanfarDesktop.Views.Notebook;

public sealed partial class NotebookPage : UserControl
{
    public NotebookViewModel ViewModel { get; }

    public NotebookPage(NotebookViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        CellList.ItemsSource = ViewModel.Cells;

        ViewModel.PropertyChanged += (_, e) => DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(ViewModel.Title):
                    TitleText.Text = ViewModel.Title;
                    break;
                case nameof(ViewModel.IsDirty):
                    DirtyIndicator.Visibility = ViewModel.IsDirty ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case nameof(ViewModel.StatusMessage):
                    StatusLabel.Text = ViewModel.StatusMessage;
                    break;
                case nameof(ViewModel.KernelDisplayName):
                    KernelLabel.Text = ViewModel.KernelDisplayName;
                    break;
                case nameof(ViewModel.KernelState):
                    UpdateKernelIndicator(ViewModel.KernelState);
                    break;
            }
        });

        ViewModel.Cells.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            CellCountLabel.Text = $"{ViewModel.Cells.Count} cells";
        });

        // Wire SaveAs for unsaved notebooks
        ViewModel.SaveAsRequested += async () => OnSaveAs(this, new RoutedEventArgs());

        // Initial state
        TitleText.Text = ViewModel.Title;
        KernelLabel.Text = ViewModel.KernelDisplayName;
        StatusLabel.Text = ViewModel.StatusMessage;
        CellCountLabel.Text = $"{ViewModel.Cells.Count} cells";
        UpdateKernelIndicator(ViewModel.KernelState);
    }

    private void UpdateKernelIndicator(KernelState state)
    {
        KernelLabel.Text = state.ToString();
        KernelDot.Fill = state switch
        {
            KernelState.Idle => Helpers.Notebook.ThemeHelper.GetBrush("SystemFillColorSuccessBrush"),
            KernelState.Busy => Helpers.Notebook.ThemeHelper.GetBrush("SystemFillColorCautionBrush"),
            KernelState.Starting => Helpers.Notebook.ThemeHelper.GetBrush("AccentFillColorDefaultBrush"),
            KernelState.Error => Helpers.Notebook.ThemeHelper.GetBrush("SystemFillColorCriticalBrush"),
            _ => new SolidColorBrush(Colors.Gray),
        };
    }

    /// <summary>
    /// Check for recovery files and prompt the user. Call once after page loads.
    /// </summary>
    public async Task CheckRecoveryAsync()
    {
        var recovery = App.Services.GetRequiredService<IRecoveryService>();
        var orphaned = await Task.Run(() => recovery.DetectOrphanedFiles());
        if (orphaned.Count == 0) return;

        var dialog = new ContentDialog
        {
            Title = "Recover unsaved notebooks?",
            Content = $"{orphaned.Count} unsaved notebook(s) found from a previous session.",
            PrimaryButtonText = "Recover",
            SecondaryButtonText = "Discard",
            CloseButtonText = "Later",
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            // Open the most recent recovery file
            var latest = orphaned.OrderByDescending(c => c.LastModifiedUtc).First();
            await OpenFileAsync(latest.AutoSavePath);
            ViewModel.StatusMessage = "[Recovered] " + (latest.OriginalPath ?? latest.DisplayName);
        }
        else if (result == ContentDialogResult.Secondary)
        {
            recovery.DiscardAll();
            ViewModel.StatusMessage = "Recovery files discarded";
        }
    }

    #region File operations

    public async Task OpenFileAsync(string filePath)
    {
        try
        {
            await using var stream = File.OpenRead(filePath);
            var document = await Helpers.Notebook.NotebookParser.ParseAsync(stream);
            ViewModel.LoadFromFile(filePath, document);
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Open failed: {ex.Message}";
        }
    }

    private async void OnOpenFile(object s, RoutedEventArgs e)
    {
        try
        {
            var hWnd = GetWindowHandle();
            if (hWnd == nint.Zero) return;

            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.FileTypeFilter.Add(".ipynb");
            picker.FileTypeFilter.Add("*");

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            await OpenFileAsync(file.Path);
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Open error: {ex.Message}";
        }
    }

    private async void OnSaveAs(object s, RoutedEventArgs e)
    {
        try
        {
            var hWnd = GetWindowHandle();
            if (hWnd == nint.Zero) return;

            var picker = new Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.SuggestedFileName = ViewModel.Title.Replace(".ipynb", "") + ".ipynb";
            picker.FileTypeChoices.Add("Jupyter Notebook", [".ipynb"]);

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            await ViewModel.SaveAsCommand.ExecuteAsync(file.Path);
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Save As error: {ex.Message}";
        }
    }

    private void OnNewNotebook(object s, RoutedEventArgs e)
    {
        ViewModel.LoadNew();
    }

    private static nint GetWindowHandle()
    {
        if (WindowHelper.ActiveWindows.Count > 0)
            return WinRT.Interop.WindowNative.GetWindowHandle(WindowHelper.ActiveWindows[0]);
        return nint.Zero;
    }

    #endregion

    #region Toolbar handlers

    private void OnSave(object s, RoutedEventArgs e) => ViewModel.SaveCommand.Execute(null);
    private void OnAddCodeCell(object s, RoutedEventArgs e) => ViewModel.AddCellBelowCommand.Execute("code");
    private void OnAddMarkdownCell(object s, RoutedEventArgs e) => ViewModel.AddCellBelowCommand.Execute("markdown");
    private void OnMoveCellUp(object s, RoutedEventArgs e) => ViewModel.MoveCellUpCommand.Execute(null);
    private void OnMoveCellDown(object s, RoutedEventArgs e) => ViewModel.MoveCellDownCommand.Execute(null);
    private void OnDeleteCell(object s, RoutedEventArgs e) => ViewModel.DeleteSelectedCellCommand.Execute(null);
    private void OnClearAllOutputs(object s, RoutedEventArgs e) => ViewModel.ClearAllOutputsCommand.Execute(null);
    private async void OnRunCell(object s, RoutedEventArgs e) => await ViewModel.RunSelectedCellCommand.ExecuteAsync(null);
    private async void OnRunAll(object s, RoutedEventArgs e) => await ViewModel.RunAllCellsCommand.ExecuteAsync(null);
    private async void OnInterrupt(object s, RoutedEventArgs e) => await ViewModel.InterruptKernelCommand.ExecuteAsync(null);
    private async void OnRestart(object s, RoutedEventArgs e) => await ViewModel.RestartKernelCommand.ExecuteAsync(null);

    #endregion

    #region Keyboard shortcuts

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                       .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                        .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        switch (e.Key)
        {
            case Windows.System.VirtualKey.S when ctrl && !shift:
                ViewModel.SaveCommand.Execute(null);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.S when ctrl && shift:
                OnSaveAs(sender, e);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.O when ctrl:
                OnOpenFile(sender, e);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.N when ctrl:
                OnNewNotebook(sender, e);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Enter when ctrl && !shift:
                _ = ViewModel.RunSelectedCellCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Enter when shift && !ctrl:
                _ = ViewModel.RunSelectedAndAdvanceCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Enter when ctrl && shift:
                _ = ViewModel.RunAllCellsCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.A when ctrl && shift:
                ViewModel.AddCellAboveCommand.Execute("code");
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.B when ctrl && shift:
                ViewModel.AddCellBelowCommand.Execute("code");
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Delete when ctrl:
                ViewModel.DeleteSelectedCellCommand.Execute(null);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Up when ctrl && shift:
                ViewModel.MoveCellUpCommand.Execute(null);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Down when ctrl && shift:
                ViewModel.MoveCellDownCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    #endregion
}
