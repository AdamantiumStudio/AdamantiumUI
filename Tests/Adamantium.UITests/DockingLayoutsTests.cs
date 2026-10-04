using System;
using System.Linq;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.Navigation;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Controls.Navigation;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// Arrangements as a view model handles them: back to the markup's, kept under names, carried across a renamed pane, and
/// each pane's own state saved with them.
/// </summary>
[TestFixture]
public class DockingLayoutsTests
{
    private static Pane Tool(string id) => new() { Header = id, Id = id, Kind = PaneKind.Tool };

    private static Pane Document(string id) => new() { Header = id, Id = id };

    private static PaneGroup Group(string name, DockZone zone, params Pane[] panes)
    {
        var group = new PaneGroup { Name = name, Zone = zone };
        foreach (var pane in panes) group.Items.Add(pane);
        return group;
    }

    private static DockingArea Area(string inspectorId = "inspector")
    {
        var area = new DockingArea { DividerThickness = 0 };
        area.Children.Add(Group("documents", DockZone.Center, Document("scene"), Document("game")));
        area.Children.Add(Group("tools", DockZone.Right, Tool(inspectorId), Tool("hierarchy")));
        area.Children.Add(Group("console", DockZone.Bottom, Tool("output")));

        area.Measure(new Size(1000, 800));
        area.Arrange(new Rect(0, 0, 1000, 800));
        return area;
    }

    private static DockZone ZoneOf(DockingArea area, string id) => area.Layout.ZoneOf(area.Layout.FindGroup(id));

    [Test]
    public async Task Reset_GoesBackToTheMarkup_AndKeepsWhatIsOpen()
    {
        var area = Area();
        area.PaneById("inspector").Zone = DockZone.Left;
        area.Layout.CollapseGroup(area.Layout.FindGroup("output"));
        area.Rebuild();
        Assert.That(await area.ClosePaneAsync("hierarchy"), Is.True);
        area.AddPane(Document("notes"));

        Assert.That(area.ResetLayout(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(ZoneOf(area, "inspector"), Is.EqualTo(DockZone.Right));
            Assert.That(area.Layout.FindGroup("hierarchy"), Is.SameAs(area.Layout.FindGroup("inspector")), "the closed tool is back");
            Assert.That(area.HiddenPanes, Is.Empty);
            Assert.That(area.Layout.FindGroup("output").State, Is.EqualTo(PaneGroupState.Docked));
            Assert.That(area.Layout.FindGroup("notes"), Is.SameAs(area.Layout.FindGroup("scene")), "a document opened since stays");
        });
    }

    /// <summary>The document area is a place, empty or not: reset with every document closed, the tools stay where the
    /// markup put them and none of them becomes the documents.</summary>
    [Test]
    public async Task Reset_WithEveryDocumentClosed_KeepsTheDocumentArea()
    {
        var area = Area();
        Assert.That(await area.ClosePaneAsync("scene"), Is.True);
        Assert.That(await area.ClosePaneAsync("game"), Is.True);

        Assert.That(area.ResetLayout(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(area.Layout.DocumentWell, Is.Not.Null);
            Assert.That(area.Layout.RootOf(area.Layout.DocumentWell), Is.SameAs(area.Layout.Main), "and it is in the tree");
            Assert.That(area.Layout.IsDocument(area.Layout.FindGroup("output")), Is.False, "a tool stays a tool");
            Assert.That(area.Layout.IsDocument(area.Layout.FindGroup("inspector")), Is.False);
            Assert.That(ZoneOf(area, "inspector"), Is.EqualTo(DockZone.Right));
            Assert.That(ZoneOf(area, "output"), Is.EqualTo(DockZone.Bottom));
        });

        area.AddPane(Document("notes"));
        Assert.That(area.Layout.IsDocument(area.Layout.FindGroup("notes")), Is.True, "a new document goes to the area");
    }

