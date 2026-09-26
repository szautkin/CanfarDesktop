using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Services;
using CanfarDesktop.Services.Database;
using CanfarDesktop.Services.Export;
using CanfarDesktop.ViewModels;

namespace CanfarDesktop.Views;

public sealed partial class ResearchPage : UserControl
{
    public ResearchViewModel ViewModel { get; }
    private readonly ObservationNoteStore _noteStore;
    private readonly ExportService _exportService;
    private readonly IReadOnlyList<IExportableModule> _exportModules;
    private readonly IStorageService _storage;
    private readonly IAuthService _auth;
    private CancellationTokenSource? _previewCts;

    // Research-notes editor state. _noteEditId is the publisher ID the editor currently holds;
    // saving always writes under it, and we flush under the OUTGOING id before loading a new one,
    // so quick selection switches never cross-contaminate notes.
    private readonly DispatcherTimer _noteSaveTimer;
    private string? _noteEditId;
    private bool _suppressNoteEvents;
    private TextBox? _noteBox;
    private RatingControl? _ratingControl;
    private TextBox? _tagsBox;

    public ResearchPage(ResearchViewModel viewModel, ObservationNoteStore noteStore,
        ExportService exportService, IEnumerable<IExportableModule> exportModules,
        IStorageService storage, IAuthService auth)
    {
        ViewModel = viewModel;
        _noteStore = noteStore;
        _exportService = exportService;
        _exportModules = exportModules.ToList();
        _storage = storage;
        _auth = auth;
        InitializeComponent();

        _noteSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _noteSaveTimer.Tick += (_, _) => SaveNoteNow();

        // Downloads finish in the background, on the downloader's thread and in their own time.
        ViewModel.ObservationsChanged += () => DispatcherQueue.TryEnqueue(OnObservationsChanged);

        RefreshList();
    }

    /// <summary>
    /// The person wants a cutout of this observation — of the file a cutout record was cut from, when
    /// it is one. The host opens the observation's cutout editor.
    /// </summary>
    public event Action<string, string?>? CutoutRequested;

    /// <summary>
    /// "Cut out…", always there: greyed while it is found out whether this observation's files can be
    /// cut — by CADC, or from its file on this computer — then live, or greyed with the reason, so the
    /// person knows the app can do it, and whether it can for THIS observation.
    /// </summary>
    private FrameworkElement CutoutButton(DownloadedObservation obs)
    {
        var button = UIFactory.CreateIconButton("", Loc.T("Cutout_Button"),
            (_, _) => CutoutRequested?.Invoke(obs.PublisherID, obs.Cutout?.ArtifactId ?? obs.ArtifactId));
        button.Name = "ResearchCutoutButton";
        var wrapper = UIFactory.Explained(button);
        UIFactory.Enable(button, false, Loc.T("Cutout_Checking"));

        var ct = _previewCts?.Token ?? CancellationToken.None;
        _ = Task.Run(() => ViewModel.CutoutSourcesAsync(obs)).ContinueWith(t => DispatcherQueue.TryEnqueue(() =>
        {
            if (ct.IsCancellationRequested) return;
            IReadOnlyList<Services.Cutouts.ICutoutSource> ways = t.Status == TaskStatus.RanToCompletion ? t.Result : [];
            var why = ways.FirstOrDefault(w => w.Method == Models.Cutouts.CutoutMethod.Local)?.Unavailable is { } local
                ? Loc.F("Cutout_NoneForObservationLocal", local)
                : Loc.T("Cutout_NoneForObservation");
            UIFactory.Enable(button, ways.Any(w => w.Unavailable is null), why);
        }), TaskScheduler.Default);

        return wrapper;
    }

