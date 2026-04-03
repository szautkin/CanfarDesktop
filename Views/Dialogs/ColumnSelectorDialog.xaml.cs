using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Models;

namespace CanfarDesktop.Views.Dialogs;

public sealed partial class ColumnSelectorDialog : ContentDialog
{
    internal readonly List<ResultColumnInfo> _columns;
    private readonly List<CheckBox> _checkBoxes = [];

    public ColumnSelectorDialog(List<ResultColumnInfo> columns)
    {
        _columns = columns;
        InitializeComponent();
        BuildGrid();
    }

    private void BuildGrid()
    {
        var rowsPerCol = (int)Math.Ceiling(_columns.Count / 3.0);

        // Ensure enough rows
        for (var r = 0; r < rowsPerCol; r++)
            ColumnsGrid.RowDefinitions.Add(new RowDefinition { Height = Microsoft.UI.Xaml.GridLength.Auto });

        for (var i = 0; i < _columns.Count; i++)
        {
            var col = _columns[i];
            var gridCol = i / rowsPerCol;
            var gridRow = i % rowsPerCol;

            var cb = new CheckBox
            {
                Content = col.Label,
                IsChecked = col.Visible,
                Tag = i,
                FontSize = 13,
                MinWidth = 0,
                Padding = new Microsoft.UI.Xaml.Thickness(4, 2, 4, 2)
            };

            Grid.SetColumn(cb, gridCol);
            Grid.SetRow(cb, gridRow);
            ColumnsGrid.Children.Add(cb);
            _checkBoxes.Add(cb);
        }
    }

    /// <summary>
    /// Apply checkbox states back to the column list. Call after dialog returns Primary.
    /// </summary>
    public void ApplySelections()
    {
        for (var i = 0; i < _checkBoxes.Count && i < _columns.Count; i++)
            _columns[i].Visible = _checkBoxes[i].IsChecked == true;
    }
}
