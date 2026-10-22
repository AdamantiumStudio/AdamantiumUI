using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Adamantium.Mathematics;
using Adamantium.Navigation;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A tool put away stays put away through a saved layout and through a view built anew.</summary>
[TestFixture]
public class DockingHiddenToolsTests
{
    private sealed class TestWindowRoot : Grid, IRootVisualComponent
    {
        public Vector2 PointToClient(PixelPoint point) => new((float)point.X, (float)point.Y);
        public PixelPoint PointToScreen(Vector2 point) => new(point.X, point.Y);
        public PixelPoint Position { get; set; }
        public void AttachContextAndInitialize(IUIContext context) { }
        public double Left { get; set; }
        public double Top { get; set; }
        public string Title { get; set; }
        public double ClientWidth { get; set; }
        public double ClientHeight { get; set; }
        public IUIContext UIContext => null;
    }

    private static DockingArea Rooted(DockingArea area)
    {
        var root = new TestWindowRoot { Width = 1000, Height = 700, ClientWidth = 1000, ClientHeight = 700 };
        root.Children.Add(area);
        WindowExtension.UpdateTree(root);
        WindowExtension.UpdateTree(root);
        return area;
    }

    private static Pane Tool(string id, string restoreKey = null) =>
        new() { Id = id, Header = id, Kind = PaneKind.Tool, RestoreKey = restoreKey };

    private static DockingArea SplitArea()
    {
        var area = Rooted(new DockingArea());
        area.AddPane(new Pane { Id = "scene", Header = "scene" }, DockZone.Center);
        area.AddPane(Tool("inspector"), DockZone.Right);
        return area;
    }

    private static async Task<DockingWorkspace> WorkspaceWithClosedHierarchy()
    {
        var workspace = new DockingWorkspace();
        var first = SplitArea();
        first.AddPane(Tool("hierarchy"), DockZone.Left);
        workspace.Attach(first);
        Assert.That(await first.ClosePaneAsync("hierarchy"), Is.True, "precondition: closed");
        return workspace;
    }

    private static async Task<string> StateWithOutput(bool closed)
    {
        var area = SplitArea();
        area.AddPane(Tool("output", "output-key"), DockZone.Bottom);
        if (closed)
        {
            Assert.That(await area.ClosePaneAsync("output"), Is.True, "precondition: closed");
        }

        return area.SaveLayout();
    }

    private static int Placements(DockingArea area, string id) =>
        area.Layout.Roots.Sum(root => DockingLayout.PanesIn(root.Content).Count(pane => pane == id));

    private static void RestoresTools(DockingArea area) => area.PaneRestoring += (_, e) => e.Pane ??= Tool(e.PaneId, e.RestoreKey);

