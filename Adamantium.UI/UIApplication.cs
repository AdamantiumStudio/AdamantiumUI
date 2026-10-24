using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Adamantium.Core;
using Adamantium.Core.Collections;
using Adamantium.Core.DependencyInjection;
using Adamantium.Core.Events;
using Adamantium.ECS;
using Adamantium.Multiverse;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.UI.Core.Diagnostics;
using Adamantium.UI.AggregatorEvents;
using Adamantium.UI.ApplicationModel;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Dispatcher;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Core.Rendering;
using Adamantium.UI.Platforms.Windows;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.EntityServices;
using Adamantium.UI.Events;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Controls.Navigation;
using Adamantium.UI.Navigation;
using Adamantium.UI.Platforms.MacOS;
using Adamantium.UI.Rendering;
using Adamantium.UI.Themes.FluentTheme;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using UnhandledExceptionEventArgs = Adamantium.UI.Core.RoutedEvents.UnhandledExceptionEventArgs;
using UnhandledExceptionEventHandler = Adamantium.UI.Core.RoutedEvents.UnhandledExceptionEventHandler;

namespace Adamantium.UI;

public abstract class UIApplication : FundamentalUIComponent, IAdamantiumApplication, IUIApplication, IWindowPlatformService
{
    private readonly object applicationLocker = new object();
    
    private Dictionary<IWindow, WindowRenderService> windowToSystem;
    private WindowRenderService[] _windowServices = [];
    private readonly Dictionary<IWindow, Entity> windowToEntity = new();

    private double accumulatedFrameTime;
    private TimeSpan totalTime;
    private int _countedFrames;
    private long _countingSince = Stopwatch.GetTimestamp();
    private PreciseTimer preciseTimer;
    private Double fpsTime;
    private Int32 fpsCounter;
    private AppTime appTime;
        
    private IWindow mainWindow;
    private AdamantiumCollection<IWindow> windowsCollection;
    private List<IWindow> addedWindows;
    private bool firstWindowAdded;
    private Thread applicationLoopThread;
    private CancellationTokenSource cancellationTokenSource;

    // The loop never waits for the render: past MaxFramesInFlight unrendered frames it stops recording, not updating.
    private Thread renderThread;
    // Held around each render frame, so window teardown and shutdown never dispose a device mid-frame.
    private readonly object _renderGate = new object();
    // ShutDown can arrive from any thread at any time; this guards re-entry.
    private volatile bool _isShuttingDown;
    private int _framesInFlight;
    private AppTime _renderAppTime;   // time of the newest recorded frame
    private const int MaxFramesInFlight = 2;
    private const int RetainedLogRuns = 10;
    private const long LogSizeLimitBytes = 32L * 1024 * 1024;
    private const int IdleWakeMs = 250;
    private const double StallFrameSeconds = 1.0 / 20.0;
    private const double StallAnimationStep = 0.004;
    private const int DiscardReleasesPerFrame = 128;
    private long _loopFrameDue;
    private IVisualRenderer _visualRenderer;
    private bool _recordedThisFrame;
    private const int LoopExitWaitMs = 2000;
    private const double LowestLoopRate = 60;
    private long _frameRequestsRecorded = -1;
    private bool _withheldSinceShown;
    private bool _recordSkipped;

    private readonly record struct Claims(bool AnyDrawn, bool AnyDrawable, bool LeftWaiting);

    private long _publishedFrame;
    private bool _drawFailing;
    private long _presentedFrame;
    private long _requestsAtRecord;
    private long _loadsAtRecord;
    private readonly HashSet<IWindow> _minimizedWindows = [];
    private readonly HashSet<IWindow> _hiddenWindows = [];
    private IWindow[] _arrivingWindows = [];
    private int _arrivingAt;
    private readonly HashSet<IWindow> _closedOnArrival = [];
    private string _unrecordedBecause;
    private readonly List<TaskCompletionSource> _idleWaiters = [];
    private long _idleFrame = -1;
    private readonly object _debugToolsLock = new();
    private readonly Dictionary<Diagnostics.IDebugTool, bool> _debugTools = new();
    private readonly HashSet<Diagnostics.IDebugTool> _debugToolsToDetach = [];
    private SplashHost _splash;
    private readonly CancellationTokenSource _startupCancellation = new();
    private volatile bool _startupDone;
    private volatile bool _showingStartupWindow;
    private int _splashHolds;
    private bool _loopStarted;

    protected UIApplication()
    {
        Current = this;
        UIAppContext.Initialize(this, this);
        DesiredFPS = 60;
        appTime = new AppTime();
        windowToSystem = new Dictionary<IWindow, WindowRenderService>();
        addedWindows = new List<IWindow>();
        windowsCollection = new AdamantiumCollection<IWindow>();
        
        preciseTimer = new PreciseTimer();

        Container = new AdamantiumDependencyContainer();
        EventAggregator = Container.Resolve<IEventAggregator>();
        ApplicationBuilder.Build(Container);
        ResourceManager = CreateResourceManager();
        ThemeManager = CreateThemeManager(Container);
        UIContext =  new UIContext(Container, this);

        GraphicsDeviceService = new GraphicsDeviceService(Container.Resolve<IGraphicsDeviceFactory>(), EnableGraphicsDebug,
            CreateGraphicsSettings());
        GraphicsDeviceService.DeviceLost += OnDeviceLost;
        Container.RegisterInstance<IGraphicsDeviceService>(GraphicsDeviceService);
        Container.RegisterSingleton<IResourceFactory, ResourceFactory>();
        Container.RegisterSingleton<IGraphicsContext, GraphicsContext>();
        GraphicsContext = Container.Resolve<IGraphicsContext>();
        var satellites = new Satellites();
        satellites.Add<IAdamantiumApplication>(this);
        EntityWorld = new EntityWorld(Container, satellites);
        RegisterBasicServices(Container);
        
        applicationLoopThread = new Thread(ApplicationLoopThread);

        ConfigureLogging();
    }

    protected virtual IThemeManager CreateThemeManager(IDependencyContainer container)
    {
        return new ThemeManager(Container);
    }

    /// <summary>What the application asks of Vulkan. Called from the constructor, before a subclass's fields are set.</summary>
    protected virtual GraphicsSettings CreateGraphicsSettings() => GraphicsSettings.CreateDefault();

    protected virtual IResourceManager CreateResourceManager()
    {
        var resourceManager = new ResourceManager();
        
        return resourceManager;
    }

    private void ConfigureLogging()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(directory);
        SweepOldLogs(directory);

        var path = Path.Combine(directory, $"adamantium_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(theme: AnsiConsoleTheme.Code)
            // Stops at the limit instead of rolling: the first occurrences, the ones worth reading, are at the top.
            .WriteTo.File(path, fileSizeLimitBytes: LogSizeLimitBytes, rollOnFileSizeLimit: false)
            .CreateLogger();
    }

    private static void SweepOldLogs(string directory)
    {
        try
        {
            var stale = new DirectoryInfo(directory)
                .GetFiles("adamantium_*.txt")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(RetainedLogRuns);

            foreach (var file in stale) file.Delete();
        }
        catch (IOException)
        {
            // Held open by another instance; not worth failing a launch over.
        }
    }

    public static UIApplication Current { get; private set; }

    public static readonly AdamantiumProperty EnableGraphicsDebugProperty =
        AdamantiumProperty.Register(nameof(EnableGraphicsDebug), typeof(bool), typeof(UIApplication),
            new PropertyMetadata(false, GraphicsDebugChangedCallback));

