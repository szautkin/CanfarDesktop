using System.Xml.Linq;
using Xunit;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// How XAML reaches its translated strings.
///
/// <para>An <c>x:Uid</c> is resolved when the page loads, not when it compiles: every resource named
/// <c>Uid.Property</c> is set on that element. Nothing checks at build time that the element HAS the
/// property, so a mismatch builds cleanly and then fails the whole page with a XamlParseException whose
/// entire message is "The text associated with this error code could not be found." — no element, no
/// key. These move that failure from the first person to open the page to the test run.</para>
/// </summary>
public class XamlResourceTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// The plain properties translations set, and the elements known to have each.
    ///
    /// <para>Listed by hand, so a property or element missing here fails the guard below until
    /// somebody has checked that the element really has it. Attached properties —
    /// <c>[using:…]AutomationProperties.Name</c> and the like — are not listed: every element takes
    /// those.</para>
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> Carriers = new()
    {
        ["Text"] = ["TextBlock", "TextBox", "Run", "AutoSuggestBox", "NumberBox",
                    "MenuFlyoutItem", "ToggleMenuFlyoutItem", "RadioMenuFlyoutItem", "MenuFlyoutSubItem"],
        ["Content"] = ["Button", "ToggleButton", "RepeatButton", "HyperlinkButton", "DropDownButton",
                       "SplitButton", "ToggleSplitButton", "AppBarButton", "AppBarToggleButton",
                       "CheckBox", "RadioButton", "ComboBoxItem", "ListViewItem", "GridViewItem",
                       "NavigationViewItem", "NavigationViewItemHeader", "ContentControl", "Expander", "ToolTip"],
        ["Header"] = ["TextBox", "PasswordBox", "RichEditBox", "AutoSuggestBox", "NumberBox", "ComboBox",
                      "ToggleSwitch", "Slider", "Expander", "PivotItem", "TabViewItem", "RadioButtons",
                      "DatePicker", "TimePicker", "CalendarDatePicker", "ListView", "GridView"],
        ["PlaceholderText"] = ["TextBox", "PasswordBox", "RichEditBox", "AutoSuggestBox", "NumberBox",
                               "ComboBox", "CalendarDatePicker"],
        ["Title"] = ["ContentDialog", "InfoBar", "TeachingTip", "MenuBarItem"],
        ["Subtitle"] = ["TeachingTip"],
        ["Message"] = ["InfoBar"],
        ["Label"] = ["AppBarButton", "AppBarToggleButton"],
        ["OnContent"] = ["ToggleSwitch"],
        ["OffContent"] = ["ToggleSwitch"],
        ["PrimaryButtonText"] = ["ContentDialog"],
        ["SecondaryButtonText"] = ["ContentDialog"],
        ["CloseButtonText"] = ["ContentDialog"],
    };

    /// <summary>
    /// Every translated property lands on an element that has it.
    ///
    /// <para>The cube viewer's section buttons were given the x:Uid of the TextBlock inside them, to
    /// pick up that uid's accessible name. They picked up its Text as well, a Button has none, and the
    /// cube viewer stopped opening at all.</para>
    /// </summary>
    [Fact]
    public void EveryTranslatedPropertyExistsOnItsElement()
    {
        var properties = PlainProperties();

        var wrong = Uids()
            .SelectMany(u => properties.GetValueOrDefault(u.Uid, [])
                .Where(p => !(Carriers.TryGetValue(p, out var on) && on.Contains(u.Element)))
                .Select(p => $"{u.Uid}.{p} on a {u.Element} in {u.File}"))
            .Distinct()
            .ToList();

        Assert.True(wrong.Count == 0,
            "a translation sets a property its element may not have, which fails the page as it loads. " +
            "Give the element an x:Uid of its own — or, having checked the element does have the " +
            "property, add it to Carriers: " + string.Join("; ", wrong));
    }

    /// <summary>
    /// One x:Uid, one kind of element.
    ///
    /// <para>A uid shared by, say, a Button and a TextBlock is safe only while every key under it suits
    /// both, and the next key somebody adds for one of them breaks the other. A second uid costs
    /// nothing.</para>
    /// </summary>
    [Fact]
    public void EveryUidBelongsToOneKindOfElement()
    {
        var mixed = Uids()
            .GroupBy(u => u.Uid)
            .Where(g => g.Select(u => u.Element).Distinct().Count() > 1)
            .Select(g => $"{g.Key} ({string.Join(", ", g.Select(u => $"{u.Element} in {u.File}").Distinct())})")
            .ToList();

        Assert.True(mixed.Count == 0,
            "these x:Uids are shared by different kinds of element: " + string.Join("; ", mixed));
    }

    /// <summary>
    /// No name is both a string and a scope. A key "A.B" puts B inside a scope A, and MakePri refuses
    /// the whole build when A is also a string of its own — "PRI278: 'Resources/A' or one of its parents
    /// is defined as both resource and scope". Search_CancelButton, a dialog's button text read in code,
    /// met Search_CancelButton.Content, a button's x:Uid. Only a Windows build runs MakePri; this catches
    /// it wherever the tests run.
    /// </summary>
    [Fact]
    public void NoNameIsBothAStringAndAScope()
    {
        var clashes = Directory.EnumerateFiles(RepoFiles.PathTo("Strings"), "Resources.resw", SearchOption.AllDirectories)
            .SelectMany(resw =>
            {
                var names = XDocument.Load(resw).Descendants("data")
                    .Select(d => (string?)d.Attribute("name") ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
                return names.SelectMany(Scopes).Where(names.Contains).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(name => $"{name} in {Path.GetFileName(Path.GetDirectoryName(resw))}");
            })
            .ToList();

        Assert.True(clashes.Count == 0,
            "these names are both a string and the scope of other keys, which fails the Windows build " +
            "(PRI278) — give the x:Uid a name of its own: " + string.Join("; ", clashes));
    }

    /// <summary>No name is defined twice in one language: MakePri fails the Windows build on that too.</summary>
    [Fact]
    public void NoNameIsDefinedTwice()
    {
        var twice = Directory.EnumerateFiles(RepoFiles.PathTo("Strings"), "Resources.resw", SearchOption.AllDirectories)
            .SelectMany(resw => XDocument.Load(resw).Descendants("data")
                .GroupBy(d => (string?)d.Attribute("name") ?? "", StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key} in {Path.GetFileName(Path.GetDirectoryName(resw))}"))
            .ToList();

        Assert.True(twice.Count == 0, "defined twice: " + string.Join("; ", twice));
    }

    /// <summary>The scopes a key sits in: "A.B.C" is in A and in A.B. An attached property's "[using:…]" part is one step.</summary>
    private static IEnumerable<string> Scopes(string name)
    {
        var bracket = name.IndexOf('[');
        var plain = bracket >= 0 ? name[..bracket] : name;
        for (var dot = plain.IndexOf('.'); dot > 0; dot = plain.IndexOf('.', dot + 1))
            yield return plain[..dot];
    }

    /// <summary>The guards above would pass by accident if the sweep found nothing to look at.</summary>
    [Fact]
    public void TheSweepReachesThePagesAndTheirTranslations()
    {
        var files = Uids().Select(u => u.File).ToHashSet();

        Assert.Contains("CubeViewerPage.xaml", files);
        Assert.Contains("SearchPage.xaml", files);
        Assert.Contains("Header", PlainProperties().Values.SelectMany(p => p));
    }

    private static IEnumerable<(string File, string Uid, string Element)> Uids()
        => RepoFiles.Sources("*.xaml").SelectMany(path => XDocument.Load(path).Descendants()
            .Select(e => (Element: e, Uid: (string?)e.Attribute(X + "Uid")))
            .Where(e => e.Uid is not null)
            .Select(e => (Path.GetFileName(path), e.Uid!, e.Element.Name.LocalName)));

    /// <summary>
    /// Each uid's plain properties, across every language — a key only the French resources have
    /// would fail the page for French speakers alone.
    /// </summary>
    private static Dictionary<string, HashSet<string>> PlainProperties()
        => Directory.EnumerateFiles(RepoFiles.PathTo("Strings"), "Resources.resw", SearchOption.AllDirectories)
            .SelectMany(resw => XDocument.Load(resw).Descendants("data"))
            .Select(d => (string?)d.Attribute("name") ?? "")
            .Where(name => name.Contains('.') && !name.Contains('['))
            .Select(name => (Uid: name[..name.IndexOf('.')], Property: name[(name.IndexOf('.') + 1)..]))
            .GroupBy(k => k.Uid)
            .ToDictionary(g => g.Key, g => g.Select(k => k.Property).ToHashSet());
}
