using System.Linq;
using Adamantium.Mathematics;
using Adamantium.Navigation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// What the layout is allowed to do to the sizes it hands out: the document well has a floor, minimums are honored
/// wherever a size comes from, and the area does not hold on to controls for nodes that have died.
/// </summary>
[TestFixture]
public class DockingSizingTests
{
    private static PaneGroup Group(string name, DockZone zone, double size = double.NaN)
    {
        var group = new PaneGroup { Name = name, Zone = zone, Size = size };
        group.Items.Add(new Pane { Header = name, Id = name });
        return group;
    }

    private static DockingArea Area(params PaneGroup[] groups)
    {
        var area = new DockingArea { DividerThickness = 0 };
        foreach (var group in groups) area.Children.Add(group);
        return area;
    }

    private static void Lay(DockingArea area, double width, double height)
    {
        area.Measure(new Size<double>(width, height));
        area.Arrange(new Rect(0, 0, width, height));
    }

    private static PaneGroup Control(DockingArea area, string paneId)
    {
        return Descendants(area).OfType<PaneGroup>()
            .FirstOrDefault(g => g.Items.OfType<Pane>().Any(p => p.Id == paneId));
    }

    private static System.Collections.Generic.IEnumerable<IUIComponent> Descendants(IUIComponent root)
    {
        foreach (var child in root.VisualChildren)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    /// <summary>A rebuild of an unchanged split keeps its divider and makes none to throw away; a divider the layout no
    /// longer has is let go of. A thrown-away divider stayed subscribed to the application's resources and held the area
    /// - and the window round it - for good.</summary>
    [Test]
    public void ADividerIsMadeOnlyWhenWanted_AndLetGoOfWhenNot()
    {
        var area = Area(Group("scene", DockZone.Center));
        area.AddPane(new Pane { Header = "tool", Id = "tool", Kind = PaneKind.Tool }, DockZone.Left);
        Lay(area, 700, 600);
        var divider = Descendants(area).OfType<PaneSplitter>().Single();
        var made = new System.Collections.Generic.List<PaneSplitter>();

        area.Rebuild();
        Lay(area, 700, 600);
        made.AddRange(Descendants(area).OfType<PaneSplitter>());
        area.RemovePane("tool");
        Lay(area, 700, 600);

        Assert.Multiple(() =>
        {
            Assert.That(made, Is.EqualTo(new[] { divider }), "the same divider, and no other");
            Assert.That(Descendants(area).OfType<PaneSplitter>(), Is.Empty);
            Assert.That(divider.IsDiscarded, Is.True, "the divider the layout lost is let go of");
        });
    }

    /// <summary>
    /// Every tool docked against the center is paid for by the center, so without a floor enough of them
    /// squeeze it out of existence - measured before the fix at 60px against a stated minimum of 200.
    /// </summary>
    [Test]
    public void TheDocumentWellIsNeverSqueezedBelowItsFloor()
    {
        var area = Area(Group("scene", DockZone.Center));
        area.DocumentMinSize = 200;

        // Four side panels, each asking for a band of its own out of a 700px-wide area: 4 x 240 is more than there is.
        for (var i = 0; i < 4; i++)
        {
            area.AddPane(new Pane { Header = $"tool{i}", Id = $"tool{i}", Kind = PaneKind.Tool }, DockZone.Left);
        }

        Lay(area, 700, 600);

        Assert.That(Control(area, "scene").Bounds.Width, Is.GreaterThanOrEqualTo(200 - 0.5),
            "the center keeps its floor and the bands are squeezed instead");
    }

    /// <summary>A share is not a permission to disappear: whoever falls under its minimum is pinned at it, and the cost
    /// comes out of those that still have room. Before this, minimums were a splitter's business alone.</summary>
    [Test]
    public void MinimumsAreHonoredWhereverTheSizeComesFrom()
    {
        var area = Area(Group("scene", DockZone.Center), Group("inspector", DockZone.Right, size: 240));
        area.DocumentMinSize = 200;

        // Narrower than the two of them want together: 240 + 200 does not fit in 300.
        Lay(area, 300, 600);

        Assert.Multiple(() =>
        {
            var center = Control(area, "scene").Bounds.Width;
            var side = Control(area, "inspector").Bounds.Width;

            Assert.That(center + side, Is.EqualTo(300).Within(1), "between them they still fill the area exactly");
            Assert.That(center, Is.GreaterThan(0), "and neither is squeezed out of existence");
            Assert.That(side, Is.GreaterThan(0));
        });
    }

    /// <summary>A pane opened from CODE joins the panel already on that side. Opening from code cannot see what a new
    /// column would cost, and each one used to take its band off the center until the layout was a row of slivers.</summary>
    [Test]
    public void APaneOpenedOnAnOccupiedSideBecomesATabThere()
    {
        var area = Area(Group("scene", DockZone.Center), Group("inspector", DockZone.Right, size: 240));
        Lay(area, 1000, 800);

        area.AddPane(new Pane { Header = "hierarchy", Id = "hierarchy", Kind = PaneKind.Tool }, DockZone.Right);
        Lay(area, 1000, 800);

        var side = area.Layout.FindGroup("inspector");

        Assert.Multiple(() =>
        {
            Assert.That(side.PaneIds, Does.Contain("hierarchy"), "it joined the panel that was already there");
            Assert.That(Control(area, "hierarchy"), Is.SameAs(Control(area, "inspector")), "one control, two tabs");
        });
    }

    /// <summary>The area keeps a control per model node ACROSS rebuilds, so it has to let go of the nodes that die -
    /// groups when their last pane leaves, and split hosts when a row is left with one child and collapses away.</summary>
    [Test]
    public void ControlsOfDeadNodesAreForgotten()
    {
        var area = Area(Group("scene", DockZone.Center));
        Lay(area, 1000, 800);

        var groupsAtRest = area.TrackedGroups;
        var hostsAtRest = area.TrackedHosts;

        // Open a panel on each side and close it again, several times over. Every round builds a split and then
        // collapses it, so anything not pruned accumulates.
        for (var i = 0; i < 5; i++)
        {
            var pane = new Pane { Header = "tool", Id = "tool", Kind = PaneKind.Document };
            area.AddPane(pane, DockZone.Left);
            Lay(area, 1000, 800);

            area.RemovePane("tool");
            Lay(area, 1000, 800);
        }

        Assert.Multiple(() =>
        {
            Assert.That(area.TrackedGroups, Is.EqualTo(groupsAtRest), "no group control is held for a node that died");
            Assert.That(area.TrackedHosts, Is.EqualTo(hostsAtRest), "and no split host either");
        });
    }

    // ...and it is not only forgotten but thrown away: only out of the tree, its buttons followed the theme and held
    // the view model they were bound to, and through it the area and the whole page.
    [Test]
    public void TheControlOfADeadGroup_IsDiscarded()
    {
        var area = Area(Group("scene", DockZone.Center));
        Lay(area, 1000, 800);
        area.AddPane(new Pane { Header = "tool", Id = "tool", Kind = PaneKind.Document }, DockZone.Left);
        Lay(area, 1000, 800);
        var group = Control(area, "tool");

        area.RemovePane("tool");
        Lay(area, 1000, 800);
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.That(group.IsDiscarded, Is.True);
    }

    [Test]
    public void ADiscardedArea_LeavesItsWorkspace()
    {
        var workspace = new DockingWorkspace();
        var area = Area(Group("scene", DockZone.Center));
        area.Workspace = workspace;
        Lay(area, 1000, 800);
        Assume.That(workspace.IsReady, Is.True, "the area joined the workspace");

        DiscardedVisuals.PublishSubtree(area);
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.That(workspace.IsReady, Is.False);
    }
}
