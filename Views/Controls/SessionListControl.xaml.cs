using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Models;
using CanfarDesktop.ViewModels;

namespace CanfarDesktop.Views.Controls;

public sealed partial class SessionListControl : UserControl
{
    public SessionListViewModel ViewModel { get; }

    public event EventHandler<string>? SessionOpenRequested;
    public event EventHandler<string>? SessionDeleteRequested;
    public event EventHandler<string>? SessionRenewRequested;
    public event EventHandler<string>? SessionEventsRequested;

    // Live cards keyed by session id: poll refreshes re-bind the existing card
    // instead of destroying and recreating it, so hover/focus and the strip's
    // horizontal scroll position survive every tick.
    private readonly Dictionary<string, SessionCard> _cards = [];
    private bool _reconcileQueued;

    public SessionListControl(SessionListViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ViewModel.Sessions.CollectionChanged += (_, _) => ScheduleReconcile();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.IsPolling))
            {
                DispatcherQueue.TryEnqueue(() =>
                    PollingIndicator.Visibility = ViewModel.IsPolling ? Visibility.Visible : Visibility.Collapsed);
            }
            else if (e.PropertyName == nameof(ViewModel.PollCountdown))
            {
                DispatcherQueue.TryEnqueue(() =>
                    CountdownText.Text = $"{ViewModel.PollCountdown}s");
            }
        };
    }

    private void ScheduleReconcile()
    {
        // Coalesce the burst of collection events from one refresh into one pass.
        if (_reconcileQueued) return;
        _reconcileQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _reconcileQueued = false;
            ReconcileCards();
        });
    }

    private void ReconcileCards()
    {
        var sessions = ViewModel.Sessions.ToList();
        var live = new HashSet<string>(sessions.Select(s => s.Id));

        // Drop cards whose session is gone, and any empty-state placeholder.
        for (var i = SessionsPanel.Children.Count - 1; i >= 0; i--)
        {
            if (SessionsPanel.Children[i] is SessionCard card)
            {
                if (card.Tag is not string id || !live.Contains(id))
                {
                    SessionsPanel.Children.RemoveAt(i);
                    if (card.Tag is string gone) _cards.Remove(gone);
                }
            }
            else
            {
                SessionsPanel.Children.RemoveAt(i);
            }
        }

        if (sessions.Count == 0)
        {
            _cards.Clear();
            SessionsPanel.Children.Add(new TextBlock
            {
                Text = "No active sessions.",
                Opacity = 0.6,
                Margin = new Thickness(0, 16, 0, 0)
            });
            return;
        }

        for (var i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            if (_cards.TryGetValue(session.Id, out var card))
            {
                card.Bind(session);
                var current = SessionsPanel.Children.IndexOf(card);
                if (current != i)
                {
                    SessionsPanel.Children.RemoveAt(current);
                    SessionsPanel.Children.Insert(i, card);
                }
            }
            else
            {
                card = new SessionCard { Tag = session.Id };
                card.Bind(session);
                card.OpenRequested += (_, id) => SessionOpenRequested?.Invoke(this, id);
                card.DeleteRequested += (_, id) => SessionDeleteRequested?.Invoke(this, id);
                card.RenewRequested += (_, id) => SessionRenewRequested?.Invoke(this, id);
                card.EventsRequested += (_, id) => SessionEventsRequested?.Invoke(this, id);
                _cards[session.Id] = card;
                SessionsPanel.Children.Insert(Math.Min(i, SessionsPanel.Children.Count), card);
            }
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadSessionsCommand.ExecuteAsync(null);
    }

    public async Task LoadAsync()
    {
        await ViewModel.LoadSessionsCommand.ExecuteAsync(null);
    }
}
