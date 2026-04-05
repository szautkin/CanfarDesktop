using Microsoft.Extensions.DependencyInjection;
using CanfarDesktop.ViewModels;

namespace CanfarDesktop.Services.Fits;

public class FitsTabFactory(IServiceProvider sp) : IFitsTabFactory
{
    public FitsViewerTabItem CreateTab()
    {
        var vm = sp.GetRequiredService<FitsViewerViewModel>();
        return new FitsViewerTabItem(vm);
    }
}
