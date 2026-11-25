using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.Navigation;
using Adamantium.UI.Controls.Navigation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;

namespace Adamantium.UI.Controls;

/// <summary>Default overlay service: resolves the view model through DI, resolves its view, and shows it as a non-modal,
/// draggable, resizable, pinnable <see cref="OverlayWindow"/> on the active window's overlay via the
/// <see cref="OverlayWindowManager"/>. The UI-aware implementation of the UI-free <see cref="IOverlayService"/>.</summary>
public sealed class OverlayService : IOverlayService
{
    private readonly IUIApplication _application;
    private readonly IViewLocator _viewLocator;
    private readonly IDependencyResolver _resolver;

    public OverlayService(IUIApplication application, IViewLocator viewLocator, IDependencyResolver resolver)
    {
        _application = application;
        _viewLocator = viewLocator;
        _resolver = resolver;
    }

    public async Task<object> ShowOverlayAsync(Type overlayViewModelType, NavigationParameters parameters = null, CancellationToken cancellationToken = default)
    {
        // The overlay lives on a window's popup layer; without a window there is nowhere to show it.
        if (_application.FrontWindow is not IPopupHost host)
        {
            return null;
        }

        // Made for this showing alone, and done with when it ends, as a closed document's is - however it ends.
        var viewModel = _resolver.Resolve(overlayViewModelType);
        try
        {
            return await ShowAsync(host, viewModel, parameters, cancellationToken);
        }
        finally
        {
            (viewModel as IDisposable)?.Dispose();
        }
    }

    private async Task<object> ShowAsync(IPopupHost host, object viewModel, NavigationParameters parameters, CancellationToken cancellationToken)
    {
        var aware = viewModel as IOverlayAware;
        OverlayWindow window = null;
        var closedWhileOpening = false;
        object earlyResult = null;

        void OnRequestClose(object result)
        {
            if (window != null)
            {
                window.Close(result);
                return;
            }
            closedWhileOpening = true;
            earlyResult = result;
        }

        if (aware != null)
        {
            aware.RequestClose += OnRequestClose;
            await aware.OnOverlayOpenedAsync(parameters ?? new NavigationParameters(), cancellationToken);
            if (closedWhileOpening)
            {
                aware.RequestClose -= OnRequestClose;
                return earlyResult;
            }
        }

        // Chrome options come from the view model (IOverlayAware defaults them to a plain floating window).
        window = new OverlayWindow
        {
            Title = aware?.Title ?? string.Empty,
            Icon = aware?.Icon,
            IsModal = aware?.IsModal ?? false,
            AllowMove = aware?.AllowMove ?? true,
            CanResize = aware?.CanResize ?? true,
            CanPin = aware?.CanPin ?? true,
            CanCollapse = aware?.CanCollapse ?? false,
            CanClose = aware?.CanClose ?? true,
            StartupLocation = aware?.StartupLocation ?? OverlayStartupLocation.CenterOwner,
            Content = _viewLocator.ResolveView(viewModel)
        };
        if (aware?.AutomationId is { } automationId)
        {
            Core.Automation.AutomationProperties.SetAutomationId(window, automationId);
        }

        // Live title: the view model may change its Title while open; reflect it on the bar.
        if (aware is INotifyPropertyChanged inpc)
        {
            void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(IOverlayAware.Title)) window.Title = aware.Title;
            }
            inpc.PropertyChanged += OnPropertyChanged;
            window.Closed += (_, _) => inpc.PropertyChanged -= OnPropertyChanged;
        }

        // The view model closes itself (with a result) via RequestClose; the x button / Escape close it with null.
        if (aware != null)
        {
            // Two-way bind the window's Left/Top to the view model's: the VM can move the window by assigning them, and a
            // drag writes them back. A VM without settable, notifying Left/Top properties leaves these inert (the source
            // path doesn't resolve, so the binding no-ops). Set up before Show so a Manual window opens at the VM's value.
            window.SetBinding(OverlayWindow.LeftProperty, new Binding(nameof(IOverlayAware.Left)) { Source = aware, Mode = BindingMode.TwoWay });
            window.SetBinding(OverlayWindow.TopProperty, new Binding(nameof(IOverlayAware.Top)) { Source = aware, Mode = BindingMode.TwoWay });

            // The width goes one way: the window coerces its own while collapsed, which is not for the view model to keep.
            BindIfDeclared(OverlayWindow.WidthProperty, nameof(IOverlayAware.Width), BindingMode.OneWay);
            BindIfDeclared(OverlayWindow.IsCollapsedProperty, nameof(IOverlayAware.IsCollapsed), BindingMode.TwoWay);
            window.Closed += (_, _) => aware.RequestClose -= OnRequestClose;
        }

        // Made for this showing alone: closed, it goes, its view with it.
        window.Closed += (_, _) => DiscardedVisuals.PublishSubtree(window);

        return await OverlayWindowManager.GetFor(host).ShowAsync(window);

        // A path the view model's own type does not have would be reported as a broken binding.
        void BindIfDeclared(AdamantiumProperty property, string name, BindingMode mode)
        {
            if (aware.GetType().GetProperty(name) != null)
            {
                window.SetBinding(property, new Binding(name) { Source = aware, Mode = mode });
            }
        }
    }

    public Task<object> ShowOverlayAsync<TOverlayViewModel>(NavigationParameters parameters = null, CancellationToken cancellationToken = default)
        where TOverlayViewModel : IOverlayAware
        => ShowOverlayAsync(typeof(TOverlayViewModel), parameters, cancellationToken);
}
