namespace CanfarDesktop.Services.Notebook;

using Microsoft.Extensions.DependencyInjection;
using CanfarDesktop.ViewModels.Notebook;

/// <summary>
/// Resolves a fresh NotebookViewModel from DI for each new tab.
/// Transient registrations (DirtyTracker, AutoSaveService, KernelService)
/// ensure each tab gets its own independent instances.
/// </summary>
public class NotebookTabFactory(IServiceProvider sp) : INotebookTabFactory
{
    public NotebookTabItem CreateTab()
    {
        var vm = sp.GetRequiredService<NotebookViewModel>();
        return new NotebookTabItem(vm);
    }
}
