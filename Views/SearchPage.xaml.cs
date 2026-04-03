using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Services;
using CanfarDesktop.ViewModels;
using CanfarDesktop.Views.Dialogs;

namespace CanfarDesktop.Views;

public sealed partial class SearchPage : Page
{
    public SearchViewModel ViewModel { get; }
    private readonly DataLinkService _dataLinkService;
    private readonly DataTrainManager _dataTrainMgr = new();
    private bool _dataTrainLoaded;

    public string[] SpectralUnits => UnitConverter.SpectralUnits;
    public string[] TimeUnits => UnitConverter.TimeUnits;
    public string[] PixelScaleUnits => UnitConverter.PixelScaleUnits;

    public SearchPage(SearchViewModel viewModel, DataLinkService dataLinkService)
    {
        ViewModel = viewModel;
        _dataLinkService = dataLinkService;
        InitializeComponent();

        // Ctrl+Enter to search
        var accelerator = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.Enter,
            Modifiers = Windows.System.VirtualKeyModifiers.Control
        };
        accelerator.Invoked += (_, e) => { OnSearchClick(this, new RoutedEventArgs()); e.Handled = true; };
        KeyboardAccelerators.Add(accelerator);
    }

    public void LoadAsync()
    {
        ViewModel.LoadRecentSearchesFromStore();
        ViewModel.LoadSavedQueriesFromStore();

        if (!_dataTrainLoaded)
        {
            _dataTrainLoaded = true;
            _ = LoadDataTrainInBackground();
        }
    }

    private async Task LoadDataTrainInBackground()
    {
        try
        {
            await ViewModel.LoadDataTrainAsync();
            var rows = ViewModel.AllDataTrainRows.ToList();

            DispatcherQueue.TryEnqueue(() =>
            {
                _dataTrainMgr.Load(rows);
                RebuildAllCheckColumns();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Data train background load: {ex.Message}");
            _dataTrainLoaded = false;
        }
    }

    #region Search actions

    private void SyncDataTrainToViewModel()
    {
        // Copy DataTrainManager selections to ViewModel for ADQL building
        CopySet(ViewModel.SelectedBands, _dataTrainMgr.SelectedBands);
        CopySet(ViewModel.SelectedCollections, _dataTrainMgr.SelectedCollections);
        CopySet(ViewModel.SelectedInstruments, _dataTrainMgr.SelectedInstruments);
        CopySet(ViewModel.SelectedFilters, _dataTrainMgr.SelectedFilters);
        CopySet(ViewModel.SelectedCalLevels, _dataTrainMgr.SelectedCalLevels);
        CopySet(ViewModel.SelectedDataTypes, _dataTrainMgr.SelectedDataTypes);
        CopySet(ViewModel.SelectedObsTypes, _dataTrainMgr.SelectedObsTypes);
    }

    private static void CopySet(System.Collections.ObjectModel.ObservableCollection<string> target, HashSet<string> source)
    {
        target.Clear();
        foreach (var v in source) target.Add(v);
    }

    private async void OnSearchClick(object sender, RoutedEventArgs e)
    {
        try
        {
            SyncDataTrainToViewModel();
            await ViewModel.SearchCommand.ExecuteAsync(null);
            if (ViewModel.Results is not null)
            {
                RowsPerPageCombo.SelectedItem = ViewModel.RowsPerPage;
                RenderResultsPage();
                MainPivot.SelectedIndex = 1;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Search error: {ex}");
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearForm();
        _dataTrainMgr.ClearAll();
        if (_dataTrainUIBuilt) SyncAllTrainLists();
    }

    private async void OnExecuteAdqlClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.ExecuteAdqlCommand.ExecuteAsync(null);
            if (ViewModel.Results is not null)
            {
                RowsPerPageCombo.SelectedItem = ViewModel.RowsPerPage;
                RenderResultsPage();
                MainPivot.SelectedIndex = 1;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ADQL execute error: {ex}");
        }
    }

    #endregion

    #region Data train (ListView-based cascade via DataTrainManager)

    private bool _dataTrainUIBuilt;
    private bool _suppressTrainEvents;
    private ListView[] _trainLists = [];

    private void RebuildAllCheckColumns()
    {
        if (!_dataTrainMgr.IsLoaded || _dataTrainUIBuilt) return;
        _dataTrainUIBuilt = true;
        _trainLists = [BandList, CollectionList, InstrumentList, FilterList, CalLevelList, DataTypeList, ObsTypeList];
        SyncAllTrainLists();
    }

    private void SyncAllTrainLists()
    {
        _suppressTrainEvents = true;
        SyncTrainList(BandList, _dataTrainMgr.AvailableBands, _dataTrainMgr.SelectedBands);
        SyncTrainList(CollectionList, _dataTrainMgr.AvailableCollections, _dataTrainMgr.SelectedCollections);
        SyncTrainList(InstrumentList, _dataTrainMgr.AvailableInstruments, _dataTrainMgr.SelectedInstruments);
        SyncTrainList(FilterList, _dataTrainMgr.AvailableFilters, _dataTrainMgr.SelectedFilters);
        SyncTrainList(CalLevelList, _dataTrainMgr.AvailableCalLevels, _dataTrainMgr.SelectedCalLevels);
        SyncTrainList(DataTypeList, _dataTrainMgr.AvailableDataTypes, _dataTrainMgr.SelectedDataTypes);
        SyncTrainList(ObsTypeList, _dataTrainMgr.AvailableObsTypes, _dataTrainMgr.SelectedObsTypes);
        _suppressTrainEvents = false;
    }

    private static void SyncTrainList(ListView list, HashSet<string> available, HashSet<string> selected)
    {
        var sorted = available.OrderBy(v => v).ToList();
        list.ItemsSource = sorted;

        // Restore selections
        foreach (var value in selected)
        {
            var idx = sorted.IndexOf(value);
            if (idx >= 0) list.SelectedItems.Add(sorted[idx]);
        }
    }

    private void OnTrainSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTrainEvents) return;
        if (sender is not ListView list || list.Tag is not string tagStr || !int.TryParse(tagStr, out var colIdx))
            return;

        // Read current selections from the ListView
        var newSelected = new HashSet<string>();
        foreach (var item in list.SelectedItems)
            if (item is string s) newSelected.Add(s);

        // Determine what changed
        var mgr = _dataTrainMgr;
        var oldSelected = colIdx switch
        {
            0 => mgr.SelectedBands,
            1 => mgr.SelectedCollections,
            2 => mgr.SelectedInstruments,
            3 => mgr.SelectedFilters,
            4 => mgr.SelectedCalLevels,
            5 => mgr.SelectedDataTypes,
            6 => mgr.SelectedObsTypes,
            _ => new HashSet<string>()
        };

        // Find the toggled item (added or removed)
        foreach (var added in e.AddedItems)
            if (added is string s && !oldSelected.Contains(s))
                mgr.Toggle(colIdx, s);

        foreach (var removed in e.RemovedItems)
            if (removed is string s && oldSelected.Contains(s))
                mgr.Toggle(colIdx, s);

        SyncAllTrainLists();
    }

    #endregion

    #region Recent searches + saved queries

    private bool _suppressRecentSelection;

    private void OnRecentSearchSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressRecentSelection) return;
        if (e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is RecentSearch search)
        {
            ViewModel.LoadFromRecentSearch(search);
            MainPivot.SelectedIndex = 0;
        }
        _suppressRecentSelection = true;
        RecentSearchList.SelectedIndex = -1;
        _suppressRecentSelection = false;
    }

    private void OnRemoveRecentSearch(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RecentSearch search })
            ViewModel.RemoveRecentSearch(search);
    }

    private void OnClearRecentSearches(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearAllRecentSearches();
    }

    private void OnSaveQuery(object sender, RoutedEventArgs e)
    {
        var name = SaveQueryName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = $"Query {DateTime.Now:yyyy-MM-dd HH:mm}";
        ViewModel.SaveCurrentQuery(name);
        SaveQueryName.Text = string.Empty;
    }

    private void OnLoadSavedQuery(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SavedQuery query })
        {
            ViewModel.LoadSavedQuery(query);
            MainPivot.SelectedIndex = 2; // Switch to ADQL tab
        }
    }

    private void OnDeleteSavedQuery(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SavedQuery query })
            ViewModel.DeleteSavedQuery(query);
    }

    #endregion

    #region Results table

    private void RenderResultsPage()
    {
        ResultsPanel.Children.Clear();

        if (ViewModel.Results is null || ViewModel.Results.TotalRows == 0)
        {
            ResultsPanel.Children.Add(new TextBlock
            {
                Text = "No results found.",
                Opacity = 0.6,
                Margin = new Thickness(0, 16, 0, 0)
            });
            UpdatePaginationUI();
            return;
        }

        var visibleKeys = ViewModel.GetVisibleColumnKeys();
        if (visibleKeys.Length == 0) return;

        // Header row
        ResultsPanel.Children.Add(BuildRow(visibleKeys, isHeader: true));

        // Current page data rows
        var pageRows = ViewModel.GetCurrentPageRows();
        for (var i = 0; i < pageRows.Count; i++)
        {
            var row = pageRows[i];
            var rowBorder = BuildRow(visibleKeys, isHeader: false, row: row, rowIndex: i);
            var capturedRow = row;
            rowBorder.Tapped += (_, e) =>
            {
                // Don't open detail if user clicked download/preview button
                if (e.OriginalSource is FrameworkElement fe &&
                    (fe.Tag as string == "action" || FrameworkElementExtensions.FindParentWithTag(fe, "action") is not null))
                    return;
                ShowRowDetail(capturedRow);
            };
            rowBorder.PointerEntered += (s, _) => ((Border)s).Opacity = 0.7;
            rowBorder.PointerExited += (s, _) => ((Border)s).Opacity = 1.0;
            ResultsPanel.Children.Add(rowBorder);
        }

        UpdatePaginationUI();
    }

    private Border BuildRow(string[] columnKeys, bool isHeader, SearchResultRow? row = null, int rowIndex = 0)
    {
        var sp = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Padding = new Thickness(4, 5, 4, 5),
            Spacing = 2
        };

        var publisherID = row?.Get(ViewModel.GetColumnHeader("publisherid")) ?? "";

        foreach (var key in columnKeys)
        {
            var width = ViewModel.GetColumnWidth(key);

            if (!isHeader && key == "download" && !string.IsNullOrEmpty(publisherID))
            {
                var capturedPubId = publisherID;
                var dlBtn = new Button
                {
                    Padding = new Thickness(2),
                    Background = null,
                    BorderThickness = new Thickness(0),
                    Width = width,
                    VerticalAlignment = VerticalAlignment.Center,
                    Content = new FontIcon { Glyph = "\uE896", FontSize = 13 },
                    Tag = "action" // marks as action cell — prevents row detail
                };
                dlBtn.Click += async (_, _) => await DownloadFileAsync(capturedPubId);
                sp.Children.Add(dlBtn);
            }
            else if (!isHeader && key == "preview" && !string.IsNullOrEmpty(publisherID))
            {
                var capturedPubId = publisherID;

                // Container button with flyout for preview
                var previewBtn = new Button
                {
                    Padding = new Thickness(2),
                    Background = null,
                    BorderThickness = new Thickness(0),
                    Width = width,
                    VerticalAlignment = VerticalAlignment.Center,
                    Content = new FontIcon { Glyph = "\uE8B9", FontSize = 13 },
                    Tag = "action"
                };

                // Flyout with preview image — loads on open
                var flyoutPanel = new StackPanel { MinWidth = 200, MinHeight = 100 };
                var spinner = new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center };
                flyoutPanel.Children.Add(spinner);

                var flyout = new Flyout { Content = flyoutPanel };
                var flyoutLoaded = false;

                flyout.Opened += async (_, _) =>
                {
                    if (flyoutLoaded) return;
                    flyoutLoaded = true;

                    try
                    {
                        var links = await _dataLinkService.GetLinksAsync(capturedPubId);
                        var imageUrl = links.Thumbnails.FirstOrDefault() ?? links.Previews.FirstOrDefault();

                        if (imageUrl is not null)
                        {
                            try
                            {
                                using var imgResponse = await _dataLinkService.DownloadAsync(imageUrl);
                                using var imgStream = await imgResponse.Content.ReadAsStreamAsync();
                                var memStream = new System.IO.MemoryStream();
                                await imgStream.CopyToAsync(memStream);
                                memStream.Position = 0;

                                var bitmap = new BitmapImage();
                                await bitmap.SetSourceAsync(memStream.AsRandomAccessStream());

                                flyoutPanel.Children.Clear();
                                flyoutPanel.Children.Add(new Image
                                {
                                    Source = bitmap,
                                    MaxWidth = 300,
                                    MaxHeight = 300,
                                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                                });
                            }
                            catch (Exception imgEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"Image download failed: {imgEx.Message}");
                                flyoutPanel.Children.Clear();
                                flyoutPanel.Children.Add(new TextBlock { Text = "Image load failed", Opacity = 0.6 });
                            }
                        }
                        else
                        {
                            flyoutPanel.Children.Clear();
                            flyoutPanel.Children.Add(new TextBlock { Text = "No preview available", Opacity = 0.6 });
                        }
                    }
                    catch
                    {
                        flyoutPanel.Children.Clear();
                        flyoutPanel.Children.Add(new TextBlock { Text = "Preview failed", Opacity = 0.6 });
                        flyoutLoaded = false;
                    }
                };

                // Open flyout on hover
                previewBtn.PointerEntered += (s, _) =>
                {
                    flyout.ShowAt((FrameworkElement)s);
                };

                previewBtn.Flyout = flyout;
                sp.Children.Add(previewBtn);
            }
            else
            {
                var text = isHeader ? ViewModel.GetColumnLabel(key) : ViewModel.FormatCell(key, row?.Get(ViewModel.GetColumnHeader(key)) ?? "");
                sp.Children.Add(new TextBlock
                {
                    Text = text,
                    Width = width,
                    FontSize = 12,
                    FontWeight = isHeader ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
                });
            }
        }

        var border = new Border { Child = sp, MinHeight = 28 };

        if (isHeader)
            border.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"];
        else if (rowIndex % 2 == 1)
            border.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];

        return border;
    }

    private async void OnColumnsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new ColumnSelectorDialog(ViewModel.ResultColumns.ToList())
            {
                XamlRoot = XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                dialog.ApplySelections();
                // Sync back to ViewModel
                for (var i = 0; i < ViewModel.ResultColumns.Count; i++)
                    ViewModel.ResultColumns[i].Visible = dialog._columns[i].Visible;
                RenderResultsPage();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Column selector error: {ex.Message}");
        }
    }

    private void UpdatePaginationUI()
    {
        ViewModel.UpdatePagination();
        PageStatusText.Text = ViewModel.PageStatus;
        PageNumberText.Text = ViewModel.TotalPages > 0
            ? $"Page {ViewModel.CurrentPage} / {ViewModel.TotalPages}"
            : "";
    }

    // Pagination handlers
    private void OnFirstPage(object s, RoutedEventArgs e) { ViewModel.GoToFirstPage(); RenderResultsPage(); }
    private void OnPrevPage(object s, RoutedEventArgs e) { ViewModel.GoToPreviousPage(); RenderResultsPage(); }
    private void OnNextPage(object s, RoutedEventArgs e) { ViewModel.GoToNextPage(); RenderResultsPage(); }
    private void OnLastPage(object s, RoutedEventArgs e) { ViewModel.GoToLastPage(); RenderResultsPage(); }

    private void OnRowsPerPageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RowsPerPageCombo.SelectedItem is int rpp)
        {
            ViewModel.RowsPerPage = rpp;
            ViewModel.CurrentPage = 1;
            RenderResultsPage();
        }
    }

    private async void ShowRowDetail(SearchResultRow row)
    {
        if (ViewModel.Results is null) return;
        try
        {
            var publisherID = row.Get(ViewModel.GetColumnHeader("publisherid"));
            var fgBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];

            var panel = new StackPanel { Spacing = 12 };

            // Preview images section (loads async)
            var imagePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var imageProgress = new ProgressRing { IsActive = true, Width = 24, Height = 24 };
            var imageSection = new StackPanel { Spacing = 4 };
            imageSection.Children.Add(imageProgress);
            imageSection.Children.Add(new ScrollViewer
            {
                Content = imagePanel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 200
            });
            panel.Children.Add(imageSection);

            // Action buttons
            if (!string.IsNullOrEmpty(publisherID))
            {
                var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var downloadBtn = new Button
                {
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Children = { new FontIcon { Glyph = "\uE896", FontSize = 14 }, new TextBlock { Text = "Download" } }
                    }
                };
                var capturedPubIdForDl = publisherID;
                downloadBtn.Click += async (_, _) => await DownloadFileAsync(capturedPubIdForDl);

                var viewBtn = new Button { Content = "View on CADC" };
                viewBtn.Click += (_, _) =>
                {
                    var url = $"https://ws.cadc-ccda.hia-iha.nrc-cnrc.gc.ca/caom2ops/details?ID={Uri.EscapeDataString(publisherID)}";
                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                        _ = Windows.System.Launcher.LaunchUriAsync(uri);
                };

                btnPanel.Children.Add(downloadBtn);
                btnPanel.Children.Add(viewBtn);
                panel.Children.Add(btnPanel);
            }

            // Metadata fields
            foreach (var col in ViewModel.ResultColumns)
            {
                var rawVal = row.Get(col.Header);
                if (string.IsNullOrWhiteSpace(rawVal)) continue;
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                sp.Children.Add(new TextBlock
                {
                    Text = col.Label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Width = 170, FontSize = 12, Foreground = fgBrush
                });
                sp.Children.Add(new TextBlock
                {
                    Text = ViewModel.FormatCell(col.Key, rawVal), IsTextSelectionEnabled = true,
                    FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = fgBrush
                });
                panel.Children.Add(sp);
            }

            var dialog = new ContentDialog
            {
                Title = row.Get(ViewModel.GetColumnHeader("targetname")) is { Length: > 0 } name
                    ? $"Observation — {name}" : "Observation Detail",
                Content = new ScrollViewer { Content = panel, MaxHeight = 550 },
                CloseButtonText = "Close",
                XamlRoot = XamlRoot
            };
            dialog.Resources["ContentDialogMinWidth"] = 650.0;

            // Load images in background
            if (!string.IsNullOrEmpty(publisherID))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var links = await _dataLinkService.GetLinksAsync(publisherID);
                        var imageUrls = links.Previews.Concat(links.Thumbnails).Take(5).ToList();
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            imageProgress.IsActive = false;
                            imageProgress.Visibility = Visibility.Collapsed;
                            if (imageUrls.Count == 0)
                            {
                                imageSection.Visibility = Visibility.Collapsed;
                                return;
                            }
                            foreach (var url in imageUrls)
                            {
                                imagePanel.Children.Add(new Image
                                {
                                    Source = new BitmapImage(new Uri(url)),
                                    MaxHeight = 180,
                                    MaxWidth = 250,
                                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                                });
                            }
                        });
                    }
                    catch
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            imageProgress.IsActive = false;
                            imageSection.Visibility = Visibility.Collapsed;
                        });
                    }
                });
            }
            else
            {
                imageSection.Visibility = Visibility.Collapsed;
            }

            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Row detail error: {ex.Message}");
        }
    }

    #endregion

    #region Download + Preview

    private async Task DownloadFileAsync(string publisherID)
    {
        try
        {
            var url = _dataLinkService.GetDownloadUrl(publisherID);

            var hWnd = nint.Zero;
            if (WindowHelper.ActiveWindows.Count > 0)
                hWnd = WinRT.Interop.WindowNative.GetWindowHandle(WindowHelper.ActiveWindows[0]);
            if (hWnd == nint.Zero) return;

            var picker = new Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.SuggestedFileName = ExtractFilenameFromPublisherID(publisherID);
            picker.FileTypeChoices.Add("All Files", new List<string> { "." });

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            // Download
            using var response = await _dataLinkService.DownloadAsync(url);
            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = await file.OpenStreamForWriteAsync();
            await stream.CopyToAsync(fileStream);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Download error: {ex.Message}");
        }
    }

    private static string ExtractFilenameFromPublisherID(string publisherID)
    {
        // e.g. "ivo://cadc.nrc.ca/CFHT?1100689/1100689o" → "1100689o"
        var lastSlash = publisherID.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash < publisherID.Length - 1)
            return publisherID[(lastSlash + 1)..];
        var lastQuestion = publisherID.LastIndexOf('?');
        if (lastQuestion >= 0 && lastQuestion < publisherID.Length - 1)
            return publisherID[(lastQuestion + 1)..];
        return "observation";
    }

    private async Task LoadPreviewFlyout(Flyout flyout, string publisherID)
    {
        try
        {
            var spinner = new ProgressRing { IsActive = true, Width = 24, Height = 24 };
            flyout.Content = spinner;

            var links = await _dataLinkService.GetLinksAsync(publisherID);
            var imageUrl = links.Previews.FirstOrDefault() ?? links.Thumbnails.FirstOrDefault();

            if (imageUrl is not null)
            {
                flyout.Content = new Image
                {
                    Source = new BitmapImage(new Uri(imageUrl)),
                    MaxWidth = 300,
                    MaxHeight = 300,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                };
            }
            else
            {
                flyout.Content = new TextBlock { Text = "No preview available", Opacity = 0.6 };
            }
        }
        catch
        {
            flyout.Content = new TextBlock { Text = "Preview failed", Opacity = 0.6 };
        }
    }

    #endregion

    #region Export

    private async void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        await ExportFileAsync(ViewModel.ExportResultsCsv(), "search_results", ".csv", "CSV");
    }

    private async void OnExportTsvClick(object sender, RoutedEventArgs e)
    {
        await ExportFileAsync(ViewModel.ExportResultsTsv(), "search_results", ".tsv", "TSV");
    }

    private async Task ExportFileAsync(string content, string suggestedName, string extension, string formatLabel)
    {
        if (string.IsNullOrEmpty(content)) return;

        try
        {
            var hWnd = nint.Zero;
            if (WindowHelper.ActiveWindows.Count > 0)
                hWnd = WinRT.Interop.WindowNative.GetWindowHandle(WindowHelper.ActiveWindows[0]);
            if (hWnd == nint.Zero) return;

            var picker = new Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.SuggestedFileName = suggestedName;
            picker.FileTypeChoices.Add(formatLabel, new List<string> { extension });

            var file = await picker.PickSaveFileAsync();
            if (file is not null)
                await Windows.Storage.FileIO.WriteTextAsync(file, content);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Export failed: {ex.Message}");
        }
    }

    #endregion
}

internal static class WindowHelper
{
    internal static readonly List<Window> ActiveWindows = [];

    internal static void TrackWindow(Window window)
    {
        window.Closed += (_, _) => ActiveWindows.Remove(window);
        ActiveWindows.Add(window);
    }
}

internal static class FrameworkElementExtensions
{
    /// <summary>Walk up the visual tree looking for an element with the given Tag value.</summary>
    internal static FrameworkElement? FindParentWithTag(this FrameworkElement element, string tag)
    {
        var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element) as FrameworkElement;
        while (parent is not null)
        {
            if (parent.Tag as string == tag) return parent;
            parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent) as FrameworkElement;
        }
        return null;
    }
}
