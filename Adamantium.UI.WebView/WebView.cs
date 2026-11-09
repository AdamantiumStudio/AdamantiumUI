using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Input;
using Adamantium.UI.WebView.Cef;
using Adamantium.UI.WebView.Cef.Interop;
using Adamantium.UI.WebView.Themes;

namespace Adamantium.UI.WebView;

/// <summary>
/// A web page drawn by Chromium (CEF) off screen into a texture of ours and shown by the panel of its template
/// (PART_Surface) - no window of its own, so popups, clipping, transforms and transparency work over and around it.
/// The page's browser is made once the view is in a window and has an <see cref="Engine"/>; its address is
/// <see cref="Source"/>.
/// </summary>
public partial class WebView : Control, IWebViewBrowserView, ITextEntry
{
    public static readonly AdamantiumProperty SourceProperty = AdamantiumProperty.Register(nameof(Source), typeof(Uri),
        typeof(WebView), new PropertyMetadata(null, OnSourceChanged));

    public static readonly AdamantiumProperty EngineProperty = AdamantiumProperty.Register(nameof(Engine),
        typeof(WebViewEngine), typeof(WebView), new PropertyMetadata(null, OnBrowserSettingChanged));

    public static readonly AdamantiumProperty AllowSharedTextureProperty = AdamantiumProperty.Register(
        nameof(AllowSharedTexture), typeof(bool), typeof(WebView), new PropertyMetadata(true, OnBrowserSettingChanged));

    public static readonly AdamantiumProperty FramePathProperty = AdamantiumProperty.Register(nameof(FramePath),
        typeof(WebViewFramePath), typeof(WebView), new PropertyMetadata(WebViewFramePath.None));

    public static readonly AdamantiumProperty FrameRateProperty = AdamantiumProperty.Register(nameof(FrameRate),
        typeof(int), typeof(WebView), new PropertyMetadata(0));

    private RenderTargetPanel surface;
    private InputUIComponent pageHost;
    private WebViewBrowser browser;
    private IWindow window;
    private bool attached;
    private uint frameWidth;
    private uint frameHeight;

    static WebView()
    {
        AddStyleSetToThemes<WebViewStyleSet>();
        FocusableProperty.OverrideMetadata(typeof(WebView), new PropertyMetadata(true));
        DragDrop.AllowDropProperty.OverrideMetadata(typeof(WebView), new PropertyMetadata(true));
    }

    public WebView()
    {
        MakeCommands();
        SizeChanged += OnSizeChanged;
        PointerWheelChanged += OnWheel;
        PointerCanceled += OnPointerCanceled;
        TextInput += OnTyped;
        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;
        Drop += OnDrop;
        AddHandler(DragDropEvents.DragCompletedEvent, new DragDropEventHandler(OnDragCompleted));
        AddHandler(Keyboard.TextCompositionStartedEvent, new TextCompositionEventHandler(OnCompositionStarted));
        AddHandler(Keyboard.TextCompositionChangedEvent, new TextCompositionEventHandler(OnCompositionChanged));
        AddHandler(Keyboard.TextCompositionEndedEvent, new TextCompositionEventHandler(OnCompositionEnded));
    }

