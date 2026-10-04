using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.Navigation;
using NUnit.Framework;

namespace Adamantium.UITests;

[TestFixture]
public class RegionLifecycleTests
{
    private class Page : INavigationAware
    {
        public Func<CancellationToken, Task> Arrive { get; set; } = _ => Task.CompletedTask;
        public bool Reusable { get; set; }
        public int Arrivals { get; private set; }
        public int Departures { get; private set; }

        public Task OnNavigatedToAsync(NavigationContext context, CancellationToken cancellationToken = default)
        {
            Arrivals++;
            return Arrive(cancellationToken);
        }

        public Task OnNavigatedFromAsync(NavigationContext context, CancellationToken cancellationToken = default)
        {
            Departures++;
            return Task.CompletedTask;
        }

        public bool IsNavigationTarget(NavigationContext context) => Reusable;
    }

    private sealed class FirstPage : Page { }
    private sealed class SecondPage : Page { }
    private sealed class ThirdPage : Page { }
    private sealed class InnerPage : Page { }

    private sealed class PagesResolver : IDependencyResolver
    {
        private readonly Dictionary<Type, object> _pages = new();

        public PagesResolver(params object[] pages)
        {
            foreach (var page in pages)
            {
                _pages[page.GetType()] = page;
            }
        }

        public T Resolve<T>(string name = "") => (T)Resolve(typeof(T), name);
        public object Resolve(Type type, string name = "") => _pages[type];
    }

    private static Adamantium.Navigation.Region CreateRegion(params object[] pages) => new("pages", new PagesResolver(pages), null);

    [Test]
    public async Task ThePreviousPageStaysUntilTheNextOneHasArrived()
    {
        var first = new FirstPage();
        var second = new SecondPage();
        var arrival = new TaskCompletionSource();
        second.Arrive = _ => arrival.Task;
        var region = CreateRegion(first, second);
        await region.NavigateToAsync<FirstPage>();

        var navigation = region.NavigateToAsync<SecondPage>();

        Assert.That(region.CurrentViewModel, Is.SameAs(first));
        Assert.That(first.Departures, Is.Zero, "the page being left is told only once the next one is ready");

        arrival.SetResult();
        var result = await navigation;

        Assert.That(result.Success, Is.True);
        Assert.That(region.CurrentViewModel, Is.SameAs(second));
        Assert.That(first.Departures, Is.EqualTo(1));
    }

    [Test]
    public async Task AFailedArrivalChangesNothing()
    {
        var first = new FirstPage();
        var second = new SecondPage();
        var failure = new InvalidOperationException("no data");
        second.Arrive = _ => Task.FromException(failure);
        var region = CreateRegion(first, second);
        await region.NavigateToAsync<FirstPage>();

        var result = await region.NavigateToAsync<SecondPage>();

        Assert.That(result.Error, Is.SameAs(failure));
        Assert.That(region.CurrentViewModel, Is.SameAs(first));
        Assert.That(region.ActiveViewModels, Is.EqualTo(new object[] { first }));
        Assert.That(region.CanGoBack, Is.False);
        Assert.That(first.Departures, Is.Zero);
    }

    [Test]
    public async Task ANewerNavigationCancelsTheOneStillArriving()
    {
        var first = new FirstPage();
        var second = new SecondPage();
        var third = new ThirdPage();
        second.Arrive = token => Task.Delay(Timeout.Infinite, token);
        var region = CreateRegion(first, second, third);
        await region.NavigateToAsync<FirstPage>();

        var stale = region.NavigateToAsync<SecondPage>();
        var fresh = await region.NavigateToAsync<ThirdPage>();
        var staleResult = await stale;

        Assert.That(staleResult.Canceled, Is.True);
        Assert.That(fresh.Success, Is.True);
        Assert.That(region.CurrentViewModel, Is.SameAs(third));
        Assert.That(region.ActiveViewModels, Does.Not.Contain(second));
        Assert.That(region.Journal.BackStack, Has.Count.EqualTo(1));
        Assert.That(first.Departures, Is.EqualTo(1), "only the navigation that went through leaves the first page");
    }

