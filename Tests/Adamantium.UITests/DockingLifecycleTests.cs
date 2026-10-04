using System;
using System.Collections.Generic;
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
/// <see cref="IDockingAware"/>: a view model hears its own pane come and go - in the order of the change, the pane left
/// behind first.
/// </summary>
[TestFixture]
public class DockingLifecycleTests
{
    private sealed class Tool(string id, List<string> log) : IDockablePane, IDockingAware
    {
        public string PaneId => id;
        public string PaneTitle => id;
        public DockZone PaneZone => DockZone.Bottom;
        public DockZone PaneAllowed => DockZone.All;
        public PaneKind PaneKind => PaneKind.Tool;

        public void OnActivated() => log.Add($"{id} activated");
        public void OnDeactivated() => log.Add($"{id} deactivated");
        public void OnShown() => log.Add($"{id} shown");
        public void OnHidden() => log.Add($"{id} hidden");
        public void OnPlacementChanged(PanePlacement placement) => log.Add($"{id} placed {placement.Zone} {placement.State}");
    }

    private sealed class NoResolver : IDependencyResolver
    {
        public T Resolve<T>(string name = "") => throw new NotSupportedException();
        public object Resolve(Type type, string name = "") => throw new NotSupportedException();
    }

    private static (DockingArea Area, Adamantium.Navigation.Region Region) Docking()
    {
        var area = new DockingArea { DividerThickness = 0 };
        var documents = new PaneGroup { Name = "documents", Zone = DockZone.Center };
        documents.Items.Add(new Pane { Header = "scene", Id = "scene" });
        area.Children.Add(documents);

        area.Measure(new Size(1000, 800));
        area.Arrange(new Rect(0, 0, 1000, 800));

        var region = new Adamantium.Navigation.Region("docking", new NoResolver(), null);
        new DockingAreaRegionAdapter(new ViewLocator()).Attach(region, area);
        return (area, region);
    }

    [Test]
    public void ATabOpenedInFront_LeavesTheOneBehindFirst()
    {
        var (_, region) = Docking();
        var log = new List<string>();

        region.Add(new Tool("output", log));
        region.Add(new Tool("errors", log));

        Assert.That(log, Is.EqualTo(new[]
        {
            "output shown", "output activated",
            "output deactivated", "output hidden", "errors shown", "errors activated"
        }));
    }

    [Test]
    public void SwitchingTabs_HidesOneAndShowsTheOther()
    {
        var (area, region) = Docking();
        var log = new List<string>();
        region.Add(new Tool("output", log));
        region.Add(new Tool("errors", log));
        log.Clear();

        area.Activate("output");

        Assert.That(log, Is.EqualTo(new[] { "errors deactivated", "errors hidden", "output shown", "output activated" }));
    }

    [Test]
    public void FoldingThePanelAway_HidesItsFrontTab_AndMovesIt()
    {
        var (area, region) = Docking();
        var log = new List<string>();
        region.Add(new Tool("output", log));
        region.Add(new Tool("errors", log));
        log.Clear();

        area.Layout.CollapseGroup(area.Layout.FindGroup("errors"));
        area.Rebuild();

        // Both tabs moved, in the panel's order; only the front one was on screen.
        Assert.That(log, Is.EqualTo(new[] { "errors hidden", "output placed Bottom Collapsed", "errors placed Bottom Collapsed" }));
    }

    [Test]
    public async Task ClosingATool_AndBringingItBack()
    {
        var (area, region) = Docking();
        var log = new List<string>();
        region.Add(new Tool("output", log));
        log.Clear();

        Assert.That(await area.ClosePaneAsync("output"), Is.True);
        Assert.That(log, Is.EqualTo(new[] { "output deactivated", "output hidden" }));

        log.Clear();
        Assert.That(area.Activate("output"), Is.True);
        Assert.That(log, Is.EqualTo(new[] { "output shown", "output activated" }));
    }
}
