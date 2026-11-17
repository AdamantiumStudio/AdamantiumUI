namespace Adamantium.UI.Core.Input;

/// <summary>
/// Alt pressed and released with nothing else between - the key that opens a window's menu bar in Windows, the
/// Ribbon's key tips and a browser's menu. It is half of something else when a modifier is already held as Alt goes
/// down (Shift switches the layout; AltGr comes as Ctrl and Alt), when another key goes down or up between - a key
/// whose press the system keeps, PrintScreen, shows only its release - or when the owner calls <see cref="Cancel"/>:
/// a pointer press, a wheel turn, the window losing activation.
/// </summary>
public sealed class AltPressedAlone
{
    private const InputModifiers OtherModifiers = InputModifiers.LeftControl | InputModifiers.RightControl |
                                                  InputModifiers.LeftShift | InputModifiers.RightShift |
                                                  InputModifiers.LeftWindows | InputModifiers.RightWindows;

    private bool _alone;
    private Key? _held;

    /// <summary>Whether the last key down repeated - a key not released yet counts too, as the platform misses the
    /// first autorepeat.</summary>
    public bool LastWasRepeat { get; private set; }

    /// <summary>Follows a key going down; every key down the owner sees, handled or not.</summary>
    public void KeyDown(KeyEventArgs e)
    {
        LastWasRepeat = e.IsRepeated || e.Key == _held;
        _held = e.Key;
        if (!e.Key.IsAlt())
        {
            _alone = false;
            return;
        }

        // Alt held through a drag repeats: the press that made it a gesture is not forgotten on a repeat.
        if (!LastWasRepeat)
        {
            _alone = (e.Modifiers & OtherModifiers) == 0;
        }
    }

    /// <summary>Follows a key coming up; true when this release completes the gesture.</summary>
    public bool KeyUp(KeyEventArgs e)
    {
        if (e.Key == _held)
        {
            _held = null;
        }

        var completes = _alone && e.Key.IsAlt();
        _alone = false;
        return completes;
    }

    /// <summary>The gesture became half of something else: Alt's release completes nothing.</summary>
    public void Cancel() => _alone = false;
}