    [Test]
    public async Task ARedirectFromAnArrivingPageTakesItsPlace()
    {
        var first = new FirstPage();
        var second = new SecondPage();
        var third = new ThirdPage();
        var region = CreateRegion(first, second, third);
        NavigationResult redirect = null;
        second.Arrive = async _ => redirect = await region.NavigateToAsync<ThirdPage>();
        await region.NavigateToAsync<FirstPage>();

        var navigation = region.NavigateToAsync<SecondPage>();
        var finished = await Task.WhenAny(navigation, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.That(finished, Is.SameAs(navigation), "a redirect must not wait for the navigation it was started from");
        Assert.That((await navigation).Canceled, Is.True);
        Assert.That(redirect.Success, Is.True);
        Assert.That(region.CurrentViewModel, Is.SameAs(third));
        Assert.That(region.ActiveViewModels, Does.Not.Contain(second));
    }

    [Test]
    public async Task GoingBackMovesTheJournalOnlyOnceThePageHasArrived()
    {
        var first = new FirstPage();
        var second = new SecondPage();
        var region = CreateRegion(first, second);
        await region.NavigateToAsync<FirstPage>();
        await region.NavigateToAsync<SecondPage>();
        first.Arrive = _ => Task.FromException(new InvalidOperationException("no data"));

        var result = await region.GoBackAsync();

        Assert.That(result.Error, Is.Not.Null);
        Assert.That(region.CurrentViewModel, Is.SameAs(second));
        Assert.That(region.CanGoBack, Is.True);
        Assert.That(region.CanGoForward, Is.False);
    }

    [Test]
    public async Task ANavigationOfAnotherRegionFromAHookCancelsNothing()
    {
        var outer = new FirstPage();
        var inner = new InnerPage();
        var regions = new RegionManager(new PagesResolver(outer, inner));
        NavigationResult innerResult = null;
        outer.Arrive = async token => innerResult = await regions.NavigateToAsync<InnerPage>("Inner", cancellationToken: token);

        var navigation = regions.NavigateToAsync<FirstPage>("Outer");
        var finished = await Task.WhenAny(navigation, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.That(finished, Is.SameAs(navigation));
        Assert.That((await navigation).Success, Is.True);
        Assert.That(innerResult.Success, Is.True);
        Assert.That(regions["Outer"].CurrentViewModel, Is.SameAs(outer));
        Assert.That(regions["Inner"].CurrentViewModel, Is.SameAs(inner));
    }

    [Test]
    public async Task ARedirectBackThroughAnotherRegionTakesThePlaceOfTheOneInProgress()
    {
        var first = new FirstPage();
        var second = new SecondPage();
        var third = new ThirdPage();
        var inner = new InnerPage();
        var regions = new RegionManager(new PagesResolver(first, second, third, inner));
        await regions.NavigateToAsync<FirstPage>("Outer");
        second.Arrive = token => regions.NavigateToAsync<InnerPage>("Inner", cancellationToken: token);
        inner.Arrive = _ => regions.NavigateToAsync<ThirdPage>("Outer");

        var navigation = regions.NavigateToAsync<SecondPage>("Outer");
        var finished = await Task.WhenAny(navigation, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.That(finished, Is.SameAs(navigation), "the outer region waited for itself through the inner one");
        Assert.That((await navigation).Canceled, Is.True);
        Assert.That(regions["Outer"].CurrentViewModel, Is.SameAs(third));
    }

    [Test]
    public async Task ANamedRegionIsNavigatedInOneCallBeforeAnyControlDeclaresIt()
    {
        var first = new FirstPage();
        var regions = new RegionManager(new PagesResolver(first));

        var result = await regions.NavigateToAsync<FirstPage>("Main");

        Assert.That(result.Success, Is.True);
        Assert.That(regions["Main"].CurrentViewModel, Is.SameAs(first), "the control declaring 'Main' later finds it there");
    }

    [Test]
    public async Task AReusedPageArrivesAgainWithoutBeingLeft()
    {
        var first = new FirstPage { Reusable = true };
        var region = CreateRegion(first);
        await region.NavigateToAsync<FirstPage>();

        var result = await region.NavigateToAsync<FirstPage>();

        Assert.That(result.Success, Is.True);
        Assert.That(first.Arrivals, Is.EqualTo(2));
        Assert.That(first.Departures, Is.Zero);
    }
}
