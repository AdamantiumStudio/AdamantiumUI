using System;
using System.Runtime.InteropServices;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>The NSApplicationDelegate: quitting - from the Dock, at logout - is asked of the application, which closes its
/// windows, any of which may refuse; AppKit itself would end the process on the spot.</summary>
internal static unsafe class MacOSApplicationDelegate
{
    private const nint TerminateCancel = 0;

    /// <summary>The class, registered with the runtime on first use.</summary>
    internal static readonly IntPtr Class = new ObjCClassBuilder("AdamantiumApplicationDelegate", "NSObject")
        .AddProtocol("NSApplicationDelegate")
        .AddMethod("applicationShouldTerminate:",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, nint>)&ShouldTerminate, "q@:@")
        .AddMethod("applicationDidUnhide:",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Unhidden, "v@:@")
        .AddMethod("menuClosed:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&MenuClosed, "v@:@")
        .Register();

    [UnmanagedCallersOnly]
    private static nint ShouldTerminate(IntPtr self, IntPtr selector, IntPtr application)
    {
        if (UIAppContext.Current is UIApplication running)
            MacOSPlatform.DispatchInput(running.OnQuitRequested);
        return TerminateCancel;
    }

    [UnmanagedCallersOnly]
    private static void Unhidden(IntPtr self, IntPtr selector, IntPtr notification) =>
        ObjCManagedObject.Get<MacOSPlatform>(self)?.JoinAllOwned();

    [UnmanagedCallersOnly]
    private static void MenuClosed(IntPtr self, IntPtr selector, IntPtr notification) =>
        ObjCManagedObject.Get<MacOSPlatform>(self)?.Signal();
}
