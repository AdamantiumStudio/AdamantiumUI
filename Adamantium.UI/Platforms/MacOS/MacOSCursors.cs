using System;
using Adamantium.MacOS.AppKit;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>AppKit's cursors: the shapes AppKit lacks (wait, help, diagonal resizing) are its nearest.</summary>
internal sealed class MacOSCursors(MacOSPlatform platform) : INativeCursors
{
    private bool _hidden;

    public void Apply(Cursor cursor)
    {
        if (cursor == null)
            return;

        var type = cursor.Type;
        platform.RunOnMainThread(() => Show(type));
    }

    private void Show(CursorType type)
    {
        if (type == CursorType.None)
        {
            if (!_hidden)
                NSCursor.Hide();
            _hidden = true;
            return;
        }

        if (_hidden)
            NSCursor.Unhide();
        _hidden = false;
        NSCursor.Set(Native(type));
    }

    private static IntPtr Native(CursorType type) => type switch
    {
        CursorType.Crosshair => NSCursor.Crosshair,
        CursorType.Hand => NSCursor.PointingHand,
        CursorType.IBeam => NSCursor.IBeam,
        CursorType.No => NSCursor.OperationNotAllowed,
        CursorType.SizeAll => NSCursor.ClosedHand,
        CursorType.SizeNS => NSCursor.ResizeUpDown,
        CursorType.SizeWE or CursorType.SizeNESW or CursorType.SizeNWSE => NSCursor.ResizeLeftRight,
        CursorType.UpArrow => NSCursor.ResizeUp,
        CursorType.DragCopy => NSCursor.DragCopy,
        CursorType.DragLink => NSCursor.DragLink,
        _ => NSCursor.Arrow
    };
}
