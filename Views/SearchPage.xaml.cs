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
    private readonly ObservationStore _observationStore;
    private readonly DataTrainManager _dataTrainMgr = new();
    private bool _dataTrainLoaded;

    public string[] SpectralUnits => UnitConverter.SpectralUnits;
    public string[] TimeUnits => UnitConverter.TimeUnits;
    public string[] PixelScaleUnits => UnitConverter.PixelScaleUnits;

    public SearchPage(SearchViewModel viewModel, DataLinkService dataLinkService, ObservationStore observationStore)
    {
        ViewModel = viewModel;
        _dataLinkService = dataLinkService;
        _observationStore = observationStore;
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

    private void OnLoadRecentSearch(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RecentSearch search })
        {
            ViewModel.LoadFromRecentSearch(search);
            // Sync data train UI if loaded
            if (_dataTrainUIBuilt)
            {
                _dataTrainMgr.ClearAll();
                // Restore data train manager selections from ViewModel
                foreach (var v in ViewModel.SelectedBands) _dataTrainMgr.SelectedBands.Add(v);
                foreach (var v in ViewModel.SelectedCollections) _dataTrainMgr.SelectedCollections.Add(v);
                foreach (var v in ViewModel.SelectedInstruments) _dataTrainMgr.SelectedInstruments.Add(v);
                foreach (var v in ViewModel.SelectedFilters) _dataTrainMgr.SelectedFilters.Add(v);
                foreach (var v in ViewModel.SelectedCalLevels) _dataTrainMgr.SelectedCalLevels.Add(v);
                foreach (var v in ViewModel.SelectedDataTypes) _dataTrainMgr.SelectedDataTypes.Add(v);
                foreach (var v in ViewModel.SelectedObsTypes) _dataTrainMgr.SelectedObsTypes.Add(v);
                _dataTrainMgr.Refresh();
                SyncAllTrainLists();
            }
            MainPivot.SelectedIndex = 0;
        }
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
            MainPivot.SelectedIndex = 2;
        }
    }

    private async void OnRunSavedQuery(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SavedQuery query }) return;
        try
        {
            // Show progress
            QueryRunStatus.Visibility = Visibility.Visible;
            QueryRunStatusText.Text = $"Running \"{query.Name}\"...";

            ViewModel.AdqlText = query.Adql;
            await ViewModel.ExecuteAdqlCommand.ExecuteAsync(null);

            QueryRunStatus.Visibility = Visibility.Collapsed;

            if (ViewModel.Results is not null)
            {
                RowsPerPageCombo.SelectedItem = ViewModel.RowsPerPage;
                RenderResultsPage();
                MainPivot.SelectedIndex = 1;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Run saved query error: {ex}");
            QueryRunStatus.Visibility = Visibility.Collapsed;
        }
    }

    private void OnDeleteSavedQuery(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SavedQuery query })
            ViewModel.DeleteSavedQuery(query);
    }

    #endregion

    #region Results table

    private void RenderResultsPage(bool rebuildHeader = true)
    {
        if (rebuildHeader)
        {
            DisposeFilterTimers();
            HeaderPanel.Children.Clear();

            if (ViewModel.Results is not null && ViewModel.Results.TotalRows > 0)
            {
                var visibleKeys = ViewModel.GetVisibleColumnKeys();
                if (visibleKeys.Length > 0)
                {
                    HeaderPanel.Children.Add(BuildRow(visibleKeys, isHeader: true));
                    HeaderPanel.Children.Add(BuildFilterRow(visibleKeys));
                }
            }
        }

        ResultsPanel.Children.Clear();

        if (ViewModel.Results is null || ViewModel.Results.TotalRows == 0)
        {
            ResultsPanel.Children.Add(new TextBlock
            {
                Text = "No results found.",
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Margin = new Thickness(0, 16, 0, 0)
            });
            UpdatePaginationUI();
            return;
        }

        var keys = ViewModel.GetVisibleColumnKeys();
        if (keys.Length == 0) return;

        var altBg = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
        var hoverBg = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];

        var pageRows = ViewModel.GetCurrentPageRows();
        for (var i = 0; i < pageRows.Count; i++)
        {
            var row = pageRows[i];
            var rowBorder = BuildRow(keys, isHeader: false, row: row, rowIndex: i);
            var capturedRow = row;
            var capturedIndex = i;
            var originalBg = capturedIndex % 2 == 1 ? altBg : null;

            rowBorder.Tapped += (_, e) =>
            {
                if (e.OriginalSource is FrameworkElement fe &&
                    (fe.Tag as string == "action" || FrameworkElementExtensions.FindParentWithTag(fe, "action") is not null))
                    return;
                ShowRowDetail(capturedRow);
            };
            rowBorder.PointerEntered += (s, _) => ((Border)s).Background = hoverBg;
            rowBorder.PointerExited += (s, _) => ((Border)s).Background = originalBg;
            ResultsPanel.Children.Add(rowBorder);
        }

        UpdatePaginationUI();
    }

    private void OnDataScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        HeaderScroll.ChangeView(DataScroll.HorizontalOffset, null, null, disableAnimation: true);
    }

    private readonly List<System.Threading.Timer> _filterTimers = [];

    private void DisposeFilterTimers()
    {
        foreach (var t in _filterTimers) t.Dispose();
        _filterTimers.Clear();
    }

    private Border BuildFilterRow(string[] columnKeys)
    {
        var sp = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Padding = new Thickness(4, 2, 4, 2),
            Spacing = 2
        };

        foreach (var key in columnKeys)
        {
            var width = ViewModel.GetColumnWidth(key);
            if (key is "download" or "preview")
            {
                sp.Children.Add(new Border { Width = width });
                continue;
            }

            var capturedKey = key;
            var tb = new TextBox
            {
                Width = width,
                Height = 24,
                FontSize = 11,
                Padding = new Thickness(4, 2, 4, 2),
                PlaceholderText = "Filter...",
                Text = ViewModel.GetColumnFilter(key)
            };

            System.Threading.Timer? debounce = null;
            tb.TextChanged += (s, _) =>
            {
                debounce?.Dispose();
                var timer = new System.Threading.Timer(_ =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        ViewModel.SetColumnFilter(capturedKey, ((TextBox)s).Text);
                        ViewModel.UpdatePagination();
                        RenderResultsPage(rebuildHeader: false);
                        UpdateApplyFiltersButton();
                    });
                }, null, 300, Timeout.Infinite);
                debounce = timer;
                _filterTimers.Add(timer);
            };

            sp.Children.Add(tb);
        }

        return new Border
        {
            Child = sp,
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
            Padding = new Thickness(0, 2, 0, 2)
        };
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
                var capturedRow = row;
                dlBtn.Click += async (_, _) => await DownloadFileAsync(capturedPubId, capturedRow);
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
                    Content = new TextBlock { Text = "Preview", FontSize = 11 },
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

                        if (imageUrl is null)
                        {
                            flyoutPanel.Children.Clear();
                            flyoutPanel.Children.Add(new TextBlock { Text = "No preview available", Opacity = 0.6 });
                            return;
                        }

                        var img = await LoadImageFromUrlAsync(imageUrl);
                        flyoutPanel.Children.Clear();
                        if (img is not null)
                            flyoutPanel.Children.Add(new Image { Source = img, MaxWidth = 300, MaxHeight = 300, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform });
                        else
                            flyoutPanel.Children.Add(new TextBlock { Text = "Image load failed", Opacity = 0.6 });
                    }
                    catch
                    {
                        flyoutPanel.Children.Clear();
                        flyoutPanel.Children.Add(new TextBlock { Text = "Preview failed", Opacity = 0.6 });
                        flyoutLoaded = false;
                    }
                };

                previewBtn.Flyout = flyout;
                sp.Children.Add(previewBtn);
            }
            else if (isHeader && key != "download" && key != "preview")
            {
                // Sortable header cell
                var label = ViewModel.GetColumnLabel(key);
                var sort = ViewModel.CurrentSort;
                if (sort.Key == key)
                    label += sort.Ascending ? " \u25B2" : " \u25BC";

                var headerText = new TextBlock
                {
                    Text = label,
                    Width = width,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var capturedKey = key;
                headerText.Tapped += (_, _) =>
                {
                    ViewModel.SortBy(capturedKey);
                    RenderResultsPage(rebuildHeader: true);
                };
                headerText.PointerEntered += (s, _) => ((TextBlock)s).Opacity = 0.6;
                headerText.PointerExited += (s, _) => ((TextBlock)s).Opacity = 1.0;
                sp.Children.Add(headerText);
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
                    VerticalAlignment = VerticalAlignment.Center
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
    private void OnFirstPage(object s, RoutedEventArgs e) { ViewModel.GoToFirstPage(); RenderResultsPage(rebuildHeader: false); }
    private void OnPrevPage(object s, RoutedEventArgs e) { ViewModel.GoToPreviousPage(); RenderResultsPage(rebuildHeader: false); }
    private void OnNextPage(object s, RoutedEventArgs e) { ViewModel.GoToNextPage(); RenderResultsPage(rebuildHeader: false); }
    private void OnLastPage(object s, RoutedEventArgs e) { ViewModel.GoToLastPage(); RenderResultsPage(rebuildHeader: false); }

    private void OnRowsPerPageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RowsPerPageCombo.SelectedItem is int rpp)
        {
            ViewModel.RowsPerPage = rpp;
            ViewModel.CurrentPage = 1;
            RenderResultsPage(rebuildHeader: false);
        }
    }

    private async void ShowRowDetail(SearchResultRow row)
    {
        if (ViewModel.Results is null) return;
        try
        {
            var publisherID = row.Get(ViewModel.GetColumnHeader("publisherid"));
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

            // Action buttons — close dialog first to avoid "only one ContentDialog" error
            ContentDialog? detailDialog = null;
            if (!string.IsNullOrEmpty(publisherID))
            {
                var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var capturedPubIdForDl = publisherID;
                var capturedRowForDl = row;
                btnPanel.Children.Add(UIFactory.CreateIconButton("\uE8B7", "Save to Research",
                    async (_, _) =>
                    {
                        detailDialog?.Hide();
                        await SaveToResearchAsync(capturedPubIdForDl, capturedRowForDl);
                    }));
                btnPanel.Children.Add(UIFactory.CreateIconButton("\uE896", "Download FITS",
                    async (_, _) =>
                    {
                        detailDialog?.Hide();
                        await DownloadFileAsync(capturedPubIdForDl, capturedRowForDl);
                    }));
                panel.Children.Add(btnPanel);
            }

            // Metadata fields
            foreach (var col in ViewModel.ResultColumns)
            {
                var rawVal = row.Get(col.Header);
                var metaRow = UIFactory.CreateMetadataRow(col.Label, ViewModel.FormatCell(col.Key, rawVal), 170);
                if (metaRow is not null) panel.Children.Add(metaRow);
            }

            detailDialog = new ContentDialog
            {
                Title = row.Get(ViewModel.GetColumnHeader("targetname")) is { Length: > 0 } name
                    ? $"Observation — {name}" : "Observation Detail",
                Content = new ScrollViewer { Content = panel, MaxHeight = 550 },
                CloseButtonText = "Close",
                XamlRoot = XamlRoot
            };
            detailDialog.Resources["ContentDialogMinWidth"] = 650.0;

            // Load images in background
            if (!string.IsNullOrEmpty(publisherID))
            {
                _ = LoadDetailImagesAsync(publisherID, imagePanel, imageProgress, imageSection);
            }
            else
            {
                imageSection.Visibility = Visibility.Collapsed;
            }

            await detailDialog.ShowAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Row detail error: {ex.Message}");
        }
    }

    #endregion

    #region Download + Preview

    private async Task LoadDetailImagesAsync(string publisherID, StackPanel imagePanel, ProgressRing spinner, StackPanel section)
    {
        try
        {
            var links = await _dataLinkService.GetLinksAsync(publisherID);
            var imageUrls = links.Previews.Concat(links.Thumbnails).Distinct().Take(3).ToList();

            spinner.IsActive = false;
            spinner.Visibility = Visibility.Collapsed;

            if (imageUrls.Count == 0)
            {
                section.Visibility = Visibility.Collapsed;
                return;
            }

            foreach (var url in imageUrls)
            {
                var img = await LoadImageFromUrlAsync(url);
                if (img is not null)
                    imagePanel.Children.Add(new Image { Source = img, MaxHeight = 180, MaxWidth = 250, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform });
            }
        }
        catch
        {
            spinner.IsActive = false;
            section.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Download image from URL with retry and timeout. Returns null on failure.
    /// Reusable across preview flyout and detail modal.
    /// </summary>
    private async Task<BitmapImage?> LoadImageFromUrlAsync(string url)
    {
        var bytes = await _dataLinkService.DownloadImageBytesAsync(url);
        if (bytes is null || bytes.Length == 0) return null;

        var bitmap = new BitmapImage();
        using var stream = new System.IO.MemoryStream(bytes);
        await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
        return bitmap;
    }

    private async Task SaveToResearchAsync(string publisherID, SearchResultRow? sourceRow)
    {
        if (sourceRow is null) return;
        try
        {
            var dataLink = await _dataLinkService.GetLinksAsync(publisherID);
            var obs = DownloadedObservation.FromSearchResult(sourceRow, null,
                dataLink, k => ViewModel.GetColumnHeader(k));
            _observationStore.Save(obs);

            DownloadInfoBar.IsOpen = true;
            DownloadInfoBar.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success;
            DownloadInfoBar.Title = "Saved to Research";
            DownloadProgressBar.Visibility = Visibility.Collapsed;
            DownloadProgressText.Text = obs.TargetName ?? publisherID;
            _ = Task.Delay(3000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() =>
            {
                DownloadInfoBar.IsOpen = false;
                DownloadProgressBar.Visibility = Visibility.Visible;
            }));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Save to research error: {ex.Message}");
        }
    }

    private async Task DownloadFileAsync(string publisherID, SearchResultRow? sourceRow = null)
    {
        try
        {
            // Resolve DataLink to find direct file URLs
            var dataLink = await _dataLinkService.GetLinksAsync(publisherID);
            var url = dataLink.DirectFileUrl ?? _dataLinkService.GetDownloadUrl(publisherID);
            var selectedFilename = "";

            // If multiple files available, let user choose
            if (dataLink.DirectFiles.Count > 1)
            {
                var picked = await ShowFileSelectionDialogAsync(dataLink.DirectFiles);
                if (picked is null) return; // cancelled
                url = picked.Url;
                selectedFilename = picked.Filename;
            }
            else if (dataLink.DirectFiles.Count == 1)
            {
                selectedFilename = dataLink.DirectFiles[0].Filename;
            }

            var hWnd = nint.Zero;
            if (WindowHelper.ActiveWindows.Count > 0)
                hWnd = WinRT.Interop.WindowNative.GetWindowHandle(WindowHelper.ActiveWindows[0]);
            if (hWnd == nint.Zero) return;

            var picker = new Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            var suggestedName = !string.IsNullOrEmpty(selectedFilename)
                ? selectedFilename
                : ExtractFilenameFromPublisherID(publisherID);
            if (!Path.HasExtension(suggestedName))
                suggestedName += ".fits";
            picker.SuggestedFileName = suggestedName;
            picker.FileTypeChoices.Add("FITS Image", new List<string> { ".fits" });
            picker.FileTypeChoices.Add("All Files", new List<string> { "." });

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            // Download with progress tracking
            var tempPath = file.Path + ".tmp";
            try
            {
                DownloadInfoBar.IsOpen = true;
                DownloadInfoBar.Title = $"Downloading {Path.GetFileName(file.Path)}...";
                DownloadProgressBar.IsIndeterminate = true;
                DownloadProgressText.Text = "";

                using var response = await _dataLinkService.DownloadAsync(url);
                var totalBytes = response.Content.Headers.ContentLength;
                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new System.IO.FileStream(tempPath, System.IO.FileMode.Create);

                if (totalBytes.HasValue)
                {
                    DownloadProgressBar.IsIndeterminate = false;
                    DownloadProgressBar.Maximum = totalBytes.Value;
                }

                var buffer = new byte[81920];
                long downloaded = 0;
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    downloaded += bytesRead;

                    if (totalBytes.HasValue)
                    {
                        DownloadProgressBar.Value = downloaded;
                        var pct = (double)downloaded / totalBytes.Value * 100;
                        DownloadProgressText.Text = $"{FormatBytes(downloaded)} / {FormatBytes(totalBytes.Value)} ({pct:F0}%)";
                    }
                    else
                    {
                        DownloadProgressText.Text = FormatBytes(downloaded);
                    }
                }

                await fileStream.FlushAsync();

                if (File.Exists(file.Path)) File.Delete(file.Path);
                File.Move(tempPath, file.Path);

                DownloadInfoBar.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success;
                DownloadInfoBar.Title = $"Downloaded {Path.GetFileName(file.Path)}";
                DownloadProgressBar.Visibility = Visibility.Collapsed;
                DownloadProgressText.Text = FormatBytes(downloaded);
                _ = Task.Delay(3000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() =>
                {
                    DownloadInfoBar.IsOpen = false;
                    DownloadProgressBar.Visibility = Visibility.Visible;
                }));
            }
            catch
            {
                DownloadInfoBar.IsOpen = false;
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                throw;
            }

            // Track in Research module
            var row = sourceRow;
            if (row is not null)
            {
                try
                {
                    var dlForObs = await _dataLinkService.GetLinksAsync(publisherID);
                    var obs = DownloadedObservation.FromSearchResult(row, file.Path,
                        dlForObs, k => ViewModel.GetColumnHeader(k));
                    var fi = new System.IO.FileInfo(file.Path);
                    if (fi.Exists) obs.FileSize = fi.Length;
                    _observationStore.Save(obs);
                }
                catch (Exception trackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Download tracking error: {trackEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Download error: {ex.Message}");
        }
    }

    private async Task<DataLinkFile?> ShowFileSelectionDialogAsync(List<DataLinkFile> files)
    {
        var listView = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 300,
        };

        foreach (var f in files)
        {
            var panel = new StackPanel { Spacing = 2 };
            panel.Children.Add(new TextBlock
            {
                Text = f.Filename,
                Style = (Microsoft.UI.Xaml.Style)Microsoft.UI.Xaml.Application.Current.Resources["BodyStrongTextBlockStyle"],
            });
            if (!string.IsNullOrEmpty(f.Description))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = f.Description,
                    Style = (Microsoft.UI.Xaml.Style)Microsoft.UI.Xaml.Application.Current.Resources["CaptionTextBlockStyle"],
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorTertiaryBrush"],
                });
            }
            if (!string.IsNullOrEmpty(f.ContentType))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = f.ContentType,
                    Style = (Microsoft.UI.Xaml.Style)Microsoft.UI.Xaml.Application.Current.Resources["CaptionTextBlockStyle"],
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorTertiaryBrush"],
                });
            }
            listView.Items.Add(panel);
        }

        if (listView.Items.Count > 0) listView.SelectedIndex = 0;

        var dialog = new ContentDialog
        {
            Title = $"Select file to download ({files.Count} available)",
            Content = listView,
            PrimaryButtonText = "Download",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        var idx = listView.SelectedIndex;
        return idx >= 0 && idx < files.Count ? files[idx] : null;
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

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };

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

    private void OnApplyFiltersToAdql(object sender, RoutedEventArgs e)
    {
        var filteredAdql = ViewModel.BuildFilteredAdql();
        if (filteredAdql is null) return;

        ViewModel.AdqlText = filteredAdql;
        MainPivot.SelectedIndex = 2; // Navigate to ADQL Editor
    }

    private void UpdateApplyFiltersButton()
    {
        ApplyFiltersBtn.Visibility = ViewModel.HasActiveFilters ? Visibility.Visible : Visibility.Collapsed;
    }

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
