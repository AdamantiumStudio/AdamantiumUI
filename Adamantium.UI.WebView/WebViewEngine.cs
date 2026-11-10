using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.UI.Core;
using Adamantium.UI.WebView.Cef;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView;

/// <summary>
/// CEF for the application, one for the process as CEF is: initialized once with every setting at the first
/// <see cref="WebView"/>, its work run on the UI thread when CEF asks for it, shut down on it as the application
/// stops, its browsers closed first. Registered in the application's container
/// (<see cref="WebViewRegistration.AddWebView"/>); a view model takes it from there and a WebView's
/// <see cref="WebView.Engine"/> is bound to it.
/// </summary>
public sealed unsafe class WebViewEngine : IDisposable
{
    private const long MaxPumpDelayMs = 1000 / 30;
    private const int CloseTurns = 100;
    private const int CloseTurnMs = 10;

    private readonly IGraphicsDeviceService devices;
    private readonly IUIApplication application;
    private readonly WebViewOptions options;
    private readonly Timer pumpTimer;
    private readonly List<WebViewBrowser> browsers = [];
    private readonly WebViewSitePermissions sitePermissions;
    private WebViewBrowserProcessHandler browserProcess;
    private WebViewApp app;
    private CefMainArguments mainArguments;
    private IGraphicsDevice device;
    private int pumpPosted;
    private long timerDue;
    private long? adapterLuid;
    private bool sharedTextureRefused;
    private bool initialized;
    private bool failed;
    private volatile bool shutDown;

    public WebViewEngine(IGraphicsDeviceService devices, IUIApplication application, WebViewOptions options)
    {
        this.devices = devices;
        this.application = application;
        this.options = options ?? new WebViewOptions();
        sitePermissions = new WebViewSitePermissions(this.options.ProfileFolder ?? DefaultProfileFolder());
        pumpTimer = new Timer(_ => PostPumpWork());
        if (application != null)
        {
            application.UIThreadExiting += OnUIThreadExiting;
        }

        devices.DeviceChangeBegin += OnDeviceChanging;
        devices.DeviceChangeEnd += OnDeviceChanged;
    }

    /// <summary>The answers sites were given - what each may use, or may not - kept in the profile, so a site is not
    /// asked again; in the order of the sites.</summary>
    public IReadOnlyList<WebSitePermission> SitePermissions => sitePermissions.All;

    /// <summary><see cref="SitePermissions"/> changed: an answer kept, or taken back.</summary>
    public event EventHandler SitePermissionsChanged;

