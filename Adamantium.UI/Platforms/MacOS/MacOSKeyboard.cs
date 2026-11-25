using System;
using System.Collections.Generic;
using Adamantium.MacOS.AppKit;
using Adamantium.MacOS.Carbon;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>The keys of the keyboard layout in use. A layout of no Latin letters - Russian, say - takes them from the
/// Latin one in use beside it, so the shortcuts stay. Main thread only.</summary>
internal sealed class MacOSKeyboard
{
    private readonly Dictionary<ushort, Key> _held = new();
    private Dictionary<ushort, Key> _keys = new();
    private Dictionary<ushort, Key> _keysWithCommand = new();
    private IntPtr _layout;

    /// <summary>The key of a key event in the layout in use - with Command held, the one the layout has for shortcuts.
    /// </summary>
    public Key KeyOf(IntPtr nsEvent)
    {
        var current = KeyboardLayout.CopyCurrentLatin();
        if (KeyboardLayout.AreEqual(current, _layout))
            KeyboardLayout.Release(current);
        else
        {
            KeyboardLayout.Release(_layout);
            _layout = current;
            _keys = MacOSKeys.InLayout(code => KeyboardLayout.CharacterOf(_layout, code));
            _keysWithCommand = MacOSKeys.InLayout(code => KeyboardLayout.CharacterOf(_layout, code, true));
        }

        var keyCode = NSEvent.KeyCodeOf(nsEvent);
        var keys = (NSEvent.ModifierFlagsOf(nsEvent) & NSEventModifierFlags.Command) != 0 ? _keysWithCommand : _keys;
        return keys.TryGetValue(keyCode, out var key) ? key : MacOSKeys.ToKey(keyCode);
    }

    /// <summary>The key pressed, or let go of: the one it was pressed as, though the layout has changed since.</summary>
    public Key KeyOf(IntPtr nsEvent, bool down)
    {
        var keyCode = NSEvent.KeyCodeOf(nsEvent);
        if (down)
            return _held[keyCode] = KeyOf(nsEvent);

        return _held.Remove(keyCode, out var pressed) ? pressed : KeyOf(nsEvent);
    }
}
