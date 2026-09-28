using Adamantium.UI.Core;

namespace Adamantium.UI.Controls;

public class Window : WindowBase
{
    public override IntPtr SurfaceHandle { get; internal set; }
    public override IntPtr Handle { get; internal set; }

    public override Vector2 PointToClient(PixelPoint point)
    {
        return ScreenToClient(point);
    }

    public override PixelPoint PointToScreen(Vector2 point)
    {
        return ClientToScreen(point);
    }

    public override void Show()
    {
        // A window built in code has no OS window yet: attach and initialize it here, so new Window().Show() works.
        if (Handle == IntPtr.Zero && UIContext == null && UIAppContext.Current?.UIContext is { } context)
        {
            AttachContextAndInitialize(context);
        }

        if (Handle == IntPtr.Zero)
            return;

        VerifyAccess();
        if (Renderer is not { FirstFrameProcessed: true })
        {
            WindowWorkerService.SetTitle(Title);
            ShouldDisplayWindow = true;
        }
        else
        {
            var firstDisplay = ShouldDisplayWindow;   // this Show is the deferred FIRST display of the window
            ShouldDisplayWindow = false;
            WindowWorkerService.ShowWindow(State);
            // Pull the window to the foreground when it first appears (ShowWindow alone doesn't steal focus from the
            // launching foreground window). Applies to the main window (OnStartup) and any window opened from a VM.
            if (firstDisplay) Activate();
        }
    }
        
    public override void Close()
    {
        // Cancel a deferred show, as Hide does, or a window closed before its first frame appears afterwards.
        ShouldDisplayWindow = false;
        IsClosed = true;
        OnClosed();
    }

    public override void Hide()
    {
        // Cancel a deferred show too, or a window hidden before its first frame reappears after it.
        ShouldDisplayWindow = false;
        WindowWorkerService.HideWindow();
    }
}