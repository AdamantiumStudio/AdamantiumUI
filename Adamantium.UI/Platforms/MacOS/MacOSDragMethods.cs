using System;
using System.Runtime.InteropServices;
using Adamantium.MacOS.AppKit;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>What the system tells the view a window's content is of drags over it and out of it (NSDraggingDestination,
/// NSDraggingSource). Main thread.</summary>
internal static unsafe class MacOSDragMethods
{
    private const nint WithinApplication = 1;

    internal static ObjCClassBuilder AddDragging(this ObjCClassBuilder view) => view
        .AddMethod("draggingEntered:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, ulong>)&Entered, "Q@:@")
        .AddMethod("draggingUpdated:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, ulong>)&Updated, "Q@:@")
        .AddMethod("draggingExited:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Exited, "v@:@")
        .AddMethod("performDragOperation:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte>)&Dropped, "B@:@")
        .AddMethod("draggingSession:sourceOperationMaskForDraggingContext:",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, nint, ulong>)&Allowed, "Q@:@q")
        .AddMethod("draggingSession:endedAtPoint:operation:",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, CGPoint, ulong, void>)&Ended, "v@:@{CGPoint=dd}Q")
        .AddProtocol("NSDraggingSource");

    private static MacOSWindowWorker Worker(IntPtr self) => ObjCManagedObject.Get<MacOSWindowWorker>(self);

    [UnmanagedCallersOnly]
    private static ulong Entered(IntPtr self, IntPtr selector, IntPtr info) =>
        (ulong)(Worker(self)?.OnDragOver(info, true) ?? NSDragOperation.None);

    [UnmanagedCallersOnly]
    private static ulong Updated(IntPtr self, IntPtr selector, IntPtr info) =>
        (ulong)(Worker(self)?.OnDragOver(info, false) ?? NSDragOperation.None);

    [UnmanagedCallersOnly]
    private static void Exited(IntPtr self, IntPtr selector, IntPtr info) => Worker(self)?.OnDragLeft();

    [UnmanagedCallersOnly]
    private static byte Dropped(IntPtr self, IntPtr selector, IntPtr info) =>
        Worker(self)?.OnDropped(info) == true ? (byte)1 : (byte)0;

    [UnmanagedCallersOnly]
    private static ulong Allowed(IntPtr self, IntPtr selector, IntPtr session, nint context) =>
        (ulong)(Worker(self)?.AllowedDragOperations(context == WithinApplication) ?? NSDragOperation.None);

    [UnmanagedCallersOnly]
    private static void Ended(IntPtr self, IntPtr selector, IntPtr session, CGPoint point, ulong operation) =>
        Worker(self)?.OnDragEnded((NSDragOperation)operation);
}
