using System.ComponentModel;
using System.Text;
using CanfarDesktop.Services;
using CanfarDesktop.Services.Workflows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CanfarDesktop.Views;

/// <summary>One step card in the rendered workflow (INPC for the done/current visuals).</summary>
public sealed class WorkflowStepRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; init; }
    public string Number => (Index + 1).ToString();
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
    public IReadOnlyList<string> Tools { get; init; } = Array.Empty<string>();
    public string ViewKey { get; init; } = string.Empty;
    public string ViewLinkText { get; init; } = string.Empty;
    public bool CanToggle { get; init; }
    public string ToggleTooltip => CanToggle
        ? "Mark this step done / not done"
        : "Read-only source — press “Use this workflow” to track progress";

    public Visibility BodyVisibility => Body.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoteVisibility => Note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ViewLinkVisibility => ViewKey.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    private bool _done;
    public bool Done
    {
        get => _done;
        set { if (_done == value) return; _done = value; NotifyStateChanged(); }
    }

    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set { if (_isCurrent == value) return; _isCurrent = value; NotifyStateChanged(); }
    }

    // Derived visuals the DataTemplate binds OneWay.
    public Visibility NumberVisibility => Done ? Visibility.Collapsed : Visibility.Visible;
    public Visibility CheckVisibility => Done ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CurrentPillVisibility => IsCurrent ? Visibility.Visible : Visibility.Collapsed;
    public Thickness AccentThickness => IsCurrent ? new Thickness(3, 1, 1, 1) : new Thickness(1);
    public double BodyOpacity => Done ? 0.55 : 1.0;
    public Windows.UI.Text.TextDecorations TitleDecorations
        => Done ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None;
    public Brush RoundelBrush => (Brush)Application.Current.Resources[
        Done ? "AccentFillColorDefaultBrush" : "SubtleFillColorSecondaryBrush"];

    private void NotifyStateChanged()
    {
        foreach (var p in new[]
                 {
                     nameof(Done), nameof(IsCurrent), nameof(NumberVisibility), nameof(CheckVisibility),
                     nameof(CurrentPillVisibility), nameof(AccentThickness), nameof(BodyOpacity),
                     nameof(TitleDecorations), nameof(RoundelBrush),
                 })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}

/// <summary>
/// The Workflows landing surface: built-in templates + local working copies + the user's
/// <c>vos:&lt;user&gt;/workflows/</c> folder, rendered as check-off step cards. Check-off state is
/// only ever written to LOCAL files (the file is the state); templates and VOSpace items are
/// read-only sources instantiated via "Use this workflow". Refreshes on <see cref="WorkflowStore.Changed"/>
/// (agent writes included) with a coalesced dispatch.
/// </summary>
public sealed partial class WorkflowsPage : UserControl
{
    private sealed class ListItem
    {
        public required string Id { get; init; }
        public required string Display { get; init; }
        public override string ToString() => Display;
    }

    private readonly WorkflowStore _store;
    private readonly IStorageService _storage;
    private readonly IAuthService _auth;
    private readonly Dictionary<string, string> _vosTexts = new(StringComparer.OrdinalIgnoreCase);

    private string? _selectedId;
    private string? _followId;            // last agent/user-mutated id, applied on the next refresh
    private string? _editingId;           // null while creating a brand-new workflow
    private bool _suppressSelection;
    private int _refreshPending;
    private DispatcherTimer? _previewDebounce;

    /// <summary>MainWindow routes these through NavigateByKey (step View: deep-links).</summary>
    public event Action<string>? NavigateRequested;

