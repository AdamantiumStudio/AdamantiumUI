using System;
using System.Runtime.InteropServices;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.UI.Core.Resources;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>The system's light or dark appearance, followed as the user, or the evening, changes it.</summary>
internal static unsafe class MacOSAppearance
{
    private const string StyleKey = "AppleInterfaceStyle";
    private const string Dark = "Dark";
    private const string ChangedNotification = "AppleInterfaceThemeChangedNotification";

    private static readonly IntPtr ObserverClass = new ObjCClassBuilder("AdamantiumAppearanceObserver", "NSObject")
        .AddMethod("appearanceChanged:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnChanged, "v@:@")
        .Register();

    /// <summary>Reads the appearance and follows it from then on. Main thread.</summary>
    internal static void Follow()
    {
        SystemAppearance.PrefersDark = PrefersDark;
        NSNotificationCenter.AddDistributedObserver(NSObject.New(ObserverClass), "appearanceChanged:", ChangedNotification);
    }

    // The key is there while the appearance is dark.
    private static bool PrefersDark => NSUserDefaults.StringOf(StyleKey) == Dark;

    [UnmanagedCallersOnly]
    private static void OnChanged(IntPtr self, IntPtr selector, IntPtr notification) =>
        SystemAppearance.PrefersDark = PrefersDark;
}
