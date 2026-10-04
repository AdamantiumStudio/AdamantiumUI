using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.Navigation;
using NUnit.Framework;

namespace Adamantium.UITests;

[TestFixture]
public class WindowNavigationTests
{
    private sealed class Backend : IWindowNavigationBackend
    {
        public List<object> Opened { get; } = [];

        public Task<object> OpenWindowAsync(object contentViewModel, NavigationParameters parameters, string windowShell, CancellationToken cancellationToken)
        {
            Opened.Add(contentViewModel);
            return Task.FromResult<object>(null);
        }

        public object TryActivateExisting(Type contentViewModelType) => null;
        public void CloseWindow(object contentViewModel) { }
    }

    private sealed class Content : INavigationAware
    {
        public Func<Task> Arrive { get; set; } = () => Task.CompletedTask;
        public NavigationContext Context { get; private set; }

        public Task OnNavigatedToAsync(NavigationContext context, CancellationToken cancellationToken = default)
        {
            Context = context;
            return Arrive();
        }

        public Task OnNavigatedFromAsync(NavigationContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool IsNavigationTarget(NavigationContext context) => false;
    }

    private sealed class OneResolver : IDependencyResolver
    {
        private readonly object _instance;
        public OneResolver(object instance) => _instance = instance;
        public T Resolve<T>(string name = "") => (T)_instance;
        public object Resolve(Type type, string name = "") => _instance;
    }

    [Test]
    public async Task AWindowOpensOnceItsViewModelHasArrived()
    {
        var content = new Content();
        var arrival = new TaskCompletionSource();
        content.Arrive = () => arrival.Task;
        var backend = new Backend();
        var resolver = new OneResolver(content);
        var navigation = new NavigationService(new RegionManager(resolver), resolver, backend);

        var opening = navigation.OpenWindowAsync<Content>(new NavigationParameters().Add("id", 7));

        Assert.That(backend.Opened, Is.Empty);

        arrival.SetResult();
        var result = await opening;

        Assert.That(result.Success, Is.True);
        Assert.That(backend.Opened, Is.EqualTo(new object[] { content }));
        Assert.That(content.Context.Region, Is.Null);
        Assert.That(content.Context.TargetViewModel, Is.SameAs(content));
        Assert.That(content.Context.Parameters.GetValue<int>("id"), Is.EqualTo(7));
    }

    [Test]
    public async Task AWindowWhoseViewModelFailsToArriveDoesNotOpen()
    {
        var failure = new InvalidOperationException("no data");
        var content = new Content { Arrive = () => Task.FromException(failure) };
        var backend = new Backend();
        var resolver = new OneResolver(content);
        var navigation = new NavigationService(new RegionManager(resolver), resolver, backend);

        var result = await navigation.OpenWindowAsync<Content>();

        Assert.That(result.Error, Is.SameAs(failure));
        Assert.That(backend.Opened, Is.Empty);
    }
}
