namespace Adamantium.UI.Core.Automation;

/// <summary>Tells whoever listens - a platform's accessibility bridge, an automation agent - what changes on elements.
/// While nobody listens nothing is raised, and no peer is made only to report a change.</summary>
public static class AutomationEvents
{
    /// <summary>Raised on the UI loop thread, as the change happens.</summary>
    public static event EventHandler<AutomationEventArgs> Raised;

    /// <summary>Whether anyone listens; asked before any work is done to report a change.</summary>
    public static bool IsListening => Raised != null;

    /// <summary>Tells that <paramref name="automationEvent"/> happened to <paramref name="element"/> - or to its nearest
    /// ancestor automation knows, when the element itself has no peer.</summary>
    public static void Raise(IUIComponent element, AutomationEvent automationEvent)
    {
        if (Raised != null && PeerOf(element, out var owner) is { } peer)
        {
            Raised(owner, new AutomationEventArgs(owner, peer, automationEvent));
        }
    }

    /// <summary>Tells that <paramref name="property"/> of <paramref name="element"/> went from
    /// <paramref name="oldValue"/> to <paramref name="newValue"/> - only once automation has seen the element: before that
    /// nobody holds the old value, so the element being built is not reported as changing.</summary>
    public static void RaisePropertyChanged(IUIComponent element, AutomationProperty property, object oldValue, object newValue)
    {
        if (Raised != null && !Equals(oldValue, newValue) && element?.FindAutomationPeer() is { } peer)
        {
            Raised(element, new AutomationEventArgs(element, peer, AutomationEvent.PropertyChanged, property, oldValue, newValue));
        }
    }

    /// <summary>Tells that <paramref name="element"/> expanded or collapsed.</summary>
    public static void RaiseExpandCollapse(IUIComponent element, bool expanded)
    {
        if (Raised != null)
        {
            RaisePropertyChanged(element, AutomationProperty.ExpandCollapseState,
                expanded ? ExpandCollapseState.Collapsed : ExpandCollapseState.Expanded,
                expanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
        }
    }

    /// <summary>Tells that <paramref name="element"/> became selected or stopped being so, once automation has seen it.</summary>
    public static void RaiseSelected(IUIComponent element, bool selected)
    {
        if (Raised != null && element?.FindAutomationPeer() != null)
        {
            RaisePropertyChanged(element, AutomationProperty.IsSelected, !selected, selected);
            if (selected)
            {
                Raise(element, AutomationEvent.ElementSelected);
            }
        }
    }

    private static AutomationPeer PeerOf(IUIComponent element, out IUIComponent owner)
    {
        for (owner = element; owner != null; owner = owner.VisualParent)
        {
            if (owner.GetAutomationPeer() is { } peer)
            {
                return peer;
            }
        }

        return null;
    }
}
