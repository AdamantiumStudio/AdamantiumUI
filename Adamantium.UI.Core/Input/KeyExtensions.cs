namespace Adamantium.UI.Core.Input;

/// <summary>What a key is, beyond which: one place for the lists the keyboard's handlers would otherwise each keep.</summary>
public static class KeyExtensions
{
    /// <summary>Either Alt, by either code.</summary>
    public static bool IsAlt(this Key key) => key is Key.Alt or Key.LeftAlt or Key.RightAlt;

    /// <summary>Alt, Control or Shift, either one, by either code: a key held with others, state more than a stroke.</summary>
    public static bool IsModifier(this Key key) => key.IsAlt()
        || key is Key.Ctrl or Key.LeftCtrl or Key.RightCtrl or Key.Shift or Key.LeftShift or Key.RightShift;
}
