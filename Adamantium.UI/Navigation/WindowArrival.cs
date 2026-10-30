using System;
using System.Threading.Tasks;
using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Navigation;

/// <summary>A window on its way to the screen: settled once it is shown, hidden or minimized there, or closed first.</summary>
internal sealed class WindowArrival
{
    private readonly IWindow _window;
    private readonly TaskCompletionSource _settled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public WindowArrival(IWindow window)
    {
        _window = window;
        window.Displayed += OnShown;
        window.Hidden += OnShown;
        window.StateChanged += OnStateChanged;
        window.Closed += OnClosed;
    }

    /// <summary>Completes once the window has settled.</summary>
    public Task Settled => _settled.Task;

    /// <summary>Whether the window closed before it came on screen.</summary>
    public bool ClosedFirst { get; private set; }

    private void OnShown(object sender, EventArgs e) => Settle();

    private void OnStateChanged(object sender, StateChangedEventArgs e)
    {
        if (_window.State == WindowState.Minimized)
        {
            Settle();
        }
    }

    private void OnClosed(object sender, EventArgs e)
    {
        ClosedFirst = true;
        Settle();
    }

    private void Settle()
    {
        _window.Displayed -= OnShown;
        _window.Hidden -= OnShown;
        _window.StateChanged -= OnStateChanged;
        _window.Closed -= OnClosed;
        _settled.TrySetResult();
    }
}
