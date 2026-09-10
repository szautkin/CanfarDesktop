using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Views.Controls;

/// <summary>
/// Draws marks over a viewer.
///
/// A Canvas of XAML shapes rather than a bitmap: it is what the crosshair overlay in the same viewer
/// already is, it composites and scales for free, and it does not need a second rendering path to be
/// kept in step with the first.
///
/// It knows nothing about FITS or cubes. Everything it needs comes from <see cref="IAnnotationSurface"/>
/// and <see cref="AnnotationGeometry"/> — the same geometry an export plate will use, so a figure and
/// the screen cannot disagree about where a leader goes.
/// </summary>
public sealed class AnnotationLayer : Canvas
{
    /// <summary>The mark being edited: grips out, drawn in the editing ink.</summary>
    public string? EditingId { get; set; }

    /// <summary>The mark merely picked out — from the list, a click, or an agent pointing at it.</summary>
    public string? SelectedId { get; set; }

    /// <summary>The mark being EDITED — grips you can drag.</summary>
    private static readonly Color EditingInk = Color.FromArgb(255, 255, 199, 89);

    /// <summary>
    /// A mark merely picked out. Brighter than the rest, but not the editing colour: the two states look
    /// different because they ARE different — one has grips you can drag and the other does not.
    /// </summary>
    private static readonly Color SelectedInk = Colors.White;

    private const double Alpha = 0.92;

    /// <summary>Redraw every mark. Cheap enough to do wholesale: these are tens of shapes, not thousands.</summary>
    public void Render(IReadOnlyList<Annotation> annotations, IAnnotationSurface surface, double canvasWidth)
    {
        Children.Clear();

        // Asked once, and made usable once: it is a property of this rendering, not of a mark, and every
        // length below is multiplied by it.
        var ink = MarkStyle.UsableInk(surface.InkScale);

        foreach (var mark in annotations)
        {
            // Skipped, not clamped, when the anchor is not on this surface: a clamped mark points at the
            // wrong thing, which is worse than a mark that is not drawn.
            if (surface.Project(mark.Anchor) is not { } centre) continue;

            var editing = mark.Id == EditingId;
            var selected = mark.Id == SelectedId;
            var style = mark.EffectiveStyle;
            var brush = new SolidColorBrush(InkFor(style, selected, editing)) { Opacity = Alpha };
            var stroke = (editing || selected ? AnnotationGeometry.SelectedStroke : style.Stroke) * ink;

            var box = AnnotationGeometry.HalfSize(mark, surface, fallback: 0);
            var halfW = box?.HalfW ?? 0;
            var halfH = box?.HalfH ?? 0;

            switch (mark.Kind)
            {
                case AnnotationKind.Circle:
                    AddShape(new Ellipse { Width = halfW * 2, Height = halfH * 2, Stroke = brush, StrokeThickness = stroke },
                             centre.X - halfW, centre.Y - halfH);
                    break;

                case AnnotationKind.Rect:
                    AddShape(new Rectangle { Width = halfW * 2, Height = halfH * 2, Stroke = brush, StrokeThickness = stroke },
                             centre.X - halfW, centre.Y - halfH);
                    break;

                case AnnotationKind.Callout:
                    // A callout's shape is a small ring at the subject; the leader is the point of it.
                    if (mark.Extent is not null)
                        AddShape(new Ellipse { Width = halfW * 2, Height = halfH * 2, Stroke = brush, StrokeThickness = stroke },
                                 centre.X - halfW, centre.Y - halfH);
                    break;

                case AnnotationKind.Text:
                    AddLabel(mark.Text, style, brush, ink, centre.X, centre.Y - style.FontSize * ink);
                    break;
            }

            // Every labelled shape is labelled the same way — a leader leaving the outline at a fixed
            // acute angle, with the text on the rule at its end.
            //
            // A box and a circle used to put their label at their own corner while a callout drew a
            // leader, which read as two different products on one canvas. A blueprint labels everything
            // with a leader; and once a circle has one, a "callout" is just a circle with a label, which
            // is why the shape picker no longer offers one.
            if (mark.Kind != AnnotationKind.Text && !string.IsNullOrEmpty(mark.Text))
                DrawLeaderAndLabel(mark, style, brush, stroke, centre, halfW, halfH, canvasWidth, ink);

            // Grips on the mark that is picked out, not only on the one being labelled: selecting a
            // mark is how a person says "this one", and a resize handle that appears only while a text
            // field happens to be open is one nobody finds.
            if (selected || editing) DrawHandles(mark, surface);
        }
    }

