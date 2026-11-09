using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.WebView.Themes;

namespace Adamantium.UI.WebView;

/// <summary>
/// The status bar of a <see cref="WebView"/>'s page: the link under the pointer, the zoom with a step either way and back
/// to 100% on a press, and the DevTools shown or put away. A <see cref="WebView"/> shows one under its page
/// (<see cref="WebView.ShowStatusBar"/>); one placed elsewhere - an application's own status line - is given its
/// <see cref="View"/>.
/// </summary>
public class WebViewStatusBar : Control
{
    public static readonly AdamantiumProperty ViewProperty = AdamantiumProperty.Register(nameof(View),
        typeof(WebView), typeof(WebViewStatusBar), new PropertyMetadata(null));

    static WebViewStatusBar()
    {
        AddStyleSetToThemes<WebViewStatusBarStyleSet>();
    }

    // No peer of its own: the theme's status bar inside answers for itself and its fields.
    protected override AutomationPeer OnCreateAutomationPeer() => null;

    /// <summary>The view whose page the bar tells of.</summary>
    public WebView View
    {
        get => GetValue<WebView>(ViewProperty);
        set => SetValue(ViewProperty, value);
    }
}
