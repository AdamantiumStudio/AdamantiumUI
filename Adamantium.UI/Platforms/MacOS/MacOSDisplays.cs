using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.MacOS.AppKit;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.Mathematics;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>The monitors as AppKit tells them, read on the main thread and again whenever they change. AppKit places
/// things in points from the bottom of the screen with the menu bar; windows are told physical pixels from its top: each
/// monitor at its place times the largest scale, so that monitors of different scales never overlap.</summary>
internal sealed unsafe class MacOSDisplays : IDisplayPlatform
{
    private static readonly IntPtr ObserverClass = new ObjCClassBuilder("AdamantiumScreenObserver", "NSObject")
        .AddMethod("screensChanged:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnScreensChanged, "v@:@")
        .Register();

    private volatile Display[] _displays = [];
    private IReadOnlyList<ScreenInfo> _screens = [];

    internal MacOSDisplays()
    {
        Read();
        var observer = NSObject.New(ObserverClass);
        ObjCManagedObject.Attach(observer, this);
        NSNotificationCenter.AddObserver(observer, "screensChanged:", NSScreen.ParametersChangedNotification);
    }

    public IReadOnlyList<ScreenInfo> Screens => System.Threading.Volatile.Read(ref _screens);

    public Rect VirtualScreen
    {
        get
        {
            var screens = Screens;
            if (screens.Count == 0)
                return default;

            var left = screens.Min(screen => screen.Bounds.X);
            var top = screens.Min(screen => screen.Bounds.Y);
            return new Rect(left, top, screens.Max(screen => screen.Bounds.Right) - left,
                screens.Max(screen => screen.Bounds.Bottom) - top);
        }
    }

    /// <summary>Pixels per point of the monitor at a physical pixel.</summary>
    internal double ScaleAt(double left, double top)
    {
        var displays = _displays;
        return displays.Length == 0 ? 1 : Nearest(displays, left, top, true).Scale;
    }

    /// <summary>The physical pixel at a point of AppKit's desktop.</summary>
    internal Vector2<double> ToPixels(double x, double y)
    {
        var displays = _displays;
        if (displays.Length == 0)
            return new Vector2<double>(x, y);

        var fromTop = displays[0].Points.Height - y;
        var display = Nearest(displays, x, fromTop, false);
        return new Vector2<double>(display.Pixels.X + (x - display.Points.X) * display.Scale,
            display.Pixels.Y + (fromTop - display.Points.Y) * display.Scale);
    }

    /// <summary>The point of AppKit's desktop at a physical pixel.</summary>
    internal CGPoint ToPoints(double left, double top)
    {
        var displays = _displays;
        if (displays.Length == 0)
            return new CGPoint(left, top);

        var display = Nearest(displays, left, top, true);
        var x = display.Points.X + (left - display.Pixels.X) / display.Scale;
        var fromTop = display.Points.Y + (top - display.Pixels.Y) / display.Scale;
        return new CGPoint(x, displays[0].Points.Height - fromTop);
    }

    private static Display Nearest(Display[] displays, double x, double y, bool inPixels)
    {
        var nearest = displays[0];
        var nearestDistance = double.MaxValue;
        foreach (var display in displays)
        {
            var area = inPixels ? display.Pixels : display.Points;
            if (x >= area.X && x < area.Right && y >= area.Y && y < area.Bottom)
                return display;

            var dx = Math.Max(Math.Max(area.X - x, x - area.Right), 0);
            var dy = Math.Max(Math.Max(area.Y - y, y - area.Bottom), 0);
            var distance = dx * dx + dy * dy;
            if (distance < nearestDistance)
            {
                nearest = display;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private void Read()
    {
        var all = NSScreen.All;
        if (all.Length == 0)
            return;

        var primaryHeight = NSScreen.FrameOf(all[0]).Size.Height;
        var placeScale = all.Max(NSScreen.BackingScaleFactorOf);
        var displays = new Display[all.Length];
        var screens = new ScreenInfo[all.Length];
        var ids = new HashSet<string>();
        for (var i = 0; i < all.Length; i++)
        {
            var scale = NSScreen.BackingScaleFactorOf(all[i]);
            var points = FromTop(NSScreen.FrameOf(all[i]), primaryHeight);
            var work = FromTop(NSScreen.VisibleFrameOf(all[i]), primaryHeight);
            var pixels = new Rect(points.X * placeScale, points.Y * placeScale, points.Width * scale, points.Height * scale);
            var workArea = new Rect(pixels.X + (work.X - points.X) * scale, pixels.Y + (work.Y - points.Y) * scale,
                work.Width * scale, work.Height * scale);
            displays[i] = new Display(points, pixels, scale);
            screens[i] = new ScreenInfo(IdOf(all[i], ids), pixels, workArea, scale, i == 0,
                NSScreen.MaximumFramesPerSecondOf(all[i]));
        }

        _displays = displays;
        System.Threading.Volatile.Write(ref _screens, screens);
    }

    // Two monitors of one model may tell the same serial number: the second is told apart by its place in the list.
    private static string IdOf(IntPtr screen, HashSet<string> taken)
    {
        var display = NSScreen.DisplayIdOf(screen);
        var id = $"{CoreGraphicsInterop.DisplayVendor(display):X}-{CoreGraphicsInterop.DisplayModel(display):X}-" +
                 $"{CoreGraphicsInterop.DisplaySerial(display):X}";
        var unique = id;
        for (var next = 2; !taken.Add(unique); next++)
            unique = $"{id}-{next}";

        return unique;
    }

    private static Rect FromTop(CGRect frame, double primaryHeight) =>
        new(frame.Origin.X, primaryHeight - frame.Origin.Y - frame.Size.Height, frame.Size.Width, frame.Size.Height);

    [UnmanagedCallersOnly]
    private static void OnScreensChanged(IntPtr self, IntPtr selector, IntPtr notification) =>
        ObjCManagedObject.Get<MacOSDisplays>(self)?.Read();

    private sealed record Display(Rect Points, Rect Pixels, double Scale);
}