    /// <summary>
    /// The ink for one mark. The decision is the same on every canvas — editing first, then picked out,
    /// then the mark's own colour — so it is made once, here.
    /// </summary>
    private static Color InkFor(MarkStyle style, bool selected, bool editing)
    {
        if (editing) return EditingInk;
        if (selected) return SelectedInk;

        static byte Q(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return Color.FromArgb(255, Q(style.Red), Q(style.Green), Q(style.Blue));
    }

    /// <summary>
    /// The leader and the label, for any shape that has words.
    ///
    /// One path for all of them, which is what stops a box and a circle drifting into being labelled
    /// differently from each other.
    /// </summary>
    private void DrawLeaderAndLabel(Annotation mark, MarkStyle style, Brush brush, double stroke,
                                    (double X, double Y) centre, double halfW, double halfH, double canvasWidth, double ink)
    {
        // The text is measured with the SCALED font, because the rule is as long as its text and mixing
        // a scaled width with an unscaled overhang leaves a rule that does not reach its own text.
        var fontSize = style.FontSize * ink;
        var textWidth = AnnotationGeometry.EstimateTextWidth(mark.Text, fontSize, style.Bold);

        var offset = mark.LabelOffsetX is { } dx && mark.LabelOffsetY is { } dy ? (dx, dy) : ((double, double)?)null;

        // A mark with no extent — a bare callout — still needs somewhere for the leader to start, so it
        // gets the small ring's radius rather than a zero the geometry would divide by.
        var (hw, hh) = mark.Extent is not null ? (Math.Max(halfW, 1), Math.Max(halfH, 1)) : (3.0, 3.0);

        var leader = AnnotationGeometry.LeaderGeometry(
            centre.X, centre.Y, hw, hh,
            elliptical: mark.Kind != AnnotationKind.Rect, offset, textWidth, canvasWidth, ink);

        Children.Add(new Polyline
        {
            Points = { new(leader.StartX, leader.StartY), new(leader.ElbowX, leader.ElbowY), new(leader.RuleEndX, leader.ElbowY) },
            Stroke = brush,
            StrokeThickness = stroke,
        });

        AddLabel(mark.Text, style, brush, ink, leader.TextX, leader.ElbowY - fontSize - AnnotationGeometry.TextLift * ink);
    }

    private void AddLabel(string text, MarkStyle style, Brush brush, double ink, double x, double y)
    {
        if (string.IsNullOrEmpty(text)) return;

        var label = new TextBlock
        {
            Text = text,
            FontSize = style.FontSize * ink,
            FontWeight = style.Bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = brush,
            IsHitTestVisible = false,
        };

        AddShape(label, x, y);
    }

    /// <summary>
    /// The four resize grips. Screen-sized, not data-sized: a grip has to be grabbable at any zoom, and
    /// one that shrank with the image would become unusable exactly when a mark is small enough to need
    /// adjusting.
    /// </summary>
    private void DrawHandles(Annotation mark, IAnnotationSurface surface)
    {
        var fill = new SolidColorBrush(Color.FromArgb(242, 20, 23, 28));
        var edge = new SolidColorBrush(SelectedInk);

        foreach (var (x, y) in AnnotationGeometry.Handles(mark, surface))
        {
            AddShape(new Ellipse
            {
                Width = AnnotationGeometry.HandleRadius * 2,
                Height = AnnotationGeometry.HandleRadius * 2,
                Fill = fill,
                Stroke = edge,
                StrokeThickness = 1.5,
            }, x - AnnotationGeometry.HandleRadius, y - AnnotationGeometry.HandleRadius);
        }
    }

    private void AddShape(FrameworkElement element, double left, double top)
    {
        element.IsHitTestVisible = false;   // the canvas beneath handles every press; see AnnotationGeometry.GrabAt
        SetLeft(element, left);
        SetTop(element, top);
        Children.Add(element);
    }
}
