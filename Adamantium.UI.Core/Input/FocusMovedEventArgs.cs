namespace Adamantium.UI.Core.Input;

/// <summary>A move of the focus, told by <see cref="FocusManager.FocusMoved"/> once it is over.</summary>
public sealed class FocusMovedEventArgs(IInputComponent oldFocus, IInputComponent newFocus,
    NavigationMethod navigationMethod, InputModifiers modifiers) : EventArgs
{
    /// <summary>Where the focus was; null when nothing had it.</summary>
    public IInputComponent OldFocus { get; } = oldFocus;

    /// <summary>Where it is now; null when it was let go of.</summary>
    public IInputComponent NewFocus { get; } = newFocus;

    /// <summary>How it came: a click, Tab, an arrow, or set by code.</summary>
    public NavigationMethod NavigationMethod { get; } = navigationMethod;

    /// <summary>The modifiers held as it came.</summary>
    public InputModifiers Modifiers { get; } = modifiers;
}
