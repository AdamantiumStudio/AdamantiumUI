using System;
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

    private readonly int _threadId;

    public MacOSPlatform()
    {
        _threadId = Environment.CurrentManagedThreadId;
        var pool = ObjCRuntime.PushAutoreleasePool();
        NSApplication.Launch();
        ShowApplicationIcon();
        ObjCRuntime.PopAutoreleasePool(pool);
        Cursor.Platform = new MacOSCursors();
    }

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

                if (NSEvent.TypeOf(nsEvent) == NSEventType.ApplicationDefined && NSEvent.SubtypeOf(nsEvent) == WakeSubtype)
                    Signaled?.Invoke();
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
