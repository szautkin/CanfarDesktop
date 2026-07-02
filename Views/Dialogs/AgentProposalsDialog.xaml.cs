using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CanfarDesktop.Mcp;
using CanfarDesktop.Mcp.Agents;
using CanfarDesktop.Mcp.Tools.Proposals;

namespace CanfarDesktop.Views.Dialogs;

/// <summary>
/// The proposal strip: pending agent writes with per-row Apply / Reject and inline errors, plus a
/// History tab over the activity feed. Opens on the History tab when nothing is pending. 1-to-1 with
/// the macOS ProposalStripSheet.
/// </summary>
public sealed partial class AgentProposalsDialog : ContentDialog
{
    private readonly McpHost _host;
    private readonly Action _proposalsChangedHandler;
    private int _refreshPending;

    public AgentProposalsDialog(McpHost host)
    {
        _host = host;
        InitializeComponent();

        // Coalesce event bursts (one store event per mutation) into a single queued refresh.
        _proposalsChangedHandler = () =>
        {
            if (Interlocked.Exchange(ref _refreshPending, 1) == 1) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                Interlocked.Exchange(ref _refreshPending, 0);
                RefreshLists();
            });
        };
        // Subscribe on Opened, not in the constructor: if ShowAsync throws (another ContentDialog
        // is open) Closed never fires, and a constructor-time subscription would root this
        // instance on the singleton host forever.
        Opened += (_, _) => _host.ProposalsChanged += _proposalsChangedHandler;
        Closed += (_, _) => _host.ProposalsChanged -= _proposalsChangedHandler;

        RefreshLists();
        if (_host.PendingProposalCount == 0 && _host.Activity.Count > 0)
            TabsPivot.SelectedItem = HistoryTab;
    }

    public static Task ShowAsync(XamlRoot root, McpHost host)
        => new AgentProposalsDialog(host) { XamlRoot = root }.ShowAsync().AsTask();

    private void RefreshLists()
    {
        var pending = _host.PendingProposals;
        // Preserve rows that are mid-apply so their busy state survives the refresh.
        var existing = (PendingList.ItemsSource as IEnumerable<ProposalRow>)?.ToDictionary(r => r.Proposal.Id)
                       ?? new Dictionary<Guid, ProposalRow>();
        PendingList.ItemsSource = pending
            .Select(p => existing.TryGetValue(p.Id, out var row) ? row : new ProposalRow(p))
            .ToList();
        PendingEmptyState.Visibility = pending.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PendingTab.Header = pending.Count > 0
            ? Helpers.Loc.F("Proposals_PendingCountTab", pending.Count)
            : Helpers.Loc.T("Proposals_PendingTabLabel");
        PendingCountText.Text = Helpers.Loc.F("Proposals_PendingCount", pending.Count);

        var history = _host.Activity.Recent(100).Select(HistoryRow.From).ToList();
        HistoryList.ItemsSource = history;
        HistoryEmptyState.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryTab.Header = history.Count > 0
            ? Helpers.Loc.F("Proposals_HistoryCountTab", history.Count)
            : Helpers.Loc.T("Proposals_HistoryTabLabel");
    }

    private async void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ProposalRow row || row.IsBusy) return;
        row.IsBusy = true;
        row.Error = null;
        try
        {
            await _host.ApplyProposalAsync(row.Proposal.Id);
            // The store change event refreshes the lists (row leaves Pending).
        }
        catch (ProposalApplyException ex)
        {
            row.Error = ex.Message;
        }
        catch (Exception ex)
        {
            row.Error = Helpers.Loc.F("Proposals_ApplyFailed", ex.Message);
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    private void OnRejectClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ProposalRow row || row.IsBusy) return;
        if (!_host.RejectProposal(row.Proposal.Id))
            row.Error = Helpers.Loc.T("Proposals_NoLongerPending");
    }
}

/// <summary>Display row for one pending proposal (mutable busy/error state for the strip).</summary>
public sealed class ProposalRow : INotifyPropertyChanged
{
    public PendingProposal Proposal { get; }

    public ProposalRow(PendingProposal proposal) => Proposal = proposal;

    public string Kind => Proposal.Kind;
    public string Summary => Proposal.Summary;

    public string Meta => string.Join("  ·  ", new[]
    {
        Proposal.ToolName,
        $"{Proposal.Origin.Label} ({AgentActivityEntry.Fingerprint(Proposal.Origin)})",
        Proposal.CreatedAt.ToLocalTime().ToString("t"),
    });

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            Raise(nameof(IsBusy));
            Raise(nameof(BusyVisibility));
            Raise(nameof(ButtonsVisibility));
        }
    }

    private string? _error;
    public string? Error
    {
        get => _error;
        set
        {
            if (_error == value) return;
            _error = value;
            Raise(nameof(Error));
            Raise(nameof(ErrorVisibility));
        }
    }

    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ButtonsVisibility => IsBusy ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ErrorVisibility => string.IsNullOrEmpty(Error) ? Visibility.Collapsed : Visibility.Visible;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Display row for one activity-feed entry on the History tab.</summary>
public sealed class HistoryRow
{
    public string Glyph { get; init; } = string.Empty;
    public Brush GlyphBrush { get; init; } = null!;
    public string Summary { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;

    public static HistoryRow From(AgentActivityEntry e) => new()
    {
        Glyph = char.ConvertFromUtf32(e.Outcome switch
        {
            AgentActivityOutcome.Applied => 0xE73E,
            AgentActivityOutcome.Rejected => 0xE711,
            AgentActivityOutcome.Withdrawn => 0xE7A7,
            _ => 0xE99A,
        }),
        GlyphBrush = (Brush)Application.Current.Resources[e.Outcome switch
        {
            AgentActivityOutcome.Applied => "SystemFillColorSuccessBrush",
            AgentActivityOutcome.Rejected => "SystemFillColorCautionBrush",
            AgentActivityOutcome.Withdrawn => "TextFillColorSecondaryBrush",
            _ => "AccentFillColorDefaultBrush",
        }],
        Summary = e.Summary,
        Subtitle = string.Join("  ·  ", new[]
        {
            e.Kind,
            $"{e.OriginLabel} ({e.OriginFingerprint})",
            e.AutoApplied ? "auto" : null,
            e.Timestamp.ToLocalTime().ToString("g"),
        }.Where(s => !string.IsNullOrEmpty(s))),
    };
}
