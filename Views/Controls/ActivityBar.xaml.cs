using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Views.Controls;

/// <summary>
/// The status bar over <see cref="TaskRegistry"/> — see ActivityBar.xaml for what it is for.
///
/// Two things it deliberately does not do. It does not rebuild on every change: a catalogue sweep can
/// move the registry hundreds of times a second, so changes are coalesced onto one UI-thread pass. And
/// it does not build the expanded list while the list is closed, which is most of the time.
/// </summary>
public sealed partial class ActivityBar : UserControl
{
    /// <summary>
    /// How often a running task's "3m 7s" is redrawn.
    ///
    /// Only while something is running, and only when the list is open — the whole reason to show an
    /// elapsed time is to separate a slow task from a stuck one, and that reading does not need to be
    /// per-frame.
    /// </summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private readonly DispatcherTimer _tick = new() { Interval = TickInterval };
    private bool _expanded;
    private long _renderedSequence = -1;
    private int _refreshPending;

    public ActivityBar()
    {
        InitializeComponent();

        _tick.Tick += (_, _) => Render();

        Loaded += (_, _) =>
        {
            TaskRegistry.Changed += OnRegistryChanged;
            Render();
        };

        Unloaded += (_, _) =>
        {
            TaskRegistry.Changed -= OnRegistryChanged;
            _tick.Stop();
        };
    }

    /// <summary>
    /// Raised from whatever thread did the work. Coalesced: a burst of changes queues ONE pass, because
    /// a sweep that moves the registry three hundred times must not queue three hundred layouts.
    /// </summary>
    private void OnRegistryChanged()
    {
        if (Interlocked.Exchange(ref _refreshPending, 1) != 0) return;

        try
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                Interlocked.Exchange(ref _refreshPending, 0);
                Render();
            });
        }
        catch
        {
            // The window is going away. Let the next pass — if there is one — try again.
            Interlocked.Exchange(ref _refreshPending, 0);
        }
    }

    private void Render()
    {
        var tasks = TaskRegistry.Snapshot();
        var running = tasks.Count(t => !t.IsFinished);

        SummaryText.Text = ActivitySummary.Line(tasks);
        Dots.IsRunning = running > 0;

        var failures = ActivitySummary.Failures(tasks);
        FailuresText.Text = failures ?? string.Empty;
        FailuresText.Visibility = failures is null ? Visibility.Collapsed : Visibility.Visible;

        // Tick only while there is an elapsed time on screen that is still growing.
        if (running > 0 && _expanded) _tick.Start();
        else _tick.Stop();

        if (!_expanded) return;

        // The list is rebuilt only when the registry has actually moved. A timer tick that finds nothing
        // changed still needs the elapsed times redrawn, though — which is why this compares sequences
        // rather than skipping the whole pass.
        if (TaskRegistry.Sequence != _renderedSequence || running > 0)
        {
            _renderedSequence = TaskRegistry.Sequence;
            RebuildList(tasks);
        }
    }

    private void RebuildList(IReadOnlyList<TrackedTask> tasks)
    {
        TaskList.Items.Clear();
        foreach (var line in ActivitySummary.Lines(tasks))
            TaskList.Items.Add(Row(line));

        EmptyText.Visibility = tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.Visibility = tasks.Any(t => t.IsFinished) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>One task: what it is, and where it got to.</summary>
    private static UIElement Row(TaskLine line)
    {
        var icon = new FontIcon
        {
            Glyph = GlyphFor(line.Progress),
            FontSize = 12,
            Foreground = BrushFor(line.Progress),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
        };

        var title = new TextBlock
        {
            Text = line.Title,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var detail = new TextBlock
        {
            Text = line.Detail,
            // A failure's reason can be a paragraph from a service, and truncating the one thing the
            // reader opened this list to find out would defeat the point.
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = BrushFor(line.Progress),
            IsTextSelectionEnabled = true,
        };

        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(title);
        text.Children.Add(detail);

        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(text, 1);
        row.Children.Add(icon);
        row.Children.Add(text);

        return row;
    }

    private static string GlyphFor(TaskProgress progress) => progress switch
    {
        TaskProgress.Succeeded => "",    // check
        TaskProgress.Failed => "",       // error badge
        TaskProgress.Cancelled => "",    // cancel
        _ => "",                         // stopwatch
    };

    private static Brush BrushFor(TaskProgress progress)
    {
        var key = progress switch
        {
            TaskProgress.Succeeded => "SystemFillColorSuccessBrush",
            TaskProgress.Failed => "SystemFillColorCriticalBrush",
            TaskProgress.Cancelled => "SystemFillColorCautionBrush",
            _ => "TextFillColorSecondaryBrush",
        };

        return Application.Current.Resources[key] as Brush
            ?? (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
    }

    private void OnToggleExpanded(object sender, RoutedEventArgs e)
    {
        _expanded = !_expanded;
        DetailScroller.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        ExpandIcon.Glyph = _expanded ? "" : "";   // chevron down when open, up when closed

        // The list was not being kept up to date while it was closed, so it is stale by definition.
        _renderedSequence = -1;
        Render();
    }

    private void OnClearFinished(object sender, RoutedEventArgs e)
    {
        TaskRegistry.ClearFinished();
        _renderedSequence = -1;
        Render();
    }
}