    /// <summary>
    /// Delete the file from this computer, after asking — the observation, its notes and (for a cutout)
    /// its region stay in Research, and Download brings the file back.
    /// </summary>
    private async Task ConfirmRemoveFileAsync(DownloadedObservation obs)
    {
        var dialog = new ContentDialog
        {
            Title = Loc.T("Research_RemoveFileTitle"),
            Content = new TextBlock { Text = Loc.F("Research_RemoveFileBody", obs.Filename), TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = Loc.T("Research_RemoveFile"),
            CloseButtonText = Loc.T("Research_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        if (ViewModel.RemoveLocalFile(obs) is { } why)
            await ShowExportResultAsync(Loc.T("Research_RemoveFileFailed"), why);
        // Done: the store's Changed rebuilds this detail with Download in place of the file's buttons.
    }

    /// <summary>What the open detail was built to offer: the file's buttons, "downloading", or Download.</summary>
    private (bool HasFile, bool Downloading) _detailState;

    private (bool HasFile, bool Downloading) DetailState(DownloadedObservation obs)
        => (obs.FileExists, ViewModel.IsDownloading(obs));

    /// <summary>
    /// A download started, landed or failed, or a record changed elsewhere. The list follows, and the
    /// open detail is rebuilt only if what it offers has changed — rebuilding on every save would
    /// reset the notes editor under someone typing in it.
    /// </summary>
    private void OnObservationsChanged()
    {
        RefreshList();
        if (ViewModel.SelectedObservation is { } obs && DetailState(obs) != _detailState)
        {
            FlushNote();
            BuildDetail(obs);
        }
    }

    public void RefreshList()
    {
        // Re-entering Research must not wipe the user's selection and scroll when
        // nothing changed — only reassign the list when the observation set differs.
        // Compare INSTANCES, not ids: a re-download replaces the record with a new
        // instance under the same PublisherID, and keeping the stale one would show
        // old fields and break Delete (which matches on the record's internal Id).
        var before = (FileList.ItemsSource as IEnumerable<DownloadedObservation>)?.ToList();
        // The same PRODUCT, not just the same observation: a cutout and the complete download share a
        // publisher id, and re-selecting by it alone jumped from one to the other.
        var selected = FileList.SelectedItem as DownloadedObservation;
        var selectedId = selected?.PublisherID;
        var selectedProduct = selected?.ProductKey;

        ViewModel.Refresh();
        var fresh = ViewModel.FilteredObservations;

        if (before is null || before.Count != fresh.Count
            || !before.Zip(fresh).All(pair => ReferenceEquals(pair.First, pair.Second)))
        {
            FileList.ItemsSource = fresh;
            if (selectedId is not null)
                FileList.SelectedItem = fresh.FirstOrDefault(o => o.PublisherID == selectedId && o.ProductKey == selectedProduct);
        }
        CountText.Text = $"({ViewModel.ObservationCount})";
    }

    /// <summary>Dropdown offering BOTH in-app viewers for a file with a real third axis; the label
    /// reflects the recommendation (spectral cube -> Cube Viewer, detector stack -> FITS Viewer) but
    /// the user can always pick either.</summary>
    private Microsoft.UI.Xaml.Controls.DropDownButton BuildResearchViewerDropdown(Helpers.FitsShape shape)
    {
        var fitsItem = new MenuFlyoutItem { Text = Loc.T("Research_FitsViewer") };
        fitsItem.Click += (_, _) => ViewModel.OpenInFitsViewerCommand.Execute(null);
        var cubeItem = new MenuFlyoutItem { Text = Loc.T("Research_CubeViewer") };
        cubeItem.Click += (_, _) => ViewModel.OpenInCubeViewerCommand.Execute(null);

        var flyout = new MenuFlyout();
        if (shape.RecommendCube) { flyout.Items.Add(cubeItem); flyout.Items.Add(fitsItem); }
        else { flyout.Items.Add(fitsItem); flyout.Items.Add(cubeItem); }

        return new Microsoft.UI.Xaml.Controls.DropDownButton
        {
            Content = Loc.T(shape.RecommendCube ? "Research_CubeViewer" : "Research_FitsViewer"),
            Flyout = flyout,
        };
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.FilterText = FilterBox.Text;
        FileList.ItemsSource = ViewModel.FilteredObservations;
        CountText.Text = $"({ViewModel.ObservationCount})";
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            // 1. Options
            var includeNotes = new CheckBox { Content = Loc.T("Research_ExportIncludeNotes"), IsChecked = true };
            var includeHistory = new CheckBox { Content = Loc.T("Research_ExportIncludeHistory"), IsChecked = true };
            var includeFiles = new CheckBox { Content = Loc.T("Research_ExportIncludeFiles"), IsChecked = false };
            var uploadVo = new CheckBox { Content = Loc.T("Research_ExportUploadVoSpace"), IsChecked = false };
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("Research_ExportDescription"),
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            });
            panel.Children.Add(includeNotes);
            panel.Children.Add(includeHistory);
            panel.Children.Add(includeFiles);
            panel.Children.Add(uploadVo);

            var optionsDialog = new ContentDialog
            {
                Title = Loc.T("Research_ExportDialogTitle"),
                Content = panel,
                PrimaryButtonText = Loc.T("Research_ExportChooseFolder"),
                CloseButtonText = Loc.T("Research_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };
            if (await optionsDialog.ShowAsync() != ContentDialogResult.Primary) return;

            var options = new ExportOptions
            {
                IncludeNotes = includeNotes.IsChecked == true,
                IncludeSearchHistory = includeHistory.IsChecked == true,
                IncludeFileCopies = includeFiles.IsChecked == true,
            };

            // 2. Destination folder
            var hwnd = WindowHelper.ActiveWindows.Count > 0
                ? WinRT.Interop.WindowNative.GetWindowHandle(WindowHelper.ActiveWindows[0])
                : nint.Zero;
            if (hwnd == nint.Zero) return;

            var picker = new Windows.Storage.Pickers.FolderPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) return;

            // 3. Build → zip → optional upload
            var bundleDir = await _exportService.BuildBundleAsync(
                folder.Path, _exportModules, options, DateTimeOffset.Now, AppVersion(), Environment.MachineName);
            var zipPath = _exportService.ZipBundle(bundleDir);

            string? remote = null;
            if (uploadVo.IsChecked == true && _auth.CurrentUsername is { Length: > 0 } username)
                remote = await _exportService.UploadBundleToVoSpaceAsync(zipPath, _storage, username);

            var message = Loc.F("Research_ExportBundleLine", bundleDir) + "\n" + Loc.F("Research_ExportZipLine", zipPath);
            if (remote is not null) message += "\n" + Loc.F("Research_ExportUploadedLine", remote);
            else if (uploadVo.IsChecked == true) message += "\n\n" + Loc.T("Research_ExportUploadSkipped");
            await ShowExportResultAsync(Loc.T("Research_ExportComplete"), message);
        }
        catch (ExportException ex)
        {
            await ShowExportResultAsync(Loc.T("Research_ExportFailed"), ex.Message);
        }
        catch (Exception ex)
        {
            await ShowExportResultAsync(Loc.T("Research_ExportFailed"), ex.Message);
        }
    }

    private Task ShowExportResultAsync(string title, string message)
        => new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            CloseButtonText = Loc.T("Research_Close"),
            XamlRoot = XamlRoot,
        }.ShowAsync().AsTask();

