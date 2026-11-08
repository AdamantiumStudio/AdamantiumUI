using Adamantium.Core.DependencyInjection;

namespace Adamantium.UI.WebView;

/// <summary>Registers the application's <see cref="WebViewEngine"/>.</summary>
public static class WebViewRegistration
{
    /// <summary>Registers one <see cref="WebViewEngine"/> for the application, with the options
    /// <paramref name="configure"/> sets.</summary>
    public static IContainerRegistry AddWebView(this IContainerRegistry registry, Action<WebViewOptions> configure = null)
    {
        var options = new WebViewOptions();
        configure?.Invoke(options);
        return registry.RegisterInstance<WebViewOptions>(options).RegisterSingleton<WebViewEngine>();
    }
}
