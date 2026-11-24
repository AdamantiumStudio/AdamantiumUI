using System;
using System.Runtime.InteropServices;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>The NSWindowDelegate of a window: it tells the window's worker of a close asked for, a resize, a move, a
/// change of screen scale, the keyboard coming and going, the Dock, zooming and the full screen.</summary>
internal static unsafe class MacOSWindowDelegate
{
    private const string Notification = "v@:@";

    /// <summary>The class, registered with the runtime on first use.</summary>
    internal static readonly IntPtr Class = new ObjCClassBuilder("AdamantiumWindowDelegate", "NSObject")
        .AddProtocol("NSWindowDelegate")
        .AddMethod("windowShouldClose:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte>)&ShouldClose, "B@:@")
        .AddMethod("windowDidResize:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Resized, Notification)
        .AddMethod("windowDidMove:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Moved, Notification)
        .AddMethod("windowDidChangeBackingProperties:",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&ScaleChanged, Notification)
        .AddMethod("windowDidChangeScreen:",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&ScaleChanged, Notification)
        .AddMethod("windowDidBecomeKey:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&BecameKey, Notification)
        .AddMethod("windowDidResignKey:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&ResignedKey, Notification)
        .AddMethod("windowDidMiniaturize:",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Miniaturized, Notification)
        .AddMethod("windowDidDeminiaturize:",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Deminiaturized, Notification)
        .AddMethod("windowShouldZoom:toFrame:",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, CGRect, byte>)&ShouldZoom,
            "B@:@{CGRect={CGPoint=dd}{CGSize=dd}}")
        .AddMethod("windowWillEnterFullScreen:",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&EnteringFullScreen, Notification)
        .AddMethod("windowDidEnterFullScreen:",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Resized, Notification)
        .AddMethod("windowDidExitFullScreen:",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Resized, Notification)
        .Register();

    [UnmanagedCallersOnly]
    private static byte ShouldZoom(IntPtr self, IntPtr selector, IntPtr window, CGRect frame)
    {
        Worker(self)?.OnLeavingNormal();
        return 1;
    }

    [UnmanagedCallersOnly]
    private static void EnteringFullScreen(IntPtr self, IntPtr selector, IntPtr notification) =>
        Worker(self)?.OnLeavingNormal();

    [UnmanagedCallersOnly]
    private static byte ShouldClose(IntPtr self, IntPtr selector, IntPtr sender)
    {
        Worker(self)?.OnCloseRequested();
        return 0;
    }

    [UnmanagedCallersOnly]
    private static void Resized(IntPtr self, IntPtr selector, IntPtr notification) => Worker(self)?.OnResized();

    [UnmanagedCallersOnly]
    private static void Moved(IntPtr self, IntPtr selector, IntPtr notification) => Worker(self)?.OnMoved();

    [UnmanagedCallersOnly]
    private static void ScaleChanged(IntPtr self, IntPtr selector, IntPtr notification) => Worker(self)?.OnScaleChanged();

    [UnmanagedCallersOnly]
    private static void BecameKey(IntPtr self, IntPtr selector, IntPtr notification) => Worker(self)?.OnKeyWindowChanged(true);

    [UnmanagedCallersOnly]
    private static void ResignedKey(IntPtr self, IntPtr selector, IntPtr notification) =>
        Worker(self)?.OnKeyWindowChanged(false);

    [UnmanagedCallersOnly]
    private static void Miniaturized(IntPtr self, IntPtr selector, IntPtr notification) => Worker(self)?.OnMiniaturized(true);

    [UnmanagedCallersOnly]
    private static void Deminiaturized(IntPtr self, IntPtr selector, IntPtr notification) =>
        Worker(self)?.OnMiniaturized(false);

    private static MacOSWindowWorker Worker(IntPtr self) => ObjCManagedObject.Get<MacOSWindowWorker>(self);
}
