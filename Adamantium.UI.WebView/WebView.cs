using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.WebView.Cef;
using Adamantium.UI.WebView.Themes;

namespace Adamantium.UI.WebView;

/// <summary>
/// A web page drawn by Chromium (CEF) off screen into a texture of ours and shown by the panel of its template
/// (PART_Surface) - no window of its own, so popups, clipping, transforms and transparency work over and around it.
/// The page's browser is made once the view is in a window and has an <see cref="Engine"/>; its address is
/// <see cref="Source"/>.
/// </summary>
public class WebView : Control
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
    private WebViewBrowser browser;
    private IWindow window;
    private bool attached;
    private uint frameWidth;
    private uint frameHeight;

    static WebView()
    {
        AddStyleSetToThemes<WebViewStyleSet>();
    }

    public WebView()
    {
        SizeChanged += OnSizeChanged;
    }

    /// <summary>The page's address.</summary>
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
        StartBrowser();
    }

    public override void OnRemoveTemplate()
    {
        StopBrowser();
        surface = null;
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

    protected override Size<double> ArrangeOverride(Size<double> finalSize)
    {
        var scale = Scale();
        var size = frameWidth > 0 && frameHeight > 0
            ? new Size<double>(frameWidth / scale, frameHeight / scale)
            : finalSize;
        foreach (var visual in VisualChildren)
        {
            ((IMeasurableComponent)visual).Arrange(new Rect(size));
        }

        return finalSize;
    }

    private static void OnSourceChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        var view = (WebView)component;
        if (view.browser != null && e.NewValue is Uri address)
        {
            view.browser.Navigate(address.AbsoluteUri);
        }
    }

    private static void OnBrowserSettingChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        var view = (WebView)component;
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

    private void StartBrowser()
    {
        if (browser != null || !attached || surface == null || Engine is not { IsAvailable: true } engine ||
            ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        try
        {
            WebViewBrowser made = null;
            made = browser = engine.CreateBrowser(AllowSharedTexture,
                descriptor =>
                {
                    if (ReferenceEquals(browser, made))
                    {
                        Show(descriptor);
                    }
                },
                () =>
                {
                    if (ReferenceEquals(browser, made))
                    {
                        LetGo();
                    }
                },
                () =>
                {
                    if (ReferenceEquals(browser, made))
                    {
                        RequestFrame();
                    }
                },
                rate =>
                {
                    if (ReferenceEquals(browser, made))
                    {
                        SetCurrentValue(FrameRateProperty, rate);
                    }
                },
                () => RestartOnPixels(made));
            SetCurrentValue(FramePathProperty, browser.FramePath);
            browser.Create(Source?.AbsoluteUri ?? "about:blank", (int)Math.Ceiling(ActualWidth),
                (int)Math.Ceiling(ActualHeight), Scale(), window?.Handle ?? IntPtr.Zero);
            browser.SetHidden(IsWindowOutOfSight());
        }
        catch (Exception exception)
        {
            Serilog.Log.Logger.Error(exception, "The WebView's page could not be started");
            StopBrowser();
        }
    }

    private void StopBrowser()
    {
        if (browser == null)
        {
            return;
        }

        browser.Dispose();
        browser = null;
        SetCurrentValue(FramePathProperty, WebViewFramePath.None);
        SetCurrentValue(FrameRateProperty, 0);
    }

    private void RestartOnPixels(WebViewBrowser refused)
    {
        if (!ReferenceEquals(browser, refused))
        {
            return;
        }

        StopBrowser();
        StartBrowser();
    }

    private void LetGo()
    {
        surface?.ClearSource();
        frameWidth = 0;
        frameHeight = 0;
        InvalidateArrange();
    }

    private void ResizeBrowser() =>
        browser?.Resize((int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight), Scale());

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
        InvalidateArrange();
    }

    private void OnWindowStateChanged(object sender, StateChangedEventArgs e) => browser?.SetHidden(IsWindowOutOfSight());

    private bool IsWindowOutOfSight() => window?.State == WindowState.Minimized;

    private void Show(SharedSurfaceDescriptor descriptor)
    {
        frameWidth = descriptor.Width;
        frameHeight = descriptor.Height;
        InvalidateArrange();
        surface?.SetSource(descriptor);
    }

    private void RequestFrame()
    {
        if (window is WindowBase drawn)
        {
            (drawn.Renderer ?? drawn.DefaultRenderer)?.RequestFrame();
        }
    }

    private float Scale() => window is WindowBase scaled ? (float)scaled.DpiScale.X : 1;
}
