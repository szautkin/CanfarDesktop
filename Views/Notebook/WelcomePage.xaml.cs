using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Services.Notebook;

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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshRecent();
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
