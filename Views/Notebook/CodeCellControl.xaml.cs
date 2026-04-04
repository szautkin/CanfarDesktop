using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CanfarDesktop.Helpers.Notebook;
using CanfarDesktop.ViewModels.Notebook;

namespace CanfarDesktop.Views.Notebook;

public sealed partial class CodeCellControl : UserControl
{
    private CodeCellViewModel? _viewModel;
    private bool _suppressTextChanged;

    public CodeCellControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Outputs.CollectionChanged -= OnOutputsChanged;
        }

        _viewModel = args.NewValue as CodeCellViewModel;
        if (_viewModel is null) return;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Outputs.CollectionChanged += OnOutputsChanged;

        _suppressTextChanged = true;
        SourceEditor.Text = _viewModel.Source ?? "";
        _suppressTextChanged = false;

        ExecutionLabel.Text = _viewModel.ExecutionStatus;
        UpdateAccentBorder();
        RenderOutputs();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(CodeCellViewModel.ExecutionStatus):
                    ExecutionLabel.Text = _viewModel?.ExecutionStatus ?? "[ ]";
                    break;
                case nameof(CodeCellViewModel.IsSelected):
                case nameof(CodeCellViewModel.IsEditing):
                    UpdateAccentBorder();
                    break;
            }
        });
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

    private void OnEditorGotFocus(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        _viewModel.IsEditing = true;
        _viewModel.IsFocused = true;
    }

    private void OnEditorLostFocus(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        _viewModel.IsEditing = false;
        _viewModel.IsFocused = false;
    }

    private void OnSourceTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChanged || _viewModel is null) return;
        _viewModel.Source = SourceEditor.Text;
    }

    private void OnOutputsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(RenderOutputs);
    }

    private void RenderOutputs()
    {
        OutputStack.Children.Clear();

        if (_viewModel is null || _viewModel.Outputs.Count == 0)
        {
            OutputArea.Visibility = Visibility.Collapsed;
            return;
        }

        OutputArea.Visibility = Visibility.Visible;

        foreach (var output in _viewModel.Outputs)
        {
            if (output.IsError)
            {
                var tb = new TextBlock
                {
                    Text = output.Traceback,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
                };
                OutputStack.Children.Add(tb);
            }
            else if (!string.IsNullOrEmpty(output.TextContent))
            {
                var tb = new TextBlock
                {
                    Text = output.TextContent,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
                };
                OutputStack.Children.Add(tb);
            }
        }
    }
}