    /// <summary>Takes back what <paramref name="site"/> was answered for <paramref name="permission"/>: it is asked
    /// again the next time it wants it.</summary>
    public void ForgetSitePermission(string site, WebPermission permission)
    {
        if (!sitePermissions.Forget(site, permission))
        {
            return;
        }

        SetChromiumSetting(site, permission, CefContentSettingValues.Default);
        ClearSiteEmbargo(site);
        SitePermissionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Clears what Chromium counted against <paramref name="site"/> - prompts let go unanswered - after which it
    /// would block the site for days unasked.</summary>
    internal void ClearSiteEmbargo(string site)
    {
        if (!initialized || shutDown)
        {
            return;
        }

        var context = CefInterop.CefRequestContextGetGlobalContext();
        if (context == null)
        {
            return;
        }

        var address = new CefStringUtf16();
        try
        {
            CefStrings.Set(&address, site);
            context->set_website_setting(context, &address, &address, CefContentSettingTypes.PermissionAutoblockerData, null);
        }
        finally
        {
            CefStrings.Clear(&address);
            CefRefCounting.ReleaseCef(context);
        }
    }

    /// <summary>What <paramref name="site"/> was answered for all of <paramref name="permissions"/>: true when each was
    /// allowed, false when any was blocked, null when one was never answered.</summary>
    internal bool? SitePermissionAnswer(string site, WebPermission permissions) => sitePermissions.Answer(site, permissions);

    /// <summary>Keeps the user's answer for the site, here and in Chromium's own settings - whose answer would otherwise
    /// outlive one taken back here.</summary>
    internal void KeepSitePermission(string site, WebPermission permissions, bool allowed)
    {
        sitePermissions.Keep(site, permissions, allowed);
        foreach (var permission in WebViewSitePermissions.Each(permissions & ~WebPermission.ScreenCapture))
        {
            SetChromiumSetting(site, permission, allowed ? CefContentSettingValues.Allow : CefContentSettingValues.Block);
        }

        SitePermissionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetChromiumSetting(string site, WebPermission permission, CefContentSettingValues value)
    {
        CefContentSettingTypes? type = permission switch
        {
            WebPermission.Camera => CefContentSettingTypes.MediastreamCamera,
            WebPermission.Microphone => CefContentSettingTypes.MediastreamMic,
            WebPermission.Geolocation => CefContentSettingTypes.Geolocation,
            WebPermission.Notifications => CefContentSettingTypes.Notifications,
            WebPermission.Clipboard => CefContentSettingTypes.ClipboardReadWrite,
            _ => null
        };
        if (type == null || !initialized || shutDown)
        {
            return;
        }

        var context = CefInterop.CefRequestContextGetGlobalContext();
        if (context == null)
        {
            return;
        }

        var address = new CefStringUtf16();
        try
        {
            CefStrings.Set(&address, site);
            context->set_content_setting(context, &address, &address, type.Value, value);
        }
        finally
        {
            CefStrings.Clear(&address);
            CefRefCounting.ReleaseCef(context);
        }
    }

    // The main device is made again - lost, most often: the frames and the device they were written on go while the
    // old main device is still there to let go of them. The pages live on, CEF knows nothing of it.
    private void OnDeviceChanging(object sender, EventArgs e)
    {
        foreach (var browser in browsers)
        {
            try
            {
                browser.LetGoOfDevice();
            }
            catch (Exception exception)
            {
                Serilog.Log.Logger.Warning(exception, "A WebView's frames did not go cleanly with the lost device");
            }
        }

        if (device == null)
        {
            return;
        }

        try
        {
            device.Dispose();
        }
        catch (Exception exception)
        {
            Serilog.Log.Logger.Warning(exception, "The WebView's device did not go cleanly with the lost main device");
        }

        device = null;
    }

    // A device on the new main device, the pages' frames written on it from now on, each page asked for a whole one; the
    // new device may take Chromium's textures where the old refused them. Nothing thrown here may stop the rest of the
    // application making itself again: a device not made now is made by the next browser.
    private void OnDeviceChanged(object sender, EventArgs e)
    {
        sharedTextureRefused = false;
        if (shutDown || browsers.Count == 0 || devices.MainGraphicsDevice is not { IsLost: false } main)
        {
            return;
        }

        try
        {
            device = main.CreateRenderDevice();
        }
        catch (Exception exception)
        {
            Serilog.Log.Logger.Warning(exception, "The WebView's device could not be made on the new main device");
            device = null;
            return;
        }

        foreach (var browser in browsers)
        {
            try
            {
                browser.TakeDevice(device);
            }
            catch (Exception exception)
            {
                Serilog.Log.Logger.Warning(exception, "A WebView did not take the new device");
            }
        }
    }

    /// <summary>Whether CEF is beside the application, where native/fetch-cef stages it, and has not refused to start.
    /// </summary>
    public bool IsAvailable => !failed && !shutDown && File.Exists(CefLibrary.LibraryFile(CefLibrary.ApplicationFolder));

    /// <summary>The helper CEF starts its processes on, beside CEF.</summary>
    public static string HelperFile => Path.Combine(CefLibrary.ApplicationFolder,
        OperatingSystem.IsWindows() ? "Adamantium.UI.WebView.Helper.exe" : "Adamantium.UI.WebView.Helper");

    /// <summary>Initializes CEF on the UI loop, once; throws when CEF or its helper is missing or refuses, and CEF is not
    /// tried again - it allows one initialization a process.</summary>
    internal void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        if (failed || shutDown)
        {
            throw new InvalidOperationException("CEF is shut down or refused to start.");
        }

        try
        {
            Initialize();
        }
        catch
        {
            failed = true;
            throw;
        }
    }

    /// <summary>A browser for one WebView, its frames written on the engine's device on the UI's main device: copied
    /// from Chromium's textures when <paramref name="allowSharedTexture"/> and the device takes them, else written from
    /// pixels in memory.</summary>
    internal WebViewBrowser CreateBrowser(bool allowSharedTexture, IWebViewBrowserView view)
    {
        EnsureInitialized();
        device ??= devices.MainGraphicsDevice.CreateRenderDevice();
        var path = allowSharedTexture && SharesTextures() ? WebViewFramePath.SharedTexture : WebViewFramePath.PixelBuffer;
        var browser = new WebViewBrowser(this, device, path, view);
        browsers.Add(browser);
        ArmPumpTimer(MaxPumpDelayMs);
        return browser;
    }

    /// <summary>A browser for a page another one opens, its frames coming as its opener's do: CEF makes it, from the
    /// opener's request. None while the device is lost and not made anew.</summary>
    internal WebViewBrowser CreatePopupBrowser(WebViewFramePath path, IWebViewBrowserView view)
    {
        if (device == null)
        {
            return null;
        }

        var browser = new WebViewBrowser(this, device, path, view);
        browsers.Add(browser);
        return browser;
    }

    /// <summary>A browser of <see cref="CreateBrowser"/> closed by CEF, or never made by it.</summary>
    internal void BrowserClosed(WebViewBrowser browser) => browsers.Remove(browser);

    /// <summary>A texture of Chromium's the device did not take: browsers made from now on get pixels in memory.</summary>
    internal void SharedTextureRefused() => sharedTextureRefused = true;

    private bool SharesTextures() =>
        !sharedTextureRefused && adapterLuid != null &&
        SharedSurface.CanCopyFrom(device, SharedHandleType.D3D11Texture, SurfaceFormat.B8G8R8A8.UNorm);

    /// <summary>Closes the browsers and CEF, running CEF's work until they have gone; on the UI loop, which CEF was
    /// started on.</summary>
    public void Dispose()
    {
        if (shutDown)
        {
            return;
        }

        shutDown = true;
        devices.DeviceChangeBegin -= OnDeviceChanging;
        devices.DeviceChangeEnd -= OnDeviceChanged;
        if (application != null)
        {
            application.UIThreadExiting -= OnUIThreadExiting;
        }

        if (initialized)
        {
            for (var i = browsers.Count - 1; i >= 0; i--)
            {
                browsers[i].Dispose();
            }

            for (var turn = 0; turn < CloseTurns && browsers.Count > 0; turn++)
            {
                CefInterop.CefDoMessageLoopWork();
                Thread.Sleep(CloseTurnMs);
            }

            CefInterop.CefShutdown();
            for (var i = browsers.Count - 1; i >= 0; i--)
            {
                browsers[i].Abandon();
            }

            browsers.Clear();
        }

        pumpTimer.Dispose();
        ReleaseHandlers();
        if (device != null)
        {
            device.AddToDeferDisposeQueue(device);
            device = null;
        }
    }

    private void Initialize()
    {
        if (!File.Exists(HelperFile))
        {
            throw new FileNotFoundException($"The WebView helper is not at {HelperFile}.", HelperFile);
        }

        CefLibrary.Load(CefLibrary.ApplicationFolder);
        browserProcess = new WebViewBrowserProcessHandler(SchedulePumpWork);
        app = new WebViewApp(browserProcess, Switches());
        mainArguments = new CefMainArguments(Environment.GetCommandLineArgs());

        var settings = new CefSettings
        {
            size = (ulong)sizeof(CefSettings),
            no_sandbox = 1,
            external_message_pump = 1,
            windowless_rendering_enabled = 1,
            log_severity = CefLogSeverity.Warning,
        };
        var profile = options.ProfileFolder ?? DefaultProfileFolder();
        CefStrings.Set(&settings.browser_subprocess_path, HelperFile);
        CefStrings.Set(&settings.resources_dir_path, CefLibrary.ApplicationFolder);
        CefStrings.Set(&settings.locales_dir_path, Path.Combine(CefLibrary.ApplicationFolder, "locales"));
        CefStrings.Set(&settings.root_cache_path, profile);
        CefStrings.Set(&settings.cache_path, profile);
        CefStrings.Set(&settings.locale, options.Locale);
        CefStrings.Set(&settings.log_file, Path.Combine(profile, "cef.log"));
        try
        {
            if (CefInterop.CefInitialize(mainArguments.Pointer, &settings, app.Shared, null) == 0)
            {
                throw new InvalidOperationException($"CEF refused to initialize (exit code {CefInterop.CefGetExitCode()}).");
            }
        }
        catch
        {
            ReleaseHandlers();
            throw;
        }
        finally
        {
            CefStrings.Clear(&settings.browser_subprocess_path);
            CefStrings.Clear(&settings.resources_dir_path);
            CefStrings.Clear(&settings.locales_dir_path);
            CefStrings.Clear(&settings.root_cache_path);
            CefStrings.Clear(&settings.cache_path);
            CefStrings.Clear(&settings.locale);
            CefStrings.Clear(&settings.log_file);
        }

        initialized = true;
    }

    private List<KeyValuePair<string, string>> Switches()
    {
        var switches = new List<KeyValuePair<string, string>>();
        adapterLuid = OperatingSystem.IsWindows() ? devices.MainGraphicsDevice.GraphicsAdapter.Luid : null;
        if (adapterLuid is { } luid)
        {
            switches.Add(new("use-adapter-luid", $"{(int)(luid >> 32)},{(uint)luid}"));
        }

        return switches;
    }

    private void ReleaseHandlers()
    {
        app?.Release();
        app = null;
        browserProcess?.Release();
        browserProcess = null;
        mainArguments?.Dispose();
        mainArguments = null;
    }

    private void OnUIThreadExiting(object sender, EventArgs e) => Dispose();

    private void SchedulePumpWork(long delayMs)
    {
        if (shutDown)
        {
            return;
        }

        if (delayMs <= 0)
        {
            PostPumpWork();
            return;
        }

        ArmPumpTimer(Math.Min(delayMs, MaxPumpDelayMs));
    }

    private void ArmPumpTimer(long delayMs)
    {
        Volatile.Write(ref timerDue, Environment.TickCount64 + delayMs);
        try
        {
            pumpTimer.Change(delayMs, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void PostPumpWork()
    {
        if (Interlocked.Exchange(ref pumpPosted, 1) == 0)
        {
            LoopSignal.Post(DoPumpWork);
        }
    }

    private void DoPumpWork()
    {
        Volatile.Write(ref pumpPosted, 0);
        if (!initialized || shutDown)
        {
            return;
        }

        CefInterop.CefDoMessageLoopWork();
        var now = Environment.TickCount64;
        foreach (var browser in browsers)
        {
            browser.RepaintIfOwed();
            browser.MeasureFrameRate(now);
            browser.FollowSecurity(now);
        }

        if (browsers.Count > 0 && Volatile.Read(ref timerDue) <= Environment.TickCount64)
        {
            ArmPumpTimer(MaxPumpDelayMs);
        }
    }

    private static string DefaultProfileFolder() => Path.Combine(ApplicationData.Folder, "WebView");
}
