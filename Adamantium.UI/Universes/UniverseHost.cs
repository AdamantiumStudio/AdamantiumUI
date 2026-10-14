using Adamantium.Multiverse;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Behaviors;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Universes;

/// <summary>
/// Shows a universe made and owned elsewhere - by a view-model - in the <see cref="RenderTargetPanel"/> it is attached
/// to. While the panel is in a window, the universe runs in that window's frames and draws into the panel; out of the
/// tree it is paused. When the panel is discarded or shown another universe, the output goes and the universe it left
/// is paused, staying with its owner. One universe is shown by one host at a time.
/// <code>
/// &lt;RenderTargetPanel.Behaviors&gt;&lt;universes:UniverseHost Universe="{Binding Universe}"/&gt;&lt;/RenderTargetPanel.Behaviors&gt;
/// </code>
/// </summary>
public class UniverseHost : Behavior<RenderTargetPanel>
{
    private RenderTargetPanel _panel;
    private Universe _shown;

    public static readonly AdamantiumProperty UniverseProperty = AdamantiumProperty.Register(nameof(Universe),
        typeof(Universe), typeof(UniverseHost), new PropertyMetadata(null, OnUniverseChanged));

    /// <summary>The universe to show, or null for none.</summary>
    public Universe Universe
    {
        get => GetValue<Universe>(UniverseProperty);
        set => SetValue(UniverseProperty, value);
    }

    private static void OnUniverseChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        ((UniverseHost)component).Sync();
    }

    protected override void OnAttached(RenderTargetPanel panel)
    {
        _panel = panel;
        panel.AttachedToVisualTreeEvent += OnPanelAttachedToVisualTree;
        panel.DetachedFromVisualTreeEvent += OnPanelDetachedFromVisualTree;
        Sync();
    }

    protected override void OnDetached(RenderTargetPanel panel)
    {
        panel.AttachedToVisualTreeEvent -= OnPanelAttachedToVisualTree;
        panel.DetachedFromVisualTreeEvent -= OnPanelDetachedFromVisualTree;
        Withdraw();
        _panel = null;
    }

    private void OnPanelAttachedToVisualTree(object sender, VisualTreeAttachmentEventArgs e)
    {
        Sync();
    }

    private void OnPanelDetachedFromVisualTree(object sender, VisualTreeAttachmentEventArgs e)
    {
        _shown?.Pause();
    }

    private void Sync()
    {
        var universe = Universe;
        if (!ReferenceEquals(universe, _shown))
        {
            Withdraw();
        }

        if (universe == null || _panel?.RootVisual is not IWindow window
            || ResolveService<IUniverseService>() is not { } universes)
        {
            return;
        }

        universes.Show(universe, window);
        if (_shown == null)
        {
            universe.CreateOutputFromContext(_panel);
            _shown = universe;
        }

        universe.Resume();
    }

    private void Withdraw()
    {
        if (_shown == null)
        {
            return;
        }

        _shown.Pause();
        _shown.RemoveWindowByContext(_panel);
        _panel.ClearSource();
        _shown = null;
    }
}
