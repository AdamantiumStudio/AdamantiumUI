namespace Adamantium.UI.Diagnostics;

/// <summary>A debugging tool an application attaches with <see cref="UIApplication.AttachDebugTool"/>. Attached, it
/// is handed the application: its windows and their trees, the frame verifier, and every routed event, which it
/// watches through <see cref="Core.RoutedEvents.RoutedEvent.AddObserver"/> - only the events it watches pay for it.</summary>
public interface IDebugTool
{
    /// <summary>The tool starts on <paramref name="application"/>.</summary>
    void Attach(UIApplication application);

    /// <summary>The tool stops: it removes what it added (observers, subscriptions). Told as well when the application
    /// shuts down, on the thread that shuts it down (or, for a tool still attaching then, when its Attach returns); what
    /// it throws then is logged and the shutdown goes on. Never runs while Attach does.</summary>
    void Detach();
}
