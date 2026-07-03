using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using CanfarDesktop.Models;
using CanfarDesktop.Services;

namespace CanfarDesktop.Views.Controls;

public sealed partial class RecentLaunchesControl : UserControl
{
    private readonly IRecentLaunchService _service;
    private List<RecentLaunch> _allLaunches = [];
    private readonly List<Button> _relaunchButtons = [];
    private bool _isAtSessionLimit;

    public event EventHandler<RecentLaunch>? RelaunchRequested;

    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _filterDebounce;

    public RecentLaunchesControl(IRecentLaunchService service)
    {
        _service = service;
        InitializeComponent();

        // Rebuilding every card per keystroke lags and resets the list scroll;
        // wait for a short typing pause instead.
        _filterDebounce = DispatcherQueue.CreateTimer();
        _filterDebounce.Interval = TimeSpan.FromMilliseconds(200);
        _filterDebounce.IsRepeating = false;
        _filterDebounce.Tick += (_, _) => ApplyFilter();

        Refresh();
    }

    public void Refresh()
    {
        // Keep the user's filter text — Refresh() also runs after every completed
        // launch, and silently clearing a filter they typed loses their place.
        _allLaunches = _service.Load();
        ApplyFilter();
    }

    private void OnFilterChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _filterDebounce.Stop();
            _filterDebounce.Start();
        }
    }

    private void ApplyFilter()
    {
        var filter = FilterBox.Text?.Trim() ?? string.Empty;

        var hasAny = _allLaunches.Count > 0;
        FilterBox.Visibility = hasAny ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.Visibility = hasAny ? Visibility.Visible : Visibility.Collapsed;

        var filtered = string.IsNullOrEmpty(filter)
            ? _allLaunches
            : _allLaunches.Where(l =>
                l.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                l.Type.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                l.ImageLabel.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        LaunchList.Children.Clear();
        _relaunchButtons.Clear();

        if (filtered.Count == 0)
        {
            EmptyText.Visibility = Visibility.Visible;
            EmptyText.Text = hasAny ? Helpers.Loc.T("Recent_NoMatches") : Helpers.Loc.T("Recent_Empty");
            return;
        }

        EmptyText.Visibility = Visibility.Collapsed;

        foreach (var launch in filtered)
        {
            LaunchList.Children.Add(BuildCard(launch));
        }
    }

    private Border BuildCard(RecentLaunch launch)
    {
        var (typeColor, typeImageFile, typeLabel) = GetTypeInfo(launch.Type);

        // Outer card border — matches SessionCard style
        var card = new Border
        {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1)
        };

        var outerGrid = new Grid { ColumnSpacing = 12 };
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left: Type image (same as SessionCard)
        var imageBorder = new Border
        {
            CornerRadius = new CornerRadius(6),
            Width = 48,
            Height = 48,
            VerticalAlignment = VerticalAlignment.Top,
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"]
        };
        var typeImage = new Image
        {
            Width = 36,
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform,
            Source = new BitmapImage(new Uri($"ms-appx:///Assets/{typeImageFile}"))
        };
        imageBorder.Child = typeImage;
        Grid.SetColumn(imageBorder, 0);
        outerGrid.Children.Add(imageBorder);

        // Right: Info rows
        var infoGrid = new Grid { RowSpacing = 5 };
        infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 0: Name + Type badge
        infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1: Project / Image
        infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 2: Resources
        infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 3: Date + Buttons
        Grid.SetColumn(infoGrid, 1);
        outerGrid.Children.Add(infoGrid);

        // Row 0: Name + Type badge
        var nameRow = new Grid();
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(nameRow, 0);
        infoGrid.Children.Add(nameRow);

        var nameText = new TextBlock
        {
            Text = launch.Name,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(nameText, 0);
        nameRow.Children.Add(nameText);

        var typeBadge = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 6, 1),
            Background = new SolidColorBrush(typeColor)
        };
        typeBadge.Child = new TextBlock
        {
            Text = typeLabel,
            FontSize = 11,
            Foreground = new SolidColorBrush(Colors.White),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"]
        };
        Grid.SetColumn(typeBadge, 1);
        nameRow.Children.Add(typeBadge);

        // Row 1: Project / Image label
        var imageInfo = string.IsNullOrEmpty(launch.Project)
            ? launch.ImageLabel
            : $"{launch.Project} / {launch.ImageLabel}";
        var imageText = new TextBlock
        {
            Text = imageInfo,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Opacity = 0.6,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetRow(imageText, 1);
        infoGrid.Children.Add(imageText);

        // Row 2: Resource info
        var resourceText = BuildResourceText(launch);
        var resourceBlock = new TextBlock
        {
            Text = resourceText,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Opacity = 0.5,
            FontSize = 11
        };
        Grid.SetRow(resourceBlock, 2);
        infoGrid.Children.Add(resourceBlock);

        // Row 3: Date + Relaunch/Remove buttons
        var bottomRow = new Grid();
        bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(bottomRow, 3);
        infoGrid.Children.Add(bottomRow);

        var dateText = new TextBlock
        {
            Text = FormatDate(launch.LaunchedAt),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Opacity = 0.5,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(dateText, 0);
        bottomRow.Children.Add(dateText);

        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        Grid.SetColumn(buttonPanel, 1);
        bottomRow.Children.Add(buttonPanel);

        var relaunchBtn = new Button { Padding = new Thickness(6, 4, 6, 4), IsEnabled = !_isAtSessionLimit };
        ToolTipService.SetToolTip(relaunchBtn, Helpers.Loc.T("Recent_Relaunch"));
        var relaunchContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        relaunchContent.Children.Add(new FontIcon { Glyph = "\uE768", FontSize = 12 });
        relaunchContent.Children.Add(new TextBlock { Text = Helpers.Loc.T("Recent_Relaunch"), FontSize = 12 });
        relaunchBtn.Content = relaunchContent;
        relaunchBtn.Click += (_, _) => RelaunchRequested?.Invoke(this, launch);
        buttonPanel.Children.Add(relaunchBtn);
        _relaunchButtons.Add(relaunchBtn);

        var removeBtn = new Button { Padding = new Thickness(6, 4, 6, 4) };
        ToolTipService.SetToolTip(removeBtn, Helpers.Loc.T("Recent_Remove"));
        removeBtn.Content = new FontIcon
        {
            Glyph = "\uE74D",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
        };
        removeBtn.Click += (_, _) =>
        {
            _service.Remove(launch);
            Refresh();
        };
        buttonPanel.Children.Add(removeBtn);

        card.Child = outerGrid;
        return card;
    }

    private static string BuildResourceText(RecentLaunch launch)
    {
        if (launch.ResourceType != "fixed")
            return Helpers.Loc.T("Launch_FlexibleResources");

        var parts = new List<string> { Helpers.Loc.F("Portal_CpuCount", launch.Cores), Helpers.Loc.F("Portal_RamGb", launch.Ram) };
        if (launch.Gpus > 0)
            parts.Add(Helpers.Loc.F("Portal_GpuCount", launch.Gpus));
        return string.Join("  \u00B7  ", parts);
    }

    private static string FormatDate(DateTime dt)
    {
        return dt.ToString("MMM dd, yyyy");
    }

    private static (Windows.UI.Color color, string imageFile, string label) GetTypeInfo(string type) => type switch
    {
        "notebook" => (ColorHelper.FromArgb(255, 33, 150, 243), "session-notebook.jpg", Helpers.Loc.T("Sessions_TypeNotebook")),
        "desktop" => (ColorHelper.FromArgb(255, 103, 58, 183), "session-desktop.png", Helpers.Loc.T("Sessions_TypeDesktop")),
        "carta" => (ColorHelper.FromArgb(255, 0, 150, 136), "session-carta.png", "CARTA"), // brand name
        "contributed" => (ColorHelper.FromArgb(255, 255, 87, 34), "session-contributed.png", Helpers.Loc.T("Sessions_TypeContrib")),
        "firefly" => (ColorHelper.FromArgb(255, 255, 152, 0), "session-firefly.png", "Firefly"), // brand name
        _ => (ColorHelper.FromArgb(255, 158, 158, 158), "session-desktop.png", type)
    };

    public void UpdateSessionLimit(bool isAtLimit)
    {
        _isAtSessionLimit = isAtLimit;
        DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var btn in _relaunchButtons)
                btn.IsEnabled = !isAtLimit;
        });
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _service.Clear();
        FilterBox.Text = string.Empty; // explicit clear-all is the one place the filter resets
        Refresh();
    }
}
