using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace CanfarDesktop.Views.Controls;

/// <summary>
/// Three travelling dots — see WorkingDots.xaml for why they move at all.
///
/// This half is the wave, and when it runs. Starting twice must not be two animations, and stopping must
/// actually park the dots on the line rather than freeze them mid-rise, because a row stopped halfway
/// through a wave reads as a hang.
/// </summary>
public sealed partial class WorkingDots : UserControl
{
    /// <summary>
    /// One full pass, in seconds. Slow enough to read as a wave rather than as a flicker.
    /// </summary>
    private const double PeriodSeconds = 1.15;

    /// <summary>How far a dot rises above and falls below the line, in logical pixels.</summary>
    private const double Travel = 2.5;

    private Storyboard? _wave;
    private bool _running;

    public WorkingDots()
    {
        InitializeComponent();

        // A storyboard on an unloaded control keeps a timeline alive for nothing.
        Unloaded += (_, _) => IsRunning = false;
    }

    /// <summary>Whether the wave is running. Setting it to what it already is does nothing.</summary>
    public bool IsRunning
    {
        get => _running;
        set
        {
            if (value == _running) return;
            _running = value;

            if (value) Start();
            else Stop();
        }
    }

    private void Start()
    {
        // Someone who has turned animations off in Windows has said what they think of moving dots, and
        // three still ones still say "working" — the bar's text is what carries the meaning either way.
        if (!AnimationsEnabled()) return;

        try
        {
            _wave ??= BuildWave();
            _wave.Begin();
        }
        catch
        {
            // A storyboard that will not start is a missing animation, not a broken status bar.
        }
    }

    private void Stop()
    {
        // Stop rather than Pause: it restores the base value, so the dots park on the line instead of
        // wherever the last frame left them.
        try { _wave?.Stop(); }
        catch { /* nothing to stop */ }
    }

    /// <summary>
    /// The wave.
    ///
    /// Each dot is a third of a period behind the one before it, which is what makes this read as
    /// something travelling along the row rather than as three dots bouncing together — in phase they
    /// are one thing jumping, and the travel is the whole idea. Written as arithmetic on the period so
    /// the three stay a third apart when the period changes.
    ///
    /// RenderTransform is what is animated, rather than Margin or Canvas.Top: transform animations run
    /// on the composition thread, so the dots keep moving while the UI thread is busy doing the very
    /// work they are reporting on — which is exactly when anyone is looking at them.
    /// </summary>
    private Storyboard BuildWave()
    {
        var wave = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        TranslateTransform[] dots = [Dot0Move, Dot1Move, Dot2Move];

        for (var i = 0; i < dots.Length; i++)
        {
            var rise = new DoubleAnimation
            {
                From = Travel,
                To = -Travel,
                // Half a period up, then AutoReverse brings it back down: one period, one full pass.
                Duration = new Duration(TimeSpan.FromSeconds(PeriodSeconds / 2)),
                AutoReverse = true,
                BeginTime = TimeSpan.FromSeconds(PeriodSeconds * i / dots.Length),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };

            Storyboard.SetTarget(rise, dots[i]);
            Storyboard.SetTargetProperty(rise, "Y");
            wave.Children.Add(rise);
        }

        return wave;
    }

    private static bool AnimationsEnabled()
    {
        try { return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled; }
        catch { return true; }
    }
}