    private static string AppVersion()
    {
        try
        {
            var v = Windows.ApplicationModel.Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
        }
        catch
        {
            return "unknown";
        }
    }

    private void OnFileSelected(object sender, SelectionChangedEventArgs e)
    {
        _previewCts?.Cancel();
        FlushNote(); // persist the outgoing observation's note before switching

        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not DownloadedObservation obs)
        {
            _noteEditId = null;
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
        _detailState = DetailState(obs);

        // Preview image
        if (obs.PreviewURL is not null || obs.ThumbnailURL is not null)
        {
            var imageContainer = new StackPanel { Spacing = 4 };
            var spinner = new ProgressRing { IsActive = true, Width = 24, Height = 24 };
            imageContainer.Children.Add(spinner);
            DetailContent.Children.Add(imageContainer);
            _ = LoadPreviewAsync(obs.PreviewURL ?? obs.ThumbnailURL!, imageContainer, spinner, _previewCts.Token);
        }
        else if (!string.IsNullOrEmpty(obs.PublisherID) && !_previewLookupDone.Contains(obs.PublisherID))
        {
            // Older records (or MCP downloads) saved without preview URLs: look them up via
            // DataLink once, persist on the record, and show the preview like any other.
            var imageContainer = new StackPanel { Spacing = 4 };
            var spinner = new ProgressRing { IsActive = true, Width = 24, Height = 24 };
            imageContainer.Children.Add(spinner);
            DetailContent.Children.Add(imageContainer);
            _ = ResolvePreviewViaDataLinkAsync(obs, imageContainer, spinner, _previewCts.Token);
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

        // A cutout says so before anything else: its metadata and notes are the observation's, and
        // nothing else on this page would tell a reader the file is only part of it.
        if (obs.Cutout is { } cutout)
        {
            DetailContent.Children.Add(new InfoBar
            {
                Name = "CutoutNoteBar",
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Warning,
                Title = Loc.T("Research_CutoutTitle"),
                Message = Loc.F(cutout.CutBy == Models.Cutouts.CutoutMethod.Local ? "Research_CutoutNoteLocal" : "Research_CutoutNote",
                    cutout.Summary),
            });
        }

        // Action buttons
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        if (obs.FileExists)
        {
            // Open-in-viewer. Default to the FITS-viewer button; once the (off-UI-thread) header
            // sniff resolves, a file with a real third axis becomes a dropdown offering BOTH viewers
            // (recommendation reflected in the label — spectral cube → Cube Viewer, detector
            // stack → FITS Viewer), so the user is never restricted. The sniff is file I/O, run
            // off-thread because a OneDrive/AV-scanned path can block the open for seconds.
            var fitsBtn = UIFactory.CreateIconButton("", Loc.T("Research_FitsViewer"), (_, _) => ViewModel.OpenInFitsViewerCommand.Execute(null));
            btnPanel.Children.Add(fitsBtn);
            var sniffPath = obs.LocalPath;
            var sniffCt = _previewCts.Token;
            _ = Task.Run(() => Helpers.FitsSniff.Inspect(sniffPath)).ContinueWith(t =>
            {
                if (t.Status != TaskStatus.RanToCompletion || sniffCt.IsCancellationRequested || !t.Result.HasCubeAxis) return;
                var shape = t.Result;
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (sniffCt.IsCancellationRequested) return;
                    var idx = btnPanel.Children.IndexOf(fitsBtn);
                    if (idx >= 0) btnPanel.Children[idx] = BuildResearchViewerDropdown(shape);
                });
            });
            btnPanel.Children.Add(UIFactory.CreateIconButton("\uE8E5", Loc.T("Research_OpenFile"), (_, _) => ViewModel.OpenFileCommand.Execute(null)));
            btnPanel.Children.Add(UIFactory.CreateIconButton("\uE838", Loc.T("Research_ShowInExplorer"), (_, _) => ViewModel.ShowInExplorerCommand.Execute(null)));

            // The file off this computer, the observation kept: Download brings it back.
            var removeFile = UIFactory.CreateIconButton("\uE738", Loc.T("Research_RemoveFile"), async (_, _) => await ConfirmRemoveFileAsync(obs));
            removeFile.Name = "ResearchRemoveFileButton";
            ToolTipService.SetToolTip(removeFile, Loc.T("Research_RemoveFileTip"));
            btnPanel.Children.Add(removeFile);
        }
        else if (ViewModel.IsDownloading(obs))
        {
            // On its way, in the background: the status bar shows how far, and this detail rebuilds
            // itself with the file's buttons when it lands (or with Download again if it fails).
            btnPanel.Children.Add(new TextBlock
            {
                Text = Loc.T("Research_DownloadingInBackground"),
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            });
        }
        else
        {
            // A local cutout is made again from the file it was cut from, rather than downloaded.
            var fetch = obs.Cutout?.CutBy == Models.Cutouts.CutoutMethod.Local ? Loc.T("Research_CutAgain") : Loc.T("Research_DownloadFits");
            btnPanel.Children.Add(UIFactory.CreateIconButton("\uE896", fetch, async (_, _) =>
            {
                try
                {
                    var hwnd = nint.Zero;
                    if (WindowHelper.ActiveWindows.Count > 0)
                        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(WindowHelper.ActiveWindows[0]);
                    if (hwnd == nint.Zero) return;

                    var picker = new Windows.Storage.Pickers.FileSavePicker();
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                    var name = obs.TargetName is { Length: > 0 } ? obs.TargetName : Loc.T("Research_DefaultFileName");
                    // A cutout comes back as the cutout it is, under its own name.
                    picker.SuggestedFileName = obs.Cutout is { } cut
                        ? System.IO.Path.GetFileNameWithoutExtension(cut.FileName)
                        : name;
                    picker.FileTypeChoices.Add(Loc.T("Research_FileTypeFits"), new List<string> { ".fits" });

                    var file = await picker.PickSaveFileAsync();
                    if (file is null) return;

                    // Started, not awaited: it belongs to the app now. Closing this detail, or choosing
                    // another observation, no longer has any say in it.
                    ViewModel.StartDownload(obs, file.Path);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Research download error: {ex.Message}");
                }
            }));
        }

