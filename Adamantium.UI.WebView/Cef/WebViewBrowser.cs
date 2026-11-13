using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>
/// One CEF browser drawn off screen, for one <see cref="WebView"/>: made with the view's size and scale, told when they
/// change, its frames written by a <see cref="WebViewFrameProducer"/> on the engine's device. CEF makes the browser
/// asynchronously; an address set before it is made is loaded once it is. Lives on the UI loop, where CEF calls back.
/// </summary>
internal sealed unsafe class WebViewBrowser : IDisposable
{
    private const double ZoomStep = 1.2;
    /// <summary>The blank page's address.</summary>
    internal const string Blank = "about:blank";

    private readonly WebViewEngine engine;
    private readonly Dictionary<int, WebViewBrowser> popupsAsked = [];
    private readonly List<WebViewPermissionRequest> permissionsAsked = [];
    private readonly WebViewClient client;
    private readonly WebViewRenderHandler renderHandler;
    private readonly WebViewLifeSpanHandler lifeSpanHandler;
    private readonly WebViewDisplayHandler displayHandler;
    private readonly WebViewLoadHandler loadHandler;
    private readonly WebViewRequestHandler requestHandler;
    private volatile bool interceptsResources;
    private readonly WebViewContextMenuHandler contextMenuHandler;
    private readonly WebViewJsDialogHandler jsDialogHandler;
    private readonly WebViewDialogHandler dialogHandler;
    private readonly WebViewPermissionHandler permissionHandler;
    private readonly WebViewDownloadHandler downloadHandler;
    private readonly WebViewFindHandler findHandler;
    private readonly WebViewDevTools devTools;
    private IWebViewBrowserView view;
    private WebViewBrowser opener;
    private int popupId;
    private IntPtr parentWindow;
    private WebViewFileDialogRequest askedFiles;
    private WebViewFrameProducer producer;
    private WebViewFrameProducer popupProducer;
    private IGraphicsDevice frameDevice;
    private WebViewScriptDialogRequest askedDialog;
    private WebViewContextMenu askedMenu;
    private bool popupShown;
    private bool popupRepaintOwed;
    private Rect popupShownAt;
    private Vector2<double> popupMovedBy;
    private CefBrowser* browser;
    private string address;
    private string wanted;
    private bool scriptsPending;
    private bool madeBlank;
    private bool blankBehind;
    private bool isLoading;
    private string errorPageFor;
    private string failedAddress;
    private bool isDevTools;
    private WebViewDevToolsSocket devToolsSocket;
    private string createdWith;
    private bool requested;
    private bool closing;
    private bool keptForDownloads;
    private bool disposed;
    private bool repaintOwed;
    private bool hidden;
    private bool deviceLost;
    private bool focused;
    private double zoom = 1;
    private int findCount;
    private int findActive;
    private string findSought;
    private bool findForward;
    private bool findMatchCase;
    private bool findFirstOwed;
    private int iconsAsked;
    private Task<System.Text.Json.JsonElement> inspected;
    private string[] iconAddresses = [];
    private bool secure;
    private long securityAskedAt;
    private bool refused;
    private int framesCounted;
    private long countedSince;

    public WebViewBrowser(WebViewEngine engine, IGraphicsDevice device, WebViewFramePath framePath,
        IWebViewBrowserView view)
    {
        this.engine = engine;
        this.view = view;
        FramePath = framePath;
        frameDevice = device;
        // Made while the main device is made again: no device to write on until the engine hands it the next one.
        deviceLost = device == null;
        if (device != null)
        {
            producer = MakeProducer(device);
            popupProducer = MakePopupProducer(device);
        }
        renderHandler = new WebViewRenderHandler(this);
        lifeSpanHandler = new WebViewLifeSpanHandler(this);
        loadHandler = new WebViewLoadHandler(this);
        requestHandler = new WebViewRequestHandler(this);
        // The view of the moment: a page another opened is adopted by a view of its own.
        devTools = new WebViewDevTools(json => this.view.MessageReceived(this, json))
        {
            OverlayHeard = RevealWaiting,
            HostCalled = (context, isMainFrame, origin, json) =>
                this.view.HostCalled(this, new WebHostCall(this, context, isMainFrame, origin, json)),
            ContextGone = context => this.view.HostContextGone(this, context)
        };
        displayHandler = new WebViewDisplayHandler(this);
        contextMenuHandler = new WebViewContextMenuHandler(this);
        jsDialogHandler = new WebViewJsDialogHandler(this);
        dialogHandler = new WebViewDialogHandler(this);
        permissionHandler = new WebViewPermissionHandler(this);
        downloadHandler = new WebViewDownloadHandler(this);
        findHandler = new WebViewFindHandler(this);
        client = new WebViewClient(renderHandler, lifeSpanHandler, displayHandler, loadHandler, requestHandler,
            contextMenuHandler, jsDialogHandler, dialogHandler, permissionHandler, downloadHandler, findHandler);
    }

    /// <summary>The download Chromium numbers <paramref name="id"/>, the engine's one for the profile.</summary>
    internal WebDownload Download(uint id) => engine.Download(id);

    /// <summary>Chromium told of <paramref name="download"/>: one it says no more of is let go of when not saved.</summary>
    internal void DownloadHeard(WebDownload download) => engine.DownloadHeard(download);

    /// <summary>Whether the download Chromium numbers <paramref name="id"/> was given up here: it is not to go on.</summary>
    internal bool IsDownloadGivenUp(uint id) => engine.IsDownloadGivenUp(id);

    /// <summary>The page downloads a file: asked of the view, false when it cannot ask - the download is refused.
    /// </summary>
    internal bool OnDownloadRequested(WebDownload download) => !closing && view.DownloadRequested(this, download);

    /// <summary>The page's file field asks for files: asked of the view, false when it cannot ask.</summary>
    internal bool OnFileDialog(WebViewFileDialogRequest request)
    {
        if (closing)
        {
            return false;
        }

        // One at a time, as Chromium asks: one still asked is left unanswered no longer.
        askedFiles?.Cancel();
        askedFiles = request;
        return view.FileDialogRequested(this, request);
    }

    /// <summary>A site asks to use something: answered as it was answered before, else asked of the view; refused
    /// unasked when it asks for something unknown, or the view cannot ask.</summary>
    internal void OnPermissionRequest(WebViewPermissionRequest request, bool known)
    {
        if (closing || !known)
        {
            request.Answer(false);
            return;
        }

        if (engine.SitePermissionAnswer(request.Site, request.Permissions) is { } kept)
        {
            request.Answer(kept);
            return;
        }

        permissionsAsked.Add(request);
        request.Closed += (_, _) => permissionsAsked.Remove(request);
        if (!view.PermissionRequested(this, request))
        {
            request.Answer(false);
        }
    }

    /// <summary>The user answered <paramref name="request"/>: kept for the site.</summary>
    internal void PermissionDecided(WebViewPermissionRequest request, bool allowed)
    {
        engine.KeepSitePermission(request.Site, request.Permissions, allowed);
        // The same question asked again meanwhile - another frame, a retry - has its answer now too.
        for (var i = permissionsAsked.Count - 1; i >= 0; i--)
        {
            if (i < permissionsAsked.Count && permissionsAsked[i] is var waiting && waiting.Site == request.Site &&
                engine.SitePermissionAnswer(waiting.Site, waiting.Permissions) is { } kept)
            {
                waiting.Answer(kept);
            }
        }
    }

