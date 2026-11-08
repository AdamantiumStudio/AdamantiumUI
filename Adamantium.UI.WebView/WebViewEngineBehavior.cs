using Adamantium.UI.Core.Behaviors;

namespace Adamantium.UI.WebView;

/// <summary>Gives the <see cref="WebView"/> it is attached to the application's <see cref="WebViewEngine"/>, when the
/// view has none of its own - the view itself reaches no service.</summary>
public class WebViewEngineBehavior : Behavior<WebView>
{
    protected override void OnAttached(WebView view)
    {
        base.OnAttached(view);
        if (view.Engine == null && UIApplication.Current?.Container is { } container &&
            container.IsRegistered<WebViewEngine>())
        {
            view.SetCurrentValue(WebView.EngineProperty, container.Resolve<WebViewEngine>());
        }
    }
}
