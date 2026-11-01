using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls;

/// <summary>Remembers where focus was when an overlay opened and restores it on close, but only if focus is still inside
/// the overlay (or nowhere); focus the user moved elsewhere stays put.</summary>
public sealed class FocusReturn
{
    private IInputComponent _element;
    private bool _wasVisible;

    /// <summary>Called as the overlay opens: remembers the element AND whether it was wearing the focus ring, so the
    /// way back looks the way it did - a menu opened from the keyboard returns a visible focus, one opened by a click
    /// returns a quiet one.</summary>
    public void Capture()
    {
        _element = FocusManager.Focused;
        _wasVisible = FocusManager.IsFocusVisible;
    }

    /// <summary>Called as the overlay closes. <paramref name="content"/> is the overlay's own content - what "inside"
    /// means for this decision. An element gone from its window since - its page torn down, the row a command deleted,
    /// the item of a menu closed since that opened the overlay - is not given the focus back; given
    /// <paramref name="around"/>, the element the overlay stands on, what had the focus last in its focus scope gets it
    /// instead - the field a menu was opened from.</summary>
    public void Restore(IUIComponent content, IUIComponent around = null)
    {
        var element = _element;
        _element = null;
        if (!CanTakeBack(element))
        {
            element = around != null ? FocusManager.GetFocusedElement(FocusManager.GetFocusScope(around)) : null;
            if (!CanTakeBack(element) || element is IUIComponent inside && IsInside(inside, content))
                return;
        }

        var focused = FocusManager.Focused;
        if (focused != null && !IsInside(focused, content))
            return;

        FocusManager.Focus(element, _wasVisible ? NavigationMethod.Tab : NavigationMethod.Mouse);
    }

    private static bool CanTakeBack(IInputComponent element) =>
        element != null && FocusManager.CanFocus(element) &&
        element is not (IUIComponent { RootVisual: null } or FundamentalUIComponent { IsDiscarded: true });

    private static bool IsInside(IUIComponent element, IUIComponent content)
    {
        for (var node = element; node != null; node = node.VisualParent)
        {
            if (ReferenceEquals(node, content))
                return true;
        }

        return false;
    }
}
