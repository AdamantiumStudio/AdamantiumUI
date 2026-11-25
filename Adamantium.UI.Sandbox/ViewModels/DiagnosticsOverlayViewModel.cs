using System;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.MVVM;
using Adamantium.Navigation;
using Adamantium.UI.Sandbox.Localization;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>The live diagnostics in a window of their own over the page: moved where they do not cover what is watched,
/// collapsed to their title when not read. Opened by a Diagnostics switch, closed by it - the token it is shown with - or
/// by the window's own close button. A window with a presentation of its own - the ribbon's swapchain - passes itself as
/// "presentation", and its choice shows above the statistics.</summary>
[ViewModel]
public partial class DiagnosticsOverlayViewModel : AdamantiumViewModel, IOverlayAware
{
    // Opened at the window's top-right, under the switch: the room from its right and top edges.
    [Bindable] private double left = 16;
    [Bindable] private double top = 140;

    public string Title => MainStrings.Diagnostics;

    public bool CanCollapse => true;

    public OverlayStartupLocation StartupLocation => OverlayStartupLocation.TopRight;


    /// <summary>The window whose presentation is chosen here - its PresentPolicies and PresentPolicy - if it has its own.</summary>
    public object Presentation { get; private set; }

    /// <summary>Whether a window passed its presentation: the chooser shows only then.</summary>
    public bool HasPresentation => Presentation != null;

    public event Action<object> RequestClose;

    public Task OnOverlayOpenedAsync(NavigationParameters parameters, CancellationToken cancellationToken = default)
    {
        if (parameters != null && parameters.TryGetValue<object>("presentation", out var presentation))
        {
            Presentation = presentation;
        }

        // A window whose own switch stands lower - under a band - opens it under that switch.
        if (parameters != null && parameters.TryGetValue<double>("top", out var fromTop))
        {
            Top = fromTop;
        }

        cancellationToken.Register(() => RequestClose?.Invoke(null));
        return Task.CompletedTask;
    }
}
