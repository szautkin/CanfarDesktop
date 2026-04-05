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
            CellCountLabel.Text = $"{ViewModel.Cells.Count} cells";
        });

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
        KernelLabel.Text = ViewModel.KernelState.ToString();
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

            // Scan for missing dependencies
            await CheckDependenciesAsync(document);
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Open failed: {ex.Message}";
        }
    }

    private async Task CheckDependenciesAsync(Models.Notebook.NotebookDocument document)
    {
        var discovery = App.Services.GetRequiredService<IPythonDiscoveryService>();
        var pythonPath = discovery.PythonPath;
        if (pythonPath is null) return;

        ViewModel.StatusMessage = "Checking dependencies...";

        var imports = Helpers.Notebook.DependencyScanner.ExtractImports(document);
        if (imports.Count == 0) return;

        var missing = await Helpers.Notebook.DependencyScanner.FindMissingAsync(imports, pythonPath);
        if (missing.Count == 0)
        {
            ViewModel.StatusMessage = "All dependencies installed";
            return;
        }

        // Wait for XamlRoot
        if (XamlRoot is null) return;

        var packageList = string.Join("\n", missing.Select(m => $"  - {m.pipName}"));
        var dialog = new ContentDialog
        {
            Title = $"{missing.Count} missing package(s)",
            Content = $"This notebook needs:\n{packageList}\n\nInstall them now?",
            PrimaryButtonText = "Install All",
            CloseButtonText = "Skip",
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.StatusMessage = $"Installing {missing.Count} packages...";
            var pipNames = missing.Select(m => m.pipName);
            var result = await Helpers.Notebook.DependencyScanner.InstallAsync(pipNames, pythonPath);
            var errors = result.errors;

            ViewModel.StatusMessage = errors.Contains("ERROR")
                ? "Some packages failed to install"
                : $"Installed {missing.Count} packages";
        }
        else
        {
            ViewModel.StatusMessage = "Ready (some packages may be missing)";
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
