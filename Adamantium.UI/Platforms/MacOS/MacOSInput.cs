using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.MacOS.AppKit;
using Adamantium.MacOS.Carbon;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>Where the cursor is, what the keyboard is doing right now and what the user set up for both, as macOS tells
/// them.</summary>
internal sealed unsafe class MacOSInput : INativeMouse, INativeKeyboard, INativePlatformSettings
{
    private const int Hardware = 1;

    // How far AppKit lets a press move before it drags, in points: no setting says it.
    private const double DragPoints = 3;

    private static readonly IntPtr ObserverClass = new ObjCClassBuilder("AdamantiumLayoutObserver", "NSObject")
        .AddMethod("layoutsChanged:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnLayoutsChanged, "v@:@")
        .Register();

    private readonly MacOSPlatform _platform;
    private readonly uint _doubleClickTime;
    private readonly uint _keyRepeatDelay;
    private readonly uint _keyRepeatInterval;
    private volatile bool _hasRightToLeftLayout;

    /// <summary>Main thread, the only one the keyboard layouts may be asked for on.</summary>
    internal MacOSInput(MacOSPlatform platform)
    {
        _platform = platform;
        _doubleClickTime = Milliseconds(NSEvent.DoubleClickInterval);
        _keyRepeatDelay = Milliseconds(NSEvent.KeyRepeatDelay);
        _keyRepeatInterval = Milliseconds(NSEvent.KeyRepeatInterval);
        UserLanguages = NSLocale.PreferredLanguages;
        ReadLayouts();
        var observer = NSObject.New(ObserverClass);
        ObjCManagedObject.Attach(observer, this);
        NSNotificationCenter.AddDistributedObserver(observer, "layoutsChanged:", KeyboardLayout.EnabledChangedNotification);
    }

    public PixelPoint Position
    {
        get
        {
            var at = NSEvent.MouseLocation;
            var pixel = _platform.Desktop.ToPixels(at.X, at.Y);
            return new PixelPoint((int)Math.Round(pixel.X), (int)Math.Round(pixel.Y));
        }
        set
        {
            CoreGraphicsInterop.WarpCursor(_platform.Desktop.ToPointsFromTop(value.X, value.Y));
            // Or the cursor stands still for a moment after.
            CoreGraphicsInterop.AssociateMouseAndCursor(true);
        }
    }

    // Command is Control's role, as of every key event.
    public bool IsKeyDown(Key key) => key switch
    {
        Key.Ctrl => IsDown(Key.LeftCtrl) || IsDown(Key.RightCtrl),
        Key.Shift => IsDown(Key.LeftShift) || IsDown(Key.RightShift),
        Key.Alt => IsDown(Key.LeftAlt) || IsDown(Key.RightAlt),
        _ => IsDown(key)
    };

    private bool IsDown(Key key)
    {
        foreach (var keyCode in _platform.Keyboard.CodesOf(key))
        {
            if (CoreGraphicsInterop.IsKeyDown(Hardware, keyCode))
                return true;
        }

        return false;
    }

    public bool IsKeyToggled(Key key) => key == Key.CapsLock &&
        ((NSEventModifierFlags)CoreGraphicsInterop.ModifierFlags(Hardware) & NSEventModifierFlags.CapsLock) != 0;

    public bool HasRightToLeftLayout => _hasRightToLeftLayout;

    public uint DoubleClickTime => _doubleClickTime;

    // Not told: the defaults stand.
    public Size<double> DoubleClickSize => default;

    public uint HoverTime => 0;

    public Size<double> DragThreshold
    {
        get
        {
            var at = NSEvent.MouseLocation;
            var threshold = DragPoints * _platform.Desktop.ScaleAtPoint(at.X, at.Y);
            return new Size<double>(threshold, threshold);
        }
    }

    public uint KeyRepeatDelay => _keyRepeatDelay;

    public uint KeyRepeatInterval => _keyRepeatInterval;

    /// <summary>As the application started: macOS gives it a change of them the next time it starts.</summary>
    public IReadOnlyList<string> UserLanguages { get; }

    private void ReadLayouts() => _hasRightToLeftLayout = KeyboardLayout.EnabledLanguages().Any(IsRightToLeft);

    private static bool IsRightToLeft(string language)
    {
        try
        {
            return CultureInfo.GetCultureInfo(language).TextInfo.IsRightToLeft;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static uint Milliseconds(double seconds) => (uint)Math.Clamp(Math.Round(seconds * 1000), 0, 60000);

    [UnmanagedCallersOnly]
    private static void OnLayoutsChanged(IntPtr self, IntPtr selector, IntPtr notification) =>
        ObjCManagedObject.Get<MacOSInput>(self)?.ReadLayouts();
}
