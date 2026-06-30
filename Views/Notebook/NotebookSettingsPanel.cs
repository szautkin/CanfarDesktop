using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Services.Notebook;

namespace CanfarDesktop.Views.Notebook;

/// <summary>
/// Notebook settings as a self-contained, code-built panel (editor / saving / execution / interface /
/// diagnostics). Resolves the <see cref="NotebookSettings"/> singleton from DI and auto-saves on every
/// change — the editors subscribe to <c>NotebookSettings.Changed</c>, so edits apply live. Embeds in the
/// standalone <see cref="NotebookSettingsDialog"/> and the unified Settings window.
/// </summary>
public sealed class NotebookSettingsPanel : UserControl
{
    private readonly NotebookSettings _settings = App.Services.GetRequiredService<NotebookSettings>();

    private readonly ComboBox _fontSizeBox;
    private readonly ComboBox _tabSizeBox;
    private readonly ComboBox _autosaveIntervalBox;
    private readonly ComboBox _timeoutBox;
    private readonly ToggleSwitch _wordWrapToggle;
    private readonly ToggleSwitch _autosaveToggle;
    private readonly ToggleSwitch _toolbarToggle;
    private readonly TextBox _pythonPathBox;
    private bool _loading;

    public NotebookSettingsPanel()
    {
        _loading = true;

        _fontSizeBox = Combo("Font size", new object[] { 11, 12, 13, 14, 15, 16, 18, 20 }, 120);
        _fontSizeBox.SelectedItem = _settings.FontSize;
        _tabSizeBox = Combo("Tab size (spaces)", new object[] { 2, 4, 8 }, 120);
        _tabSizeBox.SelectedItem = _settings.TabSize;

        _wordWrapToggle = new ToggleSwitch { Header = "Word wrap", IsOn = _settings.WordWrap };

        _autosaveToggle = new ToggleSwitch { Header = "Autosave enabled", IsOn = _settings.AutosaveEnabled };
        _autosaveIntervalBox = Combo("Autosave interval", new object[] { "15 seconds", "30 seconds", "60 seconds", "120 seconds" }, 160);
        _autosaveIntervalBox.SelectedIndex = _settings.AutosaveIntervalSeconds switch { 15 => 0, 30 => 1, 60 => 2, 120 => 3, _ => 1 };

        _timeoutBox = Combo("Execution timeout warning", new object[] { "30 seconds", "60 seconds", "120 seconds", "300 seconds", "Never" }, 160);
        _timeoutBox.SelectedIndex = _settings.ExecutionTimeoutSeconds switch { 30 => 0, 60 => 1, 120 => 2, 300 => 3, 0 => 4, _ => 1 };
        _pythonPathBox = new TextBox
        {
            Header = "Python path (leave empty for auto-detect)",
            Text = _settings.PythonPath ?? string.Empty,
            PlaceholderText = "auto-detect",
            MinWidth = 300,
        };

        _toolbarToggle = new ToggleSwitch { Header = "Show toolbar", IsOn = _settings.ShowToolbar };

        var logButton = new HyperlinkButton { Content = "Open log folder" };
        logButton.Click += (_, _) => NotebookLogger.OpenLogFolder();

        var editorRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        editorRow.Children.Add(_fontSizeBox);
        editorRow.Children.Add(_tabSizeBox);

        var panel = new StackPanel { Spacing = 16, MinWidth = 360 };
        panel.Children.Add(Header("Editor", first: true));
        panel.Children.Add(editorRow);
        panel.Children.Add(_wordWrapToggle);
        panel.Children.Add(Header("Saving"));
        panel.Children.Add(_autosaveToggle);
        panel.Children.Add(_autosaveIntervalBox);
        panel.Children.Add(Header("Execution"));
        panel.Children.Add(_timeoutBox);
        panel.Children.Add(_pythonPathBox);
        panel.Children.Add(Header("Interface"));
        panel.Children.Add(_toolbarToggle);
        panel.Children.Add(Header("Diagnostics"));
        panel.Children.Add(logButton);

        Content = panel;
        _loading = false;

        // Auto-save on change (matches the Settings window's General tab).
        _fontSizeBox.SelectionChanged += OnChanged;
        _tabSizeBox.SelectionChanged += OnChanged;
        _autosaveIntervalBox.SelectionChanged += OnChanged;
        _timeoutBox.SelectionChanged += OnChanged;
        _wordWrapToggle.Toggled += OnChanged;
        _autosaveToggle.Toggled += OnChanged;
        _toolbarToggle.Toggled += OnChanged;
        _pythonPathBox.LostFocus += OnChanged;
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Persist();

    private void Persist()
    {
        if (_loading) return;
        _settings.FontSize = _fontSizeBox.SelectedItem is int fs ? fs : 13;
        _settings.TabSize = _tabSizeBox.SelectedItem is int ts ? ts : 4;
        _settings.WordWrap = _wordWrapToggle.IsOn;
        _settings.AutosaveEnabled = _autosaveToggle.IsOn;
        _settings.AutosaveIntervalSeconds = _autosaveIntervalBox.SelectedIndex switch { 0 => 15, 1 => 30, 2 => 60, 3 => 120, _ => 30 };
        _settings.ExecutionTimeoutSeconds = _timeoutBox.SelectedIndex switch { 0 => 30, 1 => 60, 2 => 120, 3 => 300, 4 => 0, _ => 60 };
        _settings.PythonPath = string.IsNullOrWhiteSpace(_pythonPathBox.Text) ? null : _pythonPathBox.Text;
        _settings.ShowToolbar = _toolbarToggle.IsOn;
        _settings.Save();
    }

    private static ComboBox Combo(string header, object[] items, double minWidth)
    {
        var combo = new ComboBox { Header = header, MinWidth = minWidth };
        foreach (var item in items) combo.Items.Add(item);
        return combo;
    }

    private static TextBlock Header(string text, bool first = false) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        Margin = first ? new Thickness(0) : new Thickness(0, 8, 0, 0),
    };
}
