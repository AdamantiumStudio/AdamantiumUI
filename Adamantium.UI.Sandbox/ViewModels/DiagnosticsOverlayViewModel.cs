using System;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.MVVM;
using Adamantium.Navigation;
using Adamantium.UI.Sandbox.Localization;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>The live diagnostics in a window of their own over the page: moved where they do not cover what is watched,
/// collapsed to their title when not read. Opened by the Diagnostics switch, closed by it - the token it is shown with -
/// or by the window's own close button.</summary>
[ViewModel]
public partial class DiagnosticsOverlayViewModel : AdamantiumViewModel, IOverlayAware
{
    [Bindable] private double left = 16;
    [Bindable] private double top = 140;

    public string Title => MainStrings.Diagnostics;

    public bool CanCollapse => true;

    public OverlayStartupLocation StartupLocation => OverlayStartupLocation.Manual;

    public event Action<object> RequestClose;

    public Task OnOverlayOpenedAsync(NavigationParameters parameters, CancellationToken cancellationToken = default)
    {
        cancellationToken.Register(() => RequestClose?.Invoke(null));
        return Task.CompletedTask;
    }
}
