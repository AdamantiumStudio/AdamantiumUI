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

    // What a press at a point reaches in the area: the panes, tabs and fields of its panels.
    private static bool ReachesAPane(DockingArea area, double x, double y) =>
        area.HitTest(new Vector2<double>(x, y)) is IUIComponent hit && !ReferenceEquals(hit, area);

    [Test]
    public void ABusyArea_IsCoveredWhole_ThePanesUnderItOutOfReach()
    {
        var area = Area();
        Assume.That(ReachesAPane(area, 400, 300), Is.True, "the documents, not busy");

        area.IsBusy = true;
        Lay(area);

        Assert.Multiple(() =>
        {
            Assert.That(area.Children.Last(), Is.SameAs(area.BusyOverlay), "over everything else");
            Assert.That(area.BusyOverlay.Bounds, Is.EqualTo(new Rect(0, 0, 1000, 800)));
            Assert.That(ReachesAPane(area, 400, 300), Is.False, "the documents");
            Assert.That(ReachesAPane(area, 900, 10), Is.False, "a tool panel's tabs");
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
            Assert.That(ReachesAPane(area, 400, 300), Is.True);
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

    // An area busy from the start - an application getting ready as it opens - still waits for the panels its markup
    // declares before it builds its layout, as one not busy does.
    [TestCase(false)]
    [TestCase(true)]
    public void AnAreaBusyFromTheStart_StillBuildsWhatItsMarkupDeclares(bool busy)
    {
        var area = new DockingArea { DividerThickness = 0, IsBusy = busy };
        area.AddPane(new Pane { Header = "console", Id = "console" }, DockZone.Bottom);
        area.Children.Add(Group("scene", DockZone.Center));
        Lay(area);

        Assert.That(area.Layout.FindGroup("scene"), Is.Not.Null);
    }

    // Disabled, nothing in it takes the focus - not Tab, not a window giving a field its focus back.
    [Test]
    public void ABusyArea_IsDisabled_AndNoMoreWhenItIsNot()
    {
        var area = Area();

        area.IsBusy = true;
        var whileBusy = area.IsEnabled;
        area.IsBusy = false;

        Assert.Multiple(() =>
        {
            Assert.That(whileBusy, Is.False);
            Assert.That(area.IsEnabled, Is.True);
        });
    }

    [Test]
    public void AnAreaDisabledByItsApplication_StaysSoAfterBeingBusy()
    {
        var area = Area();
        area.IsEnabled = false;

        area.IsBusy = true;
        area.IsBusy = false;

        Assert.That(area.IsEnabled, Is.False);
    }

    [Test]
    public void AnAreaNeverBusy_MakesNoOverlay()
    {
        var area = Area();

        Assert.That(area.Children.OfType<DockingBusyOverlay>(), Is.Empty);
    }
}
