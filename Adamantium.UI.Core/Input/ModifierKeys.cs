namespace Adamantium.UI.Core.Input;

/// <summary>The modifier keys of a <see cref="KeyGesture"/>, the left and the right key alike.</summary>
[Flags]
public enum ModifierKeys
{
    None = 0,

    Control = 1,

    Alt = 2,

    Shift = 4,

    /// <summary>The Windows key, or Command on a Mac.</summary>
    Meta = 8
}
