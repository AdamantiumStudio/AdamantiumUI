using System;
using System.Threading;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Platforms;
using Adamantium.UI.Platforms.Wayland;
using Adamantium.UI.Platforms.Windows;

namespace Adamantium.UI;

/// <summary>Runs the splash window on a thread, a dispatcher and a connection to the desktop of its own, so that it
/// shows, moves and answers its close button while the application's own thread is busy starting.</summary>
internal sealed class SplashHost
{
    private readonly UIApplication _application;
    private readonly Type _windowType;
    private readonly WindowPlacement _startupPlacement;
    private readonly CancellationTokenSource _stop = new();
    private Threading.Dispatcher _dispatcher;
    private IApplicationPlatform _platform;
    private SplashWindow _window;
    private IWindow _below;
    private int _handedOver;
    private int _closing;
    private int _closed;
    private int _closable;
    private volatile bool _running;

    /// <param name="startupPlacement">Where the startup window was remembered: the splash window appears over it.</param>
    public SplashHost(UIApplication application, Type windowType, WindowPlacement startupPlacement)
    {
        _application = application;
        _windowType = windowType;
        _startupPlacement = startupPlacement;
    }

    /// <summary>Raised when the window is closed before the application handed over: the start is cancelled. A splash
    /// shown while the application runs has nobody listening, and closing it cancels nothing.</summary>
    public event Action Canceled;

    /// <summary>The splash window, once it is made.</summary>
    public SplashWindow Window => Volatile.Read(ref _window);

    /// <summary>From <see cref="Start"/> until the window has closed and its connection is gone, or it failed.</summary>
    public bool IsRunning => _running;

    /// <summary>Whether the window was closed, or asked to close, already.</summary>
    public bool IsHandedOver => Volatile.Read(ref _handedOver) != 0;

    public void Start()
    {
        _running = true;
        var thread = new Thread(Run) { IsBackground = true, Name = "AdamantiumSplash" };
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }

        thread.Start();
    }

    /// <summary>Closes the window, on the loop as every window closes, because the application has shown its own; this
    /// cancels nothing. A window not made yet is closed as soon as it is shown.</summary>
    public void HandOver()
    {
        if (Interlocked.Exchange(ref _handedOver, 1) == 0 && Volatile.Read(ref _closable) != 0)
        {
            CloseOnLoop();
        }
    }

    /// <summary>Keeps the window above <paramref name="below"/>, the application's main window, while both are up: from
    /// now, or from when the window is shown.</summary>
    public void KeepAbove(IWindow below)
    {
        Volatile.Write(ref _below, below);
        if (!_running || Window is not { } window)
        {
            return;
        }

        _dispatcher.InvokeAsync(() => StayAbove(window));
    }

    /// <summary>Ends the splash window's loop and its connection because the application is ending - after its windows'
    /// swapchains are gone - with no loop left to close the window on; this cancels nothing.</summary>
    public void Abandon()
    {
        Interlocked.Exchange(ref _handedOver, 1);
        _stop.Cancel();
    }

    private void Run()
    {
        try
        {
            Open();
        }
        catch (Exception e)
        {
            Serilog.Log.Logger.Error(e, "The splash window failed; the application goes on without it");
            Interlocked.Exchange(ref _handedOver, 1);
            BecomeClosable();
        }

        try
        {
            if (_dispatcher != null && Window != null)
            {
                _dispatcher.Run(_stop.Token);
            }
        }
        catch (Exception e)
        {
            Serilog.Log.Logger.Error(e, "The splash window's loop failed");
        }
        finally
        {
            _dispatcher?.Detach();
            (_platform as WaylandPlatform)?.Disconnect();
            (_platform as WindowsPlatform)?.Disconnect();
            _running = false;
            LoopSignal.Request();
        }
    }

    private void Open()
    {
        _platform = CreatePlatform();
        _dispatcher = new Threading.Dispatcher(_platform);
        if (_platform is WaylandPlatform wayland)
        {
            wayland.Dispatcher = _dispatcher;
        }

        var window = (SplashWindow)Activator.CreateInstance(_windowType);
        if (ThemeContext.GetTheme(window) == null)
        {
            ThemeContext.SetTheme(window, new Themes.SplashTheme.Splash());
        }

        window.StartupPlacement = _startupPlacement;
        Volatile.Write(ref _window, window);
        window.AttachContextAndInitialize(new SplashUIContext(_application.UIContext, _platform, _dispatcher));
        window.Closed += OnClosed;
        Interlocked.Exchange(ref _closable, 1);
        window.Show();
        StayAbove(window);
        if (IsHandedOver)
        {
            CloseOnLoop();
        }
    }

    private void StayAbove(SplashWindow window)
    {
        if (!window.IsClosed && Volatile.Read(ref _below) is { IsClosed: false } below)
        {
            window.StayAbove(below);
        }
    }

    private void BecomeClosable()
    {
        if (Window is { } window && Interlocked.Exchange(ref _closable, 1) == 0)
        {
            window.Closed += OnClosed;
        }

        CloseOnLoop();
    }

    private void CloseOnLoop()
    {
        if (Window is { } window && Interlocked.Exchange(ref _closing, 1) == 0)
        {
            LoopSignal.Post(() =>
            {
                if (!window.IsClosed)
                {
                    window.Close();
                }
            });
        }
    }

    private void OnClosed(object sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _closing, 1);
        if (Interlocked.Exchange(ref _handedOver, 1) == 0)
        {
            Canceled?.Invoke();
        }

        _dispatcher.InvokeAsync(_stop.Cancel);
    }

    private static IApplicationPlatform CreatePlatform() => Configuration.Platform switch
    {
        Platform.Linux => new WaylandPlatform(false),
        Platform.Windows => new WindowsPlatform(false),
        _ => throw new PlatformNotSupportedException($"No splash window on {Configuration.Platform} yet.")
    };
}