        btnPanel.Children.Add(CutoutButton(obs));

        var deleteBtn = UIFactory.CreateIconButton("\uE74D", Loc.T("Research_Delete"), (_, _) =>
        {
            ViewModel.DeleteObservationCommand.Execute(null);
            EmptyState.Visibility = Visibility.Visible;
            DetailPanel.Visibility = Visibility.Collapsed;
            RefreshList();
        });
        btnPanel.Children.Add(deleteBtn);
        DetailContent.Children.Add(btnPanel);

        // Metadata
        AddRow(Loc.T("Research_RowCollection"), obs.Collection);
        AddRow(Loc.T("Research_RowObservationId"), obs.ObservationID);
        AddRow(Loc.T("Research_RowTarget"), obs.TargetName);
        AddRow(Loc.T("Research_RowInstrument"), obs.Instrument);
        AddRow(Loc.T("Research_RowFilter"), obs.Filter);
        AddRow(Loc.T("Research_RowRa"), obs.RA);
        AddRow(Loc.T("Research_RowDec"), obs.Dec);
        if (!string.IsNullOrEmpty(obs.StartDate))
            AddRow(Loc.T("Research_RowStartDate"), CellFormatter.Format("startdate", obs.StartDate));
        AddRow(Loc.T("Research_RowCalLevel"), CellFormatter.Format("callev", obs.CalLevel));

