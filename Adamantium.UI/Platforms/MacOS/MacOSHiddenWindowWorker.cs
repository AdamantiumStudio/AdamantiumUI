using System;
using System.Linq;
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

    private readonly object _placementLock = new();
    private readonly MacOSPlatform _platform;
    private IWindow _window;
    private MacOSModality _modality;
    private IntPtr _layer;
    private Placement _placement;
    private volatile bool _shown;
    private volatile bool _closed;

    public MacOSHiddenWindowWorker(IUIContext context)
    {
        UIContext = context;
        _platform = (MacOSPlatform)context.Resolve<IApplicationPlatform>();
    }

    public IUIContext UIContext { get; }

    public Rect RestoreBounds => Current?.Restore ?? Rect.Empty;

    public bool AppliesWindowOpacity => true;

    private Placement Current
    {
        get
        {
            lock (_placementLock)
                return _placement;
        }
    }

    public void SetWindow(IWindow window)
    {
        if (!_platform.IsOnUIThread)
            throw new Exceptions.DispatcherException("A window is made on the main thread: use IUIApplication.ExecuteOnUIThread");

        _window = window;
        _modality = new MacOSModality(_platform, window, Activate);
        window.Closed += OnWindowClosed;

        var left = double.IsNaN(window.Left) ? 0 : window.Left;
        var top = double.IsNaN(window.Top) ? 0 : window.Top;
        var width = Extent(window.ClientWidth, window.Width, DefaultWidth);
        var height = Extent(window.ClientHeight, window.Height, DefaultHeight);
        var scale = _platform.Desktop.ScaleAt(left, top);
        var bounds = new Rect(left, top, width * scale, height * scale);
        _placement = new Placement(bounds, bounds, scale, false, false);

        _layer = NSObject.Retain(CAMetalLayer.Create());
        SizeLayer();
        window.SetHandle(_layer);
        window.SetSurfaceHandle(_layer);
        Tell();

        Input.DragDrop.RegisterNativeDropTarget(window);
        UIContext.ThemeEngine.ApplyCurrentTheme(window);
        UIContext.UIApplication.AddWindow(window);
        _platform.Register(_modality);
        window.OnSourceInitialized();
        window.StateChanged += OnStateChanged;
    }

    public void SetTitle(string title)
    {
    }

    public void UpdateModal() => _modality.Update();

    public void SetPosition(double left, double top) => Move(placement =>
    {
        var scale = _platform.Desktop.ScaleAt(left, top);
        return new Rect(left, top, placement.Bounds.Width / placement.Scale * scale,
            placement.Bounds.Height / placement.Scale * scale);
    });

    public void SetSize(double clientWidth, double clientHeight) => Move(placement =>
        new Rect(placement.Bounds.X, placement.Bounds.Y, clientWidth * placement.Scale, clientHeight * placement.Scale));

    public void UpdateOverlayTraits()
    {
    }

    public void ShowWindow(WindowState windowState)
    {
        _shown = true;
        if (windowState == WindowState.Minimized)
        {
            lock (_placementLock)
                _placement = _placement with { IsMinimized = true };
            ((UIApplication)UIContext.UIApplication).SetWindowHidden(_window, true);
            OnLoop(GiveUpActivation);
            return;
        }

        bool wasMinimized;
        lock (_placementLock)
        {
            wasMinimized = _placement.IsMinimized;
            // From the Dock the window comes back as it went, maximized or not.
            var maximized = windowState == WindowState.Maximized || wasMinimized && _placement.IsMaximized;
            var bounds = maximized ? ScreenOf(_placement.Restore)?.WorkArea ?? _placement.Restore : _placement.Restore;
            _placement = new Placement(_placement.Restore, bounds, ScaleOf(bounds), maximized, false);
        }

        Apply();
        ((UIApplication)UIContext.UIApplication).SetWindowHidden(_window, false);
        OnLoop(() =>
        {
            _platform.HiddenWindows.Remove(this);
            _platform.HiddenWindows.Add(this);
        });
        if (_window.ActivateOnShow || wasMinimized)
            OnLoop(() =>
            {
                if (_window.ActivateOnShow || !TakesNoActivation)
                    BecomeActive();
            });
    }

    public void HideWindow()
    {
        _shown = false;
        OnLoop(GiveUpActivation);
    }

    public void Activate() => OnLoop(() =>
    {
        if (Current.IsMinimized)
            _window.State = WindowState.Normal;
        BecomeActive();
    });

    public void RaiseWithoutActivation()
    {
    }

    public void SetMouseCapture(bool capture)
    {
    }

    public PixelPoint ScreenToClient(PixelPoint screen)
    {
        var bounds = Current.Bounds;
        return new PixelPoint(screen.X - (int)Math.Round(bounds.X), screen.Y - (int)Math.Round(bounds.Y));
    }

    public PixelPoint ClientToScreen(PixelPoint client)
    {
        var bounds = Current.Bounds;
        return new PixelPoint(client.X + (int)Math.Round(bounds.X), client.Y + (int)Math.Round(bounds.Y));
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

    // An overlay - a menu, a drag's compass - says so itself; automation only takes the default from the others.
    private bool TakesNoActivation => _window is SplashWindow ||
        _window is WindowBase { ActivateOnShow: false } window &&
        window.GetValueSource(WindowBase.ActivateOnShowProperty) < ValuePriority.TypeDefault;

    private void BecomeActive()
    {
        if (_modality.IsBlocked)
        {
            _modality.ActivateBlocker();
            return;
        }

        _platform.HiddenWindows.Remove(this);
        _platform.HiddenWindows.Add(this);
        var active = UIContext.UIApplication.Windows
            .Where(other => other.IsActive && other is not SplashWindow && !ReferenceEquals(other, _window)).ToList();
        foreach (var other in active)
            other.StopBeingActive(UIContext.UIApplication);
        if (!_closed && !_window.IsActive)
            _window.BecomeActive(UIContext.UIApplication);
    }

    // As AppKit does when the key window goes: its owner becomes active, else the one active before it.
    private void GiveUpActivation()
    {
        if (!_window.IsActive)
            return;

        _window.StopBeingActive(UIContext.UIApplication);
        var candidates = _platform.HiddenWindows.Where(other => other != this && other.CanTakeActivation).ToList();
        var next = candidates.LastOrDefault(other => ReferenceEquals(other._window, _window.Owner)) ??
                   candidates.LastOrDefault();
        next?.BecomeActive();
    }

    private bool CanTakeActivation =>
        _shown && !_closed && !Current.IsMinimized && !_modality.IsBlocked && !TakesNoActivation;

    // Moving or sizing a maximized window leaves it in no maximized state, as AppKit's zoomed one.
    private void Move(Func<Placement, Rect> move)
    {
        bool wasMaximized;
        lock (_placementLock)
        {
            // Placed before the window is made: SetWindow reads it from the window.
            if (_placement == null)
                return;

            wasMaximized = _placement.IsMaximized;
            var bounds = move(_placement);
            _placement = new Placement(bounds, bounds, ScaleOf(bounds), false, _placement.IsMinimized);
        }

        Apply();
        if (wasMaximized)
            OnLoop(TellState);
    }

    private void Apply()
    {
        _platform.RunOnMainThread(() =>
        {
            if (_layer != IntPtr.Zero)
                SizeLayer();
        });
        OnLoop(Tell);
    }

    // The placement as it is when this runs, not as it was when it was asked for: a newer one may have come meanwhile.
    private void Tell()
    {
        var placement = Current;
        var bounds = placement.Bounds;
        _window.DpiScale = new Vector2<double>(placement.Scale, placement.Scale);
        _window.DisplayRefreshRate = ScreenOf(bounds)?.RefreshRate ?? 60;
        _window.Width = bounds.Width;
        _window.Height = bounds.Height;
        _window.UpdatePositionFromPlatform(bounds.X, bounds.Y);
        _window.UpdateClientSizeFromPlatform(bounds.Width / placement.Scale, bounds.Height / placement.Scale);
        TellState();
    }

    private void TellState()
    {
        var placement = Current;
        if (!_shown || placement.IsMinimized)
            return;

        var state = placement.IsMaximized ? WindowState.Maximized : WindowState.Normal;
        if (_window.State == state)
            return;

        _window.StateChanged -= OnStateChanged;
        _window.State = state;
        _window.StateChanged += OnStateChanged;
    }

    private void SizeLayer()
    {
        var placement = Current;
        CAMetalLayer.SetContentsScale(_layer, placement.Scale);
        CAMetalLayer.SetDrawableSize(_layer, new CGSize(placement.Bounds.Width, placement.Bounds.Height));
    }

    private void OnWindowClosed(object sender, EventArgs e)
    {
        if (_closed)
            return;

        _closed = true;
        _window.StateChanged -= OnStateChanged;
        _platform.Unregister(_modality);
        _modality.Close();
        Input.DragDrop.UnregisterNativeDropTarget(_window);
        UIContext.UIApplication.RemoveWindow(_window);
        _window.SetHandle(IntPtr.Zero);
        MacOSPlatform.DispatchInput(() =>
        {
            _platform.HiddenWindows.Remove(this);
            GiveUpActivation();
        });
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

    private double ScaleOf(Rect bounds) => _platform.Desktop.ScaleAt(bounds.X, bounds.Y);

    private static ScreenInfo ScreenOf(Rect bounds) =>
        Displays.Screens.Count == 0 ? null : WindowPlacer.ScreenOf(bounds, Displays.Screens);

    private static double Extent(double preferred, double fallback, int defaultExtent)
    {
        if (!double.IsNaN(preferred) && preferred > 0)
            return preferred;

        return !double.IsNaN(fallback) && fallback > 0 ? fallback : defaultExtent;
    }

    // In physical pixels of the desktop.
    private sealed record Placement(Rect Restore, Rect Bounds, double Scale, bool IsMaximized, bool IsMinimized);
}
