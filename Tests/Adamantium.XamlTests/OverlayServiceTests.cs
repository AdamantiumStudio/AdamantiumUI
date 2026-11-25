using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.Navigation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Navigation;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>What an overlay's view model says of its window: collapsed, how wide, its automation id - and that it goes
/// with the window.</summary>
[TestFixture]
public class OverlayServiceTests
{
    private sealed class Card : IOverlayAware, INotifyPropertyChanged, IDisposable
    {
        private bool _isCollapsed = true;
        private double _width = 320;

        public string Title => "Card";
        public bool CanCollapse => true;
        public string AutomationId => "TheCard";
        public bool Disposed { get; private set; }

        public bool IsCollapsed
        {
            get => _isCollapsed;
            set
            {
                _isCollapsed = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCollapsed)));
            }
        }

        public double Width
        {
            get => _width;
            set
            {
                _width = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Width)));
            }
        }

        public event Action<object> RequestClose { add { } remove { } }
        public event PropertyChangedEventHandler PropertyChanged;

        public Task OnOverlayOpenedAsync(NavigationParameters parameters, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void Dispose() => Disposed = true;
    }

    // A view model that says nothing of them: the window keeps its own.
    private sealed class Plain : IOverlayAware
    {
        public string Title => "Plain";
        public event Action<object> RequestClose { add { } remove { } }

        public Task OnOverlayOpenedAsync(NavigationParameters parameters, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class Resolver(object viewModel) : IDependencyResolver
    {
        public T Resolve<T>(string name = "") => (T)viewModel;
        public object Resolve(Type type, string name = "") => viewModel;
    }

    private Window _host;

    private OverlayWindow Shown(object viewModel)
    {
        var resolver = new Resolver(viewModel);
        var app = new FakeApp(resolver) { ResourceManager = new ResourceManager() };
        var host = new Window { Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600, Content = new Grid() };
        _host = host;
        app.MainWindow = host;
        var views = new ViewLocator();
        views.RegisterFactory(viewModel.GetType(), () => new Grid());

        _ = new OverlayService(app, views, resolver).ShowOverlayAsync(viewModel.GetType());
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        return OverlayWindowManager.GetFor(host).Windows.Single();
    }

    [Test]
    public void TheWindowOpensAsItsViewModelSays()
    {
        var window = Shown(new Card());

        Assert.Multiple(() =>
        {
            Assert.That(window.IsCollapsed, Is.True);
            Assert.That(window.Width, Is.EqualTo(320), "collapsed, the width is the one given");
            Assert.That(AutomationProperties.GetAutomationId(window), Is.EqualTo("TheCard"));
        });
        window.Close();
    }

    // Folded by its button it tells its view model, and still follows it after: the press left no value of its own.
    [Test]
    public void FoldedByItsButton_ItTellsItsViewModel_AndStillFollowsIt()
    {
        var card = new Card();
        var window = Shown(card);

        window.SetCurrentValue(OverlayWindow.IsCollapsedProperty, false);   // as the button does
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        var told = card.IsCollapsed;
        card.IsCollapsed = true;
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(told, Is.False);
            Assert.That(window.IsCollapsed, Is.True);
        });
        window.Close();
    }

    [Test]
    public void SizedToItsContent_CollapsingDoesNotGiveItsViewModelAWidth()
    {
        var card = new Card { Width = double.NaN, IsCollapsed = false };
        var window = Shown(card);

        window.IsCollapsed = true;
        window.IsCollapsed = false;
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(card.Width, Is.NaN);
            Assert.That(window.Width, Is.NaN);
        });
        window.Close();
    }

    [Test]
    public void ClosedWithItsHost_ItsViewModelIsDisposed()
    {
        var card = new Card();
        Shown(card);

        _host.Close();

        Assert.That(card.Disposed, Is.True);
    }

    // Closed before it appeared, it goes too.
    [Test]
    public void ClosedWhileOpening_ItsViewModelIsDisposed()
    {
        var early = new Early();
        var resolver = new Resolver(early);
        var app = new FakeApp(resolver)
        {
            ResourceManager = new ResourceManager(),
            MainWindow = new Window { Width = 800, Height = 600, Content = new Grid() }
        };

        var shown = new OverlayService(app, new ViewLocator(), resolver).ShowOverlayAsync(typeof(Early));

        Assert.Multiple(() =>
        {
            Assert.That(shown.IsCompleted, Is.True);
            Assert.That(early.Disposed, Is.True);
        });
    }

    private sealed class Early : IOverlayAware, IDisposable
    {
        public string Title => "Early";
        public bool Disposed { get; private set; }
        public event Action<object> RequestClose;

        public Task OnOverlayOpenedAsync(NavigationParameters parameters, CancellationToken cancellationToken = default)
        {
            RequestClose?.Invoke(null);
            return Task.CompletedTask;
        }

        public void Dispose() => Disposed = true;
    }

    [Test]
    public void Closed_ItsViewModelIsDisposed()
    {
        var card = new Card();
        var window = Shown(card);
        var whileOpen = card.Disposed;

        window.Close();

        Assert.Multiple(() =>
        {
            Assert.That(whileOpen, Is.False);
            Assert.That(card.Disposed, Is.True);
        });
    }

    [Test]
    public void AViewModelThatSaysNothing_LeavesTheWindowItsOwn()
    {
        var window = Shown(new Plain());

        Assert.Multiple(() =>
        {
            Assert.That(window.IsCollapsed, Is.False);
            Assert.That(window.Width, Is.NaN);
            Assert.That(AutomationProperties.GetAutomationId(window), Is.Null.Or.Empty);
        });
        window.Close();
    }
}
