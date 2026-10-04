using System.Collections.Generic;
using Adamantium.Navigation;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Behaviors;

namespace Adamantium.UI.Sandbox.Behaviors;

/// <summary>Demonstrates application docking policy: answers <see cref="DockingArea.PaneDocking"/> and
/// <see cref="DockingArea.PaneTearingOff"/> (always yes) and logs each answer. Static rules go in <see cref="Pane.Allowed"/>.</summary>
public class DockingPolicyBehavior : Behavior<DockingArea>
{
    private DockingArea _area;

    protected override void OnAttached(DockingArea area)
    {
        _area = area;
        area.PaneDocking += OnDocking;
        area.PaneTearingOff += OnTearingOff;
    }

    protected override void OnDetached(DockingArea area)
    {
        area.PaneDocking -= OnDocking;
        area.PaneTearingOff -= OnTearingOff;
        _area = null;
    }

    /// <summary>Says what was answered, out loud. Through the VIEW MODEL rather than a property on this behavior: a
    /// behavior is not an element of the visual tree, so nothing in the markup can bind to it by name - the view model
    /// is what both the view and this share.</summary>
    private void Answer(ViewModels.DockingAnswer answer, IEnumerable<string> panes, DockZone zone = DockZone.None)
    {
        if (_area?.DataContext is ViewModels.DockingViewModel viewModel)
        {
            viewModel.Answer(answer, string.Join(", ", panes), zone);
        }
    }

    private void OnTearingOff(object sender, PaneTearingOffEventArgs e)
    {
        Answer(e.IsWholePanel ? ViewModels.DockingAnswer.PanelTornOff : ViewModels.DockingAnswer.TabTornOff, e.Panes);
    }

    private void OnDocking(object sender, PaneDockingEventArgs e)
    {
        Answer(ViewModels.DockingAnswer.Docked, e.Panes, e.Zone);
    }
}
