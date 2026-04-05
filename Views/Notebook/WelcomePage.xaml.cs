using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Services.Notebook;
using IPythonDiscoveryService = CanfarDesktop.Services.Notebook.IPythonDiscoveryService;

namespace CanfarDesktop.Views.Notebook;

public sealed partial class WelcomePage : UserControl
{
    public event Action? NewRequested;
    public event Action<string>? OpenFileRequested;
    public event Action? OpenPickerRequested;

    public WelcomePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshRecent();
        await CheckSystemRequirementsAsync();
    }

    private bool _systemChecked;

    private async Task CheckSystemRequirementsAsync()
    {
        // Only check once — PythonDiscovery caches, but the Process.Start
        // calls for package checks throw Win32Exception in the debugger
        if (_systemChecked) return;
        _systemChecked = true;

        StatusStack.Children.Clear();

        var pythonDiscovery = App.Services.GetRequiredService<IPythonDiscoveryService>();
        var pythonPath = await pythonDiscovery.FindPythonAsync();

        if (pythonPath is not null)
        {
            AddStatusRow("\uE73E", $"Python {pythonDiscovery.PythonVersion}", pythonPath,
                "SystemFillColorSuccessBrush");
        }
        else
        {
            AddStatusRow("\uE783", "Python not found", "Install Python 3.8+ from python.org",
                "SystemFillColorCriticalBrush");

            var installBtn = new HyperlinkButton
            {
                Content = "Download Python",
                NavigateUri = new System.Uri("https://www.python.org/downloads/"),
                Margin = new Thickness(24, 0, 0, 0),
            };
            StatusStack.Children.Add(installBtn);
        }

        // Check for common packages
        if (pythonPath is not null)
        {
            await CheckPackageAsync("numpy");
            await CheckPackageAsync("matplotlib");
        }
    }

    private async Task CheckPackageAsync(string package)
    {
        try
        {
            var discovery = App.Services.GetRequiredService<IPythonDiscoveryService>();
            var pythonExe = discovery.PythonPath;
            if (pythonExe is null) return; // no Python found — skip package check
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $"-c \"import {package}; print({package}.__version__)\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return;
            var output = (await proc.StandardOutput.ReadToEndAsync()).Trim();
            await proc.WaitForExitAsync();

            if (proc.ExitCode == 0 && !string.IsNullOrEmpty(output))
                AddStatusRow("\uE73E", $"{package} {output}", null, "SystemFillColorSuccessBrush");
            else
                AddStatusRow("\uE783", $"{package} not installed",
                    $"Run: %pip install {package}", "SystemFillColorCautionBrush");
        }
        catch
        {
            AddStatusRow("\uE783", $"{package} not installed",
                $"Run: %pip install {package}", "SystemFillColorCautionBrush");
        }
    }

    private void AddStatusRow(string glyph, string label, string? detail, string brushKey)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 14,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[brushKey],
        });
        var textStack = new StackPanel();
        textStack.Children.Add(new TextBlock
        {
            Text = label,
            Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
        });
        if (detail is not null)
        {
            textStack.Children.Add(new TextBlock
            {
                Text = detail,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            });
        }
        row.Children.Add(textStack);
        StatusStack.Children.Add(row);
    }

    public void RefreshRecent()
    {
        var recentService = App.Services.GetRequiredService<RecentNotebooksService>();
        var entries = recentService.Entries;

        RecentList.Children.Clear();

        if (entries.Count == 0)
        {
            RecentSection.Visibility = Visibility.Collapsed;
            return;
        }

        RecentSection.Visibility = Visibility.Visible;

        foreach (var entry in entries)
        {
            var btn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 8, 12, 8),
                Tag = entry.Path,
            };

            var panel = new StackPanel { Spacing = 2 };
            panel.Children.Add(new TextBlock
            {
                Text = entry.Name,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            });
            panel.Children.Add(new TextBlock
            {
                Text = entry.Path,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            btn.Content = panel;
            btn.Click += (s, _) =>
            {
                if (s is Button { Tag: string path })
                    OpenFileRequested?.Invoke(path);
            };

            RecentList.Children.Add(btn);
        }
    }

    private void OnNewNotebook(object s, RoutedEventArgs e) => NewRequested?.Invoke();
    private void OnOpenNotebook(object s, RoutedEventArgs e) => OpenPickerRequested?.Invoke();
}
