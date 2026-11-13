using Adamantium.UI.Core;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.WebView.Cef;

namespace Adamantium.UI.WebView;

/// <summary>The view's page as a browser shows it: its address both ways, title, loading and how far it has come, the
/// link under the pointer, why it failed to load, and going back, forward, again and stopping.</summary>
public partial class WebView
{
    public static readonly AdamantiumProperty TitleProperty = AdamantiumProperty.Register(nameof(Title), typeof(string),
        typeof(WebView), new PropertyMetadata(string.Empty));

    public static readonly AdamantiumProperty IsLoadingProperty = AdamantiumProperty.Register(nameof(IsLoading),
        typeof(bool), typeof(WebView), new PropertyMetadata(false));

    public static readonly AdamantiumProperty LoadProgressProperty = AdamantiumProperty.Register(nameof(LoadProgress),
        typeof(double), typeof(WebView), new PropertyMetadata(0d));

    public static readonly AdamantiumProperty CanGoBackProperty = AdamantiumProperty.Register(nameof(CanGoBack),
        typeof(bool), typeof(WebView), new PropertyMetadata(false));

    public static readonly AdamantiumProperty CanGoForwardProperty = AdamantiumProperty.Register(nameof(CanGoForward),
        typeof(bool), typeof(WebView), new PropertyMetadata(false));

    public static readonly AdamantiumProperty StatusTextProperty = AdamantiumProperty.Register(nameof(StatusText),
        typeof(string), typeof(WebView), new PropertyMetadata(string.Empty));

    public static readonly AdamantiumProperty LoadErrorProperty = AdamantiumProperty.Register(nameof(LoadError),
        typeof(WebViewLoadError), typeof(WebView), new PropertyMetadata(null));

    public static readonly AdamantiumProperty ZoomFactorProperty = AdamantiumProperty.Register(nameof(ZoomFactor),
        typeof(double), typeof(WebView), new PropertyMetadata(1d, OnZoomFactorChanged));

    public static readonly AdamantiumProperty ZoomPercentProperty = AdamantiumProperty.Register(nameof(ZoomPercent),
        typeof(int), typeof(WebView), new PropertyMetadata(100));

    public static readonly AdamantiumProperty IconProperty = AdamantiumProperty.Register(nameof(Icon),
        typeof(ImageSource), typeof(WebView), new PropertyMetadata(null));

    public static readonly AdamantiumProperty IsSecureProperty = AdamantiumProperty.Register(nameof(IsSecure),
        typeof(bool), typeof(WebView), new PropertyMetadata(false));

    private const double MinZoom = 0.25;
    private const double MaxZoom = 5;
    private static readonly double[] ZoomSteps = [0.25, 0.33, 0.5, 0.67, 0.75, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4, 5];

    private bool followingPage;
    private bool followingZoom;

