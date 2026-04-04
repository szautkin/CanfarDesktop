using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Input;
using CanfarDesktop.Helpers.Notebook;
using CanfarDesktop.Services.Notebook;
using CanfarDesktop.ViewModels.Notebook;

namespace CanfarDesktop.Views.Notebook;

public sealed partial class NotebookTabHost : UserControl
{
    public NotebookTabHostViewModel ViewModel { get; }

    /// <summary>Raised when the last tab is closed — MainWindow navigates back to landing.</summary>
    public event Action? AllTabsClosed;

    public NotebookTabHost(NotebookTabHostViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        Welcome.NewRequested += () => AddNewTab();
        Welcome.OpenPickerRequested += () => OnOpenFile(this, new RoutedEventArgs());
        Welcome.OpenFileRequested += async path => await AddTabForFileAsync(path);

        UpdateWelcomeVisibility();
    }

    #region Tab lifecycle

    public NotebookPage AddNewTab()
    {
        var tabItem = ViewModel.AddNewTab();
        var page = CreateTabViewItem(tabItem);
        UpdateWelcomeVisibility();
        return page;
    }

    public async Task<NotebookPage> AddTabForFileAsync(string filePath)
    {
        var tabItem = ViewModel.AddTabForFile();
        var page = CreateTabViewItem(tabItem);
        UpdateWelcomeVisibility();
        await page.OpenFileAsync(filePath);
        return page;
    }

