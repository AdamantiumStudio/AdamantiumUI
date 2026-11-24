using System;
using Adamantium.MacOS.AppKit;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.MacOS.QuartzCore;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>The ghost of a drag: a borderless, see-through window above every application that lets the mouse through,
/// its layer showing the ghost's pixels.</summary>
internal sealed class MacOSDragGhost : IDragGhost
{
    private readonly MacOSPlatform _platform;
    private IntPtr _window;
    private IntPtr _view;
    private double _width;
    private double _height;

    public MacOSDragGhost(IApplicationPlatform platform)
    {
        _platform = (MacOSPlatform)platform;
    }

    public uint Dpi => (uint)Math.Round(96 * NSScreen.BackingScaleFactorOf(NSScreen.Main));

    public event Action<uint> DpiChanged
    {
        add { }
        remove { }
    }

    public void Show(byte[] premultipliedBgra, int width, int height, int screenX, int screenY)
    {
        var pixels = (byte[])premultipliedBgra.Clone();
        _platform.RunOnMainThread(() =>
        {
            EnsureWindow();
            var scale = NSScreen.BackingScaleFactorOf(NSScreen.Main);
            _width = width / scale;
            _height = height / scale;
            var layer = NSView.LayerOf(_view);
            var image = CGImage.FromPremultipliedBgra(pixels, width, height);
            CALayer.SetContents(layer, image);
            CALayer.SetContentsScale(layer, scale);
            CoreGraphicsInterop.Release(image);
            Place(screenX, screenY);
            NSWindow.OrderFront(_window);
        });
    }

    public void Move(int screenX, int screenY) => _platform.RunOnMainThread(() =>
    {
        if (_window != IntPtr.Zero)
            Place(screenX, screenY);
    });

    public void Hide() => _platform.RunOnMainThread(() =>
    {
        if (_window != IntPtr.Zero)
            NSWindow.OrderOut(_window);
    });

    public void Dispose() => _platform.RunOnMainThread(() =>
    {
        if (_window == IntPtr.Zero)
            return;

        NSWindow.Close(_window);
        NSObject.Release(_view);
        NSObject.Release(_window);
        _window = IntPtr.Zero;
        _view = IntPtr.Zero;
    });

    private void EnsureWindow()
    {
        if (_window != IntPtr.Zero)
            return;

        _window = NSWindow.Create(new CGRect(0, 0, 1, 1), NSWindowStyleMask.Borderless);
        NSWindow.MakeTransparent(_window);
        NSWindow.SetIgnoresMouseEvents(_window, true);
        NSWindow.SetLevel(_window, NSWindow.PopUpMenuLevel);
        _view = NSView.Create(ObjCRuntime.GetClass("NSView"), new CGRect(0, 0, 1, 1));
        var layer = NSObject.New(ObjCRuntime.GetClass("CALayer"));
        NSView.SetLayer(_view, layer);
        NSObject.Release(layer);
        NSWindow.SetContentView(_window, _view);
    }

    private void Place(int screenX, int screenY)
    {
        var scale = NSScreen.BackingScaleFactorOf(NSScreen.Main);
        var screenHeight = NSScreen.FrameOf(NSScreen.Main).Size.Height;
        var left = screenX / scale;
        var top = screenY / scale;
        NSWindow.SetFrame(_window, new CGRect(left, screenHeight - top - _height, _width, _height));
    }
}
