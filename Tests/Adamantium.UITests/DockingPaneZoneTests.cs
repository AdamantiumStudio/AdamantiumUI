using System.Linq;
using System.Threading.Tasks;
using Adamantium.Mathematics;
using Adamantium.Navigation;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// <see cref="Pane.Zone"/> is STATE: it reads where the pane is after any change, and writing it moves the pane - the
/// way opening it there would - or snaps back when the move is refused.
/// </summary>
[TestFixture]
public class DockingPaneZoneTests
{
    private static Pane Tool(string id, DockZone zone = DockZone.Center) =>
        new() { Header = id, Id = id, Kind = PaneKind.Tool, Zone = zone };

    private static Pane Document(string id) => new() { Header = id, Id = id };

    private static PaneGroup Group(string name, DockZone zone, params Pane[] panes)
    {
        var group = new PaneGroup { Name = name, Zone = zone };
        foreach (var pane in panes) group.Items.Add(pane);
        return group;
    }

    private static DockingArea Area(params IMeasurableComponent[] children)
    {
        var area = new DockingArea { DividerThickness = 0 };
        foreach (var child in children) area.Children.Add(child);

        area.Measure(new Size(1000, 800));
        area.Arrange(new Rect(0, 0, 1000, 800));
        return area;
    }

    // Documents in the middle, a tool panel on the right, a console along the bottom.
    private static DockingArea Standard() => Area(
        Group("documents", DockZone.Center, Document("scene"), Document("game")),
        Group("tools", DockZone.Right, Tool("inspector"), Tool("hierarchy")),
        Group("console", DockZone.Bottom, Tool("output")));

    private static DockZone ZoneOf(DockingArea area, string id) => area.Layout.ZoneOf(area.Layout.FindGroup(id));

    [Test]
    public void AuthoredPanes_ReportWhereTheyStand()
    {
        var area = Standard();

        Assert.Multiple(() =>
        {
            Assert.That(area.PaneById("scene").Zone, Is.EqualTo(DockZone.Center));
            Assert.That(area.PaneById("inspector").Zone, Is.EqualTo(DockZone.Right), "the group's zone, not the pane's default");
            Assert.That(area.PaneById("output").Zone, Is.EqualTo(DockZone.Bottom));
        });
    }

    [Test]
    public void WritingAZone_MovesThePaneThere()
    {
        var area = Standard();
        var inspector = area.PaneById("inspector");

        inspector.Zone = DockZone.Left;

        Assert.Multiple(() =>
        {
            Assert.That(ZoneOf(area, "inspector"), Is.EqualTo(DockZone.Left));
            Assert.That(inspector.Zone, Is.EqualTo(DockZone.Left));
            Assert.That(area.Layout.FindGroup("hierarchy").PaneIds, Does.Not.Contain("inspector"), "it left its old panel");
            Assert.That(area.PaneById("hierarchy").Zone, Is.EqualTo(DockZone.Right), "which stayed where it was");
        });
    }

    [Test]
    public void WritingAZone_JoinsThePanelAlreadyOnThatSide()
    {
        var area = Standard();

        area.PaneById("inspector").Zone = DockZone.Bottom;

        Assert.That(area.Layout.FindGroup("inspector"), Is.SameAs(area.Layout.FindGroup("output")),
            "a tab in the console, not a second band under it");
    }

    [Test]
    public void WritingCenter_MakesItATabAmongTheDocuments()
    {
        var area = Standard();

        area.PaneById("inspector").Zone = DockZone.Center;

        Assert.Multiple(() =>
        {
            Assert.That(area.Layout.FindGroup("inspector"), Is.SameAs(area.Layout.FindGroup("scene")));
            Assert.That(area.Layout.IsDocument(area.Layout.FindGroup("inspector")), Is.True);
        });
    }

    [Test]
    public void AMoveMadeOnTheLayout_IsWrittenBackToTheZone()
    {
        var area = Standard();

        // What a drop does: the model moves, the area rebuilds.
        area.Layout.MovePane("inspector", area.Layout.FindGroup("output"), DockZone.Center);
        area.Rebuild();

        Assert.That(area.PaneById("inspector").Zone, Is.EqualTo(DockZone.Bottom));
    }

    [Test]
    public void APutAwayPanel_KeepsTheZoneOfItsEdge()
    {
        var area = Standard();
        var tools = area.Layout.FindGroup("inspector");

        Assert.That(area.Layout.CollapseGroup(tools), Is.True);
        area.Rebuild();
        area.PaneById("inspector").Zone = DockZone.Right;   // where it already is: nothing to do

        Assert.Multiple(() =>
        {
            Assert.That(area.PaneById("inspector").Zone, Is.EqualTo(DockZone.Right));
            Assert.That(area.Layout.FindGroup("inspector"), Is.SameAs(tools), "and writing it again moved nothing");
            Assert.That(tools.State, Is.EqualTo(PaneGroupState.Collapsed));
        });
    }

    [Test]
    public void AZoneThePaneMayNotGoTo_SnapsBack()
    {
        var area = Standard();
        var inspector = area.PaneById("inspector");
        inspector.Allowed = DockZone.Right | DockZone.Floating;

        inspector.Zone = DockZone.Left;

        Assert.Multiple(() =>
        {
            Assert.That(ZoneOf(area, "inspector"), Is.EqualTo(DockZone.Right), "it stayed");
            Assert.That(inspector.Zone, Is.EqualTo(DockZone.Right), "and says so");
        });
    }

