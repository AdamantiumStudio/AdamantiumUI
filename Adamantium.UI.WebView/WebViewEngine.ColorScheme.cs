using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.WebView.Cef;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView;

public sealed unsafe partial class WebViewEngine
{
    private WebViewColorScheme colorScheme;
    private WebViewColorScheme schemeAsked;
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
            AskColorSchemeIfChanged();
        }
    }

    /// <summary>The scheme pages are asked in now: the application's taken as light or dark.</summary>
    internal WebViewColorScheme PagesColorScheme =>
        colorScheme != WebViewColorScheme.Application ? colorScheme
        : IsDark(followedThemes?.CurrentTheme) ? WebViewColorScheme.Dark
        : WebViewColorScheme.Light;

    // Dark: the theme's dark variant, by the common name or the one the theme says it takes in the system's dark.
    private static bool IsDark(ITheme theme)
    {
        if (theme == null)
        {
            return false;
        }

        var variant = theme.CurrentVariant;
        return variant == ThemeVariant.Dark || (!theme.SystemDarkVariant.IsUnspecified && variant == theme.SystemDarkVariant);
    }

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

    // Told on whatever thread changed it - the system's dark mode comes on the window's own thread on Windows - and
    // answered on the UI loop, where CEF's work runs.
    private void OnApplicationThemeChanged(object sender, ThemeChangedEventArgs e) =>
        LoopSignal.Post(() =>
        {
            FollowTheme(followedThemes?.CurrentTheme);
            AskColorSchemeIfChanged();
        });

    private void OnApplicationThemePropertyChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property == Theme.CurrentVariantProperty)
        {
            LoopSignal.Post(AskColorSchemeIfChanged);
        }
    }

    // Chromium's own scheme - what about:blank and its own pages are painted in - and every page open now, when the
    // scheme they are asked in is another.
    private void AskColorSchemeIfChanged()
    {
        if (!initialized || shutDown || PagesColorScheme == schemeAsked)
        {
            return;
        }

        ApplyChromeColorScheme();
        foreach (var browser in browsers)
        {
            browser.AskColorScheme(schemeAsked);
        }
    }

    private void ApplyChromeColorScheme()
    {
        if (!initialized || shutDown)
        {
            return;
        }

        schemeAsked = PagesColorScheme;
        var context = CefInterop.CefRequestContextGetGlobalContext();
        if (context == null)
        {
            return;
        }

        try
        {
            var variant = schemeAsked switch
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
