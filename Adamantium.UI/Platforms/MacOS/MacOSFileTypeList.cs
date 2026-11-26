using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.MacOS.AppKit;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>The kinds of file a dialog offers, under its files: the one chosen says which are offered. Main thread only.
/// </summary>
internal sealed unsafe class MacOSFileTypeList : IDisposable
{
    private const double MarginPoints = 8;

    private static readonly IntPtr TargetClass = new ObjCClassBuilder("AdamantiumFileTypeTarget", "NSObject")
        .AddMethod("picked:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnPicked, "v@:@")
        .Register();

    private readonly IntPtr _panel;
    private readonly IReadOnlyList<FileType> _types;
    private FileType _picked;
    private IntPtr _target;

    /// <summary>Offers the first of <paramref name="types"/> in a dialog, with a list to choose another from when there is
    /// more than one.</summary>
    internal MacOSFileTypeList(IntPtr panel, IReadOnlyList<FileType> types)
    {
        _panel = panel;
        _types = Offered(types);
        if (_types.Count == 0)
            return;

        _picked = _types[0];
        NSSavePanel.SetExtensions(panel, ExtensionsOf(_picked));
        if (_types.Count > 1)
            ShowList();
    }

    public void Dispose()
    {
        if (_target == IntPtr.Zero)
            return;

        ObjCManagedObject.Release(_target);
        NSObject.Release(_target);
        _target = IntPtr.Zero;
    }

    private void ShowList()
    {
        _target = NSObject.New(TargetClass);
        ObjCManagedObject.Attach(_target, this);
        var button = NSPopUpButton.Create();
        for (var i = 0; i < _types.Count; i++)
            NSMenu.AddItem(NSPopUpButton.MenuOf(button), NameOf(_types[i]), _target, "picked:", i, true, false);
        NSPopUpButton.Select(button, 0);
        NSPopUpButton.SizeToFit(button);

        var size = NSView.BoundsOf(button).Size;
        var view = NSView.Create(ObjCRuntime.GetClass("NSView"),
            new CGRect(0, 0, size.Width + 2 * MarginPoints, size.Height + 2 * MarginPoints));
        NSView.AddSubview(view, button, new CGPoint(MarginPoints, MarginPoints));
        NSSavePanel.SetAccessory(_panel, view);
        NSObject.Release(view);
        NSObject.Release(button);
    }

    private void Pick(nint index)
    {
        var previous = _picked;
        _picked = _types[(int)index];
        NSSavePanel.SetExtensions(_panel, ExtensionsOf(_picked));
        var name = NSSavePanel.NameOf(_panel);
        if (Renamed(name, previous, _picked) is { } renamed && renamed != name)
            NSSavePanel.SetName(_panel, renamed);
    }

    /// <summary>A name to save under with the extension of another kind in place of its own kind's.</summary>
    internal static string Renamed(string name, FileType from, FileType to)
    {
        var extension = Path.GetExtension(name);
        return extension is { Length: > 1 } && ExtensionsOf(to) is [var first, ..] &&
            !ExtensionsOf(to).Contains(extension[1..], StringComparer.OrdinalIgnoreCase) &&
            ExtensionsOf(from).Contains(extension[1..], StringComparer.OrdinalIgnoreCase)
            ? Path.ChangeExtension(name, first)
            : name;
    }

    /// <summary>The kinds that say which files they are.</summary>
    internal static IReadOnlyList<FileType> Offered(IReadOnlyList<FileType> types) =>
        (types ?? []).Where(type => type?.Extensions.Count > 0).ToArray();

    /// <summary>The extensions of a kind; none when it is of any file.</summary>
    internal static IReadOnlyList<string> ExtensionsOf(FileType type) =>
        type.Extensions.Any(extension => extension is "*" or "*.*") ? [] : type.Extensions;

    /// <summary>What a kind is called: by its extensions when it is not named.</summary>
    internal static string NameOf(FileType type) => string.IsNullOrEmpty(type.Name)
        ? string.Join(", ", type.Extensions.Select(extension => "*." + extension))
        : type.Name;

    [UnmanagedCallersOnly]
    private static void OnPicked(IntPtr self, IntPtr selector, IntPtr sender) =>
        ObjCManagedObject.Get<MacOSFileTypeList>(self)?.Pick(NSMenu.TagOf(sender));
}
