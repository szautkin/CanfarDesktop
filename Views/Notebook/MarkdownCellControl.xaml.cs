using Microsoft.Extensions.DependencyInjection;
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
        Unloaded += (_, _) => DetachViewModel();

        var settings = App.Services.GetRequiredService<Services.Notebook.NotebookSettings>();
        settings.Changed += () => DispatcherQueue?.TryEnqueue(() =>
        {
            SourceEditor.FontSize = settings.FontSize;
        });
        SourceEditor.FontSize = settings.FontSize;
    }

    private void DetachViewModel()
    {
        if (_viewModel is null) return;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = null;
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
            RenderMarkdown();
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

    private void RenderMarkdown()
    {
        RenderedView.Children.Clear();

        if (string.IsNullOrWhiteSpace(_viewModel?.Source))
        {
            RenderedView.Children.Add(new TextBlock
            {
                Text = "Double-click to edit",
                Foreground = Helpers.Notebook.ThemeHelper.GetBrush("TextFillColorTertiaryBrush"),
                FontStyle = Windows.UI.Text.FontStyle.Italic,
            });
            return;
        }

        var elements = Helpers.Notebook.MarkdownRenderer.Render(_viewModel.Source);
        foreach (var element in elements)
            RenderedView.Children.Add(element);
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

    private void OnCellPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _viewModel?.RequestSelection();
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
