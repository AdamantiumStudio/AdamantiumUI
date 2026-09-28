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
    /// means for this decision.</summary>
    public void Restore(IUIComponent content)
    {
        var element = _element;
        _element = null;
        if (element == null || !FocusManager.CanFocus(element))
            return;

        var focused = FocusManager.Focused;
        if (focused != null && !IsInside(focused, content))
            return;

        FocusManager.Focus(element, _wasVisible ? NavigationMethod.Tab : NavigationMethod.Mouse);
    }

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