    /// <summary>A prompt refused without the user's answer: not held against the site.</summary>
    internal void PermissionRefusedUnasked(WebViewPermissionRequest request) => engine.ClearSiteEmbargo(request.Site);

    /// <summary>Chromium put a prompt away itself.</summary>
    internal void OnPermissionPromptGone(ulong promptId) =>
        permissionsAsked.Find(asked => asked.PromptId != 0 && asked.PromptId == promptId)?.Withdraw();

    /// <summary>The view's size in device-independent pixels, never empty.</summary>
    public (int Width, int Height) ViewSize { get; private set; } = (1, 1);

    /// <summary>The screen's pixels per device-independent pixel.</summary>
    public float Scale { get; private set; } = 1;

    /// <summary>How the frames come: Chromium's textures where the engine shares them, else pixels in memory.</summary>
    public WebViewFramePath FramePath { get; }

    /// <summary>Opens the way this page's DevTools front end talks to it, if it is not open: the address the front end
    /// is shown at, which tells it where to connect.</summary>
    public string OpenDevTools()
    {
        if (devToolsSocket == null)
        {
            devToolsSocket = new WebViewDevToolsSocket(ForwardToPage, FrontEndGone);
            devTools.ServeFrontEnd(null, devToolsSocket.Send);
        }

        return devToolsSocket.FrontEndAddress;
    }

    /// <summary>Closes the way the DevTools front end talked to this page; the page's own protocol channel goes on.
    /// </summary>
    public void CloseDevTools()
    {
        if (devToolsSocket == null)
        {
            return;
        }

        devToolsSocket.Dispose();
        devToolsSocket = null;
        var host = LiveHost();
        devTools.ServeFrontEnd(host, null);
        if (host != null)
        {
            CefRefCounting.ReleaseCef(host);
        }
    }

    private void FrontEndGone()
    {
        var host = LiveHost();
        devTools.FrontEndGone(host);
        if (host != null)
        {
            CefRefCounting.ReleaseCef(host);
        }
    }

