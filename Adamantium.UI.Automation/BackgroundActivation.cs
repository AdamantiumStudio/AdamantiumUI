using System;
using System.Linq;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Internals;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Automation;

/// <summary>A driven application's windows open in the background (<see cref="AutomationAgentExtensions"/>), so the
/// system never makes one of them the active window - and nothing that follows the active window happens: a form shown
/// does not take the keyboard, a hosted view clicked into is not the one worked in. This makes them active inside the
/// application, as the system would have made them had they opened in the foreground: a window becomes the active one
/// as it shows, and when the active one closes, the window shown last of those left is. The system's foreground is
/// never touched.</summary>
internal sealed class BackgroundActivation : IDisposable
{
    private readonly IUIApplication _application;

    public BackgroundActivation(IUIApplication application)
    {
        _application = application;
        AutomationEvents.Raised += OnRaised;
    }

    public void Dispose() => AutomationEvents.Raised -= OnRaised;

    // Raised on the thread of the window's own loop: a splash window's is not the application's, and it is left alone.
    private void OnRaised(object sender, AutomationEventArgs e)
    {
        if (e.Element is not WindowBase window || !OpensInBackground(window))
        {
            return;
        }

        switch (e.Event)
        {
            case AutomationEvent.WindowOpened:
                LoopSignal.Post(() => Activate(window));
                break;
            case AutomationEvent.WindowClosed:
                LoopSignal.Post(() => HandOn(window));
                break;
        }
    }

    /// <summary>Whether the window would have taken the foreground as it showed, had automation not kept it in the
    /// background: an overlay that never takes it says so itself, and a splash window is not one of the application's.
    /// </summary>
    private static bool OpensInBackground(WindowBase window) =>
        window is not SplashWindow && !window.ActivateOnShow &&
        window.GetValueSource(WindowBase.ActivateOnShowProperty) >= ValuePriority.TypeDefault;

    private void Activate(WindowBase window)
    {
        if (window.IsClosed || !window.IsDisplayed || window.IsActive)
        {
            return;
        }

        var wereActive = _application.Windows
            .Where(other => other.IsActive && other is not SplashWindow && !ReferenceEquals(other, window))
            .ToList();
        foreach (var other in wereActive)
        {
            other.StopBeingActive(_application);
        }

        window.BecomeActive(_application);
    }

    private void HandOn(WindowBase closed)
    {
        if (!closed.IsActive)
        {
            return;
        }

        closed.StopBeingActive(_application);
        var next = _application.Windows.LastOrDefault(window => !ReferenceEquals(window, closed) && !window.IsClosed &&
                                                                window.IsDisplayed && window is WindowBase shown &&
                                                                OpensInBackground(shown));
        if (next is WindowBase { IsActive: false } nextWindow)
        {
            Activate(nextWindow);
        }
    }
}