    /// <summary>A saved layout carries the document area even though it never carries documents.</summary>
    [Test]
    public void ASavedLayout_KeepsTheDocumentArea_AndTheOpenDocumentsInIt()
    {
        var area = Area();
        var saved = area.SaveLayout();

        Assert.That(area.LoadLayout(saved), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(area.Layout.IsDocument(area.Layout.FindGroup("scene")), Is.True);
            Assert.That(area.Layout.FindGroup("game"), Is.SameAs(area.Layout.FindGroup("scene")));
            Assert.That(ZoneOf(area, "inspector"), Is.EqualTo(DockZone.Right));
            Assert.That(ZoneOf(area, "output"), Is.EqualTo(DockZone.Bottom));
        });
    }

    [Test]
    public void ANamedLayout_IsKept_AndPutBack_WithTheDocumentsStillOpen()
    {
        var area = Area();
        var workspace = new DockingWorkspace();
        area.Workspace = workspace;

        Assert.That(workspace.SaveAs("Coding"), Is.True);
        area.PaneById("inspector").Zone = DockZone.Left;
        Assert.That(workspace.SaveAs("Animation"), Is.True);

        Assert.That(workspace.Apply("Coding"), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(workspace.Layouts.Keys, Is.EquivalentTo(new[] { "Coding", "Animation" }));
            Assert.That(ZoneOf(area, "inspector"), Is.EqualTo(DockZone.Right));
            Assert.That(area.Layout.IsDocument(area.Layout.FindGroup("scene")), Is.True, "documents are not saved, yet stay open");
        });

        Assert.That(workspace.Apply("Animation"), Is.True);
        Assert.That(ZoneOf(area, "inspector"), Is.EqualTo(DockZone.Left));
        Assert.That(workspace.Apply("Nothing"), Is.False);
    }

    [Test]
    public void ALayoutSavedBeforeAPaneWasRenamed_KeepsItsPlace()
    {
        var before = Area();
        before.LayoutVersion = 1;
        before.PaneById("inspector").Zone = DockZone.Left;
        var saved = before.SaveLayout();

        var after = Area(inspectorId: "properties");
        after.LayoutVersion = 2;
        var asked = 0;
        after.PaneMigrating += (_, e) =>
        {
            asked++;
            Assert.That(e.SavedVersion, Is.EqualTo(1));
            if (e.PaneId == "inspector") e.NewId = "properties";
        };

        Assert.That(after.LoadLayout(saved), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(ZoneOf(after, "properties"), Is.EqualTo(DockZone.Left));
            Assert.That(asked, Is.GreaterThan(0));
        });
    }

    [Test]
    public void ALayoutOfTheSameVersion_IsNotMigrated()
    {
        var area = Area();
        area.LayoutVersion = 3;
        var saved = area.SaveLayout();
        var asked = 0;
        area.PaneMigrating += (_, _) => asked++;

        Assert.That(area.LoadLayout(saved), Is.True);
        Assert.That(asked, Is.EqualTo(0));
    }

    [Test]
    public void APanesOwnState_TravelsWithTheLayout()
    {
        var area = Area();
        area.PaneStateSaving += (_, e) => e.State = e.PaneId == "output" ? "line 42" : null;
        var saved = area.SaveLayout();

        var other = Area();
        string restored = null;
        other.PaneStateRestoring += (_, e) =>
        {
            if (e.PaneId == "output") restored = e.State;
        };

        Assert.That(other.LoadLayout(saved), Is.True);
        Assert.That(restored, Is.EqualTo("line 42"));
    }

    // --- A view model's state, through a region ---------------------------------------------------------------------

    private sealed class Log : IDockablePane, IRestorablePane
    {
        public string Line { get; set; } = "top";
        public string PaneId => "log";
        public string PaneTitle => "Log";
        public DockZone PaneZone => DockZone.Bottom;
        public DockZone PaneAllowed => DockZone.All;
        public PaneKind PaneKind => PaneKind.Tool;

        public void RestoreFrom(string paneId) { }
        public string SaveState() => Line;
        public void RestoreState(string state) => Line = state;
    }

    private sealed class Resolver : IDependencyResolver
    {
        public T Resolve<T>(string name = "") => (T)Resolve(typeof(T), name);
        public object Resolve(Type type, string name = "") => Activator.CreateInstance(type);
    }

    [Test]
    public void AViewModelsState_IsSavedAndGivenBack()
    {
        var area = Area();
        var region = new Adamantium.Navigation.Region("docking", new Resolver(), null);
        new DockingAreaRegionAdapter(new ViewLocator()).Attach(region, area);

        var log = new Log { Line = "line 7" };
        region.Add(log);
        var saved = area.SaveLayout();

        log.Line = "elsewhere";
        Assert.That(area.LoadLayout(saved), Is.True);

        Assert.That(log.Line, Is.EqualTo("line 7"));
    }
}