    private void ForwardToPage(string json)
    {
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        devTools.Forward(host, json);
        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>Asks CEF to make the browser for a page's DevTools, showing their front end at <paramref name="url"/>:
    /// an ordinary page drawn off screen - CEF's own DevTools come only in a window - and none of the application's, so
    /// it gets no protocol channel and no window.adamantium.</summary>
    public void CreateDevToolsFrontEnd(string url, int width, int height, float scale, IntPtr parentWindow)
    {
        isDevTools = true;
        Create(url, width, height, scale, parentWindow);
    }

    /// <summary>What shows where the page paints no background of its own; read when the browser is made.</summary>
    public Color Background { get; set; } = Colors.White;

    /// <summary>Asks CEF to make the browser, showing <paramref name="url"/>.</summary>
    public void Create(string url, int width, int height, float scale, IntPtr parentWindow)
    {
        address = url;
        // The application's page is made blank and goes to its address once it took its scripts for new documents:
        // they and window.adamantium are there for the first document too. Made with nothing, it would have no page to
        // take them. The blank page is ours, not the page's: nobody hears of it, and it is wiped from the history once
        // the next is there. DevTools, and a page asked to be blank, go there at once.
        var goesThereAtOnce = isDevTools || url == Blank;
        createdWith = goesThereAtOnce ? url : Blank;
        madeBlank = !goesThereAtOnce;
        if (!isDevTools)
        {
            wanted ??= url;
        }

        ViewSize = (Math.Max(1, width), Math.Max(1, height));
        Scale = scale;
        this.parentWindow = parentWindow;

        var info = CefWindowInfos.Windowless(parentWindow, FramePath == WebViewFramePath.SharedTexture);
        var settings = new CefBrowserSettings { size = (ulong)sizeof(CefBrowserSettings) };
        Configure(&settings);
        var target = new CefStringUtf16();
        try
        {
            CefStrings.Set(&target, createdWith);
            if (CefInterop.CefBrowserHostCreateBrowser(info, client.Shared, &target, &settings, null, null) == 0)
            {
                throw new InvalidOperationException($"CEF refused to make a browser for {url}.");
            }

            requested = true;
        }
        finally
        {
            CefStrings.Clear(&target);
            CefWindowInfos.Free(info);
        }
    }

    /// <summary>Where the page is, or is going in a browser not made yet.</summary>
    public string Address => address;

    // What every browser of the view is made with.
    private void Configure(CefBrowserSettings* settings)
    {
        settings->windowless_frame_rate = 60;
        // CEF takes fully opaque or fully transparent alone; left unset, a windowless page paints transparent.
        settings->background_color = new CefColor
        {
            value = Background.A == 0 ? 0u : 0xFF000000u | ((uint)Background.R << 16) | ((uint)Background.G << 8) | Background.B
        };
    }

    /// <summary>Hands the page to <paramref name="adopter"/>, the view that shows it now: told what the page said to
    /// <paramref name="waited"/> meanwhile, and given its frames from the next one on.</summary>
    internal void Adopt(IWebViewBrowserView adopter, WebViewWaitingView waited)
    {
        // Surfaces for the new view, once the page is seen - on the device of the moment; one lost meanwhile gives them
        // when it is made anew.
        ReleaseSurfaces();
        view = adopter;
        waited?.Replay(adopter, this);
        ScriptsForNewDocumentsChanged();
        if (!hidden)
        {
            MakeSurfaces();
        }
    }

    /// <summary>The view's scripts for new documents changed: the page's next documents run them as they are now.
    /// </summary>
    public void ScriptsForNewDocumentsChanged()
    {
        var host = isDevTools ? null : LiveHost();
        if (host == null)
        {
            return;
        }

        devTools.SetScriptsForNewDocuments(host, view.ScriptsForNewDocuments(this));
        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>The page opens <paramref name="url"/> in a window of its own - a link to a new tab, <c>window.open</c>:
    /// asked of the view by the user's own doing alone. Shown, the browser CEF makes for it is <paramref name="popup"/>'s,
    /// drawn off screen like this one, its <c>window.opener</c> this page; false refuses it.</summary>
    internal bool OnBeforePopup(int id, string url, CefWindowOpenDisposition disposition, bool userGesture,
        void* windowInfo, CefClient** popupClient, CefBrowserSettings* settings)
    {
        if (!userGesture || closing || string.IsNullOrEmpty(url))
        {
            return false;
        }

        var waiting = new WebViewWaitingView(view.ScriptsForNewDocuments(this),
            asked => this.view.ResourceRequested(this, asked),
            download => !closing && this.view.DownloadRequested(this, download));
        if (engine.CreatePopupBrowser(FramePath, waiting) is not { } popup)
        {
            // No device to draw it on yet: the page goes there itself, as when nobody shows it apart.
            Navigate(url);
            return false;
        }

        popup.address = url;
        popup.createdWith = url;
        // Its requests are the opener's view's to decide while nobody shows it - from its very first.
        popup.interceptsResources = interceptsResources;
        popup.ViewSize = ViewSize;
        popup.Scale = Scale;
        popup.parentWindow = parentWindow;
        popup.Background = Background;
        // Out of sight until a view shows it: a tab opened behind draws nothing and holds no surfaces.
        popup.SetHidden(true);
        var page = new WebViewNewPage(url, popup, waiting);
        bool shown;
        try
        {
            shown = view.NewWindowRequested(this, page, KindOf(disposition));
        }
        catch
        {
            page.Dispose();
            throw;
        }

        // Let go of already - a view model that kept it no longer - it is refused: its client is gone.
        if (!shown || popup.disposed)
        {
            page.Dispose();
            if (!shown)
            {
                // Nobody shows it apart: the page goes there itself, as it did before new pages were asked for.
                Navigate(url);
            }

            return false;
        }

        CefWindowInfos.MakeWindowless(windowInfo, parentWindow, popup.FramePath == WebViewFramePath.SharedTexture);
        popup.Configure(settings);
        // Handed back with a reference CEF takes.
        *popupClient = popup.client.Shared;
        popup.requested = true;
        popup.opener = this;
        popup.popupId = id;
        popupsAsked[id] = popup;
        return true;
    }

    /// <summary>CEF gave up making the browser of a popup it was allowed.</summary>
    internal void OnPopupAborted(int id)
    {
        if (!popupsAsked.Remove(id, out var popup))
        {
            return;
        }

        popup.requested = false;
        popup.opener = null;
        if (popup.disposed)
        {
            engine.BrowserClosed(popup);
            return;
        }

        // A view showing it already makes the page again at its address; one still waiting for it gets a page that
        // never comes, as a page refused would be.
        popup.view.BrowserLost(popup);
    }

    /// <summary>A link the page opens elsewhere - Ctrl, Shift or the middle button: true when the view opened it, and
    /// the page stays where it is; false lets the page go there itself.</summary>
    internal bool OnOpenElsewhere(string url, CefWindowOpenDisposition disposition, bool userGesture)
    {
        if (closing || string.IsNullOrEmpty(url) || disposition is not (CefWindowOpenDisposition.NewForegroundTab or
                CefWindowOpenDisposition.NewBackgroundTab or CefWindowOpenDisposition.NewPopup or
                CefWindowOpenDisposition.NewWindow))
        {
            return false;
        }

        if (!userGesture)
        {
            return true;
        }

        var page = new WebViewNewPage(url);
        if (view.NewWindowRequested(this, page, KindOf(disposition)))
        {
            return true;
        }

        page.Dispose();
        return false;
    }

    private static WebNewWindowKind KindOf(CefWindowOpenDisposition disposition) => disposition switch
    {
        CefWindowOpenDisposition.NewBackgroundTab => WebNewWindowKind.BackgroundTab,
        CefWindowOpenDisposition.NewPopup => WebNewWindowKind.Popup,
        CefWindowOpenDisposition.NewWindow => WebNewWindowKind.Window,
        _ => WebNewWindowKind.ForegroundTab
    };

    /// <summary>Shows <paramref name="url"/>: at once in a browser already made, else once it is.</summary>
    public void Navigate(string url)
    {
        errorPageFor = null;
        if (browser == null || closing || scriptsPending)
        {
            wanted = url;
            return;
        }

        // A blank page asked for is the page's, heard of like any other.
        if (url == Blank)
        {
            blankBehind = false;
        }

        var frame = browser->get_main_frame(browser);
        var target = new CefStringUtf16();
        try
        {
            CefStrings.Set(&target, url);
            frame->load_url(frame, &target);
        }
        finally
        {
            CefStrings.Clear(&target);
            CefRefCounting.ReleaseCef(frame);
        }
    }

    /// <summary>Downloads <paramref name="url"/> as a link to it would, asked of the view like any download; false when
    /// there is no live browser to start it.</summary>
    public bool StartDownload(string url)
    {
        var host = LiveHost();
        if (host == null)
        {
            return false;
        }

        var target = new CefStringUtf16();
        try
        {
            CefStrings.Set(&target, url);
            host->start_download(host, &target);
        }
        finally
        {
            CefStrings.Clear(&target);
            CefRefCounting.ReleaseCef(host);
        }

        return true;
    }

    /// <summary>Tells the browser the view's new size or scale.</summary>
    public void Resize(int width, int height, float scale)
    {
        var size = (Math.Max(1, width), Math.Max(1, height));
        var rescaled = Math.Abs(scale - Scale) > float.Epsilon;
        if (size == ViewSize && !rescaled)
        {
            return;
        }

        ViewSize = size;
        Scale = scale;
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        if (rescaled)
        {
            host->notify_screen_info_changed(host);
        }

        host->was_resized(host);
        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>Tells the browser whether it is out of sight, when CEF stops drawing it.</summary>
    public void SetHidden(bool hidden)
    {
        var changed = this.hidden != hidden;
        this.hidden = hidden;
        var host = LiveHost();
        if (host != null)
        {
            host->was_hidden(host, hidden ? 1 : 0);
            CefRefCounting.ReleaseCef(host);
        }

        // Out of sight, the page draws nothing: its surfaces - a frame's size each, a few of them - go back to the GPU,
        // and come again with a whole frame once it is seen.
        if (!changed)
        {
            return;
        }

        if (hidden)
        {
            // A dropped list goes with the page out of sight: nothing would draw it there once seen again.
            if (popupShown)
            {
                popupShown = false;
                popupRepaintOwed = false;
                view.PopupClosed(this);
            }

            ReleaseSurfaces();
        }
        else if (producer == null)
        {
            MakeSurfaces();
        }
    }

    /// <summary>Whether the device the frames were written on is gone and no other given yet.</summary>
    internal bool IsDeviceLost => deviceLost;

    /// <summary>The device the frames were written on is lost: the panels let go of them and they go. The page lives on
    /// and paints into nothing until <see cref="TakeDevice"/>.</summary>
    public void LetGoOfDevice()
    {
        deviceLost = true;
        ReleaseSurfaces();
    }

    /// <summary>Writes the frames on <paramref name="device"/> from now on - the one made in place of a lost one - and
    /// asks the page for a whole frame again, and its dropped list for one if it has one; a page out of sight, once it is
    /// seen.</summary>
    public void TakeDevice(IGraphicsDevice device)
    {
        ReleaseSurfaces();
        deviceLost = false;
        frameDevice = device;
        if (!hidden)
        {
            MakeSurfaces();
        }
    }

    private void ReleaseSurfaces()
    {
        if (producer == null)
        {
            return;
        }

        view.LetGo(this);
        producer.Dispose();
        producer = null;
        popupProducer?.Dispose();
        popupProducer = null;
    }

    // Surfaces on the device of the moment, and a whole frame asked for them; none while the device is lost or the page
    // going.
    private void MakeSurfaces()
    {
        if (deviceLost || closing || frameDevice == null)
        {
            return;
        }

        ReleaseSurfaces();
        producer = MakeProducer(frameDevice);
        popupProducer = MakePopupProducer(frameDevice);
        repaintOwed = true;
        popupRepaintOwed = popupShown;
        if (popupShown)
        {
            view.PopupPlaced(this, popupShownAt);
        }

        RepaintIfOwed();
    }

    private WebViewFrameProducer MakeProducer(IGraphicsDevice device) =>
        new(device, descriptor => view.Show(this, descriptor), () =>
        {
            framesCounted++;
            view.FrameWritten(this);
        });

    private WebViewFrameProducer MakePopupProducer(IGraphicsDevice device) =>
        new(device, descriptor => view.ShowPopup(this, descriptor), () => view.FrameWritten(this));

    private void Owe(bool popup)
    {
        if (popup)
        {
            popupRepaintOwed = true;
        }
        else
        {
            repaintOwed = true;
        }
    }

    /// <summary>Asks the page again for a frame no surface was free for, once the panel has given one back - the
    /// page's, and its dropped list's.</summary>
    public void RepaintIfOwed()
    {
        var page = repaintOwed && producer is { HasFreeSurface: true };
        var popup = popupRepaintOwed && popupShown && popupProducer is { HasFreeSurface: true };
        if (closing || !page && !popup)
        {
            return;
        }

        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        if (page)
        {
            repaintOwed = false;
            host->invalidate(host, CefPaintElementType.View);
        }

        if (popup)
        {
            popupRepaintOwed = false;
            host->invalidate(host, CefPaintElementType.Popup);
        }

        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>The mouse moved to <paramref name="x"/>, <paramref name="y"/> in the view's units, or left the view.
    /// </summary>
    public void SendMouseMove(double x, double y, InputModifiers modifiers, bool leave)
    {
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        var mouse = MouseEvent(x, y, modifiers);
        host->send_mouse_move_event(host, &mouse, leave ? 1 : 0);
        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>A mouse button pressed or released at <paramref name="x"/>, <paramref name="y"/> in the view's units,
    /// the <paramref name="clickCount"/>th click in a row.</summary>
    public void SendMouseClick(double x, double y, InputModifiers modifiers, CefMouseButtonType button, bool up,
        int clickCount)
    {
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        var mouse = MouseEvent(x, y, modifiers);
        host->send_mouse_click_event(host, &mouse, button, up ? 1 : 0, clickCount);
        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>The wheel turned over <paramref name="x"/>, <paramref name="y"/> in the view's units, by 120 a notch.
    /// </summary>
    public void SendMouseWheel(double x, double y, InputModifiers modifiers, int deltaX, int deltaY)
    {
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        var mouse = MouseEvent(x, y, modifiers);
        host->send_mouse_wheel_event(host, &mouse, deltaX, deltaY);
        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>A finger or a pen, <paramref name="id"/> while it touches, at <paramref name="x"/>, <paramref name="y"/>
    /// in the view's units: put down, moved, lifted or cancelled.</summary>
    public void SendTouch(uint id, double x, double y, CefTouchEventType type, CefPointerType pointerType,
        PointerPointProperties properties, InputModifiers modifiers)
    {
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        (x, y) = OnTheList(x, y);
        var touch = new CefTouchEvent
        {
            id = (int)id,
            x = (float)x,
            y = (float)y,
            radius_x = (float)(properties.ContactRect.Width / 2),
            radius_y = (float)(properties.ContactRect.Height / 2),
            rotation_angle = properties.Twist,
            pressure = properties.Pressure,
            type = type,
            modifiers = CefInputMap.Flags(modifiers),
            pointer_type = pointerType
        };
        host->send_touch_event(host, &touch);
        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>What the page last said it does with what is dragged over it.</summary>
    public DragDropEffects DragEffects { get; private set; }

    /// <summary>Whether the page has answered the drag over it yet - it answers a move late.</summary>
    public bool DragAnswered { get; private set; }

    /// <summary>A drag came over the view, carrying <paramref name="data"/> and allowing <paramref name="allowed"/>.
    /// </summary>
    public void DragEnter(IDataPackage data, double x, double y, InputModifiers modifiers, DragDropEffects allowed)
    {
        DragEffects = DragDropEffects.None;
        DragAnswered = false;
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        var mouse = DragEvent(x, y, modifiers);
        host->drag_target_drag_enter(host, CefDragDatas.FromPackage(data), &mouse, CefInputMap.Operations(allowed));
        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>The drag moved over the view.</summary>
    public void DragOver(double x, double y, InputModifiers modifiers, DragDropEffects allowed)
    {
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        var mouse = DragEvent(x, y, modifiers);
        host->drag_target_drag_over(host, &mouse, CefInputMap.Operations(allowed));
        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>The drag left the view.</summary>
    public void DragLeave()
    {
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        host->drag_target_drag_leave(host);
        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>What was dragged was dropped on the view.</summary>
    public void Drop(double x, double y, InputModifiers modifiers)
    {
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        var mouse = DragEvent(x, y, modifiers);
        host->drag_target_drop(host, &mouse);
        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>A drag the page started ended at <paramref name="x"/>, <paramref name="y"/> in the view's units, as
    /// <paramref name="effects"/> - none when it landed nowhere.</summary>
    public void DragEnded(double x, double y, DragDropEffects effects)
    {
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        host->drag_source_ended_at(host, (int)Math.Round(x), (int)Math.Round(y), CefInputMap.Operations(effects));
        host->drag_source_system_drag_ended(host);
        CefRefCounting.ReleaseCef(host);
    }

    internal bool OnStartDragging(CefDragData* data, CefDragOperationsMask allowed) =>
        !closing && view.StartDragging(this, CefDragDatas.ToPackage(data), CefInputMap.Effects(allowed));

    internal void OnDragCursor(CefDragOperationsMask operation)
    {
        DragEffects = CefInputMap.Effects(operation);
        DragAnswered = true;
    }

    /// <summary>The view lost the mouse it had captured.</summary>
    public void SendCaptureLost()
    {
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        host->send_capture_lost_event(host);
        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>The view took the keyboard focus, or lost it; told to a browser not made yet once it is.</summary>
    public void SetFocus(bool focused)
    {
        this.focused = focused;
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        host->set_focus(host, focused ? 1 : 0);
        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>Whether the page's focus is in a field that takes typing, as CEF last said - true until it has said,
    /// since it says so only after a focus the user gave, so keys that type go to the page rather than away from it.
    /// </summary>
    public bool InEditableField { get; private set; } = true;

    /// <summary>A key went down or up: <paramref name="key"/> is the key the layout makes it, <paramref name="physicalKey"/>
    /// where it is on the keyboard (a HID usage, 0 when not known).</summary>
    public void SendKey(Key key, uint physicalKey, InputModifiers modifiers, bool up, bool repeated)
    {
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        var flags = CefInputMap.Flags(modifiers) | (repeated ? (uint)CefEventFlags.IsRepeat : 0);
        var alt = (flags & (uint)CefEventFlags.AltDown) != 0;
        var control = (flags & (uint)CefEventFlags.ControlDown) != 0;
        var keyEvent = new CefKeyEvent
        {
            size = (ulong)sizeof(CefKeyEvent),
            type = up ? CefKeyEventType.Keyup : CefKeyEventType.Rawkeydown,
            modifiers = flags,
            windows_key_code = (int)key,
            native_key_code = CefInputMap.NativeKeyCode(physicalKey),
            is_system_key = alt && !control ? 1 : 0,
            focus_on_editable_field = InEditableField ? 1 : 0
        };
        host->send_key_event(host, &keyEvent);
        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>A character typed - what a key that types sends after it went down.</summary>
    public void SendCharacter(char character, InputModifiers modifiers)
    {
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        var keyEvent = new CefKeyEvent
        {
            size = (ulong)sizeof(CefKeyEvent),
            type = CefKeyEventType.Char,
            modifiers = CefInputMap.Flags(modifiers),
            windows_key_code = character,
            character = character,
            unmodified_character = character,
            focus_on_editable_field = InEditableField ? 1 : 0
        };
        host->send_key_event(host, &keyEvent);
        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>The text an input method is composing, its cursor at <paramref name="cursor"/>: shown in the page, not yet
    /// part of it.</summary>
    public void SetComposition(string text, int cursor)
    {
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        var cefText = new CefStringUtf16();
        try
        {
            CefStrings.Set(&cefText, text);
            var underline = new CefCompositionUnderline
            {
                size = (ulong)sizeof(CefCompositionUnderline),
                range = new CefRange { from = 0, to = (uint)text.Length },
                color = new CefColor { value = 0xFF000000 },
                style = CefCompositionUnderlineStyle.Solid
            };
            var replacement = new CefRange { from = uint.MaxValue, to = uint.MaxValue };
            var selection = new CefRange { from = (uint)cursor, to = (uint)cursor };
            host->ime_set_composition(host, &cefText, text.Length > 0 ? 1ul : 0ul, &underline, &replacement, &selection);
        }
        finally
        {
            CefStrings.Clear(&cefText);
            CefRefCounting.ReleaseCef(host);
        }
    }

    /// <summary>The text an input method composed, put into the page in place of the composition.</summary>
    public void CommitComposition(string text)
    {
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        var cefText = new CefStringUtf16();
        try
        {
            CefStrings.Set(&cefText, text);
            var replacement = new CefRange { from = uint.MaxValue, to = uint.MaxValue };
            host->ime_commit_text(host, &cefText, &replacement, 0);
        }
        finally
        {
            CefStrings.Clear(&cefText);
            CefRefCounting.ReleaseCef(host);
        }
    }

    /// <summary>The input method's composition is dropped, nothing put into the page.</summary>
    public void CancelComposition()
    {
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        host->ime_cancel_composition(host);
        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>Shows the page at <paramref name="factor"/> times its size; told to a browser not made yet once it is.
    /// Chromium keeps it for the page's site.</summary>
    public void SetZoom(double factor)
    {
        zoom = factor;
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        host->set_zoom_level(host, Math.Log(factor) / Math.Log(ZoomStep));
        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>Finds <paramref name="text"/> on the page and marks its matches, the current one apart: a search
    /// started anew when <paramref name="findNext"/> is false, else the next match, or the previous one when
    /// <paramref name="forward"/> is false. Chromium tells how many it found through <see cref="OnFindResult"/>.
    /// </summary>
    public void Find(string text, bool forward, bool matchCase, bool findNext)
    {
        if (isDevTools || string.IsNullOrEmpty(text))
        {
            return;
        }

        if (!findNext)
        {
            findCount = 0;
            findActive = 0;
            findSought = text;
            findForward = forward;
            findMatchCase = matchCase;
        }

        // A search begun makes no match current; the first is made so once Chromium has counted them, as a browser
        // does.
        findFirstOwed = !findNext;
        SendFind(text, forward, matchCase, findNext);
    }

    private void SendFind(string text, bool forward, bool matchCase, bool findNext)
    {
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        var target = new CefStringUtf16();
        CefStrings.Set(&target, text);
        host->find(host, &target, forward ? 1 : 0, matchCase ? 1 : 0, findNext ? 1 : 0);
        CefStrings.Clear(&target);
        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>Ends the search: the matches are no longer marked, and the current one stays selected unless
    /// <paramref name="clearSelection"/>.</summary>
    public void StopFinding(bool clearSelection)
    {
        var host = isDevTools ? null : LiveHost();
        if (host == null)
        {
            return;
        }

        findFirstOwed = false;
        host->stop_finding(host, clearSelection ? 1 : 0);
        CefRefCounting.ReleaseCef(host);
    }

    // Chromium tells -1 for what has not changed since its last word on the search.
    internal void OnFindResult(int count, int activeMatch, bool final)
    {
        if (count >= 0)
        {
            findCount = count;
        }

        if (activeMatch >= 0)
        {
            findActive = activeMatch;
        }

        var stepping = false;
        if (final && findFirstOwed)
        {
            findFirstOwed = false;
            if (findCount > 0 && findActive == 0)
            {
                stepping = true;
                SendFind(findSought, findForward, findMatchCase, true);
            }
        }

        view.FindResult(this, findCount, findActive, final && !stepping);
    }

    /// <summary>Goes back a page in the browser's history.</summary>
    public void GoBack()
    {
        errorPageFor = null;
        if (browser != null && !closing && !blankBehind)
        {
            browser->go_back(browser);
        }
    }

    /// <summary>Goes forward a page in the browser's history.</summary>
    public void GoForward()
    {
        errorPageFor = null;
        if (browser != null && !closing)
        {
            browser->go_forward(browser);
        }
    }

    /// <summary>Loads the page again.</summary>
    public void Reload()
    {
        errorPageFor = null;
        if (browser != null && !closing)
        {
            browser->reload(browser);
        }
    }

    /// <summary>Stops loading the page.</summary>
    public void Stop()
    {
        if (browser != null && !closing)
        {
            browser->stop_load(browser);
        }
    }

    internal void OnCursorChange(CefCursorType type) => view.CursorChanged(this, CefInputMap.Cursor(type));

    internal void OnAddressChange(string url)
    {
        if (OfTheBlankPage(url))
        {
            return;
        }

        url = Shown(url);
        address = url;
        view.AddressChanged(this, url);
        TellSecurity();
    }

    // A page's source is told as the page's own address: the address it is shown at says it is the source.
    private string Shown(string url)
    {
        var host = LiveHost();
        if (host == null)
        {
            return url;
        }

        var entry = host->get_visible_navigation_entry(host);
        CefRefCounting.ReleaseCef(host);
        if (entry == null)
        {
            return url;
        }

        var shown = entry->is_valid(entry) != 0 ? CefStrings.ReadAndFree(entry->get_display_url(entry)) : null;
        CefRefCounting.ReleaseCef(entry);
        return shown != null && shown.StartsWith("view-source:", StringComparison.Ordinal) ? shown : url;
    }

    /// <summary>Asks again, once a second from the engine's pump, whether the page is secure: what it loads after it
    /// has loaded - a script's request in the open - can make it less than secure, and CEF tells nothing of it.
    /// </summary>
    public void FollowSecurity(long now)
    {
        if (now - securityAskedAt < 1000)
        {
            return;
        }

        securityAskedAt = now;
        TellSecurity();
    }

    private void TellSecurity()
    {
        if (madeBlank)
        {
            return;
        }

        var isSecure = PageIsSecure();
        if (isSecure != secure)
        {
            secure = isSecure;
            view.SecurityChanged(this, isSecure);
        }
    }

    internal void OnTitleChange(string title)
    {
        if (!madeBlank && !OfTheBlankPage(title))
        {
            view.TitleChanged(this, title);
        }
    }

    internal void OnStatusMessage(string message)
    {
        if (!madeBlank)
        {
            view.StatusChanged(this, message);
        }
    }

    internal void OnTooltip(string text) => view.TooltipChanged(this, text);

    /// <summary>A dialog the page's script asks: the view asks it over the page, or answers it at once as cancelled.
    /// </summary>
    internal bool OnScriptDialog(WebViewScriptDialogRequest request)
    {
        // A closing page asks nobody: kept by an answer to stay, it would never close.
        if (closing)
        {
            return false;
        }

        askedDialog = request;
        return view.ScriptDialogRequested(this, request);
    }

    /// <summary>A dialog the view did not take is not the page's to answer at the end.</summary>
    internal void ForgetScriptDialog(WebViewScriptDialogRequest request)
    {
        if (ReferenceEquals(askedDialog, request))
        {
            askedDialog = null;
        }
    }

    /// <summary>The page went on - navigated, closed: what its dialogs asked is asked no more.</summary>
    internal void OnScriptDialogsReset() => view.ScriptDialogsReset(this);

    /// <summary>Opens the element at <paramref name="x"/>, <paramref name="y"/> of the view, in device-independent
    /// pixels, in the DevTools' Elements panel: now, if their front end listens, else once it does - while the page
    /// is the same.</summary>
    public void InspectAt(double x, double y)
    {
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        // Found now, as the page is laid out under the menu: the DevTools opening beside it lay it out narrower.
        inspected = devTools.Locate(host, x / zoom, y / zoom);
        CefRefCounting.ReleaseCef(host);
        if (devTools.FrontEndHearsOverlay)
        {
            RevealWaiting();
        }
    }

    private void RevealWaiting()
    {
        if (inspected == null)
        {
            return;
        }

        devTools.Reveal(inspected);
        inspected = null;
    }

    /// <summary>The menu the page asks for: the view shows it, or answers it at once with none.</summary>
    internal bool OnRunContextMenu(WebViewContextMenu menu)
    {
        askedMenu = menu;
        return !isDevTools && view.ContextMenuRequested(this, menu);
    }

    /// <summary>Puts the page back at the address it is at - its own question kept it there, as a navigation it asked
    /// about was not made.</summary>
    public void KeepAddress()
    {
        if (address != null)
        {
            view.AddressChanged(this, address);
        }
    }

    internal void OnLoadingProgress(double progress)
    {
        if (!madeBlank)
        {
            view.ProgressChanged(this, progress);
        }
    }

    internal void OnLoadingState(bool isLoading, bool canGoBack, bool canGoForward)
    {
        this.isLoading = isLoading;
        if (madeBlank)
        {
            return;
        }

        if (!isLoading)
        {
            var host = LiveHost();
            if (host != null)
            {
                zoom = Math.Pow(ZoomStep, host->get_zoom_level(host));
                CefRefCounting.ReleaseCef(host);
                view.ZoomChanged(this, zoom);
            }

            // A load stopped before it got anywhere leaves the page where it was: the view gets that address back.
            if (address != null)
            {
                view.AddressChanged(this, address);
            }

            // What the page loaded once it is done tells too: content in the open makes it less than secure.
            TellSecurity();
        }

        // Behind the first page is only the blank one it was made with, until that is wiped.
        view.LoadingChanged(this, isLoading, canGoBack && !blankBehind, canGoForward);
    }

    // A page's icons, 16 device-independent pixels across as a tab shows them.
    private const int IconSize = 16;

    /// <summary>A new page in the main frame: its icon is not known yet, and no icon asked for the last one is shown.
    /// </summary>
    internal void OnLoadStart(string url)
    {
        // What the page left asked is asked no more: Chromium puts its prompts away itself, a media request is refused.
        for (var i = permissionsAsked.Count - 1; i >= 0; i--)
        {
            if (i < permissionsAsked.Count && permissionsAsked[i].PromptId == 0)
            {
                permissionsAsked[i].Withdraw();
            }
        }

        // An element of the page gone is none of the next's.
        inspected = null;
        iconsAsked++;
        view.IconChanged(this, null);
        // The blank page's own start, late, is not the page that gives it away.
        if (blankBehind && url != Blank)
        {
            ForgetTheBlankPage();
        }
    }

    /// <summary>The page's icons as it names them, the one it prefers first: fetched in turn until one comes; none
    /// shows none.</summary>
    internal void OnIconAddresses(string[] addresses)
    {
        iconsAsked++;
        iconAddresses = addresses;
        FetchIcon(0);
    }

    private void FetchIcon(int index)
    {
        var host = isDevTools || index >= iconAddresses.Length ? null : LiveHost();
        if (host == null)
        {
            view.IconChanged(this, null);
            return;
        }

        var download = new WebViewIconDownload(this, iconsAsked, index);
        var target = new CefStringUtf16();
        try
        {
            CefStrings.Set(&target, iconAddresses[index]);
            host->download_image(host, &target, 1, IconSize, 0, download.Shared);
        }
        finally
        {
            CefStrings.Clear(&target);
            download.Release();
            CefRefCounting.ReleaseCef(host);
        }
    }

    /// <summary>The icon asked for under <paramref name="asked"/> from the page's <paramref name="index"/>th address,
    /// or null for none: shown at the screen's scale if it is still the page's; none tries the next address.</summary>
    internal void OnIconDownloaded(int asked, int index, CefImage* image)
    {
        if (asked != iconsAsked || disposed)
        {
            return;
        }

        var icon = image == null || image->is_empty(image) != 0 ? null : IconOf(image);
        if (icon == null)
        {
            FetchIcon(index + 1);
            return;
        }

        view.IconChanged(this, icon);
    }

    private BitmapSource IconOf(CefImage* image)
    {
        int width, height;
        var bitmap = image->get_as_bitmap(image, Scale, CefColorType.Bgra8888, CefAlphaType.Postmultiplied, &width,
            &height);
        if (bitmap == null)
        {
            return null;
        }

        try
        {
            var size = (int)bitmap->get_size(bitmap);
            if (width <= 0 || height <= 0 || size < width * height * 4)
            {
                return null;
            }

            var pixels = new byte[width * height * 4];
            fixed (byte* into = pixels)
            {
                bitmap->get_data(bitmap, into, (ulong)pixels.Length, 0);
            }

            // As wide as the page's image is in device-independent pixels, whatever the pixels of the one that came.
            var across = (double)image->get_width(image);
            var scale = across > 0 ? across / width : 1 / Scale;
            return new BitmapSource((uint)width, (uint)height, scale, scale, SurfaceFormat.B8G8R8A8.UNorm, pixels);
        }
        finally
        {
            CefRefCounting.ReleaseCef(bitmap);
        }
    }

    /// <summary>Whether the page the browser shows came over a connection with nothing wrong: encrypted, its
    /// certificate good, nothing of it loaded in the open.</summary>
    private bool PageIsSecure()
    {
        var host = LiveHost();
        if (host == null)
        {
            return false;
        }

        var entry = host->get_visible_navigation_entry(host);
        CefRefCounting.ReleaseCef(host);
        if (entry == null)
        {
            return false;
        }

        var status = entry->is_valid(entry) != 0 ? entry->get_sslstatus(entry) : null;
        CefRefCounting.ReleaseCef(entry);
        if (status == null)
        {
            return false;
        }

        var secure = status->is_secure_connection(status) != 0 &&
                     CefInterop.CefIsCertStatusError(status->get_cert_status(status)) == 0 &&
                     status->get_content_status(status) == CefSslContentStatus.NormalContent;
        CefRefCounting.ReleaseCef(status);
        return secure;
    }

    internal void OnLoadError(WebViewLoadError error)
    {
        if (OfTheBlankPage(error.Address))
        {
            return;
        }

        errorPageFor = error.Address;
        failedAddress = error.Address;
        view.LoadFailed(this, error);
        view.NavigationCompleted(this, error.Address, error);
    }

    internal void OnTextInputMode(CefTextInputMode mode) => InEditableField = mode != CefTextInputMode.None;

    internal void OnCompositionBounds(CefRect bounds) =>
        view.CompositionBoundsChanged(this, new Rect(bounds.x, bounds.y, bounds.width, bounds.height));

    /// <summary>Hands on the frames written in the last second, once a second, from the engine's pump.</summary>
    public void MeasureFrameRate(long now)
    {
        if (countedSince == 0)
        {
            countedSince = now;
            return;
        }

        if (now - countedSince < 1000)
        {
            return;
        }

        view.FrameRate(this, (int)(framesCounted * 1000 / (now - countedSince)));
        framesCounted = 0;
        countedSince = now;
    }

    /// <summary>Whether <paramref name="other"/> is this view's browser - not a popup or DevTools of it.</summary>
    internal bool Is(CefBrowser* other)
    {
        if (browser == null || other == null)
        {
            return false;
        }

        CefRefCounting.AddRefCef(other);
        return browser->is_same(browser, other) != 0;
    }

    internal void OnCreated(CefBrowser* created)
    {
        if (browser != null)
        {
            CefRefCounting.ReleaseCef(created);
            return;
        }

        browser = created;
        opener?.popupsAsked.Remove(popupId);
        opener = null;
        // DevTools are not a page of the application's: no protocol channel, no window.adamantium in them.
        if (!isDevTools)
        {
            var host = Host();
            scriptsPending = true;
            devTools.Attach(host, view.ScriptsForNewDocuments(this), engine.ColorScheme, GoWhereWanted);
            CefRefCounting.ReleaseCef(host);
        }

        if (closing)
        {
            Close();
            return;
        }

        if (hidden)
        {
            SetHidden(true);
        }

        if (focused)
        {
            SetFocus(true);
        }

        if (Math.Abs(zoom - 1) > 1e-6)
        {
            SetZoom(zoom);
        }

        if (!scriptsPending)
        {
            GoWhereWanted();
        }
    }

    // Goes where the page was asked to before it could: once made, and for the application's page once it took its
    // scripts for new documents.
    private void GoWhereWanted()
    {
        scriptsPending = false;
        if (closing)
        {
            return;
        }

        var first = wanted;
        wanted = null;
        if (madeBlank && first == Blank)
        {
            // Asked to be blank meanwhile: the blank page it was made with is the page asked for, and told as it.
            madeBlank = false;
            address = Blank;
            view.AddressChanged(this, Blank);
            view.LoadingChanged(this, false, false, false);
            view.NavigationCompleted(this, Blank, null);
            return;
        }

        if (first != null && first != createdWith)
        {
            blankBehind = madeBlank;
            madeBlank = false;
            Navigate(first);
        }
    }

    // What the blank page the browser was made with says - nobody's to hear: its address, and while the browser is on
    // it, all it tells.
    private bool OfTheBlankPage(string url) => (madeBlank || blankBehind) && url == Blank;

    // The page the blank one it was made with gave way to is there: the blank one goes from its history - nothing to
    // go back to - and the history is told again once it went.
    private void ForgetTheBlankPage()
    {
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        devTools.ResetHistory(host, reset =>
        {
            blankBehind = false;
            if (browser == null || closing)
            {
                return;
            }

            // Wiped, the history is the page alone - CEF tells what it last heard until the next loading change.
            // Refused, the blank page stays behind, and is not hidden.
            view.LoadingChanged(this, isLoading, !reset && browser->can_go_back(browser) != 0,
                !reset && browser->can_go_forward(browser) != 0);
        });
        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>A frame from CEF, the page's or its dropped list's; one no surface was free for is asked for again by
    /// <see cref="RepaintIfOwed"/>.</summary>
    internal void OnPaint(CefPaintElementType type, ReadOnlySpan<CefRect> dirty, byte* pixels, int width, int height)
    {
        if (closing)
        {
            return;
        }

        if (type == CefPaintElementType.Popup)
        {
            if (popupShown && (popupProducer == null || !popupProducer.Write(dirty, pixels, width, height)))
            {
                popupRepaintOwed = true;
            }

            return;
        }

        // No device to write on - it was lost and the new one is not there yet: the frame is asked for again then.
        if (producer == null || !producer.Write(dirty, pixels, width, height))
        {
            repaintOwed = true;
        }
    }

    /// <summary>The page dropped a list - a select's - or put it away: shown apart from the page, over it.</summary>
    internal void OnPopupShow(bool show)
    {
        popupShown = show;
        if (show)
        {
            return;
        }

        // Put away with its surfaces: the next list comes on surfaces of its own, never over this one's last frame.
        popupRepaintOwed = false;
        view.PopupClosed(this);
        if (popupProducer != null && frameDevice != null)
        {
            popupProducer.Dispose();
            popupProducer = MakePopupProducer(frameDevice);
        }
    }

    /// <summary>Where the page places its dropped list, in the view's device-independent pixels: moved into the view
    /// where it would stand past its edge - a select near the bottom - and shown there.</summary>
    internal void OnPopupSize(CefRect rect)
    {
        var x = Math.Max(0, Math.Min(rect.x, ViewSize.Width - rect.width));
        var y = Math.Max(0, Math.Min(rect.y, ViewSize.Height - rect.height));
        popupShownAt = new Rect(x, y, rect.width, rect.height);
        popupMovedBy = new Vector2<double>(rect.x - x, rect.y - y);
        view.PopupPlaced(this, popupShownAt);
    }

    /// <summary>A frame in Chromium's <paramref name="texture"/>, copied before the call returns, when CEF takes the
    /// texture back. One that cannot be copied - not BGRA, its visible part not at the corner, or not taken by the
    /// device - ends the shared path: the engine starts no more browsers on it, and the view makes this one again on
    /// pixels in memory.</summary>
    internal void OnAcceleratedPaint(CefPaintElementType type, ReadOnlySpan<CefRect> dirty, IntPtr texture,
        CefColorType format, int textureWidth, int textureHeight, CefRect visible)
    {
        if (closing || refused)
        {
            return;
        }

        var popup = type == CefPaintElementType.Popup;
        if (popup && !popupShown)
        {
            return;
        }

        var into = popup ? popupProducer : producer;
        if (into == null)
        {
            Owe(popup);
            return;
        }

        try
        {
            if (format != CefColorType.Bgra8888 || visible.x != 0 || visible.y != 0)
            {
                throw new NotSupportedException($"A {format} texture whose visible part is at {visible.x},{visible.y}.");
            }

            if (!into.Copy(dirty, texture, textureWidth, textureHeight, visible.width, visible.height))
            {
                Owe(popup);
            }
        }
        catch (Exception exception)
        {
            refused = true;
            Serilog.Log.Logger.Warning(exception, "The page's texture is not copied; it is drawn into memory instead");
            engine.SharedTextureRefused();
            LoopSignal.Post(() => view.SharedTextureRefused(this));
        }
    }

    internal void OnClosing(CefBrowser* closed)
    {
        if (!Is(closed))
        {
            return;
        }

        devTools.Detach();
        var own = browser;
        browser = null;
        CefRefCounting.ReleaseCef(own);
        engine.BrowserClosed(this);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        devToolsSocket?.Dispose();
        devToolsSocket = null;
        closing = true;
        // The page's requests are asked of nobody now, and what still waits is refused while CEF is there.
        interceptsResources = false;
        requestHandler.RefuseWaiting();
        // What the page still asks is answered while CEF is there to hear it: the view may answer only later, CEF gone.
        askedDialog?.Answer(false, null);
        askedDialog = null;
        askedMenu?.Dismiss();
        askedMenu = null;
        askedFiles?.Cancel();
        askedFiles = null;
        // Refused, kept for nobody: the page asking is going.
        for (var i = permissionsAsked.Count - 1; i >= 0; i--)
        {
            permissionsAsked[i].Answer(false);
        }

        engine.RefuseAsked(this);
        view.ScriptDialogsReset(this);
        view.LetGo(this);
        // What CEF still tells of the page reaches nobody: the view is let go of, not held by the closing page.
        view = new WebViewUnseenView();
        producer?.Dispose();
        producer = null;
        popupProducer?.Dispose();
        popupProducer = null;
        if (browser != null)
        {
            if (engine.HasDownloadsComingIn(this))
            {
                KeepForDownloads();
            }
            else
            {
                Close();
            }
        }
        else if (!requested)
        {
            engine.BrowserClosed(this);
        }

        client.Release();
        renderHandler.Release();
        lifeSpanHandler.Release();
        displayHandler.Release();
        loadHandler.Release();
        requestHandler.Release();
        contextMenuHandler.Release();
        jsDialogHandler.Release();
        dialogHandler.Release();
        permissionHandler.Release();
        downloadHandler.Release();
        findHandler.Release();
    }

    /// <summary>Whether the view listens to the page's requests: each is asked of it then, else none is - read on
    /// CEF's IO thread.</summary>
    public bool InterceptsResources
    {
        get => interceptsResources;
        set => interceptsResources = value;
    }

    /// <summary>Whether the browser is closing, or closed.</summary>
    internal bool IsClosing => closing;

    /// <summary>Whether CEF has made the browser, and it is not closed yet.</summary>
    internal bool IsMade => browser != null;

    /// <summary>Whether the page was closed while its downloads came in, and waits for them to be done.</summary>
    internal bool IsKeptForDownloads => keptForDownloads;

    // Its downloads are the profile's, not the page's: Chromium cancels them with the browser, so the page, shown
    // nowhere, silent and blank, goes once they are done.
    private void KeepForDownloads()
    {
        keptForDownloads = true;
        var host = Host();
        if (host != null)
        {
            host->was_hidden(host, 1);
            host->set_audio_muted(host, 1);
            CefRefCounting.ReleaseCef(host);
        }

        var frame = browser->get_main_frame(browser);
        if (frame == null)
        {
            return;
        }

        var blank = new CefStringUtf16();
        try
        {
            CefStrings.Set(&blank, Blank);
            frame->load_url(frame, &blank);
        }
        finally
        {
            CefStrings.Clear(&blank);
            CefRefCounting.ReleaseCef(frame);
        }
    }

    /// <summary>Closes a page kept for its downloads: the last is done, or CEF shuts down.</summary>
    internal void CloseKept()
    {
        if (!keptForDownloads)
        {
            return;
        }

        keptForDownloads = false;
        if (browser != null)
        {
            Close();
        }
    }

    /// <summary>The page is about to make a request: the view decides, on the UI loop.</summary>
    internal void OnResourceRequested(WebResourceRequestedEventArgs asked) => view.ResourceRequested(this, asked);

    private void Close()
    {
        var host = Host();
        if (host == null)
        {
            return;
        }

        host->close_browser(host, 1);
        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>Lets go of a browser CEF did not close before it shut down: CEF and its objects are gone.</summary>
    internal void Abandon()
    {
        devToolsSocket?.Dispose();
        devToolsSocket = null;
        devTools.Abandon();
        browser = null;
    }

    /// <summary>Shows <paramref name="html"/> as the page.</summary>
    public void LoadHtml(string html) =>
        Navigate("data:text/html;charset=utf-8;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(html)));

    /// <summary>Runs <paramref name="script"/> in the page; its value as JSON.</summary>
    public Task<string> EvaluateAsync(string script)
    {
        var host = LiveHost();
        if (host == null)
        {
            return Task.FromException<string>(new WebViewScriptException("The page is not there yet."));
        }

        try
        {
            return devTools.EvaluateAsync(host, script);
        }
        finally
        {
            CefRefCounting.ReleaseCef(host);
        }
    }

    /// <summary>Runs <paramref name="script"/> in the page's script world <paramref name="context"/>; its value as
    /// JSON.</summary>
    internal Task<string> EvaluateInAsync(string context, string script)
    {
        var host = LiveHost();
        if (host == null)
        {
            return Task.FromException<string>(new WebViewScriptException("The page went away."));
        }

        try
        {
            return devTools.EvaluateInAsync(host, context, script);
        }
        finally
        {
            CefRefCounting.ReleaseCef(host);
        }
    }

    /// <summary>Hands the page the answer to its call of an object of the application's, into the script world it
    /// called from.</summary>
    internal void AnswerHost(string context, string json)
    {
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        devTools.AnswerHost(host, context, json);
        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>Whether the page's script world <paramref name="context"/> is still there.</summary>
    internal bool HasContext(string context) => !closing && devTools.HasContext(context);

    /// <summary>Hands the page a message, as JSON.</summary>
    public void Post(string json)
    {
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        devTools.Post(host, json);
        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>The page is about to go to <paramref name="url"/>: true keeps it from going. Chromium going to the failed
    /// address by itself is its own page for the error loading: not a navigation of the page's, and the error stays.
    /// Any other going on ends the last load's error.</summary>
    internal bool OnBeforeBrowse(string url, bool userGesture, bool isRedirect)
    {
        if (closing)
        {
            return false;
        }

        if (!userGesture && url == errorPageFor)
        {
            errorPageFor = null;
            return false;
        }

        if (OfTheBlankPage(url))
        {
            return false;
        }

        if (view.NavigationStarting(this, url, userGesture, isRedirect))
        {
            if (address != null)
            {
                view.AddressChanged(this, address);
            }

            return true;
        }

        errorPageFor = null;
        failedAddress = null;
        view.LoadFailed(this, null);
        return false;
    }

    internal void OnLoadEnd(string url)
    {
        var host = isDevTools ? null : LiveHost();
        if (host != null)
        {
            devTools.Prepare(host);
            CefRefCounting.ReleaseCef(host);
        }

        // The failed load and Chromium's page for its error end too: completed already, with the error.
        if (url == failedAddress || OfTheBlankPage(url))
        {
            return;
        }

        errorPageFor = null;
        view.NavigationCompleted(this, url, null);
    }

    // Over the dropped list a pointer is where the list really is: moved into the view, it is shown apart from where
    // the page placed it, and the page is told the place it knows. A drag is the page's, not the list's.
    private (double X, double Y) OnTheList(double x, double y) =>
        popupShown && popupShownAt.Contains(new Vector2<double>(x, y))
            ? (x + popupMovedBy.X, y + popupMovedBy.Y)
            : (x, y);

    private CefMouseEvent MouseEvent(double x, double y, InputModifiers modifiers)
    {
        (x, y) = OnTheList(x, y);
        return DragEvent(x, y, modifiers);
    }

    private static CefMouseEvent DragEvent(double x, double y, InputModifiers modifiers) => new()
    {
        x = (int)Math.Round(x),
        y = (int)Math.Round(y),
        modifiers = CefInputMap.Flags(modifiers)
    };

    private CefBrowserHost* Host() => browser == null ? null : browser->get_host(browser);

    private CefBrowserHost* LiveHost() => closing ? null : Host();
}
