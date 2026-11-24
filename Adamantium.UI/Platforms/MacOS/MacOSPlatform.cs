using System;
using System.Collections.Generic;
using System.Threading;
using Adamantium.Core.DependencyInjection;
using Adamantium.MacOS.AppKit;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>macOS through AppKit: the application's event loop on the main thread, woken from any thread by an
/// application-defined event.</summary>
public class MacOSPlatform : IApplicationPlatform
{
    private const short WakeSubtype = 0x4144;

    internal readonly object ModalLock = new();

    private readonly int _threadId;
    private readonly List<MacOSWindowWorker> _windows = [];

    public MacOSPlatform()
    {
        _threadId = Environment.CurrentManagedThreadId;
        var pool = ObjCRuntime.PushAutoreleasePool();
        NSApplication.Launch();
        NSApplication.SetDelegate(NSObject.New(MacOSApplicationDelegate.Class));
        ShowApplicationIcon();
        Desktop = new MacOSDisplays();
        Displays.Platform = Desktop;
        ObjCRuntime.PopAutoreleasePool(pool);
        Cursor.Platform = new MacOSCursors(this);
    }

    /// <summary>The monitors, and the way between AppKit's points and the pixels windows are told.</summary>
    internal MacOSDisplays Desktop { get; }

    public bool IsOnUIThread => Environment.CurrentManagedThreadId == _threadId;

    public event Action Signaled;

    public void Run(CancellationToken token)
    {
        using var wake = token.Register(Signal);
        NSApplication.Activate();
        while (!token.IsCancellationRequested)
        {
            var pool = ObjCRuntime.PushAutoreleasePool();
            try
            {
                var nsEvent = NSApplication.NextEvent(NSDate.DistantFuture);
                if (nsEvent == IntPtr.Zero)
                    continue;

                var type = NSEvent.TypeOf(nsEvent);
                if (type == NSEventType.ApplicationDefined && NSEvent.SubtypeOf(nsEvent) == WakeSubtype)
                    Signaled?.Invoke();
                else if (type == NSEventType.KeyUp && NSApplication.KeyWindow is var key && key != IntPtr.Zero &&
                         (NSEvent.ModifierFlagsOf(nsEvent) & NSEventModifierFlags.Command) != 0)
                    NSWindow.SendEvent(key, nsEvent);   // NSApplication keeps a key's release to itself while Command is held
                else
                    NSApplication.SendEvent(nsEvent);
            }
            finally
            {
                ObjCRuntime.PopAutoreleasePool(pool);
            }
        }
    }

    public void Signal()
    {
        var pool = ObjCRuntime.PushAutoreleasePool();
        NSApplication.PostEvent(NSEvent.CreateApplicationDefined(WakeSubtype));
        ObjCRuntime.PopAutoreleasePool(pool);
    }

    // TODO(macOS): NSWindow.windowNumberAtPoint:belowWindowWithWindowNumber: -> the window number, mapped back to our
    // handle. Zero until then, which makes the drag fall back to client-bounds containment.
    public IntPtr WindowFromScreenPoint(PixelPoint point) => IntPtr.Zero;

    internal void Register(MacOSWindowWorker worker)
    {
        lock (ModalLock)
            _windows.Add(worker);
    }

    internal void Unregister(MacOSWindowWorker worker)
    {
        lock (ModalLock)
            _windows.Remove(worker);
    }

    /// <summary>Fills <paramref name="others"/> with the open windows other than <paramref name="worker"/>: those a modal
    /// window keeps from input.</summary>
    internal void CollectOthers(MacOSWindowWorker worker, List<MacOSWindowWorker> others)
    {
        lock (ModalLock)
        {
            foreach (var other in _windows)
            {
                if (other != worker)
                    others.Add(other);
            }
        }
    }

    /// <summary>Raises on the loop thread what AppKit told this one, in the order it came: the visual tree is not to
    /// be touched from the main thread.</summary>
    internal static void DispatchInput(Action raise)
    {
        var dispatcher = Threading.Dispatcher.CurrentDispatcher;
        if (dispatcher != null)
            dispatcher.Post(raise);
        else
            raise();
    }

    /// <summary>Runs <paramref name="action"/> on the main thread, the only one AppKit may be called from: at once
    /// when called there, else queued.</summary>
    internal void RunOnMainThread(Action action)
    {
        if (IsOnUIThread)
            action();
        else
            Threading.Dispatcher.CurrentDispatcher.Invoke(action);
    }

    private static void ShowApplicationIcon()
    {
        if (ApplicationIcon.Load() is not { Pictures.Count: > 0 } icon)
            return;

        var picture = icon.Pictures[0];
        var cgImage = CGImage.FromPremultipliedBgra(picture.PremultipliedBgra, picture.Size, picture.Size);
        var image = NSImage.FromCGImage(cgImage, new CGSize(picture.Size, picture.Size));
        NSApplication.SetIconImage(image);
        NSObject.Release(image);
        CoreGraphicsInterop.Release(cgImage);
    }

    public static void Initialize(IContainerRegistry resolver)
    {
        resolver.RegisterSingleton<IApplicationPlatform, MacOSPlatform>();
        resolver.RegisterSingleton<IDragGhost, MacOSDragGhost>();
        FileManager.Platform = new FinderFileManager();
        Trash.Platform = new FinderTrash();
    }
}
