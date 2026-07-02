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

        // Apply settings
        var settings = App.Services.GetRequiredService<NotebookSettings>();
        ToolbarPanel.Visibility = settings.ShowToolbar ? Visibility.Visible : Visibility.Collapsed;
        settings.Changed += () => DispatcherQueue?.TryEnqueue(() =>
            ToolbarPanel.Visibility = settings.ShowToolbar ? Visibility.Visible : Visibility.Collapsed);

        Welcome.NewRequested += () => AddNewTab();
        Welcome.OpenPickerRequested += () => OnOpenFile(this, new RoutedEventArgs());
        Welcome.OpenFileRequested += async path => await AddTabForFileAsync(path);

        // Wire cell navigation from editor boundary (Up at first line, Down at last line)
        _navigateHandler = delta =>
        {
            if (ActiveVM is null) return;
            var newIndex = ActiveVM.SelectedCellIndex + delta;
            if (newIndex >= 0 && newIndex < ActiveVM.Cells.Count)
            {
                ActiveVM.SelectCell(newIndex);
                ActivePage?.BringCellIntoView(newIndex);
            }
        };
        CodeCellControl.NavigateRequested += _navigateHandler;
        Unloaded += (_, _) => CodeCellControl.NavigateRequested -= _navigateHandler;

        UpdateWelcomeVisibility();
    }

    private readonly Action<int> _navigateHandler;

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

    /// <summary>Discard the active tab without prompting (used to drop an orphan tab from a failed MCP open).</summary>
    public void DiscardActiveTab()
    {
        if (ViewModel.ActiveTab is not { } tabItem) return;
        var tvi = TabViewControl.TabItems.OfType<TabViewItem>().FirstOrDefault(t => ReferenceEquals(t.Tag, tabItem));
        ViewModel.CloseTab(tabItem);          // disposes the tab VM (+ its never-started kernel) and re-selects
        if (tvi is not null) TabViewControl.TabItems.Remove(tvi);
        UpdateWelcomeVisibility();
    }

    // ── MCP surface (active tab) ─────────────────────────────────────────────────
    // The viewer owns its own MCP logic (mirrors FitsTabHost / CubeViewerPage.Mcp). These run on the UI
    // thread; MainWindow keeps only the serialization gate + the open/create branch (shell navigation +
    // host instantiation a UserControl can't do to itself).

    /// <summary>
    /// Resolve a notebook selector (<see cref="NotebookCommand.Notebook"/>) to a target VM: the active
    /// notebook when the selector is empty, or the open notebook whose id/path matches. Throws on a
    /// selector that matches no open notebook (a real error, distinct from "nothing is open"); returns
    /// null only when there's no active notebook and no selector.
    /// </summary>
    private NotebookViewModel? ResolveTarget(string? selector)
    {
        var candidates = ViewModel.Tabs
            .Select(t => new NotebookTargetResolver.Candidate(t.ViewModel.NotebookId, t.ViewModel.FilePath))
            .ToList();
        var (kind, index) = NotebookTargetResolver.Resolve(candidates, selector);
        return kind switch
        {
            NotebookTargetKind.Resolved => ViewModel.Tabs[index].ViewModel,
            NotebookTargetKind.UseActive => ViewModel.ActiveViewModel,
            _ => throw new InvalidOperationException(
                $"No open notebook matches '{selector}'. Call list_open_notebooks for the open notebooks (id + path)."),
        };
    }

    /// <summary>All currently-open notebook tabs (for list_open_notebooks).</summary>
    public IReadOnlyList<OpenNotebookInfo> ListOpenNotebooks()
    {
        var active = ViewModel.ActiveViewModel;
        return ViewModel.Tabs.Select(t =>
        {
            var vm = t.ViewModel;
            return new OpenNotebookInfo(
                vm.NotebookId, vm.Title, vm.FilePath,
                ReferenceEquals(vm, active), vm.IsDirty, vm.Cells.Count, vm.KernelState.ToString());
        }).ToList();
    }

    /// <summary>Apply a NON-open/create notebook command to the targeted tab (active by default, or the
    /// one named by <see cref="NotebookCommand.Notebook"/>). Returns null if no notebook is open.</summary>
    public async Task<NotebookState?> ApplyNotebookCommandAsync(NotebookCommand cmd)
    {
        var vm = ResolveTarget(cmd.Notebook);
        if (vm is null) return null;

        // Index-addressed ops fail fast on an out-of-range index (consistent with move_cell) rather than
        // silently no-op'ing while reporting success. add_cell intentionally appends instead.
        int RequireIndex(int? i)
            => InRange(vm, i) ? i!.Value
                : throw new InvalidOperationException($"cell index {i} out of range (notebook has {vm.Cells.Count} cells).");

        switch (cmd.Op)
        {
            case NotebookOp.Save:
                if (cmd.Path is { Length: > 0 } sp) await vm.SaveAsAsync(sp);
                else if (vm.FilePath is null)
                    throw new InvalidOperationException(
                        "Cannot save an unsaved (Untitled) notebook without a path — pass a full .ipynb path to save_notebook.");
                else await vm.SaveAsync();
                break;
            case NotebookOp.EditCell:
                { var ei = RequireIndex(cmd.Index); if (cmd.Source is not null) vm.Cells[ei].Source = cmd.Source; }
                break;
            case NotebookOp.AddCell:
                AddNotebookCell(vm, cmd.Index, cmd.CellType ?? "code", cmd.Source);
                break;
            case NotebookOp.DeleteCell:
                vm.SelectCell(RequireIndex(cmd.Index)); vm.DeleteSelectedCell();
                break;
            case NotebookOp.ChangeCellType:
                if (cmd.CellType is not null) { vm.SelectCell(RequireIndex(cmd.Index)); vm.ChangeCellType(cmd.CellType); }
                break;
            case NotebookOp.MoveCell:
                if (!MoveNotebookCell(vm, cmd.Index ?? -1, cmd.ToIndex ?? -1))
                    throw new InvalidOperationException($"move_cell: from/to out of range (notebook has {vm.Cells.Count} cells).");
                break;
            case NotebookOp.ClearOutputs:
                vm.ClearAllOutputs();
                break;
            case NotebookOp.RunCell:
                vm.SelectCell(RequireIndex(cmd.Index)); await vm.RunSelectedCellAsync();
                break;
            case NotebookOp.RunAll:
                await vm.RunAllCellsAsync();
                break;
            case NotebookOp.StartKernel:
                await vm.StartKernelAsync();
                break;
            case NotebookOp.InterruptKernel:
                await vm.InterruptKernelAsync();
                break;
            case NotebookOp.RestartKernel:
                await vm.RestartKernelAsync();
                break;
        }

        // The tab may have been CLOSED mid-await (run/save/kernel ops yield the UI pump) — don't report
        // state from an orphaned VM. Membership (not active-ness) is the check: a background-targeted op
        // is intentionally not the active tab, but is still open.
        return ViewModel.Tabs.Any(t => ReferenceEquals(t.ViewModel, vm)) ? ToNotebookState(vm) : null;
    }

    /// <summary>A snapshot of a notebook tab (active by default, or the one named by
    /// <paramref name="selector"/>), or null if none is open.</summary>
    public NotebookState? GetNotebookState(string? selector = null)
        => ResolveTarget(selector) is { } vm ? ToNotebookState(vm) : null;

    /// <summary>The outputs of a cell in a notebook (active by default, or <paramref name="selector"/>),
    /// or null if none open / index out of range.</summary>
    public NotebookCellOutputs? GetCellOutputs(int index, string? selector = null)
    {
        var vm = ResolveTarget(selector);
        if (vm is null || index < 0 || index >= vm.Cells.Count) return null;
        var cell = vm.Cells[index];
        if (cell is not CodeCellViewModel code)
            return new NotebookCellOutputs(index, cell.CellType, null, Array.Empty<NotebookOutputInfo>());

        var outs = new List<NotebookOutputInfo>(code.Outputs.Count);
        foreach (var o in code.Outputs)
        {
            var text = o.TextContent ?? string.Empty;
            var tb = o.Traceback ?? string.Empty;
            outs.Add(new NotebookOutputInfo(
                o.OutputType,
                ClipText(text, McpNotebookTextCap), text.Length > McpNotebookTextCap,
                o.IsError, o.ErrorType, // the exception type only ("ValueError"); the message is in the traceback
                ClipText(tb, McpNotebookTextCap), tb.Length > McpNotebookTextCap,
                o.HasImage, o.HasHtml));
        }
        return new NotebookCellOutputs(index, "code", code.ExecutionCount, outs);
    }

    /// <summary>A notebook's kernel status (active by default, or <paramref name="selector"/>), or a
    /// dead/no-notebook placeholder.</summary>
    public NotebookKernelInfo GetKernelInfo(string? selector = null)
    {
        var vm = ResolveTarget(selector);
        return vm is null
            ? new NotebookKernelInfo("Dead", "no notebook open", "")
            : new NotebookKernelInfo(vm.KernelState.ToString(), vm.KernelStatusText, vm.KernelDisplayName);
    }

    // Cap notebook cell source + outputs for MCP transport (one place so the two serializers can't drift).
    private const int McpNotebookTextCap = 16000;

    private static bool InRange(NotebookViewModel vm, int? index)
        => index is { } i && i >= 0 && i < vm.Cells.Count;

    private static void AddNotebookCell(NotebookViewModel vm, int? index, string type, string? source)
    {
        if (index is { } i && i >= 0 && i < vm.Cells.Count)
        {
            vm.SelectCell(i);
            vm.AddCellAbove(type); // inserts at i, selects the new cell
        }
        else
        {
            if (vm.Cells.Count > 0) vm.SelectCell(vm.Cells.Count - 1);
            vm.AddCellBelow(type); // appends after the last cell, selects it
        }
        if (source is not null && vm.SelectedCellIndex >= 0 && vm.SelectedCellIndex < vm.Cells.Count)
            vm.Cells[vm.SelectedCellIndex].Source = source;
    }

    private static bool MoveNotebookCell(NotebookViewModel vm, int from, int to)
    {
        int n = vm.Cells.Count;
        if (from < 0 || from >= n || to < 0 || to >= n) return false; // out of range → surface as an error
        if (from == to) return true;                                  // legitimate no-op
        vm.SelectCell(from);
        if (to < from) { for (int i = from; i > to; i--) vm.MoveCellUp(); }
        else { for (int i = from; i < to; i++) vm.MoveCellDown(); }
        return true;
    }

    private static NotebookState ToNotebookState(NotebookViewModel vm)
    {
        var cells = new List<NotebookCellInfo>(vm.Cells.Count);
        for (int i = 0; i < vm.Cells.Count; i++)
        {
            var c = vm.Cells[i];
            var src = c.Source ?? string.Empty;
            int? exec = c is CodeCellViewModel code ? code.ExecutionCount : null;
            int outs = c is CodeCellViewModel cc ? cc.Outputs.Count : 0;
            cells.Add(new NotebookCellInfo(i, c.CellType, ClipText(src, McpNotebookTextCap), src.Length > McpNotebookTextCap, exec, outs));
        }
        return new NotebookState(
            Loaded: true, NotebookId: vm.NotebookId, Title: vm.Title, FilePath: vm.FilePath, FileMode: vm.FileMode.ToString(),
            IsDirty: vm.IsDirty, KernelState: vm.KernelState.ToString(), KernelName: vm.KernelDisplayName,
            SelectedIndex: vm.SelectedCellIndex, CellCount: vm.Cells.Count, Cells: cells);
    }

    private static string ClipText(string? s, int max)
    {
        s ??= string.Empty;
        return s.Length <= max ? s : s[..max];
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
        // Wait for control to be in the visual tree (XamlRoot must be non-null for dialogs)
        if (XamlRoot is null)
        {
            var tcs = new TaskCompletionSource();
            void OnLoaded(object s, RoutedEventArgs e) { Loaded -= OnLoaded; tcs.SetResult(); }
            Loaded += OnLoaded;
            await tcs.Task;
        }

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
                // Restore original path so kernel uses the correct working directory
                if (candidate.OriginalPath is not null && File.Exists(candidate.OriginalPath))
                {
                    page.ViewModel.LoadFromFile(candidate.OriginalPath, page.ViewModel.Document);
                    page.ViewModel.StatusMessage = $"[Recovered] {candidate.OriginalPath}";
                }
                else
                {
                    page.ViewModel.StatusMessage = "[Recovered] " + candidate.DisplayName;
                }
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

            // File type matches the source format
            switch (ActiveVM.FileMode)
            {
                case NotebookFileMode.PythonScript:
                    picker.SuggestedFileName = ActiveVM.Title;
                    picker.FileTypeChoices.Add("Python Script", [".py"]);
                    break;
                case NotebookFileMode.Markdown:
                    picker.SuggestedFileName = ActiveVM.Title;
                    picker.FileTypeChoices.Add("Markdown", [".md"]);
                    break;
                default:
                    picker.SuggestedFileName = ActiveVM.Title.Replace(".ipynb", "") + ".ipynb";
                    picker.FileTypeChoices.Add("Jupyter Notebook", [".ipynb"]);
                    break;
            }

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
    // Guards ContentDialog collisions: a second ShowAsync while one is open throws.
    private bool _dialogOpen;

    private async void OnSettings(object s, RoutedEventArgs e)
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try { await NotebookSettingsDialog.ShowAsync(XamlRoot); }
        finally { _dialogOpen = false; }
    }

    private async void ShowShortcutHelp()
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try { await ShortcutReferenceDialog.ShowAsync(XamlRoot); }
        finally { _dialogOpen = false; }
    }
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

    private NotebookPage? ActivePage => (TabViewControl.SelectedItem as TabViewItem)?.Content as NotebookPage;

    /// <summary>
    /// Command-mode keys must only fire while keyboard focus is inside the cell
    /// list. Inferring command mode from "focus is not a TextBox" made destructive
    /// shortcuts (D,D delete, M convert) fire right after clicking a toolbar
    /// button or while focus was orphaned.
    /// </summary>
    private bool IsFocusInCellArea()
    {
        if (XamlRoot is null) return false;
        if (FocusManager.GetFocusedElement(XamlRoot) is not DependencyObject focused) return false;
        if (focused is TextBox) return false; // edit mode

        for (DependencyObject? current = focused; current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is NotebookPage) return true;
        }
        return false;
    }

    // For double-key commands (DD, II, 00)
    private Windows.System.VirtualKey _lastCommandKey;
    private DateTime _lastCommandKeyTime;

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                       .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                        .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        // Command-mode shortcuts (only while focus is inside the cell list)
        if (IsFocusInCellArea() && !ctrl && !shift && ActiveVM is not null)
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
            case Windows.System.VirtualKey.Z when ctrl && !shift:
                ActiveVM?.UndoCommand.Execute(null);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Y when ctrl:
                ActiveVM?.RedoCommand.Execute(null);
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
                {
                    vm.SelectCell(vm.SelectedCellIndex - 1);
                    ActivePage?.BringCellIntoView(vm.SelectedCellIndex);
                }
                return true;

            case Windows.System.VirtualKey.Down or Windows.System.VirtualKey.J:
                if (vm.SelectedCellIndex < vm.Cells.Count - 1)
                {
                    vm.SelectCell(vm.SelectedCellIndex + 1);
                    ActivePage?.BringCellIntoView(vm.SelectedCellIndex);
                }
                return true;

            // Enter edit mode
            case Windows.System.VirtualKey.Enter:
                if (vm.SelectedCell is ViewModels.Notebook.MarkdownCellViewModel md)
                    md.EnterEditMode();
                // Focus the cell's editor once the view-mode switch has applied.
                var editIndex = vm.SelectedCellIndex;
                DispatcherQueue.TryEnqueue(() => ActivePage?.EnterCellEditMode(editIndex));
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

            // Toggle output collapse
            case Windows.System.VirtualKey.O:
                if (vm.SelectedCell is ViewModels.Notebook.CodeCellViewModel codeVm)
                    codeVm.IsOutputCollapsed = !codeVm.IsOutputCollapsed;
                return true;

            // Copy/paste cell
            case Windows.System.VirtualKey.C:
                vm.CopySelectedCellCommand.Execute(null);
                return true;

            case Windows.System.VirtualKey.V:
                vm.PasteCellBelowCommand.Execute(null);
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

            // Undo structural change (command mode)
            case Windows.System.VirtualKey.Z:
                vm.UndoCommand.Execute(null);
                return true;

            // Help (H key in command mode)
            case Windows.System.VirtualKey.H:
                ShowShortcutHelp();
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
