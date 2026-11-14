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
public sealed unsafe partial class WebViewEngine : IDisposable
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
    private readonly Lock sitesLock = new();
    private readonly Dictionary<string, IWebSiteContent> sites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, WebDownload> downloadsById = [];
    private readonly HashSet<uint> downloadsGivenUp = [];
    private readonly Adamantium.Core.Collections.TrackingCollection<WebDownload> downloads = [];
    private readonly WebViewDownloadHistory downloadHistory;
    private WebViewBrowserProcessHandler browserProcess;
    private WebViewApp app;
    private WebSiteSchemeHandlerFactory siteFactory;
    private WebViewBrowser warmUp;
    private CefMainArguments mainArguments;
    private IGraphicsDevice device;
    private int pumpPosted;
    private int timerArmed;
    private long? adapterLuid;
    private bool sharedTextureRefused;
    private bool changingDevice;
    private bool initialized;
    private bool failed;
    private volatile bool shutDown;

    public WebViewEngine(IGraphicsDeviceService devices, IUIApplication application, WebViewOptions options)
    {
        this.devices = devices;
        this.application = application;
        this.options = options ?? new WebViewOptions();
        sitePermissions = new WebViewSitePermissions(this.options.ProfileFolder ?? DefaultProfileFolder());
        downloadHistory = new WebViewDownloadHistory(this.options.ProfileFolder ?? DefaultProfileFolder());
        foreach (var kept in downloadHistory.Load())
        {
            downloads.Add(WebDownload.Restore(kept, _ => KeepDownloads()));
        }

        pumpTimer = new Timer(_ => OnPumpTimer());
        if (application != null)
        {
            application.UIThreadExiting += OnUIThreadExiting;
        }

        devices.DeviceChangeBegin += OnDeviceChanging;
        devices.DeviceChangeEnd += OnDeviceChanged;
    }

    /// <summary>Serves <paramref name="content"/> as the site <c>app://<paramref name="host"/>/</c>, at an origin of
    /// its own - one an object of the application's can be let in for - with what a secure site has: fetch, modules,
    /// storage. A site added under the host before is replaced; every page of every view reaches it.</summary>
    public void AddSite(string host, IWebSiteContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrEmpty(host) || Uri.CheckHostName(host) != UriHostNameType.Dns)
        {
            throw new ArgumentException($"'{host}' is no host name.", nameof(host));
        }

        lock (sitesLock)
        {
            sites[ChromiumHost(host)] = content;
        }
    }

    // A host as Chromium asks for it: an international name in punycode.
    private static string ChromiumHost(string host) => new System.Globalization.IdnMapping().GetAscii(host);

    /// <summary>The site <c>app://<paramref name="host"/>/</c> is served no more; false for one not added.</summary>
    public bool RemoveSite(string host)
    {
        lock (sitesLock)
        {
            return !string.IsNullOrEmpty(host) && sites.Remove(ChromiumHost(host));
        }
    }

    // The site a request names, asked on CEF's IO thread.
    private IWebSiteContent Site(string host)
    {
        lock (sitesLock)
        {
            return sites.GetValueOrDefault(host);
        }
    }

    /// <summary>The downloads the application said where to save, in the order it did - those coming in and those
    /// finished, until forgotten (<see cref="ForgetDownload"/>). Kept in the profile: the next run lists them again,
    /// one that was coming in as interrupted (<see cref="WebDownloadInterruption.ApplicationClosed"/>). Changes are told
    /// as a collection's, on the UI thread.</summary>
    public IReadOnlyList<WebDownload> Downloads => downloads;

    /// <summary>Takes a download done with off <see cref="Downloads"/> - finished, or kept from an earlier run; its file
    /// stays where it is, the part of one an earlier run left unfinished is deleted. False for one not there, or still
    /// coming in or able to go on.</summary>
    public bool ForgetDownload(WebDownload download)
    {
        if (download is not ({ IsFinished: true } or { Ended: true }) || !downloads.Contains(download))
        {
            return false;
        }

        if (!download.IsFinished)
        {
            download.Cancel();
        }

        downloads.Remove(download);
        download.KeptAs = null;

        if (download.Ended)
        {
            if (downloadsById.TryGetValue(download.Id, out var known) && ReferenceEquals(known, download))
            {
                downloadsById.Remove(download.Id);
            }

            download.Origin = null;
        }

        KeepDownloads();
        return true;
    }

    /// <summary>The download Chromium numbers <paramref name="id"/>: made the first time it is heard of.</summary>
    internal WebDownload Download(uint id)
    {
        if (!downloadsById.TryGetValue(id, out var download))
        {
            download = new WebDownload(id, Listed, DownloadHeard, IsSavedAt);
            downloadsById[id] = download;
        }

        return download;
    }

    private void Listed(WebDownload download)
    {
        downloads.Add(download);
        KeepDownloads();
    }

    private void KeepDownloads()
    {
        // Closing, what Chromium says - cancelled, every one - is not what the next run is to list.
        if (!shutDown)
        {
            downloadHistory.Save(downloads);
        }
    }

    private bool IsSavedAt(string path)
    {
        // Linux's names differ by case; Windows' and macOS's do not.
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var full = Path.GetFullPath(path);
        foreach (var download in downloadsById.Values)
        {
            if (!download.IsFinished && download.FilePath.Length > 0 &&
                string.Equals(Path.GetFullPath(download.FilePath), full, comparison))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A download Chromium says no more of, not on <see cref="Downloads"/>, is not kept; the page closed while
    /// it came in goes once its last download is done. A listed one whose state or reason changed is written to the
    /// profile - closing, only one that completed, the rest kept as they were before Chromium cancelled them.</summary>
    internal void DownloadHeard(WebDownload download)
    {
        if (download.KeptDiffers && !shutDown)
        {
            KeepDownloads();
        }
        else if (download.KeptDiffers && download.State == WebDownloadState.Completed)
        {
            downloadHistory.Update(download);
        }
        else if (downloadHistory.LastWriteFailed && !shutDown)
        {
            KeepDownloads();
        }

        if (!download.IsFinished)
        {
            return;
        }

        if (download.IsGivenUp)
        {
            downloadsGivenUp.Add(download.Id);
        }

        if (!downloads.Contains(download))
        {
            downloadsById.Remove(download.Id);
        }

        var origin = download.Origin;
        if (download.Ended)
        {
            // Done with: it holds the page it came from no longer.
            download.Origin = null;
        }

        if (origin is { IsKeptForDownloads: true } && !HasDownloadsComingIn(origin))
        {
            // Out of Chromium's callback, which is telling of the download now.
            LoopSignal.Post(origin.CloseKept);
        }
    }

    /// <summary>Whether the download Chromium numbers <paramref name="id"/> was given up here, interrupted: Chromium keeps
    /// it to resume, and is answered no each time it tries.</summary>
    internal bool IsDownloadGivenUp(uint id) => downloadsGivenUp.Contains(id);

    /// <summary>The color scheme every page is asked in (<see cref="WebViewOptions.ColorScheme"/>).</summary>
    internal WebViewColorScheme ColorScheme => options.ColorScheme;

    /// <summary>Refuses the downloads <paramref name="browser"/> asked about and the application has not answered: the
    /// page goes, and an answer coming later would start a download nobody hears of.</summary>
    internal void RefuseAsked(WebViewBrowser browser)
    {
        List<WebDownload> refused = null;
        foreach (var download in downloadsById.Values)
        {
            if (ReferenceEquals(download.Origin, browser) && download.IsAsked)
            {
                (refused ??= []).Add(download);
            }
        }

        if (refused == null)
        {
            return;
        }

        foreach (var download in refused)
        {
            download.Refuse();
            DownloadHeard(download);
        }
    }

    /// <summary>Whether a download <paramref name="browser"/> started is still coming in - paused too.</summary>
    internal bool HasDownloadsComingIn(WebViewBrowser browser)
    {
        foreach (var download in downloadsById.Values)
        {
            if (ReferenceEquals(download.Origin, browser) && !download.IsFinished && !download.IsAsked)
            {
                return true;
            }
        }

        return false;
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
        changingDevice = true;
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
        changingDevice = false;
        sharedTextureRefused = false;
        if (!shutDown && browsers.Count > 0)
        {
            EnsureDevice();
        }
    }

    // The engine's device on the main one, made when there is none and the main device can make one - not while it is
    // being made again; the pages left without a device take it then. Null while none can be made.
    private IGraphicsDevice EnsureDevice()
    {
        if (device != null)
        {
            return device;
        }

        if (changingDevice || devices.MainGraphicsDevice is not { IsLost: false } main)
        {
            return null;
        }

        try
        {
            device = main.CreateRenderDevice();
        }
        catch (Exception exception)
        {
            Serilog.Log.Logger.Warning(exception, "The WebView's device could not be made on the main device");
            device = null;
            return null;
        }

        foreach (var browser in browsers)
        {
            if (!browser.IsDeviceLost)
            {
                continue;
            }

            try
            {
                browser.TakeDevice(device);
            }
            catch (Exception exception)
            {
                Serilog.Log.Logger.Warning(exception, "A WebView did not take the new device");
            }
        }

        return device;
    }

    /// <summary>Whether CEF is beside the application, where native/fetch-cef stages it, and has not refused to start.
    /// </summary>
    public bool IsAvailable => !failed && !shutDown && File.Exists(CefLibrary.LibraryFile(CefLibrary.ApplicationFolder));

    /// <summary>The helper CEF starts its processes on, beside CEF.</summary>
    public static string HelperFile => Path.Combine(CefLibrary.ApplicationFolder,
        OperatingSystem.IsWindows() ? "Adamantium.UI.WebView.Helper.exe" : "Adamantium.UI.WebView.Helper");

    /// <summary>Whether CEF is initialized, or can be now: on Windows it is started on the main graphics device's adapter,
    /// so not before that device is made - asked before, it is not tried and not given up on.</summary>
    internal bool CanInitialize => initialized || !OperatingSystem.IsWindows() || devices.MainGraphicsDevice != null;

    /// <summary>Initializes CEF on the UI loop, once; throws when CEF or its helper is missing or refuses, and CEF is not
    /// tried again - it allows one initialization a process. Before the main graphics device it throws and may be tried
    /// again (<see cref="CanInitialize"/>).</summary>
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

        if (!CanInitialize)
        {
            throw new InvalidOperationException("CEF waits for the main graphics device, whose adapter it is started on.");
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
        // None while the main device is made again: the page starts without one, and takes the next with the rest.
        var made = EnsureDevice();
        var path = made != null && allowSharedTexture && SharesTextures()
            ? WebViewFramePath.SharedTexture
            : WebViewFramePath.PixelBuffer;
        var browser = new WebViewBrowser(this, made, path, view);
        browsers.Add(browser);
        ArmPumpTimer(MaxPumpDelayMs);
        warmUp?.Dispose();
        warmUp = null;
        return browser;
    }

    /// <summary>Starts Chromium ahead of the first page: CEF, and its processes - the GPU's, the network's, a page's -
    /// kept up by a blank page shown nowhere until the first browser is made. That page then shows without waiting for
    /// them; until it does, they hold their memory - the blank page draws nothing and takes no device. On the UI loop;
    /// nothing while a browser is open, before the main graphics device is made, or with no CEF beside the application.
    /// </summary>
    public void StartAhead()
    {
        if (!IsAvailable || !CanInitialize || warmUp != null || browsers.Count > 0)
        {
            return;
        }

        try
        {
            EnsureInitialized();
            warmUp =new WebViewBrowser(this, null, WebViewFramePath.PixelBuffer, new WebViewUnseenView());
            browsers.Add(warmUp);
            ArmPumpTimer(MaxPumpDelayMs);
            warmUp.SetHidden(true);
            warmUp.Create(WebViewBrowser.Blank, 1, 1, 1f, IntPtr.Zero);
        }
        catch (Exception exception)
        {
            Serilog.Log.Logger.Warning(exception, "Chromium could not be started ahead of the first page");
            warmUp?.Dispose();
            warmUp = null;
        }
    }

    /// <summary>Whether the blank page of <see cref="StartAhead"/> keeps Chromium up: made by CEF, no browser made since.
    /// </summary>
    internal bool IsStartedAhead => warmUp is { IsMade: true };

    /// <summary>The browsers made and not closed yet - the blank page of <see cref="StartAhead"/> among them.</summary>
    internal int BrowserCount => browsers.Count;

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
        warmUp = null;
        devices.DeviceChangeBegin -= OnDeviceChanging;
        devices.DeviceChangeEnd -= OnDeviceChanged;
        if (application != null)
        {
            application.UIThreadExiting -= OnUIThreadExiting;
        }

        if (initialized)
        {
            // Kept as they are now: closing, Chromium cancels what comes in, which the next run lists as interrupted.
            downloadHistory.Save(downloads);
            // Downloads end with the application: a page kept for them closes too.
            for (var i = browsers.Count - 1; i >= 0; i--)
            {
                var browser = browsers[i];
                browser.Dispose();
                browser.CloseKept();
            }

            for (var turn = 0; turn < CloseTurns && browsers.Count > 0; turn++)
            {
                CefInterop.CefDoMessageLoopWork();
                Thread.Sleep(CloseTurnMs);
            }

            // What Chromium gave the downloads goes while it is there to take it back.
            foreach (var download in downloadsById.Values)
            {
                download.LetGo();
            }

            CefInterop.CefShutdown();
            for (var i = browsers.Count - 1; i >= 0; i--)
            {
                browsers[i].Abandon();
            }

            browsers.Clear();
        }

        downloadHistory.Dispose();
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

        siteFactory = new WebSiteSchemeHandlerFactory(Site);
        var scheme = new CefStringUtf16();
        try
        {
            CefStrings.Set(&scheme, WebViewSchemes.App);
            CefInterop.CefRegisterSchemeHandlerFactory(&scheme, null, siteFactory.Shared);
        }
        finally
        {
            CefStrings.Clear(&scheme);
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

        // Chromium forks its processes on Linux from a zygote it starts first, without exec - and the helper is a .NET
        // process, whose runtime does not live through a fork. Each process is exec'd instead, as with no sandbox
        // Chromium allows.
        if (OperatingSystem.IsLinux())
        {
            switches.Add(new("no-zygote", null));
        }

        if (ProprietaryCodecs.IsInstalled(CefLibrary.ApplicationFolder))
        {
            switches.Add(new(WebViewApp.EnableFeaturesSwitch, ProprietaryCodecs.Feature));
        }

        return switches;
    }

    private void ReleaseHandlers()
    {
        app?.Release();
        app = null;
        siteFactory?.Release();
        siteFactory = null;
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
        Volatile.Write(ref timerArmed, 1);
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
            LoopSignal.PostBetweenFrames(DoPumpWork);
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

        if (Volatile.Read(ref timerArmed) == 0)
        {
            ArmPumpTimer(MaxPumpDelayMs);
        }
    }

    private void OnPumpTimer()
    {
        Volatile.Write(ref timerArmed, 0);
        PostPumpWork();
    }

    private static string DefaultProfileFolder() => Path.Combine(ApplicationData.Folder, "WebView");
}
