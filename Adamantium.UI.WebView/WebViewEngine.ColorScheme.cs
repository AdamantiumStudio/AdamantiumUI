using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.WebView.Cef;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView;

public sealed unsafe partial class WebViewEngine
{
    private WebViewColorScheme colorScheme;
    private IThemeManager followedThemes;
    private ITheme followedTheme;

    /// <summary>The color scheme every page is asked in; <see cref="WebViewOptions.ColorScheme"/> at first. Set, the pages
    /// open now are asked again at once; the application's scheme follows its theme as it changes. On the UI thread.</summary>
    public WebViewColorScheme ColorScheme
    {
        get => colorScheme;
        set
        {
            if (colorScheme == value)
            {
                return;
            }

            colorScheme = value;
            AskColorSchemeAgain();
        }
    }

    /// <summary>The scheme pages are asked in now: the application's taken as light or dark.</summary>
    internal WebViewColorScheme PagesColorScheme =>
        colorScheme != WebViewColorScheme.Application ? colorScheme
        : followedTheme?.CurrentVariant == ThemeVariant.Dark ? WebViewColorScheme.Dark
        : WebViewColorScheme.Light;

    // The theme and its variant are followed for as long as the engine lives, whatever the scheme: set to the
    // application's later, it is right at once.
    private void FollowApplicationTheme()
    {
        followedThemes = application?.ThemeManager;
        if (followedThemes == null)
        {
            return;
        }

        followedThemes.ThemeChanged += OnApplicationThemeChanged;
        FollowTheme(followedThemes.CurrentTheme);
    }

    private void StopFollowingApplicationTheme()
    {
        if (followedThemes != null)
        {
            followedThemes.ThemeChanged -= OnApplicationThemeChanged;
            followedThemes = null;
        }

        FollowTheme(null);
    }

    private void FollowTheme(ITheme theme)
    {
        if (ReferenceEquals(followedTheme, theme))
        {
            return;
        }

        if (followedTheme != null)
        {
            followedTheme.PropertyChanged -= OnApplicationThemePropertyChanged;
        }

        followedTheme = theme;
        if (followedTheme != null)
        {
            followedTheme.PropertyChanged += OnApplicationThemePropertyChanged;
        }
    }

    private void OnApplicationThemeChanged(object sender, ThemeChangedEventArgs e)
    {
        var before = PagesColorScheme;
        FollowTheme(followedThemes?.CurrentTheme);
        if (PagesColorScheme != before)
        {
            AskColorSchemeAgain();
        }
    }

    private void OnApplicationThemePropertyChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property.Name == nameof(ITheme.CurrentVariant) && colorScheme == WebViewColorScheme.Application)
        {
            AskColorSchemeAgain();
        }
    }

    // Chromium's own scheme - what about:blank and its own pages are painted in - and every page open now.
    private void AskColorSchemeAgain()
    {
        if (!initialized || shutDown)
        {
            return;
        }

        ApplyChromeColorScheme();
        foreach (var browser in browsers)
        {
            browser.AskColorScheme(PagesColorScheme);
        }
    }

    private void ApplyChromeColorScheme()
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

        try
        {
            var variant = PagesColorScheme switch
            {
                WebViewColorScheme.Light => CefColorVariant.Light,
                WebViewColorScheme.Dark => CefColorVariant.Dark,
                _ => CefColorVariant.System
            };
            context->set_chrome_color_scheme(context, variant, default);
        }
        finally
        {
            CefRefCounting.ReleaseCef(context);
        }
    }
}
