using System;
using System.Runtime.InteropServices;
using Adamantium.MacOS.AppKit;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>The NSView subclass that fills a window: it takes the keyboard and hands its mouse and key events to the
/// window's worker.</summary>
internal static unsafe class MacOSContentView
{
    private const string Event = "v@:@";

    /// <summary>The class, registered with the runtime on first use.</summary>
    internal static readonly IntPtr Class = new ObjCClassBuilder("AdamantiumContentView", "NSView")
        .AddMethod("acceptsFirstResponder", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte>)&Yes, "B@:")
        .AddMethod("acceptsFirstMouse:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte>)&YesFor, "B@:@")
        .AddMethod("mouseDownCanMoveWindow", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte>)&No, "B@:")
        .AddMethod("mouseDown:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&LeftDown, Event)
        .AddMethod("mouseUp:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&LeftUp, Event)
        .AddMethod("rightMouseDown:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&RightDown, Event)
        .AddMethod("rightMouseUp:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&RightUp, Event)
        .AddMethod("otherMouseDown:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OtherDown, Event)
        .AddMethod("otherMouseUp:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OtherUp, Event)
        .AddMethod("mouseMoved:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Moved, Event)
        .AddMethod("mouseDragged:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Moved, Event)
        .AddMethod("rightMouseDragged:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Moved, Event)
        .AddMethod("otherMouseDragged:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Moved, Event)
        .AddMethod("mouseExited:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Exited, Event)
        .AddMethod("scrollWheel:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Scrolled, Event)
        .AddMethod("magnifyWithEvent:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Magnified, Event)
        .AddMethod("rotateWithEvent:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Rotated, Event)
        .AddMethod("keyDown:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&KeyDown, Event)
        .AddMethod("keyUp:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&KeyUp, Event)
        .AddMethod("flagsChanged:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&FlagsChanged, Event)
        .Register();

    [UnmanagedCallersOnly]
    private static byte Yes(IntPtr self, IntPtr selector) => 1;

    [UnmanagedCallersOnly]
    private static byte YesFor(IntPtr self, IntPtr selector, IntPtr nsEvent) => 1;

    [UnmanagedCallersOnly]
    private static byte No(IntPtr self, IntPtr selector) => 0;

    [UnmanagedCallersOnly]
    private static void LeftDown(IntPtr self, IntPtr selector, IntPtr nsEvent) =>
        Pointer(self, nsEvent, RawPointerEventType.Down, MouseButtons.Left);

    [UnmanagedCallersOnly]
    private static void LeftUp(IntPtr self, IntPtr selector, IntPtr nsEvent) =>
        Pointer(self, nsEvent, RawPointerEventType.Up, MouseButtons.Left);

    [UnmanagedCallersOnly]
    private static void RightDown(IntPtr self, IntPtr selector, IntPtr nsEvent) =>
        Pointer(self, nsEvent, RawPointerEventType.Down, MouseButtons.Right);

    [UnmanagedCallersOnly]
    private static void RightUp(IntPtr self, IntPtr selector, IntPtr nsEvent) =>
        Pointer(self, nsEvent, RawPointerEventType.Up, MouseButtons.Right);

    [UnmanagedCallersOnly]
    private static void OtherDown(IntPtr self, IntPtr selector, IntPtr nsEvent) =>
        Pointer(self, nsEvent, RawPointerEventType.Down, OtherButton(nsEvent));

    [UnmanagedCallersOnly]
    private static void OtherUp(IntPtr self, IntPtr selector, IntPtr nsEvent) =>
        Pointer(self, nsEvent, RawPointerEventType.Up, OtherButton(nsEvent));

    [UnmanagedCallersOnly]
    private static void Moved(IntPtr self, IntPtr selector, IntPtr nsEvent) =>
        Pointer(self, nsEvent, RawPointerEventType.Move, MouseButtons.None);

    [UnmanagedCallersOnly]
    private static void Exited(IntPtr self, IntPtr selector, IntPtr nsEvent) =>
        Pointer(self, nsEvent, RawPointerEventType.LeaveWindow, MouseButtons.None);

    [UnmanagedCallersOnly]
    private static void Scrolled(IntPtr self, IntPtr selector, IntPtr nsEvent) =>
        ObjCManagedObject.Get<MacOSWindowWorker>(self)?.OnScroll(nsEvent);

    [UnmanagedCallersOnly]
    private static void Magnified(IntPtr self, IntPtr selector, IntPtr nsEvent) =>
        ObjCManagedObject.Get<MacOSWindowWorker>(self)?.OnMagnify(nsEvent);

    [UnmanagedCallersOnly]
    private static void Rotated(IntPtr self, IntPtr selector, IntPtr nsEvent) =>
        ObjCManagedObject.Get<MacOSWindowWorker>(self)?.OnRotate(nsEvent);

    [UnmanagedCallersOnly]
    private static void KeyDown(IntPtr self, IntPtr selector, IntPtr nsEvent) =>
        ObjCManagedObject.Get<MacOSWindowWorker>(self)?.OnKey(nsEvent, true);

    [UnmanagedCallersOnly]
    private static void KeyUp(IntPtr self, IntPtr selector, IntPtr nsEvent) =>
        ObjCManagedObject.Get<MacOSWindowWorker>(self)?.OnKey(nsEvent, false);

    [UnmanagedCallersOnly]
    private static void FlagsChanged(IntPtr self, IntPtr selector, IntPtr nsEvent) =>
        ObjCManagedObject.Get<MacOSWindowWorker>(self)?.OnFlagsChanged(nsEvent);

    private static void Pointer(IntPtr self, IntPtr nsEvent, RawPointerEventType type, MouseButtons button) =>
        ObjCManagedObject.Get<MacOSWindowWorker>(self)?.OnPointer(nsEvent, type, button);

    private static MouseButtons OtherButton(IntPtr nsEvent) => NSEvent.ButtonNumberOf(nsEvent) switch
    {
        2 => MouseButtons.Middle,
        3 => MouseButtons.XButton1,
        4 => MouseButtons.XButton2,
        _ => MouseButtons.None
    };
}