    [Test]
    public void AMoveTheApplicationRefuses_SnapsBack()
    {
        var area = Standard();
        var asked = new System.Collections.Generic.List<DockZone>();
        area.PaneDocking += (_, e) =>
        {
            asked.Add(e.Zone);
            e.Cancel = true;
        };

        var inspector = area.PaneById("inspector");
        inspector.Zone = DockZone.Left;

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.EqualTo(new[] { DockZone.Left }), "asked once, about the zone that was written");
            Assert.That(ZoneOf(area, "inspector"), Is.EqualTo(DockZone.Right));
            Assert.That(inspector.Zone, Is.EqualTo(DockZone.Right));
        });
    }

    [Test]
    public async Task AClosedTool_ComesBackToTheZoneWrittenWhileItWasAway()
    {
        var area = Standard();
        var inspector = area.PaneById("inspector");

        Assert.That(await area.ClosePaneAsync("inspector"), Is.True);
        inspector.Zone = DockZone.Left;
        Assert.That(area.RestorePane("inspector"), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(ZoneOf(area, "inspector"), Is.EqualTo(DockZone.Left), "not back into the panel it left");
            Assert.That(inspector.Zone, Is.EqualTo(DockZone.Left));
        });
    }

    [Test]
    public async Task AClosedTool_WithItsPanelGone_ComesBackToItsLastZone()
    {
        var area = Standard();

        Assert.That(await area.ClosePaneAsync("output"), Is.True);   // the console's only pane: its panel dies
        Assert.That(area.RestorePane("output"), Is.True);

        Assert.That(ZoneOf(area, "output"), Is.EqualTo(DockZone.Bottom));
    }

    [Test]
    public void PanesWrittenStraightIntoTheArea_ArePlacedByTheirZone()
    {
        var area = Area(
            Document("scene"),
            Tool("inspector", DockZone.Right),
            Tool("hierarchy", DockZone.Right),
            Tool("output", DockZone.Bottom),
            Document("game"));

        Assert.Multiple(() =>
        {
            Assert.That(area.Layout.FindGroup("game"), Is.SameAs(area.Layout.FindGroup("scene")), "documents together");
            Assert.That(area.Layout.IsDocument(area.Layout.FindGroup("scene")), Is.True);
            Assert.That(area.Layout.FindGroup("hierarchy"), Is.SameAs(area.Layout.FindGroup("inspector")),
                "one panel per zone");
            Assert.That(ZoneOf(area, "inspector"), Is.EqualTo(DockZone.Right));
            Assert.That(ZoneOf(area, "output"), Is.EqualTo(DockZone.Bottom));
            Assert.That(area.Panes.Count(), Is.EqualTo(5), "nothing was dropped");
        });
    }

    [Test]
    public void AToolDeclaredBeforeTheDocuments_DoesNotBecomeThem()
    {
        var area = Area(Tool("inspector", DockZone.Right), Document("scene"));

        Assert.Multiple(() =>
        {
            Assert.That(area.Layout.IsDocument(area.Layout.FindGroup("scene")), Is.True);
            Assert.That(area.Layout.IsDocument(area.Layout.FindGroup("inspector")), Is.False);
            Assert.That(area.PaneById("inspector").Zone, Is.EqualTo(DockZone.Right));
        });
    }

    [Test]
    public void APanelDeclaredBeforeTheDocuments_DoesNotBecomeThem()
    {
        var area = Area(
            Group("tools", DockZone.Right, Tool("inspector")),
            Group("documents", DockZone.Center, Document("scene")));

        Assert.Multiple(() =>
        {
            Assert.That(area.Layout.IsDocument(area.Layout.FindGroup("scene")), Is.True);
            Assert.That(ZoneOf(area, "inspector"), Is.EqualTo(DockZone.Right));
        });
    }

    [Test]
    public void APanelStackedInASideColumn_IsOnThatSide()
    {
        var area = Area(
            Group("documents", DockZone.Center, Document("scene")),
            Group("left", DockZone.Left, Tool("explorer"), Tool("outline")));

        // Split the left column top/bottom, as dropping on the lower half of the panel does.
        Assert.That(area.Layout.MovePane("outline", area.Layout.FindGroup("explorer"), DockZone.Bottom), Is.True);
        area.Rebuild();

        Assert.Multiple(() =>
        {
            Assert.That(area.Layout.FindGroup("outline"), Is.Not.SameAs(area.Layout.FindGroup("explorer")));
            Assert.That(area.PaneById("outline").Zone, Is.EqualTo(DockZone.Left), "the left column, not its bottom");
            Assert.That(area.PaneById("explorer").Zone, Is.EqualTo(DockZone.Left));
        });

        // ...so "open this at the bottom" builds a bottom band rather than joining the column's lower half.
        area.AddPane(Tool("log"), DockZone.Bottom);

        Assert.Multiple(() =>
        {
            Assert.That(area.Layout.FindGroup("log"), Is.Not.SameAs(area.Layout.FindGroup("outline")));
            Assert.That(ZoneOf(area, "log"), Is.EqualTo(DockZone.Bottom));
        });
    }

    [Test]
    public void AGroupInAWindowOfItsOwn_IsFloating()
    {
        var layout = new DockingLayout();
        var documents = new PaneGroupNode();
        documents.Add("scene");
        layout.Roots.Add(new DockingRoot(documents, isMain: true) { DocumentWell = documents });

        var torn = new PaneGroupNode();
        torn.Add("inspector");
        layout.Roots.Add(new DockingRoot(torn, isMain: false) { DocumentWell = torn });

        Assert.Multiple(() =>
        {
            Assert.That(layout.ZoneOf(torn), Is.EqualTo(DockZone.Floating));
            Assert.That(layout.ZoneOf(documents), Is.EqualTo(DockZone.Center));
            Assert.That(layout.ZoneOf(new PaneGroupNode()), Is.EqualTo(DockZone.None), "not in the layout");
        });
    }
}