        // File info
        DetailContent.Children.Add(new TextBlock
        {
            Text = Loc.T("Research_FileInfo"),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            Margin = new Thickness(0, 8, 0, 0)
        });
        AddRow(Loc.T("Research_RowPath"), obs.LocalPath);
        if (obs.FileSize.HasValue) AddRow(Loc.T("Research_RowSize"), obs.FormattedSize);
        AddRow(Loc.T("Research_RowDownloaded"), obs.DownloadedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        AddRow(Loc.T("Research_RowFileExists"), obs.FileExists ? Loc.T("Research_Yes") : Loc.T("Research_Missing"));

        BuildNotesEditor(obs);
    }

    #region Research notes editor

    private void BuildNotesEditor(DownloadedObservation obs)
    {
        // Notes are keyed by publisher ID; without one there's nothing to persist against.
        if (string.IsNullOrEmpty(obs.PublisherID))
        {
            _noteEditId = null;
            return;
        }

        // Build the editor, then load the saved note with change events suppressed so the load
        // itself doesn't trigger an autosave.
        _suppressNoteEvents = true;
        _noteEditId = obs.PublisherID;

        var saved = _noteStore.Get(obs.PublisherID);

        var header = new TextBlock
        {
            Text = Loc.T("Research_NotesTitle"),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        var headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 8, 0, 0),
        };
        headerRow.Children.Add(header);
        // Wand badge when the current note text was written by an agent.
        headerRow.Children.Add(new Controls.AgentBadge { Attribution = saved?.AgentAttribution });
        DetailContent.Children.Add(headerRow);