    /// <summary>The page's title, set by the view.</summary>
    public string Title
    {
        get => GetValue<string>(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Whether the page is loading, set by the view.</summary>
    public bool IsLoading
    {
        get => GetValue<bool>(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    /// <summary>How far the page has loaded, 0 to 1, set by the view.</summary>
    public double LoadProgress
    {
        get => GetValue<double>(LoadProgressProperty);
        set => SetValue(LoadProgressProperty, value);
    }

    /// <summary>Whether there is a page to go back to, set by the view.</summary>
    public bool CanGoBack
    {
        get => GetValue<bool>(CanGoBackProperty);
        set => SetValue(CanGoBackProperty, value);
    }

    /// <summary>Whether there is a page to go forward to, set by the view.</summary>
    public bool CanGoForward
    {
        get => GetValue<bool>(CanGoForwardProperty);
        set => SetValue(CanGoForwardProperty, value);
    }

    /// <summary>The address of the link under the pointer; empty for none. Set by the view.</summary>
    public string StatusText
    {
        get => GetValue<string>(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    /// <summary>Why the page failed to load; null while it loads or has loaded. Set by the view.</summary>
    public WebViewLoadError LoadError
    {
        get => GetValue<WebViewLoadError>(LoadErrorProperty);
        set => SetValue(LoadErrorProperty, value);
    }

    /// <summary>How many times its size the page is shown, from 0.25 to 5; Ctrl with the wheel steps it as a browser
    /// does. Chromium keeps it for the page's site, so going to another site shows that site's.</summary>
    public double ZoomFactor
    {
        get => GetValue<double>(ZoomFactorProperty);
        set => SetValue(ZoomFactorProperty, value);
    }

    /// <summary>The page's icon, as a tab shows it: 16 device-independent pixels across, drawn at the screen's scale;
    /// null while a page loads and for a page without one. Set by the view.</summary>
    public ImageSource Icon
    {
        get => GetValue<ImageSource>(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Whether the page came over a connection with nothing wrong: encrypted, its certificate good, nothing of
    /// it loaded unencrypted. False for an unencrypted page and before one is shown. Set by the view.</summary>
    public bool IsSecure
    {
        get => GetValue<bool>(IsSecureProperty);
        set => SetValue(IsSecureProperty, value);
    }

    /// <summary>Goes back a page; disabled when there is none.</summary>
    public WebViewCommand GoBackCommand { get; private set; }

    /// <summary>Goes forward a page; disabled when there is none.</summary>
    public WebViewCommand GoForwardCommand { get; private set; }

    /// <summary>Loads the page again.</summary>
    public WebViewCommand ReloadCommand { get; private set; }

    /// <summary>Stops loading the page; disabled when it is not loading.</summary>
    public WebViewCommand StopCommand { get; private set; }

    /// <summary>Shows the page a step larger, as Ctrl with the wheel does.</summary>
    public WebViewCommand ZoomInCommand { get; private set; }

    /// <summary>Shows the page a step smaller, as Ctrl with the wheel does.</summary>
    public WebViewCommand ZoomOutCommand { get; private set; }

    /// <summary>Shows the page at its size.</summary>
    public WebViewCommand ResetZoomCommand { get; private set; }

    /// <summary>The zoom as a whole percentage, as the status bar shows it; set by the view.</summary>
    public int ZoomPercent
    {
        get => GetValue<int>(ZoomPercentProperty);
        set => SetValue(ZoomPercentProperty, value);
    }

    private void MakeCommands()
    {
        GoBackCommand = new WebViewCommand(() => browser?.GoBack(), () => browser != null && CanGoBack);
        GoForwardCommand = new WebViewCommand(() => browser?.GoForward(), () => browser != null && CanGoForward);
        ReloadCommand = new WebViewCommand(() => browser?.Reload(), () => browser != null);
        StopCommand = new WebViewCommand(() => browser?.Stop(), () => browser != null && IsLoading);
        ZoomInCommand = new WebViewCommand(() => StepZoom(true), () => ZoomFactor < MaxZoom - 1e-3);
        ZoomOutCommand = new WebViewCommand(() => StepZoom(false), () => ZoomFactor > MinZoom + 1e-3);
        ResetZoomCommand = new WebViewCommand(() => SetCurrentValue(ZoomFactorProperty, 1d), () => true);
        MakeFindCommands();
    }

    private void RaiseCommandsChanged()
    {
        GoBackCommand.RaiseCanExecuteChanged();
        GoForwardCommand.RaiseCanExecuteChanged();
        ReloadCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        RaiseFindCommandsChanged();
    }

    private void ForgetPage()
    {
        SetCurrentValue(TitleProperty, string.Empty);
        SetCurrentValue(IsLoadingProperty, false);
        SetCurrentValue(LoadProgressProperty, 0d);
        SetCurrentValue(CanGoBackProperty, false);
        SetCurrentValue(CanGoForwardProperty, false);
        SetCurrentValue(StatusTextProperty, string.Empty);
        SetCurrentValue(LoadErrorProperty, null);
        ShowIcon(null);
        HidePageTip();
        SetCurrentValue(IsSecureProperty, false);
        ForgetFindResult();
        RaiseCommandsChanged();
    }

    void IWebViewBrowserView.IconChanged(WebViewBrowser from, ImageSource icon)
    {
        if (ReferenceEquals(browser, from))
        {
            ShowIcon(icon);
        }
        else
        {
            icon?.Dispose();
        }
    }

    // The icons are the view's own, made for it from the page's: the one replaced goes, its texture with it once no
    // frame draws it.
    private void ShowIcon(ImageSource icon)
    {
        var previous = Icon;
        SetCurrentValue(IconProperty, icon);
        if (!ReferenceEquals(previous, icon))
        {
            previous?.Dispose();
        }
    }

    void IWebViewBrowserView.SecurityChanged(WebViewBrowser from, bool isSecure)
    {
        if (ReferenceEquals(browser, from))
        {
            SetCurrentValue(IsSecureProperty, isSecure);
        }
    }

    void IWebViewBrowserView.AddressChanged(WebViewBrowser from, string address)
    {
        if (!ReferenceEquals(browser, from) || !Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            Equals(Source, uri))
        {
            return;
        }

        followingPage = true;
        try
        {
            SetCurrentValue(SourceProperty, uri);
        }
        finally
        {
            followingPage = false;
        }
    }

    void IWebViewBrowserView.TitleChanged(WebViewBrowser from, string title)
    {
        if (ReferenceEquals(browser, from))
        {
            SetCurrentValue(TitleProperty, title ?? string.Empty);
        }
    }

    void IWebViewBrowserView.LoadingChanged(WebViewBrowser from, bool isLoading, bool canGoBack, bool canGoForward)
    {
        if (!ReferenceEquals(browser, from))
        {
            return;
        }

        SetCurrentValue(IsLoadingProperty, isLoading);
        SetCurrentValue(CanGoBackProperty, canGoBack);
        SetCurrentValue(CanGoForwardProperty, canGoForward);
        RaiseCommandsChanged();
    }

    void IWebViewBrowserView.ProgressChanged(WebViewBrowser from, double progress)
    {
        if (ReferenceEquals(browser, from))
        {
            SetCurrentValue(LoadProgressProperty, Math.Clamp(progress, 0, 1));
        }
    }

    void IWebViewBrowserView.StatusChanged(WebViewBrowser from, string status)
    {
        if (ReferenceEquals(browser, from))
        {
            SetCurrentValue(StatusTextProperty, status ?? string.Empty);
        }
    }

    void IWebViewBrowserView.ZoomChanged(WebViewBrowser from, double factor)
    {
        if (!ReferenceEquals(browser, from))
        {
            return;
        }

        followingZoom = true;
        try
        {
            SetCurrentValue(ZoomFactorProperty, Math.Round(factor, 4));
        }
        finally
        {
            followingZoom = false;
        }
    }

    private static void OnZoomFactorChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        var view = (WebView)component;
        if (e.NewValue is not double factor)
        {
            return;
        }

        var shown = Math.Clamp(factor, MinZoom, MaxZoom);
        view.SetCurrentValue(ZoomPercentProperty, (int)Math.Round(shown * 100));
        view.ZoomInCommand?.RaiseCanExecuteChanged();
        view.ZoomOutCommand?.RaiseCanExecuteChanged();
        if (!view.followingZoom)
        {
            view.browser?.SetZoom(shown);
        }
    }

    private void StepZoom(bool larger)
    {
        var current = ZoomFactor;
        var next = larger
            ? ZoomSteps.FirstOrDefault(step => step > current + 1e-3, MaxZoom)
            : ZoomSteps.LastOrDefault(step => step < current - 1e-3, MinZoom);
        SetCurrentValue(ZoomFactorProperty, next);
    }

    void IWebViewBrowserView.LoadFailed(WebViewBrowser from, WebViewLoadError error)
    {
        if (ReferenceEquals(browser, from))
        {
            SetCurrentValue(LoadErrorProperty, error);
        }
    }
}
