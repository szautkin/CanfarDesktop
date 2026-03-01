using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CanfarDesktop.Views.Controls;

public sealed partial class MetricBar : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(MetricBar), new PropertyMetadata("", OnPropertyChanged));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(MetricBar), new PropertyMetadata(0.0, OnPropertyChanged));

    public static readonly DependencyProperty MaxValueProperty =
        DependencyProperty.Register(nameof(MaxValue), typeof(double), typeof(MetricBar), new PropertyMetadata(100.0, OnPropertyChanged));

    public static readonly DependencyProperty PercentProperty =
        DependencyProperty.Register(nameof(Percent), typeof(double), typeof(MetricBar), new PropertyMetadata(0.0, OnPropertyChanged));

    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(MetricBar), new PropertyMetadata("", OnPropertyChanged));

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double MaxValue { get => (double)GetValue(MaxValueProperty); set => SetValue(MaxValueProperty, value); }
    public double Percent { get => (double)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    public MetricBar()
    {
        InitializeComponent();
    }

    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MetricBar bar) bar.UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        LabelText.Text = Label;
        ValueText.Text = $"{Value:F1} / {MaxValue:F1} {Unit} ({Percent:F0}%)";
        Bar.Value = Percent;
    }
}
