using Adamantium.Core.TypeParsing;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Behaviors;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Sandbox.Behaviors;

/// <summary>On click, replaces a keyed brush in a resource scope via <see cref="IResourceManager.SetResourceInScope"/>,
/// showing that <c>{ObservableResource}</c> follows the change while <c>{ResourceReference}</c> does not.</summary>
public class CycleScopedResourceBehavior : Behavior<Button>
{
    private static readonly string[] Colors = ["#F87171", "#FBBF24", "#34D399", "#60A5FA", "#C084FC"];
    private int _index;

    /// <summary>The resource key to cycle.</summary>
    public string Key { get; set; }

    /// <summary>The scope whose dictionary declares <see cref="Key"/> (Theme for a palette entry, Global for an
    /// app-wide resource).</summary>
    public ResourceScope Scope { get; set; } = ResourceScope.Theme;

    protected override void OnAttached(Button button)
    {
        button.Click += OnClick;
    }

    protected override void OnDetached(Button button)
    {
        button.Click -= OnClick;
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(Key)) return;
        _index = (_index + 1) % Colors.Length;
        UIAppContext.Current?.ResourceManager?.SetResourceInScope(Key, TypeParser.Parse<Brush>(Colors[_index]), Scope);
    }
}
