using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace CanfarDesktop.Converters;

public class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value as string ?? "";
        return status switch
        {
            "Running" => new SolidColorBrush(ColorHelper.FromArgb(255, 76, 175, 80)),    // Green
            "Pending" => new SolidColorBrush(ColorHelper.FromArgb(255, 255, 152, 0)),     // Amber
            "Failed" or "Error" => new SolidColorBrush(ColorHelper.FromArgb(255, 244, 67, 54)),  // Red
            "Terminating" => new SolidColorBrush(ColorHelper.FromArgb(255, 158, 158, 158)), // Gray
            _ => new SolidColorBrush(ColorHelper.FromArgb(255, 158, 158, 158))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
