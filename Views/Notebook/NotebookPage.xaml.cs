using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CanfarDesktop.Services.Notebook;
using CanfarDesktop.ViewModels.Notebook;

namespace CanfarDesktop.Views.Notebook;

/// <summary>
/// Displays a single notebook's cells + status bar. No toolbar — that's in NotebookTabHost.
/// </summary>
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
                case nameof(ViewModel.StatusMessage):
                    StatusLabel.Text = ViewModel.StatusMessage;
                    break;
                case nameof(ViewModel.KernelState):
                    UpdateKernelIndicator(ViewModel.KernelState);
                    break;
            }
        });

        ViewModel.Cells.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            CellCountLabel.Text = Helpers.Loc.F("Nb_CellCount", ViewModel.Cells.Count);
            if (!_initialCellFocusDone && ViewModel.Cells.Count > 0)
            {
                _initialCellFocusDone = true;
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, FocusSelectedCellIfIdle);
            }
        });

        // Command-mode keys only fire while focus is inside the cell list — give focus a home when
        // the page (re)attaches so keyboard-only users aren't dead until they click a cell.
        Loaded += (_, _) => DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, FocusSelectedCellIfIdle);

        // Wire cell selection (track handlers for proper unsubscribe)
        WireCellSelection(ViewModel.Cells);
        ViewModel.Cells.CollectionChanged += (_, args) =>
        {
            if (args.NewItems is not null)
                foreach (CellViewModel cell in args.NewItems)
                    WireSingleCellSelection(cell);
            if (args.OldItems is not null)
                foreach (CellViewModel cell in args.OldItems)
                    UnwireSingleCellSelection(cell);
        };

        // Initial state
        KernelLabel.Text = KernelStateText(ViewModel.KernelState);
        StatusLabel.Text = ViewModel.StatusMessage;
        CellCountLabel.Text = Helpers.Loc.F("Nb_CellCount", ViewModel.Cells.Count);
        UpdateKernelIndicator(ViewModel.KernelState);
    }

    private bool _initialCellFocusDone;

    /// <summary>Focus the selected cell unless the user is already typing in an editor.</summary>
    private void FocusSelectedCellIfIdle()
    {
        if (ViewModel.Cells.Count == 0) return;
        if (XamlRoot is not null
            && Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) is TextBox)
            return;

        var index = Math.Clamp(ViewModel.SelectedCellIndex, 0, ViewModel.Cells.Count - 1);
        if (CellList.GetOrCreateElement(index) is Control cell)
            cell.Focus(FocusState.Programmatic);
    }

    /// <summary>Scroll the cell at the given index into view (command-mode navigation).</summary>
    public void BringCellIntoView(int index)
    {
        if (index < 0 || index >= ViewModel.Cells.Count) return;
        var element = CellList.GetOrCreateElement(index);
        element?.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.5 });
    }

    /// <summary>Focus the source editor of the cell at the given index (Enter in command mode).</summary>
    public void EnterCellEditMode(int index)
    {
        if (index < 0 || index >= ViewModel.Cells.Count) return;
        switch (CellList.TryGetElement(index))
        {
            case CodeCellControl code:
                code.EnterEditMode();
                break;
            case MarkdownCellControl md:
                md.FocusEditor();
                break;
        }
    }

    /// <summary>Localized display text for a kernel state (the enum NAMES stay English — comparisons
    /// and MCP payloads use <c>ToString()</c> elsewhere; only this status-bar label is translated).</summary>
    private static string KernelStateText(KernelState state) => state switch
    {
        KernelState.Dead => Helpers.Loc.T("Nb_KernelDead"),
        KernelState.Starting => Helpers.Loc.T("Nb_KernelStarting"),
        KernelState.Idle => Helpers.Loc.T("Nb_KernelIdle"),
        KernelState.Busy => Helpers.Loc.T("Nb_KernelBusy"),
        KernelState.Error => Helpers.Loc.T("Nb_KernelError"),
        _ => state.ToString(),
    };

    private void UpdateKernelIndicator(KernelState state)
    {
        KernelLabel.Text = KernelStateText(state);
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
            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (ext is ".py" or ".md")
            {
                // Plain text file → single-cell notebook
                var content = await File.ReadAllTextAsync(filePath);
                var mode = ext == ".py"
                    ? ViewModels.Notebook.NotebookFileMode.PythonScript
                    : ViewModels.Notebook.NotebookFileMode.Markdown;
                ViewModel.LoadFromTextFile(filePath, content, mode);
            }
            else
            {
                // .ipynb notebook
                await using var stream = File.OpenRead(filePath);
                var document = await Helpers.Notebook.NotebookParser.ParseAsync(stream);
                ViewModel.LoadFromFile(filePath, document);
                await CheckDependenciesAsync(document);
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = Helpers.Loc.F("Nb_OpenFailed", ex.Message);
        }
    }

    private async Task CheckDependenciesAsync(Models.Notebook.NotebookDocument document)
    {
        var discovery = App.Services.GetRequiredService<IPythonDiscoveryService>();
        var pythonPath = discovery.PythonPath;
        if (pythonPath is null) return;

        ViewModel.StatusMessage = Helpers.Loc.T("Nb_CheckingDeps");

        var imports = Helpers.Notebook.DependencyScanner.ExtractImports(document);
        if (imports.Count == 0) return;

        var missing = await Helpers.Notebook.DependencyScanner.FindMissingAsync(imports, pythonPath);
        if (missing.Count == 0)
        {
            ViewModel.StatusMessage = Helpers.Loc.T("Nb_DepsAllInstalled");
            return;
        }

        // Wait for XamlRoot
        if (XamlRoot is null) return;

        var packageList = string.Join("\n", missing.Select(m => $"  - {m.pipName}"));
        var dialog = new ContentDialog
        {
            Title = Helpers.Loc.F("Nb_MissingPkgTitle", missing.Count),
            Content = Helpers.Loc.F("Nb_MissingPkgBody", packageList),
            PrimaryButtonText = Helpers.Loc.T("Nb_InstallAllButton"),
            CloseButtonText = Helpers.Loc.T("Nb_SkipButton"),
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.StatusMessage = Helpers.Loc.F("Nb_InstallingPkgs", missing.Count);
            var pipNames = missing.Select(m => m.pipName);
            var result = await Helpers.Notebook.DependencyScanner.InstallAsync(pipNames, pythonPath);
            var errors = result.errors;

            ViewModel.StatusMessage = errors.Contains("ERROR")
                ? Helpers.Loc.T("Nb_PkgInstallFailed")
                : Helpers.Loc.F("Nb_PkgInstalled", missing.Count);
        }
        else
        {
            ViewModel.StatusMessage = Helpers.Loc.T("Nb_ReadyPkgMissing");
        }
    }

    private readonly Dictionary<CellViewModel, Action> _selectionHandlers = new();

    private void WireCellSelection(IEnumerable<CellViewModel> cells)
    {
        foreach (var cell in cells)
            WireSingleCellSelection(cell);
    }

    private void WireSingleCellSelection(CellViewModel cell)
    {
        if (_selectionHandlers.ContainsKey(cell)) return;
        Action handler = () => ViewModel.SelectCell(ViewModel.Cells.IndexOf(cell));
        _selectionHandlers[cell] = handler;
        cell.SelectionRequested += handler;
    }

    private void UnwireSingleCellSelection(CellViewModel cell)
    {
        if (_selectionHandlers.Remove(cell, out var handler))
            cell.SelectionRequested -= handler;
    }
}
