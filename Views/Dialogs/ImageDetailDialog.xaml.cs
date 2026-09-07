using System.Collections.ObjectModel;
using Windows.UI.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CanfarDesktop.Helpers.ImageDiscovery;
using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Views.Dialogs;

/// <summary>
/// One row of the package list: either an ecosystem heading or a package under it.
///
/// The two are one type, and the list is one flat list, because it is virtualized: a ListView per
/// ecosystem inside an ItemsControl realizes every row of every section at once, which on an image with
/// six hundred packages is the difference between a dialog that opens and one that hesitates.
/// </summary>
public sealed record PackageRow(string Name, string Version, bool IsHeading)
{
    public Windows.UI.Text.FontWeight HeaderWeight => IsHeading ? FontWeights.SemiBold : FontWeights.Normal;

    public Brush Foreground => (Brush)Application.Current.Resources[
        IsHeading ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush"];
}

/// <summary>
/// What is actually inside a container image: its OS, kernel, Python, capabilities, and its packages by
/// ecosystem.
///
/// The images card could tell you an image had been probed and how many packages it held. It could not
/// tell you WHICH — so choosing between two images that both "have astropy" meant launching one and
/// looking. This is that answer, from the manifest the probe already produced.
/// </summary>
public sealed partial class ImageDetailDialog : ContentDialog
{
    private readonly ObservableCollection<PackageRow> _rows = new();
    private ManifestDetail? _detail;

    /// <summary>The image, when the user asked to use it rather than only to look.</summary>
    public string? PickedImageId { get; private set; }

    public ImageDetailDialog()
    {
        InitializeComponent();
        PackageList.ItemsSource = _rows;
        PrimaryButtonClick += (_, _) => PickedImageId = _imageId;
    }

    private string _imageId = string.Empty;

    public void Initialize(ImageManifest manifest)
    {
        _imageId = manifest.ImageID;
        _detail = ManifestDetailBuilder.Build(manifest);

        ImageIdText.Text = manifest.ImageID;

        var parts = new List<string> { _detail.OsLine };
        if (_detail.KernelLine.Length > 0) parts.Add(_detail.KernelLine);
        if (_detail.PythonVersion is { Length: > 0 } and not "unknown") parts.Add("Python " + _detail.PythonVersion);
        OsText.Text = string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

        CapabilitiesText.Text = _detail.Capabilities.Count > 0
            ? string.Join(", ", _detail.Capabilities)
            : string.Empty;
        CapabilitiesText.Visibility = _detail.Capabilities.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // A probe note is why a section is missing or short. Shown rather than logged, because the
        // question it answers — "is this image really without astropy?" — is asked of this dialog.
        NotesBar.Message = _detail.ProbeNotes ?? string.Empty;
        NotesBar.IsOpen = !string.IsNullOrWhiteSpace(_detail.ProbeNotes);

        Rebuild(string.Empty);
    }

    private void OnFilterChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        Rebuild(sender.Text ?? string.Empty);
    }

    private void Rebuild(string filter)
    {
        _rows.Clear();
        if (_detail is null) return;

        filter = filter.Trim();
        var matched = 0;

        foreach (var section in _detail.Sections)
        {
            var packages = filter.Length == 0
                ? section.Packages
                : section.Packages.Where(p => p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

            // A heading over nothing is a heading that makes the reader check whether they misread the
            // filter. Sections that match nothing are left out entirely.
            if (packages.Count == 0) continue;

            _rows.Add(new PackageRow($"{section.Title} ({packages.Count})", string.Empty, IsHeading: true));
            foreach (var package in packages)
                _rows.Add(new PackageRow(package.Name, package.Version, IsHeading: false));

            matched += packages.Count;
        }

        if (matched == 0 && filter.Length > 0)
            _rows.Add(new PackageRow($"Nothing in this image matches “{filter}”", string.Empty, IsHeading: true));
    }
}
