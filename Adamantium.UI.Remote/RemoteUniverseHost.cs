using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Behaviors;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Remote;

/// <summary>Shows a universe of another process, connected and owned elsewhere - by a view-model - in the
/// <see cref="RenderTargetPanel"/> it is attached to; while it shows none, or is detached, the universe is shown nowhere.
/// <code>
/// &lt;RenderTargetPanel.Behaviors&gt;&lt;remote:RemoteUniverseHost Universe="{Binding Game}"/&gt;&lt;/RenderTargetPanel.Behaviors&gt;
/// </code>
/// </summary>
public class RemoteUniverseHost : Behavior<RenderTargetPanel>
{
    private RenderTargetPanel _panel;
    private IRemoteUniverse _shown;

    public static readonly AdamantiumProperty UniverseProperty = AdamantiumProperty.Register(nameof(Universe),
        typeof(IRemoteUniverse), typeof(RemoteUniverseHost), new PropertyMetadata(null, OnUniverseChanged));

    /// <summary>The universe to show, or null for none.</summary>
    public IRemoteUniverse Universe
    {
        get => GetValue<IRemoteUniverse>(UniverseProperty);
        set => SetValue(UniverseProperty, value);
    }

    private static void OnUniverseChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        ((RemoteUniverseHost)component).Sync();
    }

    protected override void OnAttached(RenderTargetPanel panel)
    {
        _panel = panel;
        Sync();
    }

    protected override void OnDetached(RenderTargetPanel panel)
    {
        Withdraw();
        _panel = null;
    }

    private void Sync()
    {
        var universe = _panel == null ? null : Universe;
        if (ReferenceEquals(universe, _shown))
        {
            return;
        }

        Withdraw();
        _shown = universe;
        universe?.ShowIn(_panel);
    }

    // Only from this panel: a universe shown in another since stays there.
    private void Withdraw()
    {
        if (_shown != null && ReferenceEquals(_shown.Panel, _panel))
        {
            _shown.ShowIn(null);
        }

        _shown = null;
    }
}
