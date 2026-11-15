namespace Adamantium.UI.Core.Input;

/// <summary>Where a <see cref="KeyBinding"/> runs from.</summary>
public enum KeyBindingReach
{
    /// <summary>From its element and the elements around the focus first, then from anywhere in the window when nothing
    /// on the way up from the focus took the key.</summary>
    Window,

    /// <summary>Only while the focus is in the element it is set on.</summary>
    FocusWithin
}
