using System.Linq;
using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.WebView;

/// <summary>Whether a <see cref="WebView"/> takes the application's <see cref="WebViewEngine"/> when it has none of its
/// own - through <see cref="WebViewEngineBehavior"/>. On in the view's default style.</summary>
public static class WebViewEngines
{
    public static readonly AdamantiumProperty UseApplicationEngineProperty = AdamantiumProperty.RegisterAttached(
        "UseApplicationEngine", typeof(bool), typeof(AdamantiumComponent),
        new PropertyMetadata(false, OnUseApplicationEngineChanged));

    public static bool GetUseApplicationEngine(AdamantiumComponent element) =>
        element.GetValue<bool>(UseApplicationEngineProperty);

    public static void SetUseApplicationEngine(AdamantiumComponent element, bool value) =>
        element.SetValue(UseApplicationEngineProperty, value);

    private static void OnUseApplicationEngineChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is not WebView view)
        {
            return;
        }

        var attached = view.Behaviors.OfType<WebViewEngineBehavior>().FirstOrDefault();
        if (e.NewValue is true && attached == null)
        {
            view.Behaviors.Add(new WebViewEngineBehavior());
        }
        else if (e.NewValue is not true && attached != null)
        {
            view.Behaviors.Remove(attached);
        }
    }
}
