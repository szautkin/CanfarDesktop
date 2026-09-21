using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Views.Controls;

/// <summary>
/// The Marks panel — see MarksPanel.xaml for what it is for.
///
/// It owns no marks. The viewer holds them; this shows what it is given and raises what was asked for,
/// which is what lets one panel serve a flat image and a cube whose marks live on channels.
/// </summary>
public sealed partial class MarksPanel : UserControl
{
    private IReadOnlyList<Annotation> _marks = [];
    private string? _selectedId;

    /// <summary>
    /// Suppresses the change events while the controls are being set FROM a mark.
    ///
    /// Setting a control raises its own changed event, so without this, showing the selected mark's
    /// style would immediately write that same style back — harmless for the mark, but it would also
    /// overwrite the stored default every time anybody clicked a row.
    /// </summary>
    private bool _settling;

    public MarksPanel()
    {
        InitializeComponent();
        ShowStyle(MarkStyle.UserDefault);
    }

    // ── What the panel asks for ─────────────────────────────────────────────────────────────────

    /// <summary>The pencil was turned on or off.</summary>
    public event Action<bool>? DrawArmedChanged;

    /// <summary>A different shape was chosen for the next mark.</summary>
    public event Action<AnnotationKind>? KindChanged;

    /// <summary>
    /// A style control moved: apply it to the selected mark, or remember it for the next one.
    /// </summary>
    public event Action<MarkStyle>? StyleChanged;

    /// <summary>A row was picked out. The viewer decides what selecting means — usually, go and show it.</summary>
    public event Action<string?>? SelectionChanged;

    /// <summary>The pencil on a row: relabel that mark.</summary>
    public event Action<string>? EditRequested;

    /// <summary>The bin on a row.</summary>
    public event Action<string>? DeleteRequested;

    /// <summary>Delete every mark. Already confirmed by the time this is raised.</summary>
    public event Action? ClearAllRequested;

    /// <summary>Write these marks out. The viewer knows where they live and what to call the file.</summary>
    public event Action? ExportRequested;

    /// <summary>
    /// The viewer, for the right-click menu on a row.
    ///
    /// Set rather than injected because the panel outlives any one file and a viewer attaches once.
    /// Left null, rows simply have no menu — every command in it is reachable another way, so a panel
    /// without a host is poorer, not broken.
    /// </summary>
    public IMarkCommandHost? Commands { get; set; }

    // ── What the viewer tells it ────────────────────────────────────────────────────────────────

    /// <summary>Whether the pencil is on. Set by the viewer too, since a toolbar may arm it as well.</summary>
    public bool DrawArmed
    {
        get => DrawToggle.IsChecked == true;
        set
        {
            if (DrawToggle.IsChecked == value) return;
            _settling = true;
            DrawToggle.IsChecked = value;
            _settling = false;
        }
    }

    /// <summary>What the next mark will be.</summary>
    public AnnotationKind Kind
    {
        get => (KindCombo.SelectedItem as FrameworkElement)?.Tag is string tag
            && AnnotationKindExtensions.Parse(tag) is { } kind ? kind : AnnotationKind.Circle;
        set
        {
            // Callout and Text are still real kinds — an agent can make them and a stored file can hold
            // them — they are simply not on this picker. Landing on nothing would leave the combo blank
            // and the next mark's shape a mystery, so the nearest thing a person can draw stands in.
            var tag = value == AnnotationKind.Rect ? "rect" : "circle";
            foreach (var item in KindCombo.Items.OfType<FrameworkElement>())
            {
                if (item.Tag as string != tag) continue;
                _settling = true;
                KindCombo.SelectedItem = item;
                _settling = false;
                return;
            }
        }
    }

    /// <summary>
    /// Redraw the list.
    ///
    /// The viewer owns which mark is chosen, so this states it rather than the panel remembering — two
    /// places deciding what is selected is how a list highlights one mark while the canvas shows
    /// another.
    /// </summary>
    public void Show(IReadOnlyList<Annotation> marks, string? selectedId)
    {
        _marks = marks;
        _selectedId = selectedId;
        Rebuild();

        // The style row follows the selection: the selected mark's own look, or — with nothing
        // selected — what the next mark will get. One place decides that, so no path through either
        // viewer can leave the row describing a mark that is no longer picked out.
        ShowStyle(marks.FirstOrDefault(m => m.Id == selectedId)?.EffectiveStyle ?? DefaultStyle());
    }

