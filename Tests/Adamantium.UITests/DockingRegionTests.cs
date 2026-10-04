using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.Navigation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Controls.Navigation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// <see cref="IDockingRegion"/>: what a view model can learn about the docking and do to its panes - the panes the region
/// opened and the ones written in markup alike.
/// </summary>
[TestFixture]
public class DockingRegionTests
{
    private abstract class Dockable : INotifyPropertyChanged, IDockablePane
    {
        private DockZone _zone;

        protected Dockable(string id, DockZone zone)
        {
            PaneId = id;
            _zone = zone;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string PaneId { get; }
        public string PaneTitle => PaneId;
        public DockZone PaneAllowed => DockZone.All;

        public DockZone PaneZone
        {
            get => _zone;
            set
            {
                if (_zone == value) return;
                _zone = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PaneZone)));
            }
        }
    }

    // Navigating to it again reuses the open one.
    private sealed class Output() : Dockable("output", DockZone.Bottom), IDockablePane, INavigationAware
    {
        public PaneKind PaneKind => PaneKind.Tool;

        public Task OnNavigatedToAsync(NavigationContext context, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task OnNavigatedFromAsync(NavigationContext context, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public bool IsNavigationTarget(NavigationContext context) => true;
    }

    // Navigating to it again makes another.
    private sealed class Timeline() : Dockable("timeline", DockZone.Bottom), IDockablePane
    {
        public PaneKind PaneKind => PaneKind.Tool;
    }

    private sealed class Script(string id) : Dockable(id, DockZone.Center);

    private sealed class SceneModel;

    private sealed class Resolver : IDependencyResolver
    {
        public int Made { get; private set; }

        public T Resolve<T>(string name = "") => (T)Resolve(typeof(T), name);

        public object Resolve(Type type, string name = "")
        {
            Made++;
            return Activator.CreateInstance(type);
        }
    }

    private sealed record Setup(DockingArea Area, Adamantium.Navigation.Region Region, IDockingRegion Docking,
        SceneModel Scene, Resolver Resolver);

    // A document written in markup with a view model of its own, a tool written in markup without one, and a region.
    private static Setup Docking()
    {
        var scene = new SceneModel();
        var area = new DockingArea { DividerThickness = 0 };

        var documents = new PaneGroup { Name = "documents", Zone = DockZone.Center };
        documents.Items.Add(new Pane
        {
            Header = "scene",
            Id = "scene",
            Content = new Adamantium.UI.Controls.Decorators.Border { DataContext = scene }
        });
        area.Children.Add(documents);

        var tools = new PaneGroup { Name = "tools", Zone = DockZone.Right };
        tools.Items.Add(new Pane { Header = "inspector", Id = "inspector", Kind = PaneKind.Tool, Content = "no view model" });
        area.Children.Add(tools);

        area.Measure(new Size(1000, 800));
        area.Arrange(new Rect(0, 0, 1000, 800));

        var resolver = new Resolver();
        var region = new Adamantium.Navigation.Region("docking", resolver, null);
        new DockingAreaRegionAdapter(new ViewLocator()).Attach(region, area);
        return new Setup(area, region, DockingRegion.Of(region), scene, resolver);
    }

    [Test]
    public void TheQueries_SeeTheRegionsPanesAndTheMarkupsAlike()
    {
        var (_, region, docking, scene, _) = Docking();
        var output = new Output();
        region.Add(output);

        Assert.Multiple(() =>
        {
            Assert.That(docking.ViewModels, Is.EquivalentTo(new object[] { scene, output }), "the markup pane without one is not there");
            Assert.That(docking.Documents, Is.EqualTo(new object[] { scene }));
            Assert.That(docking.Tools, Is.EqualTo(new object[] { output }));
            Assert.That(docking.ViewModelsIn(DockZone.Bottom), Is.EqualTo(new object[] { output }));
            Assert.That(docking.ViewModelsIn(DockZone.Edges), Is.EqualTo(new object[] { output }));
            Assert.That(docking.Contains<Output>(), Is.True);
            Assert.That(docking.Contains<Timeline>(), Is.False);
            Assert.That(docking.Find<SceneModel>(), Is.SameAs(scene));
            Assert.That(docking.PlacementOf(output).Zone, Is.EqualTo(DockZone.Bottom));
            Assert.That(docking.PlacementOf(output).State, Is.EqualTo(PaneState.Open));
            Assert.That(docking.PlacementOf(new Output()).Zone, Is.EqualTo(DockZone.None), "not here");
        });
    }

    [Test]
    public void TheRegionManager_HandsOutTheSameDockingBeforeAndAfterAControlShowsIt()
    {
        var manager = new Adamantium.Navigation.RegionManager(new Resolver());
        var docking = manager.Docking("docking");

        Assert.Multiple(() =>
        {
            Assert.That(docking.ViewModels, Is.Empty, "nothing shows it yet");
            Assert.That(docking.Activate(new Output()), Is.False);
            Assert.That(manager.Docking("docking"), Is.SameAs(docking));
            Assert.That(DockingRegion.Of(manager["docking"]), Is.SameAs(docking));
        });
    }

    [Test]
    public async Task AClosedTool_IsHidden_AndActivatingBringsTheSameOneBack()
    {
        var (area, region, docking, _, _) = Docking();
        var output = new Output();
        region.Add(output);

        Assert.That(await docking.CloseAsync(output), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(docking.Hidden, Is.EqualTo(new object[] { output }));
            Assert.That(docking.Tools, Is.Empty);
            Assert.That(docking.ViewModels, Does.Contain(output), "kept, not gone");
            Assert.That(docking.PlacementOf(output).State, Is.EqualTo(PaneState.Hidden));
            Assert.That(region.ActiveViewModels, Does.Contain(output), "the region keeps it too");
        });

        Assert.That(docking.Activate(output), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(docking.PlacementOf(output).State, Is.EqualTo(PaneState.Open));
            Assert.That(docking.PlacementOf(output).Zone, Is.EqualTo(DockZone.Bottom));
            Assert.That(docking.ActivePane, Is.SameAs(output));
            Assert.That(area.HiddenPanes, Is.Empty);
        });
    }

    [Test]
    public async Task NavigatingToAClosedTool_BringsItBack()
    {
        var (_, region, docking, _, resolver) = Docking();

        var output = (Output)(await region.NavigateToAsync<Output>()).ViewModel;
        Assert.That(await docking.CloseAsync(output), Is.True);

        var again = await region.NavigateToAsync<Output>();

        Assert.Multiple(() =>
        {
            Assert.That(again.ViewModel, Is.SameAs(output), "the same instance");
            Assert.That(resolver.Made, Is.EqualTo(1), "made once");
            Assert.That(docking.PlacementOf(output).State, Is.EqualTo(PaneState.Open));
            Assert.That(region.ActiveViewModels.OfType<Output>().Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ANewViewModelUnderAClosedToolsId_TakesItsPlace()
    {
        var (area, region, docking, _, _) = Docking();

        var first = (Timeline)(await region.NavigateToAsync<Timeline>()).ViewModel;
        Assert.That(await docking.CloseAsync(first), Is.True);

        var second = (Timeline)(await region.NavigateToAsync<Timeline>()).ViewModel;

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(docking.All<Timeline>(), Is.EqualTo(new[] { second }), "one pane, one view model");
            Assert.That(docking.PlacementOf(second).State, Is.EqualTo(PaneState.Open));
            Assert.That(area.HiddenPanes, Is.Empty);
            Assert.That(region.ActiveViewModels, Does.Not.Contain(first));
        });
    }

    [Test]
    public async Task ShowTool_OpensOnce_ThenBringsItToTheFront()
    {
        var (area, _, docking, _, resolver) = Docking();

        var first = await docking.ShowToolAsync<Output>();
        area.Activate("scene");
        var second = await docking.ShowToolAsync<Output>();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.SameAs(first));
            Assert.That(resolver.Made, Is.EqualTo(1));
            Assert.That(docking.ActivePane, Is.SameAs(first));
        });
    }

    [Test]
    public void EachChange_IsReportedOnce()
    {
        var (area, region, docking, _, _) = Docking();
        var events = new List<string>();
        docking.PaneOpened += (_, e) => events.Add($"opened {Name(e.ViewModel)}");
        docking.PaneClosed += (_, e) => events.Add($"closed {Name(e.ViewModel)}");
        docking.PlacementChanged += (_, e) => events.Add($"moved {Name(e.ViewModel)} {e.Placement.Zone} {e.Placement.State}");

        var output = new Output();
        region.Add(output);

        output.PaneZone = DockZone.Left;
        BindingUpdateQueue.Flush();

        var group = area.Layout.FindGroup("output");
        area.Layout.CollapseGroup(group);
        area.Rebuild();

        region.Remove(output);

        Assert.That(events, Is.EqualTo(new[]
        {
            "opened output",
            "moved output Left Open",
            "moved output Left Collapsed",
            "closed output"
        }));
    }

    [Test]
    public void TheActiveDocument_StaysWhileAToolIsWorkedIn()
    {
        var (area, region, docking, scene, _) = Docking();
        var first = new Script("first");
        var second = new Script("second");
        var output = new Output();
        var changes = 0;
        docking.ActivePaneChanged += (_, _) => changes++;

        region.Add(first);
        region.Add(second);
        region.Add(output);
        area.Activate("first");
        area.Activate("output");

        Assert.Multiple(() =>
        {
            Assert.That(docking.ActivePane, Is.SameAs(output));
            Assert.That(docking.ActiveDocument, Is.SameAs(first));
            Assert.That(docking.RecentDocuments, Is.EqualTo(new object[] { first, second, scene }));
            Assert.That(changes, Is.EqualTo(5), "each opened pane, then first, then output");
        });
    }

    [Test]
    public async Task CloseAll_ClosesWhatMatches_AndAsksAboutEach()
    {
        var (area, region, docking, scene, _) = Docking();
        var first = new Script("first");
        var second = new Script("second");
        region.Add(first);
        region.Add(second);

        area.PaneClosing += (_, e) =>
        {
            if (e.PaneId == "second") e.Cancel = true;
            return Task.CompletedTask;
        };

        var closed = await docking.CloseAllAsync(viewModel => viewModel is Script);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.EqualTo(1));
            Assert.That(docking.Documents, Is.EquivalentTo(new object[] { scene, second }), "the refused one stays");
            Assert.That(region.ActiveViewModels, Does.Not.Contain(first), "a closed document leaves the region");
        });
    }

    [Test]
    public async Task OpenBeside_DocksTheNewPaneAgainstTheTargetsPanel()
    {
        var (area, region, docking, _, _) = Docking();
        var output = new Output();
        region.Add(output);

        var timeline = await docking.OpenBesideAsync<Timeline>(output, DockZone.Right);

        Assert.Multiple(() =>
        {
            Assert.That(timeline, Is.Not.Null);
            Assert.That(area.Layout.FindGroup("timeline"), Is.Not.SameAs(area.Layout.FindGroup("output")), "a panel of its own");
            Assert.That(docking.PlacementOf(timeline).Zone, Is.EqualTo(DockZone.Bottom), "beside it, in the same band");
        });
    }

    [Test]
    public void TheGroups_AreDescribed_DocumentsApartFromTools()
    {
        var (area, region, docking, scene, _) = Docking();
        var output = new Output();
        var first = new Script("first");
        region.Add(output);
        region.Add(first);

        // The document area split in two, as a drop on the right of the documents does.
        Assert.That(area.DockBeside("first", "scene", DockZone.Right), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(docking.Groups, Has.Count.EqualTo(4));
            Assert.That(docking.DocumentGroups.Select(group => group.ViewModels.Single()), Is.EqualTo(new object[] { scene, first }));
            Assert.That(docking.ToolGroups, Has.Count.EqualTo(2), "the inspector's, whose pane has no view model, and the output's");
            Assert.That(docking.GroupOf(first).Kind, Is.EqualTo(PaneKind.Document));
            Assert.That(docking.GroupOf(output).Kind, Is.EqualTo(PaneKind.Tool));
            Assert.That(docking.GroupOf(output).Zone, Is.EqualTo(DockZone.Bottom));
            Assert.That(docking.GroupOf(output).Front.ViewModel, Is.SameAs(output));
        });

        area.Layout.CollapseGroup(area.Layout.FindGroup("output"));
        area.Rebuild();

        Assert.Multiple(() =>
        {
            Assert.That(docking.GroupOf(output).State, Is.EqualTo(PaneState.Collapsed), "put away, still a group");
            Assert.That(area.Layout.ToolGroups.Count(), Is.EqualTo(2));
            Assert.That(area.Layout.DocumentGroups.Count(), Is.EqualTo(2));
        });
    }

    [Test]
    public void AControlReplacingAnother_ReportsOnlyWhatChanged()
    {
        var region = new Adamantium.Navigation.Region("docking", new Resolver(), null);
        var docking = DockingRegion.Of(region);
        var events = new List<string>();
        docking.PaneOpened += (_, e) => events.Add($"opened {e.ViewModel}");
        docking.PaneClosed += (_, e) => events.Add($"closed {e.ViewModel}");

        var old = new FakeHost(("a", DockZone.Center), ("b", DockZone.Right));
        docking.Attach(old);
        docking.Detach(old);
        docking.Attach(new FakeHost(("a", DockZone.Center), ("c", DockZone.Left)));

        Assert.That(events, Is.EqualTo(new[] { "opened a", "opened b", "closed b", "opened c" }));
    }

    private static string Name(object viewModel) => (viewModel as IDockablePane)?.PaneId ?? viewModel.ToString();

    private sealed class FakeHost : IDockingHost
    {
        public FakeHost(params (string ViewModel, DockZone Zone)[] panes)
        {
            Panes = panes.Select(pane => new DockedPane(pane.ViewModel, pane.ViewModel, PaneKind.Document,
                new PanePlacement(pane.Zone, PaneState.Open, false, true))).ToList();
        }

        public IReadOnlyList<DockedPane> Panes { get; }
        public IReadOnlyList<DockedGroup> Groups => [];
        public bool Activate(string paneId) => false;
        public Task<int> CloseAsync(IReadOnlyList<string> paneIds) => Task.FromResult(0);
        public bool DockBeside(string paneId, string targetPaneId, DockZone side) => false;
        public event EventHandler Changed { add { } remove { } }
    }
}
