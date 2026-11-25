using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.MacOS.AppKit;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>What a package promises (<see cref="IDataPackage.SetDeferred"/>), made when a pasteboard is asked for it.
/// </summary>
internal sealed unsafe class MacOSPasteboardPromise
{
    private static readonly IntPtr Class = new ObjCClassBuilder("AdamantiumPasteboardPromise", "NSObject")
        .AddMethod("pasteboard:item:provideDataForType:",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&Provide, "v@:@@@")
        .AddMethod("pasteboardFinishedWithDataProvider:",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Finished, "v@:@")
        .AddProtocol("NSPasteboardItemDataProvider")
        .Register();

    private readonly IDataPackage _package;
    private readonly Dictionary<string, string> _formats = new();

    private MacOSPasteboardPromise(IDataPackage package) => _package = package;

    /// <summary>Promises the data of some types on an item: each by the format of the package it is made of.</summary>
    internal static void Give(IntPtr item, IDataPackage package, IReadOnlyCollection<(string Type, string Format)> promised)
    {
        if (promised.Count == 0)
            return;

        var promise = new MacOSPasteboardPromise(package);
        var types = new List<string>();
        foreach (var (type, format) in promised)
        {
            promise._formats[type] = format;
            types.Add(type);
        }

        var provider = NSObject.New(Class);
        ObjCManagedObject.Attach(provider, promise);
        NSPasteboard.SetProvider(item, provider, CollectionsMarshal.AsSpan(types));
        NSObject.Release(provider);
    }

    private void Provide(IntPtr item, string type)
    {
        if (type == null || !_formats.TryGetValue(type, out var format))
            return;

        try
        {
            if (MacOSClipboard.EncodeAs(type, format, _package.Get(format)) is { } bytes)
                NSPasteboard.SetData(item, bytes, type);
        }
        catch (Exception e)
        {
            Serilog.Log.Logger.Warning(e, "What a drag promised as {Format} could not be made", format);
        }
    }

    [UnmanagedCallersOnly]
    private static void Provide(IntPtr self, IntPtr selector, IntPtr pasteboard, IntPtr item, IntPtr type) =>
        ObjCManagedObject.Get<MacOSPasteboardPromise>(self)?.Provide(item, NSString.ToManaged(type));

    [UnmanagedCallersOnly]
    private static void Finished(IntPtr self, IntPtr selector, IntPtr pasteboard) => ObjCManagedObject.Release(self);
}
