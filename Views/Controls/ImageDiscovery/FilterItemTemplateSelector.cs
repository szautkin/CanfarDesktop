using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.ViewModels.ImageDiscovery;

namespace CanfarDesktop.Views.Controls.ImageDiscovery;

/// <summary>
/// Picks the section-header vs checkbox template for the flattened, virtualized left filter pane
/// (one ListView holding both <see cref="FacetSectionViewModel"/> headers and
/// <see cref="FacetValueViewModel"/> checkbox rows).
/// </summary>
public partial class FilterItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? SectionTemplate { get; set; }
    public DataTemplate? ValueTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
        => item is FacetSectionViewModel ? SectionTemplate : ValueTemplate;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);
}
