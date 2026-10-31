using Adamantium.UI.Core;

namespace Adamantium.UI.Controls;

public class Window : WindowBase
{
    private volatile bool _displayed;

    public override IntPtr SurfaceHandle { get; internal set; }
    public override IntPtr Handle { get; internal set; }

    public override Vector2<double> PointToClient(PixelPoint point)
    {
        return ScreenToClient(point);
    }

    public override PixelPoint PointToScreen(Vector2<double> point)
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
        if (Renderer is not { HasPresentedContent: true })
        {
            WindowWorkerService.SetTitle(Title);
            ShouldDisplayWindow = true;
        }
        else
        {
            var firstDisplay = ShouldDisplayWindow;   // this Show is the deferred FIRST display of the window
            ShouldDisplayWindow = false;
            var state = _opensMaximized ? WindowState.Maximized : State;
            _opensMaximized = false;
            WindowWorkerService.ShowWindow(state);
            var wasDisplayed = _displayed;
            _displayed = true;
            if (!wasDisplayed)
            {
                RaiseDisplayed();
            }
            // Pull the window to the foreground when it first appears (ShowWindow alone doesn't steal focus from the
            // launching foreground window). Applies to the main window (OnStartup) and any window opened from a VM.
            if (firstDisplay && ActivateOnShow) Activate();
        }
    }
        
    protected override void CloseCore()
    {
        // Cancel a deferred show, as Hide does, or a window closed before its first frame appears afterwards.
        ShouldDisplayWindow = false;
        _displayed = false;
    }

    protected override void HideCore()
    {
        // Cancel a deferred show too, or a window hidden before its first frame reappears after it.
        ShouldDisplayWindow = false;
        _displayed = false;
        WindowWorkerService.HideWindow();
    }

    public override bool IsDisplayed => _displayed;
}