using Adamantium.Graphics.Core;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
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

    private readonly WebViewEngine engine;
    private readonly IWebViewBrowserView view;
    private readonly WebViewClient client;
    private readonly WebViewRenderHandler renderHandler;
    private readonly WebViewLifeSpanHandler lifeSpanHandler;
    private readonly WebViewDisplayHandler displayHandler;
    private readonly WebViewLoadHandler loadHandler;
    private readonly WebViewRequestHandler requestHandler;
    private readonly WebViewDevTools devTools;
    private readonly WebViewFrameProducer producer;
    private CefBrowser* browser;
    private string address;
    private string wanted;
    private string errorPageFor;
    private string failedAddress;
    private bool isDevTools;
    private WebViewDevToolsSocket devToolsSocket;
    private string createdWith;
    private bool requested;
    private bool closing;
    private bool disposed;
    private bool repaintOwed;
    private bool hidden;
    private bool focused;
    private double zoom = 1;
    private bool refused;
    private int framesCounted;
    private long countedSince;

    public WebViewBrowser(WebViewEngine engine, IGraphicsDevice device, WebViewFramePath framePath,
        IWebViewBrowserView view)
    {
        this.engine = engine;
        this.view = view;
        FramePath = framePath;
        producer = new WebViewFrameProducer(device, descriptor => view.Show(this, descriptor), () =>
        {
            framesCounted++;
            view.FrameWritten(this);
        });
        renderHandler = new WebViewRenderHandler(this);
        lifeSpanHandler = new WebViewLifeSpanHandler(this);
        loadHandler = new WebViewLoadHandler(this);
        requestHandler = new WebViewRequestHandler(this);
        devTools = new WebViewDevTools(json => view.MessageReceived(this, json));
        displayHandler = new WebViewDisplayHandler(this);
        client = new WebViewClient(renderHandler, lifeSpanHandler, displayHandler, loadHandler, requestHandler);
    }

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

    /// <summary>Asks CEF to make the browser, showing <paramref name="url"/>.</summary>
    public void Create(string url, int width, int height, float scale, IntPtr parentWindow)
    {
        address = url;
        createdWith = url;
        ViewSize = (Math.Max(1, width), Math.Max(1, height));
        Scale = scale;

        var info = CefWindowInfos.Windowless(parentWindow, FramePath == WebViewFramePath.SharedTexture);
        var settings = new CefBrowserSettings
        {
            size = (ulong)sizeof(CefBrowserSettings),
            windowless_frame_rate = 60
        };
        var target = new CefStringUtf16();
        try
        {
            CefStrings.Set(&target, url);
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

    /// <summary>Shows <paramref name="url"/>: at once in a browser already made, else once it is.</summary>
    public void Navigate(string url)
    {
        errorPageFor = null;
        if (browser == null || closing)
        {
            wanted = url;
            return;
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
        this.hidden = hidden;
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        host->was_hidden(host, hidden ? 1 : 0);
        CefRefCounting.ReleaseCef(host);
    }

    /// <summary>Asks the page again for a frame no surface was free for, once the panel has given one back.</summary>
    public void RepaintIfOwed()
    {
        if (!repaintOwed || closing || !producer.HasFreeSurface)
        {
            return;
        }

        repaintOwed = false;        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        host->invalidate(host, CefPaintElementType.View);
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

        var mouse = MouseEvent(x, y, modifiers);
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

        var mouse = MouseEvent(x, y, modifiers);
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

        var mouse = MouseEvent(x, y, modifiers);
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

    /// <summary>Goes back a page in the browser's history.</summary>
    public void GoBack()
    {
        errorPageFor = null;
        if (browser != null && !closing)
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
        address = url;
        view.AddressChanged(this, url);
    }

    internal void OnTitleChange(string title) => view.TitleChanged(this, title);

    internal void OnStatusMessage(string message) => view.StatusChanged(this, message);

    internal void OnLoadingProgress(double progress) => view.ProgressChanged(this, progress);

    internal void OnLoadingState(bool isLoading, bool canGoBack, bool canGoForward)
    {
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
        }

        view.LoadingChanged(this, isLoading, canGoBack, canGoForward);
    }

    internal void OnLoadError(WebViewLoadError error)
    {
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
        // DevTools are not a page of the application's: no protocol channel, no window.adamantium in them.
        if (!isDevTools)
        {
            var host = Host();
            devTools.Attach(host);
            CefRefCounting.ReleaseCef(host);
        }

        if (closing)
        {
            Close();
        }
        else
        {
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

            if (wanted != null && wanted != createdWith)
            {
                Navigate(wanted);
            }

            wanted = null;
        }
    }

    /// <summary>A frame from CEF; one no surface was free for is asked for again by <see cref="RepaintIfOwed"/>.</summary>
    internal void OnPaint(ReadOnlySpan<CefRect> dirty, byte* pixels, int width, int height)
    {
        if (closing)
        {
            return;
        }

        if (!producer.Write(dirty, pixels, width, height))
        {
            repaintOwed = true;
        }
    }

    /// <summary>A frame in Chromium's <paramref name="texture"/>, copied before the call returns, when CEF takes the
    /// texture back. One that cannot be copied - not BGRA, its visible part not at the corner, or not taken by the
    /// device - ends the shared path: the engine starts no more browsers on it, and the view makes this one again on
    /// pixels in memory.</summary>
    internal void OnAcceleratedPaint(ReadOnlySpan<CefRect> dirty, IntPtr texture, CefColorType format, int textureWidth,
        int textureHeight, CefRect visible)
    {
        if (closing || refused)
        {
            return;
        }

        try
        {
            if (format != CefColorType.Bgra8888 || visible.x != 0 || visible.y != 0)
            {
                throw new NotSupportedException($"A {format} texture whose visible part is at {visible.x},{visible.y}.");
            }

            if (!producer.Copy(dirty, texture, textureWidth, textureHeight, visible.width, visible.height))
            {
                repaintOwed = true;
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

    /// <summary>A page opening a window of its own - a link to a new tab, window.open: the view shows it instead when
    /// the user asked for it; a script's own is refused.</summary>
    internal void OnPopup(string url, bool userGesture)
    {
        if (userGesture && !closing && !string.IsNullOrEmpty(url))
        {
            Navigate(url);
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
        view.LetGo(this);
        producer.Dispose();
        if (browser != null)
        {
            Close();
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
    }

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
        if (url == failedAddress)
        {
            return;
        }

        errorPageFor = null;
        view.NavigationCompleted(this, url, null);
    }

    private static CefMouseEvent MouseEvent(double x, double y, InputModifiers modifiers) => new()
    {
        x = (int)Math.Round(x),
        y = (int)Math.Round(y),
        modifiers = CefInputMap.Flags(modifiers)
    };

    private CefBrowserHost* Host() => browser == null ? null : browser->get_host(browser);

    private CefBrowserHost* LiveHost() => closing ? null : Host();
}
