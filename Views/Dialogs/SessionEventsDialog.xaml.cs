using Microsoft.UI.Xaml.Controls;

namespace CanfarDesktop.Views.Dialogs;

public sealed partial class SessionEventsDialog : ContentDialog
{
    public SessionEventsDialog()
    {
        InitializeComponent();
    }

    public void SetContent(string events, string title = "Session Events")
    {
        Title = title;
        EventsText.Text = events;
    }
}
