using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Models;

namespace CanfarDesktop.Views.Controls;

/// <summary>
/// Small wand badge shown next to entries that originated from an MCP-connected AI agent; clicking it
/// opens a popover with the provenance stamped on the entity at apply time. Hidden when the bound
/// attribution is null (user-authored). 1-to-1 with the macOS AgentAttributionBadge.
/// </summary>
public sealed partial class AgentBadge : UserControl
{
    public static readonly DependencyProperty AttributionProperty = DependencyProperty.Register(
        nameof(Attribution), typeof(AgentAttribution), typeof(AgentBadge),
        new PropertyMetadata(null, OnAttributionChanged));

    public AgentAttribution? Attribution
    {
        get => (AgentAttribution?)GetValue(AttributionProperty);
        set => SetValue(AttributionProperty, value);
    }

    public AgentBadge()
    {
        InitializeComponent();
    }

    private static void OnAttributionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var badge = (AgentBadge)d;
        var attribution = e.NewValue as AgentAttribution;
        badge.Visibility = attribution is null ? Visibility.Collapsed : Visibility.Visible;
        if (attribution is null) return;

        var tip = $"Created by {attribution.OriginLabel}";
        ToolTipService.SetToolTip(badge.BadgeButton, tip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(badge.BadgeButton, tip);
        badge.AgentText.Text = attribution.OriginLabel;
        badge.ClientText.Text = attribution.OriginFingerprint;
        badge.AppliedText.Text =
            $"{attribution.AppliedAt.ToLocalTime():g} ({Helpers.ImageDiscovery.DiscoveryFormatting.TimeAgo(attribution.AppliedAt)})";
        badge.ProposalText.Text = attribution.ProposalId.ToString()[..8] + "…";
        badge.SummaryText.Text = attribution.Summary;
    }
}