    /// <summary>The page's address: set, it is gone to; the page going somewhere of its own - a link, a redirect, back or
    /// forward - sets it in turn.</summary>
    public Uri Source
    {
        get => GetValue<Uri>(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>The CEF of the application the page is drawn by, bound from the view model; no page without it.</summary>
    public WebViewEngine Engine
    {
        get => GetValue<WebViewEngine>(EngineProperty);
        set => SetValue(EngineProperty, value);
    }

    /// <summary>Whether the page's frames may come in Chromium's own textures on the GPU, where the device takes them;
    /// otherwise, and where it does not, they come as pixels in memory. Changing it makes the page again.</summary>
    public bool AllowSharedTexture
    {
        get => GetValue<bool>(AllowSharedTextureProperty);
        set => SetValue(AllowSharedTextureProperty, value);
    }

    /// <summary>How the page's frames come, set by the view.</summary>
    public WebViewFramePath FramePath
    {
        get => GetValue<WebViewFramePath>(FramePathProperty);
        set => SetValue(FramePathProperty, value);
    }

    /// <summary>The page's frames in the last second, set by the view.</summary>
    public int FrameRate
    {
        get => GetValue<int>(FrameRateProperty);
        set => SetValue(FrameRateProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        StopBrowser();
        surface = GetTemplateChild("PART_Surface") as RenderTargetPanel;
        TakePopupSurface(GetTemplateChild("PART_PopupSurface") as RenderTargetPanel);
        pageHost = GetTemplateChild("PART_PageHost") as InputUIComponent;
        if (pageHost != null)
        {
            pageHost.SizeChanged += OnSizeChanged;
            pageHost.PointerExited += OnPageExited;
        }

        TakeDevToolsView(GetTemplateChild("PART_DevTools") as WebViewDevToolsView);
        TakePageMenu(GetTemplateChild("PART_PageMenu") as ContextMenu, GetTemplateChild("PART_PageTipPopup") as Popup,
            GetTemplateChild("PART_PageTip") as ToolTip);
        if (GetTemplateChild("PART_StatusBar") is WebViewStatusBar statusBar)
        {
            statusBar.SetCurrentValue(WebViewStatusBar.ViewProperty, this);
        }

        StartBrowser();
    }

    public override void OnRemoveTemplate()
    {
        TakeDevToolsView(null);
        TakePageMenu(null, null, null);
        StopBrowser();
        if (pageHost != null)
        {
            pageHost.SizeChanged -= OnSizeChanged;
            pageHost.PointerExited -= OnPageExited;
        }

        pageHost = null;
        surface = null;
        TakePopupSurface(null);
        base.OnRemoveTemplate();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        WatchWindow(RootVisual?.Window);
        if (browser != null)
        {
            ResizeBrowser();
            browser.SetHidden(IsWindowOutOfSight());
            return;
        }

        StartBrowser();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        attached = false;
        if (IsParked)
        {
            browser?.SetHidden(true);
        }
        else
        {
            StopBrowser();
        }

        WatchWindow(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDiscarded()
    {
        StopBrowser();
        base.OnDiscarded();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PaneAutomationPeer(this);

    private static void OnSourceChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        var view = (WebView)component;
        if (view.browser != null && !view.followingPage && e.NewValue is Uri address)
        {
            view.browser.Navigate(address.AbsoluteUri);
        }
    }

    private static void OnBrowserSettingChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        var view = (WebView)component;
        // The engine is the application's and outlives the page: an engine gone for a moment - a data context passing
        // through null - leaves the page on the one it has rather than losing it and its history.
        if (e.Property == EngineProperty && e.NewValue == null && view.browser != null)
        {
            return;
        }

        view.StopBrowser();
        view.StartBrowser();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (browser == null)
        {
            StartBrowser();
            return;
        }

        ResizeBrowser();
    }

    private protected void StartBrowser()
    {
        if (browser != null || !attached || surface == null || Engine is not { IsAvailable: true } engine ||
            PageWidth <= 0 || PageHeight <= 0 || !CanStartBrowser)
        {
            return;
        }

        try
        {
            browser = engine.CreateBrowser(AllowSharedTexture, this);
            SetCurrentValue(FramePathProperty, browser.FramePath);
            RaiseCommandsChanged();
            BeginBrowser(browser, (int)Math.Ceiling(PageWidth), (int)Math.Ceiling(PageHeight), Scale(),
                window?.Handle ?? IntPtr.Zero);
            browser.SetHidden(IsWindowOutOfSight());
            browser.SetFocus(IsKeyboardFocused);
            browser.SetZoom(Math.Clamp(ZoomFactor, MinZoom, MaxZoom));
        }
        catch (Exception exception)
        {
            Serilog.Log.Logger.Error(exception, "The WebView's page could not be started");
            StopBrowser();
            return;
        }

        PageChanged();
    }

    private protected void StopBrowser()
    {
        if (browser == null)
        {
            return;
        }

        ForgetInput();
        DismissPageMenu();
        HidePageTip();
        if (pageMenu != null)
        {
            pageMenu.IsOpen = false;
        }

        touching.Clear();
        draggingFromPage = false;
        browser.Dispose();
        browser = null;
        SetCurrentValue(FramePathProperty, WebViewFramePath.None);
        SetCurrentValue(FrameRateProperty, 0);
        ForgetPage();
        PageChanged();
    }

    // The page's own part of the view: all of it, or what the DevTools beside it leave.
    private double PageWidth => pageHost?.ActualWidth ?? ActualWidth;

    private double PageHeight => pageHost?.ActualHeight ?? ActualHeight;

    void IWebViewBrowserView.Show(WebViewBrowser from, SharedSurfaceDescriptor descriptor)
    {
        if (!ReferenceEquals(browser, from))
        {
            return;
        }

        frameWidth = descriptor.Width;
        frameHeight = descriptor.Height;
        SizeSurfaceToFrame();
        surface?.SetSource(descriptor);
    }

    // The panel is laid out at the frame's size in pixels - up to a pixel past the page's part, which clips it - so the
    // frame is drawn one to one, never scaled.
    private void SizeSurfaceToFrame()
    {
        if (surface == null)
        {
            return;
        }

        var scale = Scale();
        var drawn = frameWidth > 0 && frameHeight > 0;
        surface.Width = drawn ? frameWidth / scale : double.NaN;
        surface.Height = drawn ? frameHeight / scale : double.NaN;
    }

    void IWebViewBrowserView.LetGo(WebViewBrowser from)
    {
        if (!ReferenceEquals(browser, from))
        {
            return;
        }

        surface?.ClearSource();
        popupSurface?.ClearSource();
        HidePopup();
        frameWidth = 0;
        frameHeight = 0;
        SizeSurfaceToFrame();
    }

    void IWebViewBrowserView.FrameWritten(WebViewBrowser from)
    {
        if (ReferenceEquals(browser, from) && window is WindowBase drawn)
        {
            (drawn.Renderer ?? drawn.DefaultRenderer)?.RequestFrame();
        }
    }

    void IWebViewBrowserView.FrameRate(WebViewBrowser from, int rate)
    {
        if (ReferenceEquals(browser, from))
        {
            SetCurrentValue(FrameRateProperty, rate);
        }
    }

    void IWebViewBrowserView.SharedTextureRefused(WebViewBrowser from)
    {
        if (!ReferenceEquals(browser, from))
        {
            return;
        }

        StopBrowser();
        StartBrowser();
    }

    void IWebViewBrowserView.CursorChanged(WebViewBrowser from, Cursor cursor)
    {
        if (ReferenceEquals(browser, from))
        {
            SetCurrentValue(CursorProperty, cursor);
        }
    }

    void IWebViewBrowserView.CompositionBoundsChanged(WebViewBrowser from, Rect bounds)
    {
        if (ReferenceEquals(browser, from) && IsKeyboardFocused)
        {
            InputMethod.SetCaretBounds(this, bounds);
        }
    }

    private void ResizeBrowser() =>
        browser?.Resize((int)Math.Ceiling(PageWidth), (int)Math.Ceiling(PageHeight), Scale());

    private void WatchWindow(IWindow next)
    {
        if (ReferenceEquals(window, next))
        {
            return;
        }

        if (window != null)
        {
            window.DpiChanged -= OnWindowDpiChanged;
            window.StateChanged -= OnWindowStateChanged;
        }

        window = next;
        if (window != null)
        {
            window.DpiChanged += OnWindowDpiChanged;
            window.StateChanged += OnWindowStateChanged;
        }
    }

    private void OnWindowDpiChanged(object sender, EventArgs e)
    {
        ResizeBrowser();
        SizeSurfaceToFrame();
    }

    private void OnWindowStateChanged(object sender, StateChangedEventArgs e) => browser?.SetHidden(IsWindowOutOfSight());

    private bool IsWindowOutOfSight() => window?.State == WindowState.Minimized;

    private float Scale() => window is WindowBase scaled ? (float)scaled.DpiScale.X : 1;
}
