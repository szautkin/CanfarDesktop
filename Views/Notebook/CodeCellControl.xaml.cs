using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
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
        Unloaded += (_, _) => DetachViewModel();
    }

    private void DetachViewModel()
    {
        if (_viewModel is null) return;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Outputs.CollectionChanged -= OnOutputsChanged;
        _viewModel = null;
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
        RenderSyntax();
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

    private void OnCellPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _viewModel?.RequestSelection();
    }

    private void OnSyntaxViewTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        // Enter edit mode — show TextBox, hide syntax view
        if (_viewModel is null) return;
        SyntaxView.Visibility = Visibility.Collapsed;
        SourceEditor.Visibility = Visibility.Visible;

        _suppressTextChanged = true;
        SourceEditor.Text = _viewModel.Source ?? "";
        _suppressTextChanged = false;

        SourceEditor.Focus(FocusState.Programmatic);
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
        _viewModel.IsEditing = false;
        _viewModel.IsFocused = false;

        // Return to syntax-highlighted view
        RenderSyntax();
        SyntaxView.Visibility = Visibility.Visible;
        SourceEditor.Visibility = Visibility.Collapsed;
    }

    private string? _lastHighlightedSource;

    private void RenderSyntax()
    {
        SyntaxView.Blocks.Clear();
        var source = _viewModel?.Source ?? "";

        // Skip re-highlight if source unchanged
        if (source == _lastHighlightedSource && SyntaxView.Blocks.Count > 0) return;
        _lastHighlightedSource = source;

        if (string.IsNullOrEmpty(source))
        {
            var p = new Microsoft.UI.Xaml.Documents.Paragraph();
            p.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = "Type Python code here...",
                Foreground = ThemeHelper.GetBrush("TextFillColorTertiaryBrush"),
            });
            SyntaxView.Blocks.Add(p);
            return;
        }

        var isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
        var spans = PythonSyntaxHighlighter.Highlight(source);
        var paragraph = new Microsoft.UI.Xaml.Documents.Paragraph();
        foreach (var span in spans)
        {
            var run = new Microsoft.UI.Xaml.Documents.Run { Text = span.Text };
            if (span.Type != TokenType.Plain)
                run.Foreground = new SolidColorBrush(PythonSyntaxHighlighter.GetColor(span.Type, isDark));
            paragraph.Inlines.Add(run);
        }
        SyntaxView.Blocks.Add(paragraph);
    }

    /// <summary>
    /// Raised when Up/Down at cell boundary should navigate to adjacent cell.
    /// Int parameter: -1 for previous, +1 for next.
    /// </summary>
    public static event Action<int>? NavigateRequested;

    private void OnEditorKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Escape:
                ExitEditMode();
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Up:
                if (IsAtFirstLine())
                {
                    ExitEditMode();
                    NavigateRequested?.Invoke(-1);
                    e.Handled = true;
                }
                break;

            case Windows.System.VirtualKey.Down:
                if (IsAtLastLine())
                {
                    ExitEditMode();
                    NavigateRequested?.Invoke(+1);
                    e.Handled = true;
                }
                break;
        }
    }

    private void ExitEditMode()
    {
        if (_viewModel is not null)
        {
            _viewModel.IsEditing = false;
            _viewModel.IsFocused = false;
        }
        RenderSyntax();
        SyntaxView.Visibility = Visibility.Visible;
        SourceEditor.Visibility = Visibility.Collapsed;
        this.Focus(FocusState.Programmatic);
    }

    private bool IsAtFirstLine()
    {
        var text = SourceEditor.Text;
        var pos = SourceEditor.SelectionStart;
        // At first line if no newline before cursor position
        return pos == 0 || !text[..pos].Contains('\n');
    }

    private bool IsAtLastLine()
    {
        var text = SourceEditor.Text;
        var pos = SourceEditor.SelectionStart;
        // At last line if no newline after cursor position
        return pos >= text.Length || !text[pos..].Contains('\n');
    }

    private void OnSourceTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChanged || _viewModel is null) return;
        _viewModel.Source = SourceEditor.Text;
    }

    private void OnToggleOutputCollapse(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        _viewModel.IsOutputCollapsed = !_viewModel.IsOutputCollapsed;
        OutputStack.Visibility = _viewModel.IsOutputCollapsed ? Visibility.Collapsed : Visibility.Visible;
        CollapseIcon.Glyph = _viewModel.IsOutputCollapsed ? "\uE76C" : "\uE76B"; // ChevronRight vs ChevronDown
        CollapseLabel.Text = _viewModel.IsOutputCollapsed
            ? $"Output collapsed ({_viewModel.Outputs.Count} items)"
            : "Output";
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
                OutputStack.Children.Add(BuildErrorOutput(output));
            else if (output.HasImage)
                OutputStack.Children.Add(BuildImageOutput(output));
            else if (output.HasHtml)
                BuildHtmlOutput(output);
            else if (!string.IsNullOrEmpty(output.TextContent))
                OutputStack.Children.Add(BuildTextOutput(output));
        }
    }

    private static UIElement BuildTextOutput(CellOutputViewModel output)
    {
        var isStderr = output.OutputType == "stream" && output.Model.Name == "stderr";
        return new TextBlock
        {
            Text = output.TextContent,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Foreground = isStderr
                ? ThemeHelper.GetBrush("SystemFillColorCautionBrush")
                : ThemeHelper.GetBrush("TextFillColorPrimaryBrush"),
        };
    }

    private static UIElement BuildErrorOutput(CellOutputViewModel output)
    {
        var panel = new StackPanel { Spacing = 4 };

        // Error name (bold, red)
        panel.Children.Add(new TextBlock
        {
            Text = output.ErrorName,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeHelper.GetBrush("SystemFillColorCriticalBrush"),
        });

        // Traceback with ANSI colors
        if (!string.IsNullOrEmpty(output.Traceback))
        {
            var rtb = new RichTextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            };

            var paragraph = new Paragraph();
            var spans = AnsiParser.Parse(output.Traceback);
            foreach (var span in spans)
            {
                var run = new Run { Text = span.Text };
                if (span.Foreground is not null)
                    run.Foreground = new SolidColorBrush(span.Foreground.Value);
                if (span.IsBold)
                    run.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                paragraph.Inlines.Add(run);
            }
            rtb.Blocks.Add(paragraph);
            panel.Children.Add(rtb);
        }

        return new Border
        {
            Child = panel,
            Background = ThemeHelper.GetBrush("SystemFillColorCriticalBackgroundBrush"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
        };
    }

    private void BuildHtmlOutput(CellOutputViewModel output)
    {
        var elements = SimpleHtmlRenderer.Render(output.HtmlContent);
        foreach (var element in elements)
            OutputStack.Children.Add(element);
    }

    private UIElement BuildImageOutput(CellOutputViewModel output)
    {
        var image = new Image
        {
            MaxWidth = 800,
            MaxHeight = 600,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        // Async load the base64 image
        _ = LoadImageAsync(image, output.ImageBase64);

        var panel = new StackPanel();
        panel.Children.Add(image);

        // Fallback text/plain below image
        if (!string.IsNullOrEmpty(output.TextContent))
        {
            panel.Children.Add(new TextBlock
            {
                Text = output.TextContent,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = ThemeHelper.GetBrush("TextFillColorTertiaryBrush"),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }

        return panel;
    }

    private static async Task LoadImageAsync(Image imageControl, string base64)
    {
        var bitmap = await Base64ImageHelper.DecodeAsync(base64);
        if (bitmap is not null)
            imageControl.Source = bitmap;
    }
}
