using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Models;
using CanfarDesktop.Services.ImageDiscovery;

namespace CanfarDesktop.Views.Dialogs;

/// <summary>One search result, and whether it is already in the user's list.</summary>
public partial class RegistryResultRow : ObservableObject
{
    public string ImageId { get; }
    public IReadOnlyList<string> Types { get; }

    [ObservableProperty] private bool _added;

    public RegistryResultRow(RegistryImage image, bool added)
    {
        ImageId = image.Id;
        Types = image.Types;
        _added = added;
    }

    /// <summary>
    /// The session types the registry's labels declared, or a plain statement that there are none —
    /// which is not a fault. An image with no session-type label is still launchable from the Advanced
    /// tab, which takes a reference directly; it just cannot be offered on Standard.
    /// </summary>
    public string TypesLine => Types.Count > 0
        ? string.Join(", ", Types)
        : Helpers.Loc.T("Images_NoSessionTypes");

    public string StateText => Added ? Helpers.Loc.T("Images_AlreadyAdded") : string.Empty;
    public bool CanAdd => !Added;

    partial void OnAddedChanged(bool value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(CanAdd));
    }
}

/// <summary>
/// Searching the registry behind the platform, and keeping what you find.
///
/// Nothing happens until the Search button does. The dialog opens empty on purpose: enumerating a
/// registry to fill a list nobody asked for is a great deal of traffic on a shared service, and the
/// person who opened this already knows roughly what they are looking for.
/// </summary>
public sealed partial class RegistryBrowserDialog : ContentDialog
{
    private readonly IRegistryService _registry;
    private readonly IUserImageStore _userImages;
    private readonly ImageDiscoverySettingsService _settings;
    private readonly ObservableCollection<RegistryResultRow> _rows = new();

    public RegistryBrowserDialog(IRegistryService registry, IUserImageStore userImages, ImageDiscoverySettingsService settings)
    {
        InitializeComponent();
        _registry = registry;
        _userImages = userImages;
        _settings = settings;
        ResultList.ItemsSource = _rows;
    }

    private void OnQueryKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;

        e.Handled = true;
        _ = SearchAsync();
    }

    private void OnSearchClick(object sender, RoutedEventArgs e) => _ = SearchAsync();

    private async Task SearchAsync()
    {
        var query = (QueryBox.Text ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            Report(InfoBarSeverity.Informational, Helpers.Loc.T("Images_TypeSomething"));
            return;
        }

        SearchButton.IsEnabled = false;
        Report(InfoBarSeverity.Informational, Helpers.Loc.T("Images_Searching"));
        _rows.Clear();

        try
        {
            var found = await _registry.SearchAsync(
                _settings.Settings.RegistryHost, query, new RegistryAuth(_settings.CurrentAuthHeader()));

            var mine = _userImages.All().Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var image in found)
                _rows.Add(new RegistryResultRow(image, mine.Contains(image.Id)));

            ResultBar.IsOpen = found.Count == 0;
            if (found.Count == 0)
                Report(InfoBarSeverity.Informational, Helpers.Loc.T("Images_NoRegistryMatches"));
        }
        catch (Exception ex)
        {
            // The registry's own words. It is the only thing that knows whether this was a bad secret,
            // an unreachable host, or a project the user cannot see, and each has a different fix.
            Report(InfoBarSeverity.Error, ex.Message);
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).DataContext is not RegistryResultRow row) return;

        if (_userImages.Add(RegistryImage.FromLabels(row.ImageId, row.Types)))
            row.Added = true;
    }

    private void Report(InfoBarSeverity severity, string message)
    {
        ResultBar.Severity = severity;
        ResultBar.Message = message;
        ResultBar.IsOpen = true;
    }
}