    [Test]
    public async Task AnAreaFilledByARegionAlone_KeepsTheToolClosed()
    {
        var workspace = await WorkspaceWithClosedHierarchy();

        var second = new DockingArea();
        second.AddPane(new Pane { Id = "scene", Header = "scene" }, DockZone.Center);
        second.AddPane(Tool("inspector"), DockZone.Right);
        second.AddPane(Tool("hierarchy"), DockZone.Left);
        workspace.Attach(second);
        Rooted(second);

        Assert.Multiple(() =>
        {
            Assert.That(second.Layout.FindGroup("hierarchy"), Is.Null, "not opened again");
            Assert.That(second.HiddenPanes, Does.Contain("hierarchy"), "but put away");
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task PanesAskedForBeforeTheMarkup_KeepTheToolClosed(bool closedToolLast)
    {
        var workspace = await WorkspaceWithClosedHierarchy();

        var second = new DockingArea();
        if (closedToolLast)
        {
            second.AddPane(Tool("inspector"), DockZone.Right);
            second.AddPane(Tool("hierarchy"), DockZone.Left);
        }
        else
        {
            second.AddPane(Tool("hierarchy"), DockZone.Left);
            second.AddPane(Tool("inspector"), DockZone.Right);
        }

        second.Children.Add(new Pane { Id = "scene", Header = "scene", Zone = DockZone.Center });
        workspace.Attach(second);

        Assert.Multiple(() =>
        {
            Assert.That(second.Layout.FindGroup("hierarchy"), Is.Null, "not opened again");
            Assert.That(second.HiddenPanes, Does.Contain("hierarchy"), "but put away");
        });
    }

    [Test]
    public async Task AToolUnknownAtTheLoad_IsSavedPutAway_UntilItIsOpened()
    {
        var workspace = await WorkspaceWithClosedHierarchy();
        var second = SplitArea();
        workspace.Attach(second);

        Assert.That(DockingLayoutSerializer.ReadHidden(second.SaveLayout()).Keys, Does.Contain("hierarchy"),
            "the area has no such pane, and its being put away is not forgotten");

        second.AddPane(Tool("hierarchy"), DockZone.Left);

        Assert.Multiple(() =>
        {
            Assert.That(second.Layout.FindGroup("hierarchy"), Is.Not.Null, "opened by code, it opens");
            Assert.That(DockingLayoutSerializer.ReadHidden(second.SaveLayout()).Keys, Does.Not.Contain("hierarchy"));
        });
    }

    [Test]
    public async Task AToolUnknownAtTheLoad_KeepsItsRestoreKey()
    {
        var closed = await StateWithOutput(closed: true);
        var area = SplitArea();
        Assert.That(area.LoadLayout(closed), Is.True);

        var next = SplitArea();
        RestoresTools(next);
        Assert.That(next.LoadLayout(area.SaveLayout()), Is.True);

        Assert.That(next.HiddenPanes, Does.Contain("output"), "made from the key the first area kept, and put away");
    }

    [Test]
    public async Task AToolPutAwayBeforeALoadThatDoesNotNameIt_ComesBackWhenRestored()
    {
        var area = SplitArea();
        area.AddPane(Tool("hierarchy"), DockZone.Right);
        Assert.That(await area.ClosePaneAsync("hierarchy"), Is.True);

        Assert.That(area.LoadLayout(SplitArea().SaveLayout()), Is.True);
        Assert.That(area.HiddenPanes, Does.Contain("hierarchy"));
        Assert.That(area.RestorePane("hierarchy"), Is.True);

        Assert.That(area.Layout.FindGroup("hierarchy"), Is.Not.Null, "back in the layout, not in a group of the old one");
    }

    [Test]
    public async Task ALoadThatPutsAnOpenToolAway_SaysItClosed_AndMovesTheAccentOff()
    {
        var state = await StateWithOutput(closed: true);
        var area = SplitArea();
        area.AddPane(Tool("output"), DockZone.Bottom);
        Assert.That(area.ActivePaneId, Is.EqualTo("output"), "precondition: worked in");
        var closed = false;
        area.PaneClosed += (_, e) => closed |= e.PaneId == "output" && e.CanRestore;

        Assert.That(area.LoadLayout(state), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(area.Layout.FindGroup("output"), Is.Null);
            Assert.That(closed, Is.True, "PaneClosed for the tool the load put away");
            Assert.That(area.ActivePaneId, Is.Not.EqualTo("output"), "no accent on a pane put away");
        });
    }

    [Test]
    public async Task AViewBuiltAnew_DoesNotAnnounceAToolClosedLongAgo()
    {
        var workspace = new DockingWorkspace();
        var first = new DockingArea();
        first.Children.Add(new Pane { Id = "scene", Header = "scene", Zone = DockZone.Center });
        first.Children.Add(new Pane { Id = "hierarchy", Header = "hierarchy", Zone = DockZone.Left, Kind = PaneKind.Tool });
        Rooted(first);
        workspace.Attach(first);
        Assert.That(await first.ClosePaneAsync("hierarchy"), Is.True);
        var raised = 0;
        workspace.PaneClosed += (_, _) => raised++;

        var second = new DockingArea();
        second.Children.Add(new Pane { Id = "scene", Header = "scene", Zone = DockZone.Center });
        second.Children.Add(new Pane { Id = "hierarchy", Header = "hierarchy", Zone = DockZone.Left, Kind = PaneKind.Tool });
        Rooted(second);
        workspace.Attach(second);

        Assert.Multiple(() =>
        {
            Assert.That(second.Layout.FindGroup("hierarchy"), Is.Null);
            Assert.That(raised, Is.EqualTo(0), "nothing was closed now");
        });
    }

    [Test]
    public async Task AViewBuiltAnew_KeepsTheAccentOffAToolPutAway()
    {
        var workspace = await WorkspaceWithClosedHierarchy();

        var second = Rooted(new DockingArea());
        second.AddPane(new Pane { Id = "scene", Header = "scene" }, DockZone.Center);
        second.AddPane(Tool("inspector"), DockZone.Right);
        second.AddPane(Tool("hierarchy"), DockZone.Left);
        workspace.Attach(second);

        Assert.That(second.ActivePaneId, Is.Not.EqualTo("hierarchy"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AViewBuiltAnew_KeepsTheTabInFrontAndThePaneWorkedIn(bool built)
    {
        var workspace = new DockingWorkspace();
        var first = SplitArea();
        first.AddPane(Tool("properties"), DockZone.Right);
        Assert.That(first.Activate("inspector"), Is.True);
        workspace.Attach(first);

        var second = new DockingArea();
        if (built)
        {
            Rooted(second);
        }

        second.AddPane(new Pane { Id = "scene", Header = "scene" }, DockZone.Center);
        second.AddPane(Tool("inspector"), DockZone.Right);
        second.AddPane(Tool("properties"), DockZone.Right);
        workspace.Attach(second);
        Rooted(second);

        var group = second.Layout.FindGroup("inspector");
        Assert.Multiple(() =>
        {
            Assert.That(group.PaneIds[group.ActiveIndex], Is.EqualTo("inspector"), "the tab in front");
            Assert.That(second.ActivePaneId, Is.EqualTo("inspector"), "the pane worked in");
        });
    }

    [Test]
    public async Task ALoadOfAToolOnceUnknown_PlacesItOnce()
    {
        var closed = await StateWithOutput(closed: true);
        var open = await StateWithOutput(closed: false);
        var area = SplitArea();
        Assert.That(area.LoadLayout(closed), Is.True);
        RestoresTools(area);
        Assert.That(area.LoadLayout(open), Is.True);

        area.AddPane(area.PaneById("output"), DockZone.Bottom);
        area.Activate("output");

        Assert.Multiple(() =>
        {
            Assert.That(Placements(area, "output"), Is.EqualTo(1));
            Assert.That(area.HiddenPanes, Does.Not.Contain("output"));
        });
    }

    [Test]
    public async Task AToolPutAwayTwice_IsSavedOnce_AndMigrates()
    {
        var closed = await StateWithOutput(closed: true);
        var area = SplitArea();
        Assert.That(area.LoadLayout(closed), Is.True);
        RestoresTools(area);
        Assert.That(area.LoadLayout(closed), Is.True);
        var saved = area.SaveLayout();

        var next = SplitArea();
        next.LayoutVersion = 5;
        next.PaneMigrating += (_, e) => e.NewId = e.PaneId;

        Assert.Multiple(() =>
        {
            Assert.That(System.Text.RegularExpressions.Regex.Matches(saved, "\"output\": \"").Count, Is.EqualTo(2),
                "once in the restore table, once among the put away");
            Assert.DoesNotThrow(() => next.LoadLayout(saved));
        });
    }

    [Test]
    public async Task ALaterLoadOrAReset_ForgetsWhatAnEarlierLoadHadPutAway()
    {
        var closed = await StateWithOutput(closed: true);
        var open = await StateWithOutput(closed: false);

        var loaded = SplitArea();
        Assert.That(loaded.LoadLayout(closed), Is.True);
        Assert.That(loaded.LoadLayout(open), Is.True);
        loaded.AddPane(Tool("output"), DockZone.Bottom);

        var reset = SplitArea();
        Assert.That(reset.LoadLayout(closed), Is.True);
        Assert.That(reset.ResetLayout(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Layout.FindGroup("output"), Is.Not.Null, "the layout on screen had it open");
            Assert.That(DockingLayoutSerializer.ReadHidden(reset.SaveLayout()).Keys, Does.Not.Contain("output"));
        });
    }

    [Test]
    public async Task MarkupArrivingAfterALoad_TakesTheLoadedArrangement()
    {
        var closed = await StateWithOutput(closed: true);
        var area = new DockingArea();
        Assert.That(area.LoadLayout(closed), Is.True);
        area.Children.Add(new Pane { Id = "scene", Header = "scene", Zone = DockZone.Center });
        area.Children.Add(new Pane { Id = "output", Header = "output", Zone = DockZone.Bottom, Kind = PaneKind.Tool });
        Rooted(area);
        var saved = area.SaveLayout();

        Assert.Multiple(() =>
        {
            Assert.That(area.Layout.FindGroup("output"), Is.Null, "put away as loaded");
            Assert.That(DockingLayoutSerializer.ReadHidden(saved).Keys, Does.Contain("output"));
        });
    }

    [Test]
    public void AHandlerThatThrowsDuringALoad_LeavesThePanesAskedForPlaced()
    {
        var workspace = new DockingWorkspace();
        var first = SplitArea();
        first.PaneStateSaving += (_, e) => e.State = "s";
        workspace.Attach(first);

        var second = new DockingArea();
        second.AddPane(Tool("late"), DockZone.Left);
        second.Children.Add(new Pane { Id = "scene", Header = "scene", Zone = DockZone.Center });
        second.Children.Add(new Pane { Id = "inspector", Header = "inspector", Zone = DockZone.Right, Kind = PaneKind.Tool });
        second.PaneStateRestoring += (_, _) => throw new InvalidOperationException("the application's own bug");

        Assert.Throws<InvalidOperationException>(() => workspace.Attach(second));
        Rooted(second);
        Assert.That(second.Layout.FindGroup("late"), Is.Not.Null);
    }

    [Test]
    public async Task AnUnreadableLoad_LeavesTheOneWaitingForTheArea()
    {
        var closed = await StateWithOutput(closed: true);
        var area = new DockingArea();
        area.AddPane(new Pane { Id = "scene", Header = "scene" }, DockZone.Center);
        area.AddPane(Tool("inspector"), DockZone.Right);
        area.AddPane(Tool("output"), DockZone.Bottom);
        Assert.That(area.LoadLayout(closed), Is.True, "waits for the area to be built");

        Assert.That(area.LoadLayout("{ not json"), Is.False);
        Rooted(area);

        Assert.That(area.Layout.FindGroup("output"), Is.Null, "the readable arrangement is still the one applied");
    }

    [Test]
    public async Task AToolUnknownAtTheLoad_KeepsItsState()
    {
        var source = SplitArea();
        source.PaneStateSaving += (_, e) => e.State = e.PaneId == "output" ? "scrolled" : null;
        source.AddPane(Tool("output", "output-key"), DockZone.Bottom);
        Assert.That(await source.ClosePaneAsync("output"), Is.True);
        var state = source.SaveLayout();

        var area = SplitArea();
        Assert.That(area.LoadLayout(state), Is.True);

        Assert.That(DockingLayoutSerializer.ReadStates(area.SaveLayout()).GetValueOrDefault("output"), Is.EqualTo("scrolled"));
    }

    [TestCase("active", "7")]
    [TestCase("hidden", "{ \"output\": 3 }")]
    [TestCase("states", "{ \"output\": false }")]
    [TestCase("restore", "[ 1, 2 ]")]
    public async Task AValueOfTheWrongKind_IsNotACrash(string key, string odd)
    {
        var layout = System.Text.Json.Nodes.JsonNode.Parse(await StateWithOutput(closed: true))!.AsObject();
        layout[key] = System.Text.Json.Nodes.JsonNode.Parse(odd);
        var state = layout.ToJsonString();
        var area = SplitArea();
        area.LayoutVersion = 3;
        area.PaneMigrating += (_, e) => e.NewId = e.PaneId;

        Assert.DoesNotThrow(() => area.LoadLayout(state));
    }

    [Test]
    public async Task PanesAskedForBeforeALoad_GoWhereTheyAsked_AndThePaneWorkedInStays()
    {
        var source = SplitArea();
        Assert.That(source.Activate("inspector"), Is.True);
        var state = source.SaveLayout();
        Assert.That(DockingLayoutSerializer.ReadActivePane(state), Is.EqualTo("inspector"));

        var area = new DockingArea();
        area.AddPane(new Pane { Id = "scene", Header = "scene" }, DockZone.Center);
        area.AddPane(Tool("inspector"), DockZone.Right);
        area.AddPane(new Pane { Id = "readme", Header = "readme" }, DockZone.Center);
        area.AddPane(Tool("console"), DockZone.Left);
        Assert.That(area.LoadLayout(state), Is.True);
        Rooted(area);

        Assert.Multiple(() =>
        {
            Assert.That(area.Layout.ZoneOf(area.Layout.FindGroup("console")), Is.EqualTo(DockZone.Left));
            Assert.That(area.Layout.FindGroup("readme"), Is.Not.Null);
            Assert.That(area.ActivePaneId, Is.EqualTo("inspector"), "the pane worked in when saved");
        });
    }

    [Test]
    public async Task ALoadStartedFromAPaneClosedHandler_LeavesOnePlaceForThePane()
    {
        var closed = await StateWithOutput(closed: true);
        var open = await StateWithOutput(closed: false);
        var area = SplitArea();
        area.AddPane(Tool("output", "output-key"), DockZone.Bottom);
        var nested = false;
        area.PaneClosed += (_, _) =>
        {
            if (nested)
            {
                return;
            }

            nested = true;
            area.LoadLayout(open);
        };

        Assert.That(area.LoadLayout(closed), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(Placements(area, "output"), Is.LessThanOrEqualTo(1));
            Assert.That(area.HiddenPanes.Contains("output") && area.Layout.FindGroup("output") != null, Is.False);
        });
    }
}