    /// <summary>Point the style row at a style without announcing it back.</summary>
    public void ShowStyle(MarkStyle style)
    {
        style = style.Sane();
        _settling = true;
        try
        {
            var (r, g, b) = (Byte(style.Red), Byte(style.Green), Byte(style.Blue));
            Colour.Color = Windows.UI.Color.FromArgb(255, r, g, b);
            ColourSwatch.Background = new SolidColorBrush(Colour.Color);
            FontSize.Value = style.FontSize;
            Bold.IsChecked = style.Bold;
            Stroke.Value = style.Stroke;
        }
        finally
        {
            _settling = false;
        }
    }

    /// <summary>What the controls currently say.</summary>
    public MarkStyle Style() => new MarkStyle(
        Colour.Color.R / 255.0,
        Colour.Color.G / 255.0,
        Colour.Color.B / 255.0,
        double.IsNaN(FontSize.Value) ? MarkStyle.DefaultFontSize : FontSize.Value,
        Bold.IsChecked == true,
        double.IsNaN(Stroke.Value) ? MarkStyle.DefaultStroke : Stroke.Value).Sane();

    // ── The list ────────────────────────────────────────────────────────────────────────────────

    private void Rebuild()
    {
        var lines = MarkSummary.Lines(_marks, FilterBox.Text);

        _settling = true;
        try
        {
            MarkList.Items.Clear();
            foreach (var line in lines) MarkList.Items.Add(Row(line));

            var chosen = MarkList.Items
                .OfType<FrameworkElement>()
                .FirstOrDefault(item => item.Tag as string == _selectedId);
            MarkList.SelectedItem = chosen;
        }
        finally
        {
            _settling = false;
        }

        var count = MarkSummary.Count(_marks.Count);
        CountText.Text = count ?? string.Empty;
        CountText.Visibility = count is null ? Visibility.Collapsed : Visibility.Visible;

        // Two different nothings. No marks at all is an invitation to draw one; marks that the filter
        // hides is a filter to clear — telling somebody "nothing marked yet" while their marks sit
        // behind a stale filter is the panel being wrong about its own contents.
        EmptyText.Visibility = lines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _marks.Count == 0
            ? Loc.T("Marks_Empty")
            : Loc.T("Marks_NoneMatchFilter");

        HintText.Visibility = _marks.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ClearButton.IsEnabled = _marks.Count > 0;
        ExportButton.IsEnabled = _marks.Count > 0;
    }

    /// <summary>One mark: what it says, where it is, and the two things to do to it.</summary>
    private FrameworkElement Row(MarkLine line)
    {
        var title = new TextBlock
        {
            Text = line.Title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };

        var detail = new TextBlock
        {
            Text = line.Detail,
            FontSize = 11,
            Opacity = 0.7,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };

        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        text.Children.Add(detail);

        var edit = IconButton("", Loc.T("Marks_RowEdit"));
        edit.Click += (_, _) => EditRequested?.Invoke(line.Id);

        var delete = IconButton("", Loc.T("Marks_RowDelete"));
        delete.Click += (_, _) => DeleteRequested?.Invoke(line.Id);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
        };
        buttons.Children.Add(edit);
        buttons.Children.Add(delete);

