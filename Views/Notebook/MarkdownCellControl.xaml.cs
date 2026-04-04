using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using CanfarDesktop.Helpers.Notebook;
using CanfarDesktop.ViewModels.Notebook;

namespace CanfarDesktop.Views.Notebook;

public sealed partial class MarkdownCellControl : UserControl
{
    private MarkdownCellViewModel? _viewModel;
    private bool _suppressTextChanged;

    public MarkdownCellControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = args.NewValue as MarkdownCellViewModel;
        if (_viewModel is null) return;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _suppressTextChanged = true;
        SourceEditor.Text = _viewModel.Source ?? "";
        _suppressTextChanged = false;

        UpdateViewMode();
        UpdateAccentBorder();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(MarkdownCellViewModel.IsRendered):
                    UpdateViewMode();
                    break;
                case nameof(MarkdownCellViewModel.IsSelected):
                case nameof(MarkdownCellViewModel.IsEditing):
                    UpdateAccentBorder();
                    break;
            }
        });
    }

    private void UpdateViewMode()
    {
        if (_viewModel is null) return;

        if (_viewModel.IsRendered)
        {
            RenderedView.Visibility = Visibility.Visible;
            SourceEditor.Visibility = Visibility.Collapsed;
            // Render markdown as plain text for now (M5 will add rich rendering)
            var isEmpty = string.IsNullOrWhiteSpace(_viewModel.Source);
            RenderedView.Text = isEmpty ? "Double-click to edit" : _viewModel.Source;
            RenderedView.Foreground = (Brush)Application.Current.Resources[
                isEmpty ? "TextFillColorTertiaryBrush" : "TextFillColorPrimaryBrush"];
        }
        else
        {
            RenderedView.Visibility = Visibility.Collapsed;
            SourceEditor.Visibility = Visibility.Visible;
            _suppressTextChanged = true;
            SourceEditor.Text = _viewModel.Source ?? "";
            _suppressTextChanged = false;
            SourceEditor.Focus(FocusState.Programmatic);
        }
    }

    private void UpdateAccentBorder()
    {
        if (_viewModel is null) return;

        if (_viewModel.IsEditing)
            AccentBorder.Background = ThemeHelper.SuccessBrush;
        else if (_viewModel.IsSelected)
            AccentBorder.Background = ThemeHelper.AccentBrush;
        else
            AccentBorder.Background = ThemeHelper.Transparent;
    }

    private void OnRenderedTapped(object sender, TappedRoutedEventArgs e)
    {
        _viewModel?.RequestSelection();
    }

    private void OnRenderedDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        _viewModel?.EnterEditMode();
    }

    private void OnEditorGotFocus(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        _viewModel.IsEditing = true;
        _viewModel.IsFocused = true;
        _viewModel.RequestSelection();
    }

    private void OnEditorLostFocus(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        _viewModel.ExitEditMode();
        _viewModel.IsFocused = false;
    }

    private void OnSourceTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChanged || _viewModel is null) return;
        _viewModel.Source = SourceEditor.Text;
    }

    private void OnEditorKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Escape exits edit mode
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            _viewModel?.ExitEditMode();
            e.Handled = true;
        }
    }
}
