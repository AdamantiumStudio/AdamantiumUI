using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Platforms.Windows;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>Our input as CEF names it - modifiers and buttons as its event flags, a mouse button as its button type -
/// and the cursor a page asks for as one of ours.</summary>
internal static class CefInputMap
{
    /// <summary>The keys and mouse buttons held, as CEF's event flags.</summary>
    public static uint Flags(InputModifiers modifiers)
    {
        var flags = CefEventFlags.None;
        if ((modifiers & (InputModifiers.LeftShift | InputModifiers.RightShift)) != 0)
        {
            flags |= CefEventFlags.ShiftDown;
        }

        if ((modifiers & (InputModifiers.LeftControl | InputModifiers.RightControl)) != 0)
        {
            flags |= CefEventFlags.ControlDown;
        }

        if ((modifiers & (InputModifiers.LeftAlt | InputModifiers.RightAlt)) != 0)
        {
            flags |= CefEventFlags.AltDown;
        }

        if ((modifiers & InputModifiers.LeftMouseButton) != 0)
        {
            flags |= CefEventFlags.LeftMouseButton;
        }

        if ((modifiers & InputModifiers.MiddleMouseButton) != 0)
        {
            flags |= CefEventFlags.MiddleMouseButton;
        }

        if ((modifiers & InputModifiers.RightMouseButton) != 0)
        {
            flags |= CefEventFlags.RightMouseButton;
        }

        return (uint)flags;
    }

    /// <summary>The key's own code on this platform as Chromium takes it - on Windows the scan code, 0xE0 above it for an
    /// extended key - from which a page's KeyboardEvent.code comes; 0 where it is not known.</summary>
    public static int NativeKeyCode(uint physicalKey)
    {
        if (!OperatingSystem.IsWindows() || !Win32ScanCodes.TryFromHidUsage(physicalKey, out var scanCode, out var extended))
        {
            return 0;
        }

        return (scanCode & 0xFF) | (extended ? 0xE000 : 0);
    }

    /// <summary>The outcomes a drag allows, as CEF's operations.</summary>
    public static CefDragOperationsMask Operations(DragDropEffects effects)
    {
        var operations = CefDragOperationsMask.None;
        if ((effects & DragDropEffects.Copy) != 0)
        {
            operations |= CefDragOperationsMask.Copy;
        }

        if ((effects & DragDropEffects.Move) != 0)
        {
            operations |= CefDragOperationsMask.Move;
        }

        if ((effects & DragDropEffects.Link) != 0)
        {
            operations |= CefDragOperationsMask.Link;
        }

        return operations;
    }

    /// <summary>CEF's operations as our drag's outcomes; a generic one is a copy.</summary>
    public static DragDropEffects Effects(CefDragOperationsMask operations)
    {
        var effects = DragDropEffects.None;
        if ((operations & (CefDragOperationsMask.Copy | CefDragOperationsMask.Generic)) != 0)
        {
            effects |= DragDropEffects.Copy;
        }

        if ((operations & CefDragOperationsMask.Move) != 0)
        {
            effects |= DragDropEffects.Move;
        }

        if ((operations & CefDragOperationsMask.Link) != 0)
        {
            effects |= DragDropEffects.Link;
        }

        return effects;
    }

    /// <summary>What touches, as CEF names it: a finger, a pen, or a pen's eraser end.</summary>
    public static CefPointerType Toucher(Pointer pointer, PointerPointProperties properties) => pointer.Type switch
    {
        PointerType.Pen => properties.IsEraser ? CefPointerType.Eraser : CefPointerType.Pen,
        PointerType.Touch => CefPointerType.Touch,
        _ => CefPointerType.Mouse
    };

    /// <summary>CEF's button for <paramref name="button"/>; null for one it has none for (the side buttons).</summary>
    public static CefMouseButtonType? Button(MouseButtons button) => button switch
    {
        MouseButtons.Left => CefMouseButtonType.Left,
        MouseButtons.Middle => CefMouseButtonType.Middle,
        MouseButtons.Right => CefMouseButtonType.Right,
        _ => null
    };

    /// <summary>Our cursor for the one a page asks for; an arrow for one we have none like.</summary>
    public static Cursor Cursor(CefCursorType type) => type switch
    {
        CefCursorType.Cross or CefCursorType.Cell => Cursors.Crosshair,
        CefCursorType.Hand => Cursors.Hand,
        CefCursorType.Ibeam or CefCursorType.Verticaltext => Cursors.IBeam,
        CefCursorType.Wait => Cursors.Wait,
        CefCursorType.Progress => Cursors.AppStarting,
        CefCursorType.Help => Cursors.Help,
        CefCursorType.Eastresize or CefCursorType.Westresize or CefCursorType.Eastwestresize or
            CefCursorType.Columnresize => Cursors.SizeWE,
        CefCursorType.Northresize or CefCursorType.Southresize or CefCursorType.Northsouthresize or
            CefCursorType.Rowresize => Cursors.SizeNS,
        CefCursorType.Northeastresize or CefCursorType.Southwestresize or
            CefCursorType.Northeastsouthwestresize => Cursors.SizeNESW,
        CefCursorType.Northwestresize or CefCursorType.Southeastresize or
            CefCursorType.Northwestsoutheastresize => Cursors.SizeNWSE,
        CefCursorType.Move or CefCursorType.Middlepanning or CefCursorType.Grab or CefCursorType.Grabbing or
            CefCursorType.DndMove => Cursors.SizeAll,
        CefCursorType.Nodrop or CefCursorType.Notallowed or CefCursorType.DndNone => Cursors.No,
        CefCursorType.Copy or CefCursorType.DndCopy => Cursors.DragCopy,
        CefCursorType.Alias or CefCursorType.DndLink => Cursors.DragLink,
        CefCursorType.None => Cursors.None,
        _ => Cursors.Arrow
    };
}
