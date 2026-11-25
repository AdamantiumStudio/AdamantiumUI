using System;
using System.Collections.Generic;
using Adamantium.MacOS.Carbon;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>The keys of the keyboard layout in use. A layout of no Latin letters - Russian, say - takes them from the
/// Latin one in use beside it, so the shortcuts stay. Main thread only.</summary>
internal sealed class MacOSKeyboard
{
    private Dictionary<ushort, Key> _keys = new();
    private readonly Dictionary<ushort, Key> _held = new();
    private IntPtr _layout;

    /// <summary>The key of hardware code <paramref name="keyCode"/> in the layout in use.</summary>
    public Key KeyOf(ushort keyCode)
    {
        var current = KeyboardLayout.CopyCurrentLatin();
        if (KeyboardLayout.AreEqual(current, _layout))
            KeyboardLayout.Release(current);
        else
        {
            KeyboardLayout.Release(_layout);
            _layout = current;
            _keys = MacOSKeys.InLayout(code => KeyboardLayout.CharacterOf(_layout, code));
        }

        return _keys.TryGetValue(keyCode, out var key) ? key : MacOSKeys.ToKey(keyCode);
    }

    /// <summary>The key pressed, or let go of: the one it was pressed as, though the layout has changed since.</summary>
    public Key KeyOf(ushort keyCode, bool down)
    {
        if (down)
            return _held[keyCode] = KeyOf(keyCode);

        return _held.Remove(keyCode, out var pressed) ? pressed : KeyOf(keyCode);
    }
}
