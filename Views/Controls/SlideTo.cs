using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace CanfarDesktop.Views.Controls;

/// <summary>
/// Move an element somewhere, and let it be seen going.
///
/// <para>A panel that blinks out of existence leaves a person wondering what happened to it. The same
/// panel sliding off to its edge answers that while it moves: it went THAT way, it is still there,
/// and the picture underneath is what was wanted. The motion is the explanation.</para>
///
/// <para>Short and eased-out: long enough to be followed, brief enough that someone toggling the
/// panels to compare two things is not made to wait for it. Easing out means the panel leaves briskly
/// and settles, which reads as deliberate rather than mechanical.</para>
/// </summary>
internal static class SlideTo
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(220);

    /// <summary>
    /// Slide <paramref name="element"/> to an offset from where its layout put it.
    ///
    /// <paramref name="animate"/> is false for the first arrangement, where there is no previous
    /// position to travel from and an animation would just be a panel flying in at startup.
    /// </summary>
    public static void Offset(FrameworkElement? element, double x, double y, bool animate = true)
    {
        if (element is null) return;

        // The element keeps its own transform, so repeated calls move the same one rather than
        // stacking transforms on top of each other.
        if (element.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            element.RenderTransform = transform;
        }

        if (!animate)
        {
            transform.X = x;
            transform.Y = y;
            return;
        }

        var story = new Storyboard();
        Add(story, transform, "X", x);
        Add(story, transform, "Y", y);
        story.Begin();
    }

    private static void Add(Storyboard story, TranslateTransform transform, string property, double to)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(Duration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },

            // The transform is not a dependency property on a live tree node that the compositor can
            // take over, so this has to run on the UI thread rather than be refused for it.
            EnableDependentAnimation = true,
        };

        Storyboard.SetTarget(animation, transform);
        Storyboard.SetTargetProperty(animation, property);
        story.Children.Add(animation);
    }
}
