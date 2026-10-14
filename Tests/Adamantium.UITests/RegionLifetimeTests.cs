using System;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.Navigation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Navigation;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

// A region lives in the application's manager, a window does not. What a closed window showed in its regions has to go
// with it: the region kept its documents, and the next window asking for the name opened on the old window's tabs.
[TestFixture]
public class RegionLifetimeTests
{
    private sealed class Document : INavigationAware
    {
        public int Left;

        public Task OnNavigatedToAsync(NavigationContext context, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task OnNavigatedFromAsync(NavigationContext context, CancellationToken cancellationToken = default)
        {
            Left++;
            return Task.CompletedTask;
        }

        public bool IsNavigationTarget(NavigationContext context) => false;
    }

    private sealed class NoResolver : IDependencyResolver
    {
        public T Resolve<T>(string name = "") => throw new NotSupportedException();
        public object Resolve(Type type, string name = "") => throw new NotSupportedException();
    }

    [Test]
    public async Task ARemovedRegion_LetsGoOfWhatItShowed()
    {
        var manager = new Adamantium.Navigation.RegionManager(new NoResolver());
        var region = manager.GetOrCreateRegion("Workspace");
        var first = new Document();
        var second = new Document();
        region.Activate(first);
        region.Activate(second);

        await manager.RemoveRegionAsync(region);

        Assert.Multiple(() =>
        {
            Assert.That(first.Left + second.Left, Is.EqualTo(2), "each document hears it was left");
            Assert.That(region.ActiveViewModels, Is.Empty);
            Assert.That(region.CurrentViewModel, Is.Null);
            Assert.That(manager.GetOrCreateRegion("Workspace"), Is.Not.SameAs(region), "the name is free again");
        });
    }

    private static (Window Window, TabControl Host) WindowShowing(IRegion region)
    {
        var host = new TabControl();
        var window = new Window { Width = 200, Height = 100, Content = new StackPanel { Children = { host } } };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        new SelectorRegionAdapter(null).Attach(region, host);
        RegionHost.Track(region, host);
        return (window, host);
    }

    [Test]
    public void AClosingWindow_RemovesTheRegionsOnlyItShowed()
    {
        var manager = new Adamantium.Navigation.RegionManager(new NoResolver());
        var region = manager.GetOrCreateRegion("Workspace");
        var document = new Document();
        region.Activate(document);
        var (window, _) = WindowShowing(region);

        RegionHost.ReleaseRegionsIn(window, manager);

        Assert.Multiple(() =>
        {
            Assert.That(document.Left, Is.EqualTo(1));
            Assert.That(manager.TryGetRegion("Workspace", out _), Is.False);
        });
    }

    // A pane torn off into a window of its own shows the same region as the main window: closing that window must leave
    // the region to the one still showing it.
    [Test]
    public void AClosingWindow_LeavesARegionAnotherWindowStillShows()
    {
        var manager = new Adamantium.Navigation.RegionManager(new NoResolver());
        var region = manager.GetOrCreateRegion("Docking");
        var document = new Document();
        region.Activate(document);
        var (closing, _) = WindowShowing(region);
        var (stays, _) = WindowShowing(region);

        RegionHost.ReleaseRegionsIn(closing, manager);

        Assert.Multiple(() =>
        {
            Assert.That(document.Left, Is.Zero);
            Assert.That(manager.TryGetRegion("Docking", out var kept) && ReferenceEquals(kept, region), Is.True);
        });
        GC.KeepAlive(stays);
    }

    // A region a view model hands to its view (Region.Source) is the view model's: a window showing it closes, the view
    // model and its tabs go on, and may be shown again.
    [Test]
    public void AClosingWindow_LeavesAViewModelsOwnRegion()
    {
        var manager = new Adamantium.Navigation.RegionManager(new NoResolver());
        var region = manager.CreateRegion();
        var document = new Document();
        region.Activate(document);
        var host = new TabControl();
        var window = new Window { Width = 200, Height = 100, Content = host };
        RegionHost.Attach(region, host, named: false);

        RegionHost.ReleaseRegionsIn(window, manager);

        Assert.That(region.ActiveViewModels, Has.Member(document));
    }

    // The pane a docking region makes for a view model goes when the view model leaves: only taken out of the layout, it
    // held the view it showed for good.
    [Test]
    public void APaneMadeForAViewModel_GoesWhenItLeavesTheRegion()
    {
        var area = new Adamantium.UI.Controls.Docking.DockingArea();
        area.Measure(new Adamantium.Mathematics.Size(800, 600));
        var region = new Adamantium.Navigation.Region("docking", new NoResolver(), null);
        new DockingAreaRegionAdapter(new ViewLocator()).Attach(region, area);
        var document = new Document();
        region.Add(document);
        var pane = System.Linq.Enumerable.Single(area.Panes);

        region.Remove(document);
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.That(pane.IsDiscarded, Is.True);
    }

    // A page kept between visits waits under the control that kept it. That control discarded - its window closed -
    // nothing can ask for the page again; it waited for eviction, or for ever when kept as Required.
    [Test]
    public void AKeptPage_GoesWithTheControlThatKeptIt()
    {
        var owner = new ContentControl();
        var page = new Border();
        ParkedVisuals.Keep(owner, "page", page);

        DiscardedVisuals.Publish(owner);
        DiscardedVisuals.Drain(int.MaxValue);

        Assert.Multiple(() =>
        {
            Assert.That(ParkedVisuals.TryTake(owner, "page", null, out _, out _, out _, out _), Is.False);
            Assert.That(page.IsDiscarded, Is.True);
        });
    }

    [Test]
    public async Task AWindowsViewModel_HearsItWasLeftWhenTheWindowCloses()
    {
        var manager = new Adamantium.Navigation.RegionManager(new NoResolver());
        var navigation = new NavigationService(manager, new NoResolver(), null);
        var content = new Document();

        await navigation.LeaveWindowAsync(content);

        Assert.That(content.Left, Is.EqualTo(1));
    }
}
