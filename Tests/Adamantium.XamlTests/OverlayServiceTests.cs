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
        private double _width = 296;

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

    private static OverlayWindow Shown(object viewModel)
    {
        var resolver = new Resolver(viewModel);
        var app = new FakeApp(resolver) { ResourceManager = new ResourceManager() };
        var host = new Window { Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600, Content = new Grid() };
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
            Assert.That(window.Width, Is.EqualTo(296), "collapsed, the width is the one given");
            Assert.That(AutomationProperties.GetAutomationId(window), Is.EqualTo("TheCard"));
        });
        window.Close();
    }

    [Test]
    public void FoldedAndResized_ItTellsItsViewModel()
    {
        var card = new Card();
        var window = Shown(card);

        window.IsCollapsed = false;
        window.Width = 400;
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(card.IsCollapsed, Is.False);
            Assert.That(card.Width, Is.EqualTo(400));
        });
        window.Close();
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
