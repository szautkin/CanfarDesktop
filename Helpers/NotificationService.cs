using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace CanfarDesktop.Helpers;

public static class NotificationService
{
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        try
        {
            AppNotificationManager.Default.Register();
            _initialized = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Notification init failed: {ex.Message}");
        }
    }

    public static void SendJobCompleted(string sessionName, string image)
    {
        if (!_initialized) return;
        var builder = new AppNotificationBuilder()
            .AddText("Batch Job Completed")
            .AddText($"{sessionName} finished successfully")
            .AddText(ShortImageLabel(image));
        Send(builder);
    }

    public static void SendJobFailed(string sessionName, string image)
    {
        if (!_initialized) return;
        var builder = new AppNotificationBuilder()
            .AddText("Batch Job Failed")
            .AddText($"{sessionName} has failed")
            .AddText(ShortImageLabel(image));
        Send(builder);
    }

    private static void Send(AppNotificationBuilder builder)
    {
        try
        {
            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Notification send failed: {ex.Message}");
        }
    }

    private static string ShortImageLabel(string image)
    {
        var lastSlash = image.LastIndexOf('/');
        return lastSlash >= 0 ? image[(lastSlash + 1)..] : image;
    }
}