    private NotebookPage CreateTabViewItem(NotebookTabItem tabItem)
    {
        var page = new NotebookPage(tabItem.ViewModel);

        var tab = new TabViewItem
        {
            Content = page,
            Header = tabItem.Header,
            IconSource = new SymbolIconSource { Symbol = Symbol.Document },
            Tag = tabItem,
        };

        tabItem.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NotebookTabItem.Header))
                DispatcherQueue.TryEnqueue(() => tab.Header = tabItem.Header);
        };

        TabViewControl.TabItems.Add(tab);
        TabViewControl.SelectedItem = tab;
        return page;
    }

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
            foreach (var candidate in orphaned.OrderByDescending(c => c.LastModifiedUtc))
            {
                var page = await AddTabForFileAsync(candidate.AutoSavePath);
                page.ViewModel.StatusMessage = "[Recovered] " + (candidate.OriginalPath ?? candidate.DisplayName);
            }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            recovery.DiscardAll();
        }
    }

    private void OnAddTab(TabView sender, object args) => AddNewTab();

    private async void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab.Tag is not NotebookTabItem tabItem) return;
        if (!await TryCloseTabAsync(tabItem)) return;
        sender.TabItems.Remove(args.Tab);
    }

    /// <summary>
    /// Prompt save if dirty, then close. Returns false if user cancels.
    /// </summary>
    private async Task<bool> TryCloseTabAsync(NotebookTabItem tabItem)
    {
        if (tabItem.ViewModel.IsDirty)
        {
            var dialog = new ContentDialog
            {
                Title = "Unsaved changes",
                Content = $"Save changes to {tabItem.ViewModel.Title}?",
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Don't Save",
                CloseButtonText = "Cancel",
                XamlRoot = XamlRoot,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
                await tabItem.ViewModel.SaveCommand.ExecuteAsync(null);
            else if (result == ContentDialogResult.None)
                return false;
        }

        ViewModel.CloseTab(tabItem);
        UpdateWelcomeVisibility();

        if (TabViewControl.TabItems.Count == 0)
            AllTabsClosed?.Invoke();

        return true;
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabViewControl.SelectedItem is TabViewItem { Tag: NotebookTabItem tabItem })
            ViewModel.ActiveTab = tabItem;
        UpdateWelcomeVisibility();
    }

    private void UpdateWelcomeVisibility()
    {
        var hasTabs = TabViewControl.TabItems.Count > 0;
        Welcome.Visibility = hasTabs ? Visibility.Collapsed : Visibility.Visible;
        TabViewControl.Visibility = hasTabs ? Visibility.Visible : Visibility.Collapsed;
        if (!hasTabs) Welcome.RefreshRecent();
    }

    #endregion

    #region Active ViewModel shortcut

    private NotebookViewModel? ActiveVM => ViewModel.ActiveViewModel;

    #endregion

    #region Toolbar handlers — delegate to active tab's ViewModel

    private void OnNewNotebook(object s, RoutedEventArgs e) => AddNewTab();

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
            if (file is not null)
                await AddTabForFileAsync(file.Path);
        }
        catch (Exception ex)
        {
            if (ActiveVM is not null) ActiveVM.StatusMessage = $"Open error: {ex.Message}";
        }
    }

    private void OnSave(object s, RoutedEventArgs e) => ActiveVM?.SaveCommand.Execute(null);

    private async void OnSaveAs(object s, RoutedEventArgs e)
    {
        if (ActiveVM is null) return;
        try
        {
            var hWnd = GetWindowHandle();
            if (hWnd == nint.Zero) return;

            var picker = new Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.SuggestedFileName = ActiveVM.Title.Replace(".ipynb", "") + ".ipynb";
            picker.FileTypeChoices.Add("Jupyter Notebook", [".ipynb"]);

            var file = await picker.PickSaveFileAsync();
            if (file is not null)
                await ActiveVM.SaveAsCommand.ExecuteAsync(file.Path);
        }
        catch (Exception ex)
        {
            ActiveVM.StatusMessage = $"Save As error: {ex.Message}";
        }
    }

    private void OnAddCodeCell(object s, RoutedEventArgs e) => ActiveVM?.AddCellBelowCommand.Execute("code");
    private void OnAddMarkdownCell(object s, RoutedEventArgs e) => ActiveVM?.AddCellBelowCommand.Execute("markdown");
    private void OnMoveCellUp(object s, RoutedEventArgs e) => ActiveVM?.MoveCellUpCommand.Execute(null);
    private void OnMoveCellDown(object s, RoutedEventArgs e) => ActiveVM?.MoveCellDownCommand.Execute(null);
    private void OnDeleteCell(object s, RoutedEventArgs e) => ActiveVM?.DeleteSelectedCellCommand.Execute(null);
    private void OnClearAllOutputs(object s, RoutedEventArgs e) => ActiveVM?.ClearAllOutputsCommand.Execute(null);
    private async void OnRunCell(object s, RoutedEventArgs e) { if (ActiveVM is not null) await ActiveVM.RunSelectedCellCommand.ExecuteAsync(null); }
    private async void OnRunAll(object s, RoutedEventArgs e) { if (ActiveVM is not null) await ActiveVM.RunAllCellsCommand.ExecuteAsync(null); }
    private async void OnInterrupt(object s, RoutedEventArgs e) { if (ActiveVM is not null) await ActiveVM.InterruptKernelCommand.ExecuteAsync(null); }
    private async void OnRestart(object s, RoutedEventArgs e) { if (ActiveVM is not null) await ActiveVM.RestartKernelCommand.ExecuteAsync(null); }

    private static nint GetWindowHandle()
    {
        if (WindowHelper.ActiveWindows.Count > 0)
            return WinRT.Interop.WindowNative.GetWindowHandle(WindowHelper.ActiveWindows[0]);
        return nint.Zero;
    }

    #endregion

    #region Keyboard shortcuts (tab-level)

    /// <summary>
    /// True when a TextBox has focus (edit mode). False = command mode.
    /// In command mode, single keys trigger cell commands (A/B/DD/Y/M/Escape/arrows).
    /// </summary>
    private bool IsEditMode => FocusManager.GetFocusedElement(XamlRoot) is TextBox;

    // For double-key commands (DD, II, 00)
    private Windows.System.VirtualKey _lastCommandKey;
    private DateTime _lastCommandKeyTime;

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                       .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                        .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        // Command-mode shortcuts (when no TextBox has focus)
        if (!IsEditMode && !ctrl && !shift && ActiveVM is not null)
        {
            if (HandleCommandModeKey(e.Key))
            {
                e.Handled = true;
                return;
            }
        }

        switch (e.Key)
        {
            case Windows.System.VirtualKey.N when ctrl:
                AddNewTab();
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.O when ctrl:
                OnOpenFile(sender, e);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.W when ctrl:
                CloseActiveTab();
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.S when ctrl && !shift:
                ActiveVM?.SaveCommand.Execute(null);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.S when ctrl && shift:
                OnSaveAs(sender, e);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Enter when ctrl && !shift:
                if (ActiveVM is not null) _ = ActiveVM.RunSelectedCellCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Enter when shift && !ctrl:
                if (ActiveVM is not null) _ = ActiveVM.RunSelectedAndAdvanceCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Enter when ctrl && shift:
                if (ActiveVM is not null) _ = ActiveVM.RunAllCellsCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Handle command-mode single-key shortcuts (Jupyter-compatible).
    /// Returns true if the key was handled.
    /// </summary>
    private bool HandleCommandModeKey(Windows.System.VirtualKey key)
    {
        var vm = ActiveVM!;
        var now = DateTime.UtcNow;
        var isDoubleKey = key == _lastCommandKey && (now - _lastCommandKeyTime).TotalMilliseconds < 500;

        switch (key)
        {
            // Navigation
            case Windows.System.VirtualKey.Up or Windows.System.VirtualKey.K:
                if (vm.SelectedCellIndex > 0)
                    vm.SelectCell(vm.SelectedCellIndex - 1);
                return true;

            case Windows.System.VirtualKey.Down or Windows.System.VirtualKey.J:
                if (vm.SelectedCellIndex < vm.Cells.Count - 1)
                    vm.SelectCell(vm.SelectedCellIndex + 1);
                return true;

            // Enter edit mode
            case Windows.System.VirtualKey.Enter:
                if (vm.SelectedCell is ViewModels.Notebook.MarkdownCellViewModel md)
                    md.EnterEditMode();
                // For code cells, focus the TextBox (handled by cell control)
                return true;

            // Cell operations
            case Windows.System.VirtualKey.A:
                vm.AddCellAboveCommand.Execute("code");
                return true;

            case Windows.System.VirtualKey.B:
                vm.AddCellBelowCommand.Execute("code");
                return true;

            case Windows.System.VirtualKey.D:
                if (isDoubleKey) // DD = delete
                {
                    vm.DeleteSelectedCellCommand.Execute(null);
                    _lastCommandKey = default;
                    return true;
                }
                _lastCommandKey = key;
                _lastCommandKeyTime = now;
                return true;

            // Cell type
            case Windows.System.VirtualKey.Y:
                vm.ChangeCellTypeCommand.Execute("code");
                return true;

            case Windows.System.VirtualKey.M:
                vm.ChangeCellTypeCommand.Execute("markdown");
                return true;

            // Copy/paste cell
            case Windows.System.VirtualKey.C:
                // TODO: copy cell to clipboard
                return true;

            case Windows.System.VirtualKey.V:
                // TODO: paste cell
                return true;

            // Kernel
            case Windows.System.VirtualKey.I:
                if (isDoubleKey) // II = interrupt
                {
                    _ = vm.InterruptKernelCommand.ExecuteAsync(null);
                    _lastCommandKey = default;
                    return true;
                }
                _lastCommandKey = key;
                _lastCommandKeyTime = now;
                return true;

            case Windows.System.VirtualKey.Number0:
                if (isDoubleKey) // 00 = restart
                {
                    _ = vm.RestartKernelCommand.ExecuteAsync(null);
                    _lastCommandKey = default;
                    return true;
                }
                _lastCommandKey = key;
                _lastCommandKeyTime = now;
                return true;

            default:
                _lastCommandKey = default;
                return false;
        }
    }

    private async void CloseActiveTab()
    {
        if (TabViewControl.SelectedItem is not TabViewItem { Tag: NotebookTabItem tabItem }) return;
        if (!await TryCloseTabAsync(tabItem)) return;
        TabViewControl.TabItems.Remove(TabViewControl.SelectedItem);
    }

    #endregion
}
