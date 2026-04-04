namespace CanfarDesktop.Views.Notebook;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.ViewModels.Notebook;

public class CellTemplateSelector : DataTemplateSelector
{
    public DataTemplate? CodeCellTemplate { get; set; }
    public DataTemplate? MarkdownCellTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
    {
        return item switch
        {
            CodeCellViewModel => CodeCellTemplate,
            MarkdownCellViewModel => MarkdownCellTemplate,
            _ => CodeCellTemplate
        };
    }
}
