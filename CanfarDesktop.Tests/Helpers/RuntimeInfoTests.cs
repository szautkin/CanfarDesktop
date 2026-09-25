using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// The block About offers somebody filing a bug.
///
/// The failure being fixed is specific: a version section that named only things fixed at build time,
/// including the app's own version wearing the framework's label. That block tells a reader nothing that
/// varies between the machines where a bug appears, which is the only thing it is for.
/// </summary>
public class RuntimeInfoTests
{
    [Fact]
    public void EveryFactIsNamedAndAnswered()
    {
        foreach (var fact in RuntimeInfo.Facts())
        {
            Assert.False(string.IsNullOrWhiteSpace(fact.Name), "a fact with no name");
            Assert.False(string.IsNullOrWhiteSpace(fact.Value), $"'{fact.Name}' has no value");
        }
    }

    /// <summary>
    /// A missing line in a bug report reads as an answer rather than as a gap — "no GPU?" — so an
    /// unreadable value has to say so in words instead of dropping out.
    /// </summary>
    [Fact]
    public void TheSameFactsAreThereEveryTime()
    {
        Assert.Equal(
            ["App", "Framework", "Windows App SDK", "OS", "Architecture", "Install"],
            RuntimeInfo.Facts().Select(f => f.Name));
    }

    /// <summary>
    /// The whole point. If the app's version and the framework's come from one source, the block is
    /// back to naming the app twice and the runtime never.
    /// </summary>
    [Fact]
    public void TheAppVersionAndTheFrameworkVersionAreDifferentThings()
    {
        var facts = RuntimeInfo.Facts().ToDictionary(f => f.Name, f => f.Value);

        Assert.NotEqual(facts["App"], facts["Framework"]);
        Assert.Contains(".NET", facts["Framework"]);
    }

    [Fact]
    public void TheFrameworkIsTheOneActuallyRunning()
        => Assert.Equal(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            RuntimeInfo.Framework());

    /// <summary>
    /// x64 on an Arm64 machine is emulation, and it changes what a graphics or interop bug means. Same
    /// on both, and saying it twice would be noise.
    /// </summary>
    [Fact]
    public void TheArchitectureNamesTheMachineOnlyWhenItDiffersFromTheProcess()
    {
        var process = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
        var machine = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;

        var reported = RuntimeInfo.Architecture();

        if (process == machine) Assert.Equal(process.ToString(), reported);
        else Assert.Equal($"{process} on {machine}", reported);
    }

    /// <summary>Found by name so this file needs no WinUI reference — and so it is testable at all.</summary>
    [Fact]
    public void TheWindowsAppSdkSaysSoWhenItIsNotLoaded()
        => Assert.Equal("not loaded", RuntimeInfo.WindowsAppSdk());   // no WinUI in a test host

    /// <summary>A test host is not a packaged install, and the block must not claim otherwise.</summary>
    [Fact]
    public void AnUnpackagedHostIsReportedAsUnpackaged()
        => Assert.Equal("unpackaged", RuntimeInfo.Facts().Single(f => f.Name == "Install").Value);

    /// <summary>A bug report gets pasted, not retyped.</summary>
    [Fact]
    public void TheTextIsOneLinePerFact()
    {
        var text = RuntimeInfo.AsText([new RuntimeFact("App", "1.4.0"), new RuntimeFact("OS", "Windows")]);

        Assert.Equal($"App: 1.4.0{Environment.NewLine}OS: Windows", text);
    }

    // ── Links ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// verbinal.com is the app; canfar.net is the observatory it talks to. About used to offer only the
    /// second, so the one link given to somebody asking "what is this program?" answered a different
    /// question.
    /// </summary>
    [Fact]
    public void TheProjectLinkIsTheAppsOwnSite()
        => Assert.Equal("https://verbinal.com", AppLinks.Website);

    /// <summary>A bug in the Windows app filed against the Linux repository goes nowhere.</summary>
    [Fact]
    public void IssuesGoToThisRepositorysTracker()
    {
        Assert.Contains("szautkin/CanfarDesktop/issues", AppLinks.Issues);
        Assert.DoesNotContain("Ubuntu", AppLinks.Issues);
    }

    [Fact]
    public void EveryLinkIsAWellFormedUri()
    {
        foreach (var link in new[] { AppLinks.Website, AppLinks.Issues, AppLinks.Support })
            Assert.True(Uri.IsWellFormedUriString(link, UriKind.Absolute), link);
    }
}
