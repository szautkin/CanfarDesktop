using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace CanfarDesktop.Helpers;

/// <summary>
/// The app's shared motion vocabulary (durations + composition helpers), guarded by the OS
/// reduce-motion setting so animations vanish for users who disabled them. Mirrors the macOS
/// AppMotion (hero/quick/state-swap tiers).
/// </summary>
public static class AppMotion
{
    private static readonly Windows.UI.ViewManagement.UISettings Settings = new();

    /// <summary>True when the OS asks apps not to animate.</summary>
    public static bool Reduced
    {
        get
        {
            try { return !Settings.AnimationsEnabled; }
            catch { return false; }
        }
    }

    public const double QuickMs = 120;
    public const double StateSwapMs = 150;
    public const double HeroMs = 250;

    /// <summary>Fade an element from transparent to opaque (view/state swaps).</summary>
    public static void FadeIn(UIElement element, double durationMs = StateSwapMs)
    {
        if (Reduced) return;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var anim = visual.Compositor.CreateScalarKeyFrameAnimation();
        anim.InsertKeyFrame(0f, 0f);
        anim.InsertKeyFrame(1f, 1f);
        anim.Duration = TimeSpan.FromMilliseconds(durationMs);
        visual.StartAnimation("Opacity", anim);
    }

    /// <summary>
    /// Give an element a subtle hover scale (landing tiles). Centers the scale so the tile grows
    /// in place; no-ops entirely under reduce-motion.
    /// </summary>
    public static void AttachHoverScale(FrameworkElement element, float scale = 1.03f)
    {
        if (Reduced) return;
        element.PointerEntered += (_, _) => ScaleTo(element, scale);
        element.PointerExited += (_, _) => ScaleTo(element, 1f);
        element.PointerCaptureLost += (_, _) => ScaleTo(element, 1f);
    }

    private static void ScaleTo(FrameworkElement element, float scale)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new System.Numerics.Vector3(
            (float)(element.ActualWidth / 2), (float)(element.ActualHeight / 2), 0);
        var anim = visual.Compositor.CreateVector3KeyFrameAnimation();
        anim.InsertKeyFrame(1f, new System.Numerics.Vector3(scale, scale, 1f));
        anim.Duration = TimeSpan.FromMilliseconds(QuickMs);
        visual.StartAnimation("Scale", anim);
    }
}
