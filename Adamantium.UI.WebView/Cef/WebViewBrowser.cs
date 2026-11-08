using Adamantium.Graphics.Core;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>
/// One CEF browser drawn off screen, for one <see cref="WebView"/>: made with the view's size and scale, told when they
/// change, its frames written by a <see cref="WebViewFrameProducer"/> on the engine's device. CEF makes the browser
/// asynchronously; an address set before it is made is loaded once it is. Lives on the UI loop, where CEF calls back.
/// </summary>
internal sealed unsafe class WebViewBrowser : IDisposable
{
    private readonly WebViewEngine engine;
    private readonly Action letGo;
    private readonly WebViewClient client;
    private readonly WebViewRenderHandler renderHandler;
    private readonly WebViewLifeSpanHandler lifeSpanHandler;
    private readonly WebViewFrameProducer producer;
    private CefBrowser* browser;
    private string address;
    private string createdWith;
    private bool requested;
    private bool closing;
    private bool disposed;
    private bool repaintOwed;
    private bool hidden;

    public WebViewBrowser(WebViewEngine engine, IGraphicsDevice device, Action<SharedSurfaceDescriptor> show,
        Action letGo, Action frameWritten)
    {
        this.engine = engine;
        this.letGo = letGo;
        producer = new WebViewFrameProducer(device, show, frameWritten);
        renderHandler = new WebViewRenderHandler(this);
        lifeSpanHandler = new WebViewLifeSpanHandler(this);
        client = new WebViewClient(renderHandler, lifeSpanHandler);
    }

    /// <summary>The view's size in device-independent pixels, never empty.</summary>
    public (int Width, int Height) ViewSize { get; private set; } = (1, 1);

    /// <summary>The screen's pixels per device-independent pixel.</summary>
    public float Scale { get; private set; } = 1;

    /// <summary>Asks CEF to make the browser, showing <paramref name="url"/>.</summary>
    public void Create(string url, int width, int height, float scale, IntPtr parentWindow)
    {
        address = url;
        createdWith = url;
        ViewSize = (Math.Max(1, width), Math.Max(1, height));
        Scale = scale;

        var info = CefWindowInfos.Windowless(parentWindow);
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
        address = url;
        if (browser == null || closing)
        {
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

        repaintOwed = false;
        var host = LiveHost();
        if (host == null)
        {
            return;
        }

        host->invalidate(host, CefPaintElementType.View);
        CefRefCounting.ReleaseCef(host);
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

            if (address != createdWith)
            {
                Navigate(address);
            }
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
        closing = true;
        letGo();
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
        browser = null;
    }

    private CefBrowserHost* Host() => browser == null ? null : browser->get_host(browser);

    private CefBrowserHost* LiveHost() => closing ? null : Host();
}
