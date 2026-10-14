using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Navigation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Navigation;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

// A region lives as long as the application's region manager; the control that shows it lives as long as its page. A
// discarded host must not stay among the region's listeners - that is what kept every visited page alive.
[TestFixture]
public class RegionAdapterReleaseTests
{
    private sealed class CountedRegion : IRegion
    {
        private PropertyChangedEventHandler _propertyChanged;
        private EventHandler _activeViewsChanged;

        public int Listeners;

        public event PropertyChangedEventHandler PropertyChanged
        {
            add
            {
                Listeners++;
                _propertyChanged += value;
            }
            remove
            {
                Listeners--;
                _propertyChanged -= value;
            }
        }

        public event EventHandler ActiveViewsChanged
        {
            add
            {
                Listeners++;
                _activeViewsChanged += value;
            }
            remove
            {
                Listeners--;
                _activeViewsChanged -= value;
            }
        }

        public event EventHandler<RegionNavigationEventArgs> Navigated
        {
            add { }
            remove { }
        }

        public string Name => "Region";
        public object CurrentViewModel => null;
        public string CurrentViewKey => null;
        public IReadOnlyList<object> ActiveViewModels => [];
        public bool SingleActiveView { get; set; }
        public INavigationJournal Journal => null;
        public bool CanGoBack => false;
        public bool CanGoForward => false;

        public Task<NavigationResult> NavigateToAsync(Type viewModelType, NavigationParameters parameters = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<NavigationResult> NavigateToAsync<TViewModel>(NavigationParameters parameters = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<NavigationResult> NavigateToViewAsync(Type viewModelType, string viewKey,
            NavigationParameters parameters = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<NavigationResult> NavigateToViewAsync<TViewModel>(string viewKey, NavigationParameters parameters = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<NavigationResult> NavigateToInstanceAsync(object viewModel, string viewKey,
            NavigationParameters parameters = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<NavigationResult> GoBackAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<NavigationResult> GoForwardAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Add(object viewModel) { }
        public void Remove(object viewModel) { }
        public void Activate(object viewModel) { }
        public void Deactivate(object viewModel) { }
    }

    private static void Discard(IUIComponent host)
    {
        DiscardedVisuals.Publish(host);
        DiscardedVisuals.Drain(int.MaxValue);
    }

    [Test]
    public void ADiscardedContentHost_LeavesTheRegion()
    {
        var region = new CountedRegion();
        var host = new ContentControl();
        new ContentControlRegionAdapter(null).Attach(region, host);
        Assume.That(region.Listeners, Is.EqualTo(1), "the host follows the region");

        Discard(host);

        Assert.That(region.Listeners, Is.Zero);
    }

    [Test]
    public void ADiscardedSelectorHost_LeavesTheRegion()
    {
        var region = new CountedRegion();
        var host = new TabControl();
        new SelectorRegionAdapter(null).Attach(region, host);
        Assume.That(region.Listeners, Is.EqualTo(2), "the host follows the region");

        Discard(host);

        Assert.That(region.Listeners, Is.Zero);
    }

    // ...but leaving the tree is not going away: a selector on a kept page leaves and comes back, and one that let go of
    // its region as it left no longer followed it after the return.
    [Test]
    public void ASelectorHostThatLeavesTheTreeAndReturns_StillFollowsTheRegion()
    {
        var region = new CountedRegion();
        var host = new TabControl();
        var panel = new Adamantium.UI.Controls.Panels.StackPanel();
        panel.Children.Add(host);
        var window = new Window { Width = 200, Height = 100, Content = panel };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        new SelectorRegionAdapter(null).Attach(region, host);

        panel.Children.Remove(host);
        panel.Children.Add(host);

        Assert.That(region.Listeners, Is.EqualTo(2));
    }

    [Test]
    public void ADiscardedItemsHost_LeavesTheRegion()
    {
        var region = new CountedRegion();
        var host = new ItemsControl();
        new ItemsControlRegionAdapter(null).Attach(region, host);
        Assume.That(region.Listeners, Is.EqualTo(1), "the host follows the region");

        Discard(host);

        Assert.That(region.Listeners, Is.Zero);
    }
}
