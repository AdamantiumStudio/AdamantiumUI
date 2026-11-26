using System;
using System.Runtime.InteropServices;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.UI.Platforms.MacOS.Accessibility;

/// <summary>What a window's view tells the Mac's accessibility: the window's elements, the one under a point and the
/// one with the keyboard. The view itself is no element: what it holds stands straight in the window.</summary>
internal static unsafe class MacOSAccessibilityMethods
{
    internal static ObjCClassBuilder AddAccessibility(this ObjCClassBuilder view) => view
        .AddMethod("accessibilityChildren", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&Children, "@@:")
        .AddMethod("accessibilityHitTest:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, CGPoint, IntPtr>)&HitTest,
            "@@:{CGPoint=dd}")
        .AddMethod("accessibilityFocusedUIElement", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&Focused, "@@:");

    [UnmanagedCallersOnly]
    private static IntPtr Children(IntPtr self, IntPtr selector) =>
        Of(self)?.Children() ?? NSArray.Create([]);

    // Outside its elements, the view answers for itself.
    [UnmanagedCallersOnly]
    private static IntPtr HitTest(IntPtr self, IntPtr selector, CGPoint point) =>
        Of(self)?.HitTest(point) is { } element && element != IntPtr.Zero ? element : self;

    [UnmanagedCallersOnly]
    private static IntPtr Focused(IntPtr self, IntPtr selector) =>
        Of(self)?.Focused() is { } element && element != IntPtr.Zero ? element : self;

    private static MacOSAccessibility Of(IntPtr view) => ObjCManagedObject.Get<MacOSWindowWorker>(view)?.Accessibility;
}
