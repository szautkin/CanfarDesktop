using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models.Cutouts;

namespace CanfarDesktop.Views.Controls;

/// <summary>
/// A "Cutout" pill beside a Research entry that is part of an observation rather than the whole of
/// it, with what it covers in its tooltip. Hidden for a complete observation — the same shape as
/// <see cref="AgentBadge"/>, which marks where an entry came from as this marks what it is.
/// </summary>
public sealed class CutoutBadge : UserControl
{
    public static readonly DependencyProperty CutoutProperty = DependencyProperty.Register(
        nameof(Cutout), typeof(CutoutSpec), typeof(CutoutBadge), new PropertyMetadata(null, OnCutoutChanged));

    private readonly TextBlock _text = new() { FontSize = 10 };

    public CutoutBadge()
    {
        Visibility = Visibility.Collapsed;
        VerticalAlignment = VerticalAlignment.Center;
        _text.Text = Loc.T("Research_CutoutBadge");
        _text.Foreground = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"];
        Content = new Border
        {
            Child = _text,
            Padding = new Thickness(6, 1, 6, 1),
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
        };
    }

    public CutoutSpec? Cutout
    {
        get => (CutoutSpec?)GetValue(CutoutProperty);
        set => SetValue(CutoutProperty, value);
    }

    private static void OnCutoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var badge = (CutoutBadge)d;
        if (e.NewValue is not CutoutSpec cutout)
        {
            badge.Visibility = Visibility.Collapsed;
            return;
        }

        var tip = Loc.F("Research_CutoutBadgeTip", cutout.Summary);
        ToolTipService.SetToolTip(badge, tip);
        AutomationProperties.SetName(badge, tip);
        badge.Visibility = Visibility.Visible;
    }
}