        var row = new Grid { ColumnSpacing = 6, Tag = line.Id };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 0);
        Grid.SetColumn(buttons, 1);
        row.Children.Add(text);
        row.Children.Add(buttons);

        return row;
    }

    private static Button IconButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 12 },
            Padding = new Thickness(6, 2, 6, 2),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
        };

        ToolTipService.SetToolTip(button, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tooltip);
        return button;
    }

    // ── Handlers ────────────────────────────────────────────────────────────────────────────────

    private void OnToggleDraw(object sender, RoutedEventArgs e)
    {
        if (_settling) return;
        DrawArmedChanged?.Invoke(DrawToggle.IsChecked == true);
    }

    private void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settling) return;

        KindChanged?.Invoke(Kind);

        // Choosing a shape is choosing to draw one. Picking "Box" and then finding that nothing happens
        // until a second, separate control is also switched on is the commonest way a drawing tool
        // reads as broken.
        if (DrawToggle.IsChecked != true)
        {
            DrawToggle.IsChecked = true;
            DrawArmedChanged?.Invoke(true);
        }
    }

    private void OnColourChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        ColourSwatch.Background = new SolidColorBrush(args.NewColor);
        AnnounceStyle();
    }

    private void OnStyleChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => AnnounceStyle();

    private void OnStyleToggled(object sender, RoutedEventArgs e) => AnnounceStyle();

    /// <summary>
    /// One path for four controls: they all mean the same thing, and four copies of "read them all,
    /// clamp, notify" would be four places to forget the guard.
    /// </summary>
    private void AnnounceStyle()
    {
        if (_settling) return;
        StyleChanged?.Invoke(Style());
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e) => Rebuild();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settling) return;
        SelectionChanged?.Invoke((MarkList.SelectedItem as FrameworkElement)?.Tag as string);
    }

    /// <summary>
    /// Clicking the row that is already picked out lets the mark go — the same gesture the canvas
    /// answers to, so the two agree about what a second click means.
    ///
    /// <para>This exists because a ListView does NOT raise SelectionChanged when you click the item
    /// that is already selected, so selecting was reachable and deselecting was not. Clicking a
    /// DIFFERENT row also lands here, first; that case returns and lets SelectionChanged do its
    /// ordinary work, so the two handlers never both act on one click.</para>
    ///
    /// <para>Item click also fires on Enter and Space, so this is reachable from the keyboard.</para>
    /// </summary>
    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (_settling) return;
        if ((e.ClickedItem as FrameworkElement)?.Tag as string is not { } clicked) return;
        if ((MarkList.SelectedItem as FrameworkElement)?.Tag as string != clicked) return;

        _settling = true;
        try { MarkList.SelectedItem = null; }
        finally { _settling = false; }

        SelectionChanged?.Invoke(null);
    }

    /// <summary>
    /// Ask before clearing: it takes a person's own marks along with an agent's, and nothing brings
    /// them back. Deleting ONE mark does not ask — one mark is easy to redraw and its button is right
    /// beside it.
    /// </summary>
    private async void OnClearAll(object sender, RoutedEventArgs e)
    {
        if (_marks.Count == 0) return;

        var dialog = new ContentDialog
        {
            Title = Loc.T("Marks_ClearTitle"),
            Content = Loc.T("Marks_ClearBody"),
            PrimaryButtonText = Loc.T("Marks_ClearConfirm"),
            CloseButtonText = Loc.T("Portal_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary) ClearAllRequested?.Invoke();
    }

    private void OnExportMarks(object sender, RoutedEventArgs e) => ExportRequested?.Invoke();

    /// <summary>
    /// The same menu the canvas shows, on the row that names the mark.
    ///
    /// <para>Handled on the list rather than on each row because this one event covers BOTH ways of
    /// asking: a right-click, which arrives with a position, and the Menu key or Shift+F10 on the
    /// focused row, which arrives without one. Wiring it per row would have caught the mouse and
    /// missed the keyboard, since the key lands on the container and never reaches the row's own
    /// content.</para>
    /// </summary>
    private void OnListContextRequested(
        UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs args)
    {
        if (Commands is not { } host) return;
        var row = RowIdFrom(args.OriginalSource)
                  ?? (MarkList.SelectedItem as FrameworkElement)?.Tag as string;
        if (row is not { } id) return;

        // Picked out first: a menu acting on something not visibly chosen is how people delete the
        // wrong thing. Announcing it also moves the canvas to the mark the menu is about.
        if (_selectedId != id) SelectionChanged?.Invoke(id);

        var menu = MarkContextMenu.Build(
            MarkCommands.For(host.CommandContextFor(id)),
            command => host.InvokeMarkCommand(command, id));

        // No position means the keyboard asked, so the menu goes on the row itself.
        var at = args.TryGetPosition(MarkList, out var point)
            ? point
            : new Windows.Foundation.Point(8, 8);

        MarkContextMenu.ShowAt(menu, MarkList, at.X, at.Y);
        args.Handled = true;
    }

    /// <summary>Which row an event came from, by walking up to the element carrying the mark's id.</summary>
    private static string? RowIdFrom(object? source)
    {
        for (var node = source as DependencyObject; node is not null;
             node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement { Tag: string id } && !string.IsNullOrEmpty(id)) return id;
        }

        return null;
    }

    private static byte Byte(double channel) => (byte)Math.Round(Math.Clamp(channel, 0, 1) * 255);

    /// <summary>What the next mark gets: the stored preference, or the shipped ink.</summary>
    private static MarkStyle DefaultStyle()
    {
        try
        {
            var stored = App.Services.GetService(typeof(Services.ISettingsService)) as Services.ISettingsService;
            return MarkStyle.Decode(stored?.DefaultMarkStyle, MarkStyle.UserDefault);
        }
        catch
        {
            return MarkStyle.UserDefault;
        }
    }
}
