using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>Destructive writes never auto-apply, even with auto-apply on — they always queue for approval.</summary>
public class AutoApplyPolicyTests
{
    [Fact]
    public void Destructive_NeverAutoApplies_EvenWhenEnabled()
    {
        Assert.False(AutoApplyPolicy.ShouldAutoApply(autoApplyEnabled: true, McpVerbClass.Destructive));
        Assert.False(AutoApplyPolicy.ShouldAutoApply(autoApplyEnabled: false, McpVerbClass.Destructive));
    }

    [Theory]
    [InlineData(McpVerbClass.SemanticWrite)]
    [InlineData(McpVerbClass.ViewState)]
    public void NonDestructive_AutoAppliesOnlyWhenEnabled(McpVerbClass verb)
    {
        Assert.True(AutoApplyPolicy.ShouldAutoApply(autoApplyEnabled: true, verb));
        Assert.False(AutoApplyPolicy.ShouldAutoApply(autoApplyEnabled: false, verb));
    }
}
