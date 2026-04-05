namespace CanfarDesktop.ViewModels;

using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

/// <summary>
/// Wraps a FitsViewerViewModel for use as a tab item.
/// Header shows the filename (no dirty indicator — FITS is read-only viewing).
/// </summary>
public partial class FitsViewerTabItem : ObservableObject
{
    public FitsViewerViewModel ViewModel { get; }

    [ObservableProperty] private string _header;

    private readonly PropertyChangedEventHandler _vmHandler;

    public FitsViewerTabItem(FitsViewerViewModel viewModel)
    {
        ViewModel = viewModel;
        _header = viewModel.Title;

        _vmHandler = (_, e) =>
        {
            if (e.PropertyName == nameof(FitsViewerViewModel.Title))
                Header = viewModel.Title;
        };
        viewModel.PropertyChanged += _vmHandler;
    }

    public void Close()
    {
        ViewModel.PropertyChanged -= _vmHandler;
        ViewModel.Cleanup();
    }
}
