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
            case Windows.System.VirtualKey.S when ctrl:
                ViewModel.SaveCommand.Execute(null);
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
