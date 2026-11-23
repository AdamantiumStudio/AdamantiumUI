using System.Linq;
using Adamantium.Mathematics;
using Adamantium.Navigation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A busy area lies under an overlay that takes the pointer, so nothing in its panes is reached; not busy, the
/// overlay is gone. The windows torn out of an area follow it - the sandbox's Docking scenario shows both.</summary>
[TestFixture]
public class DockingBusyTests
{
    private static PaneGroup Group(string name, DockZone zone)
    {
        var group = new PaneGroup { Name = name, Zone = zone };
        group.Items.Add(new Pane { Header = name, Id = name });
        return group;
    }

    private static DockingArea Area()
    {
        var area = new DockingArea { DividerThickness = 0 };
        area.Children.Add(Group("scene", DockZone.Center));
        area.Children.Add(Group("inspector", DockZone.Right, 200));
        Lay(area);
        return area;
    }

    private static PaneGroup Group(string name, DockZone zone, double size)
    {
        var group = Group(name, zone);
        group.Size = size;
        return group;
    }

    private static void Lay(DockingArea area)
    {
        area.Measure(new Size<double>(1000, 800));
        area.Arrange(new Rect(0, 0, 1000, 800));
    }

    private static bool IsOverlay(IInputComponent hit, DockingArea area)
    {
        for (IUIComponent each = hit; each != null; each = each.VisualParent)
        {
            if (ReferenceEquals(each, area.BusyOverlay)) return true;
        }

        return false;
    }

    [Test]
    public void ABusyArea_IsCoveredWhole_ThePanesUnderItOutOfReach()
    {
        var area = Area();

        area.IsBusy = true;
        Lay(area);

        Assert.Multiple(() =>
        {
            Assert.That(area.Children.Last(), Is.SameAs(area.BusyOverlay), "over everything else");
            Assert.That(area.BusyOverlay.Bounds, Is.EqualTo(new Rect(0, 0, 1000, 800)));
            Assert.That(IsOverlay(area.HitTest(new Vector2<double>(400, 300)), area), Is.True, "the documents");
            Assert.That(IsOverlay(area.HitTest(new Vector2<double>(900, 10)), area), Is.True, "a tool panel's tabs");
        });
    }

    [Test]
    public void NoLongerBusy_TheOverlayGoes()
    {
        var area = Area();
        area.IsBusy = true;
        Lay(area);

        area.IsBusy = false;
        Lay(area);

        Assert.Multiple(() =>
        {
            Assert.That(area.Children, Does.Not.Contain(area.BusyOverlay));
            Assert.That(IsOverlay(area.HitTest(new Vector2<double>(400, 300)), area), Is.False);
        });
    }

    // The children are put back together whenever the layout changes; the overlay must still be among them, on top.
    [Test]
    public void APaneOpenedWhileBusy_StaysUnderTheOverlay()
    {
        var area = Area();
        area.IsBusy = true;
        Lay(area);

        area.AddPane(new Pane { Header = "console", Id = "console" }, DockZone.Bottom);
        Lay(area);

        Assert.That(area.Children.Last(), Is.SameAs(area.BusyOverlay));
    }

    [Test]
    public void AnAreaNeverBusy_MakesNoOverlay()
    {
        var area = Area();

        Assert.That(area.Children.OfType<DockingBusyOverlay>(), Is.Empty);
    }
}