    public WorkflowsPage(WorkflowStore store, IStorageService storage, IAuthService auth)
    {
        InitializeComponent();
        _store = store;
        _storage = storage;
        _auth = auth;
        PreviewSteps.ItemTemplate = StepsList.ItemTemplate;

        // Coalesced pull refresh on any mutation (user click or agent tool) — never clear+rebuild
        // blindly. When the change targets a workflow that isn't selected (an agent working on it),
        // follow it: select that workflow so the user watches the roundels flip live.
        _store.Changed += changedId =>
        {
            if (changedId is not null) _followId = changedId;
            if (System.Threading.Interlocked.Exchange(ref _refreshPending, 1) == 1) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                System.Threading.Interlocked.Exchange(ref _refreshPending, 0);
                if (_followId is { } follow)
                {
                    _followId = null;
                    if (_selectedId is null || _selectedId.StartsWith(WorkflowStore.BuiltInPrefix, StringComparison.Ordinal)
                        || !_selectedId.Equals(follow, StringComparison.OrdinalIgnoreCase))
                        _selectedId = follow;
                }
                RefreshLists();
                RenderDetail();
            });
        };

        RefreshLists();
        VosHint.Text = "Press refresh to list vos:<you>/workflows.";
    }

    // ── Lists ─────────────────────────────────────────────────────────────────

    private void RefreshLists()
    {
        _suppressSelection = true;
        try
        {
            BuiltInList.ItemsSource = _store.ListBuiltIn()
                .Select(w => new ListItem { Id = w.Id, Display = w.Doc.Title }).ToList();

            var locals = _store.ListLocal();
            LocalList.ItemsSource = locals
                .Select(w => new ListItem { Id = w.Id, Display = $"{w.Doc.Title}  ({w.Doc.DoneCount}/{w.Doc.Steps.Count})" }).ToList();
            LocalEmptyHint.Visibility = locals.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            ReselectById();
        }
        finally
        {
            _suppressSelection = false;
        }
    }

    private void ReselectById()
    {
        foreach (var list in new[] { BuiltInList, LocalList, VosList })
        {
            var match = (list.ItemsSource as IEnumerable<ListItem>)?.FirstOrDefault(i => i.Id == _selectedId);
            list.SelectedItem = match;
        }
    }

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || sender is not ListView lv) return;
        if (lv.SelectedItem is not ListItem item) return;

        _suppressSelection = true;
        foreach (var other in new[] { BuiltInList, LocalList, VosList })
            if (!ReferenceEquals(other, lv)) other.SelectedItem = null;
        _suppressSelection = false;

        _selectedId = item.Id;
        ExitEditMode();
        RenderDetail();
    }

    /// <summary>Follow-agent-activity: select (and render) the workflow the agent is acting on.</summary>
    public void SelectWorkflow(string id)
    {
        _selectedId = id;
        RefreshLists();
        RenderDetail();
    }

    // ── Detail render ─────────────────────────────────────────────────────────

    private (WorkflowInfo Info, string RawText)? ResolveSelected()
    {
        if (_selectedId is null) return null;
        if (_selectedId.StartsWith("vos:", StringComparison.OrdinalIgnoreCase))
        {
            if (!_vosTexts.TryGetValue(_selectedId, out var text)) return null; // still loading
            return (new WorkflowInfo(_selectedId, WorkflowSource.VoSpace, WorkflowFormat.Parse(text), text), text);
        }
        var info = _store.Get(_selectedId);
        return info is null ? null : (info, info.RawText);
    }

    private void RenderDetail()
    {
        if (EditPane.Visibility == Visibility.Visible) return; // don't fight the editor

        var resolved = ResolveSelected();
        if (resolved is null)
        {
            DetailPane.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            if (_selectedId?.StartsWith("vos:", StringComparison.OrdinalIgnoreCase) == true)
                _ = LoadVosTextAsync(_selectedId);
            return;
        }

        var (info, _) = resolved.Value;
        EmptyState.Visibility = Visibility.Collapsed;
        DetailPane.Visibility = Visibility.Visible;

        DetailTitle.Text = info.Doc.Title;
        DetailDescription.Text = info.Doc.Description;
        DetailDescription.Visibility = info.Doc.Description.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        TagChips.ItemsSource = info.Doc.Tags;
        TimeText.Text = info.Doc.Metadata.TryGetValue("Time", out var t) ? t : string.Empty;
        SourceBadgeText.Text = info.Source switch
        {
            WorkflowSource.BuiltIn => "Built-in template",
            WorkflowSource.VoSpace => "VOSpace",
            _ => "Local",
        };

        var local = info.Source == WorkflowSource.Local;
        ProgressBarCtl.Maximum = Math.Max(1, info.Doc.Steps.Count);
        ProgressBarCtl.Value = info.Doc.DoneCount;
        ProgressText.Text = $"{info.Doc.DoneCount}/{info.Doc.Steps.Count}";
        ProgressBarCtl.Visibility = ProgressText.Visibility = local ? Visibility.Visible : Visibility.Collapsed;

        StepsList.ItemsSource = BuildRows(info.Doc, canToggle: local);

        UseButton.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
        EditButton.Visibility = DeleteButton.Visibility = PublishButton.Visibility =
            local ? Visibility.Visible : Visibility.Collapsed;
        DuplicateButton.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
    }

    private static List<WorkflowStepRow> BuildRows(WorkflowDoc doc, bool canToggle)
    {
        var current = doc.Steps.FirstOrDefault(s => !s.Done)?.Index ?? -1;
        return doc.Steps.Select(s => new WorkflowStepRow
        {
            Index = s.Index,
            Title = s.Title,
            Body = s.Body,
            Note = s.Note ?? string.Empty,
            Tools = s.Tools,
            ViewKey = s.View ?? string.Empty,
            ViewLinkText = s.View is { Length: > 0 } v ? $"→ {TitleForView(v)}" : string.Empty,
            CanToggle = canToggle,
            Done = s.Done,
            IsCurrent = canToggle && s.Index == current,
        }).ToList();
    }

    private static string TitleForView(string key) => key switch
    {
        "portal" => "Portal", "search" => "Search", "research" => "Research", "storage" => "Storage",
        "notebook" => "Notebook", "fitsViewer" => "FITS Viewer", "aiGuide" => "AI Guide",
        "workflows" => "Workflows", _ => key,
    };

    private async Task LoadVosTextAsync(string vosId)
    {
        try
        {
            var remotePath = vosId["vos:".Length..];
            using var stream = await _storage.DownloadFileAsync(remotePath);
            using var reader = new StreamReader(stream);
            _vosTexts[vosId] = await reader.ReadToEndAsync();
            if (_selectedId == vosId) RenderDetail();
        }
        catch (Exception ex)
        {
            VosHint.Text = $"Could not open the VOSpace workflow: {ex.Message}";
        }
    }

    // ── Step interactions ─────────────────────────────────────────────────────

    private void OnStepToggleClick(object sender, RoutedEventArgs e)
    {
        if (_selectedId is null || sender is not FrameworkElement fe || fe.Tag is not int index) return;
        var info = _store.Get(_selectedId);
        if (info is null || info.Source != WorkflowSource.Local) return;
        try
        {
            _store.SetStepDone(_selectedId, index, !info.Doc.Steps[index].Done); // Changed → refresh
        }
        catch (Exception ex)
        {
            VosHint.Text = ex.Message;
        }
    }

    private void OnToolChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string tool) return;
        var package = new DataPackage();
        package.SetText(tool);
        Clipboard.SetContent(package);
    }

    private void OnViewLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string key && key.Length > 0)
            NavigateRequested?.Invoke(key);
    }

    // ── Toolbar actions ───────────────────────────────────────────────────────

    private void OnUseClick(object sender, RoutedEventArgs e)
    {
        var resolved = ResolveSelected();
        if (resolved is null) return;
        var (info, raw) = resolved.Value;
        _selectedId = info.Source == WorkflowSource.VoSpace
            ? _store.UseText(raw, info.Doc.Title)
            : _store.UseWorkflow(info.Id);
        // Changed already queued the refresh; ensure the new local copy is the selection.
        RefreshLists();
        RenderDetail();
    }

    private void OnDuplicateClick(object sender, RoutedEventArgs e)
    {
        var info = _selectedId is null ? null : _store.Get(_selectedId);
        if (info is null) return;
        _selectedId = _store.SaveNew(info.Doc.Title + " copy", info.RawText);
        RefreshLists();
        RenderDetail();
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_selectedId is null || !_selectedId.StartsWith(WorkflowStore.LocalPrefix, StringComparison.Ordinal)) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete workflow?",
            Content = "This deletes the local file, including its progress. Templates it was copied from are unaffected.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _store.Delete(_selectedId);
            _selectedId = null;
        }
    }

    private void OnNewClick(object sender, RoutedEventArgs e)
    {
        _editingId = null;
        EnterEditMode(WorkflowFormat.Skeleton("New workflow"));
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        var info = _selectedId is null ? null : _store.Get(_selectedId);
        if (info is null || info.Source != WorkflowSource.Local) return;
        _editingId = info.Id;
        EnterEditMode(info.RawText);
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        var hwnd = WindowHelper.ActiveWindows.Count > 0
            ? WindowNative.GetWindowHandle(WindowHelper.ActiveWindows[0]) : nint.Zero;
        if (hwnd == nint.Zero) return;
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".md");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        var text = await Windows.Storage.FileIO.ReadTextAsync(file);
        var doc = WorkflowFormat.Parse(text);
        _selectedId = _store.SaveNew(doc.Title, text);
        RefreshLists();
        RenderDetail();
    }

    private async void OnPublishClick(object sender, RoutedEventArgs e)
    {
        var info = _selectedId is null ? null : _store.Get(_selectedId);
        if (info is null || info.Source != WorkflowSource.Local) return;
        var user = (_auth.CurrentUsername ?? string.Empty).Trim();
        if (user.Length == 0) { VosHint.Text = "Sign in to publish workflows to VOSpace."; return; }

        var reset = new CheckBox { Content = "Reset progress in the published copy", IsChecked = true };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Publish to VOSpace",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = $"Uploads to vos:{user}/workflows/{WorkflowStore.Slugify(info.Doc.Title)}{WorkflowFormat.FileExtension}", TextWrapping = TextWrapping.Wrap },
                    reset,
                },
            },
            PrimaryButtonText = "Publish",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            var text = info.RawText;
            if (reset.IsChecked == true)
                for (var i = 0; i < info.Doc.Steps.Count; i++)
                    text = WorkflowFormat.WithStepDone(text, i, false);

            try { await _storage.CreateFolderAsync(user, "workflows"); }
            catch { /* folder probably exists — the upload below is the real test */ }

            var remote = $"{user}/workflows/{WorkflowStore.Slugify(info.Doc.Title)}{WorkflowFormat.FileExtension}";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            await _storage.UploadFileAsync(remote, stream, "text/markdown");
            VosHint.Text = $"Published vos:{remote}";
        }
        catch (Exception ex)
        {
            VosHint.Text = $"Publish failed: {ex.Message}";
        }
    }

    private void OnCopyPromptClick(object sender, RoutedEventArgs e)
    {
        var info = _selectedId is null ? null : _store.Get(_selectedId);
        if (info is null) return;
        var package = new DataPackage();
        package.SetText(
            $"Follow my workflow \"{info.Doc.Title}\" in Verbinal: call get_workflow(id: \"{info.Id}\") to read the steps, " +
            "work through them in order using the tools each step names, mark each finished step with " +
            $"set_workflow_step(id: \"{info.Id}\", index, done: true), and stop to ask me at any judgment call.");
        Clipboard.SetContent(package);
        VosHint.Text = "Agent prompt copied — paste it into Claude.";
    }

    // ── VOSpace listing ───────────────────────────────────────────────────────

    private bool _vosBusy;

    private async void OnVosRefreshClick(object sender, RoutedEventArgs e)
    {
        if (_vosBusy) return;
        var user = (_auth.CurrentUsername ?? string.Empty).Trim();
        if (user.Length == 0) { VosHint.Text = "Sign in to browse VOSpace workflows."; return; }
        _vosBusy = true;
        VosHint.Text = "Loading…";
        try
        {
            var nodes = await _storage.ListNodesAsync($"{user}/workflows", 500);
            var items = nodes
                .Where(n => n.Name.EndsWith(WorkflowFormat.FileExtension, StringComparison.OrdinalIgnoreCase))
                .Select(n => new ListItem { Id = $"vos:{user}/workflows/{n.Name}", Display = n.Name })
                .ToList();
            _suppressSelection = true;
            VosList.ItemsSource = items;
            _suppressSelection = false;
            ReselectById();
            VosHint.Text = items.Count == 0 ? "No .workflow.md files in vos:" + user + "/workflows yet." : string.Empty;
        }
        catch (Exception ex)
        {
            VosHint.Text = $"Could not list VOSpace workflows: {ex.Message}";
        }
        finally
        {
            _vosBusy = false;
        }
    }

    // ── Editor ────────────────────────────────────────────────────────────────

    private void EnterEditMode(string text)
    {
        DetailPane.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Collapsed;
        EditPane.Visibility = Visibility.Visible;
        EditorBox.Text = text;
        UpdatePreview();
    }

    private void ExitEditMode()
    {
        if (EditPane.Visibility != Visibility.Visible) return;
        EditPane.Visibility = Visibility.Collapsed;
    }

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        _previewDebounce ??= CreatePreviewDebounce();
        _previewDebounce.Stop();
        _previewDebounce.Start();
    }

    private DispatcherTimer CreatePreviewDebounce()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) => { timer.Stop(); UpdatePreview(); };
        return timer;
    }

    private void UpdatePreview()
    {
        var doc = WorkflowFormat.Parse(EditorBox.Text);
        PreviewTitle.Text = doc.Title;
        PreviewSteps.ItemsSource = BuildRows(doc, canToggle: false);
        var problems = WorkflowFormat.Validate(doc, WorkflowFormat.KnownViews, Services.AiGuide.AiGuideCatalog.MappedToolNames);
        EditorWarnings.Text = string.Join("\n", problems);
    }

    private void OnSaveEditClick(object sender, RoutedEventArgs e)
    {
        var text = EditorBox.Text;
        var doc = WorkflowFormat.Parse(text);
        if (_editingId is not null) _store.UpdateText(_editingId, text);
        else _selectedId = _store.SaveNew(doc.Title, text);
        ExitEditMode();
        RefreshLists();
        RenderDetail();
    }

    private void OnCancelEditClick(object sender, RoutedEventArgs e)
    {
        ExitEditMode();
        RenderDetail();
    }
}
