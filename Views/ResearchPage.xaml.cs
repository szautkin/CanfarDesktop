using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.ViewModels;

namespace CanfarDesktop.Views;

public sealed partial class ResearchPage : UserControl
{
    public ResearchViewModel ViewModel { get; }
    private CancellationTokenSource? _previewCts;

    public ResearchPage(ResearchViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        RefreshList();
    }

    public void RefreshList()
    {
        ViewModel.Refresh();
        FileList.ItemsSource = ViewModel.FilteredObservations;
        CountText.Text = $"({ViewModel.ObservationCount})";
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.FilterText = FilterBox.Text;
        FileList.ItemsSource = ViewModel.FilteredObservations;
        CountText.Text = $"({ViewModel.ObservationCount})";
    }

    private void OnFileSelected(object sender, SelectionChangedEventArgs e)
    {
        _previewCts?.Cancel();

        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not DownloadedObservation obs)
        {
            ViewModel.SelectedObservation = null;
            EmptyState.Visibility = Visibility.Visible;
            DetailPanel.Visibility = Visibility.Collapsed;
            return;
        }

        ViewModel.SelectedObservation = obs;
        EmptyState.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;
        BuildDetail(obs);
    }

    private void BuildDetail(DownloadedObservation obs)
    {
        DetailContent.Children.Clear();
        _previewCts = new CancellationTokenSource();

        // Preview image
        if (obs.PreviewURL is not null || obs.ThumbnailURL is not null)
        {
            var imageContainer = new StackPanel { Spacing = 4 };
            var spinner = new ProgressRing { IsActive = true, Width = 24, Height = 24 };
            imageContainer.Children.Add(spinner);
            DetailContent.Children.Add(imageContainer);
            _ = LoadPreviewAsync(obs.PreviewURL ?? obs.ThumbnailURL!, imageContainer, spinner, _previewCts.Token);
        }

        // Title
        var title = !string.IsNullOrEmpty(obs.TargetName) ? obs.TargetName : obs.ObservationID;
        DetailContent.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["TitleTextBlockStyle"]
        });
        DetailContent.Children.Add(new TextBlock
        {
            Text = $"{obs.Collection} — {obs.ObservationID}",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Opacity = 0.6
        });

        // Action buttons
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        if (obs.FileExists)
        {
            btnPanel.Children.Add(UIFactory.CreateIconButton("\uE8E5", "Open File", (_, _) => ViewModel.OpenFileCommand.Execute(null)));
            btnPanel.Children.Add(UIFactory.CreateIconButton("\uE838", "Show in Explorer", (_, _) => ViewModel.ShowInExplorerCommand.Execute(null)));
        }

        var deleteBtn = UIFactory.CreateIconButton("\uE74D", "Delete", (_, _) =>
        {
            ViewModel.DeleteObservationCommand.Execute(null);
            EmptyState.Visibility = Visibility.Visible;
            DetailPanel.Visibility = Visibility.Collapsed;
            RefreshList();
        });
        btnPanel.Children.Add(deleteBtn);
        DetailContent.Children.Add(btnPanel);

        // Metadata
        AddRow("Collection", obs.Collection);
        AddRow("Observation ID", obs.ObservationID);
        AddRow("Target", obs.TargetName);
        AddRow("Instrument", obs.Instrument);
        AddRow("Filter", obs.Filter);
        AddRow("RA", obs.RA);
        AddRow("Dec", obs.Dec);
        if (!string.IsNullOrEmpty(obs.StartDate))
            AddRow("Start Date", CellFormatter.Format("startdate", obs.StartDate));
        AddRow("Cal. Level", CellFormatter.Format("callev", obs.CalLevel));

        // File info
        DetailContent.Children.Add(new TextBlock
        {
            Text = "File Info",
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            Margin = new Thickness(0, 8, 0, 0)
        });
        AddRow("Path", obs.LocalPath);
        if (obs.FileSize.HasValue) AddRow("Size", obs.FormattedSize);
        AddRow("Downloaded", obs.DownloadedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        AddRow("File exists", obs.FileExists ? "Yes" : "Missing");
    }

    private void AddRow(string label, string value)
    {
        var row = UIFactory.CreateMetadataRow(label, value, 130);
        if (row is not null) DetailContent.Children.Add(row);
    }

    private async Task LoadPreviewAsync(string url, StackPanel container, ProgressRing spinner, CancellationToken ct)
    {
        try
        {
            var bytes = await ViewModel.DataLink.DownloadImageBytesAsync(url);
            if (ct.IsCancellationRequested) return;

            spinner.IsActive = false;
            spinner.Visibility = Visibility.Collapsed;

            if (bytes is not null)
            {
                var bitmap = new BitmapImage();
                using var stream = new System.IO.MemoryStream(bytes);
                await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
                if (ct.IsCancellationRequested) return;

                container.Children.Add(new Image
                {
                    Source = bitmap,
                    MaxHeight = 300,
                    MaxWidth = 500,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Left
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Preview load error: {ex.Message}");
            spinner.IsActive = false;
            spinner.Visibility = Visibility.Collapsed;
        }
    }
}
