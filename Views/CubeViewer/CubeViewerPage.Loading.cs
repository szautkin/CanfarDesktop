using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CanfarDesktop.Services.CubeViewer;

namespace CanfarDesktop.Views.CubeViewer;

/// <summary>
/// The bottom-center cube-load progress indicator: a stepped list (header → decode → normalize →
/// upload) with done/active/pending glyphs plus a determinate <c>ProgressBar</c>, driven by the
/// real decode progress reported from <see cref="FitsCubeReader"/>.
/// </summary>
public sealed partial class CubeViewerPage
{
    private static readonly string[] LoadStepNames =
        { "Reading header", "Decoding planes", "Normalizing", "Uploading to GPU" };

    private (TextBlock Glyph, TextBlock Name, TextBlock Detail)[]? _loadSteps;

    private void BuildLoadingSteps()
    {
        if (_loadSteps is not null) return;
        _loadSteps = new (TextBlock, TextBlock, TextBlock)[LoadStepNames.Length];
        for (int i = 0; i < LoadStepNames.Length; i++)
        {
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var glyph = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock { Text = LoadStepNames[i], FontSize = 13, Foreground = ArgbBrush(0xFF, 0xE0, 0xE0, 0xE0) };
            var detail = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ArgbBrush(0xA0, 0xBF, 0xD8, 0xFF),
            };
            Grid.SetColumn(glyph, 0);
            Grid.SetColumn(name, 1);
            Grid.SetColumn(detail, 2);
            grid.Children.Add(glyph);
            grid.Children.Add(name);
            grid.Children.Add(detail);

            _loadSteps[i] = (glyph, name, detail);
            LoadingSteps.Children.Add(grid);
        }
    }

    private void ShowLoading(string fileName)
    {
        BuildLoadingSteps();
        HideStatus(); // clear any prior error/status snack while a fresh load runs
        _statusTimer?.Stop();
        LoadingTitle.Text = "Loading " + fileName;
        LoadingBar.Value = 0;
        UpdateLoadSteps(0, "");
        LoadingPanel.Visibility = Visibility.Visible;
    }

    private void UpdateLoadingUI(CubeLoadProgress p)
    {
        if (_closed) return;
        LoadingBar.Value = Math.Clamp(p.Fraction, 0, 1);
        UpdateLoadSteps(p.Step, p.Detail);
    }

    private void UpdateLoadSteps(int active, string detail)
    {
        if (_loadSteps is null) return;
        var done = new SolidColorBrush(ArgbColor(0xFF, 0x6C, 0xD9, 0x8A));   // green
        var cur = new SolidColorBrush(ArgbColor(0xFF, 0x56, 0xC8, 0xFF));    // cyan
        var pend = new SolidColorBrush(ArgbColor(0xFF, 0x6B, 0x7B, 0x8C));   // dim
        for (int i = 0; i < _loadSteps.Length; i++)
        {
            var (glyph, name, det) = _loadSteps[i];
            if (i < active)
            {
                glyph.Text = "✓"; glyph.Foreground = done; det.Text = ""; name.Opacity = 0.65;
            }
            else if (i == active)
            {
                glyph.Text = "●"; glyph.Foreground = cur; det.Text = detail; name.Opacity = 1.0;
            }
            else
            {
                glyph.Text = "○"; glyph.Foreground = pend; det.Text = ""; name.Opacity = 0.5;
            }
        }
    }

    private void HideLoading() => LoadingPanel.Visibility = Visibility.Collapsed;

    // ── Transient status / error snackbar (bottom-center) ───────────────────────

    private DispatcherTimer? _statusTimer;

    /// <summary>
    /// Surface a transient message to the user as a clear bottom-center snackbar (errors in red, info
    /// in cyan, auto-dismissing). The single entry point for load failures, export results, and the like.
    /// </summary>
    private void ShowStatus(string message, bool isError = false)
    {
        if (_closed || StatusSnack is null) return;

        StatusSnackText.Text = message;
        var accent = isError ? ArgbColor(0xFF, 0xFF, 0x6B, 0x6B) : ArgbColor(0xFF, 0x56, 0xC8, 0xFF);
        StatusSnackIcon.Glyph = isError ? "" : ""; // Warning : Info (Segoe MDL2)
        StatusSnackIcon.Foreground = new SolidColorBrush(accent);
        StatusSnack.BorderBrush = new SolidColorBrush(ArgbColor(0x66, accent.R, accent.G, accent.B));
        StatusSnack.Visibility = Visibility.Visible;

        if (_statusTimer is null)
        {
            _statusTimer = new DispatcherTimer();
            _statusTimer.Tick += (_, _) => { _statusTimer!.Stop(); HideStatus(); };
        }
        _statusTimer.Stop();
        _statusTimer.Interval = TimeSpan.FromSeconds(isError ? 7 : 3.5); // errors linger a little longer
        _statusTimer.Start();
    }

    private void HideStatus()
    {
        if (StatusSnack is not null) StatusSnack.Visibility = Visibility.Collapsed;
    }

    private void OnStatusSnackClose(object sender, RoutedEventArgs e)
    {
        _statusTimer?.Stop();
        HideStatus();
    }
}
