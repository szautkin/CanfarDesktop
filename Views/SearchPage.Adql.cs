using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using CanfarDesktop.Helpers;
using CanfarDesktop.Services;

namespace CanfarDesktop.Views;

/// <summary>
/// The ADQL editor's pre-flight check: read the service's own schema, and refuse to spend a round trip
/// on a query it can already show cannot work.
///
/// The check is the same one <c>validate_adql_query</c> runs, on the same cached schema — the point of
/// a pre-flight is that BOTH ways in get it, and a query an agent sends is a query a person could have
/// typed.
///
/// It is silent until the schema arrives. Greying out Execute for the first second of every session,
/// because a fetch is in flight, would be a worse bug than the one this prevents.
///
/// One deliberate difference from the GTK build: that one draws a wavy underline under the offending
/// words, which a WinUI TextBox cannot do. The same information goes in an InfoBar instead, and each
/// entry SELECTS the text it is about — so the offending word is still one click away.
/// </summary>
public sealed partial class SearchPage
{
    private readonly ITapSchemaService _tapSchema;

    /// <summary>Long enough that a query being typed is not re-checked per keystroke, short enough to feel immediate.</summary>
    private static readonly TimeSpan AdqlCheckDelay = TimeSpan.FromMilliseconds(250);

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _adqlCheckTimer;
    private IReadOnlyList<AdqlProblem> _adqlProblems = [];

    /// <summary>
    /// Warm the schema so the editor has something to check against. Fire-and-forget on purpose: the
    /// page is usable without it, and a failure here must cost the check, never the page. A failed
    /// fetch leaves the cache empty, which the checker reads as "say nothing".
    /// </summary>
    private void BeginSchemaWarmup()
    {
        if (_tapSchema.Cached() is not null) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await _tapSchema.GetSchemaAsync();
                DispatcherQueue.TryEnqueue(() => RecheckAdql());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TAP_SCHEMA warmup failed: {ex.Message}");
            }
        });
    }

    private void OnAdqlTextChanged(object sender, TextChangedEventArgs e) => ScheduleAdqlCheck();

    /// <summary>
    /// Re-check shortly after typing stops. One timer, restarted: checking per keystroke would re-scan
    /// the query for every character of a table name typed one letter at a time, and half a table name
    /// is an unknown table.
    /// </summary>
    private void ScheduleAdqlCheck()
    {
        if (_adqlCheckTimer is null)
        {
            _adqlCheckTimer = DispatcherQueue.CreateTimer();
            _adqlCheckTimer.Interval = AdqlCheckDelay;
            _adqlCheckTimer.IsRepeating = false;
            _adqlCheckTimer.Tick += (_, _) => RecheckAdql();
        }

        _adqlCheckTimer.Stop();
        _adqlCheckTimer.Start();
    }

    /// <summary>
    /// Check the editor's text, mark what is wrong, and grey out Execute if anything is.
    ///
    /// Returns the problems so a caller can also refuse to run.
    /// </summary>
    private IReadOnlyList<AdqlProblem> RecheckAdql()
    {
        var text = AdqlBox.Text ?? string.Empty;

        // Cached(), never GetSchemaAsync(): this runs as the user types, and a check that awaited a
        // network round trip would be one that runs on every keystroke.
        _adqlProblems = AdqlValidator.Problems(text, _tapSchema.Cached());

        AdqlProblemsList.Children.Clear();

        if (_adqlProblems.Count == 0)
        {
            AdqlProblemsBar.IsOpen = false;
            ExecuteAdqlButton.IsEnabled = true;
            ToolTipService.SetToolTip(ExecuteAdqlButton, null);
            return _adqlProblems;
        }

        foreach (var problem in _adqlProblems)
            AdqlProblemsList.Children.Add(BuildProblemEntry(problem));

        AdqlProblemsBar.Title = _adqlProblems.Count == 1
            ? "This query will be refused"
            : $"This query will be refused ({_adqlProblems.Count} problems)";
        AdqlProblemsBar.IsOpen = true;

        // Greyed, and told why: a disabled button with no explanation is a button that looks broken.
        ExecuteAdqlButton.IsEnabled = false;
        ToolTipService.SetToolTip(ExecuteAdqlButton, _adqlProblems[0].Describe());
        return _adqlProblems;
    }

    /// <summary>One problem as a line you can click to be taken to the text it is about.</summary>
    private HyperlinkButton BuildProblemEntry(AdqlProblem problem)
    {
        var entry = new HyperlinkButton
        {
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = new TextBlock { Text = problem.Describe(), TextWrapping = TextWrapping.Wrap },
        };

        entry.Click += (_, _) =>
        {
            var text = AdqlBox.Text ?? string.Empty;
            if (problem.Start >= text.Length) return;

            AdqlBox.Focus(FocusState.Programmatic);
            AdqlBox.Select(problem.Start, Math.Min(problem.Length, text.Length - problem.Start));
        };

        return entry;
    }
}
