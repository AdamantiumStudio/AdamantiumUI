using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.MacOS.AppKit;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>An icon in the menu bar: a click is the application's, a click of the right button, or with Control held,
/// opens its menu, which the system shows.</summary>
internal sealed unsafe class MacOSMenuBarIcon : INotificationAreaIcon
{
    private const double IconPoints = 18;
    private const int IconPixels = 36;

    private static readonly IntPtr TargetClass = new ObjCClassBuilder("AdamantiumMenuBarTarget", "NSObject")
        .AddMethod("clicked:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnClicked, "v@:@")
        .AddMethod("picked:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnPicked, "v@:@")
        .Register();

    private readonly MacOSPlatform _platform;
    private readonly INotificationAreaMenu _menu;
    private readonly List<Action> _picks = [];
    private IntPtr _item;
    private IntPtr _target;
    private string _toolTip;
    private bool _disposed;

    internal MacOSMenuBarIcon(MacOSPlatform platform, string toolTip, INotificationAreaMenu menu)
    {
        _platform = platform;
        _menu = menu;
        _toolTip = toolTip;
        _platform.RunOnMainThread(Show);
    }

    public string ToolTip
    {
        get => _toolTip;
        set
        {
            _toolTip = value;
            _platform.RunOnMainThread(() =>
            {
                if (_item != IntPtr.Zero)
                    NSStatusItem.SetToolTip(_item, value);
            });
        }
    }

    /// <summary>The system may hide it behind a full menu bar, and does not say.</summary>
    public bool IsDisplayed => !_disposed;

    public event EventHandler Clicked;

    event EventHandler<NotificationAreaMenuEventArgs> INotificationAreaIcon.MenuAsked
    {
        add { }
        remove { }
    }

    event EventHandler INotificationAreaIcon.IsDisplayedChanged
    {
        add { }
        remove { }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _platform.RunOnMainThread(() =>
        {
            NSStatusItem.Remove(_item);
            ObjCManagedObject.Release(_target);
            NSObject.Release(_target);
            _item = IntPtr.Zero;
            _target = IntPtr.Zero;
        });
    }

    private void Show()
    {
        _target = NSObject.New(TargetClass);
        ObjCManagedObject.Attach(_target, this);
        _item = NSStatusItem.Create();
        NSStatusItem.SetToolTip(_item, _toolTip);
        NSStatusItem.SetAction(_item, _target, "clicked:");
        if (ApplicationIcon.Load() is not { Pictures.Count: > 0 } icon)
            return;

        // The smallest that is sharp on a Retina screen, or the largest there is.
        var picture = icon.Pictures[0];
        foreach (var smaller in icon.Pictures)
        {
            if (smaller.Size >= IconPixels)
                picture = smaller;
        }

        var cgImage = CGImage.FromPremultipliedBgra(picture.PremultipliedBgra, picture.Size, picture.Size);
        var image = NSImage.FromCGImage(cgImage, new CGSize(IconPoints, IconPoints));
        NSStatusItem.SetImage(_item, image);
        NSObject.Release(image);
        CoreGraphicsInterop.Release(cgImage);
    }

    private void Click()
    {
        var nsEvent = NSApplication.CurrentEvent;
        var asksMenu = nsEvent != IntPtr.Zero && (NSEvent.TypeOf(nsEvent) == NSEventType.RightMouseUp ||
            (NSEvent.ModifierFlagsOf(nsEvent) & NSEventModifierFlags.Control) != 0);
        MacOSPlatform.DispatchInput(() =>
        {
            if (_disposed)
                return;

            if (!asksMenu)
                Clicked?.Invoke(this, EventArgs.Empty);
            else if (_menu?.IsOffered == true && _menu.ReadRows() is { Count: > 0 } rows)
                _platform.RunOnMainThread(() => Open(rows));
        });
    }

    private void Open(IReadOnlyList<NotificationAreaMenuItem> rows)
    {
        if (_item == IntPtr.Zero)
            return;

        _picks.Clear();
        var menu = Build(rows);
        NSMenu.PopUp(menu, NSStatusItem.ButtonOf(_item));
        NSObject.Release(menu);
        // What woke the loop meanwhile went to the menu.
        _platform.Signal();
    }

    private IntPtr Build(IReadOnlyList<NotificationAreaMenuItem> rows)
    {
        var menu = NSMenu.Create(string.Empty);
        foreach (var row in rows)
        {
            if (row.IsSeparator)
                NSMenu.AddSeparator(menu);
            else if (row.Items.Count > 0)
            {
                var submenu = Build(row.Items);
                NSMenu.AddSubmenu(menu, row.Text ?? string.Empty, submenu);
                NSObject.Release(submenu);
            }
            else
            {
                NSMenu.AddItem(menu, row.Text ?? string.Empty, _target, "picked:", _picks.Count,
                    row.IsEnabled && row.Picked != null, row.IsCheckable && row.IsChecked);
                _picks.Add(row.Picked);
            }
        }

        return menu;
    }

    private void Pick(nint tag)
    {
        if (tag >= 0 && tag < _picks.Count && _picks[(int)tag] is { } picked)
            MacOSPlatform.DispatchInput(picked);
    }

    [UnmanagedCallersOnly]
    private static void OnClicked(IntPtr self, IntPtr selector, IntPtr sender) =>
        ObjCManagedObject.Get<MacOSMenuBarIcon>(self)?.Click();

    [UnmanagedCallersOnly]
    private static void OnPicked(IntPtr self, IntPtr selector, IntPtr sender) =>
        ObjCManagedObject.Get<MacOSMenuBarIcon>(self)?.Pick(NSMenu.TagOf(sender));
}
