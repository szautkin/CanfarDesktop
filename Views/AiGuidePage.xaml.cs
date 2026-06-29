using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using CanfarDesktop.Services.AiGuide;
using CanfarDesktop.ViewModels;
using CanfarDesktop.Views.Dialogs;

namespace CanfarDesktop.Views;

/// <summary>
/// The AI Guide dashboard: a launchpad of category tiles (default) that open a centered focus panel,
/// a "See everything" full grid, a flat search view, and the user's read-only guide tools. Edits flow
/// straight to <see cref="AiGuideService"/>, which the MCP server reads live on the next tools/list.
/// </summary>
public sealed partial class AiGuidePage : Page
{
    public AiGuideViewModel ViewModel { get; }
    private readonly AiGuideService _service;

    public AiGuidePage(AiGuideViewModel viewModel, AiGuideService service)
    {
        ViewModel = viewModel;
        _service = service;
        InitializeComponent();
    }

    /// <summary>(Re)load the tool surface — called by the host when the page is shown.</summary>
    public void LoadAsync() => ViewModel.Load();

    // ── Launchpad / focus panel ──────────────────────────────────────────────
    private void OnCategoryTileClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AiGuideCategoryGroup group)
            ViewModel.OpenCategory(group);
    }

    private void OnCloseFocusClick(object sender, RoutedEventArgs e) => ViewModel.CloseFocus();

    private void OnBackdropTapped(object sender, TappedRoutedEventArgs e) => ViewModel.CloseFocus();

    private void OnClearSearchClick(object sender, RoutedEventArgs e) => ViewModel.ClearSearch();

    private void OnTilesChecked(object sender, RoutedEventArgs e) => ViewModel.ShowTiles = true;

    private void OnEverythingChecked(object sender, RoutedEventArgs e) => ViewModel.ShowEverything = true;

    // ── Description overrides ────────────────────────────────────────────────
    private void OnSaveOverrideClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AiGuideToolRowViewModel row)
            ViewModel.SaveOverride(row);
    }

    private void OnResetOverrideClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AiGuideToolRowViewModel row)
            ViewModel.ResetOverride(row);
    }

    private void OnCancelEditClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AiGuideToolRowViewModel row)
            row.IsExpanded = false;
    }

    // ── My guide tools ───────────────────────────────────────────────────────
    private async void OnAddGuideClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AiGuideEditDialog(_service) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.LoadGuides();
    }

    private async void OnEditGuideClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AiGuideGuideRowViewModel row) return;
        var entry = new AiGuideToolEntry(row.Id, row.Name, row.Description, row.Body);
        var dialog = new AiGuideEditDialog(_service, entry) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.LoadGuides();
    }

    private async void OnDeleteGuideClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AiGuideGuideRowViewModel row) return;
        var confirm = new ContentDialog
        {
            Title = "Delete guide tool",
            Content = $"Delete \"{row.Name}\"? The agent will no longer see it.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.DeleteGuide(row.Id);
    }
}
