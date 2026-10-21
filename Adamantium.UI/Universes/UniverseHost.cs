using System.Runtime.CompilerServices;
using Adamantium.Multiverse;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Behaviors;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Universes;

/// <summary>
/// Shows a universe made and owned elsewhere - by a view-model - in the <see cref="RenderTargetPanel"/> it is attached
/// to, through an output of its own (<see cref="Output"/>). While the panel is in a window, the universe runs and draws
/// into the panel; once no panel showing it is in a window, it is paused. When the panel is discarded or shown another
/// universe, the output goes, and the universe it left is paused if no other panel shows it, staying with its owner.
/// The host resumes only a universe it paused itself: one the application paused - a game paused from a toolbar - stays
/// paused across a tab switched away and back. One universe can be shown by several hosts of one window at once, each
/// panel a view of its own into it.
/// <code>
/// &lt;RenderTargetPanel.Behaviors&gt;&lt;universes:UniverseHost Universe="{Binding Universe}"/&gt;&lt;/RenderTargetPanel.Behaviors&gt;
/// </code>
/// </summary>
public class UniverseHost : Behavior<RenderTargetPanel>
{
    private static readonly ConditionalWeakTable<Universe, Hosting> Hostings = new();

    private RenderTargetPanel _panel;
    private Universe _shown;
    private bool _inWindow;

    public static readonly AdamantiumProperty UniverseProperty = AdamantiumProperty.Register(nameof(Universe),
        typeof(Universe), typeof(UniverseHost), new PropertyMetadata(null, OnUniverseChanged));

    public static readonly AdamantiumProperty OutputProperty = AdamantiumProperty.RegisterReadOnly(nameof(Output),
        typeof(UniverseOutput), typeof(UniverseHost), new PropertyMetadata(null));

    /// <summary>The universe to show, or null for none.</summary>
    public Universe Universe
    {
        get => GetValue<Universe>(UniverseProperty);
        set => SetValue(UniverseProperty, value);
    }

    /// <summary>The output the universe draws into this panel through, or null while it shows none: what tells this
    /// panel's view apart from the other panels' showing the same universe.</summary>
    public UniverseOutput Output
    {
        get => GetValue<UniverseOutput>(OutputProperty);
        private set => SetValue(OutputProperty, value);
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
        LeaveWindow();
    }

    private void Sync()
    {
        var universe = Universe;
        if (!ReferenceEquals(universe, _shown))
        {
            Withdraw();
        }

        if (universe == null || _panel?.RootVisual is not IWindow window
            || UniverseService() is not { } universes)
        {
            return;
        }

        universes.Show(universe, window);
        if (_shown == null)
        {
            Output = universe.CreateOutputFromContext(_panel);
            _shown = universe;
        }

        EnterWindow();
    }

    private static IUniverseService UniverseService()
    {
        var context = UIAppContext.Current?.UIContext;
        return context?.Resolve<IUniverseService>();
    }

    private void EnterWindow()
    {
        if (_inWindow)
        {
            return;
        }

        _inWindow = true;
        var hosting = Hostings.GetOrCreateValue(_shown);
        hosting.InWindow++;
        if (hosting.Paused)
        {
            hosting.Paused = false;
            _shown.Resume();
        }
    }

    private void LeaveWindow()
    {
        if (!_inWindow)
        {
            return;
        }

        _inWindow = false;
        var hosting = Hostings.GetOrCreateValue(_shown);
        hosting.InWindow--;
        if (hosting.InWindow == 0 && !_shown.IsPaused)
        {
            _shown.Pause();
            hosting.Paused = true;
        }
    }

    private void Withdraw()
    {
        if (_shown == null)
        {
            return;
        }

        LeaveWindow();
        _shown.RemoveWindowByContext(_panel);
        _panel.ClearSource();
        _shown = null;
        Output = null;
    }

    private sealed class Hosting
    {
        public int InWindow;
        public bool Paused;
    }
}
