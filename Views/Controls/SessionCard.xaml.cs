using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using CanfarDesktop.Models;

namespace CanfarDesktop.Views.Controls;

public sealed partial class SessionCard : UserControl
{
    public event EventHandler<string>? OpenRequested;
    public event EventHandler<string>? RenewRequested;
    public event EventHandler<string>? EventsRequested;
    public event EventHandler<string>? DeleteRequested;

    private Session? _session;

    public SessionCard()
    {
        InitializeComponent();
    }

    public void Bind(Session session)
    {
        _session = session;
        NameText.Text = session.SessionName;
        StatusText.Text = session.Status;
        ImageText.Text = ParseImageLabel(session.ContainerImage);
        StartText.Text = Helpers.Loc.F("Sessions_Started", FormatTime(session.StartedTime));
        ExpiryText.Text = Helpers.Loc.F("Sessions_Expires", FormatTime(session.ExpiresTime));
        CpuText.Text = Helpers.Loc.F("Portal_CpuCount", FormatResource(session.CpuAllocated));
        RamText.Text = Helpers.Loc.F("Sessions_Ram", FormatResource(session.MemoryAllocated));

        // Status badge color
        StatusBadge.Background = session.Status switch
        {
            "Running" => new SolidColorBrush(ColorHelper.FromArgb(255, 76, 175, 80)),
            "Pending" => new SolidColorBrush(ColorHelper.FromArgb(255, 255, 152, 0)),
            "Failed" or "Error" => new SolidColorBrush(ColorHelper.FromArgb(255, 244, 67, 54)),
            _ => new SolidColorBrush(ColorHelper.FromArgb(255, 158, 158, 158))
        };

        // Type badge color + image
        var (typeColor, typeImageFile, typeLabel) = session.SessionType switch
        {
            "notebook" => (ColorHelper.FromArgb(255, 33, 150, 243), "session-notebook.jpg", Helpers.Loc.T("Sessions_TypeNotebook")),
            "desktop" => (ColorHelper.FromArgb(255, 103, 58, 183), "session-desktop.png", Helpers.Loc.T("Sessions_TypeDesktop")),
            "carta" => (ColorHelper.FromArgb(255, 0, 150, 136), "session-carta.png", "CARTA"), // brand name
            "contributed" => (ColorHelper.FromArgb(255, 255, 87, 34), "session-contributed.png", Helpers.Loc.T("Sessions_TypeContrib")),
            "firefly" => (ColorHelper.FromArgb(255, 255, 152, 0), "session-firefly.png", "Firefly"), // brand name
            "headless" => (ColorHelper.FromArgb(255, 96, 125, 139), "session-desktop.png", Helpers.Loc.T("Sessions_TypeHeadless")),
            _ => (ColorHelper.FromArgb(255, 158, 158, 158), "session-desktop.png", session.SessionType)
        };

        TypeBadge.Background = new SolidColorBrush(typeColor);
        TypeText.Text = typeLabel;
        TypeImage.Source = new BitmapImage(new Uri($"ms-appx:///Assets/{typeImageFile}"));

        OpenButton.IsEnabled = session.Status == "Running";
        RenewButton.IsEnabled = session.Status == "Running";
    }

    private static string ParseImageLabel(string imageId)
    {
        var lastSlash = imageId.LastIndexOf('/');
        return lastSlash >= 0 ? imageId[(lastSlash + 1)..] : imageId;
    }

    private static string FormatResource(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? Helpers.Loc.T("Sessions_NotAvailable") : value;
    }

    private static string FormatTime(string? time)
    {
        if (DateTime.TryParse(time, out var dt))
            return dt.ToLocalTime().ToString("MMM dd HH:mm");
        return time ?? "";
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if (_session is not null) OpenRequested?.Invoke(this, _session.Id);
    }

    private void OnRenewClick(object sender, RoutedEventArgs e)
    {
        if (_session is not null) RenewRequested?.Invoke(this, _session.Id);
    }

    private void OnEventsClick(object sender, RoutedEventArgs e)
    {
        if (_session is not null) EventsRequested?.Invoke(this, _session.Id);
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_session is not null) DeleteRequested?.Invoke(this, _session.Id);
    }
}
