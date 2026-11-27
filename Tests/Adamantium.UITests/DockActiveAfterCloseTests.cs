using System.Linq;
using Adamantium.Mathematics;
using Adamantium.Navigation;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

// Closing the active pane keeps its group active (the active pane id moves to the newly selected tab).
[TestFixture]
public class DockActiveAfterCloseTests
{
    private sealed class TestWindowRoot : Grid, IRootVisualComponent
    {
        public Vector2<double> PointToClient(PixelPoint point) => new((float)point.X, (float)point.Y);
        public PixelPoint PointToScreen(Vector2<double> point) => new(point.X, point.Y);
        public PixelPoint Position { get; set; }
        public void AttachContextAndInitialize(IUIContext context) { }
        public double Left { get; set; }
        public double Top { get; set; }
        public string Title { get; set; }
        public double ClientWidth { get; set; }
        public double ClientHeight { get; set; }
        public IUIContext UIContext => null;
    }

    private static TestWindowRoot Rooted(DockingArea area)
    {
        var root = new TestWindowRoot { Width = 1000, Height = 700, ClientWidth = 1000, ClientHeight = 700 };
        root.Children.Add(area);
        WindowExtension.UpdateTree(root);
        WindowExtension.UpdateTree(root);
        return root;
    }

    private static bool AnyGroupActive(DockingArea area)
        => area.GetVisualDescendants().OfType<PaneGroup>().Any(g => g.IsActive);

    [Test]
    public void ClosingTheActivePane_LeavesItsPanelActive()
    {
        var area = new DockingArea();
        var root = Rooted(area);
        area.AddPane(new Pane { Id = "scene", Header = "scene" });
        area.AddPane(new Pane { Id = "game", Header = "game" });
        WindowExtension.UpdateTree(root);

        area.Activate("game");
        WindowExtension.UpdateTree(root);
        Assert.That(AnyGroupActive(area), Is.True, "a panel is active to begin with");

        area.RemovePane("game");
        WindowExtension.UpdateTree(root);
        WindowExtension.UpdateTree(root);

        Assert.That(AnyGroupActive(area), Is.True,
            "the panel is still the one being worked in - the pane that closed took the accent with it");
    }

    /// <summary>...and through the door a USER actually uses. A pane can leave two ways - RemovePane above, and the
    /// close path, which removes from the layout itself - and a rule about what closing means has to hold for both.
    /// Fixing only the first left the tab's own close button behaving exactly as before.</summary>
    [Test]
    public async System.Threading.Tasks.Task ClosingTheActivePaneThroughTheCloseCommand_LeavesItsPanelActive()
    {
        var area = new DockingArea();
        var root = Rooted(area);
        area.AddPane(new Pane { Id = "scene", Header = "scene" });
        area.AddPane(new Pane { Id = "game", Header = "game" });
        WindowExtension.UpdateTree(root);

        area.Activate("game");
        WindowExtension.UpdateTree(root);
        Assert.That(AnyGroupActive(area), Is.True, "a panel is active to begin with");

        await area.ClosePaneAsync("game");
        WindowExtension.UpdateTree(root);
        WindowExtension.UpdateTree(root);

        Assert.That(AnyGroupActive(area), Is.True, "closing a tab must not put the whole panel out");
    }

    [Test]
    public void PickingAnotherTabOfTheActivePanel_MakesItThePaneWorkedIn()
    {
        var area = new DockingArea();
        var root = Rooted(area);
        area.AddPane(new Pane { Id = "scene", Header = "scene" });
        area.AddPane(new Pane { Id = "code", Header = "code" });
        WindowExtension.UpdateTree(root);
        area.Activate("code");
        WindowExtension.UpdateTree(root);
        var changed = 0;
        area.ActivePaneChanged += (_, _) => changed++;

        var group = area.GetVisualDescendants().OfType<PaneGroup>().First(g => g.Items.OfType<Pane>().Any(pane => pane.Id == "scene"));
        group.SelectedIndex = group.Items.OfType<Pane>().ToList().FindIndex(pane => pane.Id == "scene");
        WindowExtension.UpdateTree(root);

        Assert.Multiple(() =>
        {
            Assert.That(area.ActivePaneId, Is.EqualTo("scene"), "the tab in front of the panel worked in");
            Assert.That(changed, Is.GreaterThan(0));
        });
    }

    [Test]
    public void TabsPutInANewOrder_KeepThePaneWorkedIn()
    {
        var area = new DockingArea();
        var root = Rooted(area);
        area.AddPane(new Pane { Id = "a", Header = "a" });
        area.AddPane(new Pane { Id = "b", Header = "b" });
        area.AddPane(new Pane { Id = "c", Header = "c" });
        WindowExtension.UpdateTree(root);
        area.Activate("c");
        WindowExtension.UpdateTree(root);

        var node = area.Layout.FindGroup("c");
        node.Remove("c");
        node.Insert(0, "c");
        node.ActiveIndex = 0;
        area.Rebuild();
        WindowExtension.UpdateTree(root);

        Assert.Multiple(() =>
        {
            Assert.That(area.ActivePaneId, Is.EqualTo("c"));
            Assert.That(node.ActiveIndex, Is.EqualTo(0), "the tab in front stays the one worked in");
        });
    }

    [Test]
    public void ClosingATabBeforeTheActiveOne_KeepsTheActiveOneInFront()
    {
        var area = new DockingArea();
        var root = Rooted(area);
        foreach (var id in new[] { "a", "b", "c", "d" })
            area.AddPane(new Pane { Id = id, Header = id });
        WindowExtension.UpdateTree(root);
        area.Activate("c");
        WindowExtension.UpdateTree(root);

        area.RemovePane("a");
        WindowExtension.UpdateTree(root);
        WindowExtension.UpdateTree(root);

        var group = area.GetVisualDescendants().OfType<PaneGroup>().First(g => g.Items.OfType<Pane>().Any(pane => pane.Id == "c"));
        Assert.Multiple(() =>
        {
            Assert.That(area.ActivePaneId, Is.EqualTo("c"));
            Assert.That((group.SelectedItem as Pane)?.Id, Is.EqualTo("c"), "the tab worked in stays in front");
        });
    }

    [Test]
    public void PickingATabOfAnotherPanel_LeavesThePaneWorkedIn()
    {
        var area = new DockingArea();
        var root = Rooted(area);
        area.AddPane(new Pane { Id = "scene", Header = "scene" });
        area.AddPane(new Pane { Id = "inspector", Header = "inspector", Kind = PaneKind.Tool }, DockZone.Right);
        area.AddPane(new Pane { Id = "output", Header = "output", Kind = PaneKind.Tool }, DockZone.Right);
        WindowExtension.UpdateTree(root);
        area.Activate("scene");
        WindowExtension.UpdateTree(root);

        var tools = area.GetVisualDescendants().OfType<PaneGroup>().First(g => g.Items.OfType<Pane>().Any(pane => pane.Id == "inspector"));
        tools.SelectedIndex = tools.Items.OfType<Pane>().ToList().FindIndex(pane => pane.Id == "inspector");
        WindowExtension.UpdateTree(root);

        Assert.That(area.ActivePaneId, Is.EqualTo("scene"), "a tab shown in another panel is not worked in until touched");
    }
}