        _ratingControl = new RatingControl { IsClearEnabled = true, Caption = Loc.T("Research_RatingCaption") };
        _ratingControl.ValueChanged += (_, _) => OnNoteEdited();
        DetailContent.Children.Add(_ratingControl);

        _noteBox = new TextBox
        {
            PlaceholderText = Loc.T("Research_NotePlaceholder"),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 96,
            Header = Loc.T("Research_NoteHeader"),
        };
        _noteBox.TextChanged += (_, _) => OnNoteEdited();
        DetailContent.Children.Add(_noteBox);

        _tagsBox = new TextBox
        {
            PlaceholderText = Loc.T("Research_TagsPlaceholder"),
            Header = Loc.T("Research_TagsHeader"),
        };
        _tagsBox.TextChanged += (_, _) => OnNoteEdited();
        DetailContent.Children.Add(_tagsBox);

        _noteBox.Text = saved?.Note ?? string.Empty;
        _ratingControl.Value = saved is { Rating: > 0 } ? saved.Rating : -1; // -1 = unrated placeholder
        _tagsBox.Text = saved is { Tags.Count: > 0 } ? string.Join(", ", saved.Tags) : string.Empty;

        _suppressNoteEvents = false;
    }

    private void OnNoteEdited()
    {
        if (_suppressNoteEvents) return;
        _noteSaveTimer.Stop();
        _noteSaveTimer.Start(); // debounce
    }

    /// <summary>Persist the editor's current values immediately under the id it is editing.</summary>
    private void SaveNoteNow()
    {
        _noteSaveTimer.Stop();
        if (_noteEditId is null || _noteBox is null || _ratingControl is null || _tagsBox is null) return;

        var rating = _ratingControl.Value < 0 ? 0 : (int)Math.Round(_ratingControl.Value);
        var tags = _tagsBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        _noteStore.Upsert(new ObservationNote
        {
            PublisherID = _noteEditId,
            Note = _noteBox.Text.Trim(),
            Rating = rating,
            Tags = tags,
            UpdatedUtc = DateTimeOffset.UtcNow,
        });
    }

    /// <summary>Flush any pending edit immediately (called before switching observations).</summary>
    private void FlushNote()
    {
        if (_noteSaveTimer.IsEnabled) SaveNoteNow();
    }

    #endregion

    private void AddRow(string label, string value)
    {
        var row = UIFactory.CreateMetadataRow(label, value, 130);
        if (row is not null) DetailContent.Children.Add(row);
    }

    /// <summary>PublisherIDs whose DataLink lookup already ran this session (with or without a
    /// preview found) — prevents a re-fetch per selection change for previewless/offline records.</summary>
    private readonly HashSet<string> _previewLookupDone = new(StringComparer.Ordinal);

    /// <summary>DataLink lookup for records saved without preview URLs (older records, MCP-agent
    /// downloads). Mutates the in-memory record so the session skips repeat lookups; deliberately no
    /// store.Save here — that fires Changed and would rebuild the list mid-selection.</summary>
    private async Task ResolvePreviewViaDataLinkAsync(DownloadedObservation obs, StackPanel container, ProgressRing spinner, CancellationToken ct)
    {
        try
        {
            var links = await ViewModel.DataLink.GetLinksAsync(obs.PublisherID);
            _previewLookupDone.Add(obs.PublisherID); // success or no-preview: don't re-fetch this session
            if (ct.IsCancellationRequested) return;
            obs.PreviewURL = links.Previews.FirstOrDefault();
            obs.ThumbnailURL = links.Thumbnails.FirstOrDefault();

            var url = obs.PreviewURL ?? obs.ThumbnailURL;
            if (url is null)
            {
                spinner.IsActive = false;
                container.Visibility = Visibility.Collapsed; // no preview exists for this observation
                return;
            }
            await LoadPreviewAsync(url, container, spinner, ct);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Preview DataLink lookup failed: {ex.Message}");
            spinner.IsActive = false;
            container.Visibility = Visibility.Collapsed;
        }
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
