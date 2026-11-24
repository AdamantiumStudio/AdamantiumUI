using System;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>The NSApplicationDelegate: quitting - from the Dock, at logout - closes the windows, as closing each of them
/// would, so any of them may refuse; AppKit itself would end the process on the spot.</summary>
internal static unsafe class MacOSApplicationDelegate
{
    private const nint TerminateCancel = 0;

    /// <summary>The class, registered with the runtime on first use.</summary>
    internal static readonly IntPtr Class = new ObjCClassBuilder("AdamantiumApplicationDelegate", "NSObject")
        .AddProtocol("NSApplicationDelegate")
        .AddMethod("applicationShouldTerminate:",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, nint>)&ShouldTerminate, "q@:@")
        .Register();

    [UnmanagedCallersOnly]
    private static nint ShouldTerminate(IntPtr self, IntPtr selector, IntPtr application)
    {
        MacOSPlatform.DispatchInput(CloseWindows);
        return TerminateCancel;
    }

    private static void CloseWindows()
    {
        var application = UIAppContext.Current;
        if (application == null)
            return;

        // The main window last: closing it may end the application before the others were asked.
        var main = application.MainWindow;
        foreach (var window in application.Windows.Where(window => window != main && window.Owner == null).ToArray())
            window.Close();
        main?.Close();
    }
}
