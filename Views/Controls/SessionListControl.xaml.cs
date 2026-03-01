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

    public SessionListControl(SessionListViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ViewModel.Sessions.CollectionChanged += (_, _) => RebuildCards();
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

    private void RebuildCards()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            SessionsPanel.Children.Clear();
            if (ViewModel.Sessions.Count == 0)
            {
                SessionsPanel.Children.Add(new TextBlock
                {
                    Text = "No active sessions.",
                    Opacity = 0.6,
                    Margin = new Thickness(0, 16, 0, 0)
                });
                return;
            }
            foreach (var session in ViewModel.Sessions)
            {
                var card = new SessionCard();
                card.Bind(session);
                card.OpenRequested += (_, id) => SessionOpenRequested?.Invoke(this, id);
                card.DeleteRequested += (_, id) => SessionDeleteRequested?.Invoke(this, id);
                card.RenewRequested += (_, id) => SessionRenewRequested?.Invoke(this, id);
                card.EventsRequested += (_, id) => SessionEventsRequested?.Invoke(this, id);
                SessionsPanel.Children.Add(card);
            }
        });
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
