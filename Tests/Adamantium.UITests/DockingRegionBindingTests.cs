using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.Navigation;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Controls.Navigation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// A pane opened by a docking REGION follows its view model: the tab its title, the place its <see cref="IDockablePane.PaneZone"/>
/// both ways - and the pane is a document or a tool as the view model says.
/// </summary>
[TestFixture]
public class DockingRegionBindingTests
{
    private sealed class Inspector : INotifyPropertyChanged, IDockablePane
    {
        private string _title = "Inspector";
        private DockZone _zone = DockZone.Right;

        public event PropertyChangedEventHandler PropertyChanged;

        public string PaneId => "inspector";

        public string PaneTitle
        {
            get => _title;
            set
            {
                _title = value;
                Raise();
            }
        }

        public DockZone PaneZone
        {
            get => _zone;
            set
            {
                if (_zone == value) return;
                _zone = value;
                Raise();
            }
        }

        public DockZone PaneAllowed { get; set; } = DockZone.All;

        public PaneKind PaneKind => PaneKind.Tool;

        public double PaneMinSize => 120;

        private void Raise([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // Says where it opens and nothing more: no setter, no kind - a document.
    private sealed class Report : IDockablePane
    {
        public string PaneId => "report";
        public string PaneTitle => "Report";
        public DockZone PaneZone => DockZone.Center;
        public DockZone PaneAllowed => DockZone.All;
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

    private static DockZone ZoneOf(DockingArea area, string id) => area.Layout.ZoneOf(area.Layout.FindGroup(id));

    [Test]
    public void APaneOpensWhereItsViewModelSays_AsWhatItSays()
    {
        var (area, region) = Docking();

        region.Add(new Inspector());
        var pane = area.PaneById("inspector");

        Assert.Multiple(() =>
        {
            Assert.That(ZoneOf(area, "inspector"), Is.EqualTo(DockZone.Right));
            Assert.That(pane.Kind, Is.EqualTo(PaneKind.Tool));
            Assert.That(pane.MinSize, Is.EqualTo(120));
            Assert.That(pane.Header, Is.EqualTo("Inspector"));
        });
    }

    [Test]
    public void TheTab_FollowsTheTitle()
    {
        var (area, region) = Docking();
        var inspector = new Inspector();
        region.Add(inspector);

        inspector.PaneTitle = "Inspector - Camera";
        BindingUpdateQueue.Flush();

        Assert.That(area.PaneById("inspector").Header, Is.EqualTo("Inspector - Camera"));
    }

    [Test]
    public void NoTitle_ShowsTheViewModel()
    {
        var (area, region) = Docking();
        var inspector = new Inspector { PaneTitle = null };
        region.Add(inspector);

        Assert.That(area.PaneById("inspector").Header, Is.SameAs(inspector));
    }

    [Test]
    public void SettingPaneZone_MovesThePane()
    {
        var (area, region) = Docking();
        var inspector = new Inspector();
        region.Add(inspector);

        inspector.PaneZone = DockZone.Left;
        BindingUpdateQueue.Flush();   // a source change reaches its target on the next frame

        Assert.Multiple(() =>
        {
            Assert.That(ZoneOf(area, "inspector"), Is.EqualTo(DockZone.Left));
            Assert.That(area.PaneById("inspector").Zone, Is.EqualTo(DockZone.Left));
        });
    }

    [Test]
    public void AMoveOnTheLayout_ReachesTheViewModel()
    {
        var (area, region) = Docking();
        var inspector = new Inspector();
        region.Add(inspector);

        // What a drop onto the documents does.
        area.Layout.MovePane("inspector", area.Layout.FindGroup("scene"), DockZone.Center);
        area.Rebuild();

        Assert.That(inspector.PaneZone, Is.EqualTo(DockZone.Center));
    }

    [Test]
    public void AMoveThePaneMayNotMake_SnapsTheViewModelBack()
    {
        var (area, region) = Docking();
        var inspector = new Inspector { PaneAllowed = DockZone.Right | DockZone.Floating };
        region.Add(inspector);

        inspector.PaneZone = DockZone.Left;
        BindingUpdateQueue.Flush();   // a source change reaches its target on the next frame

        Assert.Multiple(() =>
        {
            Assert.That(ZoneOf(area, "inspector"), Is.EqualTo(DockZone.Right));
            Assert.That(inspector.PaneZone, Is.EqualTo(DockZone.Right), "it says where the pane is, not what it asked");
        });
    }

    [Test]
    public void AViewModelWithoutASetter_StillLetsThePaneBeDragged()
    {
        var (area, region) = Docking();
        var report = new Report();
        region.Add(report);

        area.Layout.MovePane("report", area.Layout.Main.Content, DockZone.Left, beside: true);
        area.Rebuild();

        Assert.Multiple(() =>
        {
            Assert.That(area.PaneById("report").Kind, Is.EqualTo(PaneKind.Document), "the default kind");
            Assert.That(ZoneOf(area, "report"), Is.EqualTo(DockZone.Left));
            Assert.That(area.PaneById("report").Zone, Is.EqualTo(DockZone.Left));
            Assert.That(report.PaneZone, Is.EqualTo(DockZone.Center), "and nothing was written into it");
        });
    }

    [Test]
    public async Task AToolClosed_IsPutAway()
    {
        var (area, region) = Docking();
        region.Add(new Inspector());

        Assert.That(await area.ClosePaneAsync("inspector"), Is.True);

        Assert.That(area.HiddenPanes, Does.Contain("inspector"));
    }
}
