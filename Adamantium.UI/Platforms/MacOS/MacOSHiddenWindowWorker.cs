using System;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.QuartzCore;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Internals;
using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>A window of an application nobody is to see (<see cref="MacOSPlatform.IsHidden"/>): no NSWindow stands behind
/// it, only the CAMetalLayer it is drawn into, so it can neither show nor take the keyboard. Where it is, how large, whether
/// it is minimized and whether it is the active one is kept here, as AppKit would have told it.</summary>
internal sealed class MacOSHiddenWindowWorker : AdamantiumComponent, IWindowWorkerService
{
    private const int DefaultWidth = 800;
    private const int DefaultHeight = 600;

    private readonly MacOSPlatform _platform;
    private IWindow _window;
    private IntPtr _layer;
    private double _scale = 1;
    private Rect _restore;
    private volatile bool _closed;

    public MacOSHiddenWindowWorker(IUIContext context)
    {
        UIContext = context;
        _platform = (MacOSPlatform)context.Resolve<IApplicationPlatform>();
    }

    public IUIContext UIContext { get; }

    public Rect RestoreBounds => _restore;

    public bool AppliesWindowOpacity => true;

    public void SetWindow(IWindow window)
    {
        if (!_platform.IsOnUIThread)
            throw new Exceptions.DispatcherException("A window is made on the main thread: use IUIApplication.ExecuteOnUIThread");

        _window = window;
        window.Closed += OnWindowClosed;

        var left = double.IsNaN(window.Left) ? 0 : window.Left;
        var top = double.IsNaN(window.Top) ? 0 : window.Top;
        var width = Extent(window.ClientWidth, window.Width, DefaultWidth);
        var height = Extent(window.ClientHeight, window.Height, DefaultHeight);
        _scale = _platform.Desktop.ScaleAt(left, top);
        _restore = new Rect(left, top, width * _scale, height * _scale);

        _layer = NSObject.Retain(CAMetalLayer.Create());
        SizeLayer(width, height);
        window.SetHandle(_layer);
        window.SetSurfaceHandle(_layer);
        window.DpiScale = new Vector2<double>(_scale, _scale);
        window.DisplayRefreshRate = ScreenOf(_restore)?.RefreshRate ?? 60;
        window.Width = _restore.Width;
        window.Height = _restore.Height;
        window.UpdateClientSizeFromPlatform(width, height);
        window.UpdatePositionFromPlatform(left, top);

        Input.DragDrop.RegisterNativeDropTarget(window);
        UIContext.ThemeEngine.ApplyCurrentTheme(window);
        UIContext.UIApplication.AddWindow(window);
        window.OnSourceInitialized();
        window.StateChanged += OnStateChanged;
    }

    public void SetTitle(string title)
    {
    }

    public void SetPosition(double left, double top)
    {
        _restore = new Rect(left, top, _restore.Width, _restore.Height);
        OnLoop(() => _window.UpdatePositionFromPlatform(left, top));
    }

    public void SetSize(double clientWidth, double clientHeight)
    {
        _restore = new Rect(_restore.X, _restore.Y, clientWidth * _scale, clientHeight * _scale);
        Resize(clientWidth, clientHeight);
    }

    public void UpdateOverlayTraits()
    {
    }

    public void ShowWindow(WindowState windowState)
    {
        switch (windowState)
        {
            case WindowState.Minimized:
                ((UIApplication)UIContext.UIApplication).SetWindowHidden(_window, true);
                OnLoop(() => SetActive(false));
                return;
            case WindowState.Maximized:
                var area = ScreenOf(_restore)?.WorkArea ?? _restore;
                Place(area);
                break;
            default:
                Place(_restore);
                break;
        }

        ((UIApplication)UIContext.UIApplication).SetWindowHidden(_window, false);
        if (_window.ActivateOnShow)
            Activate();
    }

    public void HideWindow() => OnLoop(() => SetActive(false));

    public void Activate()
    {
        if (_window.State == WindowState.Minimized)
            _window.State = WindowState.Normal;

        OnLoop(() =>
        {
            foreach (var other in UIContext.UIApplication.Windows)
            {
                if (!ReferenceEquals(other, _window) && other.IsActive && other is not SplashWindow)
                    other.StopBeingActive(UIContext.UIApplication);
            }

            SetActive(true);
        });
    }

    public void RaiseWithoutActivation()
    {
    }

    public void SetMouseCapture(bool capture)
    {
    }

    public PixelPoint ScreenToClient(PixelPoint screen)
    {
        var corner = _window.LivePosition;
        return new PixelPoint(screen.X - (int)corner.X, screen.Y - (int)corner.Y);
    }

    public PixelPoint ClientToScreen(PixelPoint client)
    {
        var corner = _window.LivePosition;
        return new PixelPoint(client.X + (int)corner.X, client.Y + (int)corner.Y);
    }

    public void SetRelativeMouseMode(bool enabled, PixelPoint restoreScreen)
    {
    }

    public void SetInputMethodEnabled(bool enabled)
    {
    }

    public void BeginMoveDrag()
    {
    }

    private void OnStateChanged(object sender, StateChangedEventArgs e) => ShowWindow(e.State);

    private void SetActive(bool active)
    {
        if (_window.IsActive == active)
            return;

        if (active)
            _window.BecomeActive(UIContext.UIApplication);
        else
            _window.StopBeingActive(UIContext.UIApplication);
    }

    private void Place(Rect bounds)
    {
        OnLoop(() => _window.UpdatePositionFromPlatform(bounds.X, bounds.Y));
        Resize(bounds.Width / _scale, bounds.Height / _scale);
    }

    private void Resize(double clientWidth, double clientHeight)
    {
        _platform.RunOnMainThread(() =>
        {
            if (_layer != IntPtr.Zero)
                SizeLayer(clientWidth, clientHeight);
        });
        OnLoop(() =>
        {
            _window.Width = clientWidth * _scale;
            _window.Height = clientHeight * _scale;
            _window.UpdateClientSizeFromPlatform(clientWidth, clientHeight);
        });
    }

    private void SizeLayer(double width, double height)
    {
        CAMetalLayer.SetContentsScale(_layer, _scale);
        CAMetalLayer.SetDrawableSize(_layer, new CGSize(width * _scale, height * _scale));
    }

    private void OnWindowClosed(object sender, EventArgs e)
    {
        if (_closed)
            return;

        _closed = true;
        _window.StateChanged -= OnStateChanged;
        Input.DragDrop.UnregisterNativeDropTarget(_window);
        UIContext.UIApplication.RemoveWindow(_window);
        _window.SetHandle(IntPtr.Zero);
        _platform.RunOnMainThread(() =>
        {
            NSObject.Release(_layer);
            _layer = IntPtr.Zero;
        });
    }

    private void OnLoop(Action action) => MacOSPlatform.DispatchInput(() =>
    {
        if (!_closed)
            action();
    });

    private static ScreenInfo ScreenOf(Rect bounds) =>
        Displays.Screens.Count == 0 ? null : WindowPlacer.ScreenOf(bounds, Displays.Screens);

    private static double Extent(double preferred, double fallback, int defaultExtent)
    {
        if (!double.IsNaN(preferred) && preferred > 0)
            return preferred;

        return !double.IsNaN(fallback) && fallback > 0 ? fallback : defaultExtent;
    }
}
