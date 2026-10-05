namespace Adamantium.UI.Core.Automation;

/// <summary>What <see cref="AutomationEvents.Raised"/> tells: which element, what happened, and for a property change the
/// property with its old and new value.</summary>
public sealed class AutomationEventArgs : EventArgs
{
    public AutomationEventArgs(IUIComponent element, AutomationPeer peer, AutomationEvent automationEvent,
        AutomationProperty property = default, object oldValue = null, object newValue = null)
    {
        Element = element;
        Peer = peer;
        Event = automationEvent;
        Property = property;
        OldValue = oldValue;
        NewValue = newValue;
    }

    /// <summary>The element it happened to - which window it is in.</summary>
    public IUIComponent Element { get; }

    /// <summary>The element as automation sees it.</summary>
    public AutomationPeer Peer { get; }

    public AutomationEvent Event { get; }

    /// <summary>The property that changed, for <see cref="AutomationEvent.PropertyChanged"/>.</summary>
    public AutomationProperty Property { get; }

    public object OldValue { get; }

    public object NewValue { get; }
}
