using Adamantium.MacOS;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Platforms.MacOS;

// AppKit cursors: missing shapes (Wait, AppStarting, Help, UpArrow, diagonal resize) fall back to the nearest NSCursor.
internal sealed class MacOSCursors : INativeCursors
{
    public void Apply(Cursor cursor)
    {
        if (cursor == null) return;
        if (cursor.Type == CursorType.None)
        {
            MacOSInterop.Cursor.Hide();
            return;
        }
        MacOSInterop.Cursor.SetCursorType((uint)Native(cursor.Type));
    }

    private static MacOSCursorType Native(CursorType type) => type switch
    {
        CursorType.Crosshair => MacOSCursorType.CrosshairCursor,
        CursorType.Hand => MacOSCursorType.PointingHandCursor,
        CursorType.IBeam => MacOSCursorType.IBeamCursor,
        CursorType.No => MacOSCursorType.OperationNotAllowedCursor,
        CursorType.SizeAll => MacOSCursorType.ClosedHandCursor,          // AppKit's "grabbing and moving" shape
        CursorType.SizeNS => MacOSCursorType.ResizeUpDownCursor,
        CursorType.SizeEWE => MacOSCursorType.ResizeLeftRightCursor,
        CursorType.SizeNESW => MacOSCursorType.ResizeLeftRightCursor,    // no diagonal shape in AppKit
        CursorType.SizeNWSE => MacOSCursorType.ResizeLeftRightCursor,
        CursorType.UpArrow => MacOSCursorType.ResizeUpCursor,
        CursorType.DragCopy => MacOSCursorType.DragCopyCursor,
        CursorType.DragLink => MacOSCursorType.DragLinkCursor,
        // Arrow, AppStarting, Help, Wait and a custom file (NSCursor needs an NSImage, not a .cur) all land here.
        _ => MacOSCursorType.ArrowCursor,
    };
}