    private static void GraphicsDebugChangedCallback(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is UIApplication { GraphicsDeviceService: not null } ui)
        {
            Log.Logger.Debug("GraphicsDebugChangedCallback called");
            ui.GraphicsDeviceService.IsInDebugMode = (bool)e.NewValue;
            ui.GraphicsDeviceService.DeviceUpdateNeeded = true;
        }
    }
    
    public bool EnableGraphicsDebug
    {
        get => GetValue<bool>(EnableGraphicsDebugProperty);
        set => SetValue(EnableGraphicsDebugProperty, value);
    }

    /// <summary>The presentation policy of every window that does not set its own <see cref="IWindow.PresentPolicy"/>.
    /// Set it at startup: swapchains that already exist are not rebuilt.</summary>
    public Graphics.Core.Presentation.PresentPolicy PresentPolicy { get; set; } =
        Graphics.Core.Presentation.PresentPolicy.Adaptive;

    public static readonly AdamantiumProperty UpdateRateProperty = AdamantiumProperty.Register(nameof(UpdateRate),
        typeof(uint), typeof(UIApplication), new PropertyMetadata(IUIApplication.DefaultUpdateRate, RefreshRatesChanged));

    /// <summary>How often the loop updates at most, in hertz; 120 by default. Below it the loop keeps the pace of the
    /// fastest window, and no slower than 60 so input is not held up.</summary>
    public uint UpdateRate
    {
        get => GetValue<uint>(UpdateRateProperty);
        set => SetValue(UpdateRateProperty, value);
    }

    /// <summary>The pace the loop keeps now, in hertz.</summary>
    public double LoopRate { get; private set; } = IUIApplication.DefaultUpdateRate;

    public static readonly AdamantiumProperty RefreshRateProperty = AdamantiumProperty.Register(nameof(RefreshRate),
        typeof(uint), typeof(UIApplication), new PropertyMetadata(0u, RefreshRatesChanged));

    public static readonly AdamantiumProperty MinimumRefreshRateProperty = AdamantiumProperty.Register(
        nameof(MinimumRefreshRate), typeof(uint), typeof(UIApplication), new PropertyMetadata(0u, RefreshRatesChanged));

    public static readonly AdamantiumProperty MaximumRefreshRateProperty = AdamantiumProperty.Register(
        nameof(MaximumRefreshRate), typeof(uint?), typeof(UIApplication), new PropertyMetadata(null, RefreshRatesChanged));

    public static readonly AdamantiumProperty UnlimitedRefreshRateProperty = AdamantiumProperty.Register(
        nameof(UnlimitedRefreshRate), typeof(bool), typeof(UIApplication), new PropertyMetadata(false, UnlimitedRefreshRateChanged));

    private static void RefreshRatesChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e) =>
        LoopSignal.Request();

    private static void UnlimitedRefreshRateChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (Equals(e.OldValue, e.NewValue) || component is not UIApplication application)
        {
            return;
        }

        foreach (var window in application.Windows)
        {
            if (window.UnlimitedRefreshRate == null)
            {
                window.Renderer?.InvalidatePresenter();
            }
        }

        LoopSignal.Request();
    }

    /// <summary>The windows' <see cref="IWindow.UnlimitedRefreshRate"/> where they set none; false by default.</summary>
    public bool UnlimitedRefreshRate
    {
        get => GetValue<bool>(UnlimitedRefreshRateProperty);
        set => SetValue(UnlimitedRefreshRateProperty, value);
    }

    /// <summary>Whether <paramref name="window"/> draws unlimited: its own choice, or the application's.</summary>
    internal static bool RunsUnlimited(IWindow window) =>
        window.UnlimitedRefreshRate ?? Current?.UnlimitedRefreshRate ?? false;

    /// <summary>The windows' <see cref="IWindow.RefreshRate"/> where they set none; 0 by default.</summary>
    public uint RefreshRate
    {
        get => GetValue<uint>(RefreshRateProperty);
        set => SetValue(RefreshRateProperty, value);
    }

    /// <summary>The windows' <see cref="IWindow.MinimumRefreshRate"/> where they set none; 0 by default.</summary>
    public uint MinimumRefreshRate
    {
        get => GetValue<uint>(MinimumRefreshRateProperty);
        set => SetValue(MinimumRefreshRateProperty, value);
    }

    /// <summary>The windows' <see cref="IWindow.MaximumRefreshRate"/> where they set none; null, the default, is their
    /// monitor's rate.</summary>
    public uint? MaximumRefreshRate
    {
        get => GetValue<uint?>(MaximumRefreshRateProperty);
        set => SetValue(MaximumRefreshRateProperty, value);
    }

    /// <summary>The frame verifier: off by default, switched on at run time to hunt a rendering bug.</summary>
    public Rendering.Verification.FrameVerification FrameVerification { get; } = new();

    /// <summary>Starts <paramref name="tool"/> on this application, on the calling thread; it stays attached until
    /// detached or until the application shuts down. Attaching a tool twice attaches it once; once the application has
    /// begun to shut down, attaching throws <see cref="InvalidOperationException"/>. A tool whose
    /// <see cref="Diagnostics.IDebugTool.Attach"/> throws is detached and the exception goes on; a tool asked to detach,
    /// or caught by the shutdown, while it attaches is detached as soon as Attach returns.</summary>
    public void AttachDebugTool(Diagnostics.IDebugTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        lock (_debugToolsLock)
        {
            if (_isShuttingDown)
            {
                throw new InvalidOperationException("The application is shutting down.");
            }

            if (!_debugTools.TryAdd(tool, true))
            {
                return;
            }
        }

        try
        {
            tool.Attach(this);
        }
        catch
        {
            lock (_debugToolsLock)
            {
                _debugTools.Remove(tool);
                _debugToolsToDetach.Remove(tool);
            }

            DetachLogged(tool);
            throw;
        }

        bool detachNow;
        lock (_debugToolsLock)
        {
            detachNow = _debugToolsToDetach.Remove(tool) || _isShuttingDown;
            if (detachNow)
            {
                _debugTools.Remove(tool);
            }
            else
            {
                _debugTools[tool] = false;
            }
        }

        if (detachNow)
        {
            DetachLogged(tool);
        }
    }

    /// <summary>Stops <paramref name="tool"/> on the calling thread, if it is attached; a tool still attaching is
    /// stopped when its Attach returns.</summary>
    public void DetachDebugTool(Diagnostics.IDebugTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        lock (_debugToolsLock)
        {
            if (!_debugTools.TryGetValue(tool, out var attaching))
            {
                return;
            }

            if (attaching)
            {
                _debugToolsToDetach.Add(tool);
                return;
            }

            _debugTools.Remove(tool);
        }

        tool.Detach();
    }

    public IWindow MainWindow
    {
        get => mainWindow;
        set
        {
            if (mainWindow != null)
            {
                mainWindow.Closed -= MainWindow_Closed;
            }
            mainWindow = value;
            if (mainWindow != null)
            {
                mainWindow.Closed += MainWindow_Closed;
            }
        }
    }
    
    public IWindow ActiveWindow { get; private set; }

    public IResourceManager ResourceManager { get; }
    public IThemeManager ThemeManager { get; private set; }
    public IDispatcher Dispatcher { get; private set; }
    public IUIContext UIContext { get; private set; }

    public Adamantium.Navigation.INavigationService Navigation { get; private set; }

    /// <summary>Queues a window to join on the next frame: this runs on the pump thread while the loop walks the
    /// collections it would join.</summary>
    public void AddWindow(IWindow window)
    {
        lock (applicationLocker)
        {
            addedWindows.Add(window);
        }

        LoopSignal.Request();   // registered on the next frame; do not wait for the idle timeout
    }

    /// <summary>Tears the window down right away, never queued: a frame late, the render thread would draw into a
    /// surface that is already gone. A window closed before its first frame is never registered.</summary>
    public void RemoveWindow(IWindow window)
    {
        lock (_hiddenWindows)
        {
            _hiddenWindows.Remove(window);
        }

        lock (applicationLocker)
        {
            if (addedWindows.Remove(window))
            {
                ForgetWindow(window);
                return;
            }

            var arriving = Array.IndexOf(_arrivingWindows, window);
            if (arriving >= 0 && arriving >= _arrivingAt)
            {
                _closedOnArrival.Add(window);
                ForgetWindow(window);
                return;
            }
        }

        OnWindowRemoved(window);
    }

    /// <summary>Stops drawing into the window, or starts again. <paramref name="betweenFrames"/> runs once no frame
    /// draws into it any more.</summary>
    internal void SetWindowHidden(IWindow window, bool hidden, Action betweenFrames = null)
    {
        lock (_renderGate)
        {
            lock (_hiddenWindows)
            {
                if (hidden)
                {
                    _hiddenWindows.Add(window);
                }
                else
                {
                    _hiddenWindows.Remove(window);
                }
            }

            if (windowToSystem.TryGetValue(window, out var service))
            {
                service.Hidden = hidden;
            }

            betweenFrames?.Invoke();
        }
    }

    public void SetActiveWindow(IWindow window)
    {
        if (window is not SplashWindow)
        {
            ActiveWindow = window;
        }
    }

    public void InactivateWindow(IWindow window)
    {
        if (ActiveWindow == window) 
            ActiveWindow = null;
    }

    public void ExecuteOnUIThread(Action action)
    {
        Dispatcher.Invoke(action);
    }

    public async Task ExecuteOnUIThreadAsync(Action action)
    {
        await Dispatcher.InvokeAsync(action);
    }

    public IReadOnlyList<IWindow> Windows => windowsCollection;
    public IWindowWorkerService GetWindowWorker(IUIContext uiContext)
    {
        switch (Configuration.Platform)
        {
            case Platform.Windows:
                return new Win32WindowWorker(uiContext);
            case Platform.OSX:
                return new MacOSWindowWorker(uiContext);
            case Platform.Linux:
                return new Platforms.Wayland.WaylandWindowWorker(uiContext);
            default:
                throw new NotSupportedException($"{Configuration.Platform} does not yet supported for windowing system");
        }
    }

    public static readonly AdamantiumProperty ShutDownModeProperty = AdamantiumProperty.Register(nameof(ShutDownMode),
        typeof(ShutDownMode), typeof(UIApplication), new PropertyMetadata(ShutDownMode.OnMainWindowClosed));

    public static readonly AdamantiumProperty StartupTypeProperty = AdamantiumProperty.Register(nameof(StartupType),
        typeof(Type), typeof(UIApplication), new PropertyMetadata(null));

    public static readonly AdamantiumProperty SplashTypeProperty = AdamantiumProperty.Register(nameof(SplashType),
        typeof(Type), typeof(UIApplication), new PropertyMetadata(null));

    public static readonly AdamantiumProperty StartupThemeProperty = AdamantiumProperty.Register(nameof(StartupTheme),
        typeof(Type), typeof(UIApplication), new PropertyMetadata(null));

    public static readonly AdamantiumProperty StartupThemeVariantProperty = AdamantiumProperty.Register(nameof(StartupThemeVariant),
        typeof(ThemeVariant), typeof(UIApplication), new PropertyMetadata(default(ThemeVariant)));

    public static readonly AdamantiumProperty StartupLanguageProperty = AdamantiumProperty.Register(nameof(StartupLanguage),
        typeof(string), typeof(UIApplication), new PropertyMetadata(null));

    /// <summary>When the application ends: with its main window (the default), with its last window, or only when told.</summary>
    public ShutDownMode ShutDownMode
    {
        get => GetValue<ShutDownMode>(ShutDownModeProperty);
        set => SetValue(ShutDownModeProperty, value);
    }

    /// <summary>The window the application opens first.</summary>
    public Type StartupType
    {
        get => GetValue<Type>(StartupTypeProperty);
        set => SetValue(StartupTypeProperty, value);
    }

    /// <summary>The window shown while the application starts, until its first window has drawn. The blueprint states
    /// it; a value set in code before <see cref="Run()"/> wins. Unset shows none. With one, the loop already draws while
    /// <see cref="OnStartup"/> runs, as it does while any later window is made.</summary>
    public Type SplashType
    {
        get => GetValue<Type>(SplashTypeProperty);
        set => SetValue(SplashTypeProperty, value);
    }

    /// <summary>Canceled when the splash window is closed before the application has shown a window of its own: the
    /// start is abandoned, and a long one can stop early by it.</summary>
    public CancellationToken StartupCancellation => _startupCancellation.Token;

    /// <summary>Whether the application is still starting under its splash window: until a window of its own has drawn
    /// and the splash window has closed.</summary>
    public bool IsStarting => _splash is { IsRunning: true };

    /// <summary>The theme the application opens on: Fluent (when unset), Graphite, MacOs or one of the application's
    /// own, which is added to the themes. The application's blueprint states it; a value set in code wins over the
    /// blueprint, and the ADAM_THEME environment variable, a theme's name, over both.</summary>
    public Type StartupTheme
    {
        get => GetValue<Type>(StartupThemeProperty);
        set => SetValue(StartupThemeProperty, value);
    }

    /// <summary>The variant of the theme the application opens on: Light, Dark, System - which follows the operating
    /// system and keeps following it - or one of the theme's own; unset opens on the theme's default. A variant the theme
    /// does not have fails the start, naming the ones it has.</summary>
    public ThemeVariant StartupThemeVariant
    {
        get => GetValue<ThemeVariant>(StartupThemeVariantProperty);
        set => SetValue(StartupThemeVariantProperty, value);
    }

    /// <summary>The language the application opens in, by name ("en", "ru"); unset opens in the base language of its
    /// language files. The application's own setting: the operating system's language is not read.</summary>
    public string StartupLanguage
    {
        get => GetValue<string>(StartupLanguageProperty);
        set => SetValue(StartupLanguageProperty, value);
    }

    /// <summary>The language the application shows. Setting it switches every string while the application runs.</summary>
    public string Language
    {
        get => Languages.Current;
        set => Languages.Current = value;
    }

    public IDependencyContainer Container { get; private set; }

    protected IGraphicsDeviceService GraphicsDeviceService { get; private set; }
    
    public IGraphicsContext GraphicsContext { get; private set; }
    
    protected IEventAggregator EventAggregator { get; private set; }
    
    public EntityWorld EntityWorld { get; private set; }

    public bool IsRunning => cancellationTokenSource != null && cancellationTokenSource.IsCancellationRequested != true;
    
    public bool IsInitialized { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsFixedTimeStep { get; set; }
    public double TimeStep => 1.0d / DesiredFPS;
    public uint DesiredFPS { get; set; }
    
    public bool DisableRendering { get; set; }

    internal PointerDevice PointerDevice => PointerDevice.Current;
    internal KeyboardDevice KeyboardDevice => KeyboardDevice.CurrentDevice;

    private void MainWindow_Closed(object sender, EventArgs e)
    {
        MainWindow = null;
    }
    
    private void OnDeviceLost(object sender, EventArgs e) => LoopSignal.Request();

    private void RecreateDevicesAndServices()
    {
        Log.Logger.Debug("======Starting recreating sequence======");
        // The render thread must not draw while the devices it draws with are destroyed and remade.
        OnDevicesRecreating();
        try
        {
            RecreateUnderRenderGate();
        }
        finally
        {
            OnDevicesRecreated();
        }

        Log.Logger.Debug("======Finish recreating sequence======");
    }

    /// <summary>Called before the devices are destroyed and remade, on the loop thread: whatever else draws with them has
    /// to stop drawing until <see cref="OnDevicesRecreated"/>.</summary>
    protected virtual void OnDevicesRecreating()
    {
    }

    /// <summary>Called once the devices are remade (or remaking them failed).</summary>
    protected virtual void OnDevicesRecreated()
    {
    }

    private void RecreateUnderRenderGate()
    {
        lock (_renderGate)
        {
            EntityWorld.RemoveAllServices();
            EntityWorld.RemoveAllEntities();
            EntityWorld.ForceUpdate();
            windowToSystem.Clear();
            SnapshotWindowServices();
            windowToEntity.Clear();
            foreach (var window in Windows)
            {
                window.InvalidateRender(true);
            }
            Graphics.Fonts.FontAtlasStore.Reset();
            GraphicsDeviceService.ChangeOrCreateMainDevice("Adamantium Main", true);
            foreach (var window in Windows)
            {
                CreateWindowService(window);
            }
            EntityWorld.ForceUpdate();
            Interlocked.Exchange(ref _framesInFlight, 0);
        }
    }

    private void CreateWindowService(IWindow window)
    {
        var windowService = new WindowRenderService(EntityWorld, window);
        lock (_renderGate)
        {
            lock (_hiddenWindows)
            {
                windowService.Hidden = _hiddenWindows.Contains(window);
            }

            EntityWorld.AddService(windowService);
            windowToSystem.Add(window, windowService);
            SnapshotWindowServices();
        }

        var entity = new Entity();
        entity.AddComponent(window);
        windowToEntity[window] = entity;
        EntityWorld.EntityManager.AddEntity(entity);
        EntityWorld.ForceUpdate();
    }
    
    private void OnWindowAdded(IWindow window)
    {
        CreateWindowService(window);

        windowsCollection.Add(window);

        if (!firstWindowAdded && window != _splash?.Window)
        {
            firstWindowAdded = true;
        }

        if (window == MainWindow)
        {
            _splash?.KeepAbove(window);
        }
    }

    private void OnWindowRemoved(IWindow window)
    {
        if (!windowToSystem.TryGetValue(window, out var service)) return;

        // The render thread waits between frames while this window's resources go, then draws the other windows on.
        lock (_renderGate)
        {
            service.UnloadContent();
            windowToSystem.Remove(window);
            SnapshotWindowServices();
            lock (_minimizedWindows)
            {
                _minimizedWindows.Remove(window);
            }
            windowsCollection.Remove(window);
            EntityWorld.RemoveService(service);
            if (windowToEntity.Remove(window, out var entity))
            {
                EntityWorld.EntityManager.RemoveEntity(entity);
            }
        }
        EntityWorld.ForceUpdate();
        ForgetWindow(window);
    }

    private void ForgetWindow(IWindow window)
    {
        if (window == ActiveWindow)
        {
            ActiveWindow = null;
        }

        if (window == MainWindow)
        {
            MainWindow = null;
        }
    }

    private void Initialize()
    {
        if (IsInitialized) return;

        cancellationTokenSource = new CancellationTokenSource();
        _ = Task.Run(() => Theme.SystemDefaultFontFamily);
        _ = Task.Run(Core.TypeParsers.SelectorParser.IndexLoadedAssemblies);
        _ = Task.Run(() => System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(FX.UIBasicEffect).TypeHandle));
        Threading.Dispatcher.Initialize(UIContext);
        Dispatcher = Threading.Dispatcher.CurrentDispatcher;
        GraphicsDeviceService.IsInDebugMode = EnableGraphicsDebug;
        var devices = Task.Run(() =>
        {
            GraphicsDeviceService.CreateMainDevice("Adamantium Main");
            // Before any window, so a crash while creating shaders costs a child process, not the application.
            ShaderPrecompiler.EnsureCompiled(GraphicsDeviceService.ResourceLoaderDevice as GraphicsDevice);
        });

        if (StartWithSplash())
        {
            devices.GetAwaiter().GetResult();
            EntityWorld.Initialize();
            StartLoop();
        }

        ReadBlueprint();
        LoadThemes();
        LoadLanguage();
        devices.GetAwaiter().GetResult();
        SubscribeToEvents();

        if (!_loopStarted)
        {
            EntityWorld.Initialize();
        }

        OnInitialize();
        RegisterServices(Container);
        _visualRenderer = Container.Resolve<IVisualRenderer>();   // its device is created on the first snapshot
        IsInitialized = true;
        
        if (MainWindow != null)
        {
            OnWindowCreated(MainWindow);
        }
    }

    protected virtual void OnInitialize()
    {
    }

    /// <summary>Sets the application up as <see cref="Run()"/> does - its themes and its services - with no window, loop or
    /// device of its own: for a host that builds this application's views without running it (the designer).</summary>
    public void InitializeWithoutRunning()
    {
        ReadBlueprint();
        LoadThemes();
        LoadLanguage();
        OnInitialize();
        // First: the container resolves the first registration of a service, so a stand-in made here is the one used.
        RegisterDesignServices(Container);
        RegisterServices(Container);
    }

    /// <summary>Registers design-time stand-ins for services the designer must not run for real - the network, files, a
    /// database. Called only by the designer, BEFORE <see cref="RegisterServices"/>: the first registration of a service
    /// wins, so a stand-in registered here is what view-models get in the preview.</summary>
    protected virtual void RegisterDesignServices(IContainerRegistry containerRegistry)
    {
    }

    private ITheme StartupThemeInstance()
    {
        if (StartupTheme == null)
        {
            return null;
        }

        var theme = ThemeManager.Themes.FirstOrDefault(t => t.GetType() == StartupTheme);
        if (theme == null)
        {
            theme = (ITheme)Activator.CreateInstance(StartupTheme);
            ThemeManager.AddTheme(theme.Name, theme);
        }

        return theme;
    }

    private void ReadBlueprint()
    {
        var blueprintType = GetType().Assembly.GetCustomAttribute<ApplicationBlueprintAttribute>()?.BlueprintType;
        if (blueprintType == null)
        {
            return;
        }

        var blueprint = (IApplicationBlueprint)Activator.CreateInstance(blueprintType);
        TakeFromBlueprint(StartupTypeProperty, blueprint.StartupWindow);
        TakeFromBlueprint(StartupThemeProperty, blueprint.StartupTheme);
        TakeFromBlueprint(StartupThemeVariantProperty, blueprint.StartupThemeVariant);
        TakeFromBlueprint(StartupLanguageProperty, blueprint.StartupLanguage);
        TakeFromBlueprint(ShutDownModeProperty, blueprint.ShutDownMode);

        OnLoop(() =>
        {
            if (blueprint.Resources != null)
            {
                ResourceManager.AddSource(this, blueprint.Resources, ResourceScope.Global);
            }

            foreach (var include in blueprint.StyleIncludes ?? [])
            {
                if (include.Source != null)
                {
                    ThemeManager.AddStyleSet(include.Source);
                }
            }
        });
    }

    private void OnLoop(Action work)
    {
        if (!_loopStarted)
        {
            work();
            return;
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        LoopSignal.Post(() =>
        {
            try
            {
                work();
                done.SetResult();
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        });
        Task.WaitAny([done.Task], cancellationTokenSource.Token);
        done.Task.GetAwaiter().GetResult();
        StartupCancellation.ThrowIfCancellationRequested();
    }

    private void TakeFromBlueprint(AdamantiumProperty property, object value)
    {
        if (value != null)
        {
            SetValue(property, value, ValuePriority.Style);
        }
    }

    private void LoadLanguage()
    {
        var language = string.IsNullOrEmpty(StartupLanguage) ? Languages.Available.FirstOrDefault() : StartupLanguage;
        OnLoop(() => Languages.Current = language);
    }

    private void LoadThemes()
    {
        // One theme with light and dark variants: two themes rebuilt every template just to change the colors.
        var fluent = new Fluent();
        ThemeManager.AddTheme(fluent.Name, fluent);

        // A second theme proves the first is a theme and not the framework's own look.
        var graphite = new Themes.GraphiteTheme.Graphite();
        ThemeManager.AddTheme(graphite.Name, graphite);

        // Owns the window and the caption so far and borrows the rest from Fluent.
        var macOs = new Themes.MacOsTheme.MacOs();
        ThemeManager.AddTheme(macOs.Name, macOs);

        // ADAM_THEME wins over StartupTheme: a theme is fully exercised only when current from the first frame.
        var requested = Environment.GetEnvironmentVariable("ADAM_THEME");
        var startOn = string.IsNullOrEmpty(requested) ? StartupThemeInstance() ?? fluent : ThemeManager[requested];
        if (startOn == null)
        {
            throw new InvalidOperationException(
                $"No theme is named '{requested}'. The themes are: {string.Join(", ", ThemeManager.Themes.Select(t => t.Name))}.");
        }

        var variant = StartupThemeVariant;
        OnLoop(() =>
        {
            ThemeManager.SetTheme(startOn);
            if (!variant.IsUnspecified && !ThemeManager.SetVariant(variant))
            {
                throw new InvalidOperationException(
                    $"The theme '{startOn.Name}' has no variant '{variant}'. Its variants are: {string.Join(", ", startOn.VariantsByKey.Keys)}" +
                    (variant.FollowsSystem ? "; System needs a light and a dark one." : "."));
            }
        });
    }

    private void SubscribeToEvents()
    {
        EventAggregator.GetEvent<WindowCreatedEvent>().Subscribe(OnWindowCreated);
        EventAggregator.GetEvent<WindowClosedEvent>().Subscribe(OnWindowClosed);
        EventAggregator.GetEvent<WindowActivatedEvent>().Subscribe(OnWindowActivated);
        EventAggregator.GetEvent<WindowDeactivatedEvent>().Subscribe(OnWindowDeactivated);
    }

    private void RegisterBasicServices(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterInstance<IAdamantiumApplication>(this);
        containerRegistry.RegisterInstance<IUIApplication>(this);
        containerRegistry.RegisterInstance<EntityWorld>(EntityWorld);
        containerRegistry.RegisterSingleton<IThemeManager>(ThemeManager);
    }

    protected virtual void RegisterServices(IContainerRegistry containerRegistry)
    {
        // Off-screen renderer for drag ghosts and brush bakes; an app may register its own before base.
        if (!containerRegistry.IsRegistered<IVisualRenderer>())
            containerRegistry.RegisterSingleton<IVisualRenderer, VisualRenderer>();

        // Where windows' places are kept between runs; an application registers its own store to keep them elsewhere.
        if (!containerRegistry.IsRegistered<IWindowPlacementStore>())
            containerRegistry.RegisterInstance<IWindowPlacementStore>(new FileWindowPlacementStore());

        RegisterNavigationServices(containerRegistry);
    }

    // Each default is guarded, so an app that registers its own before calling base wins.
    private void RegisterNavigationServices(IContainerRegistry containerRegistry)
    {
        if (!containerRegistry.IsRegistered<IDependencyResolver>())
            containerRegistry.RegisterInstance<IDependencyResolver>(Container);
        if (!containerRegistry.IsRegistered<IDependencyContainer>())
        {
            containerRegistry.RegisterInstance<IDependencyContainer>(Container);
        }
        if (!containerRegistry.IsRegistered<IViewLocator>())
            containerRegistry.RegisterSingleton<IViewLocator, ViewLocator>();
        if (!containerRegistry.IsRegistered<IWindowShellRegistry>())
            containerRegistry.RegisterSingleton<IWindowShellRegistry, WindowShellRegistry>();
        if (!containerRegistry.IsRegistered<Adamantium.Navigation.IRegionManager>())
            containerRegistry.RegisterSingleton<Adamantium.Navigation.IRegionManager, Adamantium.Navigation.RegionManager>();
        if (!containerRegistry.IsRegistered<Adamantium.Navigation.IWindowNavigationBackend>())
            containerRegistry.RegisterSingleton<Adamantium.Navigation.IWindowNavigationBackend, WindowNavigationBackend>();
        if (!containerRegistry.IsRegistered<Adamantium.Navigation.INavigationService>())
            containerRegistry.RegisterSingleton<Adamantium.Navigation.INavigationService, Adamantium.Navigation.NavigationService>();

        if (!containerRegistry.IsRegistered<RegionAdapterMappings>())
        {
            var viewLocator = Container.Resolve<IViewLocator>();
            var mappings = new RegionAdapterMappings();
            mappings.Register<ContentControl>(new ContentControlRegionAdapter(viewLocator));
            mappings.Register<Selector>(new SelectorRegionAdapter(viewLocator));
            mappings.Register<ItemsControl>(new ItemsControlRegionAdapter(viewLocator));
            // For the area itself: a PaneGroup is covered as a Selector, and an area adds the choice of place.
            mappings.Register<DockingArea>(new DockingAreaRegionAdapter(viewLocator));
            containerRegistry.RegisterInstance<RegionAdapterMappings>(mappings);
        }

        // Dialog hosts: inside the window by default, or in a window of their own.
        if (!containerRegistry.IsRegistered<Adamantium.Navigation.IDialogHostRegistry>())
        {
            var dialogHosts = new Adamantium.Navigation.DialogHostRegistry();
            var overlayHost = new OverlayDialogHost(this, Container.Resolve<IViewLocator>());
            dialogHosts.Register(overlayHost.Kind, overlayHost);
            var windowHost = new WindowDialogHost(this, Container.Resolve<IViewLocator>(), Container.Resolve<IWindowShellRegistry>());
            dialogHosts.Register(windowHost.Kind, windowHost);
            containerRegistry.RegisterInstance<Adamantium.Navigation.IDialogHostRegistry>(dialogHosts);
        }
        if (!containerRegistry.IsRegistered<Adamantium.Navigation.IDialogService>())
            containerRegistry.RegisterSingleton<Adamantium.Navigation.IDialogService, Adamantium.Navigation.DialogService>();

        // Overlays (floating in-window OverlayWindows) - a service of their own, distinct from dialogs.
        if (!containerRegistry.IsRegistered<Adamantium.Navigation.IOverlayService>())
        {
            var overlayService = new OverlayService(this, Container.Resolve<IViewLocator>(),
                Container.Resolve<Adamantium.Core.DependencyInjection.IDependencyResolver>());
            containerRegistry.RegisterInstance<Adamantium.Navigation.IOverlayService>(overlayService);
        }

        Navigation = Container.Resolve<Adamantium.Navigation.INavigationService>();
    }
    
    protected virtual void OnWindowCreated(IWindow wnd)
    {
        var t = Stopwatch.StartNew();
        OnWindowAdded(wnd);
        t.Stop();
    }

    protected virtual void OnWindowClosed(IWindow wnd)
    {
        OnWindowRemoved(wnd);
    }

    protected void OnWindowActivated(IWindow obj)
    {
        SetActiveWindow(obj);
    }
    
    protected void OnWindowDeactivated(IWindow obj)
    {
        if (ActiveWindow == obj) ActiveWindow = null;
    }

    public virtual void Run()
    {
        if (IsRunning) return;

        try
        {
            Initialize();
            StartupCancellation.ThrowIfCancellationRequested();
            OnStartupInternal();
        }
        catch (OperationCanceledException) when (StartupCancellation.IsCancellationRequested || _isShuttingDown)
        {
        }
        catch
        {
            if (_loopStarted)
            {
                ShutDown();
            }
            else
            {
                _splash?.Abandon();
            }

            throw;
        }

        _startupDone = true;
        Interlocked.MemoryBarrier();
        if (StartupCancellation.IsCancellationRequested || _isShuttingDown)
        {
            ShutDown();
            return;
        }

        StartLoop();
        Dispatcher.Run(cancellationTokenSource.Token);
    }

    private void StartLoop()
    {
        if (_loopStarted) return;
        _loopStarted = true;

        if (RenderThreadOptions.RenderThreadEnabled)
        {
            renderThread = new Thread(RenderThread) { IsBackground = true, Name = "AdamantiumRenderThread" };
            // Published before it starts: BeginDraw uses it to refuse recording on the render thread.
            RenderThreadOptions.RenderThread = renderThread;
            renderThread.Start();
        }
        applicationLoopThread.Start();
    }

    private bool StartWithSplash()
    {
        if (SplashWindowType() is not { } type)
        {
            return false;
        }

        _splash = CreateSplash(type);
        _splash.Canceled += CancelStartup;
        _splash.Start();
        return true;
    }

    public void ShowSplash()
    {
        if (SplashWindowType() is { } type)
        {
            var splash = CreateSplash(type);
            if (MainWindow != null)
            {
                splash.KeepAbove(MainWindow);
            }

            splash.Start();
        }
    }

    private SplashHost CreateSplash(Type type) => new(this, type, StartupPlacement());

    private WindowPlacement StartupPlacement()
    {
        if (MainWindow is WindowBase { IsDisplayed: true } shown)
        {
            return shown.LivePlacement();
        }

        var startup = StartupType ?? GetType().Assembly.GetCustomAttribute<ApplicationBlueprintAttribute>()?.StartupWindow;
        var placementName = MainWindow is WindowBase main ? main.PlacementName() : startup?.FullName;
        if (placementName == null)
        {
            return null;
        }

        var placements = Container.IsRegistered<IWindowPlacementStore>()
            ? Container.Resolve<IWindowPlacementStore>()
            : new FileWindowPlacementStore();
        return placements.Load(placementName);
    }

    private Type SplashWindowType()
    {
        var type = SplashType ?? GetType().Assembly.GetCustomAttribute<ApplicationBlueprintAttribute>()?.SplashWindow;
        return Configuration.Platform is Platform.Linux or Platform.Windows ? type : null;
    }

    private void CancelStartup()
    {
        _startupCancellation.Cancel();
        if (_startupDone)
        {
            ShutDown();
        }
    }

    /// <summary>Keeps the splash window up - past the first frame of the startup window, or while the start opens its
    /// first window later, from work of its own - until what is returned is disposed.</summary>
    public IDisposable HoldSplash()
    {
        Interlocked.Increment(ref _splashHolds);
        return new SplashHold(this);
    }

    private sealed class SplashHold(UIApplication application) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                Interlocked.Decrement(ref application._splashHolds);
                LoopSignal.Request();
            }
        }
    }

    private void HandOverFromSplash()
    {
        if (_splash is not { IsHandedOver: false } || Volatile.Read(ref _splashHolds) > 0) return;

        var awaited = !_startupDone || _showingStartupWindow || WindowsArriving();
        foreach (var window in Windows)
        {
            if (window == _splash.Window)
            {
                continue;
            }

            if (window.IsDisplayed && window.Renderer is { PresentedFrames: > 0 })
            {
                _splash.HandOver();
                return;
            }

            awaited |= window.ShouldDisplayWindow && window.State != WindowState.Minimized;
        }

        if (!awaited)
        {
            _splash.HandOver();
        }
    }

    private bool WindowsArriving()
    {
        lock (applicationLocker)
        {
            foreach (var window in addedWindows)
            {
                if (window != _splash.Window)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public void Run(IWindow window)
    {
        if (IsRunning) return;
        
        MainWindow = window ?? throw new ArgumentNullException($"{nameof(window)}");

        Run();
    }

    public void Run(object context)
    {
        if (context is IWindow wnd)
        {
            Run(wnd);
        }
        else
        {
            throw new ArgumentException($"{nameof(context)} should be of type IWindow, but currently it is of type {context.GetType()}");
        }
    }

    public void RunOnce(AppTime time)
    {
        
    }

    private void OnStartupInternal()
    {
        Started?.Invoke(this, EventArgs.Empty);
        OnStartup();
    }

    protected virtual void OnStartup()
    {
        if (MainWindow == null && StartupType != null && typeof(IWindow).IsAssignableFrom(StartupType))
        {
            var window = (IWindow)Activator.CreateInstance(StartupType);
            if (window == null) 
                return;
            
            MainWindow = window;
            MainWindow.AttachContextAndInitialize(UIContext);
            ShowMainWindow(window);
        }
    }

    private async void ShowMainWindow(IWindow window)
    {
        _showingStartupWindow = true;
        try
        {
            await Navigation.ArriveInWindowAsync(window.DataContext);
            window.Show();
        }
        finally
        {
            _showingStartupWindow = false;
        }
    }

    private void ApplicationLoopThread()
    {
        Dispatcher.UIThread = Thread.CurrentThread;
        LoopSignal.Claim();

        while (!cancellationTokenSource.IsCancellationRequested)
        {
            try
            {
                WaitForWork();

                // Windows join here, on the loop thread, before anything walks them.
                ProcessPendingWindows();

                var frameTime = preciseTimer.GetElapsedTime();
                if (IsFixedTimeStep)
                {
                    accumulatedFrameTime += frameTime;

                    if (accumulatedFrameTime >= TimeStep)
                    {
                        Update(appTime);
                        _requestsAtRecord = LoopSignal.Requests;
                        _loadsAtRecord = BitmapImage.LoadsFinished;
                        RecordIfOwed(appTime);
                        UpdateAppTime(accumulatedFrameTime);
                        accumulatedFrameTime = 0;
                    }
                }
                else
                {
                    Update(appTime);
                    _requestsAtRecord = LoopSignal.Requests;
                    _loadsAtRecord = BitmapImage.LoadsFinished;
                    RecordIfOwed(appTime);
                    UpdateAppTime(frameTime);
                }

                OnCycleFinishedInternal();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                UnhandledException?.Invoke(this, new UnhandledExceptionEventArgs(ex));
            }
        }
    }

    private void RecordIfOwed(AppTime time)
    {
        var requests = LoopSignal.FrameRequests;
        if (!FrameOwed(requests))
        {
            return;
        }

        RecordRenderFrame();
        DispatchRenderFrame(time);
        if (_recordedThisFrame)
        {
            _frameRequestsRecorded = requests;
        }
    }

    private bool FrameOwed(long requests)
    {
        if (!(RenderThreadOptions.RenderThreadEnabled && renderThread != null))
        {
            return true;
        }

        if (RenderDirty.AnyHasWork || AnimationManager.IsSettling || Compositor.NeedsRecord
            || requests != _frameRequestsRecorded)
        {
            return true;
        }

        lock (_minimizedWindows)
        {
            foreach (var service in Volatile.Read(ref _windowServices))
            {
                if (!service.Hidden && service.Window.State != WindowState.Minimized
                    && _minimizedWindows.Contains(service.Window))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void SnapshotWindowServices()
    {
        var services = new WindowRenderService[windowToSystem.Count];
        windowToSystem.Values.CopyTo(services, 0);
        Volatile.Write(ref _windowServices, services);
    }

    /// <summary>A window's swapchain lags the size of its surface, so the window is being resized right now. Set by the
    /// render thread's every pass over the windows.</summary>
    public static bool SwapchainTrailing { get; set; }

    private void WaitForWork()
    {
        LoopRate = ResolveLoopRate();

        var period = (long)Math.Round(Stopwatch.Frequency / LoopRate);
        var due = _loopFrameDue + period;
        if (!SwapchainTrailing)
        {
            var remaining = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), due).TotalMilliseconds;
            if (remaining >= 1.0)
            {
                LoopSignal.Pause(remaining, cancellationTokenSource.Token);
            }
        }

        if (!RenderDirty.AnyHasWork && !AnimationManager.HasActiveAnimations)
        {
            LoopSignal.Wait(IdleWakeMs, cancellationTokenSource.Token);
        }

        var start = Stopwatch.GetTimestamp();
        _loopFrameDue = Math.Abs(start - due) < period ? due : start;
    }

    private double ResolveLoopRate()
    {
        var ceiling = (double)Math.Max(1u, UpdateRate);
        var fastest = 0.0;
        var unlimited = false;
        foreach (var service in Volatile.Read(ref _windowServices))
        {
            var rate = service.ResolveRefreshRates(this);
            if (service.Window.State == WindowState.Minimized || service.Hidden)
            {
                continue;
            }

            unlimited |= rate <= 0;
            fastest = Math.Max(fastest, rate);
        }

        if (unlimited || fastest <= 0)
        {
            return ceiling;
        }

        return Math.Min(ceiling, Math.Max(fastest, Math.Min(LowestLoopRate, ceiling)));
    }

    private void OnCycleFinishedInternal()
    {
        Compositor.StartPending();
        Compositor.EndFrame();

        // Snapshots are recorded here, on the loop thread, once the frame has settled; they are drawn on the render thread.
        _visualRenderer?.RecordPendingSnapshots();

        HandOverFromSplash();
        CheckExitConditions();
        if (GraphicsDeviceService.DeviceUpdateNeeded)
        {
            RecreateDevicesAndServices();
        }
        CycleFinished?.Invoke(this, EventArgs.Empty);
        NoteIdle();
    }

    /// <summary>What kept the application from going idle the last time it was waited for; null once it was idle.</summary>
    public string IdleBlocker { get; private set; }

    /// <summary>Completes once the application is idle: the loop has nothing left to do - no posted work, nothing to lay
    /// out or draw, no animation that finishes on its own, no picture still being read from its file - and the frame
    /// showing that state has been drawn. Endless
    /// animations (a caret, a spinner) do not count, nor does work running off the loop until it posts its result.</summary>
    public Task WaitForIdleAsync()
    {
        var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_idleWaiters)
        {
            _idleWaiters.Add(idle);
            _idleFrame = -1;
        }

        LoopSignal.Request();
        return idle.Task;
    }

    private void NoteIdle()
    {
        lock (_idleWaiters)
        {
            if (_idleWaiters.Count == 0)
            {
                return;
            }

            IdleBlocker = FindIdleBlocker();
            if (IdleBlocker != null)
            {
                _idleFrame = -1;
                return;
            }

            if (_idleFrame < 0)
            {
                _idleFrame = Volatile.Read(ref _publishedFrame);
            }

            IdleBlocker = _drawFailing
                ? "frames that fail to draw - see the error journal"
                : "the frame on its way to the screen";
        }

        ReleaseIdleWaiters();
    }

    private string FindIdleBlocker()
    {
        if (IsStarting)
        {
            return "the start, until a window of the application's own has drawn and the splash window has closed";
        }

        if (LoopSignal.HasPostedWork)
        {
            return "work posted to the loop";
        }

        if (Dispatcher is Threading.Dispatcher { HasPendingOperations: true })
        {
            return "work invoked on the dispatcher - a window closing, say";
        }

        if (LoopSignal.Requests != _requestsAtRecord)
        {
            return "a request that came after the frame was recorded";
        }

        if (RenderDirty.AnyHasWork)
        {
            return _unrecordedBecause ?? "a change not recorded yet";
        }

        if (BitmapImage.LoadsInFlight > 0)
        {
            return $"pictures still loading ({BitmapImage.LoadsInFlight})";
        }

        if (BitmapImage.LoadsFinished != _loadsAtRecord)
        {
            return "a picture that arrived after the frame was recorded";
        }

        foreach (var window in Windows)
        {
            var layout = LayoutManager.GetOrCreate(window);
            if (!layout.IsSettled)
            {
                var queued = layout.QueuedCounts();
                return $"layout still to do (style {queued.Style}, measure {queued.Measure}, arrange {queued.Arrange}, " +
                       $"next pass {queued.NextPass})";
            }
        }

        if (!BindingUpdateQueue.IsEmpty)
        {
            return "bindings still to push";
        }

        if (AnimationManager.IsSettling)
        {
            return "a running animation";
        }

        if (Graphics.Fonts.FontAtlasStore.HasPendingGlyphs)
        {
            return "glyphs still being made";
        }

        return null;
    }

    private void ReleaseIdleWaiters()
    {
        TaskCompletionSource[] released;
        lock (_idleWaiters)
        {
            if (_idleFrame < 0 || Volatile.Read(ref _presentedFrame) < _idleFrame)
            {
                return;
            }

            released = [.. _idleWaiters];
            _idleWaiters.Clear();
            _idleFrame = -1;
            IdleBlocker = null;
        }

        foreach (var waiter in released)
        {
            waiter.TrySetResult();
        }
    }

    // Records every window's render packet after the whole update and before any draw. The dirty set is cleared once,
    // after all windows, so the second window still sees all of it.
    private void RecordRenderFrame()
    {
        // An animation started in this frame's layout captured where its element stood before it was arranged.
        Compositor.RefreshBases();
        _recordedThisFrame = false;
        if (RenderThreadOptions.SingleThreaded || DisableRendering)
        {
            return;
        }

        var threaded = RenderThreadOptions.RenderThreadEnabled && renderThread != null;

        _unrecordedBecause = null;

        // The render is MaxFramesInFlight frames behind: skip the record, not the update. The marks stay for the next one.
        if (threaded && Volatile.Read(ref _framesInFlight) >= MaxFramesInFlight)
        {
            _unrecordedBecause = "the render thread is frames behind";
            Volatile.Write(ref _recordSkipped, true);
            return;
        }

        // Cleared only if every window recorded: one without a renderer yet records nothing, and its marks must stay.
        // A snapshot, since recording may open windows; not under _renderGate, which would lock the loop to the render thread.
        var recordedAll = true;
        var services = Volatile.Read(ref _windowServices);

        foreach (var service in services)
        {
            bool restored;
            lock (_minimizedWindows)
            {
                if (service.Window.State == WindowState.Minimized || service.Hidden)
                {
                    _minimizedWindows.Add(service.Window);
                    continue;
                }

                restored = _minimizedWindows.Remove(service.Window);
            }

            if (restored)
            {
                service.Window.InvalidateRender(true);
            }

            if (!service.RecordFrame())
            {
                recordedAll = false;
                _unrecordedBecause = $"\"{service.Window.Title}\" could not record its frame";
            }
        }

        _recordedThisFrame = recordedAll;

        // Cleared here, on the loop thread: the render thread must never touch RenderDirty.
        if (threaded && recordedAll) RenderDirty.Clear();
    }

    // Returns whether the frame drew. Never clears RenderDirty: this may run on the render thread.
    private bool ExecuteDrawSequence(AppTime appTime)
    {
        if (DisableRendering) return false;

        var drew = BeginScene();
        if (drew)
        {
            var windowsDrew = false;
            try
            {
                windowsDrew = Draw(appTime);
                OnBeforeEndScene();
            }
            finally
            {
                EndScene();

                if (windowsDrew)
                {
                    RuntimeStats.PresentedFrames++;
                }
            }
        }

        // Snapshots draw on whichever thread owns the device this frame, so it is never touched by two at once.
        _visualRenderer?.DrawPendingSnapshots();

        return drew;
    }

    // Draws inline, or, with a render thread, only publishes the recorded frame and returns without waiting.
    private void DispatchRenderFrame(AppTime appTime)
    {
        var threaded = RenderThreadOptions.RenderThreadEnabled && renderThread != null;

        // Inline: cleared once, after every window has drawn, not per window.
        if (!threaded)
        {
            var drewInline = ExecuteDrawSequence(appTime);
            if (drewInline)
            {
                RenderDirty.Clear();
                Compositor.FrameRecorded();
            }

            return;
        }

        // Threaded: the loop never draws. The device belongs to the render thread, and each packet carries its own projection.
        if (!_recordedThisFrame) return;
        Compositor.FrameRecorded();
        _renderAppTime = appTime;
        Interlocked.Increment(ref _publishedFrame);
        Interlocked.Increment(ref _framesInFlight);
        RenderSignal.Wake();
    }

    private void CountWindowFrame()
    {
        _countedFrames++;
        var elapsed = Stopwatch.GetElapsedTime(_countingSince).TotalSeconds;
        if (elapsed < 1) return;

        RuntimeStats.WindowFps = _countedFrames / elapsed;
        _countedFrames = 0;
        _countingSince = Stopwatch.GetTimestamp();
    }

    private void RenderThread()
    {
        RenderSignal.Attach();
        try
        {
            RunRenderThread();
        }
        finally
        {
            RenderSignal.Detach();
        }
    }

    private void RunRenderThread()
    {
        while (!cancellationTokenSource.IsCancellationRequested)
        {
            long now;
            long due;

            lock (_renderGate)
            {
                if (cancellationTokenSource.IsCancellationRequested)
                {
                    break;
                }

                now = Stopwatch.GetTimestamp();
                try
                {
                    var published = Volatile.Read(ref _publishedFrame);
                    due = ClaimFrames(now, out var claims);
                    var unshown = published != Volatile.Read(ref _presentedFrame) && !claims.LeftWaiting
                                  && (!_withheldSinceShown || !claims.AnyDrawable);
                    if (due <= now || unshown || _visualRenderer?.HasPendingDraws == true)
                    {
                        DrawClaimedFrames(published, claims);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    ReportFailedFrame(ex);
                    due = FramePacer.Never;
                }
            }

            var wait = due == FramePacer.Never
                ? IdleWakeMs
                : Math.Clamp(Stopwatch.GetElapsedTime(now, due).TotalMilliseconds, 1, IdleWakeMs);
            RenderSignal.Wait(TimeSpan.FromMilliseconds(wait), cancellationTokenSource.Token);
        }
    }

    private long ClaimFrames(long now, out Claims claims)
    {
        var next = FramePacer.Never;
        bool drawn = false, drawable = false, waiting = false, trailing = false;
        foreach (var service in windowToSystem.Values)
        {
            next = Math.Min(next, service.ClaimFrame(now));
            drawn |= service.DrawsThisFrame;
            drawable |= service.CanDraw;
            waiting |= service.CanDraw && service.HasRecordedChange && !service.DrawsThisFrame;
            trailing |= service.IsTrailing;
        }

        SwapchainTrailing = trailing;
        claims = new Claims(drawn, drawable, waiting);
        return next;
    }

    private void DrawClaimedFrames(long drawing, Claims claims)
    {
        Interlocked.Exchange(ref _framesInFlight, 0);
        if (Volatile.Read(ref _recordSkipped))
        {
            Volatile.Write(ref _recordSkipped, false);
            LoopSignal.Request();
        }

        var withheld = Rendering.RenderCache.WithheldFrames;
        ExecuteDrawSequence(_renderAppTime);
        if (claims.AnyDrawn)
        {
            CountWindowFrame();
        }

        var heldBack = Rendering.RenderCache.WithheldFrames != withheld;
        if (heldBack)
        {
            _withheldSinceShown = true;
        }
        else if (claims.AnyDrawn)
        {
            _withheldSinceShown = false;
        }

        if (!heldBack && !claims.LeftWaiting && (!_withheldSinceShown || !claims.AnyDrawable))
        {
            Volatile.Write(ref _presentedFrame, drawing);
        }

        _drawFailing = false;
        ReleaseIdleWaiters();
    }

    private void ReportFailedFrame(Exception exception)
    {
        Console.WriteLine(exception);
        if (!_drawFailing)
        {
            _drawFailing = true;
            Serilog.Log.Logger.Error(exception, "A frame failed to draw; frames are not reaching the screen");
        }

        UnhandledException?.Invoke(this, new UnhandledExceptionEventArgs(exception));
    }

    // Block the calling thread until an item arrives (or shutdown cancels the token, which throws OperationCanceledException).
    private T BlockingRead<T>(ChannelReader<T> reader)
    {
        var pending = reader.ReadAsync(cancellationTokenSource.Token);
        return pending.IsCompletedSuccessfully ? pending.Result : pending.AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Updates the time for each frame
    /// </summary>
    /// <param name="elapsed">elapsed time from the last frame</param>
    protected void UpdateAppTime(double elapsed)
    {
        TimeSpan frameTimeSpan = TimeSpan.FromSeconds(appTime.FrameTime);
        if (!IsPaused)
        {
            totalTime += frameTimeSpan;
        }
        if (appTime.FramesCount > 0 && appTime.FramesCount - 1 < UInt64.MaxValue)
        {
            appTime.FramesCount++;
        }
        else
        {
            appTime.FramesCount = 1;
        }

        appTime.FrameTime = elapsed;
        appTime.TotalTime = totalTime;
        CalculateFps(ref appTime);
    }

    /// <summary>
    /// Calculates FPS count
    /// </summary>
    private void CalculateFps(ref AppTime appTime)
    {
        fpsCounter++;
        fpsTime += appTime.FrameTime;
        if (fpsTime >= 1.0d)
        {
            appTime.Fps = (fpsCounter) / (Single)fpsTime;
            fpsCounter = 0;
            fpsTime = 0;
        }
    }

    // Emptied into a local under the lock: registering runs application code, which may open another window.
    private void ProcessPendingWindows()
    {
        IWindow[] arrived;
        lock (applicationLocker)
        {
            if (addedWindows.Count == 0) return;

            arrived = addedWindows.ToArray();
            addedWindows.Clear();
            _arrivingWindows = arrived;
        }

        try
        {
            for (var i = 0; i < arrived.Length; i++)
            {
                var window = arrived[i];
                lock (applicationLocker)
                {
                    _arrivingAt = i;
                    if (_closedOnArrival.Remove(window)) continue;
                }

                OnWindowAdded(window);
                bool closed;
                lock (applicationLocker)
                {
                    closed = _closedOnArrival.Remove(window);
                }

                if (closed) OnWindowRemoved(window);
            }
        }
        finally
        {
            lock (applicationLocker)
            {
                _arrivingWindows = [];
                _arrivingAt = 0;
                _closedOnArrival.Clear();
            }
        }
    }

    protected void CheckExitConditions()
    {
        // Not before the first window, or the application closes before it opens.
        if (ShutDownMode != ShutDownMode.OnExplicitShutDown && !firstWindowAdded) return;

        switch (ShutDownMode)
        {
            case ShutDownMode.OnMainWindowClosed when MainWindow == null:
            case ShutDownMode.OnLastWindowClosed when Windows.Count == 0:
                ShutDown();
                break;
        }
    }

    protected virtual bool BeginScene()
    {
        return GraphicsDeviceService.IsReady;
    }

    protected void Update(AppTime frameTime)
    {
        // Input first, before anything touches the visual tree.
        Threading.Dispatcher.CurrentDispatcher?.DrainPending();
        // A stall moves animations by a sliver only; otherwise they burn their duration unseen and appear to jump.
        var animationStep = frameTime.FrameTime < StallFrameSeconds ? frameTime.FrameTime : StallAnimationStep;
        AnimationManager.Tick(animationStep);

        EntityWorld.ServiceManager.Update(frameTime);

        // Teardowns are paid here, a bounded number per frame and never on a stall, not in the frame that caused them.
        if (frameTime.FrameTime < StallFrameSeconds) DiscardedVisuals.Drain(DiscardReleasesPerFrame);
    }

    /// <summary>Draws every window due a frame; true when one did.</summary>
    protected bool Draw(AppTime frameTime)
    {
        return EntityWorld.ServiceManager.Draw(frameTime);
    }

    protected virtual void OnBeforeEndScene()
    {
        
    }

    protected void EndScene()
    {
        GraphicsDeviceService.RaiseFrameFinished();
        EntityWorld.ServiceManager.Present();
    }

    public void ShutDown()
    {
        // May come from any thread, even twice.
        if (_isShuttingDown) return;
        _isShuttingDown = true;

        DetachDebugTools();

        // Cancel so no frame starts, then take the gate to wait out the one in flight; the render thread then exits by itself.
        cancellationTokenSource.Cancel();
        if (applicationLoopThread is { IsAlive: true } loop && loop != Thread.CurrentThread
            && Thread.CurrentThread != renderThread && !Monitor.IsEntered(_renderGate))
        {
            loop.Join(LoopExitWaitMs);
        }

        lock (_renderGate)
        {
            // Under the gate: its handlers unload the services, which a frame or a closing window's teardown may be using.
            ShuttingDown?.Invoke(this, EventArgs.Empty);
            ContentUnloading?.Invoke(this, EventArgs.Empty);
            // Windows may still be open under OnExplicitShutDown; their services go before the device.
            foreach (var service in _windowServices)
            {
                service.UnloadContent();
            }
            windowToSystem.Clear();
            SnapshotWindowServices();
            FreeResources();
        }

        _splash?.Abandon();
        Stopped?.Invoke(this, EventArgs.Empty);
    }

    private void DetachDebugTools()
    {
        List<Diagnostics.IDebugTool> attached = [];
        lock (_debugToolsLock)
        {
            foreach (var (tool, attaching) in _debugTools)
            {
                if (!attaching)
                {
                    attached.Add(tool);
                }
            }

            foreach (var tool in attached)
            {
                _debugTools.Remove(tool);
            }
        }

        foreach (var tool in attached)
        {
            DetachLogged(tool);
        }
    }

    private static void DetachLogged(Diagnostics.IDebugTool tool)
    {
        try
        {
            tool.Detach();
        }
        catch (Exception fault)
        {
            Log.Logger.Warning(fault, "Debug tool {Tool} failed to detach", tool.GetType().FullName);
        }
    }

    private void FreeResources()
    {
        if (GraphicsDeviceService is IDisposable disposableDevice)
        {
            disposableDevice?.Dispose();
        }
    }

    /// <summary>
    /// Calling this method will pause running service
    /// </summary>
    public void Pause()
    {
        if (!IsPaused)
        {
            IsPaused = true;
            Paused?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Calling this method will resume running service
    /// </summary>
    public void Resume()
    {
        if (IsPaused)
        {
            IsPaused = false;
            Resumed?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler<EventArgs> Started;
    public event EventHandler<EventArgs> ShuttingDown;
    public event EventHandler<EventArgs> Stopped;
    public event EventHandler Paused;
    public event EventHandler Resumed;
    public event EventHandler<EventArgs> ContentUnloading;
    public event EventHandler CycleFinished;
    public event UnhandledExceptionEventHandler UnhandledException;

    /// <summary>Never raised: a UI application loads content per window, not in one phase.</summary>
    public event EventHandler<EventArgs> ContentLoading
    {
        add { }
        remove { }
    }
}